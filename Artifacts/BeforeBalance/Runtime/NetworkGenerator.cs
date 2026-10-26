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
        public const int Version = 3;

        public static GeneratedNetwork Generate(Contract job,Career career) => Generate(job,career,GameCatalog.Default);
        public static GeneratedNetwork Generate(Contract job,Career career,GameCatalog catalog)
        {
            var definition=catalog.Contract(job.id);
            if(definition==null)throw new InvalidOperationException("Unknown network contract: "+job.id);
            var template=catalog.Network(job.networkId??definition.network.id);
            var lore=job.lore!=null&&job.lore.Length>0?job.lore:catalog.DescribeContract(definition,career).lore;
            var random=new System.Random(job.seed);
            var context=catalog.Context(career,job);context.Random=new System.Random(job.seed);
            var result=new GeneratedNetwork();
            foreach(var prototype in template.nodes)
            {
                var node=new NetNode{id=prototype.id,label=prototype.label,typeId=prototype.type.id,kind=prototype.type.glyph,color=prototype.type.color,detail=string.IsNullOrEmpty(prototype.detail)?prototype.type.description:prototype.detail,
                    x=prototype.x,y=prototype.y,column=prototype.column,row=prototype.row,position=prototype.position,known=prototype.known,controlled=prototype.controlled,bonus=prototype.bonus};
                context.Node=node;float security=prototype.type.security.Evaluate(context);
                foreach(var modifier in prototype.securityModifiers.OrderBy(m=>m.priority))if(modifier.when.Test(context))security=RuleMath.Apply(modifier.operation,security,modifier.value.Evaluate(context));
                node.security=Mathf.RoundToInt(security);
                if(prototype.loreIndex>=0)node.loreId=prototype.loreIndex<lore.Length?lore[prototype.loreIndex].id:null;
                result.nodes.Add(node);
            }
            NetNode Find(string id)=>result.nodes.First(n=>n.id==id);
            foreach(var objective in definition.objectives)Find(objective.nodeId).objective=true;
            if(template.layout==NetworkLayout.Authored)
            {
                foreach(var edge in template.links)if(edge.when.Test(context))result.links.Add(new NetLink(edge.a,edge.b));
                return result;
            }
            var config=template.routes;
            int difficulty=Mathf.Clamp(job.difficulty,1,config.tiers.Length);
            var tier=config.tiers[difficulty-1];int count=tier.nodeCount;
            void Type(NetNode node,NodeTypeDefinition type){node.typeId=type.id;node.kind=type.glyph;node.color=type.color;node.detail=type.description;}
            void Add(string id,NodeTypeDefinition type,string label)
            {var node=new NetNode{id=id,label=label,security=1};Type(node,type);result.nodes.Add(node);}
            for(int i=0;i<config.extraIds.Length;i++)Add(config.extraIds[i],config.extraTypes[i],config.extraLabels[i]);
            while(result.nodes.Count<count)Add(config.fillerPrefix+result.nodes.Count,config.relay,config.fillerLabel+(result.nodes.Count-config.fillerLabelOffset));
            foreach(var node in result.nodes)node.bonus=false;
            string[] archives=config.archives;
            for(int i=0;i<archives.Length;i++){var node=Find(archives[i]);node.bonus=true;node.loreId=i<lore.Length?lore[i].id:null;}
            var edges = new HashSet<string>();
            void Link(string a, string b)
            {
                var key = string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
                if (a != b && edges.Add(key)) result.links.Add(new NetLink(a, b));
            }
            var reserved=new HashSet<string>(config.reserved);
            // Shuffling the available identities varies topology while retaining contract identities.
            var pool = new Queue<NetNode>(result.nodes.Where(n => !reserved.Contains(n.id)).OrderBy(_ => random.Next()));
            var lengths=config.tiers.Select(t=>t.lengths).ToArray();
            float rotation = (float)random.NextDouble() * Mathf.PI * 2;
            Find(template.entryNode).position=config.entryPosition;
            Find(config.entrance).position=config.entrancePosition;
            Find(config.destination).position=config.destinationPosition;
            Link(template.entryNode,config.entrance);
            for (int lane = 0; lane < lengths[difficulty - 1].Length; lane++)
            {
                int length = lengths[difficulty - 1][lane];
                var route = new List<string> {template.entryNode,config.entrance};
                for (int step = 0; step < length; step++)
                {
                    var node = lane == 0 ? Find(config.directRoute[step]) : pool.Dequeue();
                    // Speed comes from fewer systems; risk comes from persistent scanner exposure.
                    if (lane > 0)
                    {
                        context.Node=node;context.Step=step;context.Lane=lane;context.SafeLane=lane==lengths[difficulty-1].Length-1;
                        Type(node,config.workstationWhen.Test(context)?config.workstation:config.relay);
                        node.label = (lane == lengths[difficulty - 1].Length - 1 ? config.safeLaneLabel : config.transitLabel) + " " + lane + "." + (step + 1);
                        node.security=Mathf.RoundToInt(config.routeSecurity.Evaluate(context));
                    }
                    else
                    {
                        context.Node=node;context.Step=step;context.Lane=lane;context.SafeLane=false;
                        node.security=Mathf.RoundToInt(config.directSecurity.Evaluate(context));
                    }
                    node.detail=catalog.NodeType(node.typeId).description;
                    float t = (step + 1f) / (length + 1), angle = rotation + lane * Mathf.PI * 2 / lengths[difficulty - 1].Length;
                    float radius=config.radius+lane*config.laneRadius;
                    node.position = new Vector3(Mathf.Lerp(config.entrancePosition.x,config.destinationPosition.x,t), Mathf.Cos(angle) * radius * Mathf.Sin(t * Mathf.PI),
                        Mathf.Sin(angle) * radius * Mathf.Sin(t * Mathf.PI));
                    node.position += new Vector3(Jitter(random), Jitter(random), Jitter(random)) * config.jitter;
                    Link(route.Last(), node.id); route.Add(node.id);
                }
                Link(route.Last(),config.destination);route.Add(config.destination); result.approaches.Add(route);
            }
            // Sensor leaves cannot become accidental traversal shortcuts. All four observe the fast ingress.
            string[] sensors=config.sensors;
            for (int i = 0; i < sensors.Length; i++) Link(config.sensorAttachment,sensors[i]);
            // Every secondary contract target has an approach from each independent lane.
            foreach (string id in config.secondaryTargets)
                foreach (var route in result.approaches) Link(route[route.Count - 2], id);
            // Crossovers connect only intermediate lanes, preserving the quiet and exposed extremes.
            if(result.approaches.Count>config.crossoverToLane+1)Link(result.approaches[config.crossoverFromLane][config.crossoverStep],result.approaches[config.crossoverToLane][config.crossoverStep]);
            for (int lane = 1; lane < result.approaches.Count - 1; lane++)
                for (int i = 0; i < tier.sensorsPerLane; i++)
                {
                    var sensor = pool.Dequeue(); Type(sensor,config.scanner);
                    sensor.label = config.sensorLabel+" " + lane + "." + (i + 1);
                    sensor.detail=config.scanner.description;
                    Link(result.approaches[lane][2], sensor.id);
                }
            for (int i = 0; i < archives.Length; i++)
            {
                var route = result.approaches[(i + 1) % result.approaches.Count];
                Link(route[2 + i % (route.Count - 3)], archives[i]);
            }
            while (pool.Count > 0)
            {
                var node = pool.Dequeue(); Type(node,config.workstation); node.security = 1;
                node.detail=config.workstation.description;
                var route = result.approaches[1 + random.Next(result.approaches.Count - 1)];
                Link(route[2 + random.Next(route.Count - 3)], node.id);
            }
            // Place satellites around their connected sector, with deterministic minimum spacing.
            var placed = result.nodes.Where(n => n.position != Vector3.zero || n.id==config.entrance||n.id==config.destination).ToList();
            foreach (var node in result.nodes.Except(placed).ToArray())
            {
                var edge = result.links.First(l => l.a == node.id || l.b == node.id);
                var parent = Find(edge.a == node.id ? edge.b : edge.a);
                for (int attempt = 0; attempt<config.placementAttempts; attempt++)
                {
                    var offset = new Vector3(Jitter(random), Jitter(random), Jitter(random));
                    node.position = parent.position + offset.normalized * (config.satelliteRadius+attempt*config.satelliteGrowth);
                    if (placed.All(n => Vector3.Distance(n.position, node.position) >=config.minimumSpacing)) break;
                }
                placed.Add(node);
            }
            // Separate close neighbors without changing their sector or topology.
            for (int pass = 0; pass<config.separationPasses; pass++)
                for (int i = 0; i < result.nodes.Count; i++)
                    for (int j = i + 1; j < result.nodes.Count; j++)
                    {
                        var delta = result.nodes[j].position - result.nodes[i].position;
                        if (delta.magnitude >=config.separationSpacing) continue;
                        var push = (delta.sqrMagnitude < .0001f ? Vector3.up : delta.normalized) * (config.separationSpacing-delta.magnitude)*config.separationForce;
                        result.nodes[i].position -= push; result.nodes[j].position += push;
                    }
            return result;
        }

        private static float Jitter(System.Random random) => (float)random.NextDouble() * 2 - 1;

        // Version 1 saves deliberately remain on the old filename. No live network migration.
    }
}
