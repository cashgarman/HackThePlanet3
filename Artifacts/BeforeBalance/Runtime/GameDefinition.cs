using System;
using UnityEngine;

namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Game",fileName="Game")]
    public sealed class GameDefinition : GameplayDefinition
    {
        public CompanyDefinition[] companies = Array.Empty<CompanyDefinition>();
        public ContractDefinition[] contracts = Array.Empty<ContractDefinition>();
        public TechnologyDefinition[] technologies = Array.Empty<TechnologyDefinition>();
        public ActionDefinition[] actions = Array.Empty<ActionDefinition>();
        public NodeTypeDefinition[] nodeTypes = Array.Empty<NodeTypeDefinition>();
        public NetworkDefinition[] networks = Array.Empty<NetworkDefinition>();
        public HardwareDefinition[] hardware = Array.Empty<HardwareDefinition>();
        public StatusDefinition[] statuses = Array.Empty<StatusDefinition>();
        public RewardDefinition rewards;
        public CareerDefinition career;
        public Modifier[] modifiers = Array.Empty<Modifier>();
        public Trigger[] triggers = Array.Empty<Trigger>();
    }
}
