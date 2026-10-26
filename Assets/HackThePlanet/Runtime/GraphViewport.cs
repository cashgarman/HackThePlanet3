using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
    public sealed class GraphCameraState
    {
        public Vector3 Center;
        public float Distance = 30f;
        public float Yaw;
        public float Pitch = 28f;
        public void Reset() { Center = Vector3.zero; Distance = 30f; Yaw = 0; Pitch = 28f; }
    }

    // A workstation-sized view of the package's actual 3D renderer. The host owns
    // gameplay state and the camera; GraphExplorer owns graph drawing and effects.
    public sealed class GraphViewport : VisualElement, IDisposable
    {
        private sealed class AllEdges : IGraphEdgeFilter
        {
            public bool EdgeMatches(IGraphEdge edge) => true;
        }

        private readonly GraphScene scene;
        private readonly Camera camera;
        private readonly RenderTexture texture;
        private readonly GraphRenderingSession session;
        private readonly LinkOverlay links;
        private readonly VisualElement labels;
        private readonly Dictionary<string, Button> nodeButtons = new Dictionary<string, Button>();
        private readonly IGraphEdgeFilter edgeFilter = new AllEdges();
        private readonly string selectedId;
        private readonly GraphCameraState savedCamera;
        private readonly bool research;
        private Vector3 center;
        private float distance = 30f;
        private float yaw, pitch = 28f;
        private bool dragging;
        private bool orbiting;
        private Vector2 dragStart;
        private Vector3 centerStart;
        private float yawStart, pitchStart;
        private bool disposed;

        public int VisibleNodes => session.Renderer.VisibleCount;
        public int VisibleEdges => session.Renderer.VisibleEdges;
        public int NodeCount => scene.Nodes.Count;

        public GraphViewport(List<NetNode> nodes, List<NetLink> links, string selected, bool preview,
            Action<string> select, GraphPathResult route = null, GraphCameraState cameraState = null)
            : this(BuildNetwork(nodes, links, preview), selected, select, false, route, cameraState)
        {
        }

        public GraphViewport(Career career, string selected, Action<string> select, GraphCameraState cameraState = null)
            : this(BuildResearch(career), selected, select, true, null, cameraState)
        {
        }

        private GraphViewport(GraphScene graph, string selected, Action<string> select, bool research,
            GraphPathResult route, GraphCameraState cameraState)
        {
            scene = graph;
            selectedId = selected;
            savedCamera = cameraState ?? new GraphCameraState();
            this.research = research;
            AddToClassList("graph-viewport");

            texture = new RenderTexture(1024, 640, 24, RenderTextureFormat.ARGB32)
            { name = "Workstation GraphExplorer", antiAliasing = 2 };
            texture.Create();
            var image = new Image { image = texture, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute;
            image.style.left = image.style.right = image.style.top = image.style.bottom = 0;
            image.pickingMode = PickingMode.Ignore;
            Add(image);

            var cameraObject = new GameObject("Workstation GraphExplorer Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.targetTexture = texture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Hex("071014");
            camera.cullingMask = 0;
            camera.fieldOfView = 48;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;

            center = savedCamera.Center;
            distance = savedCamera.Distance;
            yaw = savedCamera.Yaw;
            pitch = savedCamera.Pitch;
            PlaceCamera();
            session = GraphRenderingSession.Initialize(camera, new GraphResourceConfiguration());
            session.Layout.Bind(scene);
            session.Layout.Paused = true;
            session.Renderer.Size = SizeMapping.Uniform;
            session.Renderer.EdgeWidthScale = 3f;
            session.Renderer.ActivePath = route;
            session.Renderer.UpdateMetrics(scene, edgeFilter);

            links = new LinkOverlay(scene, camera, selected, route, research);
            Add(links);
            labels = new VisualElement();
            labels.style.position = Position.Absolute;
            labels.style.left = labels.style.right = labels.style.top = labels.style.bottom = 0;
            labels.pickingMode = PickingMode.Ignore;
            Add(labels);
            foreach (var node in scene.Nodes.Values)
            {
                var button = new Button(() => select(node.Id)) { name = (research ? "tech-" : "node-") + node.Id,
                    text = node.Label, tooltip = node.Label };
                button.style.position = Position.Absolute;
                button.style.width = research ? 118 : 104;
                button.style.height = 26;
                button.style.fontSize = 10;
                button.style.unityTextAlign = TextAnchor.MiddleCenter;
                button.style.color = node.Id == selected ? Palette.Lime : Palette.Hex("C8DBD5");
                button.style.backgroundColor = node.Id == selected ? Palette.Hex("254337") : Palette.Hex("12242A");
                button.style.borderBottomWidth = button.style.borderTopWidth = 1;
                button.style.borderLeftWidth = button.style.borderRightWidth = 1;
                var tint = scene.Schema.NodeTypes[node.Record.TypeId].Color;
                button.style.borderBottomColor = button.style.borderTopColor = tint;
                button.style.borderLeftColor = button.style.borderRightColor = tint;
                labels.Add(button);
                nodeButtons.Add(node.Id, button);
            }
            RegisterCallback<GeometryChangedEvent>(_ => PositionLabels());
            RegisterCallback<WheelEvent>(evt =>
            {
                Zoom(evt.delta.y > 0 ? 1.12f : .89f);
                evt.StopPropagation();
            });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if ((evt.button != 0 && evt.button != 1) || evt.target is Button) return;
                dragging = true;
                orbiting = evt.button == 1;
                dragStart = evt.position;
                centerStart = center;
                yawStart = yaw;
                pitchStart = pitch;
                this.CapturePointer(evt.pointerId);
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                var delta = (Vector2)evt.position - dragStart;
                if (orbiting)
                {
                    yaw = yawStart + delta.x * .35f;
                    pitch = Mathf.Clamp(pitchStart - delta.y * .3f, -65, 65);
                }
                else center = centerStart + (-camera.transform.right * delta.x + camera.transform.up * delta.y) * distance / 900f;
                PlaceCamera();
                PositionLabels();
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                this.ReleasePointer(evt.pointerId);
            });
        }

        private static GraphScene BuildNetwork(List<NetNode> nodes, List<NetLink> links, bool preview)
        {
            var document = new GraphDocument("workstation-network", "Live network");
            var schema = new GraphSchema();
            foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
                schema.Register(new GraphNodeTypeDefinition(kind.ToString(), kind.ToString(),
                    "default", Palette.Kind(kind), .95f));
            schema.Register(new GraphRelationDefinition("network", "Connection", Palette.Cyan));
            foreach (var node in nodes.Where(n => preview || n.known))
                document.UpsertNode(new GraphNodeRecord(node.id, node.kind.ToString(), node.label));
            foreach (var link in links.Where(l => document.Nodes.ContainsKey(l.a) && document.Nodes.ContainsKey(l.b)))
                document.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));
            var scene = new GraphScene(document, schema);
            foreach (var node in nodes.Where(n => document.Nodes.ContainsKey(n.id)))
            {
                var visual = scene.Nodes[node.id];
                // Keep the contract's progression map readable in X/Y while distributing
                // each row and column through depth so the camera sees a real 3D network.
                var depth = node.kind == NodeKind.Gateway
                    ? -5f
                    : (node.column - 2f) * 2.2f + (node.row - 1f) * 1.4f;
                visual.Position = new Vector3((node.x - 500) / 43f, (250 - node.y) / 43f, depth);
                visual.Pinned = true;
            }
            return scene;
        }

        private static GraphScene BuildResearch(Career career)
        {
            var document = new GraphDocument("workstation-research", "Research network");
            var schema = new GraphSchema();
            foreach (var branch in GameContent.Tech.Select(t => t.branch).Distinct())
                schema.Register(new GraphNodeTypeDefinition(branch, branch, "default",
                    Palette.Branch(branch), .95f));
            schema.Register(new GraphRelationDefinition("requires", "Requires", Palette.Cyan));
            foreach (var tech in GameContent.Tech)
                document.UpsertNode(new GraphNodeRecord(tech.id, tech.branch, tech.title));
            foreach (var tech in GameContent.Tech)
                foreach (var parent in tech.parents)
                    document.UpsertEdge(new GraphEdgeRecord(parent + ">" + tech.id, parent, tech.id, "requires"));
            var scene = new GraphScene(document, schema);
            foreach (var tech in GameContent.Tech)
            {
                var node = scene.Nodes[tech.id];
                // Research branches occupy distinct depth bands, with individual nodes
                // staggered inside each band to avoid a single planar layer.
                var branchDepth = tech.branch == "RECON" ? -5f
                    : tech.branch == "INTRUSION" ? 5f
                    : tech.branch == "STEALTH" ? -2.5f
                    : tech.branch == "CONTROL" ? 2.5f : 0f;
                var nodeDepth = branchDepth + (tech.position.y - 310f) / 100f;
                node.Position = new Vector3((tech.position.x - 500) / 43f,
                    (310 - tech.position.y) / 43f, nodeDepth);
                node.Pinned = true;
                node.Emphasis = career.unlocked.Contains(tech.id) ? ToolEmphasis.None : ToolEmphasis.Done;
            }
            return scene;
        }

        public void Tick(GameSimulation state, SignalEffects effects)
        {
            if (disposed) return;
            session.Renderer.ReducedMotion = effects != SignalEffects.Full;
            if (state.Active && !research && state.Op.task != null)
                session.Renderer.NodeProgress = new Dictionary<string, GraphNodeProgressVisual>
                { [state.Op.task.target] = new GraphNodeProgressVisual { Fraction = state.Progress } };
            else session.Renderer.NodeProgress = null;
            scene.Nodes.TryGetValue(selectedId, out var selected);
            session.Renderer.Draw(scene, edgeFilter, camera, selected, null, null);
            PositionLabels();
        }

        private void PositionLabels()
        {
            if (disposed || contentRect.width <= 0 || contentRect.height <= 0) return;
            links.MarkDirtyRepaint();
            foreach (var pair in nodeButtons)
            {
                var point = camera.WorldToScreenPoint(scene.Nodes[pair.Key].Position);
                pair.Value.style.display = point.z > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (point.z <= 0) continue;
                pair.Value.style.left = point.x / texture.width * contentRect.width - (research ? 59 : 52);
                pair.Value.style.top = (1 - point.y / texture.height) * contentRect.height + 23;
            }
        }

        public void Zoom(float factor)
        {
            distance = Mathf.Clamp(distance * factor, 12, 58);
            PlaceCamera();
            PositionLabels();
        }

        public void ResetView()
        {
            center = Vector3.zero;
            distance = 30;
            yaw = 0;
            pitch = 28;
            PlaceCamera();
            PositionLabels();
        }

        private void PlaceCamera()
        {
            var orientation = Quaternion.Euler(pitch, yaw, 0);
            camera.transform.rotation = orientation;
            camera.transform.position = center + orientation * new Vector3(0, 0, -distance);
        }

        private sealed class LinkOverlay : VisualElement
        {
            private readonly GraphScene scene;
            private readonly Camera camera;
            private readonly string selected;
            private readonly HashSet<string> path;
            private readonly bool research;

            public LinkOverlay(GraphScene scene, Camera camera, string selected, GraphPathResult route, bool research)
            {
                this.scene = scene;
                this.camera = camera;
                this.selected = selected;
                this.research = research;
                path = new HashSet<string>(route?.EdgeIds ?? Array.Empty<string>());
                style.position = Position.Absolute;
                style.left = style.right = style.top = style.bottom = 0;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                foreach (var edge in scene.Edges.Values)
                {
                    var a = scene.Nodes[edge.From];
                    var b = scene.Nodes[edge.To];
                    var start = camera.WorldToScreenPoint(a.Position);
                    var end = camera.WorldToScreenPoint(b.Position);
                    if (start.z <= 0 || end.z <= 0) continue;
                    var highlight = path.Contains(edge.Id);
                    var related = edge.From == selected || edge.To == selected;
                    var tint = research ? scene.Schema.NodeTypes[b.Record.TypeId].Color : Palette.Cyan;
                    tint.a = highlight ? .9f : related ? .65f : .38f;
                    painter.strokeColor = tint;
                    painter.lineWidth = highlight ? 2 : related ? 1.5f : 1;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(start.x / camera.pixelWidth * contentRect.width,
                        (1 - start.y / camera.pixelHeight) * contentRect.height));
                    painter.LineTo(new Vector2(end.x / camera.pixelWidth * contentRect.width,
                        (1 - end.y / camera.pixelHeight) * contentRect.height));
                    painter.Stroke();
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            savedCamera.Center = center;
            savedCamera.Distance = distance;
            savedCamera.Yaw = yaw;
            savedCamera.Pitch = pitch;
            session.Dispose();
            camera.targetTexture = null;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(camera.gameObject);
        }
    }
}
