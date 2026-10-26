using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
    public sealed class GeneratedNetwork
    {
        public List<NetNode> nodes = new List<NetNode>();
        public List<NetLink> links = new List<NetLink>();
        public List<List<string>> approaches = new List<List<string>>();
    }

    public static class NetworkGenerator
    {
        public const int Version = 2;

        public static GeneratedNetwork Generate(Contract job, Career career)
        {
            var random = new System.Random(job.seed);
            var result = new GeneratedNetwork { nodes = GameContent.LegacyNodes(job, career) };
            int difficulty = Mathf.Clamp(job.difficulty, 1, 3), count = 20 + difficulty * 12;
            NetNode Find(string id) => result.nodes.First(n => n.id == id);
            void Add(string id, NodeKind kind, string label) => result.nodes.Add(new NetNode
                { id = id, kind = kind, label = label, security = 1, detail = GameContent.Description(kind) });
            Add("watch-a", NodeKind.Scanner, "Ingress sensor");
            Add("watch-b", NodeKind.Scanner, "Deep packet sensor");
            Add("archive-c", NodeKind.Database, "Sealed correspondence");
            while (result.nodes.Count < count)
                Add("extra-" + result.nodes.Count, NodeKind.Relay, "Maintenance relay " + (result.nodes.Count - 24));
            foreach (var node in result.nodes) node.bonus = false;
            string[] archives = { "n13", "n21", "archive-c" };
            for (int i = 0; i < archives.Length; i++)
            {
                var node = Find(archives[i]); node.bonus = true;
                node.loreId = LoreContent.For(job, i).id;
            }
            var edges = new HashSet<string>();
            void Link(string a, string b)
            {
                var key = string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
                if (a != b && edges.Add(key)) result.links.Add(new NetLink(a, b));
            }
            var reserved = new HashSet<string>(new[] { "home", "n2", "n22", "n9", "n19", "n20",
                "n6", "n10", "n14", "n18", "n5", "n11", "watch-a", "watch-b" }.Concat(archives));
            // Shuffling the available identities varies topology while retaining contract identities.
            var pool = new Queue<NetNode>(result.nodes.Where(n => !reserved.Contains(n.id)).OrderBy(_ => random.Next()));
            int[][] lengths = { new[] { 4, 6, 8 }, new[] { 4, 5, 6, 8 }, new[] { 4, 5, 6, 7, 9 } };
            float rotation = (float)random.NextDouble() * Mathf.PI * 2;
            Find("home").position = new Vector3(-19, -1, -2);
            Find("n2").position = new Vector3(-14, 0, 0);
            Find("n22").position = new Vector3(14, 0, 0);
            Link("home", "n2");
            for (int lane = 0; lane < lengths[difficulty - 1].Length; lane++)
            {
                int length = lengths[difficulty - 1][lane];
                var route = new List<string> { "home", "n2" };
                for (int step = 0; step < length; step++)
                {
                    var node = lane == 0 ? Find(new[] { "n6", "n10", "n14", "n18" }[step]) : pool.Dequeue();
                    // Speed comes from fewer systems; risk comes from persistent scanner exposure.
                    if (lane > 0)
                    {
                        node.kind = step % 3 == 1 ? NodeKind.Workstation : NodeKind.Relay;
                        node.label = (lane == lengths[difficulty - 1].Length - 1 ? "Maintenance" : "Transit") + " " + lane + "." + (step + 1);
                        node.security = lane == lengths[difficulty - 1].Length - 1 ? 1 : 1 + (step % 2);
                    }
                    else
                    {
                        node.security = step == 0 ? 1 : 2;
                        if (node.kind == NodeKind.Firewall)
                            node.security += difficulty - 1 + (career.completed.Any(id =>
                                GameContent.Story.FirstOrDefault(c => c.id == id)?.company == job.company) ? 1 : 0);
                    }
                    node.detail = GameContent.Description(node.kind);
                    float t = (step + 1f) / (length + 1), angle = rotation + lane * Mathf.PI * 2 / lengths[difficulty - 1].Length;
                    float radius = 8 + lane * 1.5f;
                    node.position = new Vector3(Mathf.Lerp(-14, 14, t), Mathf.Cos(angle) * radius * Mathf.Sin(t * Mathf.PI),
                        Mathf.Sin(angle) * radius * Mathf.Sin(t * Mathf.PI));
                    node.position += new Vector3(Jitter(random), Jitter(random), Jitter(random)) * .45f;
                    Link(route.Last(), node.id); route.Add(node.id);
                }
                Link(route.Last(), "n22"); route.Add("n22"); result.approaches.Add(route);
            }
            // Sensor leaves cannot become accidental traversal shortcuts. All four observe the fast ingress.
            string[] sensors = { "n5", "n11", "watch-a", "watch-b" };
            for (int i = 0; i < sensors.Length; i++) Link("n6", sensors[i]);
            // Every secondary contract target has an approach from each independent lane.
            foreach (string id in new[] { "n9", "n19", "n20" })
                foreach (var route in result.approaches) Link(route[route.Count - 2], id);
            // Crossovers connect only intermediate lanes, preserving the quiet and exposed extremes.
            if (result.approaches.Count > 3) Link(result.approaches[1][4], result.approaches[2][4]);
            for (int lane = 1; lane < result.approaches.Count - 1; lane++)
                for (int i = 0; i < (difficulty == 1 ? 1 : 2); i++)
                {
                    var sensor = pool.Dequeue(); sensor.kind = NodeKind.Scanner;
                    sensor.label = "Transit sensor " + lane + "." + (i + 1);
                    sensor.detail = GameContent.Description(sensor.kind);
                    Link(result.approaches[lane][2], sensor.id);
                }
            for (int i = 0; i < archives.Length; i++)
            {
                var route = result.approaches[(i + 1) % result.approaches.Count];
                Link(route[2 + i % (route.Count - 3)], archives[i]);
            }
            while (pool.Count > 0)
            {
                var node = pool.Dequeue(); node.kind = NodeKind.Workstation; node.security = 1;
                node.detail = GameContent.Description(node.kind);
                var route = result.approaches[1 + random.Next(result.approaches.Count - 1)];
                Link(route[2 + random.Next(route.Count - 3)], node.id);
            }
            // Place satellites around their connected sector, with deterministic minimum spacing.
            var placed = result.nodes.Where(n => n.position != Vector3.zero || n.id == "n2" || n.id == "n22").ToList();
            foreach (var node in result.nodes.Except(placed).ToArray())
            {
                var edge = result.links.First(l => l.a == node.id || l.b == node.id);
                var parent = Find(edge.a == node.id ? edge.b : edge.a);
                for (int attempt = 0; attempt < 1000; attempt++)
                {
                    var offset = new Vector3(Jitter(random), Jitter(random), Jitter(random));
                    node.position = parent.position + offset.normalized * (3.5f + attempt * .025f);
                    if (placed.All(n => Vector3.Distance(n.position, node.position) >= 2.6f)) break;
                }
                placed.Add(node);
            }
            // Separate close neighbors without changing their sector or topology.
            for (int pass = 0; pass < 80; pass++)
                for (int i = 0; i < result.nodes.Count; i++)
                    for (int j = i + 1; j < result.nodes.Count; j++)
                    {
                        var delta = result.nodes[j].position - result.nodes[i].position;
                        if (delta.magnitude >= 2.65f) continue;
                        var push = (delta.sqrMagnitude < .0001f ? Vector3.up : delta.normalized) * (2.65f - delta.magnitude) * .51f;
                        result.nodes[i].position -= push; result.nodes[j].position += push;
                    }
            return result;
        }

        private static float Jitter(System.Random random) => (float)random.NextDouble() * 2 - 1;

        public static void UpgradeLegacy(Operation operation)
        {
            if (operation.generationVersion >= Version) return;
            foreach (var node in operation.nodes)
                node.position = new Vector3((node.x - 500) / 43f, (250 - node.y) / 43f,
                    Mathf.Sin(node.column * 1.7f + node.row * 2.3f) * 7);
            operation.generationVersion = Version;
        }
    }
}
