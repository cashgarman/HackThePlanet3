using System.Collections;
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
