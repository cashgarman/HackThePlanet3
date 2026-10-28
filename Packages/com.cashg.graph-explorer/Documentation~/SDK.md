# Graph Explorer SDK

Reference for developers and AI agents, matching package **0.1.0** and the
checkpoint-4 standalone consumer.
For a generated catalog of every public type and member, see [ARCHITECTURE.md](ARCHITECTURE.md).
The checked-in declarations linked below are authoritative when signatures change.
Snippets are integration fragments. The complete consumer is
[Northstar Robotics](../../../unity/GraphExplorer.OrgChart/README.md).

## 1. Scope and feature status

| Area        | Available now                                                                                                                                                        | Host responsibility / limits                                                                              |
|-------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------|
| Data        | Stable dataset/node/edge IDs, directed typed relationships, scalar metadata, provenance, confidence, adjacency indexes, atomic batches/snapshots, deterministic JSON | Import/normalize facts; assign stable IDs and relationship meaning                                        |
| View        | Positions, velocities, pins, selection/focus, visibility, favorites, emphasis; deterministic initial positions; reversible nested clusters and distance LOD          | Own interaction/state persistence and cluster controls                                                    |
| Layout      | Force-directed spatial/flat layouts; group constraints and ordered lanes                                                                                             | Supply grouping/lane policies; layout proximity is not evidence of a relationship                         |
| Rendering   | Instanced node shapes, dynamic edge ribbons, picking, context highlighting, toon/outline/glow effects, atmosphere                                                    | Supply camera/URP setup, input routing and frame scheduling                                               |
| UI          | Resizable responsive shell, scale/reduced motion, fuzzy node search, favorites, schema inspector, relationship search/highlight, filter controls, labels/markers     | Compose panels/bindings; dataset filters, custom forms, source actions and persistence remain host policy |
| Actions     | Definitions/targets, review, ordered submission, recovery/control/progress contracts; generic row/reorder presentation                                               | Provider executes and owns authoritative state; host owns draft and durable pending identity              |
| Storage     | Version-checked, scoped JSON, atomic replacement, copy-only legacy migration                                                                                         | Define versioned DTOs and migration policy; no automatic workspace serialization                          |
| Diagnostics | Resource/graph Editor validation, profiler markers, fixture/test utilities                                                                                           | Run checks appropriate to the consuming project                                                           |

Not implemented yet: general hierarchy forest derivation/layout, the optional
multi-language `com.cashg.graph-explorer.codebase` package, or expanded
scale/platform guarantees. The standalone CodebaseGraph host provides C# semantic
ingestion and Git revision exploration; Python/JS/TS ingestion is not available.
The standalone org-chart project demonstrates clean consumption and explicit
instance/lifecycle validation; see its evidence for the precise tested limits.
This is a composable SDK; there is no single component that builds a complete
explorer application for an empty host.

## 2. Install and assembly boundaries

Use Package Manager **Add package from disk** with `package.json`. The current
repository host uses this entry in `Packages/manifest.json`:

```json
"com.cashg.graph-explorer": "file:../../../packages/com.cashg.graph-explorer"
```

Adjust paths for your project. Reference these assemblies from your asmdefs:

| Assembly                           | Purpose                                                                                                                             |
|------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| `Cashg.GraphExplorer`              | Model, schema, projections, providers, queue coordinator, storage; uses Unity types and System.Text.Json, not URP/Input System APIs |
| `Cashg.GraphExplorer.Presentation` | Rendering, navigation, effects, UI Toolkit presenters; references core, Input System and URP/Core                                   |
| `Cashg.GraphExplorer.Editor`       | Editor validation/resource helpers; do not reference from player runtime code                                                       |
| `Cashg.GraphExplorer.Tests.Editor` | EditMode tests; optional test consumer only                                                                                         |

All public types below use namespace `Cashg.GraphExplorer`. Package dependencies
are URP **17.7.0** and Input System **7.0.0**; the host Editor is **7000.0.0a11 (fac09b31727f)**. Do not assume
compatibility with another Unity beta.
No Python/backend or Job Harvester assets/code are required by runtime assemblies.

## 3. Model terminology and rules

| Type                                      | Meaning / main API                                                                                                                                                                                                                                                                                  |
|-------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `GraphValue` / `GraphValueType`           | `From(string/double/bool)`, `Null`; finite scalar values only                                                                                                                                                                                                                                       |
| `GraphNodeRecord`                         | Immutable `Id`, `TypeId`, `Label`, `Properties`, `Sources`; `WithIdentity`, `WithProperty`, `WithSources` return replacements                                                                                                                                                                       |
| `GraphEdgeRecord`                         | Immutable `Id`, `From`, `To`, `TypeId`, `Status`, optional `Confidence`, metadata/sources; `WithEvidence` returns a replacement                                                                                                                                                                     |
| `GraphDocument`                           | Canonical facts and temporal history: `Nodes`, `Edges`, `UpsertNode`, `UpsertEdge`, removals, `Apply`, `ReplaceSnapshot`, `EstablishBaseline`, event/time `Seek`, `ReturnToLive`, `Timeline`, `RecordPositionFrame`, `ClearPositionHistory`, `ClearEventHistory`, `ClearHistory`, adjacency queries |
| `GraphBatch`                              | Mutable transaction input: `Nodes`/`Edges` upserts, `RemoveNodes`/`RemoveEdges` IDs                                                                                                                                                                                                                 |
| `GraphSnapshot`                           | Dataset identity/display name plus complete record lists                                                                                                                                                                                                                                            |
| `GraphSchema`                             | Registers descriptors and `Validate(document)`; validation returns issues                                                                                                                                                                                                                           |
| `GraphViewState` / `GraphNodeViewState`   | Separate per-instance presentation state; `GetOrCreate`, `Synchronize`, `SelectedId`, `FocusId`, `FoldedNodeIds`                                                                                                                                                                                    |
| `GraphPathfinder` / `GraphPathState`      | Directed unweighted shortest paths, persistent endpoints/results, graph-pick requests and topology refresh                                                                                                                                                                                          |
| `GraphRevisionTimeline` / `GraphRevision` | Host-imported discrete revisions, parent IDs, optional source timestamps, branch lane connections, readiness and confirmed selection; snapshots remain provider-owned                                                                                                                               |
| `GraphScene`                              | Default live `IGraphScene` projection over document/schema/view; call `Synchronize()` after document mutation                                                                                                                                                                                       |
| `GraphClusterScene`                       | Optional `IGraphScene` wrapper for nested temporary clusters, crossing-edge rewiring, and camera-distance LOD; source facts remain intact                                                                                                                                                           |
| `IGraphNode` / `IGraphEdge`               | Live facade over records and view state; implement these for richer domain adapters                                                                                                                                                                                                                 |

IDs are nonempty, case-sensitive ordinal strings; no implicit deduplication or
normalization occurs. Self-links, parallel edges, reversed edges and cycles are
valid. Distinct-neighbor degree ignores direction/parallel edges and excludes
self-links. Confidence must be finite if supplied; its scale is dataset-defined.

Records copy their input collections. `WithProperty(key, null)` removes a property;
`WithProperty(key, GraphValue.Null)` stores explicit null. Nested metadata is not
supported; encode timestamps as text and keep structured provider data separate.

Edges require existing endpoints. `Apply` validates before committing, supports
forward references within the same batch and removes incident edges with removed
nodes. Contradictory/duplicate operations or dangling endpoints reject the whole
batch. `ReplaceSnapshot` validates a detached replacement and requires the same
dataset ID. Use a new document/view/scene for a different dataset.

`Revision` tracks semantic changes; `TopologyRevision` tracks structure/type
changes. View edits change neither. `GraphScene.Revision` exposes topology revision.
Preserve projection identity; do not copy an entire graph each frame. Reacquire
adjacency handles after snapshot replacement or node deletion/recreation.

The model is single-threaded: load/analyze elsewhere, then apply on the owner
thread. Snapshot work is O (V+E); neighborhood traversal visits reached topology.

### Reversible neighborhood clustering

Wrap an existing scene and pass the wrapper to layout, renderer, picking, and any
UI that should show clusters. The document stays authoritative; projected edges
keep their original IDs, direction, type, properties, and sources. Edges entirely
inside a collapsed cluster are hidden, then restored on expansion. Parallel
crossing edges stay separate. `LeafIds`, `MemberRecords`, and `InternalEdges`
resolve source facts across every nesting level, so application actions can
target real entities and an inspector can show the cluster contents.

```csharp
var clustered = new GraphClusterScene(scene);
string team = clustered.Collapse(new[] { "lead", "dev" }, "Team");
string division = clustered.Collapse(new[] { team, "designer" }, "Division");
layout.Bind(clustered);
renderer.UpdateMetrics(clustered, edgeFilter);

// On an expand action, the first call reveals the nested Team cluster.
clustered.Expand(division);
layout.Bind(clustered);
// Expand Team to reveal lead and dev with their original edges.
clustered.Expand(team);
layout.Bind(clustered);
```

`Collapse` accepts visible nodes or existing clusters that share an immediate
parent. A host can call it from its domain logic. `Recollapse` retains a saved
group; `RemoveCluster` dissolves the group. Moving a collapsed cluster shifts
its children by the same amount when expanded. Cluster IDs are temporary view
IDs; save the member IDs and domain rule if a host needs persistent groups.

For automatic distance LOD, set `LodEnabled = true` and call
`UpdateLod(camera.transform.position)` at a modest interval. It groups connected
nodes whose positions are within `LodNeighborSeparation`, after they pass
`LodEnterDistance`; it releases automatic groups inside `LodExitDistance`.
Keep exit below enter for hysteresis. Rebind layout and update renderer metrics
when the method returns true. Manual clusters remain under application control.
Expanding an automatic cluster suppresses automatic regrouping of its members;
call `ClearLodOverrides()` to let distance LOD group them again.

### Shortest paths

Shortest paths follow directed `From → To` edges and count each eligible loaded
edge as one jump. Breadth-first search orders adjacent destinations and edge IDs
ordinally so equal-length routes are deterministic. UI visibility filters and edge
metadata do not change traversal cost. The optional `Func<IGraphEdge, bool>
edgeAllowed` argument on `GraphPathfinder.Find` and `GraphPathState` restricts
eligibility without changing the canonical document or default behavior.

```csharp
var path = new GraphPathState(scene);
var renderBinding = GraphPathRendering.Bind(path, renderer);
path.Show("employee:1", "company:9"); // active route, or an active no-path result
path.Synchronize();                    // after scene topology synchronization
path.BeginPick(GraphPathEndpoint.Start);
// In the host's primary-click handler, consume endpoint clicks before selection:
bool used = GraphSceneInput.HandlePathPick(hoverSample, path);
path.Clear();                          // dismiss and restore ordinary highlights
renderBinding.Dispose();               // on host teardown
```

A code consumer can use `new GraphPathState(scene, edge => edge.Relation ==
"calls" || edge.Relation == "uses_type")` to exclude structural shortcuts. The
predicate remains in effect across `Bind` and topology refreshes. Call
`Synchronize(true)` when a predicate closes over policy that changes without a
topology change. Pathfinding should use the canonical scene; reveal route symbols
through a cluster projection before rendering their original IDs.

`GraphPathState.Changed` reports endpoint, route and active-state changes.
`Result.NodeIds` and `Result.EdgeIds` preserve order; `Jumps` is edge count and
the same-node route has zero jumps. After topology changes, synchronize the
scene and call `path.Synchronize()`; endpoints remain active and the result is
recomputed, including an unreachable result if the route no longer exists.

`GraphPathPresenter` builds the compact UI Toolkit window. Pass it the root
overlay, scene, path state, a navigation callback, and optionally a pick callback (the default requests picking from the
state). Check
`GraphSceneInput.HandlePathPick` before ordinary node selection so endpoint
clicks do not replace selection. Dispose the presenter when removing the panel.
Selecting a route row invokes the supplied navigation callback.
The optional final `nodeLabel` callback formats endpoint choices and route rows
for domains with duplicate short labels, such as qualified code symbols.

### Imported revision graphs

`GraphTimeline` records one document's ordinary event/motion stream. Source-control
branches instead use `GraphRevisionTimeline`: it accepts an ordered collection of
`GraphRevision(id, parentIds, timestampUtc, label)`. Supply parent-before-child
topological order. Source timestamps are optional and descriptive; they do not
determine seek order and need not be monotonic. A working copy can use a null
timestamp.

```csharp
var revisions = new GraphRevisionTimeline();
revisions.Replace(new[] {
    new GraphRevision("commit-a", Array.Empty<string>(), commitTime, "Initial"),
    new GraphRevision("working", new[] { "commit-a" }, null, "Working Tree")
});
revisions.SelectionRequested += revision => RequestSnapshotFromProvider(revision.Id);
// After the provider succeeds and the host displays its validated snapshot:
revisions.MarkReady("commit-a", GraphRevisionChangeSummary.Between(null, snapshot));
revisions.ConfirmSelection("commit-a");
```

`Replace` validates revision IDs before changing the active collection.
`RequestSelection` asks the host to load a revision; `ConfirmSelection` only follows
successful display, so a failed load can retain the old graph. `Changed` reports
metadata/readiness/selection changes. `Lane`, `LaneCount`, and `LaneLinks` describe
connections for rows displayed newest first; hosts draw parent/merge lanes and own
scrubbing, playback, progress, cancellation, source opening, and snapshot caching.
Revision descriptors do not retain full graph snapshots and are not serialized by
`GraphJson`.

`GraphRevisionChangeSummary.Between(before, after)` returns ordered created,
modified, and deleted node/edge IDs and counts. Symbols with a `content_hash`
property use that fingerprint for modification checks, ignoring source-line
movement. Other nodes compare type/label; edges compare endpoints, relation,
status, confidence, and scalar properties. The host defines the comparison parent (CodebaseGraph uses first-parent
diffs). Do not turn jumps between branch snapshots
into invented timestamped document events. The complete C# integration is in
[`GraphExplorer.CodebaseGraph`](../../../unity/GraphExplorer.CodebaseGraph/README.md).

### Create and update an ordinary dataset

```csharp
using Cashg.GraphExplorer;
using UnityEngine;

var document = new GraphDocument("org:fictional", "Fictional team");
var batch = new GraphBatch();
batch.Nodes.Add(new GraphNodeRecord("lead", "employee", "Team lead"));
batch.Nodes.Add(new GraphNodeRecord("dev", "employee", "Developer")
    .WithProperty("tenure", GraphValue.From(4.5)));
batch.Edges.Add(new GraphEdgeRecord("reports:dev", "dev", "lead", "reports_to"));
document.Apply(batch);

var schema = new GraphSchema();
schema.Register(new GraphNodeTypeDefinition("employee", "Employee", "Person", Color.cyan));
schema.Register(new GraphFieldDefinition("tenure", "Tenure", GraphValueType.Number, unit: "years"));
schema.Register(new GraphRelationDefinition("reports_to", "Reports to",
    isHierarchy: true, parentIsFrom: false)); // Declares meaning, not an available hierarchy layout.
var scene = new GraphScene(document, schema);

// Replace the immutable record; synchronize after a transaction.
document.UpsertNode(document.Nodes["dev"].WithProperty("tenure", GraphValue.From(5.0)));
scene.Synchronize();
scene.Nodes["dev"].Pinned = true;
scene.View.SelectedId = "dev";

string json = GraphJson.Serialize(document);
GraphDocument imported = GraphJson.Parse(json); // Parse before touching a displayed document.
```

`GraphJson` version 1 exports **facts only**, not schema, view, workspaces or action
drafts. Ordering is deterministic. Future versions/invalid topology are rejected.
Schema and workspace persistence need their own host-defined versioned format.

## 4. Schema, appearance and layout

- `GraphNodeTypeDefinition`: type label, `ShapeId`, color and positive default radius.
- `GraphFieldDefinition`: type, label, `Searchable`/`Inspectable`, unit,
  `PartitionBy`, optional category dictionary. Undeclared fields are allowed.
- `GraphCategoryDefinition`: label, color, badge and order.
- `GraphRelationDefinition`: label/color, spring strength and hierarchy direction.
  `ParentIsFrom=false` means the edge's **To** endpoint is the parent.
- Registration uses unique IDs; duplicate registrations throw rather than replace.

Built-in shape IDs: `Briefcase`, `Building`, `Badge`, `Factory`, `Gear`, `Person`,
`MapPin`, `Globe`, `Document`, `Gem`. Missing shapes fall back to `Gem`; unknown
node types get a neutral presentation descriptor.

`GraphPresentationProfile` selects `MetricField`/`CategoryField`, optional applicability
predicates and category fallback/metric color functions. Numeric ranges are computed
per field-defined `PartitionBy` value, avoiding comparisons across incompatible units.
Grouping uses `IsGroupAnchor`, `GroupId`, `GroupStrength`; lanes use `Lane`,
`LaneCenter`, `LaneSpacing`. Configure policies before binding the layout.

```csharp
var profile = new GraphPresentationProfile { MetricField = "tenure" };
// session is initialized in the next section.
session.Layout.Mode = LayoutMode.Flat; // Spatial, Flat, Groups, Lanes
session.Renderer.Size = SizeMapping.Metric; // Uniform, Connections, Metric
session.Renderer.ColorBy = ColorMapping.Type; // Type, Connections, Category, Metric
session.Layout.Reheat();
```

The solver uses softened Barnes-Hut repulsion, damped velocity integration and
relation springs. `Paused`, `Energy`, `Temperature`, `Bind`, `Step`, `Reheat` are
its main controls. Layout coordinates are presentation, never semantic facts.

## 5. Rendering, navigation and lifetime

Supply a host-configured camera and URP scene. Initialization validates defaults,
creates owned runtime resources and attaches effects; it does not frame the camera
or configure global frame rate. Keep the session alive across frames.

```csharp
// Setup once, e.g. in your host's initialization method:
var resources = new GraphResourceConfiguration();
var options = new GraphExplorerOptions(nodeRenderBudget: 8000, nodeLabelBudget: 48);
var session = GraphRenderingSession.Initialize(camera, resources, profile, options);
session.Layout.Bind(scene);
IGraphEdgeFilter edgeFilter = new AllEdges();
session.Renderer.UpdateMetrics(scene, edgeFilter);
session.Navigation.Overview(scene); // Explicit host request to move the camera.

// Each host frame; selected/hovered references may be null:
session.Layout.Step(Time.deltaTime);
session.Navigation.Tick(inputBlocked, selected);
session.Atmosphere.Tick(reducedMotion);
session.Renderer.ReducedMotion = reducedMotion;
session.Renderer.Draw(scene, edgeFilter, camera, selected, hovered, hoveredEdge);

// Teardown, after stopping host coroutines and disposing UI presenters:
session.Dispose();

// Declare this helper separately in your consumer:
sealed class AllEdges : IGraphEdgeFilter {
    public bool EdgeMatches(IGraphEdge edge) => true;
}
```

These are lifecycle fragments: `camera`, input/UI state and `IGraphNode`/`IGraphEdge`
selection/hover references belong to your host. Recompute renderer metrics after
facts, filtering or mapping policies change; synchronize/rebind layout after
topology changes. Do not initialize or rebuild UI every frame.

`GraphNavigation` provides `Tick`, `Focus`, `Overview`, `Frame`, `Follow`, `Retarget`,
`StopTracking`, `Restore`, `Release`. The host blocks navigation while UI owns input.
Navigation controls: left mouse orbits; right and middle mouse pan with inertial
coast after release; the mouse wheel zooms. Selection and other application shortcuts are host bindings.
`GraphRenderer.Pick`/`PickEdge` resolve pointer hits; the host updates selection/view,
drag-to-pin and inspector state. Picking receives Unity screen-space coordinates;
convert separately to UI Toolkit panel coordinates for overlays.
`GraphSceneInput.Sample` and `HandlePrimaryClick` centralize UI blocking, hover picks,
and primary-button behavior: a single click selects; a double-click on the same node
calls `GraphNavigation.Focus`. Keep selection callbacks inspection-only—do not focus
the camera there unless the action is explicit (search Enter, a Focus button, etc.).

`Visible` controls inclusion; `Matches` distinguishes direct matches from context.
Apply your predicates to view/projection state, not by deleting semantic records.
`IGraphEdgeFilter.EdgeMatches` supplies edge policy. `VisibleDegree` controls
filtered degree metrics. Inspect `VisibleCount`, `VisibleEdges`, `Truncated`,
`IsDrawn` to disclose render-budget truncation.

Defaults are 8,000 rendered nodes / 48 node labels; Job Harvester explicitly
uses 160 labels. Pass the label limit into your label-presenter bindings too;
initializing a rendering session does not construct a label UI. Rendering uses
1,023-instance batches and a dynamic edge mesh, not a GameObject per node.
These budgets are not guarantees of interactivity at larger dataset sizes.

### Ownership and resources

One active rendering session per camera. `Dispose` is idempotent and releases
owned meshes/materials, effects, atmosphere, render subscriptions and cursor capture;
borrowed camera/effect state is restored where managed by the session. Camera,
document/schema, source resource assets and host UI roots remain host-owned.

Defaults load from `Resources/CashgGraphExplorer/` using `GraphResources.Load<T>(key)`.
Per-instance overrides use `resources.Set(key, asset)`; model keys are
`GraphNodeModels/<ShapeId>`. Settings/assets include `GraphVisualSettings`,
`GraphNodeLabelSettings`, `GraphPanelSettings`, `GraphTheme`, `GraphExplorer` and
`GraphShell` styles. Clone mutable ScriptableObject/PanelSettings assets per host;
do not edit package defaults to change one explorer instance.

`GraphNodeLabelSettings` enables mouse-proximity title reveal by default. The
shared world-label presenter measures from each node's projected center to the
mouse in screen pixels (independent of UI scale), with full opacity inside
`RevealInnerRadius` (40 px) and a smooth falloff to zero at `RevealOuterRadius`
(180 px). `RevealFadeSeconds` (0.15 s) controls unscaled fade timing. Moving over
UI, outside the graph viewport, or out of the focused application fades labels
away. `KeepSelectedLabelVisible` preserves the selected title; disable it for
strict proximity visibility. The entire label panel fades together. Host distance
LOD/opacity, label budgets and overlap avoidance still apply. Set
`MouseProximityReveal` to false to restore the original host visibility policy.

## 6. Compose the UI

Create a host `UIDocument`/root, clone/configure `GraphPanelSettings`, apply
`GraphTheme`, and attach `GraphExplorer`/`GraphShell` styles through
`GraphResources.Load`. Keep branding, panel content and state in host bindings.

| Component                                                         | How to use / extend                                                                                                                                                                                                                                                                                                                                                                                                      |
|-------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `GraphShellController` + `IGraphShellHost`                        | Bind panels, preferences, menus, callbacks and state. Call `BuildShellHeader()`, create content, then `CompleteShell()`. `RefreshShellLayout()` handles responsive layout; dispose before removing content/root.                                                                                                                                                                                                         |
| `GraphTimelineControls` + `IGraphTimelineShellHost`               | The shell owns the history scrubber, playback and change summary. Implement `TimelineDocument` and `SeekTimeline(int)` to connect a host graph and refresh its view/layout after event seeks. `IGraphPositionHistoryShellHost` additionally opts into motion controls and implements `SeekTimelineTime(DateTimeOffset)`. The shared View drawer toggle uses `GraphShellPreferences.timelineEnabled`, enabled by default. |
| `GraphSearchPresenter<TNode>` + `GraphSearchOptions<TNode>`       | Bind root/search host, live nodes, stable result list, type/shape/icon/ghost callbacks, open state, reset/reveal/activate actions. `BuildNodeSearch()`, `RefreshNodeSearch()`, `PositionNodeSearch()`, `CloseNodeSearch()`, `Dispose()`.                                                                                                                                                                                 |
| `GraphFavoritesPresenter<TNode>` + `GraphFavoritesOptions<TNode>` | Bind parent ScrollView, nodes/results, favorite-ID set and activate/filter callbacks. `BuildFavoritesDrawer()`, `RefreshFavorites()`, `Dispose()`. Host persists IDs and updates node `Favorite`.                                                                                                                                                                                                                        |
| `GraphInspectorPresenter`                                         | `Render(parent, scene, node, options)` builds ordinary properties, relationships and sources. Pass null node for empty state. `Header`/`Relationships` allow custom composition.                                                                                                                                                                                                                                         |
| `GraphInspectorOptions`                                           | `Select`, `OpenSource`, `AppendSections`, host-defined `Actions`, tooltip cleanup/binding and foldout decoration. Source validation/opening is the host's responsibility.                                                                                                                                                                                                                                                |
| `GraphFilterPresenter`                                            | `TypeButton`/`Mode` render included/hidden states. Host supplies predicates, numeric/category forms and context behavior.                                                                                                                                                                                                                                                                                                |
| `GraphWorldLabelsPresenter<TNode,TLabel>`                         | Bind label pool/state, nodes, camera/renderer, safe viewport, limits/settings, compose/process callbacks; handles projection, placement, priority, collision, LOD and pooling.                                                                                                                                                                                                                                           |
| `GraphWorldLabelPresenter`, `GraphFavoriteMarkers`                | Label widget/geometry helpers and world favorite markers. Their host container owns the elements.                                                                                                                                                                                                                                                                                                                        |
| `GraphUi`, `GraphContentUi`, `HeaderIcons`                        | Shared controls, formatting, favorite visuals and icons.                                                                                                                                                                                                                                                                                                                                                                 |
| `GraphQueueView`                                                  | Shared draft/run cards, progress, status and reorder gestures; bind host actions/lock/focus/change callbacks.                                                                                                                                                                                                                                                                                                            |

`TNode` is a reference type implementing `IGraphNode`. Option fields are bindings,
not an automatic dependency-injection system: supply their required callbacks and
collections. Reuse result lists/pools; refresh after relevant state changes.

For temporal history, establish a baseline after loading the initial snapshot.
Subsequent document mutations record timestamped before/after node and edge
records, including metadata changes. Hosts can implement
`IGraphTimelineShellHost`; the shared shell owns the scrubber UI while the host
synchronizes its scene projection and force layout after a seek.
`GraphShellPreferences.timelineEnabled` controls history capture and UI visibility
and is enabled by default.

Position history is separately opt-in with `Timeline.PositionRecordingEnabled`
(off by default); disabling it never disables graph event recording. Set
`Timeline.PositionSampleIntervalSeconds` from 0.1 through 60 seconds (default 1
second). `ForceLayout.Step` records timestamped frames for canonical nodes while
the simulation is awake. `GraphJson` stores frames and recording settings with
the graph. Hosts own durable storage and should persist when the document changes.
Implement `IGraphPositionHistoryShellHost` to show the shared recording/interval
controls and continuous time scrubber; its capability flag can hide motion
controls and stop capture without deleting saved frames. `GraphDocument.ClearPositionHistory()`,
`ClearEventHistory()`, and `ClearHistory()` clear the selected history while
preserving the current graph; clearing event history re-establishes the current
graph as the baseline.

```csharp
GraphInspectorPresenter.Render(inspectorRoot, scene, selected,
    new GraphInspectorOptions {
        Select = node => { scene.View.SelectedId = node.Id; /* inspect only; double-click or F focuses */ },
        OpenSource = source => OpenValidatedSource(source), // Your callback.
        Actions = new[] {
            new GraphInspectorAction("org:archive", "Archive", node => Archive(node),
                isVisible: node => IsArchivable(node),
                isEnabled: node => CanArchive(node),
                disabledReason: node => "Resolve linked work first.")
        },
        AppendSections = (parent, node) => AddDomainSections(parent, node)
    });
```

Node search ranks label, type ID and node ID, supports one-edit word typos for
longer tokens, and sorts favorites first. It does **not** automatically search all
schema `Searchable` metadata or implement Job Harvester's structured query language.
Reveal/reset/filter expansion are host callbacks. Full salary/workflow/filter
semantics, investigation dialogs and custom analysis cards stay in that adapter.
Default inspector relationships navigate neighbors; use custom `Relationships`
callbacks to connect row hover/focus to your world-edge highlight state.

Shell search focus uses Ctrl/Cmd+F or slash; result arrows/Enter navigate/activate.
Queue reorder supports the grip and Alt+Up/Down; lock callbacks must prevent edits
during review/submission. The shell owns scale, drawer/dock and resize interactions.

Set `IGraphShellHost.ActionsAvailable=false` without an action provider: queue/tool
surfaces may be omitted. Independent exploration-only runtime proof is still pending.

## 7. Dataset providers

`IGraphDataProvider.Identity` identifies the source. `Load(datasetId, progress,
cancellation, completed, failed)` returns a `GraphSnapshot`; call it again to
refresh. `LoadDetails(datasetId, nodeId, ...)` returns a `GraphBatch`.

Both return `IEnumerator`: run them on your host coroutine runner. Completion/error
callbacks must occur once on the owning thread. `GraphProviderProgress` contains
message and optional fraction 0..1. Providers should honor cancellation; the
interface does not abort IO automatically. Cancel/stop owned operations on teardown.

Apply a successful same-dataset snapshot with `ReplaceSnapshot`, then synchronize
scene/rebind layout/refresh metrics and UI. On failure/cancellation, retain the last
valid graph. The contracts do not automatically load, swap documents or render UI.
Job Harvester's adapter wraps its existing loading/application pipeline; do not
copy that domain controller into a new generic consumer.

## 8. Action providers and reviewed queues

| Contract                                         | Responsibility                                                                                                   |
|--------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `GraphActionDefinition` / `GraphActionParameter` | Stable action IDs, display/accepted types, scalar parameters; optional structured `ConfigurationSchema`          |
| `GraphActionRequest`                             | Stable request ID, action/source/target IDs, scalar `Binding`/`Parameters`, optional opaque JSON `Payload`       |
| `GraphActionRun` / `GraphActionEntryProgress`    | Run ID/review token/status/revision, ordered entry statuses/fractions/errors/warnings, optional provider payload |
| `IGraphActionProvider`                           | `Actions`, `ResolveTargets`, `Review`, `Execute`, `Recover`, `Refresh`, `Control`                                |
| `IGraphActionHistoryProvider`                    | Optional `LoadActions` and `ListRuns`                                                                            |
| `GraphQueueCoordinator`                          | Per-coordinator `Busy` exclusion; `Review`, save-before-send `Submit`, `Recover`, `Control`                      |
| `GraphTaskVisual`                                | Optional renderer task-ring/status projection; host derives it from authoritative progress                       |

Queue lifecycle:

1. Resolve candidate targets and let users choose ambiguous/group targets. Capture
   stable bindings; do not infer ownership from spatial proximity.
2. Keep an ordered host draft; review with `queue.Review(draft, token, completed, failed)`.
   The coordinator snapshots list order, not a deep immutable copy of requests.
3. Lock edits while reviewing/submitting. Invalidate review after any draft change.
4. Call `queue.Submit(review, persistPending, token, completed, failed)`. The synchronous
   `Func<bool>` must durably save **review.Id, ReviewToken and provider identity**;
   false prevents execution. It must not throw for an ordinary save failure.
5. Provider execution must reject stale/changed reviews and be idempotent for that
   same run ID. After uncertain completion retain pending identity and reconcile
   with `Recover`; do not create a new submission ID or assume success/failure.
6. Refresh authoritative progress/history. `Control(runId, operation, entryId, ...)`
   uses provider-defined operations; pass null entry ID for run-level control.

Coordinator inputs/callbacks must be non-null and well formed. It does not own
persistent drafts, clear them, poll automatically, validate domain prerequisites,
choose targets or execute worker semantics. `GraphProviderException.ResponseCode`
retains transport status; `Rejected` classifies 4xx. Host/provider policy decides
which errors can safely unlock drafts; network/5xx failures may be uncertain.

Opaque payloads preserve nested provider configuration and richer worker progress.
Clone `JsonElement` values borrowed from a `JsonDocument` before disposing it.
For host-defined draft/run serialization, explicitly handle optional/Undefined
JSON values and scalar GraphValue encoding; `GraphJson` serializes graph facts,
not these DTOs. Cancellation is cooperative; disposing a coroutine cannot undo an
already accepted remote action.

## 9. Workspaces, favorites and storage

```csharp
var storage = new GraphStorage(explicitRoot, "my-host", document.DatasetId, providerIdentity);
bool saved = storage.Write("workspace", 1, "{\"version\":1,\"selectedId\":\"dev\"}");
string previous = storage.Read("workspace", 1); // null if missing/unsupported version.
```

Keys hash the full host/dataset/provider/document identities into scoped paths.
The root is explicit; storage does not choose a global directory. `Write` checks
versions and atomically replaces existing valid data with a backup; false means an
existing unsupported/corrupt document was preserved. Invalid input/IO may throw;
`Read` can throw on corrupt JSON. Preserve the data and report errors instead of
silently replacing it. `ImportLegacy(name, path)` copies only if destination is absent.

Define DTOs for positions/pins, camera, favorites, preferences and drafts; do not
expect `GraphViewState` or the shell to serialize them. Use one owner/main thread;
there is no multi-process transaction/locking protocol. Scope pending submissions
to their provider so backend changes cannot reroute recovery.

Job Harvester deliberately uses a `legacy-workspace` compatibility scope for its
existing cross-dataset preferences/favorites and backend-bound drafts. Its five
legacy files are copied, not moved/rewritten. This compatibility choice is not the
recommended identity scheme for a new dataset-specific consumer.

## 10. Source map, validation and AI handoff

Core declarations are under [Runtime](../Runtime/); rendering/UI under
[Runtime/Presentation](../Runtime/Presentation/). Start with:

| Task                                        | Source / working consumer reference                                                                          |
|---------------------------------------------|--------------------------------------------------------------------------------------------------------------|
| Facts, updates, JSON                        | `GraphRecords.cs`, `GraphDocument.cs`, `GraphJson.cs`                                                        |
| Discrete revision DAGs and change summaries | `GraphRevisionTimeline.cs`; CodebaseGraph history/provider integration                                       |
| Descriptors and live projection             | `GraphSchema.cs`, `GraphViewState.cs`, `GraphScene.cs`, `GraphPresentationContracts.cs`                      |
| Data/actions/storage                        | `GraphProviders.cs`, `GraphQueueCoordinator.cs`, `GraphStorage.cs`                                           |
| Rendering ownership/resources               | `GraphRenderingSession.cs`, `GraphResources.cs`, `GraphRenderer.cs`                                          |
| Camera/layout/UI                            | `GraphNavigation.cs`, `ForceLayout.cs`, named presenter files, `GraphShellHost.cs`/`GraphShellController.cs` |
| Host setup and schema policies              | `JobHarvesterGraphAdapter.cs`, `GraphApplication.cs`, `GraphInterface.cs`                                    |
| Host bindings/content                       | `GraphShellBindings.cs`, `GraphSearch.cs`, `GraphFavorites.cs`, `GraphInterface.cs`                          |
| Host action transport/persistence           | `JobHarvesterProviders.cs`, `GraphCommands.cs`                                                               |

Host references above are in
[`Assets/JobHarvester/Scripts`](../../../unity/IfYouCantBeatThemFuckThem/Assets/JobHarvester/Scripts/).
Read them as adapter examples, not package dependencies. Preserve existing `.meta`
GUIDs when moving Unity assets. Three `MovedFrom` namespace strings intentionally
preserve serialization compatibility; they are not runtime Job Harvester dependencies.

Editor menus: **Graph Explorer → Settings**, **Validate semantic graph JSON**, and **Validate package resources**. The
settings window edits the package's `GraphVisualSettings` defaults, including atmosphere and data-beam controls. For package test
discovery, add the package ID to
manifest `testables` and install the host's Unity Test Framework separately.
Profiler markers use `GraphExplorer.*`.

[TESTING.md](../../../TESTING.md) is the maintained test inventory, commands and
manual handoff. [Checkpoint 3 evidence](../../../docs/graph-extraction/checkpoint-3/README.md)
records 35 package tests, 124 Editor checks, a Windows development build,
363 shell checks/36 cases and the reviewed-queue fixture. These are existing
results, not tests rerun for this documentation. Editor search-index and shutdown
allocation warnings remain untriaged. Independent clean consumers, multiple-instance
cleanup and interactive lost-response/restart acceptance are still open checks.

The historical benchmark uses 1,500 nodes/5,000 edges; see checkpoint evidence for
conditions and numbers. No 100,000-node/million-node performance claim is made.
When changing this SDK: inspect declarations, preserve facts/view/provider boundaries,
update this guide and TESTING.md as applicable, and run the smallest relevant check.

## External history events and custom drawers

Implement `IGraphTimelineEventShellHost` alongside the existing timeline host to
combine independently loaded states (such as Git commits) with document events
and recorded motion in the shared History panel. `TimelineDocument` must remain
the working/session document even when an external snapshot is displayed.

```csharp
public GraphTimelineEventTrack TimelineEvents { get; } = new GraphTimelineEventTrack();
// Register once; unregister when disposing the host.
TimelineEvents.SelectionRequested += item => LoadExternalSnapshot(item.Id);
TimelineEvents.Replace(commits.Select(commit => new GraphTimelineEvent(
    commit.Id, commit.CommitterTimeUtc, commit.Subject, commit.Details,
    tooltip: commit.Message)));
// A host button can request the same selection as a timeline marker:
TimelineEvents.RequestSelection(commitId);
// After validating and displaying the requested snapshot:
TimelineEvents.ConfirmSelection(commitId);
// On failure (retains the confirmed selection):
TimelineEvents.FailSelection(commitId, error.Message);
```

`Replace` validates unique IDs and sorts stably by UTC time; equal timestamps
preserve discovery order. It does not clamp dates or create document deltas.
The optional `tooltip` constructor argument sets `GraphTimelineEvent.Tooltip`.
The shared History panel displays this plain text in a wrapped runtime hover
popup above the event marker, preserving line breaks. Omit it (or pass null) to
use the existing event description; pass an empty string to disable the popup.
Tooltip text is independent of the label and selected-event details.
`PendingId`, `SelectedId`, `Error`, `Changed`, and `SelectionStarting` expose
asynchronous state. Hosts must cancel obsolete loads and ignore stale results.
`RequestSelection(id, false)` is reserved for playback that must continue after
confirmation. Normal requests stop shared playback. `SeekSessionTime` must switch
back to the session graph, cancel pending loads, call `SelectSession()`, then seek
its facts/motion. `ReturnToLive` restores the newest working state explicitly.
Uncached external descriptors are rebuilt by the host. Cached event descriptors and
snapshots are portable with the graph through the timeline workspace described below.

Implement `IGraphCustomDrawerShellHost.CustomDrawers` to add optional left-rail
windows without changing other hosts. Return a stable list of
`new GraphShellDrawer("Repo", "Repo", "list", content)` descriptors before
`CompleteShell()`. Content is attached to the existing drawer scroll container;
IDs must be unique and distinct from built-in drawer IDs. The shell handles rail
activation, close/Escape, focus release, and compact-layout exclusivity.

## Timeline ranges, portable caches, and saved zoom

Shift-drag over the shared History slider or marker strip to select an inclusive
UTC time range in either direction. This does not seek the graph. **Cache Timeline**
becomes available when the range contains events. Cached external markers turn
green; the selected range is shaded blue. The button shows cancellation while a
cache operation is running. Completed events survive cancellation or a later failure;
click again to retry. Ctrl-wheel zoom remains anchored under the cursor.

`GraphDocument.Timeline.Workspace` owns this state, independently of shell preferences:

```csharp
var workspace = document.Timeline.Workspace;
workspace.SelectRange(startUtc, endUtc); // inclusive, reversed endpoints accepted
workspace.SetView(viewStartUtc, viewEndUtc); // actual timestamps, not percentages
workspace.SetView(null, null); // restore the full timeline
var selected = workspace.SelectedEvents(TimelineEvents.Events);
workspace.Changed += SaveGraph; // debounce disk writes in an interactive host
```

Attach an `IGraphTimelineEventProvider` to `GraphTimelineEventTrack.Provider`. Its
`CacheContext` must be stable for identical output and must change with references,
parser configuration, credentials/data version, or other inputs that affect output.
Event IDs must identify immutable states inside that context. Implement
`LoadEvent(id, token, completed, failed)` as an owner-thread coroutine returning
an immutable `GraphSnapshot` for the same dataset. Do not modify the active document.
The library drives it from the Cache Timeline button. Outside the UI, drive:

```csharp
yield return workspace.CacheSelection(document, TimelineEvents,
    TimelineEvents.Provider, cancellationToken);
// IsCaching, Completed, Total, Error, Changed expose progress/failure.
// Existing document events and motion already persist in baseline/change history.
```

In the host's event selection handler, use the shared cache-aware loading API before
projecting the returned snapshot, then confirm or fail selection as usual:

```csharp
yield return TimelineEvents.LoadEvent(document, id, token, ShowSnapshot, ReportError);
// Save with the rest of the graph, including selection, absolute zoom, descriptors,
// cached snapshots, baseline, deltas, and motion:
string json = GraphJson.Serialize(document);
GraphDocument restored = GraphJson.Parse(json);
TimelineEvents.RestoreCachedEvents(restored.Timeline.Workspace);
```

When no provider is attached, `LoadEvent` can replay a uniquely cached event offline.
With a provider attached it requires a matching context, so changing analyzer inputs
does not silently serve stale snapshots. `RestoreCachedEvents(workspace, context)`
can filter descriptors to a known context before normal discovery replaces them.
`TryGet(context, id, out snapshot)`, `Store`, `IsCached`, `CachedEvents`, `ClearCache`,
`ClearSelection`, and `RestoreFrom` are available to custom hosts. `Store` is for
trusted snapshots; the loading and JSON boundaries validate dataset ownership and
referential integrity. Cached snapshots are content-deduplicated on serialization;
identical states across multiple events are written once. Memory grows with the
number of distinct states explicitly cached by the user.

`GraphJson` v1 has an optional, versioned `timelineWorkspace` extension. Older graph
files remain readable. `GraphDocument.RestoreTimelineFrom` also copies workspace
state. Hosts that already save their document on `Changed` need no separate zoom or
cache preference file. A host that does not save graphs must add its own save hook;
the library never chooses a storage directory implicitly.

`GraphShellController.TimelineControls` exposes `SelectRange`, `SetVisibleRange`,
`CacheTimeline` (start/cancel), and the current `Viewport` for custom commands and
inspection. Change the persisted visible range through `SetVisibleRange` or the
workspace rather than mutating the viewport directly.

For adapter-side performance, `GraphSnapshotCache(capacity)` is a public bounded
LRU (`TryGet`, `Store`, `Clear`) for immutable mapped snapshots, including frequently
reused parents. Scope its keys by provider configuration as well as event identity.
The CodebaseGraph adapter demonstrates a persistent, disposable analyzer worker,
content-addressed source-tree caches, incremental Roslyn trees/compilations,
configuration invalidation, and process cancellation. Git and Roslyn remain adapter
dependencies; the library has no repository or C# language dependency.

## Host-owned node progress and workflows

Supply progress separately from transient task execution. The renderer never infers
completion from a numerical fraction, task status, node type, or node emphasis:

```csharp
var progress = new Dictionary<string, GraphNodeProgressVisual>();
session.Renderer.NodeProgress = progress;
progress[nodeId] = new GraphNodeProgressVisual { Fraction = .6f, Complete = false };
// After the host has validated completion:
progress[nodeId] = new GraphNodeProgressVisual { Fraction = 1f, Complete = true };
// Remove an indicator:
progress.Remove(nodeId);
```

Update the collection on the owning Unity thread. Missing/null values hide the
indicator. Finite fractions clamp to [0,1]; nonfinite fractions are ignored.
Explicit `Complete` overrides the fraction with one and adds a checked-box badge.
Fraction one without `Complete` shows a full ring without the badge. Zero shows
only the subdued track. Arcs are green by default, camera-facing, and clockwise
from twelve o'clock. They follow node size and rendered visibility, independently
of labels, relationship filters, selection, and task rings. They do not affect
picking, layout, node color, or graph facts. Collapsed clusters require their own
host-provided progress entry; canonical children do not imply cluster completion.

`GraphVisualSettings` exposes `ShowNodeProgress`, `ProgressColor`,
`ProgressTrackColor`, `ProgressWidth` (relative to node radius),
`ProgressRadiusScale` (minimum 2.3), and `CompletionBadgeSize`. Indicators are
static under both normal and reduced motion. `VisibleProgressIndicators` reports
the number drawn in the latest renderer pass. Rings use camera-facing instanced
quads with analytic antialiasing; there are no per-node GameObjects or materials. Geometry and CPU
draw cost increase with visible indicators. Provide a textual equivalent in your
inspector, such as “3/5 done.”

`GraphWorkflowStep`, `GraphWorkflowState`, `GraphWorkflowGrouping`,
`GraphWorkflowTask`, and `GraphWorkflowValidation` are reusable workflow records.
`GraphWorkflow.Progress(states)` counts only `Complete` among applicable states;
`NotApplicable` is excluded, and an entirely inapplicable workflow returns null.
`GraphWorkflowView.Step` builds a named inspector/chip control, while `Paint`
shares JobHarvester's existing icon styling without changing its domain rules.
The host supplies task content, eligibility, persistence identities, and completion policy.
Optional `IGraphWorkflowAcceptance` records completed steps against current source;
`IGraphExternalTaskProvider` describes hosts that can open handed-off work.
Use the existing action-provider/coordinator boundary to review requests and
persist submission identity before dispatching external effects.

### Optional Codex integration

`Cashg.GraphExplorer.Codex.CodexLinks.NewChat(workspace, prompt)` encodes a
prefilled desktop composer; it does not submit or confirm execution.
`CodexLinks.Thread(id)` opens a known chat. For long prompts, hosts can save a
local instruction document and prefill a short pointer to avoid OS URL limits.

`CodexAppServerClient` accepts an injectable `ICodexTransport`;
`CodexProcessTransport(executable)` runs `codex app-server --stdio` with a hidden
window. `Start` initializes JSON-RPC; call `Pump` on the host thread to deliver
callbacks. `Request`, `Respond`, `Reject`, and `Message` expose the protocol.
No requests retry implicitly. Pending requests time out after two minutes;
disconnects fail pending calls. Dispose the client when the host closes.

The host must handle authentication, approval/input requests, task identities,
turn recovery, and permission policy. CodebaseGraph demonstrates workspace writes
with on-request approvals and persisted thread/turn IDs. Its workflow UI queues
directly, refreshes source after successful automatic runs, and records completion
in one batch. Prefilled chats use a single Done action because the composer link
has no completion callback. Freshness, applicability, and required-page checks
remain; there is no manual per-step review stage. Completion records do not prove
the semantic correctness of generated edits.
See the official [desktop links](https://learn.chatgpt.com/docs/app/commands) and
[app-server protocol](https://learn.chatgpt.com/docs/app-server).
