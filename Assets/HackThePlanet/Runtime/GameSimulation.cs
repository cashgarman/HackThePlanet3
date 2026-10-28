using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Installed tier for a hardware definition in the career save.</summary>
	[Serializable]
	public sealed class HardwareLevel
	{
		/// <summary>Hardware definition id from the catalog.</summary>
		public string id;

		/// <summary>Zero-based tier index installed.</summary>
		public int level;
	}

	/// <summary>Named integer counter stored on an active operation.</summary>
	[Serializable]
	public sealed class CounterState
	{
		/// <summary>Counter key used by gameplay rules and actions.</summary>
		public string key;

		/// <summary>Current counter value.</summary>
		public int value;
	}

	/// <summary>Timed status applied to the operator or a network node during an operation.</summary>
	[Serializable]
	public sealed class StatusState
	{
		/// <summary>Status definition id.</summary>
		public string id, target, memoryKey;

		/// <summary>Remaining duration in simulation seconds.</summary>
		public float remaining;

		/// <summary>Trace and defense flags copied from the status definition.</summary>
		public bool preventsTrace, disablesDefense;
	}

	/// <summary>Persistent career progression, economy, rig inventory, and optional active operation.</summary>
	[Serializable]
	public sealed class Career
	{
		/// <summary>Save format version; v4 is the current schema.</summary>
		public int version = 4, credits = -1, xp, day = 1, totalEarnedXp, jobs;

		/// <summary>When true, default economy and research have been applied once.</summary>
		public bool initialized;

		/// <summary>Installed chassis definition id.</summary>
		public string chassisId;

		/// <summary>Owned rack module copies and placements.</summary>
		public List<RigPart> rigParts = new();

		/// <summary>Total XP spent on research purchases for refund calculations.</summary>
		public int researchSpent = -1;

		/// <summary>Legacy extension hardware tiers (catalog hardware definitions).</summary>
		public List<HardwareLevel> hardware = new();

		/// <summary>Completed contracts, unlocked and equipped tech, and world feed entries.</summary>
		public List<string> completed = new(), unlocked = new(), equipped = new(), news = new();

		/// <summary>Technologies visible on the research graph; persists across refunds.</summary>
		public List<string> researchRevealed = new();
        public List<string> storyFlags = new(), resolvedChoices = new();
        public List<ContractOffer> offers = new();
        public List<ResearchInvestment> investments = new();

		/// <summary>Archived operation reports retained up to the career report limit.</summary>
		public List<OperationReport> reports = new();

		/// <summary>Lore fragments recovered from completed contracts.</summary>
		public List<LoreFragment> lore = new();

		/// <summary>Pre-mission waypoint markers keyed by contract id and network seed.</summary>
		public List<NetworkWaypoint> waypoints = new();

		/// <summary>Active operation state, or null between contracts.</summary>
		public Operation operation;

		/// <summary>When true, autosave is blocked after a corrupt save was detected.</summary>
		[NonSerialized] public bool saveBlocked;

		/// <summary>Gets or sets CPU hardware tier via the legacy <c>cpu</c> hardware id.</summary>
		public int cpu
		{
			get => Level("cpu");
			set => SetLevel("cpu", value);
		}

		/// <summary>Gets or sets bandwidth hardware tier via the legacy <c>bandwidth</c> hardware id.</summary>
		public int bandwidth
		{
			get => Level("bandwidth");
			set => SetLevel("bandwidth", value);
		}

		/// <summary>Gets or sets memory hardware tier via the legacy <c>memory</c> hardware id.</summary>
		public int memory
		{
			get => Level("memory");
			set => SetLevel("memory", value);
		}

		/// <summary>Gets the installed tier for a hardware definition id.</summary>
		/// <param name="id">Hardware definition id.</param>
		/// <returns>Tier level, or zero when not installed.</returns>
		public int Level(string id)
		{
			return hardware?.FirstOrDefault(x => x.id == id)?.level ?? 0;
		}

		/// <summary>Sets the installed tier for a hardware definition id.</summary>
		/// <param name="id">Hardware definition id.</param>
		/// <param name="level">Tier to store.</param>
		public void SetLevel(string id, int level)
		{
			// Ensure the hardware list exists before writing.
			hardware ??= new List<HardwareLevel>();

			// Find the existing entry for this hardware id.
			var item = hardware.FirstOrDefault(x => x.id == id);

			// Add a new tier entry or update the existing one.
			if (item == null)
				hardware.Add(new HardwareLevel { id = id, level = level });
			else
				item.level = level;
		}
	}

	/// <summary>Action waiting to run after the current tool finishes.</summary>
	[Serializable]
	public sealed class QueuedTool
	{
		/// <summary>Target node id, or null for operator actions.</summary>
		public string target, actionId;

		/// <summary>Creates an empty queued entry for serialization.</summary>
		public QueuedTool()
		{
		}

		/// <summary>Creates a queue entry from a legacy tool kind.</summary>
		/// <param name="tool">Legacy tool enum mapped to action ids.</param>
		/// <param name="id">Target node id.</param>
		public QueuedTool(ToolKind tool, string id)
		{
			// Map the legacy tool kind onto its action id.
			kind = tool;

			// Store the target node id.
			target = id;
		}

		/// <summary>Creates a queue entry for a catalog action id.</summary>
		/// <param name="action">Action definition id.</param>
		/// <param name="id">Target node id.</param>
		public QueuedTool(string action, string id)
		{
			// Store the catalog action id.
			actionId = action;

			// Store the target node id.
			target = id;
		}

		/// <summary>Gets or sets the queued action via legacy <see cref="ToolKind" /> names.</summary>
		public ToolKind kind
		{
			get => Enum.TryParse<ToolKind>(actionId, true, out var value) ? value : ToolKind.Scan;
			set => actionId = value.ToString().ToLowerInvariant();
		}
	}

	/// <summary>In-progress workstation action with timing and trace accounting.</summary>
	[Serializable]
	public sealed class ToolTask
	{
		/// <summary>Target node id and action definition id.</summary>
		public string target, actionId;

		/// <summary>Total duration, elapsed time, and trace contributed when complete.</summary>
		public float duration, elapsed, trace;

		/// <summary>Gets or sets the running action via legacy <see cref="ToolKind" /> names.</summary>
		public ToolKind kind
		{
			get => Enum.TryParse<ToolKind>(actionId, true, out var value) ? value : ToolKind.Scan;
			set => actionId = value.ToString().ToLowerInvariant();
		}
	}

	/// <summary>Serialized state for a connected contract session.</summary>
	[Serializable]
	public sealed class Operation
	{
		/// <summary>Contract id, gateway entry node, and most recently controlled node.</summary>
		public string contractId, entryNode, lastControlled;

		/// <summary>Network generation seed and generator version.</summary>
		public int seed, generationVersion;

		/// <summary>Generated network nodes for this operation instance.</summary>
		public List<NetNode> nodes;

		/// <summary>Generated network links for this operation instance.</summary>
		public List<NetLink> links;

		/// <summary>Snapshot of security layers; old active saves intentionally have none.</summary>
		public List<DefenseState> defenses = new();

		/// <summary>Timestamped operation log lines shown in the workstation feed.</summary>
		public List<string> log = new();

		/// <summary>Actions waiting to run after the current tool completes.</summary>
		public List<QueuedTool> queue = new();

		/// <summary>Named integer counters used by contract rules and actions.</summary>
		public List<CounterState> counters = new();

		/// <summary>Active timed statuses on the operator or network nodes.</summary>
		public List<StatusState> statuses = new();

		/// <summary>Currently running tool, or null when idle.</summary>
		public ToolTask task;

		/// <summary>Trace percentage, elapsed simulation seconds, and RAM returning to the pool.</summary>
		public float trace, peakTrace, elapsed, memoryReclaiming;

		/// <summary>Persistent ability RAM reservations.</summary>
		public List<MemoryReservation> reservations = new();

		/// <summary>When true, simulation tick is paused.</summary>
		public bool paused;

		/// <summary>Gets the longest remaining trace-prevention status duration.</summary>
		public float decoyRemaining =>
			statuses?.Where(s => s.preventsTrace).Select(s => s.remaining).DefaultIfEmpty(0).Max() ?? 0;
	}

	/// <summary>Archived summary shown after disconnecting from a contract.</summary>
	[Serializable]
	public sealed class OperationReport
	{
		/// <summary>Contract title, company, outcome verdict, and narrative copy.</summary>
		public string title, company, verdict, narrative;

		/// <summary>Whether objectives were delivered successfully.</summary>
		public bool success;

		/// <summary>Final trace percentage for the attempt.</summary>
		public float trace;

		/// <summary>Rewards granted and career day when the report was recorded.</summary>
		public int credits, xp, day, bonusFiles;

		/// <summary>Total optional archives on the contract for bonus display.</summary>
		public int bonusArchivesTotal;

		/// <summary>Quiet bonus threshold copied from the contract.</summary>
		public float quietLimit;

		/// <summary>Whether the quiet trace bonus was earned.</summary>
		public bool quietBonusEarned;

		/// <summary>Operation log copied into the report archive.</summary>
		public List<string> log = new();
	}

	/// <summary>One player-facing requirement row for an action preview.</summary>
	public sealed class ActionRequirementStep
	{
		/// <summary>Gets the requirement copy shown in UI checklists.</summary>
		public string message;

		/// <summary>Gets whether this requirement is satisfied for the current target.</summary>
		public bool met;

		/// <summary>Creates a requirement row.</summary>
		public ActionRequirementStep(string message, bool met)
		{
			this.message = message;
			this.met = met;
		}
	}

	/// <summary>Result of previewing an action, upgrade, or purchase before committing.</summary>
	public sealed class ToolPreview
	{
		/// <summary>Whether the action can run or purchase proceed.</summary>
		public bool allowed, willIsolate, losesQuietBonus;

		/// <summary>Duration, trace cost, and projected trace after completion.</summary>
		public float duration, trace, projectedTrace;

		/// <summary>Player-facing explanation when blocked or describing queue behavior.</summary>
		public string reason;

		/// <summary>Ordered checklist of requirements evaluated for this preview.</summary>
		public List<ActionRequirementStep> steps = new();
	}

	/// <summary>Core simulation for career progression, network operations, research, and rig stats.</summary>
	public sealed partial class GameSimulation
	{
		/// <summary>Credits or XP granted by debug cheat methods.</summary>
		public const int DebugGrantAmount = 1_000_000;

		// Nested effect application depth and execution budget counter.
		private int effectDepth, effectCount;

		// Remembers when the player paused manually so actions do not auto-resume.
		private bool manuallyPaused;

		/// <summary>Creates a simulation with the default catalog and optional career.</summary>
		/// <param name="career">Career to simulate; a new career is created when null.</param>
		public GameSimulation(Career career = null) : this(GameCatalog.Default, career)
		{
		}

		/// <summary>Creates a simulation with the given catalog and optional career.</summary>
		/// <param name="catalog">Gameplay catalog supplying contracts, actions, and stats.</param>
		/// <param name="career">Career to simulate; a new career is created when null.</param>
		public GameSimulation(GameCatalog catalog, Career career = null)
		{
			// Store the catalog, rejecting null.
			Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

			// Use the given career or create a new one from the catalog.
			Career = career ?? catalog.CreateCareer();

			// Normalize and validate the career before use.
			Normalize();
			ValidateCareer(Career, Catalog);

			// Restore the live contract when an operation is already active.
			if (Active)
			{
				// Rebuild the contract description and its seed.
				Job = Catalog.DescribeContract(Catalog.Contract(Op.contractId), Career);
				Job.seed = Op.seed;

				// Pause the restored operation.
				Op.paused = true;

				// Rebuild the statuses and graph for the UI.
				SyncStatuses();
				RefreshGraph();
			}
		}

		/// <summary>Gets the gameplay catalog used for contracts, actions, and stats.</summary>
		public GameCatalog Catalog { get; }

		/// <summary>Gets the mutable career save backing this simulation.</summary>
		public Career Career { get; }

		/// <summary>Gets the active operation, or null between contracts.</summary>
		public Operation Op => Career.operation;

		/// <summary>Gets the live contract description while connected.</summary>
		public Contract Job { get; private set; }

		/// <summary>Gets the graph document of discovered nodes for the UI.</summary>
		public GraphDocument KnownGraph { get; private set; }

		/// <summary>Gets the layout scene for the known operation graph.</summary>
		public GraphScene Scene { get; private set; }

		/// <summary>Gets the most recent operation report after disconnect.</summary>
		public OperationReport LastReport { get; private set; }

		/// <summary>Gets a monotonically increasing revision counter for UI refresh.</summary>
		public int Revision { get; private set; }

		/// <summary>Gets whether a contract operation is in progress.</summary>
		public bool Active => Op != null;

		/// <summary>Gets whether the running tool can be cancelled per catalog rules.</summary>
		public bool CanStopTask => Active && Op.task != null && Catalog.Data.career.allowTaskCancellation;

		/// <summary>Gets the count of purchased non-starter technologies.</summary>
		public int ResearchCount =>
			Career.unlocked.Except(Catalog.Data.career.initialTechnologies.Select(t => t.id)).Count();

		/// <summary>Gets the total research nodes excluding starter technologies.</summary>
		public int ResearchTotal => Catalog.Data.technologies.Length - Catalog.Data.career.initialTechnologies.Length;

		/// <summary>Gets the fraction of research XP returned on refund.</summary>
		public float RefundFraction => Catalog.Data.career.refundFraction;

		/// <summary>Gets equipped special-ability slot capacity from the chassis.</summary>
		public int Slots => Chassis.slots;

		/// <summary>Gets maximum queued actions from the queueCapacity stat.</summary>
		public int QueueCapacity => Mathf.Max(0, Mathf.FloorToInt(Stat("queueCapacity")));

		/// <summary>Gets the trace percentage that ends the operation.</summary>
		public float TraceLimit => Catalog.Data.career.traceLimit;

		/// <summary>Gets the trace warning threshold from career settings.</summary>
		public float TraceWarning => Catalog.Data.career.traceWarning;

		/// <summary>Gets the trace danger threshold from career settings.</summary>
		public float TraceDanger => Catalog.Data.career.traceDanger;

		/// <summary>Gets current trace as a fraction of the limit.</summary>
		public float TraceFraction => Active ? Op.trace / TraceLimit : 0;

		/// <summary>Gets allowed simulation speed multipliers.</summary>
		public float[] SimulationSpeeds => Catalog.Data.career.simulationSpeeds.ToArray();

		/// <summary>Gets whether every non-recurring contract has been delivered.</summary>
		public bool StoryComplete =>
			Catalog.Data.contracts.Any(x => x.isEnding && Career.completed.Contains(x.id));

		/// <summary>Gets the career rank title for the current job count.</summary>
		public string Rank =>
			Catalog.Data.career.ranks.Where(x => Career.jobs >= x.jobs).OrderBy(x => x.jobs).LastOrDefault()?.title ??
			string.Empty;

		// Active contract definition resolved from the current job id.
		private ContractDefinition Definition => Catalog.Contract(Job?.id);

		/// <summary>Gets secured objectives on the active contract.</summary>
		public int ObjectiveCount =>
			!Active ? 0 : Definition.objectives.Count(o => o.complete.Test(Context(Node(o.nodeId))));

		/// <summary>Gets total required objectives on the active contract.</summary>
		public int ObjectiveTotal => Active ? Definition.objectives.Length : 0;

		/// <summary>Gets whether all required objectives are secured.</summary>
		public bool ObjectiveComplete => Active && ObjectiveTotal > 0 && ObjectiveCount == ObjectiveTotal && EvidenceSolved;

		/// <summary>Gets normalized progress of the running tool, or zero when idle.</summary>
		public float Progress => Op?.task == null ? 0 : Mathf.Clamp01(Op.task.elapsed / Op.task.duration);

		/// <summary>Gets active scanner nodes contributing trace pressure near the selection.</summary>
		public int Scanners =>
			!Active
				? 0
				: Op.nodes.Count(n => Catalog.NodeType(n.typeId).auras
											 .Any(a => a.modifier.stat == "tracePressure" &&
													   a.source.Test(Context(n))));

		/// <summary>Gets formatted quiet-bonus reward copy for the active contract.</summary>
		public string RewardDescription =>
			Catalog.Format(
				"Below " + Job?.quietLimit +
				"% trace: +{quietCredits} cr / +{quietXp} XP\nOptional archives: +{archiveCredits} cr / +{archiveXp} XP each (XP capped at +{archiveXpCap})",
				Context());

		/// <summary>Gets formatted optional archive payout copy.</summary>
		public string ArchiveDescription =>
			Catalog.Format("+{archiveCredits} cr / +{archiveXp} XP per archive; up to +{archiveXpCap} archive XP.",
						   Context());

		/// <summary>Raised after simulation state changes that should refresh the UI.</summary>
		public event Action Changed;

		/// <summary>Returns whether a technology id is unlocked.</summary>
		/// <param name="id">Technology definition id.</param>
		/// <returns>True when the id is in <see cref="Career.unlocked" />.</returns>
		public bool Has(string id)
		{
			return Career.unlocked.Contains(id);
		}

		/// <summary>Returns whether a technology is unlocked and equipped.</summary>
		/// <param name="id">Technology definition id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Equipped(string id)
		{
			return Has(id) && Career.equipped.Contains(id);
		}

		/// <summary>Finds a network node by id in the active operation.</summary>
		/// <param name="id">Node id.</param>
		/// <returns>The node, or null when not found or inactive.</returns>
		public NetNode Node(string id)
		{
			return Op?.nodes.FirstOrDefault(n => n.id == id);
		}

		/// <summary>Gets graph neighbors of a node.</summary>
		/// <param name="node">Source node.</param>
		public IEnumerable<NetNode> Neighbors(NetNode node)
		{
			return node == null || Op == null
				? Enumerable.Empty<NetNode>()
				: Op.links.Where(l => l.a == node.id || l.b == node.id).Select(l => Node(l.a == node.id ? l.b : l.a));
		}

		/// <summary>Returns whether a known node can be targeted from control.</summary>
		/// <param name="node">Node to test.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Reachable(NetNode node)
		{
			return node != null && node.known && (node.controlled || Neighbors(node).Any(n => n.controlled));
		}

		/// <summary>Gets hop distance from the nearest controlled node.</summary>
		/// <param name="target">Known node to measure.</param>
		public int ControlledDistance(NetNode target)
		{
			// Stop early when the target is missing, unknown, or inactive.
			if (!Active || target == null || !target.known)
				return int.MaxValue;

			// Seed the breadth-first search from the target node.
			var visited = new HashSet<string> { target.id };
			var queue = new Queue<(NetNode node, int distance)>();
			queue.Enqueue((target, 0));

			// Walk outward until a controlled node is reached.
			while (queue.Count > 0)
			{
				// Dequeue the next node and return its distance when it is controlled.
				var item = queue.Dequeue();
				if (item.node.controlled)
					return item.distance;

				// Enqueue unvisited known neighbors one hop further away.
				foreach (var neighbor in Neighbors(item.node))
					if (neighbor.known && visited.Add(neighbor.id))
						queue.Enqueue((neighbor, item.distance + 1));
			}

			return int.MaxValue;
		}

		/// <summary>Returns whether all objectives on a node are complete.</summary>
		/// <param name="node">Node to inspect.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool ObjectiveDone(NetNode node)
		{
			return Active && Definition.objectives.Where(o => o.nodeId == node?.id)
									   .All(o => o.complete.Test(Context(node)));
		}

		// Fills missing career collections and initializes rig state on load.
		private void Normalize()
		{
			// Default missing career collections so the save can be used safely.
			Career.completed ??= new List<string>();
			Career.unlocked ??= new List<string>();
			Career.equipped ??= new List<string>();
			Career.news ??= new List<string>();
			Career.reports ??= new List<OperationReport>();
			Career.lore ??= new List<LoreFragment>();
			Career.hardware ??= new List<HardwareLevel>();
			Career.waypoints ??= new List<NetworkWaypoint>();
			Career.researchRevealed ??= new List<string>();
            Career.storyFlags ??= new List<string>(); Career.resolvedChoices ??= new List<string>();
            Career.offers ??= new List<ContractOffer>(); Career.investments ??= new List<ResearchInvestment>(); RefreshOffers();

			// Apply starter defaults once for a new career.
			if (!Career.initialized)
			{
				// Read the catalog's starter values.
				var defaults = Catalog.Data.career;

				// Fill in missing credits, XP, and day from the defaults.
				if (Career.credits < 0)
					Career.credits = defaults.credits;
				if (Career.xp == 0)
					Career.xp = defaults.xp;
				if (Career.day == 1)
					Career.day = defaults.day;

				// Unlock the starter technologies that are not yet owned.
				foreach (var tech in defaults.initialTechnologies)
					if (!Has(tech.id))
						Career.unlocked.Add(tech.id);

				// Show the welcome message on an empty news feed.
				if (Career.news.Count == 0)
					Career.news.Add(defaults.welcome);

				// Mark the career as initialized so defaults apply only once.
				Career.initialized = true;
			}

			// Prepare the rig and the research reveal state.
			InitializeRig();
			EnsureResearchReveal();

			// Derive the research spent total for older saves.
			if (Career.researchSpent < 0)
			{
				// Collect the purchased, non-starter technologies.
				var purchased = Career.unlocked.Except(Catalog.Data.career.initialTechnologies.Select(t => t.id))
									  .ToArray();

				// Estimate investment from earned XP, falling back to technology costs.
				var earnedInvestment = Career.totalEarnedXp + Catalog.Data.career.xp - Career.xp;
				Career.researchSpent = purchased.Length == 0 ? 0 :
					earnedInvestment > 0 ? earnedInvestment : purchased.Sum(id => Catalog.Technology(id)?.cost ?? 0);
			}

			// Drop an empty operation left behind by an earlier session.
			if (Op != null && string.IsNullOrEmpty(Op.contractId) && (Op.nodes == null || Op.nodes.Count == 0) &&
				(Op.links == null || Op.links.Count == 0) && Op.trace == 0 && Op.elapsed == 0)
				Career.operation = null;

			// Normalize the live operation data when one is active.
			if (Active)
			{
				// Ensure the reservation list exists and clear blank target ids.
				Op.reservations ??= new List<MemoryReservation>();
				foreach (var reservation in Op.reservations)
					if (string.IsNullOrEmpty(reservation.target))
						reservation.target = null;

				// Ensure the remaining operation lists exist.
				Op.queue ??= new List<QueuedTool>();
				Op.log ??= new List<string>();
				Op.counters ??= new List<CounterState>();
				Op.statuses ??= new List<StatusState>();
				Op.defenses ??= new List<DefenseState>();

				// Store empty status target and memory keys as null.
				foreach (var status in Op.statuses)
				{
					if (string.IsNullOrEmpty(status.target))
						status.target = null;
					if (string.IsNullOrEmpty(status.memoryKey))
						status.memoryKey = null;
				}

				// Drop a running task that has no action or duration.
				if (Op.task != null && string.IsNullOrEmpty(Op.task.actionId) && Op.task.duration == 0)
					Op.task = null;
			}
		}

		/// <summary>Validates a v4 career save against the catalog.</summary>
		/// <param name="career">Career to validate.</param>
		/// <param name="catalog">Catalog used for reference checks.</param>
		/// <exception cref="InvalidDataException">The career fails validation.</exception>
		public static void ValidateCareer(Career career, GameCatalog catalog)
		{
			// Throw a save validation error when a check fails.
			void Require(bool pass, string message)
			{
				if (!pass)
					throw new InvalidDataException("Career v4: " + message + ". The save has not been overwritten.");
			}

			// Validate the save version and the economy values.
			Require(career.version == 4, "unsupported save version");

			Require(career.credits >= 0 && career.xp >= 0 && career.day >= 1 && career.researchSpent >= 0,
					"invalid economy values");

            Require(career.storyFlags != null && career.resolvedChoices != null && career.offers != null && career.investments != null, "missing campaign collections");
            Require(career.offers.All(o => o != null && catalog.Contract(o.id)?.optional == true && o.openedDay >= 1) && career.offers.Select(o => o.id).Distinct().Count() == career.offers.Count, "invalid offer history");
            Require(career.investments.All(i => i != null && i.paid >= 0 && career.unlocked.Contains(i.id)), "invalid paid research investment");
			// Validate completed contracts and unlocked technology.
			Require(career.completed.All(id => catalog.Contract(id) != null && !catalog.Contract(id).recurring),
					"unknown completed contract");

			Require(
				career.unlocked.All(id => catalog.Technology(id) != null) &&
				career.unlocked.Distinct().Count() == career.unlocked.Count, "unknown or duplicate technology");

			// Validate equipped technology and installed hardware tiers.
			Require(
				career.equipped.All(id => career.unlocked.Contains(id) && catalog.Technology(id).special) &&
				career.equipped.Distinct().Count() == career.equipped.Count, "invalid equipped technology");

			Require(
				career.hardware.All(h => catalog.Data.hardware.Any(d => d.id == h.id && h.level >= 0 &&
																		h.level <= d.tiers.Length)) &&
				career.hardware.Select(h => h.id).Distinct().Count() == career.hardware.Count,
				"unknown hardware or invalid tier");

			// Validate waypoint entries and their per-contract capacity.
			Require(
				career.waypoints != null &&
				career.waypoints.All(w => w != null && catalog.Contract(w.contractId) != null &&
										  !string.IsNullOrEmpty(w.nodeId) && w.color >= 0 &&
										  w.color < WaypointColors.Length && (w.label?.Length ?? 0) <= 40),
				"invalid waypoint");

			Require(
				career.waypoints.GroupBy(w => (w.contractId, w.seed))
					  .All(g => g.Count() <= 4 && g.Select(w => w.nodeId).Distinct().Count() == g.Count() &&
								g.Count(w => w.showPath) <= 1), "invalid waypoint capacity or route");

			// Validate the installed rig parts.
			ValidateRigCareer(career, catalog, Require);

			// Stop here when no operation is active.
			var op = career.operation;
			if (op == null)
				return;

			// Validate the active contract and its network.
			Require(catalog.Contract(op.contractId) != null, "unknown active contract " + op.contractId);

			Require(op.nodes != null && op.nodes.Count > 0 && op.links != null, "missing active network");

			// Collect node ids and check that every node type exists.
			var ids = new HashSet<string>(op.nodes.Select(n => n.id));
			Require(ids.Count == op.nodes.Count && op.nodes.All(n => catalog.Data.nodeTypes.Any(t => t.id == n.typeId)),
					"unknown or duplicate network node");

			Require(
				ids.Contains(op.entryNode) && ids.Contains(op.lastControlled) &&
				op.links.All(l => ids.Contains(l.a) && ids.Contains(l.b)), "missing network reference");

			Require(catalog.Contract(op.contractId).objectives.All(o => ids.Contains(o.nodeId)),
					"missing objective target");

			// Validate the running task, queued actions, and statuses.
			Require(
				op.task == null || (catalog.Action(op.task.actionId) != null &&
									(catalog.Action(op.task.actionId).operatorTarget || ids.Contains(op.task.target)) &&
									op.task.duration > 0 && Finite(op.task.duration) && Finite(op.task.elapsed) &&
									Finite(op.task.trace) && op.task.trace >= 0 && op.task.elapsed >= 0 &&
									op.task.elapsed <= op.task.duration), "invalid running action");

			Require(
				op.queue.All(q => catalog.Action(q.actionId) != null &&
								  (catalog.Action(q.actionId).operatorTarget || ids.Contains(q.target))),
				"unknown queued action or target");

			Require(
				op.statuses.All(s => catalog.Data.statuses.Any(d => d.id == s.id) &&
									 (string.IsNullOrEmpty(s.target) || ids.Contains(s.target)) && s.remaining > 0 &&
									 Finite(s.remaining)), "unknown or invalid status");

			// Validate the operation clock and trace values.
			Require(op.defenses != null && op.defenses.All(d => d != null && !string.IsNullOrEmpty(d.id) &&
				Enum.IsDefined(typeof(DefenseKind), d.kind) &&
				ids.Contains(d.sourceNodeId) && d.targetNodeIds != null && d.targetNodeIds.All(ids.Contains) &&
				Finite(d.remaining) && d.remaining >= 0 && Finite(d.intervalSeconds) && d.intervalSeconds > 0 &&
				Finite(d.traceBurst) && d.traceBurst >= 0 &&
				d.spoofedTargets != null && d.spoofedTargets.All(d.targetNodeIds.Contains)) &&
				op.defenses.Select(d => d.id).Distinct().Count() == op.defenses.Count, "invalid security layer");
			Require(Finite(op.trace) && op.trace >= 0 && Finite(op.elapsed) && op.elapsed >= 0,
					"invalid operation time or trace");
		}

		// Returns whether a float is neither NaN nor infinity for save validation.
		private static bool Finite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		// Builds a rule context bound to this simulation, job, and optional node or action.
		internal RuleContext Context(NetNode node = null, ActionDefinition action = null)
		{
			return new RuleContext
				{ Catalog = Catalog, Simulation = this, Career = Career, Contract = Job, Node = node, Action = action };
		}

		/// <summary>Starts a contract when prerequisites and rig validation pass.</summary>
		/// <param name="job">Contract to connect.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Start(Contract job)
		{
			// Look up the contract definition.
			var definition = Catalog.Contract(job?.id);

			// Reject the start when busy, unready, unknown, or already completed.
			if (Active || !RigValid || definition == null || !ContractAvailable(definition) ||
				(!definition.recurring && Career.completed.Contains(definition.id)))
				return false;

			// Copy the contract and attach its network and lore.
			Job = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(job));
			Job.networkId = definition.network.id;
			Job.lore = Catalog.DescribeContract(definition, Career).lore;

			// Generate the network for this operation.
			var network = NetworkGenerator.Generate(Job, Career, Catalog);
			manuallyPaused = false;

			// Create the active operation from the generated network.
			Career.operation = new Operation
			{
				contractId = Job.id, seed = Job.seed, nodes = network.nodes, links = network.links,
				generationVersion = NetworkGenerator.Version, entryNode = definition.network.entryNode,
				lastControlled = definition.network.entryNode
			};
			Op.defenses = definition.defenses.Where(d => d.minimumDifficulty <= Job.difficulty)
				.Select(d => new DefenseState(d)).ToList();
			LabelDefenseSources();

			// Mark each objective node as an objective.
			foreach (var goal in definition.objectives)
				Node(goal.nodeId).objective = true;

			// Fire the operation-started triggers.
			Dispatch(RuleEvent.OperationStarted, Context());

			// Announce the tunnel and the client brief in the log.
			Log("TUNNEL OPEN // " + Job.company + ". Gateway secured.");
			Log("CLIENT // " + Job.objective);

			// Refresh the graph and notify the UI.
			RefreshGraph();
			Touch();
			return true;
		}

		/// <summary>Sets operation pause without changing manual pause preference.</summary>
		/// <param name="paused">Desired pause state.</param>
		public void SetPaused(bool paused)
		{
			// Ignore the request when no operation is active.
			if (!Active)
				return;

			// Store the pause state and notify the UI.
			Op.paused = paused;
			Touch();
		}

		/// <summary>Pauses or resumes and remembers manual pause across actions.</summary>
		/// <param name="paused">Desired manual pause state.</param>
		public void SetManualPause(bool paused)
		{
			// Remember the manual pause preference.
			manuallyPaused = paused;

			// Apply the pause now when an operation is running.
			if (Active)
				SetPaused(paused);
		}

		// Unpauses the operation after an action unless the player chose manual pause.
		private void ResumeAfterActionUnlessManuallyPaused()
		{
			// Resume unless the player paused manually.
			if (Active && !manuallyPaused)
				Op.paused = false;
		}

		/// <summary>Gets catalog preferred or preview node for a contract.</summary>
		/// <param name="job">Contract.</param>
		/// <param name="preview">When true, returns preview node instead of preferred.</param>
		public string PreferredNode(Contract job, bool preview = false)
		{
			// Resolve the network definition for the contract.
			var n = Catalog.Network(job.networkId ?? Catalog.Contract(job.id).network.id);

			// Return the preview or preferred entry node.
			return preview ? n.previewNode : n.preferredNode;
		}

		/// <summary>Evaluates a named stat with modifiers.</summary>
		/// <param name="stat">Stat id.</param>
		public float Stat(string stat)
		{
			return Stat(stat, Context());
		}

		// Evaluates a named stat with modifiers for the supplied rule context.
		internal float Stat(string stat, RuleContext c)
		{
			return ApplyModifiers(stat, stat == "security" ? c.Node?.security ?? 0 : RigBaseStat(stat), c);
		}

		// Collects every modifier that applies to a stat from catalog, rig, and operation sources.
		private IEnumerable<(Modifier modifier, RuleContext context)> Modifiers(string stat, RuleContext context)
		{
			// Yield global catalog modifiers for this stat.
			foreach (var m in Catalog.Data.modifiers)
				if (m.stat == stat)
					yield return (m, context);

			// Yield modifiers from unlocked technologies that are active.
			foreach (var t in Catalog.Data.technologies.Where(t => Has(t.id) && (!t.special || Equipped(t.id))))
				foreach (var m in t.modifiers)
					if (m.stat == stat)
						yield return (m, context);

			// Yield modifiers from each installed hardware tier.
			foreach (var h in Catalog.Data.hardware)
				for (var i = 0; i < Career.Level(h.id); i++)
					foreach (var m in h.tiers[i].modifiers)
						if (m.stat == stat)
							yield return (m, context);

			// Yield modifiers from installed rig parts.
			foreach (var part in InstalledParts)
				foreach (var m in Module(part.modelId).modifiers)
					if (m.stat == stat)
						yield return (m, context.ForRig(part.instanceId));

			// Stop here when no operation is active.
			if (!Active)
				yield break;

			// Yield modifiers from persistent ability reservations.
			foreach (var reservation in Op.reservations)
				foreach (var m in Catalog.Action(reservation.actionId).activeModifiers)
					if (m.stat == stat)
						yield return (m, context);

			// Yield aura modifiers from nodes whose rules pass.
			var seen = new HashSet<string>();
			foreach (var source in Op.nodes)
				foreach (var aura in Catalog.NodeType(source.typeId).auras)
				{
					// Skip auras for other stats or sources.
					if (aura.modifier.stat != stat || !aura.source.Test(context.At(source)))
						continue;

					// Skip adjacency-only auras when the target is not adjacent.
					if (aura.adjacentOnly &&
						(context.Node == null || !Neighbors(source).Any(n => n.id == context.Node.id)))
						continue;

					// Skip auras whose condition fails.
					if (!aura.modifier.when.Test(context))
						continue;

					// Skip duplicate auras unless they stack.
					if (!aura.stack && !seen.Add(aura.id))
						continue;

					yield return (aura.modifier, context);
				}
		}

		// Applies ordered modifiers to a base stat value with recursion guarding.
		private float ApplyModifiers(string stat, float basis, RuleContext c)
		{
			// Enter the modifier evaluation scope.
			c.Enter();
			try
			{
				// Apply each matching modifier in priority order.
				foreach (var item in Modifiers(stat, c).OrderBy(m => m.modifier.priority))
				{
					// Read the modifier and apply it when its condition passes.
					var m = item.modifier;
					if (m.when.Test(item.context))
						basis = RuleMath.Apply(m.operation, basis, m.value.Evaluate(item.context));
				}

				// Clamp compute and bandwidth to the rig maximums.
				if (stat == "compute")
					basis = Mathf.Min(basis, Catalog.Data.rig.maximumCompute);
				if (stat == "bandwidth")
					basis = Mathf.Min(basis, Catalog.Data.rig.maximumBandwidth);

				return basis;
			}
			finally
			{
				// Leave the modifier evaluation scope.
				c.Leave();
			}
		}

		/// <summary>Gets rounded security including modifiers and minimum floor.</summary>
		/// <param name="node">Node to evaluate.</param>
		public int EffectiveSecurity(NetNode node)
		{
			return node == null
				? 0
				: Mathf.Max(Mathf.RoundToInt(Stat("minimumSecurity")),
							Mathf.RoundToInt(Stat("security", Context(node))));
		}

		// Returns whether an action is available from defaults or unlocked research grants.
		private bool Granted(ActionDefinition action)
		{
			return action.initiallyAvailable ||
				   Catalog.Data.technologies.Any(t => Has(t.id) && (!t.special || Equipped(t.id)) &&
													  t.grants.Any(a => a.id == action.id));
		}

		/// <summary>Lists actions offered for a target node or operator.</summary>
		/// <param name="targetId">Node id, or null for operator actions.</param>
		public IReadOnlyList<ActionInfo> AvailableActions(string targetId = null)
		{
			// Return nothing when no operation is active.
			if (!Active)
				return Array.Empty<ActionInfo>();

			// Resolve the target node, returning nothing for an unknown target.
			var node = Node(targetId);
			if (targetId != null && node == null)
				return Array.Empty<ActionInfo>();

			// Filter granted, visible actions for the target and sort them by priority.
			return Catalog.Actions
						  .Where(a => a.operatorTarget == (node == null) && Granted(a.Definition) &&
									  (IsAbilityActive(a.id, node?.id) ||
									   a.Definition.visible.Test(Context(node, a.Definition)))).OrderBy(a => a.priority)
						  .ToArray();
		}

		/// <summary>Gets hint text for locked actions that need research.</summary>
		/// <param name="targetId">Selected node id.</param>
		public string ActionResearchHint(string targetId)
		{
			// Return no hint when no operation is active.
			if (!Active)
				return null;

			// Collect the locked actions that are visible for this target.
			var node = Node(targetId);
			var locked = Catalog.Data.actions
								.Where(a => !a.operatorTarget && !Granted(a) && a.visible.Test(Context(node, a)))
								.ToArray();

			// Find the technology that grants one of those actions.
			var tech = Catalog.Data.technologies.FirstOrDefault(t => t.grants.Any(a => locked.Contains(a)));

			// Build the equip or research hint text.
			return tech == null
				? null
				: (Has(tech.id) ? "Equip " : "Research ") + tech.title + " between contracts for another approach.";
		}

		// Merges action requirements with contract rules that gate the same action on a node.
		private IEnumerable<Requirement> Requirements(ActionDefinition action, NetNode node)
		{
			// Yield the action's own requirements.
			foreach (var r in action.requirements)
				yield return r;

			// Yield requirements from contract rules for this action.
			foreach (var rule in Definition.actionRules.Where(r => r.action.id == action.id))
				foreach (var r in rule.requirements)
					yield return r;

			// Yield requirements from objectives on this node.
			foreach (var goal in Definition.objectives.Where(o => o.nodeId == node?.id && o.action?.id == action.id))
				foreach (var r in goal.actionRequirements)
					yield return r;
		}

		/// <summary>Gets UI title for an action, including disable and objective labels.</summary>
		/// <param name="actionId">Action definition id.</param>
		/// <param name="targetId">Target node id.</param>
		public string ActionTitle(string actionId, string targetId = null)
		{
			// Look up the action definition.
			var action = Catalog.Action(actionId);

			// Show the disable label for an active ability.
			if (IsAbilityActive(actionId, targetId))
				return "Disable " + action.title;

			// Find any objective that relabels this action on the target.
			var objective = Active
				? Definition.objectives.FirstOrDefault(o => o.nodeId == targetId && o.action?.id == actionId)
				: null;

			// Prefer the objective label over the action title.
			return string.IsNullOrEmpty(objective?.actionLabel) ? action?.title : objective.actionLabel;
		}

		/// <summary>Previews duration, trace, and eligibility for an action.</summary>
		/// <param name="actionId">Action or legacy tool kind.</param>
		/// <param name="targetId">Target node id.</param>
		/// <returns>Preview with eligibility and player-facing reason.</returns>
		public ToolPreview Preview(string actionId, string targetId = null)
		{
			// Start with an empty preview and resolve the action and target.
			var p = new ToolPreview();
			var a = Catalog.Action(actionId);
			var node = Node(targetId);

			bool Step(string message, bool met)
			{
				p.steps.Add(new ActionRequirementStep(message, met));
				return met;
			}

			// Block the preview when no operation or valid target exists.
			if (!Active || a == null || (!a.operatorTarget && node == null))
			{
				p.reason = "Connect to a contract and select a valid action target.";
				Step(p.reason, false);
				return p;
			}

			// An active ability can always be disabled to release its RAM.
			if (IsAbilityActive(actionId, targetId))
			{
				p.allowed = true;
				p.projectedTrace = Op.trace;
				p.reason = "Disable and release reserved RAM.";
				Step("Ability active — disable to release reserved RAM.", true);
				return p;
			}

			// Build the rule context for the action and its target.
			var c = Context(a.operatorTarget ? null : node, a);

			// Block the action when free RAM is too low.
			if (a.memoryCost > MemoryFree)
			{
				var ramMessage = "Not enough free RAM: disable an ability or wait for reclamation.";
				p.reason = ramMessage;
				Step(ramMessage, false);
				return p;
			}

			if (a.memoryCost > 0)
				Step("Enough free RAM (" + MemoryFree.ToString("0.#") + " GB free).", true);

			// Block the action when it does not apply to this target.
			var applies = a.visible.Test(c);
			var appliesMessage = applies ? "Action applies to this target." : ActionVisibilityBlockReason(a, node, c);
			if (!Step(appliesMessage, applies))
			{
				p.reason = appliesMessage;
				return p;
			}

			// Block the action until its technology is researched and equipped.
			if (!a.initiallyAvailable)
			{
				var granted = Granted(a);
				var grantMessage = granted
					? "Research and equipment requirement met."
					: "Research and equip " +
					  string.Join(
						  " / ",
						  Catalog.Data.technologies.Where(t => t.grants.Any(x => x.id == a.id))
									 .Select(t => t.title)) + ".";
				if (!Step(grantMessage, granted))
				{
					p.reason = grantMessage;
					return p;
				}
			}

			// Block the action when its use limit is spent.
			var limit = a.useLimit.Evaluate(c);
			if (limit > 0)
			{
				var usesOk = Counter("uses:" + a.id) < limit;
				var usesMessage = usesOk
					? "Uses remaining in this operation."
					: "This ability has no uses remaining in this operation.";
				if (!Step(usesMessage, usesOk))
				{
					p.reason = usesMessage;
					return p;
				}
			}

			// Block the action when any requirement fails.
			foreach (var r in Requirements(a, node))
			{
				var met = r.condition.Test(c);
				var message = Catalog.Format(r.message, c);
				if (!Step(message, met))
				{
					p.reason = message;
					return p;
				}
			}

			if (a.tags.Contains("download") && IsCredentialLocked(node?.id))
			{
				p.reason = CredentialHint(node.id);
				Step(p.reason, false);
				return p;
			}

			// Calculate the duration and trace cost.
			p.duration = ApplyModifiers("duration", a.duration.Evaluate(c), c) * Definition.paceMultiplier;
			c.Duration = p.duration;
			p.trace = ActionTrace(a, c, p.duration) + WatcherTrace(a.id, node?.id);

			// Reject non-finite, negative, or zero-length durations and negative traces.
			if (!Finite(p.duration) || !Finite(p.trace) || p.duration < 0 ||
				(!a.operatorTarget && !a.instant && p.duration == 0) ||
				p.trace < 0)
				throw new InvalidOperationException("Invalid duration/trace in action " + a.id);

			// A conservative forecast: protection can lower this, never increase it.
			var protectedSeconds = Mathf.Max(0, Op.decoyRemaining - Catalog.Data.career.maxTick);
			var exposedFraction = p.duration <= 0 ? 1 : Mathf.Clamp01((p.duration - protectedSeconds) / p.duration);
			p.projectedTrace = Op.trace + p.trace * exposedFraction + ForecastSweeps(p.duration);

			// Flag whether the trace would isolate or lose the quiet bonus.
			p.willIsolate = p.projectedTrace >= TraceLimit;
			p.losesQuietBonus = Op.trace < Job.quietLimit && p.projectedTrace >= Job.quietLimit;

			// Mark the action allowed and explain how it will run.
			p.allowed = true;
			p.reason = Op.task != null && a.queueable ? "Queue after the current task." : "Ready to execute.";
            var purge = Defenses.FirstOrDefault(d => d.kind == DefenseKind.PurgeTimer && d.keyAcquired && !d.disabled);
            if (purge != null) p.reason += " PURGE: " + Mathf.CeilToInt(purge.remaining) + "s left; controller capture or suppression prevents destruction.";
			Step(p.reason, true);
			return p;
		}

		// Explains why an action's visibility rule hides it from the current target.
		private static string ActionVisibilityBlockReason(ActionDefinition action, NetNode node, RuleContext context)
		{
			if (node == null)
				return "Select a system on the map.";

			if (action.id == "hack" && node.controlled)
				return "You already control this system.";

			if (action.id == "scan" && node.scanned)
				return "Already scanned. Its surrounding connections are known.";

			if (action.id == "download")
			{
				if (node.collected)
					return "Files already secured.";

				if (!node.objective && !node.bonus)
					return "No contract files or optional archives on this node.";
			}

			return "This action does not apply to this target.";
		}

		// Computes trace cost for an action including pressure, modifiers, and trace-free rules.
		private float ActionTrace(ActionDefinition action, RuleContext context, float duration)
		{
			// Record the duration on the context for duration-based rules.
			context.Duration = duration;

			// Read the base trace and the ambient trace pressure.
			float ordinary = action.baseTrace.Evaluate(context), pressure = duration * Stat("tracePressure", context);

			// Apply baseTrace modifiers, then trace modifiers and growth to the total.
			var adjusted = ApplyModifiers("baseTrace", ordinary, context);
			var total = ApplyModifiers("trace", adjusted + pressure, context) *
						Mathf.Max(0, Stat("traceGrowth", context));

			// Return no trace for trace-free actions.
			if (action.traceFree || Stat("traceFree", context) > 0)
				return 0;

			// An authored zero base trace (for example Signal) remains trace-free; ambient pressure still applies.
			return Mathf.Max(((adjusted == 0 ? 0 : ordinary) + pressure) * Catalog.Data.rig.minimumTraceFraction,
							 total);
		}

		/// <summary>Gets remaining uses for a limited action, or null when unlimited.</summary>
		/// <param name="actionId">Action definition id.</param>
		/// <param name="targetId">Target node id.</param>
		/// <returns>Remaining uses, or null when unlimited.</returns>
		public int? UsesRemaining(string actionId, string targetId = null)
		{
			// Look up the action definition.
			var action = Catalog.Action(actionId);

			// Return null when no operation is active or the action is unknown.
			if (!Active || action == null)
				return null;

			// Evaluate the use limit for this target.
			var limit = action.useLimit.Evaluate(Context(Node(targetId), action));

			// Subtract the uses already spent, or return null when unlimited.
			return limit <= 0 ? null : Mathf.Max(0, Mathf.CeilToInt(limit) - Counter("uses:" + action.id));
		}

		/// <summary>Returns whether an action can be queued on a target.</summary>
		/// <param name="actionId">Action definition id.</param>
		/// <param name="targetId">Target node id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool CanQueue(string actionId, string targetId)
		{
			// Resolve the action and target node.
			var a = Catalog.Action(actionId);
			var n = Node(targetId);

			// Reject when the queue is unavailable, full, or the action cannot be queued.
			if (!Active || Op.task == null || a == null || !a.queueable || a.operatorTarget || n == null ||
				Op.queue.Count >= QueueCapacity)
				return false;

			// Reject when free RAM is too low.
			if (a.memoryCost > MemoryFree)
				return false;

			// Reject duplicates of the queued or running action.
			if (Op.queue.Any(q => q.actionId == actionId && q.target == targetId) ||
				(Op.task.actionId == actionId && Op.task.target == targetId))
				return false;

			// Reject when the action is not granted, visible, or meets its queue requirements.
			var c = Context(n, a);
			if (!Granted(a) || !a.visible.Test(c) || !a.queueRequirements.All(r => r.condition.Test(c)))
				return false;

			// Reject when the use limit would be exceeded, counting reserved uses.
			var limit = a.useLimit.Evaluate(c);
			var reserved = (Op.task.actionId == actionId ? 1 : 0) + Op.queue.Count(q => q.actionId == actionId);
			if (limit > 0 && Counter("uses:" + actionId) + reserved >= limit)
				return false;

			// Reject future-neighbor actions that are out of reach.
			if (a.futureNeighborQueue && !Reachable(n) && !(Stat("futureQueue", c) > 0 &&
															Neighbors(Node(Op.task.target)).Any(x => x.id == targetId)))
				return false;

			return true;
		}

		/// <summary>Runs, queues, toggles off, or instant-completes an action.</summary>
		/// <param name="actionId">Action or legacy tool kind.</param>
		/// <param name="targetId">Target node id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Execute(string actionId, string targetId = null)
		{
			// Resolve the action and reject when no operation is active.
			var action = Catalog.Action(actionId);
			if (!Active || action == null)
				return false;

			// Toggle off an active ability and release its RAM.
			if (IsAbilityActive(actionId, targetId))
			{
				// Release the ability and resume the operation.
				ReleaseAbility(actionId, targetId);
				ResumeAfterActionUnlessManuallyPaused();
				Touch();
				return true;
			}

			// Queue the action behind the running task when it allows queuing.
			if (Op.task != null && action.queueable && !action.operatorTarget)
			{
				// Reject when the queue rules do not allow it.
				if (!CanQueue(actionId, targetId))
					return false;

				// Add the action to the queue and log it.
				Op.queue.Add(new QueuedTool(actionId, targetId));
				Log("QUEUED // " + ActionTitle(actionId, targetId) + " : " + Node(targetId).label);
				ResumeAfterActionUnlessManuallyPaused();
				Touch();
				return true;
			}

			// Preview the action and reject when it cannot run now.
			var p = Preview(actionId, targetId);
			if (!p.allowed)
				return false;

			if (Op.task != null && !action.instant && p.duration > 0)
				return false;

			// Spend the RAM cost and build the task.
			SpendMemory(action, targetId);
			var task = new ToolTask
			{
				actionId = actionId, target = action.operatorTarget ? null : targetId, duration = p.duration,
				trace = p.trace
			};

			// Complete instant actions immediately.
			if ((action.operatorTarget || action.instant) && p.duration == 0)
			{
				// Add the trace, complete the task, and refresh the UI.
				Op.trace = Mathf.Min(TraceLimit, Op.trace + p.trace);
				CompleteTask(task);
				RefreshGraph();
				ResumeAfterActionUnlessManuallyPaused();
				Touch();
				return true;
			}

			// Start the timed task when the workstation is free.
			if (Op.task != null)
				return false;

			Op.task = task;
			Log("RUN // " + ActionTitle(actionId, targetId) + " : " + (Node(targetId)?.label ?? "operator"));
			ResumeAfterActionUnlessManuallyPaused();
			Touch();
			return true;
		}

		// Maps a legacy tool kind to the first visible action id in that group.
		private string LegacyAction(ToolKind kind, string target)
		{
			// Derive the legacy group name from the tool kind.
			var group = kind.ToString().ToLowerInvariant();

			// Resolve the target node for visibility checks.
			var n = Node(target);

			// Return the first visible action in the group, or the group name itself.
			return Catalog.Data.actions.FirstOrDefault(a => a.legacyGroup == group && a.visible.Test(Context(n, a)))
						  ?.id ?? group;
		}

		/// <summary>Previews duration, trace, and eligibility for an action.</summary>
		/// <param name="actionId">Action or legacy tool kind.</param>
		/// <param name="targetId">Target node id.</param>
		/// <returns>Preview with eligibility and player-facing reason.</returns>
		public ToolPreview Preview(ToolKind kind, string id)
		{
			return Preview(LegacyAction(kind, id), id);
		}

		/// <summary>Runs, queues, toggles off, or instant-completes an action.</summary>
		/// <param name="actionId">Action or legacy tool kind.</param>
		/// <param name="targetId">Target node id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Execute(ToolKind kind, string id)
		{
			return Execute(LegacyAction(kind, id), id);
		}

		/// <summary>Runs the decoy operator action.</summary>
		/// <returns>True when the operation succeeded.</returns>
		public bool Decoy()
		{
			return Execute("decoy");
		}

		/// <summary>Clears queued tools on the active operation.</summary>
		public void ClearQueue()
		{
			// Ignore the request when no operation is active.
			if (!Active)
				return;

			// Empty the queue and notify the UI.
			Op.queue.Clear();
			Touch();
		}

		/// <summary>Stops the running tool and clears the queue when allowed.</summary>
		/// <returns>True when the operation succeeded.</returns>
		public bool StopTask()
		{
			// Reject when the running task cannot be cancelled.
			if (!CanStopTask)
				return false;

			// Capture the running task before clearing it.
			var stopped = Op.task;

			// Release a persistent ability's RAM when the task was one.
			if (Catalog.Action(stopped.actionId).memoryMode == MemoryMode.Persistent)
				ReleaseAbility(stopped.actionId, stopped.target);

			// Log the stop and clear the task and queue.
			Log("TOOL STOPPED // " + Catalog.Action(stopped.actionId).title + ". Existing trace remains.");
			Op.task = null;
			Op.queue.Clear();

			// Notify the UI.
			Touch();
			return true;
		}

		/// <summary>Advances simulation time, trace, statuses, and queued tools.</summary>
		/// <param name="delta">Elapsed simulation seconds.</param>
		public void Tick(float delta)
		{
			// Skip the tick when paused, idle, or given an invalid delta.
			if (!Active || Op.paused || delta <= 0 || !Finite(delta))
				return;

			// Clamp the step and advance the operation clock and RAM reclamation.
			delta = Mathf.Min(delta, Catalog.Data.career.maxTick);
			Op.elapsed += delta;
			AdvanceSweeps(delta);
            AdvancePurge(delta);
            if (!Active) return;
			if (CheckTrace()) return;
			Op.memoryReclaiming = Mathf.Max(0, Op.memoryReclaiming - MemoryReclaim * delta);

			// Count down timed statuses and expire the ones that finish.
			var expired = false;
			foreach (var status in Op.statuses.ToArray())
			{
				// Skip persistent statuses tied to a memory reservation.
				if (!string.IsNullOrEmpty(status.memoryKey))
					continue;

				// Reduce the remaining time and keep statuses still running.
				status.remaining = Mathf.Max(0, status.remaining - delta);
				if (status.remaining > 0)
					continue;

				// Remove the expired status, log it, and fire its trigger.
				Op.statuses.Remove(status);
				expired = true;
				SyncStatuses();
				Log("STATUS EXPIRED // " + Catalog.Data.statuses.First(s => s.id == status.id).title + " : " +
					(Node(status.target)?.label ?? "operator"));
				Dispatch(RuleEvent.StatusExpired, Context(Node(status.target)));
			}

			// Re-sync statuses and notify the UI when one expired.
			SyncStatuses();
			if (expired)
				Touch();

			// Accrue idle trace when no task is running.
			var task = Op.task;
			if (task == null)
			{
				if (!Op.statuses.Any(s => s.preventsTrace))
					Op.trace += delta * Stat("idleTracePerSecond");
				CheckTrace();
				return;
			}

			// Advance the running task and accrue its trace.
			var step = Mathf.Min(delta, task.duration - task.elapsed);

			// Sustained effects can be toggled while a tool runs; evaluate its remaining trace against current reservations.
			task.trace = ActionTrace(Catalog.Action(task.actionId),
									 Context(Node(task.target), Catalog.Action(task.actionId)), task.duration) + WatcherTrace(task.actionId, task.target);
			if (!Op.statuses.Any(s => s.preventsTrace))
				Op.trace += step / task.duration * task.trace;
			task.elapsed += step;

			// Stop when the trace limit is reached or the task is still running.
			if (CheckTrace())
				return;

			if (task.elapsed + .0001f < task.duration)
				return;

			// Complete the finished task and refresh the UI.
			CompleteTask(task);
			Op.task = null;
			RefreshGraph();
			Touch();

			// Run queued actions until one starts a new task.
			while (Active && Op.task == null && Op.queue.Count > 0)
			{
				// Dequeue the next action and run it.
				var next = Op.queue[0];
				Op.queue.RemoveAt(0);

				// Log the skip when the queued action no longer qualifies.
				if (!Execute(next.actionId, next.target))
					Log("SKIPPED // Queued action no longer meets its requirements.");
			}
		}

		// Disconnects with a caught outcome when trace reaches the career limit.
		private bool CheckTrace()
		{
            Op.peakTrace = Mathf.Max(Op.peakTrace, Op.trace);
			// Keep running while trace is below the limit.
			if (Op.trace < TraceLimit)
				return false;

			// Clamp trace at the limit and force a caught disconnect.
			Op.trace = TraceLimit;
			Disconnect(true);
			return true;
		}

		// Applies action effects, counters, and completion triggers when a tool task finishes.
		private void CompleteTask(ToolTask task)
		{
			// Resolve the action and build its rule context.
			var action = Catalog.Action(task.actionId);
			var c = Context(Node(task.target), action);
			c.Duration = task.duration;

			// Count the use and tag persistent memory effects with their key.
			AddCounter("uses:" + action.id, 1);
			applyingMemoryKey = action.memoryMode == MemoryMode.Persistent ? MemoryKey(action.id, task.target) : null;

			// Apply the action effects and always clear the memory key afterwards.
			try
			{
				ApplyEffects(action.effects, c);
			}
			finally
			{
				applyingMemoryKey = null;
			}

			// Fire completion triggers and log when all objectives are secured.
			Dispatch(RuleEvent.ActionCompleted, c);
            ArmPurges(task.actionId, task.target);
			if (ObjectiveComplete)
				Log(
					"ALL OBJECTIVES SECURED // Disconnect to deliver and collect your reward. Optional archives remain available.");
		}

		// Runs authored effects with depth and count limits to prevent runaway triggers.
		internal void ApplyEffects(IEnumerable<Effect> effects, RuleContext c)
		{
			// Track nested effect depth and reset the budget at the outermost level.
			if (effectDepth++ == 0)
				effectCount = 0;

			try
			{
				// Guard against runaway trigger recursion.
				if (effectDepth > 64)
					throw new InvalidOperationException("Gameplay trigger depth limit exceeded.");

				// Apply each effect while the execution budget holds.
				foreach (var effect in effects)
				{
					if (++effectCount > 4096)
						throw new InvalidOperationException("Gameplay effect execution budget exceeded.");

					effect.Apply(c);
				}
			}
			finally
			{
				// Leave the current effect depth.
				effectDepth--;
			}
		}

		// Fires catalog and technology triggers that match an event and pass their conditions.
		private void Dispatch(RuleEvent evt, RuleContext c)
		{
			// Gather catalog triggers and the triggers of unlocked, active technologies.
			var triggers = Catalog.Data.triggers
								  .Concat(Catalog.Data.technologies
												 .Where(t => Has(t.id) && (!t.special || Equipped(t.id)))
												 .SelectMany(t => t.triggers)).ToArray();

			// Fire each trigger that matches the event, is not spent, and passes its condition.
			foreach (var t in triggers)
			{
				// Skip triggers for other events, spent one-time triggers, or failed conditions.
				if (t.on != evt || (!string.IsNullOrEmpty(t.onceKey) && Counter(t.onceKey) > 0) ||
					!t.when.Test(c))
					continue;

				// Mark one-time triggers as spent and apply their effects.
				if (!string.IsNullOrEmpty(t.onceKey))
					AddCounter(t.onceKey, 1);
				ApplyEffects(t.effects, c);
			}
		}

		/// <summary>Reads a named operation counter.</summary>
		/// <param name="key">Counter key.</param>
		public int Counter(string key)
		{
			return Op?.counters.FirstOrDefault(x => x.key == key)?.value ?? 0;
		}

		// Increments or creates a named counter on the active operation.
		internal void AddCounter(string key, int amount)
		{
			// Find the existing counter for this key.
			var item = Op.counters.FirstOrDefault(x => x.key == key);

			// Add a new counter or increase the existing value.
			if (item == null)
				Op.counters.Add(new CounterState { key = key, value = amount });
			else
				item.value += amount;
		}

		/// <summary>Gets max remaining duration for a status on a target.</summary>
		/// <param name="id">Status definition id.</param>
		/// <param name="target">Node id, or null for operator status.</param>
		public float StatusRemaining(string id, string target = null)
		{
			return Op?.statuses.Where(s => s.id == id && s.target == target).Select(s => s.remaining).DefaultIfEmpty(0)
					 .Max() ?? 0;
		}

		// Adds or refreshes a timed status on the operator or a node during effect application.
		internal void ApplyStatus(StatusDefinition status, string target, float duration)
		{
			// Reject non-positive or non-finite durations.
			if (duration <= 0 || !Finite(duration))
				throw new InvalidOperationException("Invalid status duration for " + status.id);

			// Find an existing status with the same id, target, and memory key.
			var state = Op.statuses.FirstOrDefault(s => s.id == status.id && s.target == target &&
														s.memoryKey == applyingMemoryKey);

			// Create the status when it is not already applied.
			if (state == null)
			{
				state = new StatusState
				{
					id = status.id, target = target, preventsTrace = status.preventsTrace,
					disablesDefense = status.disablesDefense
				};
				Op.statuses.Add(state);
			}

			// Refresh the duration and sync node suppression.
			state.remaining = duration;
			state.memoryKey = applyingMemoryKey;
			SyncStatuses();
		}

		// Recomputes per-node defense suppression from active disabling statuses.
		private void SyncStatuses()
		{
			// Recompute each node's suppression from active defense-disabling statuses.
			foreach (var n in Op.nodes)
				n.suppressed = Op.statuses.Where(s => s.target == n.id && s.disablesDefense).Select(s => s.remaining)
								 .DefaultIfEmpty(0).Max();
		}

		// Resolves effect target nodes from a selector scope and filter in the active operation.
		internal IEnumerable<NetNode> Select(NodeSelector selector, RuleContext c)
		{
			// Pick the candidate node set for the selector's scope.
			IEnumerable<NetNode> nodes;
			switch (selector.scope)
			{
				case SelectionScope.All:       nodes = Op.nodes; break;
				case SelectionScope.Neighbors: nodes = Neighbors(c.Node); break;
				case SelectionScope.Entry:     nodes = new[] { Node(Op.entryNode) }; break;
				case SelectionScope.Named:     nodes = new[] { Node(selector.nodeId) }; break;
				default:                       nodes = new[] { c.Node }; break;
			}

			// Keep only the nodes that pass the selector's filter.
			return nodes.Where(n => n != null && selector.filter.Test(c.At(n)));
		}

		// Assigns a writable node state flag and fires control triggers when appropriate.
		internal void SetState(NetNode node, NodeState state, bool value, RuleContext context)
		{
			// Assign the flag that matches the requested state.
			switch (state)
			{
				case NodeState.Known:     node.known = value; break;
				case NodeState.Scanned:   node.scanned = value; break;
				case NodeState.Collected: node.collected = value; break;
				case NodeState.Objective: node.objective = value; break;
				case NodeState.Bonus:     node.bonus = value; break;
				case NodeState.Controlled:
					// Record whether the controlled flag actually changes.
					var changed = node.controlled != value;

					// Apply the new controlled flag.
					node.controlled = value;
					if (value)
						foreach (var defense in Defenses.Where(d => d.kind == DefenseKind.SecuritySweep && d.sourceNodeId == node.id))
							defense.disabled = true;

					// Release abilities that target this node when control is gained.
					if (value)
						foreach (var reservation in Op.reservations
													  .Where(r => r.target == node.id &&
																  Catalog.Action(r.actionId).releaseOnTargetControl)
													  .ToArray())
							ReleaseAbility(reservation.actionId, reservation.target);

					// Record the last controlled node and fire the control trigger on a real change.
					if (value && changed)
					{
						Op.lastControlled = node.id;
						Dispatch(RuleEvent.ControlEstablished, context.At(node));
					}

					break;
				default: throw new InvalidOperationException("This state is computed and cannot be assigned: " + state);
			}
		}

		// Marks nodes known within hop radius of a source on the active link graph.
		internal void Reveal(NetNode source, int radius)
		{
			// Stop when there is no source node.
			if (source == null)
				return;

			// Seed the reveal frontier with the source node.
			var visited = new HashSet<string> { source.id };
			var frontier = new List<NetNode> { source };
			source.known = true;

			// Expand the known area outward one neighbor ring at a time.
			for (var i = 0; i < radius; i++)
			{
				// Mark unvisited neighbors as known and use them as the next frontier.
				var next = new List<NetNode>();
				foreach (var n in frontier)
					foreach (var neighbor in Neighbors(n))
						if (visited.Add(neighbor.id))
						{
							neighbor.known = true;
							next.Add(neighbor);
						}

				frontier = next;
			}
		}

		/// <summary>Rebuilds known graph documents from discovered nodes.</summary>
		public void RefreshGraph()
		{
			// Nothing to build when no operation is active.
			if (!Active)
				return;

			// Start a fresh known-graph document for this operation.
			KnownGraph = new GraphDocument("operation-" + Op.contractId, Job.title);

			// Add each discovered node with its control and objective state.
			foreach (var n in Op.nodes.Where(n => n.known))
				KnownGraph.UpsertNode(new GraphNodeRecord(n.id, n.kind.ToString(), n.label,
														  new Dictionary<string, GraphValue>
														  {
															  ["controlled"] = GraphValue.From(n.controlled),
															  ["objective"] = GraphValue.From(n.objective)
														  }));

			// Add both directions of each link between discovered nodes.
			foreach (var link in Op.links.Where(l => Node(l.a).known && Node(l.b).known))
			{
				KnownGraph.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));
				KnownGraph.UpsertEdge(new GraphEdgeRecord(link.b + ">" + link.a, link.b, link.a, "network"));
			}

			// Build the layout scene from the known graph.
			Scene = new GraphScene(KnownGraph, new GraphSchema());

			// Pin each known node at its stored position.
			foreach (var n in Op.nodes.Where(n => n.known))
			{
				Scene.Nodes[n.id].Position = n.position;
				Scene.Nodes[n.id].Pinned = true;
			}
		}

		/// <summary>Finds a path from the gateway to a node id.</summary>
		/// <param name="id">Destination node id.</param>
		/// <param name="controlledOnly">When true, path must stay on controlled nodes except the destination.</param>
		/// <returns>Path result, or null when no operation graph exists.</returns>
		public GraphPathResult Route(string id, bool controlledOnly = false)
		{
			return !Active || Scene == null
				? null
				: GraphPathfinder.Find(Scene, Op.entryNode, id,
									   controlledOnly
										   ? e => Node(e.From).controlled && (Node(e.To).controlled || e.To == id)
										   : null);
		}

		/// <summary>Ends the operation, builds a report, and applies outcome rewards.</summary>
		/// <param name="caught">When true, treats the disconnect as trace lockdown.</param>
		/// <returns>The operation report, or null when not active.</returns>
		public OperationReport Disconnect(bool caught = false)
		{
			// Nothing to disconnect when no operation is active.
			if (!Active)
				return null;

			// Decide the outcome from the caught flag and objective progress.
			var success = !caught && ObjectiveComplete;
			var outcome = caught ? OperationOutcome.Caught :
				success ? OperationOutcome.Delivered : OperationOutcome.Aborted;
			var policy = Catalog.Data.career.outcomes.Single(p => p.outcome == outcome);

			// Count the optional bonus archives and how many were collected.
			var bonusTotal = Op.nodes.Count(n => n.bonus);
			var bonusCollected = Op.nodes.Count(n => n.bonus && n.collected);

			// Build the report that summarizes this operation.
			var report = new OperationReport
			{
				title = Job.title, company = Job.company, trace = Op.trace, success = success, day = Career.day,
				verdict = policy.verdict, narrative = Catalog.Format(policy.narrative, Context()),
				log = new List<string>(Op.log), bonusFiles = bonusCollected, bonusArchivesTotal = bonusTotal,
				quietLimit = Job.quietLimit
			};

			// Bank credits and XP from matching reward rules when the outcome pays out.
			if (policy.bankRewards)
			{
				// Sum the rewards from each matching rule.
				var c = Context();
				foreach (var reward in Catalog.Data.rewards.rules)
					if (reward.when.Test(c))
					{
						report.credits += Mathf.RoundToInt(reward.credits.Evaluate(c));
						report.xp += Mathf.RoundToInt(reward.xp.Evaluate(c));
					}

				// Record the quiet bonus and add the rewards to the career.
				report.quietBonusEarned = Mathf.Max(Op.peakTrace, report.trace) < Job.quietLimit;
				Career.credits += report.credits;
				Career.xp += report.xp;
				Career.totalEarnedXp += report.xp;
			}

			// Bank lore fragments from each collected bonus archive.
			if (policy.bankLore)
				foreach (var n in Op.nodes.Where(n => n.bonus && n.collected))
				{
					// Add the fragment once per career.
					var fragment = LoreContent.Find(Job, n.loreId);
					if (fragment != null && !Career.lore.Any(f => f.id == fragment.id))
						Career.lore.Add(fragment);
				}

			// Record the completed contract and its news entry.
			if (policy.recordCompletion)
			{
				Career.jobs++;

				// Mark non-recurring contracts as completed.
				if (!Job.recurring && !Career.completed.Contains(Job.id))
					Career.completed.Add(Job.id);
				if (!string.IsNullOrEmpty(Definition.revelation) && !Career.lore.Any(l => l.id == Job.id + "/revelation"))
                    Career.lore.Add(new LoreFragment { id = Job.id + "/revelation", title = Job.title + " / Debrief", contract = Job.title, company = Job.company, text = Definition.revelation });
                Career.news.Insert(0, "DAY " + Career.day.ToString("00") + " // " + Job.aftermath);
			}

			// Advance the career day and store the report.
			Career.day += policy.dayAdvance;
            RefreshOffers();
			Career.reports.Insert(0, report);

			// Trim the report archive to the configured limit.
			if (Career.reports.Count > Catalog.Data.career.reportLimit)
				Career.reports.RemoveRange(Catalog.Data.career.reportLimit,
										   Career.reports.Count - Catalog.Data.career.reportLimit);

			// Clear the live operation and its graph state.
			LastReport = report;
			Career.operation = null;
			KnownGraph = null;
			Scene = null;

			// Notify the UI and return the report.
			Touch();
			return report;
		}

		// Adds the debug grant amount without overflowing 32-bit integers.
		private static int DebugGrant(int current)
		{
			return (int)Math.Min(int.MaxValue, (long)current + DebugGrantAmount);
		}

		/// <summary>Debug: grants credits to the career.</summary>
		public void DebugAddCredits()
		{
			// Grant the debug credit amount.
			Career.credits = DebugGrant(Career.credits);

			// Notify the UI.
			Touch();
		}

		/// <summary>Debug: grants research XP to the career.</summary>
		public void DebugAddXp()
		{
			// Grant the debug XP to the spendable pool and the lifetime total.
			Career.xp = DebugGrant(Career.xp);
			Career.totalEarnedXp = DebugGrant(Career.totalEarnedXp);

			// Notify the UI.
			Touch();
		}

		/// <summary>Debug: unlocks every technology in the catalog.</summary>
		public void DebugUnlockAllTech()
		{
			// Unlock every catalog technology that is not yet owned.
			foreach (var tech in Catalog.Technologies)
				if (!Has(tech.id))
					Career.unlocked.Add(tech.id);

			// Reveal the newly unlocked research and notify the UI.
			EnsureResearchReveal();
			Touch();
		}

		/// <summary>Returns whether a technology appears on the research graph.</summary>
		/// <param name="id">Technology id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool ResearchVisible(string id)
		{
			return !string.IsNullOrEmpty(id) && Career.researchRevealed.Contains(id);
		}

		// Reveals a technology and its direct children. Only these nodes enter the research graph.
		/// <summary>Reveals a technology and its direct children on the research graph.</summary>
		/// <param name="id">Technology id.</param>
		public void RevealResearch(string id)
		{
			// Ignore unknown technology ids.
			if (Catalog.Technology(id) == null)
				return;

			// Reveal the technology itself.
			if (!Career.researchRevealed.Contains(id))
				Career.researchRevealed.Add(id);

			// Reveal each direct child technology.
			foreach (var child in Catalog.Technologies.Where(t => t.parents.Contains(id)).Select(t => t.id))
				if (!Career.researchRevealed.Contains(child))
					Career.researchRevealed.Add(child);
		}

		// Idempotent: migrates older saves and keeps the reveal set consistent with unlocked research.
		/// <summary>Migrates and syncs research reveal state with unlocked tech.</summary>
		public void EnsureResearchReveal()
		{
			// Ensure the reveal list exists and drop ids that no longer exist in the catalog.
			Career.researchRevealed ??= new List<string>();
            Career.storyFlags ??= new List<string>(); Career.resolvedChoices ??= new List<string>();
            Career.offers ??= new List<ContractOffer>(); Career.investments ??= new List<ResearchInvestment>(); RefreshOffers();
			Career.researchRevealed.RemoveAll(id => Catalog.Technology(id) == null);

			// Reveal the research around every unlocked technology.
			foreach (var id in Career.unlocked.ToList())
				RevealResearch(id);
		}

		/// <summary>Purchases a technology when affordable and prerequisites are met.</summary>
		/// <param name="id">Technology id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool BuyTech(string id)
		{
			// Look up the technology definition.
			var tech = Catalog.Technology(id);

			// Reject when research is locked, unknown, owned, unaffordable, or missing a prerequisite.
			if ((Active && !Catalog.Data.career.researchDuringOperations) || tech == null || Has(id) ||
				Career.xp < tech.cost || !tech.parents.All(t => Has(t.id)))
				return false;

			// Spend the XP and record the research spending.
			Career.xp -= tech.cost;
			Career.researchSpent += tech.cost;
            Career.investments.Add(new ResearchInvestment { id = tech.id, paid = tech.cost });

			// Unlock the technology and reveal its children.
			Career.unlocked.Add(id);
			RevealResearch(id);

			// Auto-equip special technology when a slot is free.
			if (tech.special && Career.equipped.Count < Slots)
				Career.equipped.Add(id);

			// Notify the UI.
			Touch();
			return true;
		}

		/// <summary>Equips or unequips a special technology within slot limits.</summary>
		/// <param name="id">Technology id.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool ToggleEquip(string id)
		{
			// Reject when equipping is locked, the tech is not owned, or it is not special.
			if ((Active && !Catalog.Data.career.equipDuringOperations) || !Has(id) ||
				Catalog.Technology(id)?.special != true)
				return false;

			// Unequip when already equipped, otherwise equip if a slot is free.
			if (Career.equipped.Contains(id))
				Career.equipped.Remove(id);
			else if (Career.equipped.Count < Slots)
				Career.equipped.Add(id);
			else
				return false;

			// Notify the UI.
			Touch();
			return true;
		}

		/// <summary>Refunds research XP and resets unlocks per catalog rules.</summary>
		/// <returns>True when the operation succeeded.</returns>
		public bool RefundResearch()
		{
			// Reject when refunds are locked during an operation.
			if (Active && !Catalog.Data.career.researchDuringOperations)
				return false;

			// Preview what the refund will return and read the starter technologies.
			var refund = PreviewResearchRefund();
			var initial = Catalog.Data.career.initialTechnologies.Select(t => t.id).ToArray();

			// Return the refunded XP and reset research spending.
			Career.xp += Mathf.FloorToInt(Career.researchSpent * Catalog.Data.career.refundFraction);
			Career.researchSpent = 0;
            Career.investments.Clear();

			// Reset unlocks to the starter set and clear equipment and waypoints.
			Career.unlocked = initial.ToList();
			Career.equipped.Clear();
			Career.waypoints.Clear();

			// Refund credits from sold rig parts and remove those parts.
			Career.credits += refund.credits;
			Career.rigParts.RemoveAll(p => refund.soldInstances.Contains(p.instanceId));

			// Notify the UI.
			Touch();
			return true;
		}

		/// <summary>Gets credit price for the next hardware tier.</summary>
		/// <param name="id">Hardware definition id or catalog hardware index.</param>
		public int UpgradePrice(string id)
		{
			// Look up the hardware definition and the installed tier.
			var h = Catalog.Data.hardware.FirstOrDefault(x => x.id == id);
			var level = Career.Level(id);

			// Return the next tier's price, or zero when the hardware is unknown or maxed.
			return h != null && level < h.tiers.Length ? h.tiers[level].price : 0;
		}

		/// <summary>Previews hardware upgrade eligibility and cost.</summary>
		/// <param name="id">Hardware definition id.</param>
		/// <returns>Preview with eligibility and player-facing reason.</returns>
		public ToolPreview PreviewUpgrade(string id)
		{
			// Start with an empty preview and look up the hardware definition.
			var p = new ToolPreview();
			var h = Catalog.Data.hardware.FirstOrDefault(x => x.id == id);

			// Block the preview when the hardware is unknown.
			if (h == null)
			{
				p.reason = "Unknown hardware.";
				return p;
			}

			// Block the upgrade during an operation when hardware is locked.
			if (Active && !Catalog.Data.career.hardwareDuringOperations)
			{
				p.reason = "Install hardware between contracts.";
				return p;
			}

			// Block the upgrade when the top tier is already installed.
			if (Career.Level(id) >= h.tiers.Length)
			{
				p.reason = "Maximum tier installed.";
				return p;
			}

			// Block the upgrade when any hardware requirement fails.
			foreach (var r in h.requirements)
				if (!r.condition.Test(Context()))
				{
					p.reason = Catalog.Format(r.message, Context());
					return p;
				}

			// Block the upgrade when the player cannot afford the next tier.
			if (Career.credits < UpgradePrice(id))
			{
				p.reason = "Need " + (UpgradePrice(id) - Career.credits) + " more credits.";
				return p;
			}

			// Mark the upgrade as ready to install.
			p.allowed = true;
			p.reason = "Ready to install.";
			return p;
		}

		/// <summary>Purchases the next hardware tier when preview allows.</summary>
		/// <param name="id">Hardware definition id or catalog hardware index.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Upgrade(string id)
		{
			// Look up the hardware definition.
			var h = Catalog.Data.hardware.FirstOrDefault(x => x.id == id);

			// Reject when the upgrade is not allowed.
			if (!PreviewUpgrade(id).allowed)
				return false;

			// Pay the price and install the next tier.
			Career.credits -= UpgradePrice(id);
			Career.SetLevel(id, Career.Level(id) + 1);

			// Notify the UI.
			Touch();
			return true;
		}

		/// <summary>Gets credit price for the next hardware tier.</summary>
		/// <param name="id">Hardware definition id or catalog hardware index.</param>
		public int UpgradePrice(int index)
		{
			return index >= 0 && index < Catalog.Hardware.Count ? UpgradePrice(Catalog.Hardware[index].id) : 0;
		}

		/// <summary>Purchases the next hardware tier when preview allows.</summary>
		/// <param name="id">Hardware definition id or catalog hardware index.</param>
		/// <returns>True when the operation succeeded.</returns>
		public bool Upgrade(int index)
		{
			return index >= 0 && index < Catalog.Hardware.Count && Upgrade(Catalog.Hardware[index].id);
		}

		/// <summary>Appends a timestamped line to the operation log.</summary>
		/// <param name="message">Log line without timestamp.</param>
		public void Log(string message)
		{
			// Skip logging when no operation is active.
			if (!Active)
				return;

			// Format the elapsed operation time as minutes and seconds.
			int min = (int)Op.elapsed / 60, sec = (int)Op.elapsed % 60;

			// Append the timestamped line.
			Op.log.Add(min.ToString("00") + ":" + sec.ToString("00") + "  " + message);

			// Trim the oldest line when the log exceeds its limit.
			if (Op.log.Count > Catalog.Data.career.logLimit)
				Op.log.RemoveAt(0);
		}

		// Bumps the revision counter and notifies UI listeners of simulation changes.
		private void Touch()
		{
			// Bump the revision and notify UI listeners.
			Revision++;
			Changed?.Invoke();
		}
	}

	/// <summary>Loads and saves v4 career JSON to persistent storage.</summary>
	[NoAutoStaticsCleanup]
	public static class CareerStorage
	{
		/// <summary>Gets the default career save file path under persistent data.</summary>
		public static string SavePath => Path.Combine(Application.persistentDataPath, "career-v4.json");

		/// <summary>Gets the last load or save warning message, if any.</summary>
		public static string Warning { get; private set; }

		/// <summary>Loads a career save, falling back to backup or a fresh career on failure.</summary>
		/// <param name="path">Optional save path.</param>
		/// <param name="catalog">Catalog used to validate loaded data.</param>
		/// <returns>The loaded career.</returns>
		public static Career Load(string path = null, GameCatalog catalog = null)
		{
			// Default the catalog and path, and clear any previous warning.
			catalog ??= GameCatalog.Default;
			path ??= SavePath;
			Warning = null;

			// Start a new career when no save file exists yet.
			if (!File.Exists(path))
				return catalog.CreateCareer();

			// Read and validate a save file, rebuilding the simulation to normalize it.
			Career Read(string p)
			{
				// Parse the header and the career from the same JSON text.
				var json = File.ReadAllText(p);
				var header = JsonUtility.FromJson<SaveHeader>(json);
				var career = JsonUtility.FromJson<Career>(json);

				// Reject saves that are missing or not version 4.
				if (career == null || header == null || header.version != 4)
					throw new InvalidDataException("Unsupported career save.");

				return new GameSimulation(catalog, career).Career;
			}

			// Try the main save first.
			try
			{
				return Read(path);
			}
			catch (InvalidDataException ex)
			{
				// Surface validation failures to the player without falling back.
				Warning = ex.Message;
				throw;
			}
			catch (Exception ex)
			{
				// Record the read failure as a warning.
				Warning = "The career save could not be read. " + ex.Message;

				// Preserve a copy of the unreadable save before recovering.
				try
				{
					File.Copy(path, path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true);
				}
				catch
				{
				}

				// Fall back to the backup save when one exists.
				if (File.Exists(path + ".bak"))
					try
					{
						return Read(path + ".bak");
					}
					catch (Exception backup)
					{
						Warning += " Backup: " + backup.Message;
					}

				// Otherwise start a fresh career and block autosave to protect the unreadable file.
				var fresh = catalog.CreateCareer();
				fresh.saveBlocked = true;
				return fresh;
			}
		}

		/// <summary>Writes a fresh default career to disk.</summary>
		/// <param name="path">Optional save path.</param>
		/// <returns>True when the operation succeeded.</returns>
		public static bool Reset(string path = null)
		{
			// Build a fresh default career.
			var career = GameCatalog.Default.CreateCareer();

			// Save twice so the previous backup is also replaced with the fresh career.
			return Save(career, path) && Save(career, path);
		}

		/// <summary>Validates and atomically writes a career save.</summary>
		/// <param name="career">Career to persist.</param>
		/// <param name="path">Optional save path.</param>
		/// <param name="catalog">Catalog used for validation.</param>
		/// <returns>True when the operation succeeded.</returns>
		public static bool Save(Career career, string path = null, GameCatalog catalog = null)
		{
			// Default the save path.
			path ??= SavePath;

			// Refuse to save while autosave is blocked by an unreadable career.
			if (career.saveBlocked)
			{
				Warning = "Autosave is disabled to preserve an unreadable career. Use Reset Save to start a new one.";
				return false;
			}

			// Validate and write the save through a temp file.
			try
			{
				// Validate the career against the catalog before writing.
				GameSimulation.ValidateCareer(career, catalog ?? GameCatalog.Default);

				// Ensure the save folder exists and write the JSON to a temp file.
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
				File.WriteAllText(path + ".tmp", JsonUtility.ToJson(career, true));

				// Swap the temp file into place, keeping the previous save as a backup.
				if (File.Exists(path))
					File.Replace(path + ".tmp", path, path + ".bak");
				else
					File.Move(path + ".tmp", path);

				// Clear the warning after a successful write.
				Warning = null;

				return true;
			}
			catch (Exception ex)
			{
				// Record and log the write failure.
				Warning = "Autosave could not write: " + ex.Message;
				Debug.LogWarning(Warning);

				return false;
			}
		}

		// JSON header wrapper carrying the career save format version.
		[Serializable]
		private sealed class SaveHeader
		{
			public int version;
		}
	}
}
