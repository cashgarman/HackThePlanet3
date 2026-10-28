# Playtest Lab

In Unity Play mode or a development player, open **PLAYTEST LAB** on the main hub.

Choose a single contract or a fresh career, a strategy, and a starting seed/count (up to 100). Contract trials use funded, isolated build fixtures. Custom builds accept comma-separated research IDs in prerequisite order, equipped special IDs, and module IDs. Invalid builds appear as failed trials with an explanation.

**Run seed set** executes bounded work across frames. Select a completed seed to replay it on the network, then use Play/Pause, Step, speed, and Fit. Replays start paused. Completed replays compare decisions, outcome, time, trace, and rewards with the batch result. Switching hub tabs pauses the player operation; playtest state is separate from the player career.

Direct favors objective progress and duration. Cautious favors trace costs and defensive abilities. Opportunist collects reachable archives when trace is low. The policy sees Easy full briefing intel, Medium topology, and Hard discovered nodes only. It uses legal simulation previews and actions. These heuristic agents can fail through poor exploration; a failure is evidence to inspect, not proof that a contract is impossible.

Fresh careers earn resources, purchase strategy research and rig upgrades, and progress through story jobs. They use the lowest available freelance tier when funding is needed or after a story failure, with two freelance jobs and one story retry per stage. A failed freelance job or exhausted story retry ends the run with a reason.

Session history allows comparisons across builds on matching scenarios, strategies and seeds. Findings highlight pacing, trace, failures and candidate dominance. Export writes JSON events/configuration and CSV run metrics under `Application.persistentDataPath/Playtests`. No history is automatically loaded after restarting.

Validation: **Hack The Planet > Balance > Validate Playtest Lab**. The local Editor command bridge also accepts `playtest-validate`, `playtest-ui`, and `build-playtest` (development player in `Builds/Windows-Playtest`). The existing career balance suite remains an independent regression policy.

## Career build archetypes

Career strategies now keep ordered upgrade plans across the whole run:

- Direct (Intrusion / Reset): fast hacking, spoofing, reset research, extra processing, Spoof ASIC and Accelerator.
- Cautious (Recon / Loop): reconnaissance, quiet tools, credential extraction and Loop Feed, then Veil Array, Isolation Bank and Quiet Filter.
- Opportunist (Recon / Archive): early Blueprint and data engineering, faster recovery and storage/transfer capacity, then Decoder Board, Storage Array and RAM Bank.

Research prerequisites are purchased before their descendants. Each currency follows its own priority list: the AI saves for the next research or rig target rather than spending that budget on lower priorities. Module placement is previewed using the actual rig rules, including rearrangement, rotation, power and cooling. Chassis expansion follows the catalog unlocks and prices, with no additional tier restrictions. If an expansion is affordable before its planned module, the AI buys the chassis capacity first and saves subsequent earnings for the module. Targets blocked by research or job unlocks do not prevent consideration of other eligible upgrades. Owned modules are reused. Ability priorities are reapplied after purchases, within the current chassis slot limit.

The event log includes purchases and their prices, equipment changes, missing XP/credits, blocked prerequisites, and unavailable rig installations. Policy version 3 identifies this career shopping behavior in exports. Fresh career simulations still use only earned resources; funded validation fixtures exercise late upgrades independently of early AI exploration failures.

## Career results

Each completed career has a **Career details** section beneath its seed result. It shows total successful contracts and failed attempts, the story/freelance split, chronological contract outcomes, research purchases, final equipped abilities, chassis purchases, module purchases, and the final installed rig (including starter parts). Resource totals distinguish earned rewards, spending, and remaining credits/XP.

Game time is the sum of simulated contract time. Wall-clock elapsed measures real time from creation to completion of that automated career, including scheduling delays and pauses. A fast batch therefore has a much shorter wall time than game time. Replays retain their own timing and do not replace the original batch measurement. These details are included in JSON, with summary columns in CSV. Run a new seed set to collect the new fields.
