# Progression and automated balance

The shipped catalog contains 31 research nodes, 20 module models, four chassis, six story contracts and three freelance definitions. Later networks combine credential locks with recurring security sweeps. All content remains editable in `data/` and the Content Editor.

## Security puzzles

A credential lock blocks downloads from its `targetNodeIds`. Control its `sourceNodeId` key server and use **Extract Key** to open every gate in that layer, or control a protected target and use **Spoof Gate** to open that target. Key servers receive a recognizable label when the operation starts; normal discovery still controls whether that label is visible. Locked-target hints disclose a source's name only after discovery.

A security sweep adds 12 trace every 60 unpaused simulation seconds while its controller remains active. Controller suppression and **Loop Feed** freeze the countdown; controller capture and **Hard Reset** end its sweeps. Pausing freezes the operation, statuses, RAM recovery and sweep timers. Action forecasts include bursts due during the action, including bursts after temporary suppression expires.

| Research | Branch / parent | XP | Action before contract pacing |
| --- | --- | ---: | --- |
| Credential Analysis | Recon / Wide Scan | 70 | Extract Key: 12 seconds, 4 trace |
| Protocol Spoof | Intrusion / Efficient Hack | 90 | Spoof Gate: 10 seconds, 10 trace, two uses |
| Loop Feed | Stealth / Quiet Tools | 75 | 3 seconds, 3 trace; reserves 18 GB RAM while active |
| Hard Reset | Control / Disrupt Scanner | 85 | 12 seconds, 10 trace, one use; 16 GB reclaiming RAM; discovered controller within three mapped hops |

All four are equipped abilities. Unequip an ancestor ability such as Disrupt Scanner when making room for Hard Reset. Research refunds remain free between jobs.

The first two story jobs have no special layers. Ghost in the Wire introduces an optional archive lock; Glasshouse requires a credential counter. Dead Drop and Last Light combine locks and sweeps. Freelance Easy has neither layer, Medium adds locks, and Hard adds sweeps. Briefs disclose counter categories even when Hard topology is concealed.

| New module | Gate | Credits | Benefit |
| --- | --- | ---: | --- |
| Decoder Board | Data Engineering | 750 | +0.20 bandwidth; 25% faster Extract Key |
| Spoof ASIC | Parallel Engineering | 1,000 | +0.25 compute; one extra spoof |
| Veil Array | Signal Engineering | 900 | 6% less action trace; 20% less sweep trace |
| Isolation Bank | Infrastructure II | 1,000 | +15 GB RAM; 25% faster Hard Reset |

## Run the suite

Use **Hack The Planet → Balance → Run Career Balance Suite** for the five-seed profile. From the project directory, with its Editor closed:

```powershell
./scripts/run-balance.ps1
./scripts/run-balance.ps1 -Full
./scripts/run-balance.ps1 -Full -UnityPath 'D:/Unity/7000.0.0a11/Editor/Unity.exe'
```

The script uses `UNITY_EDITOR_PATH` when set, then checks standard local installation paths. A failed gate produces a nonzero exit code. The static batch entry is `HackThePlanet.Editor.BalanceSuite.RunBatch`; add `-balanceFull` for twenty seeds. The existing open-Editor command bridge accepts `career-balance`.

Outputs under `Artifacts/`:

- `career-balance.json` and `.txt`: every contract, all freelance tiers, eleven build/rig profiles, five or twenty seeds; 825 or 3,300 isolated trials.
- Each run also keeps `career-balance-quick` or `career-balance-full` JSON/text copies and records the content fingerprint, seed count, generator version and policy version.
- The same report includes two separate careers carrying earned resources across all six story jobs. Quiet and archive bonuses are removed from their available budget: 60 or 240 career stages.
- `upgrade-balance.json` and `.txt`: marginal tool/resource measurements for every purchasable research node and module, plus possible dominance or negligible-benefit findings.
- `career-balance-unity.log`: batch execution and errors.

Trials record success, failure reason, time, trace, bonuses, rewards, XP/credit spending, actions used, and power/cooling/RAM/storage headroom. Isolated trials are funded comparison fixtures; the carried careers are the affordability gate. The policy reads generated topology to choose candidate routes, then runs actual legal `Preview`/`Execute` calls and discovery actions. It establishes reproducible solutions and tradeoffs; it does not measure human discovery time or prove that every possible strategy works.

## Pacing and regression gates

The successful-trial median must be 60–120 simulated seconds on Easy, 120–240 on Medium and 240–300 on Hard. Hard's 90th percentile must remain at or below 360 seconds. These measurements exclude human thinking time and reflect 1× simulation time.

The reference twenty-seed run passed 3,300 isolated trials and all 240 base-payout career stages. Successful-trial pacing was:

| Difficulty | Median | 90th percentile |
| --- | ---: | ---: |
| Easy | 93.0 s | 110.0 s |
| Medium | 153.0 s | 216.5 s |
| Hard | 244.0 s | 289.5 s |

The same revision passed 66 EditMode tests, five PlayMode tests, the existing content/simulation/data-driven/rig validations, GPU workstation smoke, the Windows build, and the packaged player's content validation. Per-contract dominance findings identify research with no useful counter target on easier jobs; they are informational rather than failing gates.

Authored global modifiers multiply ordinary action duration by 2.25/3/5 and trace by 0.8/0.55/0.25 for Easy/Medium/Hard. The `countermeasure` action tag exempts the four new tools from those global factors. Each contract's optional `paceMultiplier` then adjusts duration to account for objective count. Base trace, scanner exposure, research, rig modifiers, floors and explicit trace-free actions continue through the normal rule pipeline.

The suite fails when the first two jobs lose their starter-build solution, credential extraction or spoofing loses every reliable build, Loop Feed or Hard Reset loses every reliable build on combined-defense jobs, a base-payout career cannot finish, or pacing exceeds its gates. Upgrade comparisons are findings for review; they do not edit content automatically. Footprint shape and specialized conditional effects can justify an apparent numerical disadvantage.

Run `BalanceSuite.ValidateIntegration` in Unity batch mode for content/history validation, simulation validation, data-driven/tool balance validation and the complete rig checks. EditMode tests include credential settlement, sweep pause/toggle behavior, suppression forecasts, reset/save restoration and old-save compatibility. Workstation smoke and the Windows build remain separate rendering/build gates.

## Authoring and compatibility

`ContractDefinition.defenses` contains stable layer IDs, `kind` (`CredentialLock` or `SecuritySweep`), `sourceNodeId`, `targetNodeIds`, `minimumDifficulty`, `intervalSeconds` and `traceBurst`. Sources and targets must exist in the network. The strict content codec supports `DefenseCondition` and `DefenseEffect` with `KeyServer`, `LockedTarget` and `SweepController` operations. Missing `defenses` and `paceMultiplier` fields in older extension contracts default to an empty list and 1 respectively.

V3 careers retain their filename and version. New operations snapshot defense configuration, countdowns, acquired keys, spoofed targets and disabled controllers. Old active saves with no defense state resume without gaining blockers. Stable contract, research and module IDs, paid research investment and module prices are preserved. Network generation and topology hashes are unchanged.
