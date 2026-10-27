using System;
using UnityEngine;

namespace HackThePlanet
{
    public enum NetworkLayout { Authored, ParallelRoutes }
    [Serializable] public sealed class NetworkNode
    {
        public string id, label, detail;
        public NodeTypeDefinition type;
        public int column, row;
        public float x,y;
        public Vector3 position;
        public bool known, controlled, bonus;
        public int loreIndex = -1;
        public Modifier[] securityModifiers = Array.Empty<Modifier>();
    }
    [Serializable] public sealed class NetworkEdge
    {
        public string a,b;
        [SerializeReference] public Condition when = new AlwaysCondition();
    }
    [Serializable] public sealed class RouteTier
    {
        public int nodeCount;
        public int[] lengths = Array.Empty<int>();
        public int sensorsPerLane = 1;
    }
    [Serializable] public sealed class ParallelRouteLayout
    {
        public string entrance, destination, sensorAttachment;
        public string[] directRoute = Array.Empty<string>(), sensors = Array.Empty<string>(), secondaryTargets = Array.Empty<string>(), archives = Array.Empty<string>();
        public string[] reserved = Array.Empty<string>();
        public RouteTier[] tiers = Array.Empty<RouteTier>();
        public NodeTypeDefinition relay, workstation, scanner, archive;
        public string[] extraIds = Array.Empty<string>();
        public string[] extraLabels = Array.Empty<string>();
        public NodeTypeDefinition[] extraTypes = Array.Empty<NodeTypeDefinition>();
        public string fillerPrefix="extra-", fillerLabel="Maintenance relay ", safeLaneLabel="Maintenance", transitLabel="Transit", sensorLabel="Transit sensor";
        public int fillerLabelOffset=24;
        public Vector3 entryPosition=new Vector3(-19,-1,-2), entrancePosition=new Vector3(-14,0,0), destinationPosition=new Vector3(14,0,0);
        public float radius=8, laneRadius=1.5f, jitter=.45f, satelliteRadius=3.5f, satelliteGrowth=.025f, minimumSpacing=2.6f, separationSpacing=2.65f, separationForce=.51f;
        public int placementAttempts=1000, separationPasses=80, crossoverFromLane=1,crossoverToLane=2,crossoverStep=4;
        [SerializeReference] public Number directSecurity = new ConstantNumber(2);
        [SerializeReference] public Number routeSecurity = new ConstantNumber(1);
        [SerializeReference] public Condition workstationWhen = new AlwaysCondition();
    }
    [CreateAssetMenu(menuName="Hack The Planet/Network")]
    public sealed class NetworkDefinition : GameplayDefinition
    {
        public string entryNode, preferredNode, previewNode;
        public NetworkLayout layout;
        [SerializeReference] public Number payloadStorage=new ConstantNumber(20), archiveStorage=new ConstantNumber(28);
        public NetworkNode[] nodes = Array.Empty<NetworkNode>();
        public NetworkEdge[] links = Array.Empty<NetworkEdge>();
        public ParallelRouteLayout routes = new ParallelRouteLayout();
    }
}
