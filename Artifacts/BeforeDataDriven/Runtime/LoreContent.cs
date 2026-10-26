using System;

namespace HackThePlanet
{
    [Serializable]
    public sealed class LoreFragment
    {
        public string id, title, company, contract, text;
    }

    public static class LoreContent
    {
        private static readonly string[][] Titles = {
            new[] { "The missing appendix", "Lab notebook, 03:17", "A public promise" },
            new[] { "Rounding errors", "The second signature", "A letter never sent" },
            new[] { "Authorized traffic", "A borrowed key", "Someone answered" },
            new[] { "Beneficial owners", "The backup lane", "An unsigned dedication" },
            new[] { "Before the freeze", "Mirror instructions", "The name on the list" },
            new[] { "Night shift", "The targeting index", "After the signal" }
        };
        private static readonly string[][] Texts = {
            new[] {
                "HELIX / INTERNAL REVIEW: The prototype passes every efficiency benchmark. Publication remains suspended until the licensing team establishes which improvements can be removed from the public submission.",
                "Lark's lab notes: We built the first working cell from scavenged parts. The technician who fixed the cooling loop still takes two buses home. Management says affordable energy would disrupt our market position.",
                "Draft press release: Energy belongs to everyone. Revision note: Legal requests replacement of 'everyone' with 'approved commercial partners'. The original author declined to initial the change." },
            new[] {
                "MORROW / INCIDENT 441: Duplicate balances correlate exactly with the migration date. Customer support has filed nineteen matching reports. The classification remains 'individual payment dispute'.",
                "Ledger maintenance memo: Both primary and mirror require correction. An automated reconciliation job restores whichever record carries the older authorization stamp. A single correction will disappear overnight.",
                "Unsent message from a collections clerk: I can see the charge twice. I can see the payment once. The script tells me to say the account is accurate. I am keeping copies of everything." },
            new[] {
                "AEGIS / SERVICE TICKET: The community network outage was recorded as authorized load testing. No request came from the network's owners. Approval originated in a Morrow procurement account.",
                "Audit excerpt: The same maintenance credential crossed three systems in eleven seconds. The credential belongs to an engineer whose access was revoked six months ago. Someone kept the key active.",
                "Recovered radio transcript: NULL asked if anyone could still hear the neighborhood relay. Twelve operators answered. By dawn, the clinic and the night school were back online." },
            new[] {
                "Ownership schedule: Helix's research subsidiary pays a holding company for exclusive deployment rights. That holding company has no staff. Its sole voting shareholder is Aegis Systems.",
                "Retirement plan for legacy storage: Decommissioning postponed again. The backup lane carries compliance snapshots that the current board cannot delete without countersignature. Someone quietly renewed its certificate.",
                "An early prototype carries a handwritten dedication: For the people who keep the lights on. The production specification omits the inscription. The archive preserves a photograph." },
            new[] {
                "MORROW / PRIORITY ORDER: Freeze the account at 06:00, before the hearing. The attached risk assessment contains no financial analysis; it lists the whistleblower's recent contacts instead.",
                "Mirror operator's instructions: Confirm the replacement order in both stores. If the signatures agree, the automated review accepts the correction before the next batch. Do not leave a contradictory copy.",
                "The whistleblower's last draft: They have a list of everyone who asked questions. I thought leaving the company would get me off it. Now I know why the surveillance contract was worth more than the bank." },
            new[] {
                "AEGIS / NIGHT OPERATIONS: Operators are instructed to treat missing telemetry as hostile activity. A handwritten note underneath reads: Some people simply turn off their phones. That is still allowed.",
                "Index schema commentary: Risk scores include association with public-interest researchers, debt appeals, and community infrastructure. The system was never looking for a crime. It was looking for connections.",
                "Buffered message from NULL: When the index goes quiet, leave the relays running. People will need those connections tomorrow. We can build something better on the wires they left behind." }
        };
        private static readonly string[][] Freelance = {
            new[] { "A Helix technician has hidden repair manuals inside routine compliance backups. The official service program charges a year's wages for the same instructions.", "An abandoned Helix test site still transmits weather readings. Local growers use the open feed to protect their crops. An engineer keeps renewing the relay budget.", "Helix's discarded patent register credits a community workshop with the original design. The attribution survived in the archive after it disappeared from the product page." },
            new[] { "Morrow's reconciliation team keeps a private tally of corrected accounts. Each entry is a household that no longer has to call the help line.", "A branch manager quietly waived fees during the outage. Central accounting demanded an explanation. The response was a photograph of the flooded street.", "An old Morrow training guide says a ledger is a promise to remember accurately. Someone has highlighted that sentence on every surviving copy." },
            new[] { "An Aegis contractor logged an objection to a surveillance request. The objection was denied, but the timestamp proves someone knew before deployment.", "Aegis lists this relay as retired. Its maintenance channel now carries volunteer dispatches, clinic appointments, and messages home.", "A security analyst left a note in the archive: Protection means giving people somewhere safe to go. It does not mean following them there." }
        };

        public static LoreFragment For(Contract job, int index)
        {
            int story = Array.FindIndex(GameContent.Story, c => c.id == job.id);
            int company = Math.Max(0, Array.IndexOf(GameContent.Companies, job.company));
            return new LoreFragment {
                id = story >= 0 ? job.id + "/" + index : "freelance/" + company + "/" + index,
                title = story >= 0 ? Titles[story][index] : new[] { "Maintenance testimony", "An unofficial record", "What the archive kept" }[index],
                company = job.company, contract = job.title,
                text = story >= 0 ? Texts[story][index] : Freelance[company][index]
            };
        }

        public static LoreFragment Find(Contract job, string id)
        {
            for (int i = 0; i < 3; i++) { var fragment = For(job, i); if (fragment.id == id) return fragment; }
            return null;
        }
    }
}
