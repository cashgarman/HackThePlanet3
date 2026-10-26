# Modding Hack The Planet

Hack The Planet supports content authoring through Unity ScriptableObject assets: you can change balance, add contracts and networks, and compose tools and research from existing rules without editing C#. This guide walks through that workflow. The [gameplay authoring reference](DATA_DRIVEN_GAMEPLAY.md) explains formulas, stats, effects, saves, and runtime APIs in more detail.

## What you need

Use the source project in **Unity 7000.0.0a11** (the version in `ProjectSettings/ProjectVersion.txt`). Open the folder containing `Assets`, `Packages`, and `ProjectSettings`.

The current player loads `Resources/HackThePlanet/Gameplay/Game` through `GameCatalog.Default`. There is no runtime mod-folder loader or external JSON content import. To distribute asset changes to players, build a player containing them. Editing a loose `.asset` file beside an existing executable does not load it into the game. Career JSON files store saves, not content definitions.

Before editing, commit or copy the assets you intend to change. Back up your `career-v2.json` and its recovery backup before testing incompatible content; the [README](../README.md#saves) gives the save location. Prefer a fresh test career when changing IDs or topology. Reset Game Save clears the current career and recovery backup, so back them up first.

## Find and register content

The shipped catalog root is:

`Assets/HackThePlanet/Resources/HackThePlanet/Gameplay/Game.asset`

Select it in the Project window to see the content arrays and the Career and Rewards references. Create assets with **Create → Hack The Planet → …** or duplicate a similar asset in Unity. Keep new assets under the gameplay folder so its asset-change hooks invalidate the default catalog cache.

| Definition | What to author | Registration in Game.asset |
| --- | --- | --- |
| Company | Identity and company presentation | Companies |
| Contract | Brief, company, network, objectives, rewards, lore | Contracts |
| Network | Authored topology or Parallel Routes settings | Networks |
| Node Type | Glyph, color, tags, security, defense auras | Node Types |
| Action | Targeting, requirements, timing, trace, effects | Actions |
| Technology | Research parents, cost, grants, modifiers, placement | Technologies |
| Hardware | Upgrade prices, tiers, requirements, stat modifiers | Hardware |
| Status | Timed behavior and modifiers | Statuses |
| Career Rules / Rewards | Starting resources, baseline stats, outcomes / settlement formulas | Career / Rewards references |

Every definition needs a stable, unique `Id`. Duplicating an asset also duplicates this field: immediately replace it for new content, for example `my-mod-field-contract`. Register each new definition in its corresponding root array, including definitions referenced by another asset. Merely creating an asset does not register it. Existing assets that you edit in place retain their registration.

Use Unity object fields for references between definitions. Network node IDs are local to the network and are strings used by links and objectives. Keep Unity `.meta` files with assets when sharing changes; those GUIDs preserve object references. Avoid hand-editing managed-reference YAML; use the Inspector's rule type picker and expand the rule to edit its fields.

## First edit: make Efficient hack stronger

1. Exit Play mode. Select `TechnologyDefinition-efficient.asset` in the gameplay folder.
2. Expand **Modifiers → Element 0**. The shipped modifier targets `duration`, uses `Multiply`, and applies only to the Hack action through an `ActionCondition`.
3. Expand **Value**, keep its `ConstantNumber` type, and change `0.8` to `0.7`. This changes the duration reduction from 20% to 30%. Keep its condition so the reduction continues to apply only to hacks.
4. Keep the description placeholder `{modifier:0:reduction}`; it displays the value from that modifier. Save the asset.
5. Run **Hack The Planet → Validate Gameplay Assets**, then restart Play mode. Purchase Efficient hack and compare a hack forecast before and after purchase on the same target with the same hardware. A running action keeps the costs captured when it started.

This modifies an existing technology, so do not add a second root entry or change its ID. Undo the value change to restore the shipped tuning.

## Add a small mission and a new tool

This example reuses Hack for access and adds an Audit action that collects a sample. It follows the asset-only extension pattern in `Assets/HackThePlanet/Editor/DataDrivenValidation.cs` (`CreateFixture`). All assets below are created in Unity and registered in `Game.asset`.

### 1. Create a node type

Create **Node Type**, set Id to `my-mod-audit-terminal`, Title to `Audit terminal`, Glyph to `Workstation`, and choose a color. Add the tag `my-mod-audit-target`. Set Security to `ConstantNumber` with Value `1`; leave Auras empty.

### 2. Create the Audit action

Create **Action** with Id `my-mod-audit` and Title `Verify sample`. Configure:

| Field | Value |
| --- | --- |
| Operator Target | Off (this action targets a node) |
| Queueable | On |
| Initially Available | On for this tutorial |
| Shortcut / Shortcut Label | `V` / `V`; choose another unused key if necessary |
| Duration / Base Trace / Use Limit | `ConstantNumber`: `2` / `1` / `0` (unlimited uses) |
| Visible | `AllCondition` containing `NodeTagCondition` with tag `my-mod-audit-target`, and `NotCondition` wrapping `NodeStateCondition` with State `Collected` |
| Requirements | Two entries: `NodeStateCondition` for `Known`, message `Discover the target.`; and `NodeStateCondition` for `Controlled`, message `Control this terminal first.` |
| Queue Requirements | One entry: `NodeStateCondition` for `Known`, message `Discover the target.` |
| Effects | One `SetStateEffect`: Targets Scope `Target`, Filter `AlwaysCondition`, State `Collected`, Value on |

Requirements guard execution; visibility controls whether the tool is offered. The collected-state visibility check removes the completed action from the target. The effect assigns the same state the contract will check for completion.

To unlock the tool through research instead, turn Initially Available off, create a Technology with its own ID, cost, title, graph Position and a parent reference to the starting `base` technology, and add Audit to its Grants. Register the technology too. Keep Special off for a passive unlock; Special on requires the player to equip the technology in a loadout slot before its grants apply.

### 3. Create an authored network

Create **Network** with Id `my-mod-field-network`, Layout `Authored`, Entry Node `entry`, Preferred Node `alpha`, and Preview Node `beta`. Populate Nodes:

| Id | Label | Type | Position (X, Y, Z) | Known | Controlled |
| --- | --- | --- | --- | --- | --- |
| `entry` | Entry relay | Existing `gateway` node type | `(-6, 0, 0)` | On | On |
| `alpha` | Access terminal | Your Audit terminal | `(0, 0, 0)` | On | Off |
| `beta` | Sample terminal | Your Audit terminal | `(6, 0, 0)` | On | Off |

Leave Bonus off and Lore Index at `-1` on each node. Add two Links: A `entry`, B `alpha`; and A `alpha`, B `beta`. Keep each link's When rule as `AlwaysCondition`. These connections allow progression from controlled access to the sample. Parallel Routes fields are unused for an Authored layout.

### 4. Create the contract

Create **Contract**, set Id to `my-mod-field-contract`, Title to `Field investigation`, Company to an existing company, and Network to your new network. Fill in the client and brief text. Set Recurring off and Required Wins to `0` so it is available in a fresh career. Use `ConstantNumber` formulas for Credits `111`, XP `7`, Difficulty `1`, and Seed `99`. Difficulty 1 gives full pre-operation intel and a ×1 base reward multiplier.

Add one Objectives entry:

- Node Id: `beta`.
- Label: `Verify the sample`; Action Label: `Verify sample`.
- Action: your Audit action.
- Complete: `NodeStateCondition` with State `Collected`.
- Leave Action Requirements empty; the action already requires control.

Leave Action Rules and Lore empty for this tutorial. Write Condition text that matches the bonus policy if you describe bonuses: settlement still uses the catalog's shared Rewards definition. The displayed brief text does not implement a reward rule.

Register the new node type, action, network and contract in their root arrays, then validate. Restart Play mode with a fresh test career, find Field investigation, connect, hack `alpha`, hack `beta`, select Verify sample, and extract after it finishes. Confirm the report and settlement. If you chose research gating, purchase (and equip, if Special) the technology before connecting. Reload a saved attempt with the same catalog to check persistence too.

## Validate, build, and share

Start with **Hack The Planet → Validate Gameplay Assets** and a manual playthrough of your content. The validator catches missing registrations, duplicate IDs, invalid references, research cycles, and many invalid formulas; it does not establish that every mission is reachable or enjoyable.

The existing **Run Simulation Validation**, **Run Data Driven Validation**, and **Run Workstation Smoke Test** commands cover campaign behavior, rule composition, and UI integration. Some checks assert shipped values and captured baseline network hashes, so intentional balance or topology changes can fail those expectations. Inspect the failures and adapt the relevant fixtures to the intended design; keep checks for unrelated regressions. See [Verification](DATA_DRIVEN_GAMEPLAY.md#verification) and the [balance notes](BALANCE_REFINEMENT.md) for their scope.

Use **Hack The Planet → Build Windows Player** to create a player; invalid gameplay assets stop the build. Test the built player as well as Play mode. Distribute the executable with its generated `_Data` folder and accompanying DLLs. To share source changes, include the changed assets and their `.meta` files, the updated root registrations, any new C# rule types, and a short list of required base-project versions and save compatibility expectations.

## Troubleshooting and extension limits

| Symptom | Check |
| --- | --- |
| New content does not appear | Root registration, required wins, action visibility, research grants/equipment, then restart Play mode. Existing simulations retain a detached catalog snapshot. |
| Missing reference or unique-ID validation error | Register every referenced definition, replace IDs copied by duplication, and remove null root entries. |
| Action is visible but disabled | Execution and queue requirements, target control/discovery, charges, objective prerequisites, and loadout. |
| Objective never completes | Exact local node ID, assigned action, matching completion state/effect, and a traversable route to the target. |
| New save cannot load | Restore the matching content version. Saves reference definition IDs and active network state; renaming/removing IDs or changing topology may require a fresh career. |
| Formula fails | Check stat names, available context inputs, zero divisors, and rule/stat cycles. RandomNumber is for seeded generation, not action costs. |

Data can compose the existing `Number`, `Condition`, and `Effect` types in `GameplayRules.cs`. A mechanic requiring a new operation or input needs C# support, corresponding validation, and a rebuilt player. New glyph shapes, UI layouts, and another procedural network algorithm likewise require presentation or generator code. For a different fixed topology, use Authored networks before adding a generator.
