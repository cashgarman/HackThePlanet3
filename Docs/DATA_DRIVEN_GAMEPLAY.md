# Gameplay authoring

For setup, step-by-step balance and custom-mission tutorials, building, and save compatibility, start with the [modding guide](MODDING.md). This document is the rules and runtime reference.

The shipped rules live in `Assets/HackThePlanet/Resources/HackThePlanet/Gameplay/Game.asset`. Its 54 assets contain the current companies, contracts, lore, network generation, node roles, tools, research, hardware, statuses, rewards, and career policies. Runtime code supplies reusable operations; these assets select and compose them.

## Edit or add content

Use **Create → Hack The Planet** in the Project window. Give a definition a stable ID and add its reference to the corresponding array in **Game.asset**. References between assets use Unity object fields; saves use IDs. Changing a filename or display title does not change identity. Renaming or deleting an ID can invalidate a v2 save.

Rule fields have a type picker. Expand the selected rule to edit its operands, conditions, selectors, or effects. Inspectors show catalog errors and provide **Validate Gameplay Catalog**. The same validation runs before a player build; missing references and invalid rule graphs stop the build.

Assets are compiled into a detached catalog. An existing simulation keeps that snapshot. Inspector edits and asset imports invalidate the default cache for the next simulation; restart Play mode to apply a full content change to the workstation. Inject `new GameCatalog(gameDefinition)` to run an independent configuration.

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
| `slots`, `queueCapacity`, `futureQueue` | Loadout and queue rules |
| `hackSpeed`, `transferSpeed` | Inputs referenced by the shipped action formulas |
| `scanRadius`, `revealRadius` | Inputs referenced by reveal effects |
| `minimumSecurity`, `security` | Minimum and effective target security |
| `duration`, `baseTrace` | Action cost modifiers |
| `tracePressure`, `trace`, `traceFree` | Environmental pressure, final trace modifiers, final zero-trace override |
| `traceGrowth`, `idleTracePerSecond` | Time-based trace scaling and idle trace |

Cost evaluation uses effective security, the action duration formula and modifiers, base trace modifiers, duration × environmental pressure, final trace modifiers, and then the terminal trace-free override. Execution captures duration and trace when the action starts. Timed statuses and task progress freeze during pause.

Node types select their glyph, color, tags, initial security formula, and auras. An aura has an activation condition, optional adjacency restriction, modifier, and stacking policy. Scanner pressure and hub protection are authored auras. Node glyphs are presentation choices; gameplay predicates use tags and state.

### Networks and contracts

An **Authored** network contains arbitrary node IDs, positions, initial states, optional archives, and conditional links. Its entry node starts discovered and controlled. Contract objectives reference template-local node IDs and contain completion conditions, action labels, and optional prerequisites. Contract-wide action prerequisites are available for requirements such as controlling a hub before recovering files. A contract must have at least one objective.

The **Parallel Routes** generator preserves the current seeded 3D networks. Its asset defines route tiers and lengths, node counts, role references, reserved identities, direct routes, secondary targets, sensors, archives, security expressions, and placement settings. `Step`, `Lane`, and `SafeLane` are available to its formulas. `ControlledDistance` counts hops along discovered routes to controlled access; hidden links never satisfy range requirements. Changing seed retains deterministic generation for a fixed configuration. Use an Authored network when a different topology does not fit this generator.

Contract reward, XP, seed, and difficulty expressions can reference career day. Repeatable contracts use the same rule system as story contracts. Lore fragments live on contract assets and are assigned through archive indices.

Difficulty values map to **Easy** (1 or below), **Medium** (2), and **Hard** (3 or above). These tiers control both offline intel and base delivery rewards. Easy previews expose all systems, types and objectives; Medium previews preserve geometry and connections but use one anonymous node type without ownership, objective emphasis, system names or a type legend; Hard previews instantiate no network renderer and display `NO INTEL AVAILABLE` over procedural static. Live operations always use normal discovered-node presentation and research reveal rules.

The `credits` and `xp` formulas author base amounts. Catalog descriptors apply the tier multiplier once (Easy ×1, Medium ×1.5, Hard ×2), rounding to whole credits and XP. Reward rules reading `ContractCredits` and `ContractXp` receive these final amounts, matching exchange cards and briefs. Archive and quiet bonuses retain their authored values. Repeatable contracts re-evaluate their difficulty and payout with the career day.

### Hardware, rewards, and outcomes

Hardware definitions also have shared execution requirements; the shipped memory expansion requires an unlocked ability. `UnlockedAbilities` is a numeric condition input. Hardware tiers specify prices and cumulative modifiers. Add another tier or another hardware definition to extend both progression and the rig UI. Hardware levels are stored by ID.

Rewards are conditional formulas over contract values, recovered archives, trace, and the quiet threshold. Archive and quiet bonus amounts are shared inputs, so payout text and settlement read the same values. Career Rules also define starting resources and research, refund fraction, rank thresholds, trace thresholds, simulation speeds, and retention limits.

Delivered, aborted, and caught outcomes each have an authored policy controlling the verdict, narrative, reward banking, completion, lore banking, and day advancement. The default failure policies preserve gear and research and leave the contract retryable.

## Runtime interfaces and saves

Create `GameSimulation(catalog, career)` for an injected configuration. Use `AvailableActions(targetId)`, `Preview(actionId, targetId)`, `CanQueue(actionId, targetId)`, `Execute(actionId, targetId)`, and `Upgrade(hardwareId)`. Omit the target for operator actions. The older tool-enum and hardware-index overloads are compatibility adapters for existing fixtures; the workstation uses IDs and catalog metadata.

Research records the XP actually spent, so future price changes do not reduce a free refund. Older v2 careers recover their invested XP from earned and available balances, with an authored-cost fallback for injected fixtures. New careers use **career-v2.json**. Existing **career-v1.json** files remain untouched. V2 saves contain runtime IDs, nodes, links, tasks, queues, hardware levels, counters, timers, reports, and recovered lore. Restored operations start paused. Unknown references are reported without overwriting the save. Corrupt JSON can recover from an atomic backup; if recovery fails, autosave is blocked until the player explicitly resets the save.

## Verification

- **Hack The Planet → Validate Gameplay Assets** checks the authored catalog.
- **Run Simulation Validation** checks the campaign, network seed sweep, lore, economy, saves, and reset behavior.
- **Run Data Driven Validation** additionally compares 600 captured network hashes, checks all existing research effects, rejects invalid rule graphs, and exercises an injected configuration with a new action, technology, node type, hardware and tier, and four-node contract.
- **Run Workstation Smoke Test** exercises actual UI callbacks and authored shortcuts for shipped content and the injected configuration. Batch runs render the UI into a texture and verify nonblank pixels before writing captures.
- **Build Windows Player** validates assets before compiling the player.

**Balance → Validate Refined Career** also measures 600 build/route/seed combinations and checks preparation, ability limits, cancellation, research refunds, and early build affordability. Reports and captures are written to `Artifacts/`. The captured pre-refactor fixture is `Artifacts/gameplay-baseline.json`. The default import utility is an editor-only conversion aid: it refuses to overwrite an existing root and never supplies runtime fallback data.
