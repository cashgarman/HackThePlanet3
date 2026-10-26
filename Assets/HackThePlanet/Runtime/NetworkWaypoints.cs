using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable]
    public sealed class NetworkWaypoint
    {
        public string contractId, nodeId, label = "";
        public int seed, color;
        public bool showPath;
    }

    public sealed partial class GameSimulation
    {
        public static readonly Color[] WaypointColors = { new Color(0.2f, 1f, .9f), new Color(1f, .65f, .2f), new Color(.85f, .5f, 1f), new Color(1f, .4f, .6f), Color.white };
        public int WaypointTier => Mathf.Clamp(Catalog.Data.technologies.Where(t=>Has(t.id)).Select(t=>t.waypointTier).DefaultIfEmpty(0).Max(),0,4);
        public IReadOnlyList<NetworkWaypoint> Waypoints(Contract job) => Career.waypoints.Where(w=>w.contractId==job.id&&w.seed==job.seed).Take(WaypointTier).ToArray();

        // Planning is tied to the exact generated network, including recurring-contract seeds.
        public bool PlaceWaypoint(Contract job, string nodeId)
        {
            if(Active||WaypointTier==0||job==null||job.Intel==ContractIntel.None||Catalog.Contract(job.id)==null)return false;
            if(Career.completed.Contains(job.id)||Career.completed.Count<job.requiredWins)return false;
            var network=NetworkGenerator.Generate(job,Career,Catalog);
            if(!network.nodes.Any(n=>n.id==nodeId))return false;
            var existing=Career.waypoints.Where(w=>w.contractId==job.id&&w.seed==job.seed).ToArray();
            if(existing.Length>=WaypointTier||existing.Any(w=>w.nodeId==nodeId))return false;
            // Replace plans for an expired recurring network rather than accumulating them forever.
            Career.waypoints.RemoveAll(w=>w.contractId==job.id&&w.seed!=job.seed);
            Career.waypoints.Add(new NetworkWaypoint{contractId=job.id,seed=job.seed,nodeId=nodeId});Touch();return true;
        }

        public bool RemoveWaypoint(Contract job,string nodeId)
        {
            if(Active||job==null)return false;
            bool removed=Career.waypoints.RemoveAll(w=>w.contractId==job.id&&w.seed==job.seed&&w.nodeId==nodeId)>0;
            if(removed)Touch();return removed;
        }

        public bool EditWaypoint(Contract job,string nodeId,int color,string label,bool showPath)
        {
            if(Active||job==null||color<0||color>=WaypointColors.Length)return false;
            var waypoint=Waypoints(job).FirstOrDefault(w=>w.nodeId==nodeId);if(waypoint==null)return false;
            if(WaypointTier<2&&color!=waypoint.color||WaypointTier<3&&(label??"")!=waypoint.label||WaypointTier<4&&showPath!=waypoint.showPath)return false;
            waypoint.color=color;waypoint.label=(label??"").Trim();if(waypoint.label.Length>40)waypoint.label=waypoint.label.Substring(0,40);
            if(showPath)foreach(var other in Career.waypoints.Where(w=>w.contractId==job.id&&w.seed==job.seed))other.showPath=false;
            waypoint.showPath=showPath;Touch();return true;
        }

        public static GraphPathResult WaypointPath(List<NetNode> nodes,List<NetLink> links,string entry,string target)
        {
            var document=new GraphDocument("waypoint-route","Waypoint route");var schema=new GraphSchema();
            schema.Register(new GraphNodeTypeDefinition("system","System","default",Color.white));
            schema.Register(new GraphRelationDefinition("link","Connection",Color.white));
            foreach(var node in nodes)document.UpsertNode(new GraphNodeRecord(node.id,"system",""));
            foreach(var link in links)
            {
                document.UpsertEdge(new GraphEdgeRecord(link.a+">"+link.b,link.a,link.b,"link"));
                document.UpsertEdge(new GraphEdgeRecord(link.b+">"+link.a,link.b,link.a,"link"));
            }
            return GraphPathfinder.Find(new GraphScene(document,schema),entry,target);
        }
    }
}
