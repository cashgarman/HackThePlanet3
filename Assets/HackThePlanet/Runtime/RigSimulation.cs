using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Installed or inventory copy of a rack module with placement and purchase state.</summary>
	[Serializable]
	public sealed class RigPart
	{
		/// <summary>Unique instance id for this owned copy.</summary>
		public string instanceId, modelId;

		/// <summary>Credits paid when bought; used for resale.</summary>
		public int paidPrice, row = -1, column = -1;

		/// <summary>When true, width and height are swapped on the chassis grid.</summary>
		public bool rotated;

		/// <summary>Gets whether this part occupies a cell on the active chassis.</summary>
		public bool Installed => row >= 0 && column >= 0;
	}

	/// <summary>RAM held by a persistent operator or node ability until released.</summary>
	[Serializable]
	public sealed class MemoryReservation
	{
		/// <summary>Action definition id for the reserving ability.</summary>
		public string actionId, target;

		/// <summary>Reserved RAM in gigabytes.</summary>
		public float amount;
	}

	/// <summary>Severity of a rig validation finding.</summary>
	public enum RigIssueSeverity
	{
		/// <summary>Blocks starting a contract when <see cref="GameCatalog.Data" /> requires a valid build.</summary>
		Error,

		/// <summary>Advisory; weaker builds may still connect when validation allows warnings.</summary>
		Warning
	}

	/// <summary>Single rig build validation message with severity.</summary>
	public sealed class RigIssue
	{
		/// <summary>Player-facing explanation of the problem.</summary>
		public readonly string message;

		/// <summary>Whether the issue blocks connection or is advisory only.</summary>
		public readonly RigIssueSeverity severity;

		/// <summary>Creates a validation issue.</summary>
		/// <param name="severity">Error blocks connection when required; warning is advisory.</param>
		/// <param name="message">Explanation shown in the rig UI.</param>
		public RigIssue(RigIssueSeverity severity, string message)
		{
			// Store the severity and player-facing message.
			this.severity = severity;
			this.message = message;
		}
	}

	/// <summary>Forecast of XP, credits, and module sales when refunding all research.</summary>
	public sealed class ResearchRefundPreview
	{
		/// <summary>Count of module copies sold during the refund preview.</summary>
		public int copies;

		/// <summary>Credits from selling module copies that fail requirements after refund.</summary>
		public int credits;

		/// <summary>Summary text for the refund confirmation modal.</summary>
		public string description;

		/// <summary>Instance ids of modules sold because requirements would no longer be met.</summary>
		public string[] soldInstances;

		/// <summary>XP returned from spent research.</summary>
		public int xp;
	}

	public sealed partial class GameSimulation
	{
		/// <summary>Module id of the uplink required to connect to contracts.</summary>
		public const string UplinkModelId = "uplink";

		/// <summary>Module family id for storage modules.</summary>
		public const string StorageFamily = "storage";

		/// <summary>Module family id for processing modules.</summary>
		public const string ProcessingFamily = "processing";

		// Prevents re-entrant memory reservation while an ability cost is being applied.
		private string applyingMemoryKey;

		/// <summary>Gets the career's installed chassis definition.</summary>
		public RigChassisDefinition Chassis => Catalog.Data.chassis.Single(c => c.id == Career.chassisId);

		/// <summary>Gets all module definitions from the catalog.</summary>
		public IReadOnlyList<RigModuleDefinition> Modules => Catalog.Data.modules;

		/// <summary>Gets rack parts currently placed on the chassis grid.</summary>
		public IEnumerable<RigPart> InstalledParts => Career.rigParts.Where(p => p.Installed);

		/// <summary>Gets the next chassis tier upgrade, if any.</summary>
		public RigChassisDefinition NextChassis =>
			Catalog.Data.chassis.OrderBy(c => c.tier).FirstOrDefault(c => c.tier > Chassis.tier);

		/// <summary>Gets total power budget from chassis plus installed supply modules.</summary>
		public float PowerLimit => Chassis.powerCapacity + InstalledParts.Sum(p => Module(p.modelId).powerCapacity);

		/// <summary>Gets total cooling budget from chassis plus installed cooling modules.</summary>
		public float CoolingLimit =>
			Chassis.coolingCapacity + InstalledParts.Sum(p => Module(p.modelId).coolingCapacity);

		/// <summary>Gets aggregate power demand of installed modules.</summary>
		public float PowerUsed => InstalledParts.Sum(p => Module(p.modelId).powerDemand);

		/// <summary>Gets aggregate heat output of installed modules.</summary>
		public float HeatUsed => InstalledParts.Sum(p => Module(p.modelId).heatOutput);

		/// <summary>Gets file storage capacity in gigabytes from rig stats.</summary>
		public float StorageCapacity => Mathf.Max(0, Stat("storageCapacity"));

		/// <summary>Gets storage consumed by collected files during an active operation.</summary>
		public float StorageUsed => Op?.nodes.Where(n => n.collected).Sum(n => n.storage) ?? 0;

		/// <summary>Gets remaining file storage during an active operation.</summary>
		public float FreeStorage => Mathf.Max(0, StorageCapacity - StorageUsed);

		/// <summary>Gets total operator RAM pool in gigabytes from rig stats.</summary>
		public float MemoryPool => Mathf.Max(0, Stat("memoryPool"));

		/// <summary>Gets RAM reserved by persistent abilities during an active operation.</summary>
		public float MemoryReserved => Op?.reservations.Sum(r => r.amount) ?? 0;

		/// <summary>Gets RAM still returning from one-shot ability costs.</summary>
		public float MemoryReclaiming => Op?.memoryReclaiming ?? 0;

		/// <summary>Gets RAM available to start new abilities.</summary>
		public float MemoryFree => Mathf.Max(0, MemoryPool - MemoryReserved - MemoryReclaiming);

		/// <summary>Gets RAM reclaim rate in gigabytes per second.</summary>
		public float MemoryReclaim => Mathf.Max(0, Stat("memoryReclaim"));

		/// <summary>Gets effective compute from rig stats.</summary>
		public float Compute => Mathf.Max(0, Stat("compute"));

		/// <summary>Gets effective bandwidth from rig stats.</summary>
		public float Bandwidth => Mathf.Max(0, Stat("bandwidth"));

		/// <summary>Gets whether the current build passes validation for connecting.</summary>
		public bool RigValid => !Catalog.Data.rig.requireValidBuild ||
								RigIssues.All(i => i.severity != RigIssueSeverity.Error);

		/// <summary>Gets error-level validation messages that block connecting when a valid build is required.</summary>
		public IReadOnlyList<string> RigProblems => RigIssues.Where(i => i.severity == RigIssueSeverity.Error)
															 .Select(i => i.message).ToArray();

		/// <summary>Gets deduplicated rig validation issues for the current chassis layout.</summary>
		public IReadOnlyList<RigIssue> RigIssues
		{
			get
			{
				// Start with empty issue and occupancy collections.
				var issues = new List<RigIssue>();
				var occupied = new HashSet<(int, int)>();

				void Error(string message)
				{
					issues.Add(new RigIssue(RigIssueSeverity.Error, message));
				}

				void Warning(string message)
				{
					issues.Add(new RigIssue(RigIssueSeverity.Warning, message));
				}

				// Check each installed part for rotation, chassis bounds, and overlaps.
				foreach (var part in InstalledParts)
				{
					// Look up the module and its footprint.
					var model = Module(part.modelId);
					var size = PartSize(part);

					// Reject rotation when the module or rig forbids it.
					if (part.rotated && (!model.rotatable || !Catalog.Data.rig.allowRotation))
						Error(model.title + ": rotation is unavailable.");

					// Check every cell the part covers.
					for (var y = part.row; y < part.row + size.y; y++)
						for (var x = part.column; x < part.column + size.x; x++)
						{
							// Flag cells outside the chassis.
							if (x < 0 || y < 0 || x >= Chassis.columns || y >= Chassis.rows)
								Error(model.title + ": outside the chassis.");

							// Flag cells already claimed by another part.
							if (!occupied.Add((y, x)))
								Error(model.title + ": overlaps another part.");
						}
				}

				// Flag power demand above the supply limit.
				if (PowerUsed > PowerLimit)
					Error("Power demand exceeds limit by " + (PowerUsed - PowerLimit).ToString("0.#") +
						  ". Install a power supply or remove a part.");

				// Flag heat above the cooling limit.
				if (HeatUsed > CoolingLimit)
					Error("Heat exceeds cooling by " + (HeatUsed - CoolingLimit).ToString("0.#") +
						  ". Install cooling or remove a part.");

				// Require an uplink to connect.
				if (!InstalledParts.Any(p => p.modelId == UplinkModelId))
					Error("No uplink installed. Install an Uplink to connect.");

				// Warn when no storage module is installed.
				if (!InstalledParts.Any(p => Module(p.modelId).family == StorageFamily))
					Warning("No storage module installed. Only chassis storage (" + StorageCapacity.ToString("0") +
							" GB) is available. Add a Drive or Storage Array for more.");

				// Warn when no processing module is installed.
				if (!InstalledParts.Any(p => Module(p.modelId).family == ProcessingFamily))
					Warning("No processing module installed. Compute stays at base.");

				// Return one entry per distinct severity and message.
				return issues.GroupBy(i => i.severity + "|" + i.message).Select(g => g.First()).ToArray();
			}
		}

		/// <summary>Looks up a module definition by id.</summary>
		/// <param name="id">Module definition id.</param>
		/// <returns>The definition, or null when unknown.</returns>
		public RigModuleDefinition Module(string id)
		{
			return Catalog.Data.modules.FirstOrDefault(m => m.id == id);
		}

		/// <summary>Finds an owned rack part by instance id.</summary>
		/// <param name="id">Unique instance id.</param>
		/// <returns>The part, or null when not owned.</returns>
		public RigPart Part(string id)
		{
			return Career.rigParts.FirstOrDefault(p => p.instanceId == id);
		}

		// Seeds the career rig with the catalog starting chassis and placed parts when empty.
		private void InitializeRig()
		{
			// Make sure the rig inventory list exists.
			Career.rigParts ??= new List<RigPart>();

			// Skip setup when a chassis is already installed.
			if (!string.IsNullOrEmpty(Career.chassisId))
				return;

			// Install the starting chassis.
			Career.chassisId = Catalog.Data.rig.startingChassis.id;

			// Add each starting part as an owned, placed copy.
			foreach (var start in Catalog.Data.rig.startingParts)
				Career.rigParts.Add(new RigPart
				{
					instanceId = Guid.NewGuid().ToString("N"), modelId = start.module.id, row = start.row,
					column = start.column, rotated = start.rotated
				});
		}

		// Returns rig-specific base stat values before modifiers are applied.
		internal float RigBaseStat(string stat)
		{
			// Use rig-specific base values; defer other stats to the catalog.
			switch (stat)
			{
				case "storageCapacity": return Chassis.storageCapacity;
				case "memoryPool":      return Chassis.memoryPool;
				case "memoryReclaim":   return Catalog.Data.rig.memoryReclaim;
				default:                return Catalog.BaseStat(stat);
			}
		}

		/// <summary>Gets the grid footprint of an owned part, accounting for rotation.</summary>
		/// <param name="part">Installed or inventory part.</param>
		/// <returns>Width and height in chassis cells.</returns>
		public Vector2Int PartSize(RigPart part)
		{
			return PartSize(Module(part.modelId), part.rotated);
		}

		/// <summary>Gets the grid footprint for a module model and rotation.</summary>
		/// <param name="model">Module definition.</param>
		/// <param name="rotated">When true, swaps width and height.</param>
		/// <returns>Width and height in chassis cells.</returns>
		public static Vector2Int PartSize(RigModuleDefinition model, bool rotated)
		{
			return rotated ? new Vector2Int(model.height, model.width) : new Vector2Int(model.width, model.height);
		}

		/// <summary>Finds the installed part occupying a chassis cell.</summary>
		/// <param name="row">Zero-based row.</param>
		/// <param name="column">Zero-based column.</param>
		/// <returns>The part covering the cell, or null when empty.</returns>
		public RigPart PartAt(int row, int column)
		{
			return InstalledParts.FirstOrDefault(p => row >= p.row && column >= p.column &&
													  row < p.row + PartSize(p).y && column < p.column + PartSize(p).x);
		}

		/// <summary>Checks whether a part can occupy the given cells without overlap or out-of-bounds placement.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <param name="row">Anchor row.</param>
		/// <param name="column">Anchor column.</param>
		/// <param name="rotated">Desired rotation.</param>
		/// <returns>True when placement is allowed between contracts.</returns>
		public bool CanPlace(string instance, int row, int column, bool rotated)
		{
			// Look up the part being placed.
			var part = Part(instance);

			// Refuse placement during an operation or for an unknown part.
			if (Active || part == null)
				return false;

			// Measure the footprint at the requested rotation.
			var model = Module(part.modelId);
			var size = PartSize(model, rotated);

			// Check rotation permission, chassis bounds, and overlap with other parts.
			return (!rotated || (model.rotatable && Catalog.Data.rig.allowRotation)) && row >= 0 && column >= 0 &&
				   row + size.y <= Chassis.rows && column + size.x <= Chassis.columns
				   && !InstalledParts.Any(p => p != part && row < p.row + PartSize(p).y && row + size.y > p.row &&
											   column < p.column + PartSize(p).x && column + size.x > p.column);
		}

		/// <summary>Places a module on the chassis when <see cref="CanPlace" /> succeeds; allowed between contracts only.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <param name="row">Anchor row.</param>
		/// <param name="column">Anchor column.</param>
		/// <param name="rotated">When true, swaps footprint dimensions when rotation is allowed.</param>
		/// <returns>True when the part was placed.</returns>
		public bool TryPlaceModule(string instance, int row, int column, bool rotated = false)
		{
			// Reject the move when the footprint does not fit.
			if (!CanPlace(instance, row, column, rotated))
				return false;

			// Record the new anchor and rotation on the part.
			var part = Part(instance);
			part.row = row;
			part.column = column;
			part.rotated = rotated;

			// Bump the revision and notify listeners of the change.
			Touch();

			return true;
		}

		/// <summary>Scans the chassis for the first valid anchor and places the part there.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <param name="rotated">Desired rotation.</param>
		/// <returns>True when a slot was found and used.</returns>
		public bool PlaceFirstFree(string instance, bool rotated = false)
		{
			// Try every anchor cell in row-major order.
			for (var row = 0; row < Chassis.rows; row++)
				for (var column = 0; column < Chassis.columns; column++)
					if (TryPlaceModule(instance, row, column, rotated))
						return true;

			// No anchor fit the footprint.
			return false;
		}

		/// <summary>Moves an installed part back to inventory without selling it.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <returns>True when the part was uninstalled between contracts.</returns>
		public bool RemoveModule(string instance)
		{
			// Look up the part being removed.
			var part = Part(instance);

			// Refuse removal during an operation or for an unknown part.
			if (Active || part == null)
				return false;

			// Clear the grid anchor so the part returns to inventory.
			part.row = part.column = -1;

			// Bump the revision and notify listeners of the change.
			Touch();

			return true;
		}

		/// <summary>Toggles rotation in inventory or re-validates rotation for an installed part.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <returns>True when rotation changed or the installed footprint still fits.</returns>
		public bool RotateModule(string instance)
		{
			// Look up the part being rotated.
			var part = Part(instance);

			// Refuse rotation during an operation or for an unknown part.
			if (Active || part == null)
				return false;

			// Installed parts re-validate the footprint at the rotated angle.
			if (part.Installed)
				return TryPlaceModule(instance, part.row, part.column, !part.rotated);

			// Inventory parts can rotate only when the module and rig allow it.
			if (!Module(part.modelId).rotatable || !Catalog.Data.rig.allowRotation)
				return false;

			// Toggle the rotation flag.
			part.rotated = !part.rotated;

			// Bump the revision and notify listeners of the change.
			Touch();

			return true;
		}

		/// <summary>Evaluates whether a module copy can be purchased and stored in inventory.</summary>
		/// <param name="modelId">Module definition id.</param>
		/// <returns>Preview with <see cref="ToolPreview.allowed" /> and player-facing <see cref="ToolPreview.reason" />.</returns>
		public ToolPreview PreviewModule(string modelId)
		{
			// Look up the module and start an empty preview.
			var model = Module(modelId);
			var preview = new ToolPreview();

			// Reject unknown module ids.
			if (model == null)
			{
				preview.reason = "Unknown module model.";

				return preview;
			}

			// Purchases are only allowed between contracts.
			if (Active)
			{
				preview.reason = "Change the rig between contracts.";

				return preview;
			}

			// Check each module requirement against the current career.
			foreach (var requirement in model.requirements)
				if (!requirement.condition.Test(Context()))
				{
					preview.reason = Catalog.Format(requirement.message, Context());

					return preview;
				}

			// Check the price against available credits.
			if (Career.credits < model.price)
			{
				preview.reason = "Need " + (model.price - Career.credits) + " more credits.";

				return preview;
			}

			// Mark the module as buyable.
			preview.allowed = true;
			preview.reason = "Ready to buy. Stored in inventory; placement is free.";

			return preview;
		}

		// Checks whether every module purchase requirement passes for the current career.
		private bool ModuleRequirementsMet(RigModuleDefinition model)
		{
			return model.requirements.All(r => r.condition.Test(Context()));
		}

		/// <summary>Purchases a module copy when <see cref="PreviewModule" /> allows it; placement is separate.</summary>
		/// <param name="modelId">Module definition id.</param>
		/// <returns>The new inventory part, or null when purchase failed.</returns>
		public RigPart BuyModule(string modelId)
		{
			// Refuse the purchase when the preview disallows it.
			if (!PreviewModule(modelId).allowed)
				return null;

			// Look up the module and charge its price.
			var model = Module(modelId);
			Career.credits -= model.price;

			// Create a new owned copy and add it to inventory.
			var part = new RigPart
				{ instanceId = Guid.NewGuid().ToString("N"), modelId = model.id, paidPrice = model.price };
			Career.rigParts.Add(part);

			// Bump the revision and notify listeners of the change.
			Touch();

			return part;
		}

		/// <summary>Gets the resale credits for an owned copy using the catalog sale fraction.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <returns>Floor of paid price times sale fraction, or zero when unknown.</returns>
		public int ModuleSalePrice(string instance)
		{
			return Part(instance) is RigPart part
				? Mathf.FloorToInt(part.paidPrice * Catalog.Data.rig.saleFraction)
				: 0;
		}

		/// <summary>Sells an owned module copy between contracts.</summary>
		/// <param name="instance">Owned part instance id.</param>
		/// <returns>True when the part was removed and credits were added.</returns>
		public bool SellModule(string instance)
		{
			// Look up the part being sold.
			var part = Part(instance);

			// Refuse the sale during an operation or for an unknown part.
			if (Active || part == null)
				return false;

			// Credit the sale price and remove the copy from inventory.
			Career.credits += ModuleSalePrice(instance);
			Career.rigParts.Remove(part);

			// Bump the revision and notify listeners of the change.
			Touch();

			return true;
		}

		/// <summary>Evaluates whether the next chassis tier upgrade can be purchased.</summary>
		/// <returns>Preview with eligibility and player-facing reason.</returns>
		public ToolPreview PreviewChassis()
		{
			// Start a preview and look up the next chassis tier.
			var preview = new ToolPreview();
			var next = NextChassis;

			// Check each gate in order: between contracts, tier available, jobs, then credits.
			if (Active)
			{
				preview.reason = "Change the rig between contracts.";
			}
			else if (next == null)
			{
				preview.reason = "Maximum chassis installed.";
			}
			else if (Career.jobs < next.requiredJobs)
			{
				preview.reason = "Requires " + next.requiredJobs + " successful jobs.";
			}
			else if (Career.credits < next.price)
			{
				preview.reason = "Need " + (next.price - Career.credits) + " more credits.";
			}
			else
			{
				preview.allowed = true;
				preview.reason = "Expand the chassis; existing placements are preserved.";
			}

			return preview;
		}

		/// <summary>Expands to the next chassis tier when <see cref="PreviewChassis" /> allows it.</summary>
		/// <returns>True when the chassis was upgraded and placements were preserved.</returns>
		public bool UpgradeChassis()
		{
			// Refuse the upgrade when the preview disallows it.
			if (!PreviewChassis().allowed)
				return false;

			// Charge the tier price and switch to the new chassis.
			var next = NextChassis;
			Career.credits -= next.price;
			Career.chassisId = next.id;

			// Bump the revision and notify listeners of the change.
			Touch();

			return true;
		}

		/// <summary>Counts orthogonally adjacent installed modules matching a branch, family, or module id.</summary>
		/// <param name="instance">Source part instance on the rack.</param>
		/// <param name="familyOrBranch">Empty matches all neighbors; otherwise module id, family, or branch name.</param>
		/// <returns>Neighbor count used by rig gameplay formulas and conditions.</returns>
		public int RigNeighborCount(string instance, string familyOrBranch)
		{
			// Find the source part; unplaced or unknown parts have no neighbors.
			var source = Part(instance);

			// Return zero when the source is missing or not installed.
			if (source == null || !source.Installed)
				return 0;

			// Measure the source footprint.
			var a = PartSize(source);

			// Count installed parts that share an edge with the source footprint.
			return InstalledParts.Count(p =>
			{
				// Skip the source part itself.
				if (p == source)
					return false;

				// Keep only neighbors that match the id, family, or branch filter.
				var model = Module(p.modelId);
				if (!string.IsNullOrEmpty(familyOrBranch) &&
					!string.Equals(model.id, familyOrBranch, StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(model.family, familyOrBranch, StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(model.branch, familyOrBranch, StringComparison.OrdinalIgnoreCase))
					return false;

				// Measure the neighbor footprint.
				var b = PartSize(p);

				// Test for a shared vertical or horizontal edge.
				return ((source.column + a.x == p.column || p.column + b.x == source.column) &&
						source.row < p.row + b.y && source.row + a.y > p.row)
					   || ((source.row + a.y == p.row || p.row + b.y == source.row) && source.column < p.column + b.x &&
						   source.column + a.x > p.column);
			});
		}

		/// <summary>Simulates refunding all research without mutating the career.</summary>
		/// <returns>Forecast XP, credits from selling locked modules, and confirmation copy.</returns>
		public ResearchRefundPreview PreviewResearchRefund()
		{
			// Snapshot the research state so the preview can restore it afterward.
			var original = Career.unlocked;
			var originalEquipped = Career.equipped;
			var initial = Catalog.Data.career.initialTechnologies.Select(t => t.id).ToList();

			// Simulate the refund with research reset to the starting technologies.
			try
			{
				Career.unlocked = initial;
				Career.equipped = new List<string>();

				// Find modules that would fail their requirements after the refund.
				var sold = Career.rigParts.Where(p => !ModuleRequirementsMet(Module(p.modelId))).ToArray();

				// Compute the refund totals and the list of sold copies.
				var preview = new ResearchRefundPreview
				{
					xp = Mathf.FloorToInt(Career.researchSpent * RefundFraction),
					credits = sold.Sum(p => Mathf.FloorToInt(p.paidPrice * Catalog.Data.rig.researchSaleFraction)),
					copies = sold.Length, soldInstances = sold.Select(p => p.instanceId).ToArray()
				};

				// Build the confirmation copy shown in the refund modal.
				preview.description = "Refund " + preview.xp + " XP and sell " + preview.copies +
									  " locked module copies for " + preview.credits +
									  " cr. Placements will be removed; check power and cooling before connecting.";

				return preview;
			}
			finally
			{
				// Restore the original research state.
				Career.unlocked = original;
				Career.equipped = originalEquipped;
			}
		}

		// Builds the reservation key that pairs an action id with its target node.
		private static string MemoryKey(string action, string target)
		{
			return action + ":" + (target ?? "");
		}

		/// <summary>Checks whether a persistent ability currently holds a RAM reservation.</summary>
		/// <param name="action">Action definition id.</param>
		/// <param name="target">Node id for node-targeted abilities; null for operator abilities.</param>
		/// <returns>True during an active operation when a matching reservation exists.</returns>
		public bool IsAbilityActive(string action, string target = null)
		{
			return Op?.reservations.Any(r => MemoryKey(r.actionId, r.target) == MemoryKey(action, target)) == true;
		}

		// Records one-shot reclaim or persistent RAM reservation for a started ability.
		private void SpendMemory(ActionDefinition action, string target)
		{
			// One-shot costs return to the pool over time.
			if (action.memoryMode == MemoryMode.OneShot)
				Op.memoryReclaiming += action.memoryCost;

			// Persistent costs reserve RAM until the ability is released.
			if (action.memoryMode == MemoryMode.Persistent)
				Op.reservations.Add(new MemoryReservation
				{
					actionId = action.id, target = action.operatorTarget ? null : target, amount = action.memoryCost
				});
		}

		// Stops the ability task, frees its RAM reservation, and clears linked statuses.
		private void ReleaseAbility(string action, string target)
		{
			// Ignore the release when no operation is running.
			if (!Active)
				return;

			// Cancel the running task when it belongs to this ability.
			if (Op.task != null && MemoryKey(Op.task.actionId, Op.task.target) == MemoryKey(action, target))
			{
				// Clear the task and its queued follow-ups.
				Op.task = null;
				Op.queue.Clear();
			}

			// Drop the RAM reservation and any statuses tied to this ability.
			Op.reservations.RemoveAll(r => MemoryKey(r.actionId, r.target) == MemoryKey(action, target));
			var key = MemoryKey(action, target);
			Op.statuses.RemoveAll(s => s.memoryKey == key);

			// Refresh derived statuses and log the release.
			SyncStatuses();
			Log("ABILITY DISABLED // " + Catalog.Action(action).title + ". RAM released.");
		}

		// Validates chassis, inventory, and operation RAM state when loading a career save.
		private static void ValidateRigCareer(Career career, GameCatalog catalog, Action<bool, string> require)
		{
			// Check the chassis id against the catalog.
			require(catalog.Data.chassis.Any(c => c.id == career.chassisId), "unknown chassis");

			// Check each owned part's id, module, price, and grid placement.
			require(
				career.rigParts != null &&
				career.rigParts.All(p => p != null && !string.IsNullOrEmpty(p.instanceId) &&
										 catalog.Data.modules.Any(m => m.id == p.modelId) && p.paidPrice >= 0 &&
										 ((p.row == -1 && p.column == -1) || (p.row >= 0 && p.column >= 0))) &&
				career.rigParts.Select(p => p.instanceId).Distinct().Count() == career.rigParts.Count,
				"invalid rig inventory");

			// Read the active operation, if any.
			var op = career.operation;

			// Skip the operation checks when no operation is running.
			if (op == null)
				return;

			// Check RAM reservations and the reclaim total.
			require(
				Finite(op.memoryReclaiming) && op.memoryReclaiming >= 0 && op.reservations != null &&
				op.reservations.All(r => catalog.Action(r.actionId)?.memoryMode == MemoryMode.Persistent &&
										 Finite(r.amount) && r.amount >= 0 &&
										 (catalog.Action(r.actionId).operatorTarget
											 ? string.IsNullOrEmpty(r.target)
											 : op.nodes.Any(n => n.id == r.target))) &&
				op.reservations.Select(r => MemoryKey(r.actionId, r.target)).Distinct().Count() ==
				op.reservations.Count, "invalid RAM state");

			// Check file storage sizes on each node.
			require(op.nodes.All(n => Finite(n.storage) && n.storage >= 0), "invalid file storage");
		}
	}
}