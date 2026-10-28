# Vesper campaign (career v4)

The active catalog replaces the original six-contract campaign. New careers use `career-v4.json`; v3 files and backups are never opened or overwritten by the new game.

The first contract is **Ghost Meter**. Completing four opening jobs opens a choice between Rhea, Sable and Orin. Each alliance has five middle jobs, a dedicated threshold operation and two possible final contracts. Two shared crisis jobs connect the middle arc to the threshold. Each completed route contains 13 story operations; the catalog contains 30 story definitions, nine optional jobs and four repeatable templates.

Optional jobs open when their named prerequisite succeeds, then close after three career days. Every settled attempt advances one day, including an abort or isolation. Gear and research persist. Main contracts and repeatable work do not expire. Choices commit once, survive reload and disclose which alliance they open.

## Operations

| Pattern | Required play |
| --- | --- |
| Exfiltrate | Secure a source and recover its payload |
| Linked alteration | Synchronize authoritative and mirror records; the first write arms a purge |
| Reconstruction | Recover three signed clues and choose the interpretation consistent with all three |
| Interception | Recover a transfer and its receipt inside the linked purge window |
| Evacuation | Capture a response controller before opening the protected identity channel |
| Seizure | Capture the controller and recover its command record |

Six authored subnet families with 43 mission-specific variants have fast monitored ingress, maintenance ingress, cross-links, controller detours and optional archives. Seeded security and a conditional cross-link vary the legal cost and route choices without removing either main ingress. The core extraction action is available without research; research and equipped tools add alternatives or efficiency.

Watchers add a visible surcharge when hacking monitored ingress nodes. Maintenance routes avoid those nodes; controller suppression or capture removes the surcharge. Purges arm on the first linked recovery/write. Securing both linked records or capturing the controller ends the timer. Suppression pauses it, and ordinary game pause freezes it. A purge expiry ends the attempt and leaves the contract retryable.

Evidence answers require all named clues. Wrong answers give feedback and cost no trace. A correct answer is stored in operation counters. Every story success banks its debrief in Intelligence even if no optional archive was collected.

## Research and rig

The tree contains a free Field Kit, five nodes in each of four disciplines, four waypoint tiers and four crossovers. Branch prices are 45/70/95/125/160 XP; waypoint prices are 35/65/95/125; crossovers are 140/160/180/200. Special abilities still need a chassis slot. Selective refunds remove descendants, return actual paid XP, and return the paid price of incompatible modules. A confirmation is required before applying the change.

The 22 module models include two new specializations: Watcher Baffle reduces monitored ingress trace; Mirror Core improves transfer bandwidth. The four chassis cost 0/1,200/2,800/5,400 credits and offer 2/3/4/5 ability slots. Starting credits plus the first 700-credit base payout can buy the second chassis or several first-tier modules. Purchase cards compare power and heat load and show footprint and requirements.

## Verification and pacing

Run `scripts/run-balance.ps1` for the Vesper campaign suite. The suite carries base-payout resources through all six ending paths over 20 seeds for each of stock and researched careers (240 careers and 3,120 story stages), and checks guaranteed debriefs and save restoration. Unit tests cover exclusive choices, deadlines, failure costs, evidence, Watcher forecasts, purge counterplay and paid selective refunds. Live workstation tests exercise the new panels through actual UI callbacks.

Automated timings describe active simulation, excluding reading and planning. The targets are introductory medians of 90–120 seconds, middle medians of 150–240, finale medians of 240–300, and hard-job p90 at or below 360 seconds. First-time 15–20 minute operations and a 6–8 hour playthrough remain **human playtest targets**. Automated completion cannot establish those targets or prove that the mystery is understood.

The original catalog is preserved in `Docs/LegacyV3/catalog.zip` for regression fixtures. `scripts/build_vesper_catalog.py` is the deterministic authoring source for the initial Vesper catalog; running it rebuilds the authored categories. Normal balancing and modding use the active JSON and Content Editor.

## Story spoilers

The Night Index is presented first through a meter certificate, missing dispatch minutes and an erased worker. The fourth job names the protocol without revealing its full purpose. The alliance arcs expose different dependencies: public evidence, independent service routes, and signing authority. MOTH is a shared identity rather than a single reliable handler. The shared crisis reveals that the Directorate funded parts of the resistance and scored the player as a successor. No central server exists: each alliance attacks a different dependency. Final choices favor immediate reach or protection of named people, producing six valid resolutions without a hidden perfect ending.
