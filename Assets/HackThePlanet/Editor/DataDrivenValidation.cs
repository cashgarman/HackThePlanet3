using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static HackThePlanet.Editor.DefaultGameplayAssets;

namespace HackThePlanet.Editor
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class DataDrivenValidation
    {
        private static readonly List<string> results=new List<string>();
        private static void Check(bool pass,string message){if(!pass)throw new InvalidOperationException("FAILED / "+message);results.Add("PASS / "+message);}
        private static void Near(float actual,float expected,string message)=>Check(Mathf.Abs(actual-expected)<.0002f,message+" / "+actual.ToString("0.####"));
        private static GameDefinition Root=>Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");
        private static void Finish(GameSimulation sim){GameValidation.FinishTask(sim);Check(sim.Active,"action remains within trace budget");}
        private static GameSimulation Developed(params string[] technologies)
        {
            var career=GameCatalog.Default.CreateCareer();career.xp=10000;career.credits=10000;
            foreach(var id in technologies)if(!career.unlocked.Contains(id))career.unlocked.Add(id);
            career.equipped=GameContent.Tech.Where(t=>t.special&&technologies.Contains(t.id)).Select(t=>t.id).ToList();
            var sim=new GameSimulation(career);sim.Start(GameContent.Story[0]);return sim;
        }
        [MenuItem("Hack The Planet/Run Data Driven Validation")]
        public static void Run()
        {
            results.Clear();Directory.CreateDirectory("Artifacts");
            try
            {
                GameDefinitionValidator.ThrowIfInvalid(Root);Check(true,"shipped gameplay definitions validate");
                var captured=JsonUtility.FromJson<GameplaySnapshot>(File.ReadAllText("Artifacts/gameplay-baseline.json"));
                foreach(var golden in captured.networks)
                {
                    var career=new Career();if(golden.hardened)career.completed.Add("quiet-copy");
                    var job=new Contract{id="quiet-copy",company=captured.companies[0],seed=golden.seed,difficulty=golden.difficulty};
                    var actual=NetworkGenerator.Generate(job,career);var hash=GameplayBaseline.Hash(actual);
                    if(hash!=golden.hash)File.WriteAllText("Artifacts/network-parity-mismatch.json",JsonUtility.ToJson(new NetworkSnapshot(actual),true));
                    Check(hash==golden.hash,"exact network parity / "+golden.seed+" / difficulty "+golden.difficulty+" / hardened "+golden.hardened);
                }
                ValidateMechanics();ValidateAuthoring();ValidateAssetExtension();
                GameValidation.Run();Check(true,"existing simulation, full campaign, network, lore, settings-save validation passes");
                results.Add("TOTAL / "+results.Count+" assertions passed.");File.WriteAllLines("Artifacts/data-driven-validation.txt",results);Debug.Log(results.Last());
            }
            catch(Exception ex){results.Add(ex.ToString());File.WriteAllLines("Artifacts/data-driven-validation.txt",results);throw;}
        }
        private static void ValidateMechanics()
        {
            var plain=Developed();var hack=plain.Preview("hack","n2");Near(hack.duration,3.35f,"baseline relay hack duration");Near(hack.trace,4.7f,"baseline relay hack trace");
            var efficient=Developed("efficient","quiet");Near(efficient.Preview("hack","n2").duration,3.35f*.8f,"efficient hack duration");Near(efficient.Preview("hack","n2").trace,4.7f*.8f,"quiet hack trace");
            var ghost=Developed("quiet","ghost");Near(ghost.Preview("hack","n2").trace,0,"first relay fully trace free");ghost.Execute("hack","n2");Finish(ghost);Check(ghost.Counter("relay-controls")==1,"control trigger consumes first relay");
            var scan=Developed("wide","signal");Near(scan.Preview("scan","n2").duration,.8f,"signal halves scan duration");Near(scan.Preview("scan","n2").trace,0,"signal removes base scan trace");scan.Execute("scan","n2");Finish(scan);Near(scan.Stat("scanRadius"),3,"wide scan radius");
            var passive=Developed("wide","quiet","passive");passive.Execute("hack","n2");Finish(passive);Check(passive.Node("n14").known,"passive mapping reveals three hops from relay");
            var blueprint=Developed("wide","blueprint");Check(blueprint.Node("n22").known&&blueprint.Neighbors(blueprint.Node("n22")).All(n=>n.known),"blueprint operation-start trigger");
            var weak=Developed("wide","efficient","weakpoint","buffer");weak.Node("n2").scanned=true;Near(weak.Preview("hack","n2").trace,4.7f*.8f*.75f*.9f,"scan preparation, weak-point and global buffer stack");
            var pressure=Developed("efficient","quiet","buffer");pressure.Node("n6").known=true;pressure.Node("n6").controlled=true;pressure.Node("n10").known=true;
            var p=pressure.Preview("hack","n10");float basis=3+pressure.EffectiveSecurity(pressure.Node("n10"))*1.7f;
            Near(p.trace,(basis*.8f+p.duration*pressure.Scanners*.18f)*.9f,"scanner pressure follows duration modifiers and precedes global trace modifiers");
            var selective=Developed("efficient","selective");selective.Node("n22").known=true;selective.Node("n22").controlled=true;
            Near(selective.Preview("download","n22").duration,5.5f*.7f,"selective main-objective duration");Near(selective.Preview("download","n22").trace,8*.7f,"selective main-objective trace");
            var chain=Developed("efficient","chain");chain.Execute("scan","n2");Finish(chain);Check(chain.Execute("hack","n2")&&chain.Execute("hack","n6"),"chain permits future neighboring action");Check(!chain.Execute("hack","n6"),"duplicate queue entries rejected");Finish(chain);Check(chain.Node("n6").controlled,"queued action executes after gaining reachability");
            var invalid=Developed();invalid.Execute("hack","n2");invalid.Node("n6").known=true;Check(invalid.Execute("scan","n6"),"queue defers future reachability");invalid.Node("n6").scanned=true;Finish(invalid);Check(invalid.Op.queue.Count==0&&invalid.Op.log.Any(l=>l.Contains("SKIPPED")),"queued requirements rechecked and completed action skipped");
            var decoy=Developed("disrupt","decoy");decoy.Op.trace=30;Check(decoy.Execute("decoy"),"operator action executes");Near(decoy.Op.trace,18,"decoy subtracts authored trace");Check(!decoy.Execute("decoy"),"operator action use limit");
            decoy.Execute("hack","n2");decoy.SetPaused(true);decoy.Tick(.5f);Near(decoy.Op.decoyRemaining,10,"pause freezes status timers");decoy.SetPaused(false);Finish(decoy);Near(decoy.Op.trace,18,"trace prevention status protects a running action");
            while(decoy.Op.decoyRemaining>0)decoy.Tick(.5f);Check(decoy.Op.statuses.Count==0,"operator status expires while idle");
            var suppression=Developed("disrupt","blackout","efficient","cascade");suppression.Node("n6").known=true;suppression.Node("n6").controlled=true;suppression.Node("n5").known=true;
            Check(suppression.Execute("disrupt","n5"),"scanner suppression action available");Finish(suppression);Near(suppression.Node("n5").suppressed,45,"suppression uses authored duration");
            int count=suppression.Scanners;for(int i=0;i<121;i++)suppression.Tick(.5f);Check(suppression.Node("n5").suppressed==0&&suppression.Scanners>count,"expiration restores scanner pressure");
            suppression.Node("n10").known=true;suppression.Node("n10").controlled=true;suppression.Node("n14").known=true;int wall=suppression.Node("n18").security;
            Check(suppression.Execute("blackout","n14"),"hub suppression action available");Finish(suppression);Check(suppression.Node("n18").security==Mathf.Max(1,wall-2)&&suppression.Counter("cascade")==1,"cascade modifies all firewalls once");
            suppression.Op.trace=0;suppression.Execute("hack","n14");Finish(suppression);Check(suppression.Node("n18").security==Mathf.Max(1,wall-2),"capturing suppressed hub does not apply cascade twice");
            var bridge=Developed("wide","blueprint","efficient","chain","bridge");bridge.Node("n10").known=true;
            bridge.Node("n10").scanned=true;
            foreach(var n in bridge.Neighbors(bridge.Node("n10")).Take(1)){n.known=true;n.controlled=true;}
            Check(bridge.Preview("bridge","n10").allowed,"prepared bridge accepts one controlled neighbor");Near(bridge.Preview("bridge","n10").trace,0,"bridge terminal override removes all trace");bridge.Execute("bridge","n10");Finish(bridge);Check(bridge.Node("n10").controlled,"bridge applies reusable control effect");
            var backdoor=Developed("quiet","backdoor");backdoor.Execute("scan","n2");Finish(backdoor);
            var mappedRoute=NetworkGenerator.Generate(backdoor.Job,backdoor.Career).approaches.Last();var remote=backdoor.Node(mappedRoute[3]);
            Check(!backdoor.Reachable(remote)&&backdoor.ControlledDistance(remote)==3,"backdoor finds a mapped three-hop shortcut");
            Check(backdoor.Execute("backdoor",remote.id),"backdoor bypasses direct reachability within its range");Finish(backdoor);Check(remote.controlled&&!backdoor.Preview("backdoor","n2").allowed,"backdoor one-use counter");
            var saved=Developed("disrupt","decoy","efficient","chain");saved.Execute("decoy");saved.Execute("scan","n2");saved.Tick(.5f);saved.Execute("hack","n2");
            var restore=new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(saved.Career)));
            Check(restore.Op.paused&&restore.Op.queue.Count==1&&restore.Op.task.actionId=="scan"&&restore.Op.task.elapsed==.5f&&restore.Counter("uses:decoy")==1&&restore.Op.decoyRemaining>0,"v2 round trip preserves task, queue, counters and timed statuses");
        }
        private static void ValidateAuthoring()
        {
            var root=UnityEngine.Object.Instantiate(Root);var action=UnityEngine.Object.Instantiate(Root.actions[0]);root.actions=root.actions.Concat(new[]{action}).ToArray();
            Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("unique ID")),"duplicate definition IDs rejected");
            action.id="invalid-action";action.duration=B(Arithmetic.Divide,N(1),N(0));Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("division/modulo")),"constant division by zero rejected");
            action.duration=N(-1);Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("positive duration")),"invalid timing rejected");
            var cycle=new NotCondition();cycle.condition=cycle;action.visible=cycle;Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("cyclic")),"cyclic managed rules rejected");
            action.visible=new AlwaysCondition();action.duration=N(1);
            root.modifiers=new[]{Mod("queueCapacity",Arithmetic.Override,S("queueCapacity"))};
            Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("cyclic stat dependency")),"cyclic stat formulas rejected");root.modifiers=Array.Empty<Modifier>();
            var tech=UnityEngine.Object.Instantiate(Root.technologies[0]);tech.id="cycle-tech";tech.parents=new[]{tech};root.technologies=root.technologies.Concat(new[]{tech}).ToArray();
            Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("research dependency cycle")),"research dependency cycles rejected");
            var contract=UnityEngine.Object.Instantiate(Root.contracts[0]);contract.id="empty-contract";contract.objectives=Array.Empty<ObjectiveDefinition>();root.contracts=root.contracts.Concat(new[]{contract}).ToArray();
            Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("at least one objective")),"empty objective contracts rejected");
            contract.objectives=new[]{new ObjectiveDefinition{nodeId="missing"}};Check(GameDefinitionValidator.Validate(root).Any(e=>e.Contains("missing target")),"impossible objective references rejected");
            var saved=GameCatalog.Default.CreateCareer();saved.unlocked.Add("removed-technology");
            bool failed=false;try{new GameSimulation(saved);}catch(InvalidDataException){failed=true;}Check(failed,"unknown v2 references rejected before modifying save");
            string path="Artifacts/invalid-reference-career.json";File.WriteAllText(path,JsonUtility.ToJson(saved));string before=File.ReadAllText(path);
            failed=false;try{CareerStorage.Load(path);}catch(InvalidDataException){failed=true;}Check(failed&&File.ReadAllText(path)==before,"invalid v2 save stays byte-for-byte intact");
            UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(action);UnityEngine.Object.DestroyImmediate(tech);UnityEngine.Object.DestroyImmediate(contract);
            GameplayRichText.ReloadPalette();
            var scanner = new NetNode { label = "Traffic scanner", kind = NodeKind.Scanner, security = 2, known = true, detail = GameCatalog.Default.NodeTypes.First(t => t.glyph == NodeKind.Scanner).description };
            var intel = GraphViewport.DescribeNode(scanner, ContractIntel.Full, false, GameCatalog.Default);
            var rich = GameplayRichText.Format(intel);
            Check(rich.Contains("<color=") && rich.Contains("<b>") && rich.Contains("DISCOVERED"), "gameplay text palette bolds and colors tooltip terms");
            Check(GameplayRichText.Plain(rich) == intel, "gameplay rich text preserves readable copy");
        }
        public sealed class Fixture : IDisposable
        {
            public GameDefinition Definition;
            public GameCatalog Catalog;
            public List<GameplayDefinition> Owned=new List<GameplayDefinition>();
            public void Dispose(){Catalog?.Dispose();foreach(var asset in Owned)UnityEngine.Object.DestroyImmediate(asset);}
        }
        public static Fixture CreateFixture()
        {
            var fixture=new Fixture();var root=UnityEngine.Object.Instantiate(Root);root.name="Asset-only validation fixture";fixture.Owned.Add(root);fixture.Definition=root;
            root.triggers=root.triggers.Concat(new[]{new Trigger{on=RuleEvent.StatusExpired,effects=new Effect[]{new CounterEffect{key="expired-statuses"}}}}).ToArray();
            T New<T>(string id,string title)where T:GameplayDefinition{var d=ScriptableObject.CreateInstance<T>();d.id=id;d.title=title;d.name=id;fixture.Owned.Add(d);return d;}
            var nodeType=New<NodeTypeDefinition>("audit-terminal","Audit terminal");nodeType.glyph=NodeKind.Workstation;nodeType.color=Color.magenta;nodeType.tags=new[]{"audit-target"};nodeType.security=N(1);root.nodeTypes=root.nodeTypes.Concat(new[]{nodeType}).ToArray();
            var action=New<ActionDefinition>("audit","Verify sample");action.duration=N(2);action.baseTrace=N(1);action.shortcut=KeyCode.V;action.shortcutLabel="V";action.tags=new[]{"analysis"};action.visible=Tag("audit-target");
            action.requirements=new[]{Req(State(NodeState.Known),"Discover the target."),Req(State(NodeState.Controlled),"Control this terminal first.")};action.queueRequirements=new[]{Req(State(NodeState.Known),"Discover the target.")};action.effects=new Effect[]{new SetStateEffect{state=NodeState.Collected},Log("SAMPLE VERIFIED // {target}")};root.actions=root.actions.Concat(new[]{action}).ToArray();
            var tech=New<TechnologyDefinition>("field-analysis","Field analysis");tech.branch="CROSSOVER";tech.position=new Vector2(920,600);tech.parents=new[]{root.technologies[0]};tech.grants=new[]{action};root.technologies=root.technologies.Concat(new[]{tech}).ToArray();
            var hardware=New<HardwareDefinition>("expansion","Analysis expansion");hardware.category="EXPANSION";hardware.statLabel="ABILITY SLOTS";hardware.displayedStat="slots";hardware.tiers=new[]{new HardwareTier{price=1,modifiers=new[]{Mod("slots",Arithmetic.Add,N(2))}}};
            var cpu=UnityEngine.Object.Instantiate(root.hardware[0]);fixture.Owned.Add(cpu);cpu.tiers=cpu.tiers.Concat(new[]{new HardwareTier{price=2500,modifiers=new[]{Mod("hackSpeed",Arithmetic.Add,N(.5f))}}}).ToArray();root.hardware=root.hardware.Select(h=>h.id==cpu.id?cpu:h).Concat(new[]{hardware}).ToArray();
            var network=New<NetworkDefinition>("field-network","Field network");network.layout=NetworkLayout.Authored;network.entryNode="entry";network.preferredNode="alpha";network.previewNode="beta";
            network.nodes=new[]{new NetworkNode{id="entry",label="Entry",type=root.nodeTypes[0],known=true,controlled=true,position=new Vector3(-6,0,0)},new NetworkNode{id="alpha",label="Access terminal",type=nodeType,known=true,position=Vector3.zero},new NetworkNode{id="beta",label="Sample terminal",type=nodeType,known=true,position=new Vector3(6,0,0)},new NetworkNode{id="side",label="Side terminal",type=nodeType,position=new Vector3(0,5,4)}};
            network.links=new[]{new NetworkEdge{a="entry",b="alpha"},new NetworkEdge{a="alpha",b="beta"},new NetworkEdge{a="alpha",b="side"}};root.networks=root.networks.Concat(new[]{network}).ToArray();
            var contract=New<ContractDefinition>("field-contract","Field investigation");contract.company=root.companies[0];contract.network=network;contract.objective="Verify the sample.";contract.credits=N(111);contract.xp=N(7);contract.seed=N(99);contract.objectives=new[]{new ObjectiveDefinition{nodeId="beta",action=action,actionLabel="Verify sample",complete=State(NodeState.Collected),actionRequirements=new[]{Req(new NamedNodeCondition{nodeId="alpha",condition=State(NodeState.Controlled)},"Control the access terminal first.")}}};root.contracts=root.contracts.Concat(new[]{contract}).ToArray();
            fixture.Catalog=new GameCatalog(root);return fixture;
        }
        private static void ValidateAssetExtension()
        {
            using(var fixture=CreateFixture())
            {
                var sim=new GameSimulation(fixture.Catalog);sim.Career.credits=10000;
                Check(sim.BuyTech("field-analysis"),"asset-only technology can grant a new action");Check(sim.Upgrade("expansion")&&sim.Slots==4,"asset-only hardware affects generic stats");
                sim.Career.SetLevel("cpu",3);Check(sim.Upgrade("cpu")&&sim.Career.Level("cpu")==4,"asset-only extra hardware tier purchases without an enum change");
                Check(sim.Start(fixture.Catalog.Contracts(sim.Career).First(c=>c.id=="field-contract")),"asset-only contract uses a different network layout");
                Check(sim.Op.nodes.Count==4&&sim.Op.entryNode=="entry"&&sim.Node("beta").typeId=="audit-terminal","asset-only node type and arbitrary stable node IDs");
                GameValidation.Hack(sim,"alpha");GameValidation.Hack(sim,"beta");
                Check(sim.AvailableActions("beta").Any(a=>a.id=="audit"&&a.shortcut==KeyCode.V),"new action discoverable with an authored shortcut");Near(sim.Preview("audit","beta").duration,2,"new action uses authored formula");
                fixture.Definition.actions.First(a=>a.id=="audit").duration=N(999);Near(sim.Preview("audit","beta").duration,2,"compiled catalog is isolated from subsequent asset changes");
                Check(sim.Execute("audit","beta"),"new composed action executes");
                sim.ApplyStatus(fixture.Catalog.Data.statuses.First(s=>s.preventsTrace),null,.25f);sim.Tick(.5f);
                Check(sim.Counter("expired-statuses")==1,"status-expiration event runs authored effects");
                sim.ApplyStatus(fixture.Catalog.Data.statuses.First(s=>s.preventsTrace),null,5);
                Check(sim.Execute("scan","beta"),"new action supports the generic queue");
                const string savePath="Artifacts/asset-extension-career-v2.json";
                Check(CareerStorage.Save(sim.Career,savePath,fixture.Catalog),"custom catalog career saves with stable IDs");
                var restored=new GameSimulation(fixture.Catalog,CareerStorage.Load(savePath,fixture.Catalog));
                Check(restored.Op.paused&&restored.Op.task.actionId=="audit"&&restored.Op.queue[0].actionId=="scan"&&restored.Counter("expired-statuses")==1&&restored.Op.statuses.Count==1&&restored.Career.Level("cpu")==4,"custom v2 save restores actions, queue, counters, statuses and extended hardware");
                Finish(sim);Check(sim.ObjectiveComplete,"new action satisfies authored completion condition");
                var report=sim.Disconnect();Check(report.success&&sim.Career.completed.Contains("field-contract"),"new objective and contract deliver through the generic career flow");
            }
        }
    }
}
