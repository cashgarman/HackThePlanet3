using System;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable] public sealed class CareerRank { public int jobs; public string title; }
    public enum OperationOutcome { Delivered, Aborted, Caught }
    [Serializable] public sealed class OutcomeRule
    {
        public OperationOutcome outcome;
        public string verdict;
        [TextArea] public string narrative;
        public bool bankRewards, recordCompletion, bankLore;
        public int dayAdvance;
    }
    [CreateAssetMenu(menuName="Hack The Planet/Career Rules")]
    public sealed class CareerDefinition : GameplayDefinition
    {
        public int credits = 600, xp, day = 1, reportLimit = 20, logLimit = 100;
        public bool researchDuringOperations, hardwareDuringOperations, equipDuringOperations;
        public float refundFraction = 1, traceLimit = 100, traceWarning = 60, traceDanger = 85, maxTick = .5f;
        public string welcome;
        public OutcomeRule[] outcomes = {
            new OutcomeRule { outcome=OperationOutcome.Delivered,verdict="CONTRACT COMPLETE",narrative="{aftermath}",bankRewards=true,recordCompletion=true,bankLore=true,dayAdvance=1 },
            new OutcomeRule { outcome=OperationOutcome.Aborted,verdict="CONTRACT ABORTED",narrative="You disconnected before securing every required file. Your equipment and research are safe; the contract remains available." },
            new OutcomeRule { outcome=OperationOutcome.Caught,verdict="CONNECTION ISOLATED",narrative="The defender isolated your session. Equipment and research are safe. The contract can be retried with a different route." }
        };
        public float[] simulationSpeeds = {1,2,4};
        public TechnologyDefinition[] initialTechnologies = Array.Empty<TechnologyDefinition>();
        public NamedStat[] stats = Array.Empty<NamedStat>();
        public CareerRank[] ranks = Array.Empty<CareerRank>();
    }
}
