using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable]
    public sealed class Career
    {
        public int version = 1, credits = 600, xp, day = 1, totalEarnedXp, cpu, bandwidth, memory, jobs;
        public List<string> completed = new List<string>();
        public List<string> unlocked = new List<string> { "base" };
        public List<string> equipped = new List<string>();
        public List<string> news = new List<string> { "EXCHANGE ONLINE // A new operator has entered the network. Your first contract is waiting." };
        public List<OperationReport> reports = new List<OperationReport>();
        public Operation operation;
    }

    [Serializable]
    public sealed class QueuedTool
    {
        public string target;
        public ToolKind kind;
        public QueuedTool(ToolKind tool, string id) { target = id; kind = tool; }
    }

    [Serializable]
    public sealed class ToolTask
    {
        public string target;
        public ToolKind kind;
        public float duration, elapsed, trace;
    }

    [Serializable]
    public sealed class Operation
    {
        public string contractId;
        public int seed;
        public List<NetNode> nodes;
        public List<NetLink> links;
        public List<string> log = new List<string>();
        public List<QueuedTool> queue = new List<QueuedTool>();
        public ToolTask task;
        public float trace, elapsed, decoyRemaining;
        public bool paused, decoyUsed, backdoorUsed, firstRelayUsed, cascadeApplied;
        public string lastControlled = "home";
    }

    [Serializable]
    public sealed class OperationReport
    {
        public string title, company, verdict, narrative;
        public bool success;
        public float trace;
        public int credits, xp, day, bonusFiles;
        public List<string> log = new List<string>();
    }

    public sealed class ToolPreview
    {
        public bool allowed;
        public string reason;
        public float duration, trace;
    }

    // The simulation owns mission state. UI and saves are projections of these facts.
    public sealed class GameSimulation
    {
        public Career Career { get; private set; }
        public Operation Op => Career.operation;
        public Contract Job { get; private set; }
        public GraphDocument KnownGraph { get; private set; }
        public GraphScene Scene { get; private set; }
        public OperationReport LastReport { get; private set; }
        public int Revision { get; private set; }
        public event Action Changed;
        public bool Active => Op != null;
        public int Slots => 2 + Career.memory;
        public bool Has(string id) => Career.unlocked.Contains(id);
        public bool Equipped(string id) => Has(id) && Career.equipped.Contains(id);
        public NetNode Node(string id) => Op?.nodes.FirstOrDefault(n => n.id == id);
        public IEnumerable<NetNode> Neighbors(NetNode node) => Op.links.Where(l => l.a == node.id || l.b == node.id).Select(l => Node(l.a == node.id ? l.b : l.a));
        public bool Reachable(NetNode node) => node != null && node.known && (node.controlled || Neighbors(node).Any(n => n.controlled));
        public int ObjectiveCount => Active ? Op.nodes.Count(n => n.objective && ObjectiveDone(n)) : 0;
        public int ObjectiveTotal => Active ? Op.nodes.Count(n => n.objective) : 0;
        public bool ObjectiveComplete => Active && ObjectiveCount == ObjectiveTotal;
        public bool ObjectiveDone(NetNode node) => node.kind == NodeKind.SecurityHub && Job.kind == JobKind.Disrupt ? node.controlled : node.collected;
        public float Progress => Op?.task == null ? 0 : Mathf.Clamp01(Op.task.elapsed / Op.task.duration);

        public GameSimulation(Career career = null)
        {
            Career = career ?? new Career();
            Normalize();
            if (Active)
            {
                Job = ResolveContract(Op.contractId, Op.seed);
                if (Job == null || Op.nodes == null || Op.links == null) Career.operation = null;
                else { Op.paused = true; RefreshGraph(); }
            }
        }

        private void Normalize()
        {
            Career.completed ??= new List<string>(); Career.unlocked ??= new List<string>();
            Career.equipped ??= new List<string>(); Career.news ??= new List<string>(); Career.reports ??= new List<OperationReport>();
            Career.completed = Career.completed.Where(id => GameContent.Story.Any(c => c.id == id)).Distinct().ToList();
            Career.unlocked = Career.unlocked.Where(id => GameContent.FindTech(id) != null).Distinct().ToList();
            if (!Has("base")) Career.unlocked.Add("base");
            Career.cpu = Mathf.Clamp(Career.cpu, 0, 3); Career.bandwidth = Mathf.Clamp(Career.bandwidth, 0, 3); Career.memory = Mathf.Clamp(Career.memory, 0, 2);
            Career.credits = Mathf.Max(0, Career.credits); Career.xp = Mathf.Max(0, Career.xp); Career.day = Mathf.Max(1, Career.day);
            Career.equipped = Career.equipped.Where(id => Has(id) && GameContent.FindTech(id).special).Distinct().Take(Slots).ToList();
            if (Active) { Op.queue ??= new List<QueuedTool>(); Op.log ??= new List<string>(); }
        }

        private Contract ResolveContract(string id, int seed)
        {
            var job = GameContent.Contracts(Career).FirstOrDefault(c => c.id == id);
            if (job != null && job.recurring) job.seed = seed;
            return job;
        }

        public bool Start(Contract job)
        {
            if (Active || job == null || Career.completed.Count < job.requiredWins || (!job.recurring && Career.completed.Contains(job.id))) return false;
            Job = job;
            Career.operation = new Operation { contractId=job.id, seed=job.seed, nodes=GameContent.Network(job, Career), links=GameContent.Links(job.seed) };
            Reveal(Node("home"), 1);
            if (Has("blueprint")) foreach (var node in Op.nodes.Where(n => n.objective).ToArray()) Reveal(node, 1);
            Log("TUNNEL OPEN // " + Job.company + ". Gateway secured.");
            Log("CLIENT // " + job.objective);
            if (Career.completed.Count == 0) Log("GUIDE // Select Public relay, then HACK. Control connected nodes to reach the gold objective.");
            RefreshGraph(); Touch(); return true;
        }

        public void SetPaused(bool paused) { if (!Active) return; Op.paused = paused; Touch(); }

        public int EffectiveSecurity(NetNode node)
        {
            int value = node.security;
            if (node.kind == NodeKind.Firewall || node.kind == NodeKind.Database)
                if (Neighbors(node).Any(n => n.kind == NodeKind.SecurityHub && !n.controlled && n.suppressed <= 0)) value += 2;
            return Mathf.Max(1, value);
        }

        public int Scanners => Active ? Op.nodes.Count(n => n.kind == NodeKind.Scanner && !n.controlled && n.suppressed <= 0 && Neighbors(n).Any(a => a.controlled)) : 0;

        public ToolPreview Preview(ToolKind kind, string id)
        {
            var p = new ToolPreview();
            var node = Node(id);
            if (!Active || node == null) { p.reason = "Connect to a contract first."; return p; }
            if (!node.known) { p.reason = "Scan to discover this system."; return p; }
            if (!Reachable(node) && kind != ToolKind.Backdoor) { p.reason = "Control a connected neighbor first."; return p; }
            int security = EffectiveSecurity(node);
            float speed = 1 + Career.cpu * .2f;
            switch (kind)
            {
                case ToolKind.Scan:
                    if (node.scanned) { p.reason="Already scanned. Its surrounding connections are known."; return p; }
                    p.duration = Has("signal") ? 1.5f : 3; p.trace = Has("signal") ? 0 : 2; break;
                case ToolKind.Hack:
                    if (node.controlled) { p.reason="You already control this system."; return p; }
                    p.duration = (3 + security * 1.7f) / speed * (Has("efficient") ? .75f : 1);
                    p.trace = (3 + security * 1.7f) * (Has("quiet") ? .7f : 1) * (Has("weakpoint") && node.scanned ? .6f : 1);
                    break;
                case ToolKind.Download:
                    if (!node.controlled) { p.reason="Hack this system before recovering its files."; return p; }
                    if (node.collected) { p.reason="Files already secured."; return p; }
                    if (!node.objective && !node.bonus) { p.reason="No contract files or optional archives on this node."; return p; }
                    if (node.kind == NodeKind.SecurityHub) { p.reason="Control the hub to satisfy this objective."; return p; }
                    if (Job.kind == JobKind.Disrupt && !Node("n14").controlled) { p.reason="Take control of Security hub before erasing the index."; return p; }
                    p.duration = (node.objective ? 9 : 7) / (1 + Career.bandwidth * .3f) * (Has("selective") && node.objective ? .55f : 1);
                    p.trace = (node.objective ? 8 : 6) * (Has("selective") && node.objective ? .7f : 1); break;
                case ToolKind.Disrupt:
                    string tech = node.kind == NodeKind.SecurityHub ? "blackout" : "disrupt";
                    if (node.kind != NodeKind.Scanner && node.kind != NodeKind.SecurityHub) { p.reason="Select a scanner or security hub."; return p; }
                    if (!Equipped(tech)) { p.reason="Research and equip " + GameContent.FindTech(tech).title + "."; return p; }
                    if (node.controlled || node.suppressed > 0) { p.reason="This defense is already offline."; return p; }
                    p.duration=2.5f; p.trace=2; break;
                case ToolKind.Bridge:
                    if (!Equipped("bridge")) { p.reason="Research and equip Firewall bridge."; return p; }
                    if (node.controlled || node.kind != NodeKind.Firewall || Neighbors(node).Count(n => n.controlled) < 2)
                    { p.reason="Requires an uncontrolled firewall with two controlled neighbors."; return p; }
                    p.duration=2; p.trace=0; break;
                case ToolKind.Backdoor:
                    if (!Equipped("backdoor") || Op.backdoorUsed) { p.reason="Requires an equipped, unused Backdoor."; return p; }
                    if (node.controlled || (node.kind != NodeKind.Workstation && node.kind != NodeKind.Relay)) { p.reason="Choose an uncontrolled, discovered workstation or relay."; return p; }
                    p.duration=1; p.trace=0; break;
            }
            bool free = kind == ToolKind.Bridge || kind == ToolKind.Backdoor || (kind == ToolKind.Hack && node.kind == NodeKind.Relay && Has("ghost") && !Op.firstRelayUsed);
            p.trace = free ? 0 : (p.trace + p.duration * Scanners * .18f) * (Has("buffer") ? .85f : 1);
            p.allowed=true; p.reason=Op.task != null ? "Queue after the current task." : "Ready to execute."; return p;
        }

        public bool Execute(ToolKind kind, string id)
        {
            if (!Active) return false;
            if (Op.task != null)
            {
                if (Op.queue.Count >= (Has("chain") ? 3 : 1)) return false;
                var node = Node(id);
                // Future neighbors may be queued; execution rechecks every prerequisite.
                if (node == null || !node.known || (kind == ToolKind.Hack && node.controlled)) return false;
                if (Op.queue.Any(q => q.kind == kind && q.target == id) || (Op.task.target == id && Op.task.kind == kind)) return false;
                if (kind == ToolKind.Hack && !Reachable(node) && (!Has("chain") || !Op.links.Any(l => (l.a == id && l.b == Op.task.target) || (l.b == id && l.a == Op.task.target)))) return false;
                Op.queue.Add(new QueuedTool(kind, id)); Log("QUEUED // " + kind + " : " + node.label); Touch(); return true;
            }
            var p = Preview(kind, id);
            if (!p.allowed) return false;
            Op.task = new ToolTask { kind=kind, target=id, duration=p.duration, trace=p.trace };
            Log("RUN // " + kind.ToString().ToUpperInvariant() + " : " + Node(id).label);
            Touch(); return true;
        }

        public void ClearQueue() { if (!Active) return; Op.queue.Clear(); Touch(); }

        public void Tick(float delta)
        {
            if (!Active || Op.paused || delta <= 0) return;
            delta = Mathf.Min(delta, .5f);
            Op.elapsed += delta;
            Op.decoyRemaining = Mathf.Max(0, Op.decoyRemaining - delta);
            bool suppressionExpired = false;
            foreach (var node in Op.nodes)
                if (node.suppressed > 0)
                {
                    float old = node.suppressed; node.suppressed = Mathf.Max(0, old - delta);
                    if (node.suppressed == 0) { suppressionExpired = true; Log("DEFENSE ONLINE // " + node.label); }
                }
            if (suppressionExpired) Touch();
            if (Op.task == null) return; // Planning and reading never add idle trace.
            var task = Op.task;
            float step = Mathf.Min(delta, task.duration - task.elapsed);
            if (Op.decoyRemaining <= 0) Op.trace += step / task.duration * task.trace;
            task.elapsed += step;
            if (Op.trace >= 100) { Op.trace=100; Disconnect(true); return; }
            if (task.elapsed + .0001f < task.duration) return;
            CompleteTask(task);
            Op.task = null;
            RefreshGraph(); Touch();
            while (Active && Op.task == null && Op.queue.Count > 0)
            {
                var next = Op.queue[0]; Op.queue.RemoveAt(0);
                if (!Execute(next.kind, next.target)) Log("SKIPPED // Queued action no longer meets its requirements.");
            }
        }

        private void CompleteTask(ToolTask task)
        {
            var node = Node(task.target);
            switch (task.kind)
            {
                case ToolKind.Scan:
                    node.scanned = true; Reveal(node, Has("wide") ? 3 : 2);
                    Log("SCAN COMPLETE // New routes mapped around " + node.label + "."); break;
                case ToolKind.Hack:
                case ToolKind.Bridge:
                case ToolKind.Backdoor:
                    node.controlled=true; Op.lastControlled=node.id;
                    if (node.kind == NodeKind.Relay) Op.firstRelayUsed=true;
                    if (task.kind == ToolKind.Backdoor) Op.backdoorUsed=true;
                    Reveal(node, Has("passive") && node.kind == NodeKind.Relay ? 3 : 1);
                    Log("CONTROL ESTABLISHED // " + node.label + ".");
                    if (node.kind == NodeKind.SecurityHub) ApplyCascade();
                    break;
                case ToolKind.Download:
                    node.collected=true;
                    Log(node.objective ? "OBJECTIVE SECURED // " + node.label + "." : "OPTIONAL ARCHIVE // +120 cr / +15 XP on successful extraction.");
                    break;
                case ToolKind.Disrupt:
                    node.suppressed=60;
                    Log("DEFENSE SUPPRESSED // " + node.label + " offline for 60 seconds.");
                    if (node.kind == NodeKind.SecurityHub) ApplyCascade(); break;
            }
            if (ObjectiveComplete) Log("ALL OBJECTIVES SECURED // Disconnect to deliver and collect your reward. Optional archives remain available.");
        }

        private void ApplyCascade()
        {
            if (!Has("cascade") || Op.cascadeApplied) return;
            foreach (var wall in Op.nodes.Where(n => n.kind == NodeKind.Firewall)) wall.security=Mathf.Max(1, wall.security-2);
            Op.cascadeApplied=true; Log("SYSTEM CASCADE // Firewall security reduced across the network.");
        }

        public bool Decoy()
        {
            if (!Active || !Equipped("decoy") || Op.decoyUsed) return false;
            Op.trace=Mathf.Max(0, Op.trace-15); Op.decoyRemaining=20; Op.decoyUsed=true;
            Log("DECOY ACTIVE // -15 trace. Trace growth suspended for 20 seconds."); Touch(); return true;
        }

        private void Reveal(NetNode source, int radius)
        {
            var visited = new HashSet<string> { source.id };
            var frontier = new List<NetNode> { source }; source.known=true;
            for (int i=0; i<radius; i++)
            {
                var next = new List<NetNode>();
                foreach (var node in frontier)
                    foreach (var neighbor in Neighbors(node))
                        if (visited.Add(neighbor.id)) { neighbor.known=true; next.Add(neighbor); }
                frontier=next;
            }
        }

        public void RefreshGraph()
        {
            if (!Active) return;
            // Only discovered facts enter Graph Explorer. Hidden routes cannot leak through path queries.
            KnownGraph=new GraphDocument("operation-" + Op.contractId, Job.title);
            foreach (var n in Op.nodes.Where(n => n.known))
                KnownGraph.UpsertNode(new GraphNodeRecord(n.id, n.kind.ToString(), n.label,
                    new Dictionary<string, GraphValue> { ["controlled"]=GraphValue.From(n.controlled), ["objective"]=GraphValue.From(n.objective) }));
            foreach (var link in Op.links.Where(l => Node(l.a).known && Node(l.b).known))
            {
                KnownGraph.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));
                KnownGraph.UpsertEdge(new GraphEdgeRecord(link.b + ">" + link.a, link.b, link.a, "network"));
            }
            Scene=new GraphScene(KnownGraph, new GraphSchema());
            foreach (var n in Op.nodes.Where(n => n.known))
            { Scene.Nodes[n.id].Position=new Vector3(n.x, n.y, 0); Scene.Nodes[n.id].Pinned=true; }
        }

        public GraphPathResult Route(string id, bool controlledOnly = false)
        {
            if (!Active || Scene == null) return null;
            return GraphPathfinder.Find(Scene, "home", id, controlledOnly ? e => Node(e.From).controlled && (Node(e.To).controlled || e.To == id) : (Func<IGraphEdge,bool>)null);
        }

        public OperationReport Disconnect(bool caught = false)
        {
            if (!Active) return null;
            bool success = !caught && ObjectiveComplete;
            var report = new OperationReport { title=Job.title, company=Job.company, trace=Op.trace, success=success, day=Career.day,
                verdict=success ? "CONTRACT COMPLETE" : caught ? "CONNECTION ISOLATED" : "CONTRACT ABORTED",
                narrative=success ? Job.aftermath : caught ? "The defender isolated your session. Equipment and research are safe. The contract can be retried with a different route." : "You disconnected before securing every required file. Your equipment and research are safe; the contract remains available.",
                log=new List<string>(Op.log), bonusFiles=Op.nodes.Count(n => n.bonus && n.collected) };
            if (success)
            {
                bool quiet = Op.trace < Job.quietLimit;
                report.credits=Job.credits+report.bonusFiles*120+(quiet ? 150 : 0);
                report.xp=Job.xp+Mathf.Min(30, report.bonusFiles*15)+(quiet ? 10 : 0);
                Career.credits+=report.credits; Career.xp+=report.xp; Career.totalEarnedXp+=report.xp; Career.jobs++;
                if (!Job.recurring && !Career.completed.Contains(Job.id)) Career.completed.Add(Job.id);
                Career.news.Insert(0, "DAY " + Career.day.ToString("00") + " // " + Job.aftermath);
                Career.day++;
            }
            Career.reports.Insert(0, report); if (Career.reports.Count > 20) Career.reports.RemoveAt(20);
            LastReport=report; Career.operation=null; KnownGraph=null; Scene=null; Touch(); return report;
        }

        public bool BuyTech(string id)
        {
            var tech=GameContent.FindTech(id);
            if (Active || tech == null || Has(id) || Career.xp < tech.cost || !tech.parents.All(Has)) return false;
            Career.xp-=tech.cost; Career.unlocked.Add(id);
            if (tech.special && Career.equipped.Count < Slots) Career.equipped.Add(id);
            Touch(); return true;
        }

        public bool ToggleEquip(string id)
        {
            if (Active || !Has(id) || GameContent.FindTech(id)?.special != true) return false;
            if (Career.equipped.Contains(id)) Career.equipped.Remove(id);
            else if (Career.equipped.Count < Slots) Career.equipped.Add(id);
            else return false;
            Touch(); return true;
        }

        public bool RefundResearch()
        {
            if (Active) return false;
            Career.xp+=Career.unlocked.Sum(id => GameContent.FindTech(id)?.cost ?? 0);
            Career.unlocked=new List<string> { "base" }; Career.equipped.Clear(); Touch(); return true;
        }

        public int UpgradePrice(int type)
        {
            int level = type == 0 ? Career.cpu : type == 1 ? Career.bandwidth : Career.memory;
            return type == 2 ? (level == 0 ? 600 : 1400) : new[] { 400, 900, 1600, 0 }[Mathf.Clamp(level, 0, 3)];
        }

        public bool Upgrade(int type)
        {
            if (Active || type < 0 || type > 2) return false;
            int level=type == 0 ? Career.cpu : type == 1 ? Career.bandwidth : Career.memory;
            if (level >= (type == 2 ? 2 : 3)) return false;
            int price=UpgradePrice(type); if (Career.credits < price) return false;
            Career.credits-=price;
            if (type == 0) Career.cpu++; else if (type == 1) Career.bandwidth++; else Career.memory++;
            Touch(); return true;
        }

        public void Log(string message)
        {
            if (!Active) return;
            int min=(int)Op.elapsed/60, sec=(int)Op.elapsed%60;
            Op.log.Add(min.ToString("00") + ":" + sec.ToString("00") + "  " + message);
            if (Op.log.Count > 100) Op.log.RemoveAt(0);
        }

        private void Touch() { Revision++; Changed?.Invoke(); }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class CareerStorage
    {
        public static string SavePath => Path.Combine(Application.persistentDataPath, "career-v1.json");
        public static string Warning { get; private set; }

        public static Career Load(string path = null)
        {
            path ??= SavePath; Warning=null;
            if (!File.Exists(path)) return new Career();
            try
            {
                var career=JsonUtility.FromJson<Career>(File.ReadAllText(path));
                if (career == null || career.version != 1) throw new InvalidDataException("Unsupported career save.");
                return career;
            }
            catch (Exception ex)
            {
                Warning="The career save could not be read. A backup was preserved. " + ex.Message;
                try { File.Copy(path, path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); } catch { }
                if (File.Exists(path+".bak"))
                    try { return JsonUtility.FromJson<Career>(File.ReadAllText(path+".bak")) ?? new Career(); } catch { }
                return new Career();
            }
        }

        public static bool Reset(string path = null)
        {
            // Replace twice so recovery cannot bring back the previous career.
            var career=new Career();
            return Save(career,path)&&Save(career,path);
        }

        public static bool Save(Career career, string path = null)
        {
            path ??= SavePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path+".tmp", JsonUtility.ToJson(career, true));
                if (File.Exists(path)) File.Replace(path+".tmp", path, path+".bak");
                else File.Move(path+".tmp", path);
                Warning=null; return true;
            }
            catch (Exception ex) { Warning="Autosave could not write: " + ex.Message; Debug.LogWarning(Warning); return false; }
        }
    }
}
