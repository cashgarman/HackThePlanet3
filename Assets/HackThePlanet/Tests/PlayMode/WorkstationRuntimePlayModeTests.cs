using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HackThePlanet.Tests.PlayMode
{
	public sealed class WorkstationRuntimePlayModeTests
	{
		private WorkstationApp app;
		private GameObject fixtureCamera;
		private Camera mainCamera;
		private CameraClearFlags oldClearFlags;
		private Color oldBackground;
		private int oldCullingMask, oldTargetFrameRate;
		private bool oldRunInBackground;

		[UnitySetUp]
		public IEnumerator CreateWorkstationHost()
		{
			oldTargetFrameRate = Application.targetFrameRate;
			oldRunInBackground = Application.runInBackground;
			app = WorkstationApp.Instance;
			if (app == null)
			{
				// Keep one tagged camera alive for the fixture so each host reuses the same URP pipeline.
				if (Camera.main == null)
				{
					fixtureCamera = new GameObject("Hack The Planet PlayMode Camera") { tag = "MainCamera" };
					fixtureCamera.AddComponent<Camera>();
				}
				mainCamera = Camera.main;
				if (mainCamera != null)
				{
					oldClearFlags = mainCamera.clearFlags;
					oldBackground = mainCamera.backgroundColor;
					oldCullingMask = mainCamera.cullingMask;
				}
				app = new GameObject("Hack The Planet PlayMode Test Host").AddComponent<WorkstationApp>();
			}
			Assert.That(app, Is.Not.Null, "The workstation bootstrap should create its runtime host.");
			Assert.That(app.State, Is.Not.Null, "The workstation should load its simulation.");
			Assert.That(app.Root, Is.Not.Null, "The workstation should attach its UI Toolkit root.");
			Assert.That(app.Visuals, Is.Not.Null, "The workstation should create display effects state.");

			// Switch from the saved career to an isolated in-memory career before acting.
			app.UseTestCatalog(GameCatalog.Default);
			app.ActivateVisibleControl("tab-network");
			DisableGraphViewportCameras();
			yield break;
		}

		[UnityTearDown]
		public IEnumerator DestroyWorkstationHost()
		{
			DisableGraphViewportCameras();
			if (app != null && app.gameObject != null)
				Object.Destroy(app.gameObject);
			yield return null;
			Application.targetFrameRate = oldTargetFrameRate;
			Application.runInBackground = oldRunInBackground;
			if (mainCamera != null)
			{
				mainCamera.clearFlags = oldClearFlags;
				mainCamera.backgroundColor = oldBackground;
				mainCamera.cullingMask = oldCullingMask;
			}
		}

		[OneTimeTearDown]
		public void DestroyFixtureCamera()
		{
			if (fixtureCamera != null)
				Object.Destroy(fixtureCamera);
		}

		private static void DisableGraphViewportCameras()
		{
			foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
				if (camera != null && camera.name == "Workstation GraphExplorer Camera")
					camera.enabled = false;
		}

		[Test]
		public void SecurityBriefAndLiveCountdownExposeCountermeasureControls()
		{
			var catalog = GameCatalog.Default;
			var setup = new GameSimulation(catalog);
			setup.Career.completed.AddRange(new[] { "ghost-meter", "redacted-hours", "unlisted-operator", "listening-room" });
			setup.Career.jobs = 4;
            setup.Career.storyFlags.Add("arc-complete");
			setup.Career.xp = 1000;
			foreach (var id in new[] { "efficient", "chain", "selective", "protocol-spoof" })
				Assert.That(setup.BuyTech(id), Is.True);
			app.UseTestCatalog(catalog, setup.Career, "thirteenth-test");
			app.SendMessage("Update");
			DisableGraphViewportCameras();
			Assert.That(app.Root.Query<Label>().ToList().Any(l => l.text.Contains("Extract Key (core) or Credential Fork")), Is.True);
			Assert.That(app.ActivateVisibleControl("connect"), Is.True);
			app.State.SetManualPause(true);
			app.SendMessage("Update");
			DisableGraphViewportCameras();
			var sweep = app.State.Defenses.First(d => d.kind == DefenseKind.SecuritySweep);
			sweep.remaining = 5;
			app.SendMessage("Update");
			Assert.That(app.Root.Query<Label>().ToList().Any(l => l.text == "NEXT SECURITY SWEEP / 5s"), Is.True);
			Assert.That(app.State.Node("n19").label, Does.Contain("Key server"));
			app.State.Node("n22").known = app.State.Node("n22").controlled = true;
			app.SelectNode("n22");
			app.SendMessage("Update");
			DisableGraphViewportCameras();
			Assert.That(app.Root.Q<Button>("action-spoof-gate"), Is.Not.Null);
			Assert.That(app.ActivateVisibleControl("action-spoof-gate"), Is.True);
			Assert.That(app.State.Op.task.actionId, Is.EqualTo("spoof-gate"));
		}


        [Test]
        public void StoryChoiceButtonsOpenOnlyTheSelectedAlliance()
        {
            var career = GameCatalog.Default.CreateCareer();
            career.completed.AddRange(new[] { "ghost-meter", "redacted-hours", "unlisted-operator", "listening-room" });
            app.UseTestCatalog(GameCatalog.Default, career); app.SendMessage("Update"); DisableGraphViewportCameras();
            Assert.That(app.ActivateVisibleControl("story-choice-shelter"), Is.True);
            app.SendMessage("Update"); DisableGraphViewportCameras();
            Assert.That(app.State.Career.storyFlags, Does.Contain("shelter"));
            Assert.That(app.Root.Q<Button>("contract-cold-clinic"), Is.Not.Null);
            Assert.That(app.Root.Q<Button>("contract-margin-notes"), Is.Null);
        }

        [Test]
        public void EvidencePanelUsesRealAnswerCallbacksAndKeepsWrongAnswersRetryable()
        {
            var career = GameCatalog.Default.CreateCareer(); career.completed.AddRange(new[] { "ghost-meter", "redacted-hours" });
            app.UseTestCatalog(GameCatalog.Default, career, "unlisted-operator");
            app.SendMessage("Update"); Assert.That(app.ActivateVisibleControl("connect"), Is.True);
            foreach (var id in new[] { "n22", "n9", "n19" }) app.State.Node(id).collected = true;
            app.SelectNode("n22"); app.SendMessage("Update"); DisableGraphViewportCameras();
            Assert.That(app.ActivateVisibleControl("case-answer-accident"), Is.True);
            Assert.That(app.State.EvidenceSolved, Is.False);
            app.SendMessage("Update"); DisableGraphViewportCameras();
            Assert.That(app.ActivateVisibleControl("case-answer-rehearsal"), Is.True);
            Assert.That(app.State.EvidenceSolved, Is.True);
        }

        [UnityTest]
        public IEnumerator NewCampaignRendersNonblankWorkstation()
        {
            app.UseTestCatalog(GameCatalog.Default); app.SendMessage("Update");
            var texture = new RenderTexture(1680, 960, 24); texture.Create();
            var panel = app.GetComponent<UIDocument>().panelSettings;
            panel.targetTexture = texture;
            yield return null; yield return null; yield return null;
            var previous = RenderTexture.active; RenderTexture.active = texture;
            var capture = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); capture.Apply(); RenderTexture.active = previous;
            var colors = capture.GetPixels32();
            Assert.That(colors.Select(c => (c.r, c.g, c.b)).Distinct().Take(20).Count(), Is.GreaterThan(10));
            System.IO.File.WriteAllBytes("Artifacts/vesper-workstation.png", capture.EncodeToPNG());
            panel.targetTexture = null; texture.Release(); Object.Destroy(texture); Object.Destroy(capture);
        }

		[Test]
		public void WorkstationTabsBuildTheirViewsAndCanBeActivatedByName()
		{
			Assert.That(app.CurrentView, Is.EqualTo("network"));
			Assert.That(app.ActivateVisibleControl("tab-research"), Is.True);
			Assert.That(app.CurrentView, Is.EqualTo("research"));
			DisableGraphViewportCameras();
			Assert.That(app.Root.Q<VisualElement>(className: "workspace-row"), Is.Not.Null);

			Assert.That(app.ActivateVisibleControl("tab-rig"), Is.True);
			Assert.That(app.CurrentView, Is.EqualTo("rig"));
			DisableGraphViewportCameras();
			Assert.That(app.Root.Q<VisualElement>(className: "workspace-row"), Is.Not.Null);

			Assert.That(app.ActivateVisibleControl("tab-intel"), Is.True);
			Assert.That(app.CurrentView, Is.EqualTo("intel"));
			DisableGraphViewportCameras();
			Assert.That(app.Root.Q<VisualElement>(className: "workspace-row"), Is.Not.Null);
		}

		[Test]
		public void LeavingNetworkPausesTheActiveOperationAndReturningDoesNotResumeIt()
		{
			var job = app.State.Catalog.Contracts(app.State.Career)[0];
			Assert.That(app.State.Start(job), Is.True);
			Assert.That(app.State.Op.paused, Is.False);

			app.Navigate("research");
			Assert.That(app.State.Op.paused, Is.True);
			app.Navigate("network");
			Assert.That(app.State.Op.paused, Is.True);
		}

		[Test]
		public void TestingModeClampsPreferencesAndDoesNotWritePlayerPrefs()
		{
			Assert.That(app.Testing, Is.True);
			var oldText = PlayerPrefs.GetInt(WorkstationApp.TextSizePreference, int.MinValue);
			var oldFocus = PlayerPrefs.GetInt(WorkstationApp.SelectWithoutCameraFocusPreference, int.MinValue);
			var oldColor = PlayerPrefs.GetString(WorkstationApp.HackedConnectionColorPreference, "missing");
			var previousColor = WorkstationApp.HackedConnectionColor;

			app.SetTextSize(999);
			app.SetSelectWithoutCameraFocus(false);
			app.SetHackedConnectionColor(new Color(.1f, .2f, .3f, .2f));

			Assert.That(app.TextSizePercent, Is.EqualTo(300));
			Assert.That(app.SelectWithoutCameraFocus, Is.False);
			Assert.That(WorkstationApp.HackedConnectionColor.a, Is.EqualTo(1));
			Assert.That(PlayerPrefs.GetInt(WorkstationApp.TextSizePreference, int.MinValue), Is.EqualTo(oldText));
			Assert.That(PlayerPrefs.GetInt(WorkstationApp.SelectWithoutCameraFocusPreference, int.MinValue), Is.EqualTo(oldFocus));
			Assert.That(PlayerPrefs.GetString(WorkstationApp.HackedConnectionColorPreference, "missing"), Is.EqualTo(oldColor));
			app.SetHackedConnectionColor(previousColor);
		}

		[Test]
		public void DisplayEffectTogglesDeriveModeAndCanBeRestoredWithoutPersistence()
		{
			var visuals = app.Visuals;
			visuals.SetMode(SignalEffects.Off);
			Assert.That(visuals.Mode, Is.EqualTo(SignalEffects.Off));
			Assert.That(visuals.Label, Is.EqualTo("FX0"));

			visuals.SetSignalFieldEnabled(true);
			Assert.That(visuals.Mode, Is.EqualTo(SignalEffects.Reduced));
			visuals.SetAnimateDisplayEffects(true);
			Assert.That(visuals.Mode, Is.EqualTo(SignalEffects.Full));
			Assert.That(visuals.Label, Is.EqualTo("FX+"));

			visuals.SetPhosphorGlassEnabled(false);
			visuals.SetSignalFieldEnabled(false);
			Assert.That(visuals.Mode, Is.EqualTo(SignalEffects.Off));
		}

	}
}
