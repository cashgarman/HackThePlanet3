using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph navigation.</summary>
	public sealed class GraphNavigation : IDisposable
	{
		private const float PanInertiaDrag = 5.5f;
		private const float FlightAcceleration = 5f;
		private const float FlightDrag = 1.8f;
		private const float WheelZoom = .1f;
		private readonly Camera camera;

		/// <summary>Gets or sets the movement speed used for camera navigation.</summary>
		public float Speed = 22;

		private bool framing, looking, orbiting;
		private Vector3 orbitCenter, destination, panVelocity, flightVelocity, trackedPosition;
		private bool ownsCursor;
		private bool previousCursorVisible;
		private CursorLockMode previousLock;
		private IGraphNode subject;
		private float yaw, pitch;

		/// <summary>Creates navigation controls for a camera.</summary>
		/// <param name="value">Camera to control.</param>
		public GraphNavigation(Camera value)
		{
			camera = value;
			SyncAngles();
		}

		/// <summary>Gets whether the camera is in look or orbit mode.</summary>
		public bool Looking => looking || orbiting;

		/// <summary>Gets whether the camera tracks a node.</summary>
		public bool Tracking { get; private set; }

		/// <summary>Gets the tracked node, or null when tracking is inactive.</summary>
		public IGraphNode Tracked => Tracking ? subject : null;

		/// <summary>Releases cursor capture and stops camera tracking.</summary>
		public void Dispose()
		{
			Release();
			StopTracking();
		}

		/// <summary>Plain F frames the selection. Ctrl/Cmd+F stays reserved for search.</summary>
		/// <summary>Determines whether the plain F key requests framing the selection.</summary>
		/// <returns><see langword="true" /> when F was pressed without Ctrl or Command.</returns>
		public static bool FrameKeyPressed()
		{
			var keyboard = Keyboard.current;
			return keyboard != null && keyboard.fKey.wasPressedThisFrame
									&& !keyboard.ctrlKey.isPressed && !keyboard.leftMetaKey.isPressed &&
									!keyboard.rightMetaKey.isPressed;
		}

		private void SyncAngles()
		{
			yaw = camera.transform.eulerAngles.y;
			pitch = Mathf.DeltaAngle(0, camera.transform.eulerAngles.x);
		}

		/// <summary>Moves the camera to frame and track a node.</summary>
		/// <param name="node">Node to focus, or null to do nothing.</param>
		/// <param name="radius">Approximate node radius used to choose a viewing distance.</param>
		public void Focus(IGraphNode node, float radius = 12)
		{
			if (node == null) return;
			if (looking) Release();
			subject = node;
			Tracking = true;
			trackedPosition = node.Position;
			var orbitDistance = Mathf.Max(8, radius / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
			var currentDistance = Vector3.Distance(camera.transform.position, node.Position);
			// Nodes already closer than the orbit distance keep the current distance as the new zoom level;
			// distant nodes still frame at the orbit distance.
			FrameAtDistance(node.Position,
							currentDistance < orbitDistance ? Mathf.Max(1f, currentDistance) : orbitDistance);
		}

		/// <summary>Stops tracking the current node.</summary>
		public void StopTracking()
		{
			Tracking = false;
			subject = null;
			if (orbiting) Release();
		}

		/// <summary>Changes the tracked node while preserving the current camera framing.</summary>
		/// <param name="node">Node to track.</param>
		public void Retarget(IGraphNode node)
		{
			if (!Tracking || node == null) return;
			subject = node;
			trackedPosition = node.Position;
			orbitCenter = node.Position;
		}

		/// <summary>Frames a world-space point with the requested approximate radius.</summary>
		/// <param name="center">Center point to frame.</param>
		/// <param name="radius">Approximate radius of the content.</param>
		public void Frame(Vector3 center, float radius = 12)
		{
			FrameAtDistance(center, Mathf.Max(8, radius / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f)));
		}

		private void FrameAtDistance(Vector3 center, float distance)
		{
			// Cancel residual input motion and ease toward the target along the current viewing direction.
			panVelocity = Vector3.zero;
			flightVelocity = Vector3.zero;
			orbitCenter = center;
			destination = center - camera.transform.forward * distance;
			framing = true;
		}

		/// <summary>Frames all visible nodes in a scene.</summary>
		/// <param name="graph">Scene whose visible nodes are framed.</param>
		public void Overview(IGraphScene graph)
		{
			// Fit only visible nodes, retaining a small default framing volume for an empty scene.
			StopTracking();
			panVelocity = Vector3.zero;
			var bounds = new Bounds(Vector3.zero, Vector3.one * 8);
			var first = true;
			foreach (var node in graph.Nodes.Values)
				if (node.Visible)
				{
					if (first)
					{
						bounds = new Bounds(node.Position, Vector3.one * 6);
						first = false;
					}
					else
					{
						bounds.Encapsulate(node.Position);
					}
				}

			Frame(bounds.center, bounds.extents.magnitude * 1.3f);
		}

		/// <summary>Updates the tracked camera target, optionally holding the current offset.</summary>
		/// <param name="hold">When true, updates the tracking origin without moving the camera.</param>
		public void Follow(bool hold)
		{
			// While held, update the target without translating the camera; otherwise follow its exact displacement.
			if (!Tracking || subject == null) return;
			if (hold)
			{
				trackedPosition = subject.Position;
				orbitCenter = subject.Position;
				framing = false;
				return;
			}

			var delta = subject.Position - trackedPosition;
			trackedPosition = subject.Position;
			if (delta.sqrMagnitude <= 1e-12f) return;
			camera.transform.position += delta;
			destination += delta;
			orbitCenter += delta;
		}

		/// <summary>Processes camera movement and pointer controls for the current frame.</summary>
		/// <param name="blocked">Whether UI currently blocks graph interaction.</param>
		/// <param name="selected">Currently selected node.</param>
		/// <param name="frameSelected">Whether to frame the selected node this frame.</param>
		public void Tick(bool blocked, IGraphNode selected, bool frameSelected = false)
		{
			// Ignore navigation input while UI owns the pointer or keyboard focus.
			var mouse = Mouse.current;
			var keyboard = Keyboard.current;
			if (mouse == null || keyboard == null) return;
			if (frameSelected) Focus(selected);
			// Locked nodes orbit under left-drag while tracking remains active.
			var trackOrbit = Tracking && subject != null && mouse.leftButton.isPressed && !blocked;
			var delta = mouse.delta.ReadValue();
			var dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
			var coastFlight = true;
			if (trackOrbit)
			{
				// Orbit a tracked subject while clearing incompatible pan and flight momentum.
				if (!orbiting)
				{
					orbiting = true;
					panVelocity = Vector3.zero;
					flightVelocity = Vector3.zero;
					CaptureCursor();
					SyncAngles();
				}

				framing = false;
				orbitCenter = subject.Position;
				camera.transform.RotateAround(orbitCenter, Vector3.up, delta.x * .25f);
				camera.transform.RotateAround(orbitCenter, camera.transform.right, -delta.y * .25f);
				SyncAngles();
				Dolly(mouse.scroll.ReadValue().y);
				coastFlight = false;
			}
			else if (orbiting)
			{
				orbiting = false;
				Release();
			}

			if (!orbiting && !blocked)
			{
				var panning = mouse.middleButton.isPressed || mouse.rightButton.isPressed;
				if (mouse.middleButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
				{
					panVelocity = Vector3.zero;
					flightVelocity = Vector3.zero;
				}

				if (panning)
				{
					// Translate in the view plane and retain drag velocity for inertial panning after release.
					coastFlight = false;
					flightVelocity = Vector3.zero;
					var move = (-camera.transform.right * delta.x - camera.transform.up * delta.y) * Speed * .0025f;
					if (dt > 1e-5f) panVelocity = move / dt;
					camera.transform.position += move;
					orbitCenter += move;
					framing = false;
				}
				else if (!framing && panVelocity.sqrMagnitude > 1e-4f)
				{
					camera.transform.position += panVelocity * dt;
					orbitCenter += panVelocity * dt;
					panVelocity *= Mathf.Exp(-PanInertiaDrag * dt);
					if (panVelocity.sqrMagnitude < 1e-4f) panVelocity = Vector3.zero;
				}

				if (!trackOrbit && mouse.leftButton.isPressed)
				{
					// Orbit the current selection, or the existing orbit center when nothing is selected.
					coastFlight = false;
					panVelocity = Vector3.zero;
					flightVelocity = Vector3.zero;
					framing = false;
					if (selected != null) orbitCenter = selected.Position;
					camera.transform.RotateAround(orbitCenter, Vector3.up, delta.x * .25f);
					camera.transform.RotateAround(orbitCenter, camera.transform.right, -delta.y * .25f);
					SyncAngles();
				}

				var wheel = mouse.scroll.ReadValue().y;
				if (wheel != 0)
				{
					coastFlight = false;
					Dolly(wheel);
				}
			}
			else if (!orbiting && panVelocity.sqrMagnitude > 1e-4f)
			{
				panVelocity = Vector3.zero;
			}

			if (coastFlight && !framing && !Tracking) IntegrateFlight(dt, Vector3.zero);
			if (framing)
			{
				// Ease toward the framing destination until the remaining offset is negligible.
				camera.transform.position =
					Vector3.Lerp(camera.transform.position, destination, 1 - Mathf.Exp(-6 * dt));
				if ((camera.transform.position - destination).sqrMagnitude < .01f) framing = false;
			}
		}

		private void CaptureCursor()
		{
			// Save the host's cursor state only once across repeated look or orbit input frames.
			if (!ownsCursor)
			{
				previousLock = Cursor.lockState;
				previousCursorVisible = Cursor.visible;
				ownsCursor = true;
			}

			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}

		/// <summary>Stops camera interaction and restores the cursor state captured by this controller.</summary>
		public void Release()
		{
			// Restore the cursor state that was active before this navigation controller captured it.
			looking = false;
			orbiting = false;
			if (!ownsCursor) return;
			Cursor.lockState = previousLock;
			Cursor.visible = previousCursorVisible;
			ownsCursor = false;
		}

		private void Dolly(float wheel)
		{
			// Scale dolly distance by distance to the orbit center while clearing conflicting inertia.
			if (wheel == 0) return;
			panVelocity = Vector3.zero;
			flightVelocity = Vector3.zero;
			framing = false;
			wheel = Mathf.Clamp(wheel, -3f, 3f);
			var distance = Vector3.Distance(camera.transform.position, orbitCenter);
			if (distance < 6f) distance = 6f;
			camera.transform.position += camera.transform.forward * distance * (1f - Mathf.Pow(1f - WheelZoom, wheel));
		}

		private void IntegrateFlight(float dt, Vector3 wish)
		{
			// Accelerate toward active input and use a separate exponential drag when coasting.
			var rate = wish.sqrMagnitude > 1e-8f ? FlightAcceleration : FlightDrag;
			flightVelocity = Vector3.Lerp(flightVelocity, wish, 1f - Mathf.Exp(-rate * dt));
			if (wish.sqrMagnitude <= 1e-8f && flightVelocity.sqrMagnitude < 1e-4f) flightVelocity = Vector3.zero;
			var step = flightVelocity * dt;
			camera.transform.position += step;
			orbitCenter += step;
		}

		/// <summary>Restores the camera transform and resets navigation motion and tracking state.</summary>
		/// <param name="position">World-space camera position.</param>
		/// <param name="rotation">Camera rotation.</param>
		public void Restore(Vector3 position, Quaternion rotation)
		{
			// Restore a saved pose without retaining tracking, framing, captured cursor, or prior velocities.
			StopTracking();
			framing = false;
			panVelocity = Vector3.zero;
			flightVelocity = Vector3.zero;
			Release();
			camera.transform.SetPositionAndRotation(position, rotation);
			orbitCenter = position + camera.transform.forward * 20;
			SyncAngles();
		}
	}
}