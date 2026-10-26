# Hack The Planet

A playable single-player hacking career built in Unity. All systems are fictional, local network puzzles; no real network access is used by gameplay.

## Play

Run **`Builds/Windows/Hack The Planet.exe`**. Keep the executable with its `_Data` directory and the accompanying DLLs. The default window is 1680 × 960 and can be resized. A mouse and keyboard are recommended.

Choose **The Quiet Copy**, then **Connect to network**. Select Public relay and use **Hack system** to establish your first foothold. Control connected systems to reach the gold contract objectives. Recover the files, then **Extract & deliver** to bank payment and XP. A short field guide is available from **?** or **F1**.

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
| F1 or ? | Operator field guide |
| Escape or ESC button | Settings menu: text size, Resume, Reset Game Save, Quit |
| SND | Toggle interface sounds |
| FX+ / FX− / FX0 | Cycle animated, reduced/static, and disabled display effects |

## Career loop

- Six linked story contracts across Helix Dynamics, Morrow Financial, and Aegis Systems: theft, synchronized record edits, evidence recovery, and a final disruption.
- Contract difficulty controls pre-operation intel: **Easy** shows the complete network and system types; **Medium** shows the complete topology with anonymous, identical unknown systems; **Hard** shows **NO INTEL AVAILABLE** over animated signal static. Connecting starts normal discovery on every tier. Reduced effects freeze the static; effects off retain the message on a plain background.
- Difficulty also scales the contract's base credits and XP: **Easy ×1**, **Medium ×1.5**, **Hard ×2**. The exchange and brief show the final delivery amounts; optional archive and quiet bonuses are added separately.
- 32, 44, or 56 systems per operation by difficulty, with three to five approaches through a volumetric 3D network. Short ingress routes expose you to scanners; longer maintenance routes trade time for lower trace. Three optional archive detours contain bonus rewards and authored lore. Hubs strengthen connected targets; scanners add trace while tools run nearby.
- No idle trace, unlimited pause, trace forecasts, a visible queue, tool cancellation, and retryable failures. Scanning prepares targets for 15% faster hacks and 20% less hack trace. Disconnect after completing every objective to deliver. A 100% trace ends the attempt and loses its unbanked rewards; hardware and purchased research persist.
- Eighteen connected research nodes including the starting toolkit, four branches, and crossover technologies requiring both parents. XP is spent directly. Refund all research for free between contracts. Passives are always active; special abilities occupy a limited loadout.
- Credits upgrade CPU, bandwidth, and ability capacity. Research and rig changes happen between operations.
- Success changes corporate defenses and publishes a world bulletin. Three repeatable freelance templates unlock after the third story contract, with defenses, connections, and rewards changing as your career advances.
- The Intelligence tab preserves the last twenty operation reports, including action timelines and trace at disconnect. Successfully extracted lore stays in a permanent collection independently of report retention.

All bonus conditions are shown before connecting. Optional archives pay +100 credits and +15 XP each; all three award XP, up to +45 per contract. Finishing below the contract's trace threshold adds +100 credits and +20 XP.

Early branch roots cost 45 XP. Bridge has two prepared crossings per operation; Backdoor and remote Blackout use discovered routes within three hops. Use **STOP** to cancel a running tool and clear its queue; trace already generated remains. **CLEAR** removes queued tools only.

## Saves

The game automatically saves your career and active operation every five seconds and at important transitions. Reopened operations start paused, including any running tool. Saves use atomic replacement and a previous-save backup.

Open Settings with **Escape** or the **ESC** button on the left rail. The menu pauses an active operation; **Resume** restores its previous pause state. **Reset Game Save** asks for confirmation, then starts a fresh career and resets the recovery backup. **Quit** saves your career before closing the game.

Open **Settings → Cheats / Debug** to add **1,000,000 credits**, add **1,000,000 XP**, or **unlock the whole tech tree** for free. **Reset Game** uses the same confirmation as **Reset Game Save** and starts a fresh career (clears contracts, research, hardware, reports, and any active operation). Credit and XP grants are repeatable and XP also counts toward total earned XP. Changes autosave to the current career. Special abilities still use the normal Research loadout and slot limits. Escape returns to Settings; Resume restores the operation's previous pause state.

Text defaults to **200%**, twice the original size. In Settings, move **Text size** or enter a percentage from **100 to 300**. Changes apply immediately throughout the interface, including graph labels, tooltips, reports, and logs, and are remembered between launches independently of career resets. Panels wrap or scroll to keep larger text and controls accessible.

Windows save location: `%USERPROFILE%/AppData/LocalLow/Independent Operator/Hack The Planet/career-v2.json`.

V2 careers start fresh. The previous `career-v1.json` and its backup remain untouched; no migration is performed.

## Unity project

Open this folder in **Unity 7000.0.0a11**. Enter Play mode in the included scene; the workstation bootstraps automatically. The dedicated scene is `Assets/HackThePlanet/Scenes/Workstation.unity`.

The included Graph Explorer package supplies the discovered graph document, controlled-route shortest paths, and the 3D node and edge renderer for both operations and research. Hidden world state stays in the game simulation. The workstation hosts the 3D views in its UI Toolkit layout and keeps its own inspectors and action controls.

Source files are under `Assets/HackThePlanet/Runtime/`. The gameplay definitions under `Resources/HackThePlanet/Gameplay/` contain contracts, networks, technologies, and mechanics; `GameCatalog.cs` compiles immutable runtime catalogs; `GameSimulation.cs` owns tools, trace, progression, and persistence; `WorkstationApp.cs` and `GraphViewport.cs` present the workstation. Styles are under `Assets/HackThePlanet/Resources/HackThePlanet/`.

`NetworkGenerator` builds seeded topology and stable XYZ positions together for previews and live operations. V2 active saves preserve their topology, positions, tools, queues, counters, and timed statuses. The seven original low-poly hardware meshes are game-owned resources; regenerate them with `scripts/generate_network_models.py` using Python 3. The importer keeps meshes readable for player-side instancing. Lore includes eighteen story fragments and nine reusable company fragments for freelance work.

The **Ghost Frequency** graphics style uses mint phosphor, bracketed instrument cards, circuit routes with packet flow, a procedural signal field, and subtle phosphor glass. Display effects can be reduced to a static treatment or disabled from the left rail. The [art-direction guide](Docs/GHOST_FREQUENCY_ART_DIRECTION.md) covers the palette, shader stack, motion settings, and rendering budget.

The **Hack The Planet** editor menu provides scene setup, gameplay asset validation, simulation and data-driven validation, a rendered workstation smoke test, and Windows builds. Builds reject invalid gameplay assets. See the [gameplay authoring guide](Docs/DATA_DRIVEN_GAMEPLAY.md) for asset types, composable rules, extension examples, and runtime APIs. The smoke test uses an isolated in-memory career and does not write to the player's career save.

The [balance refinement notes](Docs/BALANCE_REFINEMENT.md) explain the tuning, measured route trade-offs, and compatibility. Validation reports and screenshots are in `Artifacts/`. Expanded persistent footholds, independent extraction endpoints, and long-running investigations remain outside this career loop. Mission approaches branch through the network; entry and delivery still use your gateway.
