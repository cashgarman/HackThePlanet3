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
        public bool Initialized;
        public void Reset() { Center = Vector3.zero; Distance = 30f; Yaw = 0; Pitch = 28f; Initialized = false; }
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
        private readonly GraphVisualSettings networkSettings;
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
        private string hoveredId;
        private readonly HashSet<string> objectives = new HashSet<string>();

        public int VisibleNodes => session.Renderer.VisibleCount;
        public int VisibleEdges => session.Renderer.VisibleEdges;
        public int NodeCount => scene.Nodes.Count;

        public GraphViewport(List<NetNode> nodes, List<NetLink> links, string selected, bool preview,
            Action<string> select, GraphPathResult route = null, GraphCameraState cameraState = null)
            : this(BuildNetwork(nodes, links, preview), selected, select, false, route, cameraState)
        {
        }

        public GraphViewport(Career career, string selected, Action<string> select, GraphCameraState cameraState = null, GameCatalog catalog = null)
            : this(BuildResearch(career,catalog??GameCatalog.Default), selected, select, true, null, cameraState)
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
            camera.cullingMask = 1; // GraphRenderer submits its camera-scoped instances on the default layer.
            camera.fieldOfView = 48;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;

            center = savedCamera.Center;
            distance = savedCamera.Distance;
            yaw = savedCamera.Yaw;
            pitch = savedCamera.Pitch;
            PlaceCamera();
            var resources = new GraphResourceConfiguration();
            if (!research) resources = NetworkAppearance.Create(out networkSettings);
            session = GraphRenderingSession.Initialize(camera, resources);
            session.Layout.Bind(scene);
            session.Layout.Paused = true;
            session.Renderer.Size = SizeMapping.Uniform;
            session.Renderer.EdgeWidthScale = 3f;
            // The simulation stores both traversal directions; the visual graph draws each link once.
            session.Renderer.ActivePath = route == null ? null : new GraphPathResult(route.StartId, route.EndId, route.NodeIds,
                route.EdgeIds.Select(id => scene.Edges.ContainsKey(id) ? id : string.Join(">", id.Split('>').Reverse())));
            session.Renderer.UpdateMetrics(scene, edgeFilter);

            if (research) { links = new LinkOverlay(scene, camera, selected, route, true); Add(links); }
            labels = new VisualElement();
            labels.style.position = Position.Absolute;
            labels.style.left = labels.style.right = labels.style.top = labels.style.bottom = 0;
            labels.pickingMode = PickingMode.Ignore;
            Add(labels);
            foreach (var node in scene.Nodes.Values)
            {
                if (node.Record.Properties.TryGetValue("objective", out var objective) && objective.Boolean) objectives.Add(node.Id);
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
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (disposed || contentRect.width <= 0 || contentRect.height <= 0) return;
                // Match the render target to the panel; projection and picking share this aspect.
                int width = Mathf.Clamp(Mathf.RoundToInt(contentRect.width), 256, 2048);
                int height = Mathf.Clamp(Mathf.RoundToInt(contentRect.height), 256, 2048);
                if (texture.width != width || texture.height != height)
                { texture.Release(); texture.width = width; texture.height = height; texture.Create(); }
                camera.aspect = (float)width / height;
                if (!savedCamera.Initialized) { ResetView(); savedCamera.Initialized = true; }
                PositionLabels();
            });
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
                if (!dragging)
                {
                    var local = this.WorldToLocal(evt.position);
                    var nextHoveredId = PickNode(local);
                    if (hoveredId != nextHoveredId)
                    {
                        hoveredId = nextHoveredId;
                        PositionLabels();
                    }
                    tooltip = hoveredId == null ? "Drag to pan / Right-drag to orbit / Wheel to zoom" : scene.Nodes[hoveredId].Label;
                    return;
                }
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
                if (!orbiting && Vector2.Distance(dragStart, evt.position) < 5)
                {
                    var hit = PickNode(this.WorldToLocal(evt.position));
                    if (hit != null) select(hit);
                }
                dragging = false;
                this.ReleasePointer(evt.pointerId);
            });
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hoveredId = null;
                PositionLabels();
            });
        }

        private static GraphScene BuildNetwork(List<NetNode> nodes, List<NetLink> links, bool preview)
        {
            var document = new GraphDocument("workstation-network", "Live network");
            var schema = new GraphSchema();
            foreach(var group in nodes.GroupBy(n=>string.IsNullOrEmpty(n.typeId)?n.kind.ToString():n.typeId))
            {
                var node=group.First();string glyph=node.kind.ToString();var color=node.color.a>0?node.color:Palette.Kind(node.kind);
                schema.Register(new GraphNodeTypeDefinition(group.Key,group.Key,glyph,color,.95f));
                schema.Register(new GraphNodeTypeDefinition(group.Key+"-owned",group.Key,glyph,Palette.Lime,.95f));
                schema.Register(new GraphNodeTypeDefinition(group.Key+"-objective",group.Key,glyph,Palette.Amber,1.1f));
            }
            schema.Register(new GraphRelationDefinition("network", "Connection", Palette.Cyan));
            foreach (var node in nodes.Where(n => preview || n.known))
                document.UpsertNode(new GraphNodeRecord(node.id, (string.IsNullOrEmpty(node.typeId)?node.kind.ToString():node.typeId) + (node.controlled ? "-owned" : node.objective ? "-objective" : ""), node.label,
                    new Dictionary<string, GraphValue> { ["objective"] = GraphValue.From(node.objective) }));
            foreach (var link in links.Where(l => document.Nodes.ContainsKey(l.a) && document.Nodes.ContainsKey(l.b)))
                document.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));
            var scene = new GraphScene(document, schema);
            foreach (var node in nodes.Where(n => document.Nodes.ContainsKey(n.id)))
            {
                var visual = scene.Nodes[node.id];
                visual.Position = node.position;
                visual.Pinned = true;
                visual.Emphasis = node.objective && !node.collected ? ToolEmphasis.Next : ToolEmphasis.None;
            }
            return scene;
        }

        private static GraphScene BuildResearch(Career career,GameCatalog catalog)
        {
            var document = new GraphDocument("workstation-research", "Research network");
            var schema = new GraphSchema();
            foreach (var branch in catalog.Technologies.Select(t => t.branch).Distinct())
                schema.Register(new GraphNodeTypeDefinition(branch, branch, "default",
                    Palette.Branch(branch), .95f));
            schema.Register(new GraphRelationDefinition("requires", "Requires", Palette.Cyan));
            foreach (var tech in catalog.Technologies)
                document.UpsertNode(new GraphNodeRecord(tech.id, tech.branch, tech.title));
            foreach (var tech in catalog.Technologies)
                foreach (var parent in tech.parents)
                    document.UpsertEdge(new GraphEdgeRecord(parent + ">" + tech.id, parent, tech.id, "requires"));
            var scene = new GraphScene(document, schema);
            foreach (var tech in catalog.Technologies)
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
            if (state.Active && !research && state.Op.task != null && !string.IsNullOrEmpty(state.Op.task.target))
                session.Renderer.NodeProgress = new Dictionary<string, GraphNodeProgressVisual>
                { [state.Op.task.target] = new GraphNodeProgressVisual { Fraction = state.Progress } };
            else session.Renderer.NodeProgress = null;
            scene.Nodes.TryGetValue(selectedId, out var selected);
            scene.Nodes.TryGetValue(hoveredId ?? "", out var hovered);
            session.Renderer.Draw(scene, edgeFilter, camera, selected, hovered, null);
        }

        private void PositionLabels()
        {
            if (disposed || contentRect.width <= 0 || contentRect.height <= 0) return;
            links?.MarkDirtyRepaint();
            foreach (var pair in nodeButtons.OrderBy(p => p.Key == selectedId ? 0 : objectives.Contains(p.Key) ? 1 : 2)
                         .ThenBy(p => Vector3.Distance(camera.transform.position, scene.Nodes[p.Key].Position)))
            {
                var point = camera.WorldToScreenPoint(scene.Nodes[pair.Key].Position);
                // Keep small screen-space text on whole panel pixels while the camera moves.
                // Fractional UI positions make glyph edges shimmer during orbit and zoom.
                var rect = new Rect(Mathf.Round(point.x / texture.width * contentRect.width - (research ? 59 : 52)),
                    Mathf.Round((1 - point.y / texture.height) * contentRect.height + 15), research ? 118 : 104, 26);
                bool priority = pair.Key == selectedId;
                bool active = pair.Key == selectedId || pair.Key == hoveredId;
                bool show = active && point.z > 0 && rect.xMax > 0 && rect.xMin < contentRect.width && rect.yMin >= 0 && rect.yMax < contentRect.height;
                var display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (pair.Value.resolvedStyle.display != display) pair.Value.style.display = display;
                if (!show) continue;
                if (!Mathf.Approximately(pair.Value.resolvedStyle.left, rect.x)) pair.Value.style.left = rect.x;
                if (!Mathf.Approximately(pair.Value.resolvedStyle.top, rect.y)) pair.Value.style.top = rect.y;
            }
        }

        private string PickNode(Vector2 local)
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return null;
            return session.Renderer.Pick(scene, camera, new Vector2(local.x / contentRect.width * texture.width,
                (1 - local.y / contentRect.height) * texture.height))?.Id;
        }

        public void Zoom(float factor)
        {
            distance = Mathf.Clamp(distance * factor, 4, 180);
            PlaceCamera();
            PositionLabels();
        }

        public void ResetView()
        {
            if (scene.Nodes.Count == 0) return;
            var bounds = new Bounds(scene.Nodes.Values.First().Position, Vector3.one * 3);
            foreach (var node in scene.Nodes.Values) bounds.Encapsulate(new Bounds(node.Position, Vector3.one * 3));
            center = bounds.center;
            var rotation = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0));
            float tangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            distance = 4;
            foreach (var node in scene.Nodes.Values)
            {
                var point = rotation * (node.Position - center);
                distance = Mathf.Max(distance, Mathf.Max((Mathf.Abs(point.y) + 2) / tangent,
                    (Mathf.Abs(point.x) + 2) / (tangent * camera.aspect)) - point.z);
            }
            distance *= 1.12f;
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
                if (camera == null) return;
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
            if (networkSettings != null) Object.Destroy(networkSettings);
            camera.targetTexture = null;
            texture.Release();
            Object.Destroy(texture);
            Object.Destroy(camera.gameObject);
        }
    }
}
