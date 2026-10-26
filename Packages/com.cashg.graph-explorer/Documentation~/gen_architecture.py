"""Generate ARCHITECTURE.md — public API only (top-level types, no nested classes)."""
import re
from pathlib import Path
from collections import defaultdict

root = Path(__file__).resolve().parent.parent
SKIP = ("Tests",)

PURPOSE = {
    "GraphValueType": "Discriminator for scalar metadata stored on nodes and edges.",
    "GraphValue": "Portable scalar: text, finite number, boolean, or explicit null (`GraphValue.Null`).",
    "GraphNodeRecord": "Immutable semantic node. View/layout state lives in `GraphNodeViewState`, not here.",
    "GraphEdgeRecord": "Immutable directed edge between existing node IDs.",
    "GraphSnapshot": "Complete dataset payload: id, display name, all nodes and edges.",
    "GraphBatch": "Mutable transaction buffer for `GraphDocument.Apply`.",
    "GraphIdSet": "Read-only set of opaque string IDs (neighbors, incident edges).",
    "GraphDocument": "Canonical fact store, adjacency indexes, revisions, and integrated `GraphTimeline`.",
    "GraphSchema": "Registers node types, relation types, and field definitions; optional `Validate`.",
    "GraphViewState": "Per-dataset presentation: positions, selection, focus, fold state.",
    "GraphNodeViewState": "Per-node layout/filter flags: position, velocity, pinned, visible, matches, favorite, emphasis.",
    "GraphExplorerOptions": "Render budgets passed to `GraphRenderer`.",
    "GraphTimeline": "Event history + optional position sampling; supports scrubbing via `GraphDocument.Seek`.",
    "GraphJson": "GraphJson v1 serialization (semantic snapshot + optional timeline/workspace).",
    "GraphScene": "Default live projection implementing `IGraphScene`.",
    "GraphClusterScene": "Optional wrapper: collapse/expand neighborhoods, camera-distance LOD, canonical positions.",
    "GraphPathfinder": "Directed unweighted shortest path (BFS) over projected edges.",
    "GraphPathState": "Persistent path UI state with graph-pick workflow.",
    "GraphRevisionTimeline": "Git-like revision list with lane layout; snapshots loaded by host.",
    "GraphStorage": "Scoped, version-checked JSON files with atomic replace.",
    "GraphQueueCoordinator": "Single-flight wrapper for action provider review/execute/recover.",
    "GraphWorkflow": "Static helpers mapping workflow states to labels and progress visuals.",
    "ForceLayout": "Force-directed layout (Barnes–Hut); records position frames when enabled.",
    "GraphRenderer": "URP instanced drawing, picking, path/edge highlights.",
    "GraphNavigation": "Orbit/fly/pan camera controller (Input System).",
    "GraphShellController": "Builds and drives the shared explorer shell from `IGraphShellHost`.",
    "GraphResources": "Loads packaged assets under `Resources/CashgGraphExplorer/`.",
    "GraphResourceConfiguration": "Per-host overrides for shaders, materials, and UI assets.",
    "GraphDiagnostics": "Unity Profiler marker names for layout/draw/UI.",
    "IGraphNode": "Live node facade: record + mutable view fields + adjacency.",
    "IGraphEdge": "Live edge facade over `GraphEdgeRecord`.",
    "IGraphScene": "Document + schema + projected node/edge maps + topology revision.",
    "IGraphEdgeFilter": "Host predicate to hide edge types from layout/render/pick.",
    "IGraphDataProvider": "Coroutine-based load of snapshots and incremental detail batches.",
    "IGraphActionProvider": "Review/execute/recover/control for queued graph actions.",
    "IGraphActionHistoryProvider": "Optional catalog of action definitions and past runs.",
    "IGraphTimelineEventProvider": "Loads exact graph snapshots for external timeline events.",
    "IGraphShellHost": "Host-supplied UI elements and refresh hooks for the shell.",
    "IGraphTimelineShellHost": "Seek semantic timeline by entry index.",
    "IGraphPositionHistoryShellHost": "Seek by UTC time; pause simulation during scrub.",
    "IGraphTimelineEventShellHost": "External event track + session/live return.",
    "IGraphCustomDrawerShellHost": "Extra shell drawers (filters, source panels).",
    "IGraphWorkflowAcceptance": "Host gate for marking workflow steps accepted on nodes.",
    "IGraphExternalTaskProvider": "Open external tool UI for handed-off workflow tasks.",
    "ICodexTransport": "Line-delimited stdio transport to Codex app-server.",
    "CodexAppServerClient": "JSON-RPC client; pump on host thread via `Pump()`.",
}


def brace_delta(line: str) -> int:
    return line.count("{") - line.count("}")


def parse_file(path: Path):
    text = path.read_text(encoding="utf-8")
    ns = "Cashg.GraphExplorer"
    if "namespace Cashg.GraphExplorer.Codex" in text:
        ns = "Cashg.GraphExplorer.Codex"
    elif "namespace Cashg.GraphExplorer.Editor" in text:
        ns = "Cashg.GraphExplorer.Editor"

    lines = text.splitlines()
    depth = 0
    in_namespace = False
    types = []
    pending_summary = []

    type_re = re.compile(
        r"^\s*public\s+(static\s+)?(sealed\s+)?(readonly\s+)?(class|interface|enum|struct)\s+(\w+)"
    )
    member_re = re.compile(
        r"^\s*public\s+(static\s+)?(.+?)\s+(\w+)\s*(\([^)]*\))?\s*(?:\{[^}]*\}|=>|[;=])"
    )
    iface_member_re = re.compile(r"^\s*([\w<>,\[\]\s.?]+)\s+(\w+)\s*\([^)]*\)\s*;")

    for line in lines:
        stripped = line.strip()
        if not in_namespace:
            if stripped.startswith("namespace "):
                in_namespace = True
            continue
        if stripped.startswith("///"):
            pending_summary.append(stripped[3:].strip().lstrip("/").strip())
            continue

        m = type_re.match(line)
        rel_depth = depth  # depth before this line's braces
        if m and rel_depth == 1:
            summary = " ".join(pending_summary).replace("<summary>", "").replace("</summary>", "").strip()
            pending_summary = []
            types.append(
                {
                    "kind": m.group(4),
                    "name": m.group(5),
                    "summary": summary,
                    "members": [],
                }
            )
            depth += brace_delta(line)
            continue

        if types and rel_depth == 2:
            if types[-1]["kind"] == "interface":
                im = iface_member_re.match(line)
                if im:
                    types[-1]["members"].append(f"{im.group(1).strip()} {im.group(2)}(...)")
            else:
                mm = member_re.match(line)
                if mm and " class " not in line and " interface " not in line:
                    sig = stripped.rstrip("{").strip()
                    if not sig.endswith(";") and "{" in sig:
                        sig = sig.split("{")[0].strip()
                    types[-1]["members"].append(sig)

        depth += brace_delta(line)
        if depth == 0:
            pending_summary = []

    return ns, types


sections = []
for path in sorted(root.rglob("*.cs")):
    rel = path.relative_to(root)
    if any(p in str(rel) for p in SKIP):
        continue
    ns, types = parse_file(path)
    if types:
        sections.append((str(rel).replace("\\", "/"), ns, types))

lines_out = []
lines_out += [
    "# Graph Explorer — Architecture & API Reference",
    "",
    "_Package `com.cashg.graph-explorer` v0.1.0._",
    "",
    "Complete **architecture** and **public API** reference for the Graph Explorer Unity library.",
    "Integration guide: [SDK.md](SDK.md).",
    "",
    "## Table of contents",
    "",
    "1. [Architecture overview](#architecture-overview)",
    "2. [Assemblies & dependencies](#assemblies--dependencies)",
    "3. [Core API (`Cashg.GraphExplorer`)](#core-api-cashggraphexplorer)",
    "4. [Presentation API (`Cashg.GraphExplorer.Presentation`)](#presentation-api-cashggraphexplorerpresentation)",
    "5. [Codex (`Cashg.GraphExplorer.Codex`)](#codex-cashggraphexplorercodex)",
    "6. [Editor (`Cashg.GraphExplorer.Editor`)](#editor-cashggraphexploreditor)",
    "7. [Usage examples](#usage-examples)",
    "",
    "## Architecture overview",
    "",
    "| Layer | Types | Responsibility |",
    "|-------|--------|----------------|",
    "| **Records** | `GraphNodeRecord`, `GraphEdgeRecord`, `GraphValue` | Immutable facts and metadata |",
    "| **Document** | `GraphDocument`, `GraphBatch`, `GraphTimeline` | Single-threaded store, atomic updates, history |",
    "| **Schema** | `GraphSchema`, `*Definition` | Type colors, fields, validation hints |",
    "| **View** | `GraphViewState`, `GraphNodeViewState` | Layout, selection, visibility (not in JSON) |",
    "| **Projection** | `GraphScene`, `GraphClusterScene`, `IGraphNode` | Live facades for layout/render/UI |",
    "| **IO** | `GraphJson`, `GraphStorage` | Portable files; view state excluded |",
    "| **Providers** | `IGraphDataProvider`, `IGraphActionProvider` | Host-owned backends (coroutines) |",
    "| **Presentation** | `ForceLayout`, `GraphRenderer`, presenters | URP + UI Toolkit |",
    "",
    "```mermaid",
    "flowchart LR",
    "  Provider --> Document",
    "  Document --> Scene",
    "  View --> Scene",
    "  Schema --> Scene",
    "  Scene --> Layout",
    "  Scene --> Renderer",
    "  Document --> Json",
    "```",
    "",
    "**Rules worth remembering:**",
    "",
    "- IDs are opaque, case-sensitive strings; no auto-deduplication.",
    "- `Apply` validates the whole batch; conflicting ops roll back entirely.",
    "- Editing while timeline is historical auto-returns to live (`EnsureLive`).",
    "- Layout proximity is not evidence of a semantic edge.",
    "",
    "## Assemblies & dependencies",
    "",
    "| Assembly | Unity deps | Notes |",
    "|----------|------------|-------|",
    "| `Cashg.GraphExplorer` | Core, `System.Text.Json` | Safe for non-URP tools |",
    "| `Cashg.GraphExplorer.Presentation` | URP 17.7, Input System 7.0 | Player + Editor |",
    "| `Cashg.GraphExplorer.Editor` | Editor only | Do not reference from player |",
    "",
    "---",
    "",
]

by_asm = defaultdict(list)
for rel, ns, types in sections:
    if rel.startswith("Editor/"):
        asm = "Editor"
    elif "Presentation/" in rel:
        asm = "Presentation"
    elif "CodexIntegration" in rel:
        asm = "Codex"
    else:
        asm = "Core"
    by_asm[asm].append((rel, ns, types))

titles = {
    "Core": "## Core API (`Cashg.GraphExplorer`)",
    "Presentation": "## Presentation API (`Cashg.GraphExplorer.Presentation`)",
    "Codex": "## Codex (`Cashg.GraphExplorer.Codex`)",
    "Editor": "## Editor (`Cashg.GraphExplorer.Editor`)",
}

for asm in ("Core", "Presentation", "Codex", "Editor"):
    lines_out.append(titles[asm])
    lines_out.append("")
    for rel, ns, types in sorted(by_asm.get(asm, [])):
        lines_out.append(f"### `{rel}`")
        lines_out.append("")
        lines_out.append(f"Namespace: `{ns}`")
        lines_out.append("")
        for t in types:
            lines_out.append(f"#### `{t['name']}` ({t['kind']})")
            lines_out.append("")
            blurb = t["summary"] or PURPOSE.get(t["name"], "")
            if blurb:
                lines_out.append(blurb)
                lines_out.append("")
            if t["members"]:
                lines_out.append("| Member | Declaration |")
                lines_out.append("|--------|-------------|")
                for mem in t["members"]:
                    esc = mem.replace("|", "\\|")
                    name = re.search(r"\b(\w+)\s*[\(<]", mem)
                    label = name.group(1) if name else mem.split()[-1]
                    lines_out.append(f"| `{label}` | `{esc}` |")
                lines_out.append("")
            elif t["kind"] == "enum":
                lines_out.append("_Enum constants — see source file._")
                lines_out.append("")
            else:
                lines_out.append("_See source for properties; interface/MonoBehaviour members may be split across partial declarations._")
                lines_out.append("")

lines_out += [
    "## Usage examples",
    "",
    "### Bootstrap document and scene",
    "",
    "```csharp",
    "var schema = new GraphSchema();",
    'schema.Register(new GraphNodeTypeDefinition("file", "File", shapeId: "Cube", color: Color.gray));',
    'schema.Register(new GraphRelationDefinition("imports", "Imports", springStrength: 0.5f));',
    "",
    'var doc = new GraphDocument("codebase", "My Repo");',
    "var batch = new GraphBatch();",
    'batch.Nodes.Add(new GraphNodeRecord("f1", "file", "Program.cs",',
    '    new[] { new KeyValuePair<string, GraphValue>("path", GraphValue.From("src/Program.cs")) }));',
    "doc.Apply(batch);",
    "",
    "var view = new GraphViewState(doc.DatasetId);",
    "var scene = new GraphScene(doc, schema, view);",
    "```",
    "",
    "### Force layout + render loop",
    "",
    "```csharp",
    "var profile = new GraphPresentationProfile { MetricField = \"loc\" };",
    "var layout = new ForceLayout(profile) { Mode = LayoutMode.Spatial };",
    "var renderer = new GraphRenderer(profile);",
    "",
    "layout.Bind(scene);",
    "renderer.UpdateMetrics(scene, edgeFilter: null);",
    "layout.Step(Time.deltaTime);",
    "renderer.Draw(scene, null, camera, selected, hovered, hoveredEdge);",
    "```",
    "",
    "### GraphJson round-trip",
    "",
    "```csharp",
    "doc.EstablishBaseline(DateTimeOffset.UtcNow);",
    'var json = GraphJson.Serialize(doc);',
    "var copy = GraphJson.Parse(json);",
    "```",
    "",
    "### Provider load (Unity coroutine)",
    "",
    "```csharp",
    "IEnumerator Load(IGraphDataProvider provider, string datasetId) {",
    "    GraphSnapshot snap = null; Exception err = null;",
    "    yield return provider.Load(datasetId, null, CancellationToken.None, s => snap = s, e => err = e);",
    "    if (err != null) yield break;",
    "    document.ReplaceSnapshot(snap);",
    "    scene.Synchronize();",
    "}",
    "```",
    "",
    "### Cluster + LOD",
    "",
    "```csharp",
    "var clustered = new GraphClusterScene(scene);",
    'string groupId = clustered.Collapse(new[] { "a", "b", "c" }, "Core");',
    "if (clustered.UpdateLod(camera.transform.position))",
    "    layout.Bind(clustered);",
    "```",
    "",
    "### Shortest path highlight",
    "",
    "```csharp",
    "var path = new GraphPathState(scene, e => e.Relation == \"imports\");",
    "path.SetEndpoints(\"f1\", \"f99\");",
    "using (GraphPathRendering.Bind(path, renderer)) { /* draw */ }",
    "```",
    "",
    "### Action queue",
    "",
    "```csharp",
    "var coordinator = new GraphQueueCoordinator(actionProvider);",
    "yield return coordinator.Review(draft, token, onReviewed, onFailed);",
    "yield return coordinator.Submit(review, () => store.Save(review.Id), token, onDone, onFailed);",
    "```",
    "",
    "### Scoped storage",
    "",
    "```csharp",
    'var store = new GraphStorage(root: @"C:\\GraphData", hostId: "my-app", datasetId: doc.DatasetId, providerId: "local");',
    'store.Write("preferences", supportedVersion: 1, json);',
    'var loaded = store.Read("preferences", supportedVersion: 1);',
    "```",
    "",
    "---",
    "",
    "_Regenerate this file: `python Documentation~/gen_architecture.py` from the package root._",
    "",
]

dest = Path(__file__).resolve().parent / "ARCHITECTURE.md"
dest.write_text("\n".join(lines_out), encoding="utf-8")
print(f"Wrote {dest} ({len(lines_out)} lines)")
