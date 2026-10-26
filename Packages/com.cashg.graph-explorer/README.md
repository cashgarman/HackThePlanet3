# Graph Explorer

Reusable Unity graph documents, layouts, rendering, navigation, UI presenters,
provider-driven queues and scoped storage. Job Harvester, OrgChart, and CodebaseGraph consume the package.
Checkpoint 3 is extracted; the standalone org-chart consumer is in
[`unity/GraphExplorer.OrgChart`](../../unity/GraphExplorer.OrgChart/README.md).

**[SDK guide: features, setup, examples and API map](Documentation~/SDK.md)** ·
**[Architecture & full API reference](Documentation~/ARCHITECTURE.md)**

- Package: `com.cashg.graph-explorer` **0.1.0**
- Namespace: `Cashg.GraphExplorer`
- Verified stack: Unity **7000.0.0a11**, URP **17.7.0**, Input System **7.0.0**;
  Windows Editor/player. Other versions/platforms are unverified.
- Install: Package Manager → **Add package from disk** → this `package.json`.
- Consumer asmdefs: `Cashg.GraphExplorer`; also
  `Cashg.GraphExplorer.Presentation` for rendering/UI.
- Installation creates no application, camera or backend connection and changes
  no global frame-rate setting. Hosts initialize, update and dispose explicitly.
- General hierarchy layout and packaged multi-language importers remain planned.
  The [C# CodebaseGraph consumer](../../unity/GraphExplorer.CodebaseGraph/README.md)
  demonstrates Git revision lanes/scrubbing, semantic dependency paths, and
  hierarchy grouping. The SDK includes event/motion timelines, discrete revision
  descriptors, and optional path edge predicates. Reversible nested neighborhood
  clustering and camera-distance LOD use `GraphClusterScene`; see the SDK guide. UI
  integration uses explicit host bindings; the org-chart project demonstrates them.

[Testing and validation handoff](../../TESTING.md) ·
[Checkpoint 3 evidence and limitations](../../docs/graph-extraction/checkpoint-3/README.md)
