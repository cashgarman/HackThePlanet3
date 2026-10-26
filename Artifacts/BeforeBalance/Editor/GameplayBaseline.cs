using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [Serializable] public sealed class GameplaySnapshot
    {
        public string[] companies;
        public Contract[] contracts;
        public Technology[] technologies;
        public NetNode[] prototypes;
        public List<LoreFragment> lore = new List<LoreFragment>();
        public List<NetworkGolden> networks = new List<NetworkGolden>();
    }
    [Serializable] public sealed class NetworkGolden { public int seed, difficulty; public bool hardened; public string hash; }
    [Serializable] public sealed class NetworkSnapshot
    {
        public GoldenNode[] nodes;public List<NetLink> links;[NonSerialized] public List<List<string>> approaches;
        public NetworkSnapshot(GeneratedNetwork n){nodes=n.nodes.Select(x=>new GoldenNode{id=x.id,label=x.label,detail=x.detail,kind=x.kind,security=x.security,column=x.column,row=x.row,x=x.x,y=x.y,suppressed=x.suppressed,known=x.known,controlled=x.controlled,scanned=x.scanned,collected=x.collected,objective=x.objective,bonus=x.bonus,position=x.position,loreId=x.loreId}).ToArray();links=n.links;approaches=n.approaches;}
    }
    [Serializable] public sealed class GoldenNode
    {
        public string id,label,detail;
        public NodeKind kind;
        public int security,column,row;
        public float x,y,suppressed;
        public bool known,controlled,scanned,collected;
        public bool objective,bonus;
        public Vector3 position;
        public string loreId;
    }
    public static class GameplayBaseline
    {
        public static string Hash(GeneratedNetwork n)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new NetworkSnapshot(n))))).Replace("-","");
        }
    }
}
