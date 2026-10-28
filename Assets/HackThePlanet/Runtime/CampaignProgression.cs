using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace HackThePlanet
{
    [Serializable] public sealed class StoryOption { public string id, title, description, flag; }
    [Serializable] public sealed class EvidenceAnswer { public string id, title, feedback; public bool correct; }
    [Serializable] public sealed class EvidenceCase { public string title, question; public string[] clueNodes = Array.Empty<string>(), clues = Array.Empty<string>(); public EvidenceAnswer[] answers = Array.Empty<EvidenceAnswer>(); }
    [Serializable] public sealed class ResearchInvestment { public string id; public int paid; }
    [Serializable] public sealed class ContractOffer { public string id; public int openedDay; }
    public sealed partial class GameSimulation
    {
        public bool ContractAvailable(ContractDefinition d) => d != null && Career.completed.Count >= d.requiredWins && d.prerequisites.All(Career.completed.Contains) && d.requiredFlags.All(Career.storyFlags.Contains) && (d.recurring || !Career.completed.Contains(d.id)) && !OfferExpired(d);
        public bool OfferExpired(ContractDefinition d) => d.offerDays > 0 && Career.offers.Any(o => o.id == d.id && Career.day >= o.openedDay + d.offerDays);
        public string Availability(ContractDefinition d)
        {
            if (Career.completed.Contains(d.id)) return "DELIVERED";
            if (OfferExpired(d)) return "OPPORTUNITY CLOSED";
            if (!d.requiredFlags.All(Career.storyFlags.Contains)) return "UNRESOLVED CHOICE / OTHER ALLIANCE";
            if (!d.prerequisites.All(Career.completed.Contains)) return "AWAITING PREVIOUS CONTRACT";
            var offer = Career.offers.FirstOrDefault(o => o.id == d.id);
            return d.offerDays > 0 && offer != null ? "AVAILABLE / " + (offer.openedDay + d.offerDays - Career.day) + " DAYS LEFT" : "AVAILABLE";
        }
        public void RefreshOffers()
        {
            foreach (var d in Catalog.Data.contracts.Where(d => d.offerDays > 0 && d.prerequisites.All(Career.completed.Contains) && d.requiredFlags.All(Career.storyFlags.Contains)))
                if (!Career.offers.Any(o => o.id == d.id)) Career.offers.Add(new ContractOffer { id = d.id, openedDay = Career.day });
        }
        public ContractDefinition PendingChoice => Catalog.Data.contracts.FirstOrDefault(d => d.choices.Length > 0 && Career.completed.Contains(d.id) && !Career.resolvedChoices.Contains(d.id));
        public bool ChooseStory(string optionId)
        {
            if (Active) return false;
            var pending = PendingChoice; var option = pending?.choices.FirstOrDefault(o => o.id == optionId);
            if (option == null) return false;
            Career.storyFlags.Add(option.flag); Career.resolvedChoices.Add(pending.id);
            Career.news.Insert(0, option.title + " / " + option.description); RefreshOffers(); Touch(); return true;
        }
        public bool EvidenceSolved => !Active || Definition.evidence == null || Definition.evidence.answers.Length == 0 || Counter("case-solved") > 0;
        public string EvidenceFeedback { get; private set; }
        public bool SolveEvidence(string answerId)
        {
            var evidence = Active ? Definition.evidence : null;
            if (evidence == null || !evidence.clueNodes.All(id => Node(id)?.collected == true)) return false;
            var answer = evidence.answers.FirstOrDefault(a => a.id == answerId); if (answer == null) return false;
            EvidenceFeedback = answer.feedback;
            if (answer.correct) { AddCounter("case-solved", 1); Log("CASE RECONSTRUCTED // " + answer.title); }
            Touch(); return answer.correct;
        }
        public float WatcherTrace(string action, string target) => action != "hack" || !Active ? 0 :
            Defenses.Where(d => d.kind == DefenseKind.Watcher && d.targetNodeIds.Contains(target) && !d.disabled && Node(d.sourceNodeId)?.controlled == false && Node(d.sourceNodeId).suppressed <= 0)
            .Sum(d => d.traceBurst * Mathf.Max(.2f, 1 + Stat("watcherTrace")));
        private void ArmPurges(string action, string target)
        {
            if (Catalog.Action(action)?.tags.Contains("download") != true) return;
            foreach (var d in Defenses.Where(d => d.kind == DefenseKind.PurgeTimer && !d.disabled && !d.keyAcquired && d.targetNodeIds.FirstOrDefault() == target))
            { d.keyAcquired = true; Log("PURGE ARMED // " + d.remaining.ToString("0") + "s. Secure the mirror or capture/suppress the controller."); }
        }
        private void AdvancePurge(float delta)
        {
            foreach (var d in Defenses.Where(d => d.kind == DefenseKind.PurgeTimer && d.keyAcquired && !d.disabled).ToArray())
            {
                if (d.targetNodeIds.All(id => Node(id)?.collected == true) || Node(d.sourceNodeId)?.controlled == true) { d.disabled = true; continue; }
                if (Node(d.sourceNodeId)?.suppressed > 0) continue;
                d.remaining -= delta;
                if (d.remaining <= 0) { Log("PURGE COMPLETE // Linked record destroyed. Prepare both routes or neutralize the controller before retrying."); Disconnect(true); return; }
            }
        }
        public string PurgeStatus
        {
            get
            {
                var purges = Defenses.Where(d => d.kind == DefenseKind.PurgeTimer).ToArray();
                return string.Join(" / ", purges.Select(d => d.disabled || Node(d.sourceNodeId)?.controlled == true ? "PURGE COUNTERED" :
                    d.keyAcquired ? "PURGE: " + Mathf.CeilToInt(d.remaining) + "s" + (Node(d.sourceNodeId)?.suppressed > 0 ? " / SUPPRESSED" : " / SECURE MIRROR OR CONTROLLER") :
                    "PURGE: " + Mathf.CeilToInt(d.intervalSeconds) + "s after first linked recovery"));
            }
        }
        private HashSet<string> TechnologyRefundSet(string id)
        {
            var remove = new HashSet<string>();
            if (!Has(id) || Catalog.Data.career.initialTechnologies.Any(t => t.id == id)) return remove;
            remove.Add(id); bool changed;
            do { changed = false; foreach (var t in Catalog.Data.technologies.Where(t => Has(t.id))) if (!remove.Contains(t.id) && t.parents.Any(p => remove.Contains(p.id))) changed |= remove.Add(t.id); } while (changed);
            return remove;
        }
        public ResearchRefundPreview PreviewTechnologyRefund(string id)
        {
            var remove = TechnologyRefundSet(id); var original = Career.unlocked;
            try
            {
                Career.unlocked = original.Where(key => !remove.Contains(key)).ToList();
                var parts = Career.rigParts.Where(p => !ModuleRequirementsMet(Module(p.modelId))).ToArray();
                var preview = new ResearchRefundPreview { xp = Career.investments.Where(i => remove.Contains(i.id)).Sum(i => i.paid), credits = parts.Sum(p => p.paidPrice), copies = parts.Length, soldInstances = parts.Select(p => p.instanceId).ToArray() };
                preview.description = "Refund " + preview.xp + " XP from: " + string.Join(", ", remove.Select(key => Catalog.Technology(key).title)) + ". Sell " + preview.copies + " incompatible module copies for " + preview.credits + " cr. Check your rig before connecting.";
                return preview;
            }
            finally { Career.unlocked = original; }
        }
        public bool RefundTechnology(string id)
        {
            if (Active) return false;
            var remove = TechnologyRefundSet(id); if (remove.Count == 0) return false;
            var paid = Career.investments.Where(i => remove.Contains(i.id)).Sum(i => i.paid);
            Career.investments.RemoveAll(i => remove.Contains(i.id));
            Career.unlocked.RemoveAll(remove.Contains); Career.equipped.RemoveAll(remove.Contains); Career.xp += paid; Career.researchSpent -= paid;
            foreach (var part in Career.rigParts.Where(p => !ModuleRequirementsMet(Module(p.modelId))).ToArray()) { Career.credits += part.paidPrice; Career.rigParts.Remove(part); }
            Touch(); return true;
        }
    }
}
