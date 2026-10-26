using System;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable] public sealed class RewardRule
    {
        [SerializeReference] public Condition when = new AlwaysCondition();
        [SerializeReference] public Number credits = new ConstantNumber(0);
        [SerializeReference] public Number xp = new ConstantNumber(0);
    }
    [CreateAssetMenu(menuName="Hack The Planet/Rewards")]
    public sealed class RewardDefinition : GameplayDefinition
    {
        public int archiveCredits = 120, archiveXp = 15, archiveXpCap = 30, quietCredits = 150, quietXp = 10;
        public RewardRule[] rules = Array.Empty<RewardRule>();
    }
}
