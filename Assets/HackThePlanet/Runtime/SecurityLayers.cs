using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Security configuration captured at connection time and persisted with an operation.</summary>
	[Serializable]
	public sealed class DefenseState
	{
		public string id, sourceNodeId;
		public DefenseKind kind;
		public string[] targetNodeIds = Array.Empty<string>();
		public float intervalSeconds, traceBurst, remaining;
		public bool keyAcquired, disabled;
		public List<string> spoofedTargets = new();

		public DefenseState() { }

		public DefenseState(ContractDefenseDefinition source)
		{
			id = source.id;
			sourceNodeId = source.sourceNodeId;
			kind = source.kind;
			targetNodeIds = source.targetNodeIds.ToArray();
			intervalSeconds = source.intervalSeconds;
			traceBurst = source.traceBurst;
			remaining = intervalSeconds;
		}
	}

	/// <summary>Target types used by security countermeasures.</summary>
	public enum DefenseOperation { KeyServer, LockedTarget, SweepController }

	/// <summary>Checks whether a target belongs to an unresolved contract defense.</summary>
	[Serializable]
	public sealed class DefenseCondition : Condition
	{
		public DefenseOperation operation;

		protected override bool Check(RuleContext c)
		{
			return c.Simulation?.HasDefenseTarget(operation, c.Node) == true;
		}
	}

	/// <summary>Changes a credential or sweep after a countermeasure action completes.</summary>
	[Serializable]
	public sealed class DefenseEffect : Effect
	{
		public DefenseOperation operation;

		public override void Apply(RuleContext c)
		{
			c.Simulation?.ApplyDefense(operation, c.Node);
		}
	}

	public sealed partial class GameSimulation
	{
		/// <summary>Active security layers in this operation.</summary>
		public IReadOnlyList<DefenseState> Defenses => Op?.defenses ?? (IReadOnlyList<DefenseState>)Array.Empty<DefenseState>();

		/// <summary>Whether a download target still requires a credential.</summary>
		public bool IsCredentialLocked(string targetId)
		{
			return Active && Op.defenses.Any(d => d.kind == DefenseKind.CredentialLock &&
				!d.keyAcquired && d.targetNodeIds.Contains(targetId) && !d.spoofedTargets.Contains(targetId));
		}

		private void LabelDefenseSources()
		{
			foreach (var group in Defenses.GroupBy(d => d.sourceNodeId))
			{
				var source = Node(group.Key);
				var roles = group.Select(d => d.kind == DefenseKind.CredentialLock ? "Key server" : d.kind == DefenseKind.Watcher ? "Link watcher" : d.kind == DefenseKind.PurgeTimer ? "Purge controller" : "Sweep controller").Distinct();
				source.label = string.Join(" + ", roles) + " / " + source.label;
				source.detail += "\nContract security service. Inspect its countermeasure actions after discovery.";
			}
		}

		/// <summary>Credential source hint that respects the operation's discovered nodes.</summary>
		public string CredentialHint(string targetId)
		{
			if (!IsCredentialLocked(targetId)) return "";
			var sources = Defenses.Where(d => d.kind == DefenseKind.CredentialLock && d.targetNodeIds.Contains(targetId) &&
				!d.keyAcquired && !d.spoofedTargets.Contains(targetId)).Select(d => Node(d.sourceNodeId)).ToArray();
			return "CREDENTIAL LOCK / " + string.Join(" / ", sources.Select(n => n.known ? n.label : "Key server undiscovered")) +
				". Extract its key or spoof this gate before downloading.";
		}

		/// <summary>Public description of a contract's security types without disclosing its topology.</summary>
		public string DefenseBrief(Contract job)
		{
			var definition = Catalog.Contract(job?.id);
			if (definition == null) return "";
			var layers = definition.defenses.Where(d => d.minimumDifficulty <= job.difficulty).ToArray();
			if (layers.Length == 0) return "No special security layers reported.";
			return string.Join("  ", layers.Select(d => d.kind == DefenseKind.CredentialLock
				? "CREDENTIAL LOCK / Extract Key (core) or Credential Fork"
				: d.kind == DefenseKind.Watcher ? "WATCHER / Monitored ingress adds " + d.traceBurst + " trace. Use maintenance links, suppress, or capture its controller."
                : d.kind == DefenseKind.PurgeTimer ? "PURGE / First linked recovery arms a " + d.intervalSeconds + "s window. Prepare both targets, suppress, or capture its controller."
                : "SECURITY SWEEP / False Echo, Controller Capture, Dark Interval, or controller capture"));
		}

		/// <summary>Checks target eligibility for an authored security countermeasure.</summary>
		public bool CanUseDefense(DefenseOperation operation, NetNode target)
		{
			if (!HasDefenseTarget(operation, target)) return false;
			switch (operation)
			{
				case DefenseOperation.KeyServer:
					return target.controlled;
				case DefenseOperation.LockedTarget:
					return target.controlled;
				case DefenseOperation.SweepController:
					return target.known && ControlledDistance(target) <= 3;
				default: return false;
			}
		}

		/// <summary>Whether a node represents the requested unresolved defense target.</summary>
		public bool HasDefenseTarget(DefenseOperation operation, NetNode target)
		{
			if (!Active || target == null) return false;
			return operation switch
			{
				DefenseOperation.KeyServer => Op.defenses.Any(d => d.kind == DefenseKind.CredentialLock &&
					!d.keyAcquired && d.sourceNodeId == target.id),
				DefenseOperation.LockedTarget => IsCredentialLocked(target.id),
				DefenseOperation.SweepController => Op.defenses.Any(d => d.kind == DefenseKind.SecuritySweep &&
					!d.disabled && d.sourceNodeId == target.id && !target.controlled),
				_ => false
			};
		}

		internal void ApplyDefense(DefenseOperation operation, NetNode target)
		{
			if (!CanUseDefense(operation, target)) return;
			foreach (var d in Op.defenses)
			{
				if (operation == DefenseOperation.KeyServer && d.kind == DefenseKind.CredentialLock &&
					d.sourceNodeId == target.id)
					d.keyAcquired = true;
				else if (operation == DefenseOperation.LockedTarget && d.kind == DefenseKind.CredentialLock &&
					d.targetNodeIds.Contains(target.id) && !d.spoofedTargets.Contains(target.id))
					d.spoofedTargets.Add(target.id);
				else if (operation == DefenseOperation.SweepController && d.kind == DefenseKind.SecuritySweep &&
					d.sourceNodeId == target.id)
					d.disabled = true;
			}
			Log("SECURITY COUNTERED // " + target.label);
		}

		/// <summary>Seconds until the next active sweep, or null when no sweep can fire.</summary>
		public float? NextSweepSeconds => Defenses.Where(SweepActive).Select(d => (float?)d.remaining)
			.DefaultIfEmpty(null).Min();

		/// <summary>Live sweep readout without revealing undiscovered controller locations.</summary>
		public string SweepStatus
		{
			get
			{
				var sweeps = Defenses.Where(d => d.kind == DefenseKind.SecuritySweep).ToArray();
				if (sweeps.Length == 0) return "";
				if (sweeps.All(d => d.disabled || Node(d.sourceNodeId)?.controlled == true))
					return "SECURITY SWEEPS DISABLED";
				if (IsAbilityActive("loop-feed")) return "SECURITY SWEEPS DIVERTED / RAM RESERVED";
				return NextSweepSeconds.HasValue
					? "NEXT SECURITY SWEEP / " + Mathf.CeilToInt(NextSweepSeconds.Value) + "s"
					: "SECURITY SWEEP PAUSED / CONTROLLER SUPPRESSED";
			}
		}

		private bool SweepActive(DefenseState d)
		{
			var controller = Node(d.sourceNodeId);
			return d.kind == DefenseKind.SecuritySweep && !d.disabled && controller != null &&
				!controller.controlled && controller.suppressed <= 0 && !IsAbilityActive("loop-feed");
		}

		private float SweepTrace(DefenseState d)
		{
			return Mathf.Max(0, ApplyModifiers("sweepTrace", d.traceBurst, Context()));
		}

		private void AdvanceSweeps(float delta)
		{
			foreach (var d in Defenses.Where(SweepActive))
			{
				d.remaining -= delta;
				while (d.remaining <= 0)
				{
					Op.trace += SweepTrace(d);
					Log("SECURITY SWEEP // +" + SweepTrace(d).ToString("0.#") + " trace.");
					d.remaining += d.intervalSeconds;
				}
			}
		}

		private float ForecastSweeps(float duration)
		{
			if (IsAbilityActive("loop-feed")) return 0;
			float trace = 0;
			foreach (var d in Defenses.Where(d => d.kind == DefenseKind.SecuritySweep && !d.disabled &&
				Node(d.sourceNodeId)?.controlled == false))
			{
				var suppression = Op.statuses.Where(s => s.target == d.sourceNodeId && s.disablesDefense).ToArray();
				if (suppression.Any(s => !string.IsNullOrEmpty(s.memoryKey))) continue;
				var firstBurst = d.remaining + suppression.Select(s => s.remaining).DefaultIfEmpty(0).Max();
				if (duration >= firstBurst)
					trace += (1 + Mathf.FloorToInt((duration - firstBurst) / d.intervalSeconds)) * SweepTrace(d);
			}
			return trace;
		}
	}
}
