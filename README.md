# Hack The Planet

A playable single-player hacking career built in Unity. All systems are fictional, local network puzzles; no real network access is used by gameplay.

## Play

Run **`Builds/Windows/Hack The Planet.exe`**. Keep the executable with its `_Data` directory and the accompanying DLLs. The default window is 1680 × 960 and can be resized. A mouse and keyboard are recommended.

Choose **Ghost Meter**, then **Connect to network**. Select a known ingress system and use **Hack system** to establish your first foothold. Control connected systems to reach the gold contract objectives. Recover the files, then **Extract & deliver** to bank payment and XP. A short field guide is available from **?** or **F1**.

| Control | Action |
| --- | --- |
| Click a system | Inspect it and preview actions |
| 1 / 2 / 3 / 4 | Scan and prepare / Hack / Recover / Disrupt selected target |
| Space | Pause or resume the current operation |
| 1x / 2x / 4x buttons | Simulation speed |
| Mouse wheel over graph | Zoom |
| Left-drag graph | Orbit in 3D |
| Right- or middle-drag graph | Pan |
| FIT | Recenter the graph |
| Drag a rack part / R | Move a module / rotate the selected module |
| F1 or ? | Operator field guide |
| Escape or ⚙ button | Settings menu: text size, display effects, interface sounds, Resume, Reset Game Save, Quit |

## Career loop

The Vesper v4 campaign contains 30 authored story definitions across three divergent alliances, with 13 story operations on each route, nine optional contracts and four repeatable templates. Start with **Ghost Meter**. The first four jobs uncover the Night Index; your alliance determines the middle campaign and the final operation.

Research has 29 nodes. The physical rig has four chassis and 22 module models. Plan routes around credential locks, Watchers, sweeps and linked purge windows. Recover signed clues and reconstruct evidence cases. Main plot discoveries are banked automatically after successful delivery; optional archives add context and bonuses.

Failures retain equipment and research, lose unbanked rewards, and advance one career day. Optional offers can expire; main story jobs and repeatable work remain available. Choices and explicit contract prerequisites govern progression. See [Vesper campaign](Docs/VESPER_CAMPAIGN.md) for mechanics, balance targets and verification; its final section contains story spoilers.

## Saves

The game automatically saves your career and active operation every five seconds and at important transitions. Reopened operations start paused, including any running tool. Saves use atomic replacement and a previous-save backup.

Open Settings with **Escape** or the **⚙** button on the left rail. The menu pauses an active operation; **Resume** restores its previous pause state. Display effects and interface sounds are configured here as well. **Reset Game Save** asks for confirmation, then starts a fresh career and resets the recovery backup. **Quit** saves your career before closing the game.

Open **Settings → Cheats / Debug** to add **1,000,000 credits**, add **1,000,000 XP**, or **unlock the whole tech tree** for free. **Reset Game** uses the same confirmation as **Reset Game Save** and starts a fresh career (clears contracts, research, hardware, reports, and any active operation). Credit and XP grants are repeatable and XP also counts toward total earned XP. Changes autosave to the current career. Special abilities still use the normal Research loadout and slot limits. Escape returns to Settings; Resume restores the operation's previous pause state.

Text defaults to **200%**, twice the original size. In Settings, move **Text size** or enter a percentage from **100 to 300**. Changes apply immediately throughout the interface, including graph labels, tooltips, reports, and logs, and are remembered between launches independently of career resets. Panels wrap or scroll to keep larger text and controls accessible.

Windows save location: `%USERPROFILE%/AppData/LocalLow/Independent Operator/Hack The Planet/career-v4.json`.

Vesper careers use v4. Existing `career-v1.json`, `career-v2.json`, `career-v3.json`, and their backups remain untouched; no migration is performed.

## Unity project

Open this folder in **Unity 7000.0.0a11**. Enter Play mode in the included scene; the workstation bootstraps automatically. The dedicated scene is `Assets/HackThePlanet/Scenes/Workstation.unity`.

The included Graph Explorer package supplies the discovered graph document, controlled-route shortest paths, and the 3D node and edge renderer for both operations and research. Hidden world state stays in the game simulation. The workstation hosts the 3D views in its UI Toolkit layout and keeps its own inspectors and action controls.

Source files are under `Assets/HackThePlanet/Runtime/`. Launch-loaded JSON under project-root `data/` defines gameplay and presentation; built players load `data/` beside the executable. `ContentJson.cs` populates temporary ScriptableObjects and validates references; `GameCatalog.cs` compiles immutable catalogs; `GameSimulation.cs` owns tools, trace, progression, and persistence; `WorkstationApp.cs` and `GraphViewport.cs` present the workstation. Historical definition assets are retained as migration sources. Styles remain under `Assets/HackThePlanet/Resources/HackThePlanet/`.

`NetworkGenerator` builds seeded topology and stable XYZ positions together for previews and live operations. V2 active saves preserve their topology, positions, tools, queues, counters, and timed statuses. The seven original low-poly hardware meshes are game-owned resources; regenerate them with `scripts/generate_network_models.py` using Python 3. The importer keeps meshes readable for player-side instancing. Lore includes guaranteed story debriefs and three optional archive fragments per contract.

The **Ghost Frequency** graphics style uses mint phosphor, bracketed instrument cards, circuit routes with packet flow, a procedural signal field, and subtle phosphor glass. Display effects can be reduced to a static treatment or disabled from Settings. The [art-direction guide](Docs/GHOST_FREQUENCY_ART_DIRECTION.md) covers the palette, shader stack, motion settings, and rendering budget.

The **Hack The Planet** editor menu provides a **Content Editor** with category tabs, typed forms, descriptions, validation, and named per-entry revision history. It also provides scene setup, JSON and history validation, simulation and data-driven validation, a rendered workstation smoke test, and Windows builds. Builds reject invalid JSON and package the validated `data/` tree beside the player. The smoke test uses an isolated in-memory career and does not write to the player's career save.

To change balance or add content, follow the [modding guide](Docs/MODDING.md) for direct JSON editing, the Unity Content Editor, revision restoration, and save compatibility. Editing JSON beside a built player requires only a restart. Keep `data/` with the executable and include `.data-history/` in project backups if you want to preserve authoring revisions.

The [automated balance guide](Docs/BALANCE_AUTOMATION.md) covers credential locks, security sweeps, the expanded research/rig catalog, and repeatable career and upgrade measurements. The [earlier balance notes](Docs/BALANCE_REFINEMENT.md) retain the route-refinement comparison. Validation reports and screenshots are in `Artifacts/`. Expanded persistent footholds, independent extraction endpoints, and long-running investigations remain outside this career loop. Mission approaches branch through the network; entry and delivery still use your gateway.

## Physical rig

Your rig starts with a packed 2×3 chassis. Buy independent module copies, rotate and arrange rectangular footprints, and expand through four chassis tiers. Installed power supplies increase power capacity and produce heat; cooling modules increase cooling capacity and consume power. Both have three researched model tiers. Connections require a legal layout within both budgets. Removed parts stay in inventory; voluntary sales return 75% of the paid price.

Downloaded payloads and optional archives occupy drive space until disconnect. Discard optional archives to make room, forfeiting their pending rewards and lore. Abilities use RAM: toggles reserve capacity and one-shots temporarily occupy it until reclaimed. Pause freezes recovery. The rack, file sizes, RAM costs, and all 22 module models are editable through external JSON or the Content Editor; see [Modding](Docs/MODDING.md#rig-storage-and-ram-authoring).

The rig uses a fresh v4 career file and leaves old v3 careers untouched. Generated Unity assets are JSON-owned development mirrors; player mods still load directly from the external `data/` folder without Unity or regeneration.
