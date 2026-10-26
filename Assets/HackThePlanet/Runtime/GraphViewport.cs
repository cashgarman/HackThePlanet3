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
        public bool UserAdjusted;
        public int NodeCount;
        public void Reset() { Center = Vector3.zero; Distance = 30f; Yaw = 0; Pitch = 28f; Initialized = false;UserAdjusted=false;NodeCount=0; }
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
        private bool fitPending;

        public int VisibleNodes => session.Renderer.VisibleCount;
        public int VisibleEdges => session.Renderer.VisibleEdges;
        public int NodeCount => scene.Nodes.Count;

        public GraphViewport(List<NetNode> nodes, List<NetLink> links, string selected, bool preview,
            Action<string> select, GraphPathResult route = null, GraphCameraState cameraState = null, ContractIntel intel = ContractIntel.Full)
            : this(BuildNetwork(nodes, links, preview, intel), preview && intel != ContractIntel.Full ? "" : selected, select, false, route, cameraState)
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
            fitPending=!savedCamera.Initialized||(!savedCamera.UserAdjusted&&savedCamera.NodeCount!=graph.Nodes.Count);
            savedCamera.NodeCount=graph.Nodes.Count;
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

            links = new LinkOverlay(scene, camera, selected, route, research);Add(links);
            labels = new VisualElement();
            labels.style.position = Position.Absolute;
            labels.style.left = labels.style.right = labels.style.top = labels.style.bottom = 0;
            labels.pickingMode = PickingMode.Ignore;
            Add(labels);
            foreach (var node in scene.Nodes.Values)
            {
                var button = new Button(() => select(node.Id)) { name = (research ? "tech-" : "node-") + node.Id,
                    text = node.Label };
                button.style.display = DisplayStyle.None;
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
                if (fitPending) { ResetView(); savedCamera.Initialized = true;fitPending=false; }
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
                if (!dragging) return;
                var delta = (Vector2)evt.position - dragStart;
                if(delta.sqrMagnitude>25)savedCamera.UserAdjusted=true;
                if (orbiting)
                {
                    yaw = yawStart + delta.x * .35f;
                    pitch = Mathf.Clamp(pitchStart + delta.y * .3f, -65, 65);
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

        }

        private static GraphScene BuildNetwork(List<NetNode> nodes, List<NetLink> links, bool preview, ContractIntel intel)
        {
            var document = new GraphDocument("workstation-network", "Live network");
            var schema = new GraphSchema();
            // Redact at the graph-document boundary so labels, picking, models and effects
            // all receive the same anonymous topology, without changing simulation nodes.
            if (preview && intel != ContractIntel.Full)
            {
                schema.Register(new GraphNodeTypeDefinition("unknown", "Unknown system", "default", Palette.Cyan, .95f));
                schema.Register(new GraphRelationDefinition("network", "Connection", Palette.Cyan));
                if (intel == ContractIntel.Topology)
                {
                    foreach (var node in nodes) document.UpsertNode(new GraphNodeRecord(node.id, "unknown", "UNKNOWN SYSTEM"));
                    foreach (var link in links) document.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));
                }
                var redacted = new GraphScene(document, schema);
                foreach (var node in nodes.Where(n => redacted.Nodes.ContainsKey(n.id)))
                { redacted.Nodes[node.id].Position = node.position; redacted.Nodes[node.id].Pinned = true; }
                return redacted;
            }
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
                    new Dictionary<string, GraphValue> {
                        ["objective"] = GraphValue.From(node.objective&&!node.collected),
                        ["frontier"] = GraphValue.From(!preview&&node.known&&!node.controlled&&links.Any(l=>(l.a==node.id&&nodes.Any(n=>n.id==l.b&&n.controlled))||(l.b==node.id&&nodes.Any(n=>n.id==l.a&&n.controlled))))
                    }));
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
                document.UpsertNode(new GraphNodeRecord(tech.id, tech.branch, tech.title,new Dictionary<string,GraphValue>{["affordable"]=GraphValue.From(!career.unlocked.Contains(tech.id)&&tech.cost<=career.xp&&tech.parents.All(career.unlocked.Contains))}));
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
            session.Renderer.Draw(scene, edgeFilter, camera, selected, null, null);
        }

        private void PositionLabels()
        {
            if (disposed || contentRect.width <= 0 || contentRect.height <= 0) return;
            links.SelectionVisible = false;
            foreach (var pair in nodeButtons)
            {
                var button = pair.Value;
                button.style.display = DisplayStyle.None;
                if (pair.Key != selectedId) continue;
                var node = scene.Nodes[pair.Key];
                var point = camera.WorldToViewportPoint(node.Position);
                if (point.z <= camera.nearClipPlane || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) continue;

                // Project the model's bounding sphere into the same panel coordinates as its card.
                float radius = session.Renderer.Radius(node);
                var edge = camera.WorldToViewportPoint(node.Position + camera.transform.up * radius);
                float halfSize = Mathf.Max(10, Mathf.Abs(edge.y - point.y) * contentRect.height + 4);
                var nodeCenter = new Vector2(point.x * contentRect.width, (1 - point.y) * contentRect.height);
                var reticle = new Rect(nodeCenter.x - halfSize, nodeCenter.y - halfSize, halfSize * 2, halfSize * 2);
                float width = Mathf.Min((research ? 118 : 104) * textScale, contentRect.width);
                float height = 26 * textScale;
                float top = reticle.yMax + 12;
                if (top + height > contentRect.height) top = reticle.yMin - 12 - height;
                var card = new Rect(Mathf.Round(Mathf.Clamp(nodeCenter.x - width / 2, 0, contentRect.width - width)),
                    Mathf.Round(Mathf.Clamp(top, 0, Mathf.Max(0, contentRect.height - height))), width, height);
                button.style.display = DisplayStyle.Flex;
                button.style.width = width;
                button.style.left = card.x;
                button.style.top = card.y;
                links.SelectionVisible = true;
                links.SelectionRect = reticle;
                links.CardRect = card;
            }
            links.MarkDirtyRepaint();
        }

        private float textScale=1;
        public void SetTextScale(float scale)
        {
            textScale=scale;
            foreach(var button in nodeButtons.Values)
            {
                button.style.width=Mathf.Min((research?118:104)*scale,Mathf.Max(1,contentRect.width));
                button.style.height=26*scale;
                button.style.whiteSpace=WhiteSpace.Normal;
            }
            PositionLabels();
        }

        private string PickNode(Vector2 local)
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return null;
            return session.Renderer.Pick(scene, camera, new Vector2(local.x / contentRect.width * texture.width,
                (1 - local.y / contentRect.height) * texture.height))?.Id;
        }

        public void Zoom(float factor)
        {
            savedCamera.UserAdjusted=true;
            distance = Mathf.Clamp(distance * factor, 4, 180);
            PlaceCamera();
            PositionLabels();
        }

        public void ResetView()
        {
            savedCamera.UserAdjusted=false;
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
            public bool SelectionVisible;
            public Rect SelectionRect, CardRect;

            public LinkOverlay(GraphScene scene, Camera camera, string selected, GraphPathResult route, bool research)
            {
                this.scene = scene;
                this.camera = camera;
                this.selected = selected;
                this.research = research;
                path = new HashSet<string>((route?.EdgeIds??Array.Empty<string>()).SelectMany(id=>new[]{id,string.Join(">",id.Split('>').Reverse())}));
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
                    tint.a = highlight ? .9f : related ? .65f : research ? .38f : .19f;
                    painter.strokeColor = tint;
                    painter.lineWidth = highlight ? 2 : related ? 1.5f : 1;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(start.x / camera.pixelWidth * contentRect.width,
                        (1 - start.y / camera.pixelHeight) * contentRect.height));
                    painter.LineTo(new Vector2(end.x / camera.pixelWidth * contentRect.width,
                        (1 - end.y / camera.pixelHeight) * contentRect.height));
                    painter.Stroke();
                }
                if (!SelectionVisible) return;
                painter.strokeColor = Palette.Lime;
                painter.lineWidth = 1.5f;
                var corners = Corners(SelectionRect);
                painter.BeginPath();
                painter.MoveTo(corners[0]);
                for (int i = 1; i < corners.Length; i++) painter.LineTo(corners[i]);
                painter.ClosePath();
                painter.Stroke();

                // Choose the two facing corners on each rectangle, then pair without crossing.
                var cardCorners = Corners(CardRect).OrderBy(p => (p - SelectionRect.center).sqrMagnitude).Take(2).ToArray();
                var nodeCorners = corners.OrderBy(p => (p - CardRect.center).sqrMagnitude).Take(2).ToArray();
                float direct = (cardCorners[0] - nodeCorners[0]).sqrMagnitude + (cardCorners[1] - nodeCorners[1]).sqrMagnitude;
                float crossed = (cardCorners[0] - nodeCorners[1]).sqrMagnitude + (cardCorners[1] - nodeCorners[0]).sqrMagnitude;
                for (int i = 0; i < 2; i++)
                {
                    painter.BeginPath();
                    painter.MoveTo(cardCorners[i]);
                    painter.LineTo(nodeCorners[crossed < direct ? 1 - i : i]);
                    painter.Stroke();
                }
            }

            private static Vector2[] Corners(Rect rect) => new[]
            {
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax)
            };

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
            if(camera!=null)camera.targetTexture = null;
            if(texture!=null){texture.Release();Object.Destroy(texture);}
            if(camera!=null)Object.Destroy(camera.gameObject);
        }
    }
}
