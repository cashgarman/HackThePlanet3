# Gameplay authoring

For setup, step-by-step balance and custom-mission tutorials, building, and save compatibility, start with the [modding guide](MODDING.md). This document is the rules and runtime reference.

The shipped rules live in the project-root `data/` directory, or beside the executable in a built player's `data/` directory. The versioned manifest registers one JSON file per definition. JSON includes companies, contracts, lore, networks, node roles, tools, research, rig rules, chassis, modules, statuses, rewards, career policies and presentation profiles. Runtime code supplies reusable operations; JSON selects and composes them into temporary ScriptableObjects. Historical `.asset` files remain as migration sources and do not supply runtime defaults.

## Edit or add content

Use **Hack The Planet → Content Editor** for typed editing, descriptions, references, validation and named per-entry revisions. New and Duplicate update manifest and root registrations; Save All commits structural changes together. You can also edit JSON directly. Definition references and saves use stable IDs; changing a filename or title does not change identity. Renaming or deleting an ID can invalidate a v3 save.

Rule fields have a type picker. Expand the selected rule to edit operands, conditions, selectors or effects. JSON uses explicit rule `type` discriminators, enum names and ID references. Validate All checks drafts; **JSON → Validate Data** checks disk files. The same shared validation runs before a player build; missing references and invalid rule graphs stop the build.

JSON loads once per launch or Play session and populates an owned graph of ScriptableObjects. These compile into a detached catalog; existing simulations retain their snapshot. Restart Play mode to apply saved changes. Missing/invalid data blocks initialization and saving instead of falling back to assets. Inject `new GameCatalog(gameDefinition)` to run an independent configuration.

### Actions and research

An action defines its target scope, display title, presentation priority, shortcut, visibility, requirements, queue requirements, duration, base trace, use limit, and effects. The workstation prioritizes usable actions, hides completed ones through authored visibility, and shows remaining charges and conservative trace forecasts. Zero uses means unlimited uses. Operator actions can have zero duration and run alongside a targeted tool. Actions marked initially available need no research grant; other actions are granted by technologies. A special technology must be unlocked **and equipped** to grant its actions or rules.

Use visibility for which targets should offer an action, requirements for execution, and queue requirements for prerequisites checked when enqueueing. Execution requirements are evaluated again when a queued action starts. Future-neighbor queueing is an authored action capability enabled by the `futureQueue` stat. Duplicate queued action/target pairs are rejected.

Effects include setting node state, revealing a selected region, changing trace or security, applying a timed status, updating counters, recovering lore, logging, and conditional effect groups. Triggers run at operation start, action completion, control establishment, or status expiration. A trigger's optional `onceKey` consumes a shared operation counter before running its effects. Rule graphs are validated for cycles; runtime evaluation and effect dispatch have additional depth and execution limits.

Technologies define prices, parent references, grants, modifiers, triggers, and graph placement. New technologies can reuse existing rules without a new C# branch. Their descriptions may use `{modifier:0:value}`, `{modifier:0:percent}`, or `{modifier:0:reduction}` to display the corresponding modifier value.

### Formulas, stats, and defenses

Numeric expressions support constants, runtime inputs, named stats, arithmetic, conditional expressions, and seeded integer ranges. Random expressions belong to network generation. Add custom baseline stats to Career Rules before referring to them. Modifiers support addition, subtraction, multiplication, division, minimum, maximum, and replacement. They run in ascending priority; equal priorities retain authored order.

The built-in stat inputs used by the engine are:

| Stat | Purpose |
| --- | --- |
| `queueCapacity`, `futureQueue` | Queue rules; ability slots come directly from the chassis |
| `storageCapacity`, `memoryPool`, `memoryReclaim` | Chassis/rig resource bases plus module modifiers |
| `compute` (GFLOP/s), `bandwidth` (GB/s) | Inputs referenced by the shipped action formulas |
| `scanRadius`, `revealRadius` | Inputs referenced by reveal effects |
| `minimumSecurity`, `security` | Minimum and effective target security |
| `duration`, `baseTrace` | Action cost modifiers |
| `tracePressure`, `trace`, `traceFree` | Environmental pressure, final trace modifiers, final zero-trace override |
| `traceGrowth`, `idleTracePerSecond` | Time-based trace scaling and idle trace |

Cost evaluation uses effective security, the action duration formula and modifiers, base trace modifiers, duration × environmental pressure, final trace modifiers, and then the terminal trace-free override. Execution captures duration when the action starts and evaluates trace during each step, so sustained Decoy changes take effect immediately. The authored trace floor limits stacked reductions, while explicit trace-free tools and authored zero base trace retain their exceptions. Timed statuses, RAM reclamation, and task progress freeze during pause.

Node types select their glyph, color, tags, initial security formula, and auras. An aura has an activation condition, optional adjacency restriction, modifier, and stacking policy. Scanner pressure and hub protection are authored auras. Node glyphs are presentation choices; gameplay predicates use tags and state.

### Networks and contracts

An **Authored** network contains arbitrary node IDs, positions, initial states, optional archives, and conditional links. Its entry node starts discovered and controlled. Contract objectives reference template-local node IDs and contain completion conditions, action labels, and optional prerequisites. Contract-wide action prerequisites are available for requirements such as controlling a hub before recovering files. A contract must have at least one objective.

The **Parallel Routes** generator preserves the current seeded 3D networks. Its asset defines route tiers and lengths, node counts, role references, reserved identities, direct routes, secondary targets, sensors, archives, security expressions, and placement settings. `Step`, `Lane`, and `SafeLane` are available to its formulas. `ControlledDistance` counts hops along discovered routes to controlled access; hidden links never satisfy range requirements. Changing seed retains deterministic generation for a fixed configuration. Use an Authored network when a different topology does not fit this generator.

Contract reward, XP, seed, and difficulty expressions can reference career day. Repeatable contracts use the same rule system as story contracts. Lore fragments live in contract definitions and are assigned through archive indices.

Difficulty values map to **Easy** (1 or below), **Medium** (2), and **Hard** (3 or above). These tiers control both offline intel and base delivery rewards. Easy previews expose all systems, types and objectives; Medium previews preserve geometry and connections but use one anonymous node type without ownership, objective emphasis, system names or a type legend; Hard previews instantiate no network renderer and display `NO INTEL AVAILABLE` over procedural static. Live operations always use normal discovered-node presentation and research reveal rules.

The `credits` and `xp` formulas author base amounts. Catalog descriptors apply the tier multiplier once (Easy ×1, Medium ×1.5, Hard ×2), rounding to whole credits and XP. Reward rules reading `ContractCredits` and `ContractXp` receive these final amounts, matching exchange cards and briefs. Archive and quiet bonuses retain their authored values. Repeatable contracts re-evaluate their difficulty and payout with the career day.

### Rig, rewards, and outcomes

The physical rig uses Rig Rules, Chassis, and Module definitions. Every bought module copy has an independent career instance, actual paid price, position, and rotation. Installed copies contribute modifiers, power demand, heat, and power/cooling capacities. Chassis upgrades preserve grid coordinates. Runtime connection checks require a legal assembly. The starter layout, sale policy, research gates, tiers, speed caps, trace floor, payload formulas, RAM costs, persistent effects, and reclaim rate are authored JSON; see the [rig authoring guide](MODDING.md#rig-storage-and-ram-authoring).

Module rules use `RigNeighborCondition` and `RigNeighborCount` with a model ID, family, branch, or empty selector. Shared edges count, diagonals do not, and multi-cell neighbors count once. Storage and RAM inputs expose live capacities and usage. Objectives mark actual downloaded payloads; remote tasks use no storage. Optional archives can be discarded, while required payloads remain held until disconnect. Persistent ability statuses are associated with reservations, and one-shot RAM reclaims over simulation time. All per-run resources reset on every outcome.

Legacy Hardware definitions and ID-based upgrade adapters remain supported for extension content, but the shipped hardware catalog is empty. Chassis fields control the actual ability equip cap; the retained legacy `slots` stat is not that cap.

Rewards are conditional formulas over contract values, recovered archives, trace, and the quiet threshold. Archive and quiet bonus amounts are shared inputs, so payout text and settlement read the same values. Career Rules also define starting resources and research, refund fraction, rank thresholds, trace thresholds, simulation speeds, and retention limits.

Delivered, aborted, and caught outcomes each have an authored policy controlling the verdict, narrative, reward banking, completion, lore banking, and day advancement. The default failure policies preserve gear and research and leave the contract retryable.

## Runtime interfaces and saves

Create `GameSimulation(catalog, career)` for an injected configuration. Actions use `AvailableActions`, `Preview`, `CanQueue`, and `Execute`, with a target ID omitted for operator actions. Rig interfaces include `BuyModule`, `PreviewModule`, `TryPlaceModule`, `PlaceFirstFree`, `RotateModule`, `RemoveModule`, `SellModule`, `PreviewChassis`, and `UpgradeChassis`. Research refunds expose `PreviewResearchRefund` before atomically applying module sales. `RigProblems` explains connection blockers.

Careers use **career-v3.json**. V1 and V2 files remain untouched, with no migration. Saves include the chassis, owned module instances, active file sizes, RAM reclamation and reservations, nodes, links, tasks, queues, counters, statuses, reports, and lore. Research tracks actual XP investment; owned modules track actual paid credits. Restored operations start paused. Unknown references are reported without overwriting the save. Corrupt JSON can recover from an atomic backup; otherwise autosave remains blocked until reset.

## Verification

- **Hack The Planet → Validate Gameplay Assets** and **JSON → Validate Data** check the JSON catalog.
- **JSON → Run Content and History Validation** checks generated-asset parity, property descriptions, codecs, references, history, conflicts and interrupted-save recovery in an isolated sandbox.
- **Run Simulation Validation** checks the campaign, network seed sweep, lore, economy, saves, and reset behavior.
- **Run Data Driven Validation** additionally compares 600 captured network hashes, checks all existing research effects, rejects invalid rule graphs, and exercises an injected configuration with a new action, technology, node type, repeated module copies, and four-node contract.
- **Run Workstation Smoke Test** exercises actual UI callbacks and authored shortcuts for shipped content and the injected configuration. Batch runs render the UI into a texture and verify nonblank pixels before writing captures.
- **Rig → Validate Complete Rework** checks packing, budgets, tiers, storage, RAM toggles and recovery, editor callbacks, external JSON extensions, generated asset parity, stable GUIDs, and payload fit across all contracts.
- **JSON → Generate Gameplay Assets** refreshes JSON-owned Unity mirrors without changing player mod loading.
- **Build Windows Player** validates JSON, generates asset mirrors, and packages external `data/` beside the executable.

**Balance → Validate Refined Career** also measures 600 build/route/seed combinations and checks preparation, ability limits, cancellation, research refunds, and early build affordability. Reports and captures are written to `Artifacts/`. The captured pre-refactor fixture is `Artifacts/gameplay-baseline.json`. The default import utility is an editor-only conversion aid: it refuses to overwrite an existing root and never supplies runtime fallback data.
