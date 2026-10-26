using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace HackThePlanet.Editor
{
    [InitializeOnLoad]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameBuildTools
    {
        public const string ScenePath="Assets/HackThePlanet/Scenes/Workstation.unity";
        private static double nextPoll;
        private static int smokePhase, smokeFrames;
        private static SignalEffects previousEffects;
        private static Color32[] frozenField, frozenGlass;
        private static RenderTexture sharedField, sharedGlass;
        private static float settingsElapsed, settingsTaskElapsed, settingsTrace;
        private static Career settingsCareer;
        private static int previousTextSize;
        private static readonly Dictionary<TextElement,float> textSizeBaseline=new Dictionary<TextElement,float>();
        private static DataDrivenValidation.Fixture dataFixture;
        private static RenderTexture smokeUiTexture;
        private static string Project=>Directory.GetParent(Application.dataPath).FullName;
        private static string CommandPath=>Path.Combine(Project,"Temp/HackThePlanet.command");
        private static string StatusPath=>Path.Combine(Project,"Temp/HackThePlanet.status");
        static GameBuildTools()
        {
            EditorApplication.update+=Poll;
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("HTP.Smoke",false))
                {smokePhase=1;smokeFrames=0;}
                if(state==PlayModeStateChange.EnteredEditMode&&smokePhase==43)
                {
                    File.WriteAllText(Path.Combine(Project,"Artifacts/workstation-smoke.txt"),"PASS / Real player UI callbacks: connect, scan, hack, recover, deliver, research purchase, hardware upgrade. Both shaders compile and render nonblank GPU textures; display modes and GPU texture reuse passed. Settings button and Escape, exact menu choices, menu bounds, operation freeze, previous pause state, blocked background controls and shortcuts, reset confirmation, Cancel and Escape cancellation, confirmed fresh career, usable first contract, and Quit passed. Rendered screenshots captured. Asset-only contract, node variant, action button, shortcut, objective, technology and hardware extension rendered and exercised. Tactical queue, clear and stop controls exercised. Easy full intel, Medium anonymous topology and labels, Hard renderer-free centered message, animated GPU static, reduced/off effects, and connection back to discovered systems passed. Test career and effect preference isolated from user saves.");
                    File.AppendAllText(Path.Combine(Project,"Artifacts/workstation-smoke.txt"),"\nPASS / Text size: 200% default; real slider callbacks at 100% and 300%; every TextElement scales without compounding, including inline graph labels and numeric input; scrollable Settings stays in bounds at 300%, including a 1280 x 720 render; preference persistence and career-reset independence; original user preference restored.\n");
                    if(smokeUiTexture!=null){smokeUiTexture.Release();UnityEngine.Object.DestroyImmediate(smokeUiTexture);smokeUiTexture=null;}
                    dataFixture?.Dispose();dataFixture=null;settingsCareer=null;SessionState.SetBool("HTP.Smoke",false);smokePhase=0;Status("PASS / smoke");if(Application.isBatchMode)EditorApplication.Exit(0);
                }
            };
        }
        private static void Poll()
        {
            if(smokePhase>0){SmokeStep();return;}
            if(EditorApplication.timeSinceStartup<nextPoll||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+.5;
            if(!File.Exists(CommandPath))return;
            var command=File.ReadAllText(CommandPath).Trim();File.Delete(CommandPath);
            try
            {
                Status("RUNNING / "+command);
                switch(command)
                {
                    case "import-gameplay": DefaultGameplayAssets.Import();break;
                    case "refresh": AssetDatabase.Refresh();break;
                    case "balance-before": BalanceLab.CaptureBefore();break;
                    case "balance-after": BalanceLab.CaptureAfter();break;
                    case "data-validate": DataDrivenValidation.Run();break;
                    case "setup": Setup();break;
                    case "validate": GameValidation.Run();break;
                    case "models": NetworkVisualValidation.Run();break;
                    case "build": BuildWindows();break;
                    case "build-volumetric": BuildWindowsAt("Builds/Windows-Volumetric");break;
                    case "play": EditorApplication.isPlaying=true;break;
                    case "stop": EditorApplication.isPlaying=false;break;
                    case "smoke": StartSmoke();return;
                    case "capture": CaptureScreenshot(Path.Combine(Project,"Artifacts/live-capture.png"));break;
                    default: throw new InvalidOperationException("Unknown local editor command: "+command);
                }
                Status("PASS / "+command);
            }
            catch(Exception ex){Debug.LogException(ex);Status("FAIL / "+command+"\n"+ex);}
        }
        private static void Status(string text){Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));File.WriteAllText(StatusPath,text);}

        [MenuItem("Hack The Planet/Set Up Game Scene")]
        public static void Setup()
        {
            Directory.CreateDirectory(Path.Combine(Project,"Assets/HackThePlanet/Scenes"));
            if(!File.Exists(Path.Combine(Project,ScenePath)))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene,ScenePath);EditorSceneManager.CloseScene(scene,true);
            }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.productName="Hack The Planet";PlayerSettings.companyName="Independent Operator";
            PlayerSettings.defaultScreenWidth=1680;PlayerSettings.defaultScreenHeight=960;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Hack The Planet/Build Windows Player")]
        public static void BuildWindows()
        {
            BuildWindowsAt("Builds/Windows");
        }

        private static void BuildWindowsAt(string output)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode before building.");
            GameDefinitionValidator.ThrowIfInvalid(Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game"));
            Setup();Directory.CreateDirectory(Path.Combine(Project,output));Directory.CreateDirectory(Path.Combine(Project,"Artifacts"));
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=Path.Combine(Project,output,"Hack The Planet.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText(Path.Combine(Project,"Artifacts/build-report.txt"),result.summary.result+"\nErrors: "+result.summary.totalErrors+"\nWarnings: "+result.summary.totalWarnings+"\nBytes: "+result.summary.totalSize+"\nTime: "+result.summary.totalTime);
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Windows build failed: "+result.summary.result);
        }

        [MenuItem("Hack The Planet/Run Simulation Validation")]
        public static void Validate()=>GameValidation.Run();

        [MenuItem("Hack The Planet/Run Workstation Smoke Test")]
        public static void StartSmoke()
        {
            Directory.CreateDirectory(Path.Combine(Project,"Artifacts"));SessionState.SetBool("HTP.Smoke",true);
            if(EditorApplication.isPlaying){smokePhase=1;smokeFrames=0;}else EditorApplication.isPlaying=true;
        }

        private static void SmokeStep()
        {
            try
            {
                var app=WorkstationApp.Instance;if(app==null)return;
                if(++smokeFrames<18)return;smokeFrames=0;
                switch(smokePhase)
                {
                    case 1:
                        smokeUiTexture=new RenderTexture(1680,960,24,RenderTextureFormat.ARGB32);smokeUiTexture.Create();
                        app.GetComponent<UIDocument>().panelSettings.targetTexture=smokeUiTexture;
                        previousEffects=app.Visuals.Mode;previousTextSize=app.TextSizePercent;app.UseTestCareer(new Career());app.SetTextSize(200);app.Visuals.SetMode(SignalEffects.Full);smokePhase++;return;
                    case 2:
                        Check(app.Root.Q<UnityEngine.UIElements.Button>("connect")!=null,"Connect control exists.");
                        var preview=app.Root.Q<GraphViewport>();
                        Check(preview!=null&&preview.NodeCount==32&&preview.VisibleNodes==32&&preview.VisibleEdges>0,
                            "GraphExplorer renders all preview systems and their links in 3D: "+preview?.NodeCount+" nodes / "+preview?.VisibleNodes+" drawn / "+preview?.VisibleEdges+" links.");
                        Check(app.Root.Q<Button>("node-home")!=null,"Projected system labels remain selectable.");
                        var mapBounds=preview.GetFirstAncestorOfType<ScrollView>().contentViewport.worldBound;
                        Check(mapBounds.Contains(preview.worldBound.min+Vector2.one)&&mapBounds.Contains(preview.worldBound.max-Vector2.one)&&mapBounds.Contains(app.Root.Q<Button>("fit").worldBound.center),"Default 200% layout keeps the full graph and its controls visible: viewport "+mapBounds+", graph "+preview.worldBound+", fit "+app.Root.Q<Button>("fit").worldBound);
                        ValidateSignalRendering(app);
                        NetworkVisualValidation.CheckViewport(preview);
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/01-contract-exchange.png"));smokePhase=100;return;
                    case 100:
                        app.Root.Q<Foldout>("network-key").value=true;
                        NetworkVisualValidation.Orbit(app.Root.Q<GraphViewport>(),55,15);smokePhase=101;return;
                    case 101:
                        Check(Mathf.Approximately(app.Root.Q<Foldout>("network-key").Q<Label>(className:"micro").resolvedStyle.fontSize,14),"Expanded network key doubles its authored 7px telemetry text.");
                        app.Root.Q<Foldout>("network-key").value=false;
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/network-orbit.png"));
                        smokePhase=102;return;
                    case 102:
                        NetworkVisualValidation.Orbit(app.Root.Q<GraphViewport>(),0,28);
                        Check(app.ActivateVisibleControl("contract-"+GameContent.Story.First(c=>c.Intel==ContractIntel.Topology).id),"Medium contract can be previewed.");smokePhase=120;return;
                    case 120:
                        var mediumGraph=app.Root.Q<GraphViewport>();
                        Check(mediumGraph!=null&&mediumGraph.NodeCount==44&&mediumGraph.VisibleNodes==44&&mediumGraph.VisibleEdges>0,"Medium draws the complete anonymous topology.");
                        Check(mediumGraph.Query<Button>().ToList().All(b=>b.text=="UNKNOWN SYSTEM"&&string.IsNullOrEmpty(b.tooltip)),"Medium node cards expose no system names or hover tooltips.");
                        Check(app.Root.Query<VisualElement>(className:"legend-key").ToList().Count==0&&app.Root.Q(className:"sectors")==null,"Medium contains no type legend or sector intel.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/16-medium-contract-intel.png"));
                        smokePhase=128;return;
                    case 128:
                        var hard=GameContent.Story.First(c=>c.Intel==ContractIntel.None);
                        var developed=new Career();developed.completed=GameContent.Story.TakeWhile(c=>c.id!=hard.id).Select(c=>c.id).ToList();
                        app.UseTestCareer(developed);Check(app.ActivateVisibleControl("contract-"+hard.id),"Hard contract can be previewed.");smokePhase=121;return;
                    case 121:
                        Check(app.Root.Q<GraphViewport>()==null&&app.Root.Q("no-intel-view")!=null,"Hard creates no graph renderer or selectable systems.");
                        var blocked=app.Root.Q("no-intel-view");var message=app.Root.Q<Label>(className:"no-intel-title");
                        Check(message?.text=="NO INTEL AVAILABLE"&&Vector2.Distance(message.worldBound.center,blocked.worldBound.center)<3,"Hard intel message is centered in the graph view.");
                        Check(app.Root.Q<Button>("fit")==null&&app.Root.Q<Button>("zoom-in")==null&&app.Root.Query<VisualElement>(className:"legend-key").ToList().Count==0,"Hard hides map controls and system legend.");
                        ValidateSignalRendering(app);frozenField=ReadPixels(app.Visuals.FieldTexture);
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/17-hard-contract-no-intel.png"));smokePhase++;return;
                    case 122:
                        Check(!frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),"Hard static animates in Full effects mode.");
                        app.Visuals.SetMode(SignalEffects.Reduced);smokePhase++;return;
                    case 123:
                        frozenField=ReadPixels(app.Visuals.FieldTexture);smokePhase++;return;
                    case 124:
                        Check(frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),"Hard static freezes in Reduced effects mode.");
                        app.Visuals.SetMode(SignalEffects.Off);smokePhase++;return;
                    case 125:
                        Check(app.Root.Q<Image>("signal-field").resolvedStyle.display==DisplayStyle.None&&app.Root.Q<Label>(className:"no-intel-title").resolvedStyle.display!=DisplayStyle.None,"Effects Off preserves the Hard intel message without animation.");
                        app.Visuals.SetMode(SignalEffects.Full);Check(app.ActivateVisibleControl("connect")&&app.State.Active,"Hard connects through its intel screen.");smokePhase++;return;
                    case 126:
                        Check(app.Root.Q("no-intel-view")==null&&app.Root.Q<GraphViewport>()?.NodeCount==app.State.KnownGraph.Nodes.Count&&app.State.KnownGraph.Nodes.Count<app.State.Op.nodes.Count,"Hard connection replaces the static screen with discovered systems only.");
                        app.UseTestCareer(new Career());frozenField=null;smokePhase++;return;
                    case 127:
                        Check(app.Root.Q<GraphViewport>()?.NodeCount==32&&app.Root.Q("no-intel-view")==null,"Returning to Easy restores the full preview.");smokePhase=3;return;
                    case 3:
                        Check(app.ActivateVisibleControl("connect"),"Connect button works.");Check(app.State.Active,"Mission connected through UI.");smokePhase++;return;
                    case 4:
                        Check(app.ActivateVisibleControl("action-scan"),"Scan button is wired.");
                        GameValidation.FinishTask(app.State);app.SelectNode("n2");smokePhase++;return;
                    case 5:
                        Check(app.ActivateVisibleControl("action-hack"),"Hack button is wired.");app.State.Tick(.25f);
                        Check(app.State.Execute("scan","n6"),"A future reachable scan can be queued.");app.State.SetPaused(true);smokePhase=110;return;
                    case 110:
                        Check(app.Root.Q<Button>("stop-tool")!=null&&app.Root.Query<Button>("clear-queue").ToList().Count==1&&app.Root.Q<Label>(className:"queue-summary").text.Contains("NEXT"),"Running tools expose Stop and a readable next-action summary.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/15-tactical-queue.png"));
                        Check(app.ActivateVisibleControl("clear-queue")&&app.State.Op.task!=null&&app.State.Op.queue.Count==0,"Clear removes queued work without cancelling the current tool.");
                        Check(app.State.Execute("scan","n6"),"Queue can be rebuilt after clearing.");float beforeStop=app.State.Op.trace;
                        Check(app.ActivateVisibleControl("stop-tool")&&app.State.Op.task==null&&app.State.Op.queue.Count==0&&app.State.Op.trace==beforeStop,"Stop cancels work without refunding generated trace.");smokePhase=111;return;
                    case 111:
                        Check(app.ActivateVisibleControl("pause")&&!app.State.Op.paused,"Paused cancellation resumes through the pause control.");
                        Check(app.Root.Q<Button>("action-hack").enabledInHierarchy&&app.ActivateVisibleControl("action-hack"),"A stopped tool can be restarted through the clean action list.");GameValidation.FinishTask(app.State);
                        GameValidation.Hack(app.State,"n6");GameValidation.Hack(app.State,"n10");app.SelectNode("n14");smokePhase=6;return;
                    case 6:
                        var operationGraph=app.Root.Q<GraphViewport>();
                        Check(operationGraph!=null&&operationGraph.NodeCount>2&&operationGraph.NodeCount<app.State.Op.nodes.Count&&operationGraph.VisibleEdges>0,
                            "Live GraphExplorer view shows discovered systems and connections only.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/02-live-operation.png"));
                        GameValidation.Hack(app.State,"n14");GameValidation.Hack(app.State,"n18");GameValidation.Hack(app.State,"n22");app.SelectNode("n22");smokePhase++;return;
                    case 7:
                        Check(app.ActivateVisibleControl("action-download"),"Recovery button is wired.");GameValidation.FinishTask(app.State);smokePhase++;return;
                    case 8:
                        Check(app.ActivateVisibleControl("disconnect"),"Delivery button works.");Check(!app.State.Active&&app.State.Career.jobs==1,"Delivery banked rewards.");smokePhase++;return;
                    case 9:
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/03-operation-result.png"));smokePhase++;return;
                    case 10:
                        Check(app.ActivateVisibleControl("report-research"),"Research result link works.");smokePhase++;return;
                    case 11:
                        Check(app.Root.Q<GraphViewport>()?.NodeCount==GameContent.Tech.Length&&app.Root.Q<Button>("tech-wide")!=null,
                            "GraphExplorer renders selectable research technologies.");
                        Check(app.ActivateVisibleControl("unlock"),"Research purchase works.");Check(app.State.Has("wide"),"Tech purchased using earned XP.");
                        // Preview a developed build for the research screenshot without touching the real career.
                        app.State.Career.xp=360;app.State.BuyTech("quiet");app.State.BuyTech("efficient");app.SelectTech("passive");smokePhase++;return;
                    case 12:
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/04-research-network.png"));smokePhase++;return;
                    case 13:
                        app.Navigate("rig");smokePhase++;return;
                    case 14:
                        Check(app.ActivateVisibleControl("upgrade-0"),"Hardware purchase button works.");Check(app.State.Career.cpu==1,"CPU upgraded.");smokePhase++;return;
                    case 15:
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/05-your-rig.png"));smokePhase++;return;
                    case 16:
                        // Isolated display fixture; banking and failure semantics are exercised by NetworkValidation.
                        app.State.Career.lore.Add(LoreContent.For(GameContent.Story[0],0));
                        app.Navigate("intel");smokePhase++;return;
                    case 17:
                        Check(app.Root.Query<Label>().ToList().Any(l=>l.text=="The missing appendix"),"Permanent lore collection displays fragment titles.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/06-intelligence.png"));smokePhase++;return;
                    case 18:
                        app.StartSelected();smokePhase++;return;
                    case 19:
                        GameValidation.Hack(app.State,"n2");GameValidation.Hack(app.State,"n6");GameValidation.Hack(app.State,"n10");
                        app.SelectNode("n14");app.State.Op.trace=87;
                        Check(app.State.Execute(ToolKind.Scan,"n14"),"Trace-warning fixture can run a real tool.");
                        app.State.Tick(app.State.Op.task.duration*.42f);app.State.SetPaused(true);smokePhase++;return;
                    case 20:
                        Check(app.Root.ClassListContains("trace-alert"),"High trace changes the workstation warning treatment.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/07-trace-warning.png"));
                        Check(app.ActivateVisibleControl("effects"),"Effects control is wired.");
                        Check(app.Visuals.Mode==SignalEffects.Reduced,"Full effects cycle to static reduced effects.");smokePhase++;return;
                    case 21:
                        frozenField=ReadPixels(app.Visuals.FieldTexture);frozenGlass=ReadPixels(app.Visuals.GlassTexture);
                        sharedField=app.Visuals.FieldTexture;sharedGlass=app.Visuals.GlassTexture;
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/08-reduced-effects.png"));smokePhase++;return;
                    case 22:
                        Check(frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture))&&frozenGlass.SequenceEqual(ReadPixels(app.Visuals.GlassTexture)),"Reduced shader textures stay exactly static across frames.");
                        Check(app.ActivateVisibleControl("effects")&&app.Visuals.Mode==SignalEffects.Off,"Reduced effects cycle to off.");smokePhase++;return;
                    case 23:
                        Check(app.Root.Q<Image>("signal-field").resolvedStyle.display==DisplayStyle.None&&app.Root.Q<Image>("phosphor-glass").resolvedStyle.display==DisplayStyle.None,"Off mode hides both shader layers.");
                        Check(app.Root.Q<Button>("action-scan")!=null,"Action controls survive effects changes.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/09-effects-off.png"));
                        Check(app.ActivateVisibleControl("effects")&&app.Visuals.Mode==SignalEffects.Full,"Off effects cycle back to full.");smokePhase++;return;
                    case 24:
                        Check(ReferenceEquals(sharedField,app.Visuals.FieldTexture)&&ReferenceEquals(sharedGlass,app.Visuals.GlassTexture),"UI rebuilds reuse both GPU textures.");
                        Check(!frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),"Full effects animate the signal field.");
                        app.Visuals.SetMode(previousEffects);frozenField=null;frozenGlass=null;
                        settingsCareer=app.State.Career;settingsElapsed=app.State.Op.elapsed;settingsTaskElapsed=app.State.Op.task.elapsed;settingsTrace=app.State.Op.trace;
                        Check(app.ActivateVisibleControl("settings"),"Settings rail button opens the menu.");smokePhase++;return;
                    case 25:
                        var menu=app.Root.Q<VisualElement>(className:"settings-menu");
                        Check(menu!=null&&menu.Q<Button>("resume")?.text=="Resume"&&menu.Q<Button>("reset-save")?.text=="Reset Game Save"&&menu.Q<Button>("quit")?.text=="Quit","Settings retains Resume, Reset Game Save, and Quit.");
                        Check(app.Root.Q<SliderInt>("text-size")?.value==200,"Settings defaults to double text size.");
                        Check(menu.worldBound.width>300&&menu.worldBound.height>200&&app.Root.worldBound.Contains(menu.worldBound.min)&&app.Root.worldBound.Contains(menu.worldBound.max),"Settings menu fits within the rendered viewport.");
                        Check(app.State.Op.paused&&app.State.Op.elapsed==settingsElapsed&&app.State.Op.task.elapsed==settingsTaskElapsed&&app.State.Op.trace==settingsTrace,"Settings freezes operation time, tool progress, and trace across frames.");
                        Check(!app.ActivateVisibleControl("action-scan")&&!app.ActivateVisibleControl("tab-research"),"Settings blocks underlying gameplay and navigation controls.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/10-settings-menu.png"));
                        smokePhase=300;return;
                    case 300:
                        app.Root.Q<SliderInt>("text-size").value=100;smokePhase=301;return;
                    case 301:
                        textSizeBaseline.Clear();
                        foreach(var text in app.Root.Query<TextElement>().ToList())
                            textSizeBaseline[text]=text.resolvedStyle.fontSize;
                        app.Root.Q<SliderInt>("text-size").value=300;smokePhase++;return;
                    case 302:
                        Check(app.TextSizePercent==300,"Text size changes through the actual slider callback.");
                        foreach(var text in app.Root.Query<TextElement>().ToList())
                            if(textSizeBaseline.TryGetValue(text,out var baseline))
                                Check(Mathf.Abs(text.resolvedStyle.fontSize-baseline*3)<.1f,"Every text element scales, including graph labels and settings inputs: "+text.name+" / "+text.text+" / "+text.resolvedStyle.fontSize+" expected "+baseline*3);
                        var largeMenu=app.Root.Q(className:"settings-menu");
                        Check(app.Root.worldBound.Contains(largeMenu.worldBound.min)&&app.Root.worldBound.Contains(largeMenu.worldBound.max),"Settings stays within the viewport at 300%.");
                        var menuScroll=largeMenu as ScrollView;menuScroll.ScrollTo(app.Root.Q<Button>("quit"));
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/18-settings-300-percent.png"));
                        smokePhase++;return;
                    case 303:
                        smokeUiTexture.Release();smokeUiTexture.width=1280;smokeUiTexture.height=720;smokeUiTexture.Create();smokePhase++;return;
                    case 304:
                        var smallMenu=app.Root.Q(className:"settings-menu");
                        Check(app.Root.worldBound.Contains(smallMenu.worldBound.min)&&app.Root.worldBound.Contains(smallMenu.worldBound.max),"300% settings fits after resizing to 1280 x 720.");
                        (smallMenu as ScrollView).ScrollTo(app.Root.Q<Button>("resume"));
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/19-settings-1280x720.png"));
                        smokePhase++;return;
                    case 305:
                        bool hadSize=PlayerPrefs.HasKey(WorkstationApp.TextSizePreference);int savedSize=PlayerPrefs.GetInt(WorkstationApp.TextSizePreference);
                        try
                        {
                            app.Testing=false;app.SetTextSize(175);
                            Check(PlayerPrefs.GetInt(WorkstationApp.TextSizePreference)==175,"Text size is saved independently of the career.");
                        }
                        finally
                        {
                            app.Testing=true;
                            if(hadSize)PlayerPrefs.SetInt(WorkstationApp.TextSizePreference,savedSize);else PlayerPrefs.DeleteKey(WorkstationApp.TextSizePreference);
                            PlayerPrefs.Save();app.SetTextSize(200);
                        }
                        smokeUiTexture.Release();smokeUiTexture.width=1680;smokeUiTexture.height=960;smokeUiTexture.Create();smokePhase++;return;
                    case 306:
                        Check(Mathf.Approximately(app.Root.Q<Label>(className:"micro").resolvedStyle.fontSize,16),"Returning to 200% does not compound font scaling.");
                        Check(app.ActivateVisibleControl("resume")&&app.State.Op.paused,"Resume preserves an already paused operation.");
                        app.State.SetPaused(false);SendKey(app,KeyCode.Escape);
                        Check(app.State.Op.paused,"Escape opens Settings and pauses a running operation.");smokePhase=26;return;
                    case 26:
                        SendKey(app,KeyCode.Space);SendKey(app,KeyCode.Alpha2);
                        Check(app.State.Op.paused,"Gameplay shortcuts cannot run behind Settings.");
                        SendKey(app,KeyCode.Escape);Check(!app.State.Op.paused,"Escape resumes a previously running operation.");
                        app.State.SetPaused(true);SendKey(app,KeyCode.Escape);smokePhase++;return;
                    case 27:
                        Check(app.ActivateVisibleControl("reset-save"),"Reset opens confirmation before changing progress.");
                        Check(ReferenceEquals(app.State.Career,settingsCareer)&&app.State.Active,"Opening reset confirmation preserves the career and operation.");smokePhase++;return;
                    case 28:
                        Check(app.Root.Q<Button>("confirm-reset")!=null&&app.Root.Q<Button>("cancel-reset")!=null,"Reset confirmation exposes both explicit choices.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/11-reset-save-confirmation.png"));
                        smokePhase++;return;
                    case 29:
                        SendKey(app,KeyCode.Escape);smokePhase++;return;
                    case 30:
                        Check(app.Root.Q<Button>("resume")!=null&&ReferenceEquals(app.State.Career,settingsCareer)&&app.State.Op.paused,"Escape cancels reset and returns to Settings without losing progress.");
                        Check(app.ActivateVisibleControl("reset-save"),"Reset confirmation can be reopened.");smokePhase++;return;
                    case 31:
                        Check(app.ActivateVisibleControl("cancel-reset"),"Cancel returns from reset confirmation.");smokePhase++;return;
                    case 32:
                        Check(ReferenceEquals(app.State.Career,settingsCareer)&&app.ActivateVisibleControl("reset-save"),"Cancel preserves the existing career.");smokePhase++;return;
                    case 33:
                        Check(app.ActivateVisibleControl("confirm-reset"),"Confirmed reset callback runs.");
                        var fresh=app.State.Career;
                        Check(!ReferenceEquals(fresh,settingsCareer)&&!app.State.Active&&fresh.credits==600&&fresh.xp==0&&fresh.day==1&&fresh.jobs==0&&fresh.completed.Count==0&&fresh.reports.Count==0&&fresh.cpu==0&&fresh.unlocked.SequenceEqual(new[]{"base"}),"Confirmed reset starts a fresh career and clears the active operation.");smokePhase++;return;
                    case 34:
                        Check(app.CurrentView=="network"&&app.Root.Q<Button>("connect").enabledInHierarchy,"Reset returns to the first contract with a usable connection control.");
                        Check(app.TextSizePercent==200,"Career resets preserve text size.");
                        SendKey(app,KeyCode.Escape);smokePhase++;return;
                    case 35:
                        Check(app.ActivateVisibleControl("resume"),"Settings resumes before asset-only fixture.");
                        dataFixture=DataDrivenValidation.CreateFixture();var fixtureCareer=dataFixture.Catalog.CreateCareer();fixtureCareer.credits=10000;
                        app.UseTestCatalog(dataFixture.Catalog,fixtureCareer,"field-contract");smokePhase++;return;
                    case 36:
                        Check(app.Root.Q<Button>("contract-field-contract")!=null,"Asset-only contract appears in exchange.");
                        Check(app.State.BuyTech("field-analysis")&&app.ActivateVisibleControl("connect"),"Asset-only research and contract connect through generic APIs.");
                        GameValidation.Hack(app.State,"alpha");GameValidation.Hack(app.State,"beta");app.SelectNode("beta");smokePhase++;return;
                    case 37:
                        Check(app.Root.Q<Button>("action-audit")!=null&&app.Root.Q<GraphViewport>().NodeCount==4,"Asset-only action button and arbitrary network render.");
                        app.Root.Q<Button>("action-audit").GetFirstAncestorOfType<ScrollView>().ScrollTo(app.Root.Q<Button>("action-audit"));smokePhase=103;return;
                    case 103:
                        SendKey(app,KeyCode.V);Check(app.State.Op.task?.actionId=="audit","Asset-only shortcut executes the authored action.");GameValidation.FinishTask(app.State);
                        Check(app.State.ObjectiveComplete,"Asset-only objective completes.");CaptureScreenshot(Path.Combine(Project,"Artifacts/12-asset-defined-operation.png"));
                        smokePhase=38;return;
                    case 38:
                        Check(app.ActivateVisibleControl("disconnect"),"Asset-only objective delivers through UI.");smokePhase++;return;
                    case 39:
                        Check(app.ActivateVisibleControl("close-report"),"Asset-only report closes.");app.Navigate("rig");smokePhase++;return;
                    case 40:
                        app.Root.Q<Button>("upgrade-3").GetFirstAncestorOfType<ScrollView>().ScrollTo(app.Root.Q<Button>("upgrade-3"));smokePhase=104;return;
                    case 104:
                        Check(app.Root.Q<Button>("upgrade-3")!=null&&app.Root.Query<Label>().ToList().Any(l=>l.text.Contains("OF 4")),"New hardware and extra tier appear without UI branches.");
                        Check(app.ActivateVisibleControl("upgrade-3")&&app.State.Career.Level("expansion")==1,"Asset-only hardware purchases through UI.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/13-asset-defined-hardware.png"));app.Navigate("research");smokePhase=41;return;
                    case 41:
                        Check(app.Root.Q<Button>("tech-field-analysis")!=null,"Asset-only technology appears in research graph.");
                        CaptureScreenshot(Path.Combine(Project,"Artifacts/14-asset-defined-research.png"));SendKey(app,KeyCode.Escape);smokePhase++;return;
                    case 42:
                        app.SetTextSize(previousTextSize);
                        smokePhase=43;Check(app.ActivateVisibleControl("quit"),"Quit button is wired.");return;
                }
            }
            catch(Exception ex)
            {
                if(WorkstationApp.Instance!=null)WorkstationApp.Instance.SetTextSize(previousTextSize);
                WorkstationApp.Instance?.Visuals?.SetMode(previousEffects);frozenField=null;frozenGlass=null;
                SessionState.SetBool("HTP.Smoke",false);smokePhase=0;Status("FAIL / smoke\n"+ex);Debug.LogException(ex);EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(1);
            }
        }
        private static void CaptureScreenshot(string path)
        {
            if(smokeUiTexture==null){ScreenCapture.CaptureScreenshot(path);return;}
            var previous=RenderTexture.active;var pixels=new Texture2D(smokeUiTexture.width,smokeUiTexture.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active=smokeUiTexture;pixels.ReadPixels(new Rect(0,0,smokeUiTexture.width,smokeUiTexture.height),0,0);pixels.Apply();
                var data=pixels.GetPixels32();Check(data.Any(p=>p.g>40)&&data.Select(p=>p.g).Distinct().Count()>20,"Workstation capture contains nonblank rendered UI pixels.");
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void SendKey(WorkstationApp app,KeyCode key)
        {
            using(var evt=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=key}))app.Root.SendEvent(evt);
        }
        private static void ValidateSignalRendering(WorkstationApp app)
        {
            Check(app.Visuals.Available,"Both Ghost Frequency shaders are supported.");
            foreach(var name in new[]{"SignalField","PhosphorGlass"})
                Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("HackThePlanet/Shaders/"+name)),name+" compiles without errors.");
            var field=ReadPixels(app.Visuals.FieldTexture);var glass=ReadPixels(app.Visuals.GlassTexture);
            Check(field.Any(c=>c.g>c.r&&c.g>3)&&field.Select(c=>c.g).Distinct().Count()>5,"Signal-field GPU output contains a visible nonuniform image.");
            Check(glass.All(c=>c.a<65)&&glass.Any(c=>c.a>0),"Phosphor-glass GPU output has restrained nonzero transparency.");
            Check(app.Root.Q<Image>("signal-field").pickingMode==PickingMode.Ignore&&app.Root.Q<Image>("phosphor-glass").pickingMode==PickingMode.Ignore,"Shader decorations do not intercept input.");
            Check(Resources.Load<Font>("HackThePlanet/Fonts/SpaceMono-Regular")!=null,"Telemetry font is bundled for standalone players.");
        }
        private static Color32[] ReadPixels(RenderTexture texture)
        {
            Check(texture!=null&&texture.IsCreated(),"Shader render texture is allocated.");
            var previous=RenderTexture.active;var readback=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            try
            {RenderTexture.active=texture;readback.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);readback.Apply();return readback.GetPixels32();}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(readback);}
        }
        private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
