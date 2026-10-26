# Changelog

## 0.1.0 — 2026-10-22

### CodebaseGraph integration — 2026-10-23

- Added `GraphRevisionTimeline` with imported revision identity, parent links,
  optional source dates, branch-lane geometry, readiness, confirmed selection,
  and ordered node/edge change summaries. Hosts keep snapshot/cache ownership.
- Added optional edge eligibility predicates to shortest-path queries/state,
  preserved across scene rebinding, and optional qualified labels/relation rows
  in the path presenter. Existing all-edge path calls retain their behavior.
- CodebaseGraph now uses Git snapshots, discrete scrubbing/playback/merge lanes,
  dependency-only paths, nested subtree groups, distance LOD, and scoped view
  persistence. Its C# analyzer adds declaration/body fingerprints.

- First extraction checkpoint: dataset-neutral records, document/indexes,
  transactional batches, snapshots, typed scalar metadata, schema descriptors,
  semantic JSON, per-instance Unity view state and configurable budgets.
- Separate runtime, Editor and EditMode test assemblies.
- Job Harvester consumes this package through a temporary compatibility facade.
- Presentation, providers, scoped storage, hierarchy exploration and codebase
  ingestion remain subsequent checkpoints.

### Checkpoint 3 — rendering boundary (in progress)

- Moved the renderer, spatial/flat/group/lane solver, navigation, node effects,
  atmosphere, settings, reusable models, materials/shaders/styles and diagnostics.
- Added generic scene projections, schema/policy-driven presentation, per-instance
  resource overrides, and explicit rendering-session initialization/disposal.
- Preserved moved asset GUIDs and settings; namespaced Resources defaults.
- Replaced automatic host asset generation with read-only resource validation.
- Complete UI/provider/scoped-storage extraction remains to be implemented.
