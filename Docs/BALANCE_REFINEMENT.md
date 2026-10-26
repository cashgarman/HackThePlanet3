# Career balance refinement

The target is a tactical hacking game with short, readable commitments: prepare an intrusion, choose exposure versus distance, and decide when to take optional archives. Planning and pause remain free. Existing career and operation IDs remain compatible.

## Measured pacing

The route lab executes legal tools at 1x simulation speed over 20 seeds, three difficulty tiers, five research profiles, and exposed versus maintenance routes: 600 samples before and 600 after. It measures tool time, not human decision time. All refined samples complete without unavoidable isolation.

First-contract averages:

| Build / route | Seconds before | Seconds after | Trace before | Trace after |
| --- | ---: | ---: | ---: | ---: |
| Stock / Exposed | 47.1 | 32.5 | 72.3 | 63.7 |
| Stock / Maintenance | 57.4 | 41.0 | 55.8 | 55.8 |
| Recon / Maintenance | 72.4 | 46.0 | 55.8 | 40.5 |
| Intrusion / Exposed | 34.4 | 25.8 | 61.9 | 57.3 |
| Stealth / Maintenance | 57.4 | 41.0 | 38.2 | 42.5 |
| Control / Exposed | 59.6 | 38.8 | 60.0 | 49.5 |

The exposed route remains faster and noisier than maintenance. Intrusion reduces time; Stealth lowers exposure without extra steps; Recon trades scan time for better information and prepared hacks; Control spends actions to remove environmental pressure. Control's refined policy scans protected firewalls before remotely suppressing their hub. These are automated comparisons, not a substitute for human playtesting.

## Mechanics

- Routine hacking and recovery are shorter. Baseline hacks take `2.2 + 1.15 × security` seconds before hardware and research; base trace remains `3 + 1.7 × security`.
- Scans take 1.6 seconds with 0.6 base trace and prepare 15% faster, 20% quieter hacks. Signal analysis halves scanning time, removes its base trace, and further reduces prepared hack trace by 15%. Sensors still apply environmental pressure.
- Root research costs 45 XP. A coherent second-tier build is affordable after two quiet deliveries without archive grinding. Later technologies still require connected parents.
- Bridge requires a scanned, reachable firewall instead of two controlled neighbors. It takes 1.2 seconds, has two charges, and produces no trace. It requires Wide scan and Efficient hack, making it available in the middle of the career.
- Backdoor keeps one fast, trace-free jump but requires a discovered route within three hops. Blueprint alone cannot teleport the operator across an unexplored network.
- Scanner disruption takes 1.2 seconds and 1 base trace. Blackout can act remotely within three mapped hops. Both suppress for 45 seconds, so Control can remove a hub's armor before entering its firewall.
- Decoy removes 12 trace and protects for 10 seconds. Its timing matters; it no longer blankets a long sequence of tools.
- All three optional archives award their 15 XP, plus 100 credits each. Quiet delivery awards 20 XP and 100 credits. Choosing extra data over the quiet threshold is an explicit reward/risk decision.
- Hardware costs are lower, and memory expansion requires an unlocked ability to avoid an early purchase with no immediate use.

## Interface

Actions appear before flavor text, with one emphasized available action, remaining charges, and trace forecasts. Completed actions disappear; locked defenses get a compact research hint. The current tool's queue is readable, CLEAR affects queued work only, and STOP cancels the tool and clears its queue without refunding trace.

Mission prose is collapsible so objectives and reward conditions remain visible. Frontier systems, objectives, and affordable research receive collision-managed labels. Discovered connections have quiet, readable lines. The map fits newly discovered systems until the operator manually changes the camera; manual pan, orbit, and zoom remain under player control. FIT restores automatic framing.

## Compatibility and verification

V2 saves retain career progress, equipment, current tool progress, queues, counters, and timers. Paid research XP is recorded and refunded independently of later price changes. No save reset is required; v1 files remain untouched.

Run **Hack The Planet → Balance → Validate Refined Career**, then **Run Workstation Smoke Test**. Reports are `Artifacts/balance-validation.txt`, `balance-before.txt`, `balance-after.txt`, `data-driven-validation.txt`, and `simulation-validation.txt`. The original 600 exact network hashes still pass because this tuning preserves seeded topology and node generation. The UI smoke includes real queue-clear, stop, restart, shortcut, settings, and save-reset callbacks using an isolated career.
