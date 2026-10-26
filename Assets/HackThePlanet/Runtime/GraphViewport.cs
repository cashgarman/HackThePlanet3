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
        public string FocusedId, LastClickId;
        public float LastClickAt = -1;
        public void Reset() { Center = Vector3.zero; Distance = 30f; Yaw = 0; Pitch = 28f; Initialized = false;UserAdjusted=false;NodeCount=0;FocusedId=null;LastClickId=null;LastClickAt=-1; }
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
        private WaypointOverlay waypointOverlay;
        private readonly VisualElement labels;
        private readonly Dictionary<string, Button> nodeButtons = new Dictionary<string, Button>();
        private readonly IGraphEdgeFilter edgeFilter = new AllEdges();
        private readonly string selectedId;
        private readonly GraphCameraState savedCamera;
        private readonly bool research;
        private readonly VisualElement focusedCard;
        private readonly Label focusedTitle, focusedDetails;
        private readonly ScrollView focusedScroll;
        private readonly Dictionary<string, string> nodeIntel = new Dictionary<string, string>();
        public Image IntelStatic { get; private set; }
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
        private readonly string guideEntryId, guideObjectiveId;
        private static readonly Color GuideObjectiveOrange = new(1f, .55f, .15f, 1f);

        public int VisibleNodes => session.Renderer.VisibleCount;
        public int VisibleEdges => session.Renderer.VisibleEdges;
        public int NodeCount => scene.Nodes.Count;

        public GraphViewport(List<NetNode> nodes, List<NetLink> links, string selected, bool preview,
            Action<string> select, GraphPathResult route = null, GraphCameraState cameraState = null, ContractIntel intel = ContractIntel.Full, ContractIntel? focusIntel = null, GameCatalog catalog = null,
            string guideEntryId = null, string guideObjectiveId = null, IReadOnlyList<NetworkWaypoint> waypoints = null, int waypointTier = 0)
            : this(BuildNetwork(nodes, links, preview, intel), selected, select, false, route, cameraState)
        {
            this.guideEntryId = guideEntryId;
            this.guideObjectiveId = guideObjectiveId;
            if(waypoints!=null&&waypoints.Count>0)
            {
                waypointOverlay=new WaypointOverlay(camera,nodes,links,waypoints,waypointTier,guideEntryId);
                Add(waypointOverlay);
            }
            foreach (var node in nodes.Where(n => scene.Nodes.ContainsKey(n.id)))
                nodeIntel[node.id] = DescribeNode(node, focusIntel ?? intel, preview, catalog ?? GameCatalog.Default);
            UpdateFocusedCard();
        }

        public GraphViewport(Career career, string selected, Action<string> select, GraphCameraState cameraState = null, GameCatalog catalog = null)
            : this(BuildResearch(career,catalog??GameCatalog.Default), selected, select, true, null, cameraState)
        {
            var technologies = (catalog ?? GameCatalog.Default).Technologies.ToDictionary(t => t.id);
            foreach (var id in scene.Nodes.Keys)
                nodeIntel[id] = DescribeTechnology(technologies[id], career, technologies);
            foreach (var pair in nodeButtons)
                if (nodeIntel.TryGetValue(pair.Key, out var intel)) pair.Value.tooltip = intel;
            UpdateFocusedCard();
        }

        private GraphViewport(GraphScene graph, string selected, Action<string> select, bool research,
            GraphPathResult route, GraphCameraState cameraState)
        {
            scene = graph;
            selectedId = selected ?? "";
            savedCamera = cameraState ?? new GraphCameraState();
            fitPending=!savedCamera.Initialized||(!savedCamera.UserAdjusted&&savedCamera.NodeCount!=graph.Nodes.Count);
            savedCamera.NodeCount=graph.Nodes.Count;
            savedCamera.FocusedId = string.IsNullOrEmpty(selectedId) ? null : selectedId;
            this.research = research;
            AddToClassList("graph-viewport");
            style.flexGrow = 1;
            style.minHeight = 0;

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
            var resources = GameData.Presentation(false);
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
                var button = new Button(() => ClickNode(node.Id, select)) { name = (research ? "tech-" : "node-") + node.Id,
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
            focusedCard = new VisualElement { name = "focused-node-card" };
            focusedCard.style.position = Position.Absolute;
            focusedCard.style.display = DisplayStyle.None;
            focusedCard.style.backgroundColor = Palette.Hex("102025");
            focusedCard.style.borderTopWidth = focusedCard.style.borderBottomWidth = 1;
            focusedCard.style.borderLeftWidth = focusedCard.style.borderRightWidth = 1;
            focusedCard.style.borderTopColor = focusedCard.style.borderBottomColor = Palette.Lime;
            focusedCard.style.borderLeftColor = focusedCard.style.borderRightColor = Palette.Lime;
            IntelStatic = new Image { name = "node-intel-static", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            IntelStatic.style.position = Position.Absolute;
            IntelStatic.style.left = IntelStatic.style.right = IntelStatic.style.top = IntelStatic.style.bottom = 0;
            focusedCard.Add(IntelStatic);
            var scroll = focusedScroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = scroll.style.paddingRight = 10;
            scroll.style.paddingTop = scroll.style.paddingBottom = 8;
            focusedTitle = new Label { name = "focused-node-title" };
            focusedTitle.style.fontSize = 12;
            focusedTitle.style.color = Palette.Lime;
            focusedTitle.style.whiteSpace = WhiteSpace.Normal;
            focusedDetails = new Label { name = "focused-node-intel", enableRichText = true };
            focusedDetails.style.fontSize = 10;
            focusedDetails.style.color = Palette.Hex("DCEAE4");
            focusedDetails.style.whiteSpace = WhiteSpace.Normal;
            foreach (var label in new[] { focusedTitle, focusedDetails })
            {
                label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
                label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
                label.style.flexShrink = 0;
                label.RegisterCallback<GeometryChangedEvent>(_ => PositionLabels());
            }
            focusedTitle.style.marginBottom = 4;
            scroll.Add(focusedTitle);
            scroll.Add(focusedDetails);
            focusedCard.Add(scroll);
            focusedCard.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            focusedCard.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
            labels.Add(focusedCard);
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
                if (evt.button is not (0 or 1 or 2) || evt.target is Button) return;
                if (evt.button == 1) evt.PreventDefault();
                dragging = true;
                orbiting = evt.button == 0;
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
                var smallMove = Vector2.Distance(dragStart, evt.position) < 5;
                var hit = evt.button == 0 && smallMove ? PickNode(this.WorldToLocal(evt.position)) : null;
                dragging = false;
                this.ReleasePointer(evt.pointerId);
                if (hit != null) ClickNode(hit, select);
                else if (evt.button == 0 && smallMove && !string.IsNullOrEmpty(selectedId))
                    ClearSelection(select);
            });

        }

        private void ClickNode(string id, Action<string> select)
        {
            bool doubleClick = savedCamera.LastClickId == id && Time.unscaledTime - savedCamera.LastClickAt <= .4f;
            savedCamera.LastClickId = doubleClick ? null : id;
            savedCamera.LastClickAt = Time.unscaledTime;
            if (SelectWithoutCameraFocus())
            {
                if (doubleClick) FocusNode(id);
                else FocusIntel(id);
            }
            else FocusNode(id);
            select(id);
        }

        private static bool SelectWithoutCameraFocus()
        {
            if (WorkstationApp.Instance != null) return WorkstationApp.Instance.SelectWithoutCameraFocus;
            return PlayerPrefs.GetInt(WorkstationApp.SelectWithoutCameraFocusPreference, 1) != 0;
        }

        private void ClearSelection(Action<string> select)
        {
            savedCamera.FocusedId = null;
            savedCamera.LastClickId = null;
            select(null);
        }

        public void FocusNode(string id)
        {
            if (!scene.Nodes.TryGetValue(id, out var node)) return;
            FocusIntel(id);
            savedCamera.UserAdjusted = true;
            fitPending = false;
            distance = Mathf.Clamp(session.Renderer.Radius(node) * 10, 8, 18);
            // Leave space to the right for the expanded intel card.
            center = node.Position + camera.transform.right * (distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * camera.aspect * .45f);
            PlaceCamera();
            PositionLabels();
        }

        private void FocusIntel(string id)
        {
            if (!scene.Nodes.ContainsKey(id)) return;
            savedCamera.FocusedId = id;
            UpdateFocusedCard();
            PositionLabels();
        }

        private void UpdateFocusedCard()
        {
            bool focused = savedCamera.FocusedId == selectedId && scene.Nodes.ContainsKey(selectedId);
            focusedCard.style.display = focused ? DisplayStyle.Flex : DisplayStyle.None;
            if (!focused) return;
            focusedTitle.text = scene.Nodes[selectedId].Label;
            var intel = nodeIntel.TryGetValue(selectedId, out var detail) ? detail : "NO INTEL";
            focusedDetails.text = GameplayRichText.Format(intel);
        }

        internal static string DescribeNode(NetNode node, ContractIntel intel, bool preview, GameCatalog catalog)
        {
            if (preview && intel != ContractIntel.Full) return "NO INTEL\nUnknown system. Connect to discover this network.";
            string status = node.controlled ? "CONTROLLED" : "DISCOVERED / NOT CONTROLLED";
            if (intel == ContractIntel.None) return "NO INTEL\n" + status;
            var type = catalog.NodeTypes.FirstOrDefault(t => t.id == node.typeId)
                ?? catalog.NodeTypes.FirstOrDefault(t => t.glyph == node.kind);
            string role = type?.title ?? node.kind.ToString();
            if (intel == ContractIntel.Topology) return "LIMITED INTEL / " + role + "\n" + status + "\nSecurity and internal details unavailable.";
            string detail = "FULL INTEL / " + role + "\n" + status + " / SECURITY " + node.security
                + "\n" + (string.IsNullOrEmpty(node.detail) ? type?.description : node.detail);
            switch (node.kind)
            {
                case NodeKind.Gateway: detail += "\nEntry point / route traffic through controlled systems."; break;
                case NodeKind.Workstation: detail += "\nEndpoint / control it to extend your route."; break;
                case NodeKind.Relay: detail += "\nTransit / connects network branches."; break;
                case NodeKind.Firewall: detail += "\nPerimeter defense / guards access to deeper systems."; break;
                case NodeKind.Scanner: detail += "\nDetection / " + (node.suppressed > 0 ? "SUPPRESSED" : "ACTIVE SENSOR"); break;
                case NodeKind.SecurityHub: detail += "\nSecurity coordination / " + (node.suppressed > 0 ? "SUPPRESSED" : "ACTIVE DEFENSE"); break;
                case NodeKind.Database: detail += "\nData store / " + (node.collected ? "DATA COLLECTED" : node.objective ? "CONTRACT OBJECTIVE" : node.bonus ? "BONUS DATA" : "ARCHIVE"); break;
            }
            return detail;
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

        internal static string DescribeTechnology(Technology tech, Career career, IReadOnlyDictionary<string, Technology> all)
        {
            string Title(string id) => all.TryGetValue(id, out var parent) ? parent.title : id;
            bool owned = career.unlocked.Contains(tech.id);
            bool ready = tech.parents.All(career.unlocked.Contains);
            string status = owned ? "UNLOCKED"
                : !ready ? "LOCKED / REQUIRES " + string.Join(" + ", tech.parents.Select(Title))
                : career.xp >= tech.cost ? "AVAILABLE / " + tech.cost + " XP"
                : tech.cost + " XP / NEED " + (tech.cost - career.xp) + " MORE";
            var lines = new List<string> { tech.branch + (tech.special ? " / ABILITY" : " / PASSIVE"), status, tech.description };
            if (!owned && tech.parents.Length > 1) lines.Add("BOTH parent technologies required.");
            return string.Join("\n", lines);
        }

        private static GraphScene BuildResearch(Career career,GameCatalog catalog)
        {
            var document = new GraphDocument("workstation-research", "Research network");
            var schema = new GraphSchema();
            // Each branch has a bright type for owned nodes and a dimmed twin for revealed but locked nodes.
            foreach (var branch in catalog.Technologies.Select(t => t.branch).Distinct())
            {
                schema.Register(new GraphNodeTypeDefinition(branch, branch, "default",
                    Palette.Branch(branch), .95f));
                schema.Register(new GraphNodeTypeDefinition(LockedType(branch), branch, "default",
                    LockedColor(Palette.Branch(branch)), .95f));
            }
            schema.Register(new GraphRelationDefinition("requires", "Requires", Palette.Cyan));
            // Progressive reveal: only discovered technologies (root, owned nodes and their direct
            // children) enter the document. An edge is drawn only when both of its ends are visible.
            var visible = catalog.Technologies.Where(t => career.researchRevealed.Contains(t.id)).ToArray();
            var visibleIds = new HashSet<string>(visible.Select(t => t.id));
            foreach (var tech in visible)
                document.UpsertNode(new GraphNodeRecord(tech.id, career.unlocked.Contains(tech.id) ? tech.branch : LockedType(tech.branch), tech.title,new Dictionary<string,GraphValue>{["affordable"]=GraphValue.From(!career.unlocked.Contains(tech.id)&&tech.cost<=career.xp&&tech.parents.All(career.unlocked.Contains))}));
            foreach (var tech in visible)
                foreach (var parent in tech.parents.Where(visibleIds.Contains))
                    document.UpsertEdge(new GraphEdgeRecord(parent + ">" + tech.id, parent, tech.id, "requires"));
            var scene = new GraphScene(document, schema);
            foreach (var tech in visible)
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
            }
            return scene;
        }

        // Locked research keeps its branch hue, muted toward the background so ownership still reads at a glance.
        private static string LockedType(string branch) => branch + "-locked";
        private static Color LockedColor(Color branch) => Color.Lerp(branch, new Color(.07f, .1f, .12f, 1f), .62f);

        public void Tick(GameSimulation state, SignalEffects effects)
        {
            if (disposed) return;
            waypointOverlay?.Reposition(textScale);
            session.Renderer.ReducedMotion = effects != SignalEffects.Full;
            if (state.Active && !research && state.Op.task != null && !string.IsNullOrEmpty(state.Op.task.target))
                session.Renderer.NodeProgress = new Dictionary<string, GraphNodeProgressVisual>
                { [state.Op.task.target] = new GraphNodeProgressVisual { Fraction = state.Progress } };
            else session.Renderer.NodeProgress = null;
            session.Renderer.GuideArrows = BuildGuideArrows();
            scene.Nodes.TryGetValue(selectedId, out var selected);
            session.Renderer.Draw(scene, edgeFilter, camera, selected, null, null);
        }

        private IReadOnlyList<GraphGuideArrow> BuildGuideArrows()
        {
            if (research) return null;
            var arrows = new List<GraphGuideArrow>(2);
            if (!string.IsNullOrEmpty(guideEntryId) && scene.Nodes.ContainsKey(guideEntryId))
                arrows.Add(new GraphGuideArrow { NodeId = guideEntryId, Color = Palette.Lime });
            if (!string.IsNullOrEmpty(guideObjectiveId) && scene.Nodes.ContainsKey(guideObjectiveId))
                arrows.Add(new GraphGuideArrow { NodeId = guideObjectiveId, Color = GuideObjectiveOrange });
            return arrows.Count == 0 ? null : arrows;
        }

        private void PositionLabels()
        {
            if (disposed || contentRect.width <= 0 || contentRect.height <= 0) return;
            waypointOverlay?.Reposition(textScale);
            links.SelectionVisible = false;
            bool showFocusedCard = false;
            foreach (var pair in nodeButtons)
            {
                pair.Value.style.display = DisplayStyle.None;
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
                float rightSpace = contentRect.width - reticle.xMax - 20;
                float leftSpace = reticle.xMin - 20;
                var focusedSize = MeasureFocusedCard(Mathf.Max(rightSpace, leftSpace));
                float width = focusedSize.x;
                float height = focusedSize.y;
                float beside = rightSpace >= leftSpace || width <= rightSpace + 1 ? reticle.xMax + 12 : reticle.xMin - width - 12;
                var card = new Rect(Mathf.Clamp(beside, 8, Mathf.Max(8, contentRect.width - width - 8)),
                    Mathf.Clamp(nodeCenter.y - height / 2, 8, Mathf.Max(8, contentRect.height - height - 8)), width, height);
                showFocusedCard = true;
                focusedCard.style.width = width;
                focusedCard.style.height = height;
                focusedCard.style.left = card.x;
                focusedCard.style.top = card.y;
                links.SelectionVisible = true;
                links.SelectionRect = reticle;
                links.CardRect = card;
            }
            focusedCard.style.display = showFocusedCard ? DisplayStyle.Flex : DisplayStyle.None;
            links.MarkDirtyRepaint();
        }

        private Vector2 MeasureFocusedCard(float availableWidth)
        {
            // Measure explicit lines at the current font size before imposing viewport limits.
            // Grow sideways first; wrap and scroll only when the graph cannot contain the text.
            float NaturalWidth(Label label) => GameplayRichText.Plain(label.text).Split('\n').Max(line =>
                label.MeasureTextSize(line, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x);
            float width = Mathf.Ceil(Mathf.Min(Mathf.Max(NaturalWidth(focusedTitle), NaturalWidth(focusedDetails)) + 24,
                Mathf.Max(1, Mathf.Min(contentRect.width - 16, availableWidth))));
            float textWidth = Mathf.Max(1, width - 22);
            float TextHeight(Label label) => label.MeasureTextSize(GameplayRichText.Plain(label.text), textWidth, MeasureMode.Exactly,
                0, MeasureMode.Undefined).y;
            float height = Mathf.Ceil(TextHeight(focusedTitle) + TextHeight(focusedDetails) + 22);
            float maxHeight = Mathf.Max(1, contentRect.height - 16);
            bool scrolls = height > maxHeight;
            focusedScroll.verticalScrollerVisibility = scrolls ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Hidden;
            if (scrolls) textWidth = Mathf.Max(1, textWidth - 18);
            focusedScroll.contentContainer.style.width = textWidth;
            return new Vector2(width, Mathf.Min(height, maxHeight));
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
            savedCamera.FocusedId=null;
            if (scene.Nodes.Count == 0) return;
            var positions=scene.Nodes.Values.Select(n=>n.Position).Concat(waypointOverlay?.Anchors??Enumerable.Empty<Vector3>()).ToArray();
            var bounds = new Bounds(positions[0], Vector3.one * 3);
            foreach (var position in positions) bounds.Encapsulate(new Bounds(position, Vector3.one * 3));
            center = bounds.center;
            var rotation = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0));
            float tangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            distance = 4;
            foreach (var position in positions)
            {
                var point = rotation * (position - center);
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
            if (networkSettings != null) DestroyOwned(networkSettings);
            if(camera!=null)camera.targetTexture = null;
            if(texture!=null){texture.Release();DestroyOwned(texture);}
            if(camera!=null)DestroyOwned(camera.gameObject);
        }
        private static void DestroyOwned(Object obj) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
