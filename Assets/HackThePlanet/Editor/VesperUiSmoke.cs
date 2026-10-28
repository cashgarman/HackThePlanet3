using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace HackThePlanet.Editor
{
    /// <summary>Rendered current-campaign smoke using isolated UI callbacks.</summary>
    [InitializeOnLoad]
    public static class VesperUiSmoke
    {
        private static int frames, phase;
        private static RenderTexture texture;
        static VesperUiSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Vesper.Smoke", false)) { frames = phase = 0; EditorApplication.update -= Tick; EditorApplication.update += Tick; }
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Vesper.SmokeDone", false))
                { SessionState.SetBool("Vesper.SmokeDone", false); if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool("Vesper.SmokePass", false) ? 0 : 1); }
            };
        }
        public static void Start() { SessionState.SetBool("Vesper.Smoke", true); if (EditorApplication.isPlaying) { phase=frames=0; EditorApplication.update-=Tick;EditorApplication.update+=Tick; } else EditorApplication.isPlaying = true; }
        private static void Check(bool pass, string text) { if (!pass) throw new Exception(text); }
        private static void Tick()
        {
            if (++frames < 30) return; frames=0;
            var app=WorkstationApp.Instance; if(app==null)return;
            try
            {
                switch(phase++)
                {
                    case 0:
                        app.UseTestCatalog(GameCatalog.Default);texture=new RenderTexture(1680,960,24);texture.Create();app.GetComponent<UIDocument>().panelSettings.targetTexture=texture;break;
                    case 1:
                        Check(app.Root.Q<Button>("contract-ghost-meter")!=null,"New first contract renders");
                        Capture("Artifacts/vesper-workstation.png");
                        var career=GameCatalog.Default.CreateCareer();career.completed.AddRange(new[]{"ghost-meter","redacted-hours","unlisted-operator","listening-room"});app.UseTestCatalog(GameCatalog.Default,career);break;
                    case 2:
                        Check(app.ActivateVisibleControl("story-choice-shelter"),"Alliance callback");break;
                    case 3:
                        Check(app.Root.Q<Button>("contract-cold-clinic")!=null && app.Root.Q<Button>("contract-margin-notes")==null,"Alliance filters offers");
                        Capture("Artifacts/vesper-alliance.png");
                        var clues=GameCatalog.Default.CreateCareer();clues.completed.AddRange(new[]{"ghost-meter","redacted-hours"});app.UseTestCatalog(GameCatalog.Default,clues,"unlisted-operator");break;
                    case 4:
                        Check(app.ActivateVisibleControl("connect"),"Connect callback");
                        foreach(var id in new[]{"n22","n9","n19"}) app.State.Node(id).collected=true;
                        app.SelectNode("n22");app.State.SetManualPause(true);break;
                    case 5: Check(app.ActivateVisibleControl("case-answer-accident")&&!app.State.EvidenceSolved,"Wrong answer remains retryable");break;
                    case 6: Capture("Artifacts/vesper-evidence.png");Check(app.ActivateVisibleControl("case-answer-rehearsal")&&app.State.EvidenceSolved,"Correct evidence callback");break;
                    default:
                        File.WriteAllText("Artifacts/vesper-ui-smoke.txt","PASS / New exchange, exclusive alliance buttons, filtered offers, evidence requirements and answer callbacks; rendered workstation, alliance and case captures. Test career isolated from player saves.");Finish(app,true);break;
                }
            }
            catch(Exception ex){Debug.LogException(ex);File.WriteAllText("Artifacts/vesper-ui-smoke.txt","FAIL / "+ex);Finish(app,false);}
        }
        private static void Capture(string path)
        {
            var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();RenderTexture.active=previous;
            Check(pixels.GetPixels32().Select(c=>(c.r,c.g,c.b)).Distinct().Take(20).Count()>10,"Nonblank rendered UI");File.WriteAllBytes(path,pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
        }
        private static void Finish(WorkstationApp app,bool pass)
        {
            EditorApplication.update-=Tick;app.GetComponent<UIDocument>().panelSettings.targetTexture=null;
            if(texture!=null){texture.Release();UnityEngine.Object.DestroyImmediate(texture);texture=null;}
            Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/HackThePlanet.status", pass ? "PASS / smoke" : "FAIL / smoke; see Artifacts/vesper-ui-smoke.txt");
            SessionState.SetBool("Vesper.Smoke",false);SessionState.SetBool("Vesper.SmokeDone",true);SessionState.SetBool("Vesper.SmokePass",pass);EditorApplication.isPlaying=false;
        }
    }
    public static class VesperReleaseValidation
    {
        public static void RunBatch()
        {
            try { ContentValidation.Run(); VesperValidation.Run(); GameBuildTools.BuildWindows(); File.WriteAllText("Artifacts/vesper-release.txt", "PASS / Content history and generated-asset parity, 240 full careers, 22 module placement audits, Windows player and packaged JSON."); EditorApplication.Exit(0); }
            catch(Exception ex){Debug.LogException(ex);File.WriteAllText("Artifacts/vesper-release.txt","FAIL / "+ex);EditorApplication.Exit(1);}
        }
    }
}
