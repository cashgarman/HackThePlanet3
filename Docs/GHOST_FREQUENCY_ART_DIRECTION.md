# Ghost Frequency

Hack The Planet looks like a private workstation intercepting a corporate signal: dark instrument glass, mint phosphor, warm security markings, and precisely routed circuits. The operator's presence is visible as a living current through the network.

## Visual language

| Element | Treatment |
| --- | --- |
| Workstation | Near-black blue/green panels, fine engraved dividers, a single mint registration line across the top |
| Operator and controlled routes | Mint `#9BFACB`, rectangular packet trains, quiet layered line halos |
| Connections and scanning | Ice cyan `#65D9EB`, instrument graticules, interrupted orbital arcs, a slow receive sweep |
| Objectives and firewalls | Amber `#FFBC70`, clipped diagonal marks, warm target cards |
| Scanners and trace danger | Coral `#FF786D`, expanding scanner rings, warm warning strip and interference at high trace |
| Hubs and stealth research | Violet `#BAA1ED`, with distinct device glyphs and explicit labels |
| Typography | Readable sans-serif body text; bundled Space Mono for addresses, timestamps, signal labels, and measurements |

Give every system a small instrument face: corner brackets, a device glyph, security rating, access state, address, and a quiet barcode. Keep route lines beneath cards. Ambient geometry stays quieter than unexplored connections; controlled paths are brighter. The animated progress segment beneath a running target reflects the real tool's progress. Pausing stops mission packet travel and scanner-ring expansion.

## Signal deck UI

The active operation uses a three-panel 16:9 layout: contract and objectives on the left, the largest flexible area for the 3D network in the center, and a selection-locked inspector on the right. Trace sits above the graph; actions and the two resource rings sit below it. The compact utility row keeps simulation controls visible and opens detailed rig statistics on demand. The same framed panels, cyan controls, typography, and status treatments carry through Research, the rig, Intelligence, and dialogs.

Only discovered systems next to controlled nodes receive reachable brackets. Selecting a system highlights its known route, while the inspector stays on that selection as the pointer moves. Action cards show the current trace and time forecast. Previews, no-intel contracts, hidden nodes, and modded actions follow the same disclosure rules as gameplay.

Buttons react on hover and press; panels ease in when switching screens; dialogs lift into place; trace and action-state changes transition briefly. These interface transitions follow **Animate display effects**. With motion disabled, state changes remain immediate and readable. Text size defaults to the new layout density for new players; saved text-size preferences remain in force.

Maintain the palette's gameplay meanings with glyphs, labels, and state text. New effects should convey a route, action, or security condition. Favor slow measurement and transmission over random flicker.

## Shader stack

**SignalField.shader** creates the map's background without image assets: crosshair graticules, broken orbital rings, a rotating receive sweep, sparse bit cells, and an oscilloscope lane. Tool activity strengthens the sweep. Research changes the orbital spacing. Trace shifts signal accents toward coral above 55%, reaching full intensity at 95%.

**PhosphorGlass.shader** creates a transparent layer over the workstation: fine scanlines, restrained grain, a soft edge vignette, and a slow refresh drift. Trace adds faint warm interference. The shader never samples or displaces interface text. Both UI images ignore pointer picking, including over modal controls.

**NetworkCanvas** draws crisp vector circuits, soft line halos, packet trains, selection brackets, task-progress segments, and scanner rings. The procedural field sits behind that layer. Its fixed graph coordinates preserve pan, zoom, and button selection.

**GhostFrequency.uss** layers the visual identity over the workstation's existing layout. **SignalVisuals.cs** owns the materials, textures, rendering cadence, and local display preference. Shader assets live in Resources so standalone builds include them.

## Motion, budget, and controls

Settings → **DISPLAY** toggles control each layer independently:

- **Signal field:** procedural map background (and node intel card treatment).
- **Phosphor glass:** screen-wide glass overlay.
- **Animate display effects:** when on, enabled layers animate; when off, they stay visible but frozen.

Together these replace the legacy **FX+ / FX− / FX0** presets: both layers on with animation is Full; both on without animation is Reduced; both off is Off. Graph packet trains and scanner pulses follow the animation toggle. Shader rendering is capped at 30 updates/second; decorative vectors update at up to 24/second while live tool progress remains responsive. Action progress, trace values, and warnings still update with gameplay when animation is off. The static palette, brackets, route markings, and all game controls remain available when layers are disabled.

The preference persists locally through PlayerPrefs and is separate from the career save. The field texture is capped at 1280 × 800 and is only rendered for a visible map. Glass is capped at 1920 × 1080. Two RGBA textures need at most about 12 MiB; neither uses depth or mipmaps. UI rebuilds reuse the textures when dimensions remain stable. Resize recreates only the affected texture. Destruction releases materials and GPU textures. Unsupported shaders fall back to the static theme.

## Verification and assets

Use **Hack The Planet → Run Workstation Smoke Test** for the real UI callback flow, shader compilation and GPU readback checks, glass transparency limits, high-trace treatment, all three effects modes, frozen reduced-mode pixels, full-mode animation, and texture reuse. The test captures nine screenshots in `Artifacts/`, uses an isolated career, and restores the prior effects mode without changing the saved preference.

Space Mono is distributed under the SIL Open Font License; the font and its complete license are bundled under `Assets/HackThePlanet/Resources/HackThePlanet/Fonts/`. Source: [Google Fonts Space Mono](https://github.com/google/fonts/tree/main/ofl/spacemono).
