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
                if(state==PlayModeStateChange.EnteredEditMode&&smokePhase==36)
                {
                    File.WriteAllText(Path.Combine(Project,"Artifacts/workstation-smoke.txt"),"PASS / Real player UI callbacks: connect, scan, hack, recover, deliver, research purchase, hardware upgrade. Both shaders compile and render nonblank GPU textures; display modes and GPU texture reuse passed. Settings button and Escape, exact menu choices, menu bounds, operation freeze, previous pause state, blocked background controls and shortcuts, reset confirmation, Cancel and Escape cancellation, confirmed fresh career, usable first contract, and Quit passed. Eleven rendered screenshots captured. Test career and effect preference isolated from user saves.");
                    settingsCareer=null;SessionState.SetBool("HTP.Smoke",false);smokePhase=0;Status("PASS / smoke");
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
                    case "setup": Setup();break;
                    case "validate": GameValidation.Run();break;
                    case "models": NetworkVisualValidation.Run();break;
                    case "build": BuildWindows();break;
                    case "build-volumetric": BuildWindowsAt("Builds/Windows-Volumetric");break;
                    case "play": EditorApplication.isPlaying=true;break;
                    case "stop": EditorApplication.isPlaying=false;break;
                    case "smoke": StartSmoke();return;
                    case "capture": ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/live-capture.png"));break;
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
                        previousEffects=app.Visuals.Mode;app.UseTestCareer(new Career());app.Visuals.SetMode(SignalEffects.Full);smokePhase++;return;
                    case 2:
                        Check(app.Root.Q<UnityEngine.UIElements.Button>("connect")!=null,"Connect control exists.");
                        var preview=app.Root.Q<GraphViewport>();
                        Check(preview!=null&&preview.NodeCount==32&&preview.VisibleNodes==32&&preview.VisibleEdges>0,
                            "GraphExplorer renders all preview systems and their links in 3D: "+preview?.NodeCount+" nodes / "+preview?.VisibleNodes+" drawn / "+preview?.VisibleEdges+" links.");
                        Check(app.Root.Q<Button>("node-home")!=null,"Projected system labels remain selectable.");
                        ValidateSignalRendering(app);
                        NetworkVisualValidation.CheckViewport(preview);
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/01-contract-exchange.png"));smokePhase=40;return;
                    case 40:
                        NetworkVisualValidation.Orbit(app.Root.Q<GraphViewport>(),55,15);smokePhase=41;return;
                    case 41:
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/network-orbit.png"));
                        smokePhase=42;return;
                    case 42:
                        NetworkVisualValidation.Orbit(app.Root.Q<GraphViewport>(),0,28);smokePhase=3;return;
                    case 3:
                        Check(app.ActivateVisibleControl("connect"),"Connect button works.");Check(app.State.Active,"Mission connected through UI.");smokePhase++;return;
                    case 4:
                        Check(app.ActivateVisibleControl("action-scan"),"Scan button is wired.");
                        GameValidation.FinishTask(app.State);app.SelectNode("n2");smokePhase++;return;
                    case 5:
                        Check(app.ActivateVisibleControl("action-hack"),"Hack button is wired.");GameValidation.FinishTask(app.State);
                        GameValidation.Hack(app.State,"n6");GameValidation.Hack(app.State,"n10");app.SelectNode("n14");smokePhase++;return;
                    case 6:
                        var operationGraph=app.Root.Q<GraphViewport>();
                        Check(operationGraph!=null&&operationGraph.NodeCount>2&&operationGraph.NodeCount<app.State.Op.nodes.Count&&operationGraph.VisibleEdges>0,
                            "Live GraphExplorer view shows discovered systems and connections only.");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/02-live-operation.png"));
                        GameValidation.Hack(app.State,"n14");GameValidation.Hack(app.State,"n18");GameValidation.Hack(app.State,"n22");app.SelectNode("n22");smokePhase++;return;
                    case 7:
                        Check(app.ActivateVisibleControl("action-download"),"Recovery button is wired.");GameValidation.FinishTask(app.State);smokePhase++;return;
                    case 8:
                        Check(app.ActivateVisibleControl("disconnect"),"Delivery button works.");Check(!app.State.Active&&app.State.Career.jobs==1,"Delivery banked rewards.");smokePhase++;return;
                    case 9:
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/03-operation-result.png"));smokePhase++;return;
                    case 10:
                        Check(app.ActivateVisibleControl("report-research"),"Research result link works.");smokePhase++;return;
                    case 11:
                        Check(app.Root.Q<GraphViewport>()?.NodeCount==GameContent.Tech.Length&&app.Root.Q<Button>("tech-wide")!=null,
                            "GraphExplorer renders selectable research technologies.");
                        Check(app.ActivateVisibleControl("unlock"),"Research purchase works.");Check(app.State.Has("wide"),"Tech purchased using earned XP.");
                        // Preview a developed build for the research screenshot without touching the real career.
                        app.State.Career.xp=360;app.State.BuyTech("quiet");app.State.BuyTech("efficient");app.SelectTech("passive");smokePhase++;return;
                    case 12:
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/04-research-network.png"));smokePhase++;return;
                    case 13:
                        app.Navigate("rig");smokePhase++;return;
                    case 14:
                        Check(app.ActivateVisibleControl("upgrade-0"),"Hardware purchase button works.");Check(app.State.Career.cpu==1,"CPU upgraded.");smokePhase++;return;
                    case 15:
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/05-your-rig.png"));smokePhase++;return;
                    case 16:
                        // Isolated display fixture; banking and failure semantics are exercised by NetworkValidation.
                        app.State.Career.lore.Add(LoreContent.For(GameContent.Story[0],0));
                        app.Navigate("intel");smokePhase++;return;
                    case 17:
                        Check(app.Root.Query<Label>().ToList().Any(l=>l.text=="The missing appendix"),"Permanent lore collection displays fragment titles.");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/06-intelligence.png"));smokePhase++;return;
                    case 18:
                        app.StartSelected();smokePhase++;return;
                    case 19:
                        GameValidation.Hack(app.State,"n2");GameValidation.Hack(app.State,"n6");GameValidation.Hack(app.State,"n10");
                        app.SelectNode("n14");app.State.Op.trace=87;
                        Check(app.State.Execute(ToolKind.Scan,"n14"),"Trace-warning fixture can run a real tool.");
                        app.State.Tick(app.State.Op.task.duration*.42f);app.State.SetPaused(true);smokePhase++;return;
                    case 20:
                        Check(app.Root.ClassListContains("trace-alert"),"High trace changes the workstation warning treatment.");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/07-trace-warning.png"));
                        Check(app.ActivateVisibleControl("effects"),"Effects control is wired.");
                        Check(app.Visuals.Mode==SignalEffects.Reduced,"Full effects cycle to static reduced effects.");smokePhase++;return;
                    case 21:
                        frozenField=ReadPixels(app.Visuals.FieldTexture);frozenGlass=ReadPixels(app.Visuals.GlassTexture);
                        sharedField=app.Visuals.FieldTexture;sharedGlass=app.Visuals.GlassTexture;
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/08-reduced-effects.png"));smokePhase++;return;
                    case 22:
                        Check(frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture))&&frozenGlass.SequenceEqual(ReadPixels(app.Visuals.GlassTexture)),"Reduced shader textures stay exactly static across frames.");
                        Check(app.ActivateVisibleControl("effects")&&app.Visuals.Mode==SignalEffects.Off,"Reduced effects cycle to off.");smokePhase++;return;
                    case 23:
                        Check(app.Root.Q<Image>("signal-field").resolvedStyle.display==DisplayStyle.None&&app.Root.Q<Image>("phosphor-glass").resolvedStyle.display==DisplayStyle.None,"Off mode hides both shader layers.");
                        Check(app.Root.Q<Button>("action-scan")!=null,"Action controls survive effects changes.");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/09-effects-off.png"));
                        Check(app.ActivateVisibleControl("effects")&&app.Visuals.Mode==SignalEffects.Full,"Off effects cycle back to full.");smokePhase++;return;
                    case 24:
                        Check(ReferenceEquals(sharedField,app.Visuals.FieldTexture)&&ReferenceEquals(sharedGlass,app.Visuals.GlassTexture),"UI rebuilds reuse both GPU textures.");
                        Check(!frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),"Full effects animate the signal field.");
                        app.Visuals.SetMode(previousEffects);frozenField=null;frozenGlass=null;
                        settingsCareer=app.State.Career;settingsElapsed=app.State.Op.elapsed;settingsTaskElapsed=app.State.Op.task.elapsed;settingsTrace=app.State.Op.trace;
                        Check(app.ActivateVisibleControl("settings"),"Settings rail button opens the menu.");smokePhase++;return;
                    case 25:
                        var menu=app.Root.Q<VisualElement>(className:"settings-menu");
                        Check(menu!=null&&menu.Query<Button>().ToList().Select(b=>b.text).SequenceEqual(new[]{"Resume","Reset Game Save","Quit"}),"Settings contains exactly Resume, Reset Game Save, and Quit.");
                        Check(menu.worldBound.width>300&&menu.worldBound.height>200&&app.Root.worldBound.Contains(menu.worldBound.min)&&app.Root.worldBound.Contains(menu.worldBound.max),"Settings menu fits within the rendered viewport.");
                        Check(app.State.Op.paused&&app.State.Op.elapsed==settingsElapsed&&app.State.Op.task.elapsed==settingsTaskElapsed&&app.State.Op.trace==settingsTrace,"Settings freezes operation time, tool progress, and trace across frames.");
                        Check(!app.ActivateVisibleControl("action-scan")&&!app.ActivateVisibleControl("tab-research"),"Settings blocks underlying gameplay and navigation controls.");
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/10-settings-menu.png"));
                        Check(app.ActivateVisibleControl("resume")&&app.State.Op.paused,"Resume preserves an already paused operation.");
                        app.State.SetPaused(false);SendKey(app,KeyCode.Escape);
                        Check(app.State.Op.paused,"Escape opens Settings and pauses a running operation.");smokePhase++;return;
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
                        ScreenCapture.CaptureScreenshot(Path.Combine(Project,"Artifacts/11-reset-save-confirmation.png"));
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
                        SendKey(app,KeyCode.Escape);smokePhase++;return;
                    case 35:
                        smokePhase=36;Check(app.ActivateVisibleControl("quit"),"Quit button is wired.");return;
                }
            }
            catch(Exception ex)
            {
                WorkstationApp.Instance?.Visuals?.SetMode(previousEffects);frozenField=null;frozenGlass=null;
                SessionState.SetBool("HTP.Smoke",false);smokePhase=0;Status("FAIL / smoke\n"+ex);Debug.LogException(ex);EditorApplication.isPlaying=false;
            }
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
