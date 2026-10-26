using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
    public enum NodeKind { Gateway, Workstation, Relay, Firewall, Scanner, Database, SecurityHub }
    public enum JobKind { Copy, Alter, Investigate, Disrupt }
    public enum ToolKind { Scan, Hack, Download, Disrupt, Bridge, Backdoor }

    [Serializable]
    public sealed class NetNode
    {
        public string id, label, detail;
        public NodeKind kind;
        public int security, column, row;
        public float x, y, suppressed;
        public bool known, controlled, scanned, collected;
        public bool objective, bonus;
    }

    [Serializable]
    public sealed class NetLink
    {
        public string a, b;
        public NetLink(string from, string to) { a = from; b = to; }
    }

    public sealed class Contract
    {
        public string id, title, client, company, tag, description, objective, condition, aftermath;
        public JobKind kind;
        public int credits, xp, difficulty, seed, requiredWins;
        public float quietLimit;
        public bool recurring;
    }

    public sealed class Technology
    {
        public string id, title, branch, description;
        public int cost;
        public string[] parents;
        public bool special;
        public Vector2 position;
        public Technology(string id, string title, string branch, int cost, float x, float y,
            string description, bool special = false, params string[] parents)
        {
            this.id = id; this.title = title; this.branch = branch; this.cost = cost;
            position = new Vector2(x, y); this.description = description; this.special = special;
            this.parents = parents.Length == 0 && id != "base" ? new[] { "base" } : parents;
        }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameContent
    {
        public static readonly string[] Companies = { "HELIX DYNAMICS", "MORROW FINANCIAL", "AEGIS SYSTEMS" };
        public static readonly Contract[] Story = {
            new Contract { id="quiet-copy", title="The Quiet Copy", client="LARK // independent researcher", company=Companies[0], tag="DATA ACQUISITION", kind=JobKind.Copy,
                description="Helix buried a clean-energy prototype. A researcher wants the original files before tomorrow's product demonstration. Find a way into their research vault. Leave the lights on.",
                objective="Recover the prototype from Research vault.", condition="Finish below 70% trace for +10 XP and +150 cr.", aftermath="An anonymous prototype appears on the open net. Helix's demonstration goes ahead, but the technology is no longer theirs alone.", credits=650, xp=50, quietLimit=70, difficulty=1, seed=114, requiredWins=0 },
            new Contract { id="paper-trail", title="Paper Trail", client="JUNE // public-interest archivist", company=Companies[1], tag="RECORD ALTERATION", kind=JobKind.Alter,
                description="Morrow's automated debt system has been charging a neighborhood twice. Correct the ledger and its backup mirror so the change survives the next sync.",
                objective="Correct Research vault AND Backup mirror.", condition="Synchronize both records. Below 65% trace: +10 XP / +150 cr.", aftermath="Thousands of duplicate balances disappear. Morrow replaces a maintenance identity; its perimeter gets a little stronger.", credits=850, xp=65, quietLimit=65, difficulty=1, seed=222, requiredWins=1 },
            new Contract { id="ghost-wire", title="Ghost in the Wire", client="NULL // network cooperative", company=Companies[2], tag="FORENSIC RECOVERY", kind=JobKind.Investigate,
                description="Someone used Aegis infrastructure to hit a community network. Recover three signed activity records and reconstruct the route. Evidence is scattered across the system.",
                objective="Recover records at Dev terminal, Audit terminal, and Research vault.", condition="Collect all three records. Below 75%: +10 XP / +150 cr.", aftermath="The records link Aegis's contractor to Morrow's debt platform. Three unrelated jobs begin to look like one story.", credits=1050, xp=80, quietLimit=75, difficulty=2, seed=330, requiredWins=2 },
            new Contract { id="glasshouse", title="Glasshouse", client="LARK // encrypted channel", company=Companies[0], tag="CORPORATE INTELLIGENCE", kind=JobKind.Copy,
                description="Helix's prototype was funded through a shell company. Retrieve the ownership archive. Their security team has tightened the core, but the old backup lane is still there.",
                objective="Recover the ownership archive from Research vault.", condition="Recover an optional archive for +15 XP. Below 65%: quiet bonus.", aftermath="The shell company belongs to Aegis. A private security contract is turning public research into private leverage.", credits=1250, xp=90, quietLimit=65, difficulty=2, seed=448, requiredWins=3 },
            new Contract { id="dead-drop", title="Dead Drop", client="JUNE // one-time address", company=Companies[1], tag="RECORD ALTERATION", kind=JobKind.Alter,
                description="A whistleblower's account is about to be frozen. Replace the seizure order in the primary ledger and backup mirror. There is just enough time to keep them out of sight.",
                objective="Replace the order at Research vault AND Backup mirror.", condition="Both copies must agree. Below 70%: +10 XP / +150 cr.", aftermath="The whistleblower escapes the financial dragnet and releases a list of surveillance contracts. Aegis starts taking systems offline.", credits=1450, xp=100, quietLimit=70, difficulty=3, seed=551, requiredWins=4 },
            new Contract { id="last-light", title="Last Light", client="NULL // everyone is listening", company=Companies[2], tag="INFRASTRUCTURE DISRUPTION", kind=JobKind.Disrupt,
                description="The contracts connect at a single surveillance hub. Take control of the hub, then erase the targeting index in the vault. Give the city one quiet night.",
                objective="Control Security hub, then erase the index at Research vault.", condition="Disable the hub before the vault. Below 75%: quiet bonus.", aftermath="The surveillance grid goes dark. Lark opens the research, June publishes the records, and Null keeps the channels alive. You built more than a career. The planet is a little harder to own.", credits=2000, xp=140, quietLimit=75, difficulty=3, seed=669, requiredWins=5 }
        };

        public static readonly Technology[] Tech = {
            new Technology("base", "Basic toolkit", "CORE", 0, 500, 310, "Your starting tools: scan, hack, recover data, and disconnect. Every unlocked passive is always active."),
            new Technology("wide", "Wide scan", "RECON", 50, 320, 155, "Scans reveal three network hops instead of two. Make a plan before making noise."),
            new Technology("blueprint", "Blueprint", "RECON", 120, 135, 85, "Start every contract with the objective and all its neighboring systems revealed.", false, "wide"),
            new Technology("signal", "Signal analysis", "RECON", 80, 135, 220, "Scanning generates no trace and takes half as long.", false, "wide"),
            new Technology("efficient", "Efficient hack", "INTRUSION", 50, 680, 155, "All standard hacks finish 25% faster. Hardware speed stacks with this benefit."),
            new Technology("chain", "Chain hack", "INTRUSION", 130, 865, 85, "Queue up to three actions. Queue a hack on the next discovered system before the current hack finishes.", false, "efficient"),
            new Technology("selective", "Selective download", "INTRUSION", 120, 865, 220, "Secure the main objective in 55% of the usual recovery time, with 30% less recovery trace.", false, "efficient"),
            new Technology("quiet", "Quiet tools", "STEALTH", 50, 320, 460, "Standard hacks generate 30% less trace. A quiet route leaves room for optional files."),
            new Technology("ghost", "Ghost relay", "STEALTH", 100, 135, 400, "The first relay hack in every contract generates no trace, including scanner pressure.", false, "quiet"),
            new Technology("backdoor", "Backdoor", "STEALTH", 150, 135, 545, "Once per contract, instantly reclaim any discovered workstation or relay without trace. This can open a new route.", true, "quiet"),
            new Technology("disrupt", "Disrupt scanner", "CONTROL", 50, 680, 460, "Suppress a reachable scanner for 60 seconds without first hacking it. Reusable while equipped.", true),
            new Technology("blackout", "Blackout", "CONTROL", 130, 865, 400, "Suppress a reachable security hub for 60 seconds. Its connected defenses lose their bonus.", true, "disrupt"),
            new Technology("decoy", "Decoy process", "CONTROL", 150, 865, 545, "Once per contract, remove 15 trace and prevent all trace growth for 20 seconds. Activate from your loadout.", true, "disrupt"),
            new Technology("passive", "Passive mapping", "CROSSOVER", 200, 300, 310, "Hacking a relay automatically scans three hops. Requires both Recon and Stealth roots.", false, "wide", "quiet"),
            new Technology("weakpoint", "Weak-point analysis", "CROSSOVER", 200, 500, 65, "Scanned targets generate 40% less hack trace. Requires both Recon and Intrusion roots.", false, "wide", "efficient"),
            new Technology("cascade", "System cascade", "CROSSOVER", 220, 715, 310, "Disabling or capturing the hub reduces every firewall's security by two for the rest of the contract.", false, "efficient", "disrupt"),
            new Technology("bridge", "Firewall bridge", "CROSSOVER", 230, 500, 200, "Bypass a firewall in two seconds for zero trace when at least two neighboring systems are controlled. Requires an ability slot.", true, "blueprint", "chain"),
            new Technology("buffer", "Trace buffer", "CROSSOVER", 200, 500, 565, "All trace generation is reduced by another 15%. Requires both Stealth and Control roots.", false, "quiet", "disrupt")
        };

        public static Technology FindTech(string id) => Tech.FirstOrDefault(t => t.id == id);

        public static List<Contract> Contracts(Career career)
        {
            var result = new List<Contract>(Story);
            if (career.completed.Count >= 3)
                for (int i = 0; i < 3; i++)
                    result.Add(new Contract { id = "freelance-" + i, title = new[] { "Open Secrets", "Friendly Correction", "Signal Lost" }[i], client = "EXCHANGE // verified escrow",
                        company = Companies[i], tag = "FREELANCE / REPEATABLE", kind = (JobKind)i, credits = 600 + career.day * 35, xp = 55 + i * 5,
                        difficulty = Mathf.Clamp(1 + career.day / 5, 1, 3), requiredWins = 3, seed = career.day * 71 + i * 37, quietLimit = 75, recurring = true,
                        description = "A verified exchange contract generated from today's network conditions. Familiar infrastructure, a new layout of defenses, and payment on a clean extraction.",
                        objective = i == 1 ? "Correct Research vault AND Backup mirror." : i == 2 ? "Recover records at Dev terminal, Audit terminal, and Research vault." : "Recover the archive from Research vault.",
                        condition = "Main objective: base XP. Optional archives: +15 XP each (max 30). Below 75%: +10 XP.", aftermath = "A quiet freelance job closes. The exchange has fresh work, and the network keeps changing." });
            return result;
        }

        public static List<NetNode> Network(Contract job, Career career)
        {
            var random = new System.Random(job.seed);
            var nodes = new List<NetNode> { new NetNode { id="home", label="Your gateway", kind=NodeKind.Gateway, x=60, y=228, known=true, controlled=true,
                detail="A secondhand rig. A private connection. A place to begin." } };
            string[] names = { "Guest terminal", "Public relay", "Office terminal", "Campus relay", "Edge scanner", "Staff terminal", "Perimeter wall", "Maintenance", "Dev terminal", "Core firewall", "Traffic scanner", "Build terminal", "Cold archive", "Security hub", "Service relay", "Backup relay", "Vault firewall", "Research wall", "Audit terminal", "Backup mirror", "Private archive", "Research vault", "Test terminal", "Export relay" };
            NodeKind[] kinds = { NodeKind.Workstation, NodeKind.Relay, NodeKind.Workstation, NodeKind.Relay, NodeKind.Scanner, NodeKind.Workstation, NodeKind.Firewall, NodeKind.Relay,
                NodeKind.Workstation, NodeKind.Firewall, NodeKind.Scanner, NodeKind.Workstation, NodeKind.Database, NodeKind.SecurityHub, NodeKind.Relay, NodeKind.Relay,
                NodeKind.Firewall, NodeKind.Firewall, NodeKind.Workstation, NodeKind.Database, NodeKind.Database, NodeKind.Database, NodeKind.Workstation, NodeKind.Relay };
            for (int i = 1; i <= 24; i++)
            {
                int col = (i - 1) / 4, row = (i - 1) % 4;
                var kind = kinds[i - 1];
                int security = kind == NodeKind.Firewall ? 3 + job.difficulty : kind == NodeKind.Relay ? 1 : 1 + random.Next(2);
                if (col >= 4) security += job.difficulty - 1;
                if (career.completed.Any(id => Story.FirstOrDefault(c => c.id == id)?.company == job.company) && kind == NodeKind.Firewall) security++;
                nodes.Add(new NetNode { id="n" + i, label=names[i-1], kind=kind, x=205+col*145, y=70+row*105, column=col, row=row, security=security,
                    objective = i == 22 || (job.kind == JobKind.Alter && i == 20) || (job.kind == JobKind.Investigate && (i == 9 || i == 19)) || (job.kind == JobKind.Disrupt && i == 14),
                    bonus = (i == 13 || i == 21 || (i == 20 && job.kind != JobKind.Alter)), detail=Description(kind) });
            }
            return nodes;
        }

        public static List<NetLink> Links(int seed)
        {
            var links = new List<NetLink> { new NetLink("home", "n2"), new NetLink("home", "n4") };
            for (int col = 0; col < 5; col++)
                for (int row = 0; row < 4; row++)
                    links.Add(new NetLink("n" + (col*4+row+1), "n" + ((col+1)*4+row+1)));
            for (int col = 0; col < 6; col++)
            {
                int start = col * 4 + 1;
                links.Add(new NetLink("n" + start, "n" + (start+1)));
                links.Add(new NetLink("n" + (start+2), "n" + (start+3)));
                if (col == 0 || col == 3 || col == 5 || (col + seed) % 3 == 0)
                    links.Add(new NetLink("n" + (start+1), "n" + (start+2)));
            }
            return links;
        }

        public static string Description(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Workstation: return "An everyday terminal. Low defenses make this a useful foothold. Some contain signed evidence.";
                case NodeKind.Relay: return "A stepping stone to another part of the network. Maintenance paths are often quieter than the front door.";
                case NodeKind.Firewall: return "A hardened route barrier. Hack it, take a detour, or use Firewall bridge with two controlled neighbors.";
                case NodeKind.Scanner: return "Adds trace to every active task while any adjacent node is controlled. Hack it to switch it off permanently, or suppress it with Control tools.";
                case NodeKind.SecurityHub: return "Strengthens adjacent firewalls and databases by two security. Capturing or suppressing the hub removes that bonus.";
                case NodeKind.Database: return "A store of valuable data. Control it first, then recover its files. Marked archives contain optional credits and XP.";
                default: return "Your private gateway into the network.";
            }
        }
    }
}
