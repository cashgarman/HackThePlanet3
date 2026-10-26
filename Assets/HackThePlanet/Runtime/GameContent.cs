using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
    public enum NodeKind { Gateway, Workstation, Relay, Firewall, Scanner, Database, SecurityHub }
    public enum JobKind { Copy, Alter, Investigate, Disrupt }
    public enum ToolKind { Scan, Hack, Download, Disrupt, Bridge, Backdoor }
    public enum ContractIntel { Full, Topology, None }

    [Serializable]
    public sealed class NetNode
    {
        public string id, label, detail, typeId;
        public NodeKind kind;
        public int security, column, row;
        public float x, y, suppressed;
        public bool known, controlled, scanned, collected;
        public bool objective, bonus;
        public Vector3 position;
        public Color color;
        public string loreId;
    }

    [Serializable]
    public sealed class NetLink
    {
        public string a, b;
        public NetLink(string from, string to) { a = from; b = to; }
    }

    [Serializable]
    public sealed class Contract
    {
        public string id, title, client, company, tag, description, objective, condition, aftermath;
        public JobKind kind;
        public int credits, xp, difficulty, seed, requiredWins;
        public float quietLimit;
        public bool recurring;
        public string networkId;
        public LoreFragment[] lore=Array.Empty<LoreFragment>();
        public ContractIntel Intel => difficulty <= 1 ? ContractIntel.Full : difficulty == 2 ? ContractIntel.Topology : ContractIntel.None;
        public string DifficultyLabel => Intel == ContractIntel.Full ? "EASY" : Intel == ContractIntel.Topology ? "MEDIUM" : "HARD";
        public float RewardMultiplier => Intel == ContractIntel.Full ? 1f : Intel == ContractIntel.Topology ? 1.5f : 2f;
        public string IntelDescription => Intel == ContractIntel.Full ? "Full network intel. System types and objectives are visible before connecting."
            : Intel == ContractIntel.Topology ? "Topology only. All systems are unknown until you connect and discover them."
            : "No intel available. Connect and discover the network from your gateway.";
    }

    [Serializable]
    public sealed class Technology
    {
        public string id, title, branch, description;
        public int cost;
        public string[] parents;
        public string[] grants=Array.Empty<string>();
        public bool special;
        public Vector2 position;
        public Technology(string id, string title, string branch, int cost, float x, float y,
            string description, bool special = false, params string[] parents)
        {
            this.id = id; this.title = title; this.branch = branch; this.cost = cost;
            position = new Vector2(x, y); this.description = description; this.special = special;
            this.parents = parents;
        }
    }

    // Compatibility metadata facade. Gameplay uses the catalog injected into its simulation.
    public static class GameContent
    {
        public static GameCatalog Catalog => GameCatalog.Default;
        public static string[] Companies => Catalog.Companies.ToArray();
        public static Contract[] Story => Catalog.Contracts(new Career()).Where(x=>!x.recurring).ToArray();
        public static Technology[] Tech => Catalog.Technologies.ToArray();
        public static Technology FindTech(string id) => Catalog.Technologies.FirstOrDefault(x=>x.id==id);
        public static List<Contract> Contracts(Career career) => Catalog.Contracts(career).ToList();
        public static string Description(NodeKind glyph) => Catalog.NodeTypes.FirstOrDefault(x=>x.glyph==glyph)?.description??string.Empty;
    }
}
