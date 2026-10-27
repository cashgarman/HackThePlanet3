# Modding Hack The Planet

Gameplay and presentation settings are loaded from editable JSON at launch. You can change balance, add contracts, tools, research and networks, and adjust graph presentation without rebuilding the game. ScriptableObjects still exist as runtime containers; the original Unity assets are retained as historical migration sources and are not runtime defaults.

## Data locations

- **Unity:** `data/` beside `Assets`, `Packages`, and `ProjectSettings` in the Unity project root.
- **Windows player:** `data/` beside `Hack The Planet.exe`, alongside (not inside) `Hack The Planet_Data/`.

The game uses the application location, independently of its working directory. Keep the entire `data/` tree with a distributed player. Files are read once per launch or Play session. Restart to apply changes; there is no hot reload, asset fallback, or automatic reset of broken data. Missing or invalid data produces an initialization error with the directory, filename and property information. Gameplay and saving remain blocked until the files are fixed.

Back up `career-v3.json` and its recovery backup before changing content identities or topology; see [Saves](../README.md#saves). Renaming/removing referenced IDs can make an existing save incompatible. JSON content and career save JSON have separate purposes. The rig rework starts a new v3 career; v2 files remain untouched. No old-career conversion is provided. Removing IDs can also invalidate a v3 career.

## Edit with Unity

Open the project in **Unity 7000.0.0a11**, then choose **Hack The Planet → Content Editor**.

The top tabs cover Game, Companies, Contracts, Networks, Node Types, Actions, Technologies, Rig Rules, Chassis, Modules, legacy Hardware extensions, Statuses, Career, Rewards, Text Palette, Graph Visuals, and Graph Labels. Graph Visuals contains both the research/default and network profiles. Search the left list by title, ID or filename. Select an entry to edit its properties on the right; drag the divider to resize the panes. Category selection, search and pane width are remembered with the editor window.

Every property has an explanation. Reference pickers show the definition title and stable ID, filtered to the required type. Rule pickers choose among supported formulas, conditions and effects. Colors preserve HDR channels; positions use vector fields; enum, boolean, numeric and multiline-text fields use their corresponding controls. Existing stats, tags and local network IDs are offered where applicable, and text fields permit custom values. Expand array and rule foldouts to edit their children. Arrays support add, duplicate, remove and move controls. Changing a rule type replaces that subtree with the new type's defaults and supports Undo.

### Drafts, validation and saving

Edits are isolated working copies. Switching entries keeps drafts; Unity Undo/Redo operates on draft changes. Unsaved drafts are cached under `Library/ContentEditor/` for domain reload and editor restart recovery. This cache never updates authoritative JSON automatically and does not replace persistent history.

- **Save** writes the selected entry. **Save All** writes all changed entries, including structural changes to registrations and references.
- The **Revision name** field optionally names the revisions created by the next save. An unchanged save creates no revision.
- **Validate All** checks the proposed complete catalog and property-description coverage. Click an error to navigate to its entry/property where identifiable.
- **Revert Unsaved** restores the selected entry from the loaded baseline. For structural changes spanning entries, use **Reload Disk** to discard all drafts and reload the complete catalog.
- Closing a dirty window or entering Play mode offers Save All, Discard or Cancel. Stop Play mode before editing content. Saved content is picked up on the next Play session.

All saves validate the complete candidate catalog first. Invalid drafts cannot replace valid files. Malformed files remain listed with errors and an Open file button; fix those externally and reload. They are never silently regenerated from defaults.

If an external tool changes a file after the window loaded it, saving reports a conflict and presents the disk changes for review. Reload Disk to accept external changes and discard drafts, or explicitly choose the reviewed overwrite. Overwriting still captures the previous disk version in history.

### Create and manage entries

**New** creates a definition with a unique ID and filename. **Duplicate** copies the selected definition, retaining outgoing references and assigning a new ID and independent history. Complete required fields and validate before Save All. Singleton categories cannot be duplicated or deleted.

The editor updates `manifest.json` and the Game root's registrations for new or removed definitions. Move up/down changes authored category order. **Rename ID** changes the ID and its object references together; use Save All. **Rename file** changes the relative JSON path while keeping gameplay identity and history. IDs and filenames are separate concepts.

**Delete** lists incoming references and blocks deletion until they have been removed or reassigned. Save All records a recoverable deletion revision. Merely changing descriptive copy does not implement a mechanic: actions, conditions, formulas and effects determine behavior.

## Revision history

Choose **History** to inspect entry revisions, including deleted entries. History is a built-in local snapshot store; it does not require Git or make commits in your project repository.

The first changed save records the original disk version plus the new version. Each subsequent changed save records a revision with its timestamp, ID, parent, operation, content hash and transaction ID. Name a revision when saving, or edit its name in History afterward. Content snapshots remain available when a label changes.

Select a revision to see property-level changes against the previous revision; toggle **Raw JSON comparison** for the complete before/after JSON. A marker identifies snapshots matching the current draft content. **Restore entry to draft** loads a snapshot for review. Validate and Save All to make that restore authoritative; this creates another revision and preserves all later history. To recover a deleted entry, select it in the entry dropdown and restore a revision from before deletion.

**Restore transaction to draft** restores the affected entries from a multi-entry operation, such as an ID rename. It does not replace the entire project with an old manifest. Restoring one entry does not automatically restore dependencies: use their histories to resolve missing references or ID/path collisions, then validate the combined draft.

Persistent history lives in **`.data-history/` beside `data/`**, outside Unity's `Library/` directory. It contains before/after transaction journals and revision snapshots. It is not shipped in player builds and is never automatically pruned. Include `.data-history/` in project backups or version control if history should travel to another machine. Save transactions are journaled before data replacement; reopening the editor rolls back an interrupted save to its previous files.

## Edit JSON directly

`data/manifest.json` has `schemaVersion: 1` and an ordered `entries` array. Every entry contains:

| Field | Purpose |
| --- | --- |
| `key` | Immutable authoring identity: a 32-character UUID without hyphens; retained across ID/file renames |
| `category` | Category name, such as `Actions` or `Technologies` |
| `file` | Relative JSON path inside `data/`, using forward slashes |
| `type` | Registered definition type, such as `ActionDefinition` |

Definitions live in category folders, one file per definition. The Game file contains ordered ID lists and references for registration. When adding a file manually, add its manifest entry **and** its corresponding Game registration. Unlisted files are ignored. Do not put backup copies in the manifest. Manifest and root ordering are meaningful; modifier arrays retain their order for equal priorities.

All properties emitted in the shipped files are required, including empty arrays and null optional references. Keep them when editing. Unknown properties, duplicate JSON properties, unsupported schema/type names, missing references and invalid rule graphs fail validation. Paths must remain inside `data/`; linked directories and traversal paths are rejected.

Definition references are ID strings, not Unity GUIDs. IDs must be unique within their definition type. Network node IDs are local to the template. Enums use their exact names; vectors use `x`, `y`, `z` components; colors use numeric `r`, `g`, `b`, `a`, including HDR values where appropriate.

Numeric, condition and effect subtrees use registered rule names, for example:

```json
{
  "type": "BinaryNumber",
  "operation": "Multiply",
  "left": { "type": "InputNumber", "input": "Security", "key": null, "filter": { "type": "AlwaysCondition" } },
  "right": { "type": "ConstantNumber", "value": 0.7 }
}
```

This is a numeric subtree, not a complete definition file. Use the populated files as templates for complete definitions. New mechanics requiring an additional rule operation, runtime input, procedural generator or UI layout still require C# and a new player build. External JSON cannot instantiate arbitrary CLR types or execute scripts.

## Rig, storage, and RAM authoring

The shipped catalog has four chassis and sixteen module models. `data/rig/rules/rig.json` defines the initial chassis and starter layout, sale fractions, reclaim rate, rotation policy, compute and bandwidth caps, and minimum trace fraction. `data/rig/chassis/` defines grid dimensions, career gates, prices, ability slots, power/cooling limits, base storage (GB), and RAM (GB). `data/rig/modules/` defines every purchasable model, including all power and cooling tiers.

A module's `width` and `height` describe its unrotated footprint in cells. Rotation swaps those dimensions. `family` identifies its gameplay group; `branch` supplies its research colour. Each purchase creates a separate career-owned instance. Repeated models can be installed together. `price` is the purchase cost; voluntary sales return the configured fraction of the actual price paid, rounded down. Free starter copies sell for zero. Research refunds sell newly locked copies at `researchSaleFraction` and clear their placements. Other owned copies remain in inventory.

Power Supply I/II/III and Cooling I/II/III are separate models, not upgrades to existing copies. Total power limit is the chassis base plus installed `powerCapacity`; cooling limit likewise adds installed `coolingCapacity`. Installed `powerDemand` must fit the power limit; installed `heatOutput` must fit cooling. Equality is valid. Cooling consumes power, and power supplies produce heat. Budgets are static; invalid budgets block connecting but permit rearrangement. Connecting also requires an installed Uplink (model `uplink`). Missing storage and processing modules are shown as warnings and do not block connecting.

Module `requirements` use the same typed `ResearchCondition` and other requirements as actions. `modifiers` use existing formulas and operations. In a module modifier, `InputNumber` with input `RigNeighborCount` reads adjacent installed copies matching `key`: a model ID, family, branch, or empty for all. `RigNeighborCondition` provides the same match using `kind` and a `minimum`. Orthogonal edge contact counts; diagonal contact does not. A multi-cell neighbor counts once. The Storage Array demonstrates a bounded count using `Minimum`, and Quiet Filter and Recon Cache demonstrate conditional synergies. Copy these files to add models without changing C#; register the new files in the manifest and Game root.

In the Content Editor, the **Rig** tab contains **Rig Rules**, **Chassis**, and **Modules**. Filter modules by family/tier, inspect the footprint, and use **Rotate footprint preview** to inspect the alternate orientation. Rig Rules previews the starter layout and its power/cooling totals. Typed references, nested modifier/condition editors, Undo, drafts, history, duplication, ID renaming, and validation apply to rig content just as to actions and technologies.

### Mission files

Contract objectives have a `payload` flag. Set it only when the objective downloads a file to the rig. Remote alteration, erase, and control tasks leave it false, even if they reuse the legacy download tool internally. Required payloads cannot be discarded. Objectives are excluded from optional archive rewards.

Each network defines `payloadStorage` and `archiveStorage` formulas. They may read seeded security and contract difficulty. Sizes round to nonnegative integer GB after evaluation and remain fixed for the run; later security changes do not resize files. The shipped payload formula caps individual files at 26 GB so the largest three-file objective fits the bare 80 GB starting chassis. The balance validator checks all contracts and sampled seeds/difficulties. Optional archives can exceed available space.

The download action's requirement compares `FreeStorage` with `NodeStorage`. Collected files occupy space until disconnect. `discard.json` is an instant targeted action that clears `Collected` only on optional archives. This removes their pending payout/lore and permits redownloading; rewards count final held files once. Its visibility and effects remain editable content. Storage inputs include `NodeStorage`, `StorageUsed`, `FreeStorage`, and `StorageCapacity`. The shipped download duration is file size divided by the `bandwidth` stat (GB over GB/s). Nodes with no file (`NodeStorage` of zero, such as remote alteration or erase tasks) fall back to the authored 5.5 or 4 unit duration for objectives or archives, divided by `bandwidth`.

### Ability RAM

Actions define `memoryMode` (`None`, `OneShot`, `Persistent`) and nonnegative `memoryCost` in GB. A one-shot moves RAM into the reclaiming bucket when execution begins; it returns at the modified `memoryReclaim` rate in GB/s per simulation second. Cancellation does not refund it. Persistent actions reserve RAM, apply `activeModifiers`, and keep their associated statuses active until disabled. Cancelling unfinished activation releases its reservation. `releaseOnTargetControl` releases a targeted reservation when the target becomes permanently controlled. Pause freezes reclamation.

Free RAM is pool minus reserved and reclaiming RAM. Chassis ability slots separately limit the equipped loadout. Decoy is an operator toggle with an authored trace modifier; Blackout keeps its suppression status while reserving RAM. Backdoor, Bridge, and Disrupt remain one-shots. RAM inputs include `MemoryFree`, `MemoryPool`, `MemoryReserved`, and `MemoryReclaiming`. Every disconnect outcome clears per-run RAM and storage.

### Generated Unity assets and end-user mods

**Hack The Planet → JSON → Generate Gameplay Assets**, or **Generate Assets** in the Content Editor, creates matching ScriptableObjects under `Assets/HackThePlanet/GeneratedGameplay/`. Save JSON drafts first. Generation validates the saved catalog, preserves asset GUIDs using immutable manifest keys, and remaps all definition references. It also runs before builds. Generated Inspectors are read-only and link back to the Content Editor. Historical assets stay separate.

**Generated assets do not control player mods.** The player reads external `data/` JSON at launch and never requires asset regeneration. End users can edit balance or add supported definitions beside the executable and restart without Unity. The `--validate-content <report-path>` launch option validates installed JSON without opening or writing a career. Generated files are editor/build mirrors, not a runtime fallback or an override.

## First edit: stronger Efficient hack

In the Content Editor, select **Technologies → Efficient hack**. Expand Modifiers, then Element 0. Keep Stat `duration`, Operation `Multiply`, and the Hack-only `ActionCondition`. Change its ConstantNumber Value from `0.8` to `0.7`: the duration reduction changes from 20% to 30%.

Keep `{modifier:0:reduction}` in its description so the displayed percentage follows the actual modifier. Save with a revision name such as `30 percent efficient hack`. Restart Play mode, purchase the technology, and compare hack forecasts for the same target and hardware. Use History to restore the previous tuning.

Without Unity, edit the same value in `data/technologies/efficient.json` beside the built executable, then restart the player. No rebuild is required.

## Add a small mission and Audit tool

Use these settings in the Content Editor; keep other defaults unless specified. Save All after the complete set passes validation.

1. **Node Types → New:** set ID through Rename ID to `my-mod-audit-terminal`, Title `Audit terminal`, Glyph `Workstation`, Security `ConstantNumber` value `1`, and Tags containing `my-mod-audit-target`. Choose a color; leave Auras empty.
2. **Actions → New:** use ID `my-mod-audit`, Title `Verify sample`, Initially Available on, Operator Target off, Queueable on, Duration `2`, Base Trace `1`, Use Limit `0` (unlimited), using ConstantNumber rules. Optionally assign an unused shortcut such as `V`.
3. Set action Visible to AllCondition containing a NodeTagCondition for `my-mod-audit-target` and a NotCondition around NodeStateCondition `Collected`. Add execution Requirements for Known and Controlled, with useful messages. Add a queue requirement for Known. Add a SetStateEffect with Targets Scope `Target`, Filter AlwaysCondition, State `Collected`, Value on.
4. **Networks → New:** use ID `my-mod-field-network`, Layout `Authored`, Entry Node `entry`, Preferred Node `alpha`, Preview Node `beta`. Add the nodes below. Leave Lore Index `-1` and Bonus off. Add links `entry`–`alpha` and `alpha`–`beta`, both with AlwaysCondition.
5. **Contracts → New:** use ID `my-mod-field-contract`, Title `Field investigation`, an existing Company, and your new Network. Leave Recurring off and Required Wins `0`. Set ConstantNumber formulas Credits `111`, XP `7`, Difficulty `1`, Seed `99`. Fill in client and brief text.
6. Add one objective: Node ID `beta`, Label `Verify the sample`, Action Label `Verify sample`, Action your Audit tool, Complete NodeStateCondition `Collected`. Leave Action Requirements, Action Rules and Lore empty. Describe bonuses consistently with the shared Rewards policy.

| Node ID | Label | Type | Position | Known / Controlled |
| --- | --- | --- | --- | --- |
| `entry` | Entry relay | Existing gateway | `(-6, 0, 0)` | On / On |
| `alpha` | Access terminal | Your Audit terminal | `(0, 0, 0)` | On / Off |
| `beta` | Sample terminal | Your Audit terminal | `(6, 0, 0)` | On / Off |

Restart with a fresh test career. Connect to Field investigation, hack alpha and beta, verify the sample, then extract. Check settlement, report and save/reload. To gate Audit behind research, disable Initially Available and create a technology granting it; Special technologies must also be equipped.

## Verification and distribution

- **Hack The Planet → JSON → Validate Data** validates the files on disk.
- **Run Content and History Validation** checks asset parity, rule round trips, descriptions, reference changes, revisions, conflicts and recovery using an isolated `Artifacts/` sandbox.
- Existing Simulation, Data Driven, Balance and Workstation Smoke validations now use JSON-backed defaults. Intentional balance/topology mods can fail shipped baseline expectations; investigate and update relevant fixtures rather than removing unrelated checks.
- Windows build callbacks validate and copy the manifest-listed data tree beside the executable, replacing stale output data. Distribute the executable, generated `_Data` directory, DLLs and **data/** together. History and editor drafts are not packaged. Test a relocated player as well as Play mode.

The [gameplay rules reference](DATA_DRIVEN_GAMEPLAY.md) explains formulas, stats, progression and saves. The [balance notes](BALANCE_REFINEMENT.md) explain the shipped tuning. Meshes, shaders, stylesheets and Unity rendering configuration remain project assets; player accessibility/preferences remain separate from mod content.
