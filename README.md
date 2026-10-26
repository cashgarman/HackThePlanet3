# Hack The Planet

A playable single-player hacking career built in Unity. All systems are fictional, local network puzzles; no real network access is used by gameplay.

## Play

Run **`Builds/Windows/Hack The Planet.exe`**. Keep the executable with its `_Data` directory and the accompanying DLLs. The default window is 1680 × 960 and can be resized. A mouse and keyboard are recommended.

Choose **The Quiet Copy**, then **Connect to network**. Select Public relay and use **Hack system** to establish your first foothold. Control connected systems to reach the gold contract objectives. Recover the files, then **Extract & deliver** to bank payment and XP. A short field guide is available from **?** or **F1**.

| Control | Action |
| --- | --- |
| Click a system | Inspect it and preview actions |
| 1 / 2 / 3 / 4 | Scan / Hack / Recover / Disrupt selected target |
| Space | Pause or resume the current operation |
| 1x / 2x / 4x buttons | Simulation speed |
| Mouse wheel over graph | Zoom |
| Drag empty graph space | Pan |
| FIT | Recenter the graph |
| F1 or ? | Operator field guide |
| Escape or ESC button | Settings menu: Resume, Reset Game Save, Quit |
| SND | Toggle interface sounds |
| FX+ / FX− / FX0 | Cycle animated, reduced/static, and disabled display effects |

## Career loop

- Six linked story contracts across Helix Dynamics, Morrow Financial, and Aegis Systems: theft, synchronized record edits, evidence recovery, and a final disruption.
- Twenty-five systems per operation, two broad approaches, six defense/system roles, optional archives, and discoverable connections. Hubs strengthen connected targets; scanners add trace while tools run nearby.
- No idle trace, unlimited pause, action previews, a queue, and retryable failures. Disconnect after completing every objective to deliver. A 100% trace ends the attempt and loses its unbanked rewards; hardware and purchased research persist.
- Eighteen connected research nodes including the starting toolkit, four branches, and crossover technologies requiring both parents. XP is spent directly. Refund all research for free between contracts. Passives are always active; special abilities occupy a limited loadout.
- Credits upgrade CPU, bandwidth, and ability capacity. Research and rig changes happen between operations.
- Success changes corporate defenses and publishes a world bulletin. Three repeatable freelance templates unlock after the third story contract, with defenses, connections, and rewards changing as your career advances.
- The Intelligence tab preserves the last twenty operation reports, including action timelines and trace at disconnect.

All bonus conditions are shown before connecting. Optional archives pay +120 credits and +15 XP each, with optional XP capped at +30 per contract. Finishing below the contract's trace threshold adds +150 credits and +10 XP.

## Saves

The game automatically saves your career and active operation every five seconds and at important transitions. Reopened operations start paused, including any running tool. Saves use atomic replacement and a previous-save backup.

Open Settings with **Escape** or the **ESC** button on the left rail. The menu pauses an active operation; **Resume** restores its previous pause state. **Reset Game Save** asks for confirmation, then starts a fresh career and resets the recovery backup. **Quit** saves your career before closing the game.

Windows save location: `%USERPROFILE%/AppData/LocalLow/Independent Operator/Hack The Planet/career-v1.json`.

## Unity project

Open this folder in **Unity 7000.0.0a11**. Enter Play mode in the included scene; the workstation bootstraps automatically. The dedicated scene is `Assets/HackThePlanet/Scenes/Workstation.unity`.

The included Graph Explorer package supplies the discovered graph document, graph scenes, and controlled-route shortest paths. Hidden world state stays in the game simulation. The game uses a custom stable 2D UI Toolkit presentation for mission and research graphs.

Source files are under `Assets/HackThePlanet/Runtime/`. `GameContent.cs` contains contracts, networks, and technologies; `GameSimulation.cs` owns tools, trace, progression, and persistence; `WorkstationApp.cs` and `NetworkCanvas.cs` present the workstation. Styles are under `Assets/HackThePlanet/Resources/HackThePlanet/`.

The **Ghost Frequency** graphics style uses mint phosphor, bracketed instrument cards, circuit routes with packet flow, a procedural signal field, and subtle phosphor glass. Display effects can be reduced to a static treatment or disabled from the left rail. The [art-direction guide](Docs/GHOST_FREQUENCY_ART_DIRECTION.md) covers the palette, shader stack, motion settings, and rendering budget.

The **Hack The Planet** editor menu provides scene setup, simulation validation, a rendered workstation smoke test, and Windows builds. The smoke test uses an isolated in-memory career and does not write to the player's career save.

Validation reports and screenshots are in `Artifacts/`. This is the complete first playable career loop; expanded persistent footholds, multiple independent access/extraction routes, and long-running investigations from the earlier concept are outside the casual gameplay revision implemented here.
