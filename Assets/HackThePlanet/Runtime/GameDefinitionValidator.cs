using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HackThePlanet
{
	/// <summary>Cross-reference and rule validation for a loaded <see cref="GameDefinition" /> graph.</summary>
	public static class GameDefinitionValidator
	{
		/// <summary>Enumerates every <see cref="GameplayDefinition" /> registered under the game root.</summary>
		/// <param name="root">Game root whose nested assets should be flattened.</param>
		/// <returns>Root, career, rewards, rig, and all catalog arrays combined.</returns>
		public static IEnumerable<GameplayDefinition> Definitions(GameDefinition root)
		{
			return new GameplayDefinition[] { root, root.career, root.rewards }.Concat(root.companies)
																			   .Concat(root.contracts)
																			   .Concat(root.technologies)
																			   .Concat(root.actions)
																			   .Concat(root.nodeTypes)
																			   .Concat(root.networks)
																			   .Concat(root.hardware)
																			   .Concat(root.statuses)
																			   .Concat(root.chassis)
																			   .Concat(root.modules)
																			   .Concat(new[] { root.rig });
		}

		/// <summary>Validates the game root and throws when any error is reported.</summary>
		/// <param name="root">Game definition to validate.</param>
		/// <exception cref="InvalidOperationException">Validation produced one or more errors.</exception>
		public static void ThrowIfInvalid(GameDefinition root)
		{
			// Collect every validation error for the root.
			var errors = Validate(root);

			// Throw with the full error list when anything failed.
			if (errors.Count > 0)
				throw new InvalidOperationException("Invalid gameplay assets:\n" + string.Join("\n", errors));
		}

		/// <summary>Runs structural, reference, formula, and content checks on a game definition.</summary>
		/// <param name="root">Game definition to validate; may be null.</param>
		/// <returns>Human-readable error lines; empty when valid.</returns>
		public static IReadOnlyList<string> Validate(GameDefinition root)
		{
			// Collect validation error lines.
			var errors = new List<string>();

			// Stop immediately when the game root is missing.
			if (root == null)
			{
				errors.Add("Game: missing root asset.");
				return errors;
			}

			void Error(GameplayDefinition owner, string field, string message)
			{
				errors.Add((owner == null ? "Game" : owner.name) + "." + field + ": " + message);
			}

			// Require the career, rewards, and rig references to exist.
			if (root.career == null || root.rewards == null || root.rig == null)
			{
				Error(root, "career/rewards", "both references are required");
				return errors;
			}

			// Require every catalog array to be non-null.
			if (root.companies == null || root.contracts == null || root.technologies == null || root.actions == null ||
				root.nodeTypes == null || root.networks == null || root.hardware == null || root.statuses == null ||
				root.chassis == null || root.modules == null)
			{
				Error(root, "definitions", "catalog arrays cannot be null");
				return errors;
			}

			// Gather every definition and index the registered assets.
			var all = Definitions(root).ToArray();
			var registered = new HashSet<GameplayDefinition>(all.Where(x => x != null));

			// Require nonempty, unique IDs within each definition type.
			foreach (var group in all.Where(x => x != null).GroupBy(x => x.GetType()))
			{
				// Track the IDs already seen for this definition type.
				var ids = new HashSet<string>();
				foreach (var d in group)
					if (string.IsNullOrWhiteSpace(d.id) || !ids.Add(d.id))
						Error(d, "id", "a nonempty, unique ID is required within this definition type");
			}

			// Walk each definition for reference, numeric, and rule errors.
			foreach (var d in all)
			{
				// Report missing asset references in the root catalog.
				if (d == null)
				{
					Error(root, "definitions", "missing asset reference");
					continue;
				}

				// Check each visited node inside the definition's graph.
				Walk(d, d.name, new HashSet<object>(), 0, true, (obj, path) =>
				{
					// Require every reference to be registered in the root catalog.
					if (obj is GameplayDefinition reference && !registered.Contains(reference))
						Error(d, path, "reference is not registered in the root catalog");

					// Reject non-finite floats.
					if (obj is float value && (float.IsNaN(value) || float.IsInfinity(value)))
						Error(d, path, "value must be finite");

					// Reject division or modulo by a literal zero.
					if (obj is BinaryNumber binary &&
						(binary.operation == Arithmetic.Divide || binary.operation == Arithmetic.Modulo) &&
						binary.right is ConstantNumber number &&
						number.value == 0)
						Error(d, path, "division/modulo by zero");

					// Require random ranges to be ordered and below the int maximum.
					if (obj is RandomNumber random &&
						(random.minimum > random.maximum || random.maximum == int.MaxValue))
						Error(d, path, "invalid inclusive random range");

					// Require a status reference on status effects.
					if (obj is ApplyStatusEffect status && status.status == null)
						Error(d, path, "status reference is required");

					// Require a positive literal duration for status effects.
					if (obj is ApplyStatusEffect timed && timed.duration is ConstantNumber time && time.value <= 0)
						Error(d, path, "status duration must be positive");

					// Validate adjacency selectors and minimum neighbor counts.
					if (obj is RigNeighborCondition adjacent && (adjacent.minimum < 0 ||
																 (!string.IsNullOrEmpty(adjacent.kind) &&
																  !root.modules.Any(m => m != null &&
																		  (m.id == adjacent.kind ||
																		   m.family == adjacent.kind ||
																		   m.branch == adjacent.kind)))))
						Error(d, path, "invalid adjacency selector or minimum");

					// Validate rig neighbor selectors used by numeric inputs.
					if (obj is InputNumber neighbors && neighbors.input == NumberInput.RigNeighborCount &&
						!string.IsNullOrEmpty(neighbors.key) &&
						!root.modules.Any(m => m != null && (m.id == neighbors.key || m.family == neighbors.key ||
															 m.branch == neighbors.key)))
						Error(d, path, "unknown rig neighbor selector");

					// Require technology references on research conditions.
					if (obj is ResearchCondition research && research.technology == null)
						Error(d, path, "technology reference is required");

					// Require a condition on every requirement.
					if (obj is Requirement requirement && requirement.condition == null)
						Error(d, path, "requirement condition is required");

					// Require a stat, condition, and value on every modifier.
					if (obj is Modifier modifier && (modifier.value == null || modifier.when == null ||
													 string.IsNullOrWhiteSpace(modifier.stat)))
						Error(d, path, "modifier needs a stat, condition and value");

					// Reject assigning computed node states.
					if (obj is SetStateEffect state &&
						(state.state == NodeState.DefenseActive || state.state == NodeState.Reachable))
						Error(d, path, "computed states cannot be assigned");
				}, path => Error(d, path, "cyclic or excessively nested rule reference"));
			}

			// Index each modified stat's owner and dependency set, then resolve cycles.
			var statOwners = new Dictionary<string, GameplayDefinition>();
			var dependencies = new Dictionary<string, HashSet<string>>();
			var validStats = new HashSet<string>(root.career.stats.Select(s => s.id)
													 .Concat(new[] { "security", "duration", "baseTrace", "trace" }));

			// Record stat dependencies for every modifier across the definitions.
			foreach (var d in all.Where(d => d != null))
				Walk(d, d.name, new HashSet<object>(), 0, true, (obj, path) =>
				{
					// Reject references to stats that the career does not define.
					if (obj is StatNumber stat && !validStats.Contains(stat.stat ?? ""))
						Error(d, path, "unknown stat " + stat.stat);

					// Require the condition on a NOT node.
					if (obj is NotCondition not && not.condition == null)
						Error(d, path, "condition is required");

					// Require both operands on binary formulas.
					if (obj is BinaryNumber number && (number.left == null || number.right == null))
						Error(d, path, "both formula operands are required");

					// Require both operands on comparisons.
					if (obj is CompareCondition compare && (compare.left == null || compare.right == null))
						Error(d, path, "both comparison operands are required");

					// Require every branch on conditional formulas.
					if (obj is ConditionalNumber conditional &&
						(conditional.when == null || conditional.yes == null || conditional.no == null))
						Error(d, path, "conditional formula needs all branches");

					// Record the stat dependencies introduced by each modifier.
					if (obj is Modifier modifier && !string.IsNullOrEmpty(modifier.stat))
					{
						// Reject modifiers that target an undefined stat.
						if (!validStats.Contains(modifier.stat))
							Error(d, path, "modifier targets an undefined stat " + modifier.stat);

						// Create the dependency set and owner entry the first time a stat is seen.
						if (!dependencies.TryGetValue(modifier.stat, out var edges))
						{
							edges = new HashSet<string>();
							dependencies.Add(modifier.stat, edges);
							statOwners[modifier.stat] = d;
						}

						// Collect the stats and security input that this modifier reads.
						Walk(modifier, path, new HashSet<object>(), 0, false, (child, _) =>
						{
							// Record references to other stats.
							if (child is StatNumber referenced)
								edges.Add(referenced.stat);

							// Record reads of the security input as a dependency on "security".
							if (child is InputNumber input && input.input == NumberInput.Security)
								edges.Add("security");
						}, _ => { });
					}
				}, _ => { });

			// Track visiting and finished stats to detect dependency cycles.
			var statVisiting = new HashSet<string>();
			var statDone = new HashSet<string>();

			// Resolve a stat after its dependencies, reporting cycles along the way.
			void Stat(string key)
			{
				// Skip stats that were already resolved.
				if (statDone.Contains(key))
					return;

				// Report a cycle when a stat is reached again while still visiting.
				if (!statVisiting.Add(key))
				{
					Error(statOwners[key], "modifiers", "cyclic stat dependency involving " + key);
					return;
				}

				// Visit each dependency that is itself a modified stat.
				if (dependencies.TryGetValue(key, out var edges))
					foreach (var edge in edges)
						if (dependencies.ContainsKey(edge))
							Stat(edge);

				// Mark this stat as resolved.
				statVisiting.Remove(key);
				statDone.Add(key);
			}

			// Resolve every stat dependency, reporting cycles.
			foreach (var key in dependencies.Keys)
				Stat(key);

			// Check each action's formulas, limits, and rule lists.
			foreach (var a in root.actions.Where(x => x != null))
			{
				// Require every formula and limit to be present.
				if (a.duration == null || a.baseTrace == null || a.useLimit == null || a.visible == null)
					Error(a, "formulas", "duration, trace, visibility and use limit are required");

				// Targeted actions need a positive duration; instant actions must target the operator.
				if (a.duration is ConstantNumber duration &&
					(duration.value < 0 || (!a.operatorTarget && !a.instant && duration.value == 0)))
					Error(a, "duration",
						  "targeted actions need positive duration; instant actions must target the operator");

				// Validate the RAM cost and memory mode pairing.
				if (a.memoryCost < 0 || !Enum.IsDefined(typeof(MemoryMode), a.memoryMode) ||
					(a.memoryMode == MemoryMode.None && a.memoryCost != 0))
					Error(a, "memoryCost", "invalid RAM cost or mode");

				// Sustained effects must use the Persistent memory mode.
				if (a.memoryMode != MemoryMode.Persistent && (a.activeModifiers.Length > 0 || a.releaseOnTargetControl))
					Error(a, "memoryMode", "sustained effects require Persistent mode");

				// Reject negative constant trace costs.
				if (a.baseTrace is ConstantNumber trace && trace.value < 0)
					Error(a, "baseTrace", "trace cannot be negative");

				// Reject negative constant use limits.
				if (a.useLimit is ConstantNumber uses && uses.value < 0)
					Error(a, "useLimit", "use limit cannot be negative");

				// Require rule lists to be non-null.
				if (a.requirements == null || a.queueRequirements == null || a.effects == null)
					Error(a, "rules", "rule lists cannot be null");
			}

			// Track visiting and finished technologies to detect research cycles.
			var visiting = new HashSet<TechnologyDefinition>();
			var done = new HashSet<TechnologyDefinition>();

			// Depth-first walk over research parents that reports cycles and invalid tiers or costs.
			void Tech(TechnologyDefinition t)
			{
				// Skip nulls and technologies already validated.
				if (t == null || done.Contains(t))
					return;

				// Report a cycle when a technology is reached again while still visiting.
				if (!visiting.Add(t))
				{
					Error(t, "parents", "research dependency cycle");
					return;
				}

				// Require waypoint tiers between zero and four.
				if (t.waypointTier < 0 || t.waypointTier > 4)
					Error(t, "waypointTier", "waypoint tier must be between zero and four");

				// Reject negative research costs.
				if (t.cost < 0)
					Error(t, "cost", "research cost cannot be negative");

				// Validate each parent before finishing this technology.
				foreach (var parent in t.parents)
					Tech(parent);
				visiting.Remove(t);
				done.Add(t);
			}

			// Validate every research node.
			foreach (var t in root.technologies)
				Tech(t);

			// Require hardware tiers to exist with nonnegative prices.
			foreach (var h in root.hardware.Where(x => x != null))
				if (h.tiers == null || h.tiers.Any(t => t == null || t.price < 0))
					Error(h, "tiers", "tiers must exist and prices cannot be negative");

			// Validate each network template, using authored links or parallel routes.
			foreach (var n in root.networks.Where(x => x != null))
			{
				if (n.nodes == null || n.nodes.Length == 0)
				{
					Error(n, "nodes", "network needs nodes");
					continue;
				}

				// Collect node IDs while checking that each one is unique and nonempty.
				var ids = new HashSet<string>();
				foreach (var node in n.nodes)
				{
					// Reject null, blank, or duplicate node IDs.
					if (node == null || string.IsNullOrWhiteSpace(node.id) || !ids.Add(node.id))
					{
						Error(n, "nodes", "node IDs must be unique and nonempty");
						continue;
					}

					// Require a node type on every node.
					if (node.type == null)
						Error(n, "nodes." + node.id, "node type is required");
				}

				// Require the entry, preferred, and preview nodes to exist in the template.
				if (!ids.Contains(n.entryNode) || !ids.Contains(n.preferredNode) || !ids.Contains(n.previewNode))
					Error(n, "selection", "entry, preferred and preview nodes must exist in the template");

				// Require the entry node to start known and controlled.
				if (!n.nodes.Any(x => x != null && x.id == n.entryNode && x.known && x.controlled))
					Error(n, "entryNode", "entry must start known and controlled");

				// Authored layouts validate their explicit links; parallel layouts build routes.
				if (n.layout == NetworkLayout.Authored)
				{
					// Require every authored link to join two distinct template nodes.
					foreach (var edge in n.links)
						if (!ids.Contains(edge.a) || !ids.Contains(edge.b) || edge.a == edge.b)
							Error(n, "links", "invalid endpoint or self link");

					// Walk outward from the entry node to find every reachable node.
					var seen = new HashSet<string> { n.entryNode };
					var changed = true;

					// Repeat until no new nodes become reachable.
					while (changed)
					{
						changed = false;

						// Mark the far end of any link that touches a reachable node.
						foreach (var edge in n.links)
							if (seen.Contains(edge.a) || seen.Contains(edge.b))
							{
								changed |= seen.Add(edge.a);
								changed |= seen.Add(edge.b);
							}
					}

					// Reject templates with nodes that cannot be reached from the entry.
					if (seen.Count < ids.Count)
						Error(n, "links", "network contains unreachable nodes");
				}
				else
				{
					// Read the parallel-route configuration for this template.
					var r = n.routes;

					// Require every route role reference to be set.
					if (r == null || r.relay == null || r.workstation == null || r.scanner == null || r.archive == null)
					{
						Error(n, "routes", "route role references are required");
						continue;
					}

					// Require the extra ID, type, and label arrays to have equal lengths.
					if (r.extraIds.Length != r.extraTypes.Length || r.extraIds.Length != r.extraLabels.Length)
					{
						Error(n, "routes.extraIds", "IDs, types and labels need equal lengths");
						continue;
					}

					// Reject duplicate extra identities.
					foreach (var id in r.extraIds)
						if (!ids.Add(id))
							Error(n, "routes.extraIds", "duplicate identity " + id);

					// Require every reserved, route, sensor, and archive reference to exist in the template.
					foreach (var id in r.reserved.Concat(r.directRoute).Concat(r.sensors).Concat(r.secondaryTargets)
										.Concat(r.archives)
										.Concat(new[] { r.entrance, r.destination, r.sensorAttachment }))
						if (!ids.Contains(id))
							Error(n, "routes", "missing template node " + id);

					// Require tiers and generation formulas on the route layout.
					if (r.tiers.Length == 0 || r.directSecurity == null || r.routeSecurity == null ||
						r.workstationWhen == null)
						Error(n, "routes.tiers", "tiers and generation formulas are required");

					// Validate each tier's lane lengths and pool capacity.
					foreach (var tier in r.tiers)
					{
						// Require at least two routes whose first length matches the direct route.
						if (tier == null || tier.lengths.Length < 2 || tier.lengths.Any(l => l < 1) ||
							tier.lengths[0] != r.directRoute.Length)
						{
							Error(n, "routes.tiers", "need at least two routes; first length must match direct route");
							continue;
						}

						// Compute how many pooled nodes this tier needs for lanes and sensors.
						var poolNeeded = tier.lengths.Skip(1).Sum() +
										 Math.Max(0, tier.lengths.Length - 2) * tier.sensorsPerLane;

						// Reject tiers that lack nodes for the routes and the sensor pool.
						if (tier.nodeCount < ids.Count || tier.nodeCount - r.reserved.Distinct().Count() < poolNeeded)
							Error(n, "routes.tiers", "insufficient nodes for route and sensor pool");
					}
				}
			}

			// Validate each contract's references, objectives, and formulas.
			foreach (var c in root.contracts.Where(x => x != null))
			{
				// Require the company and network references.
				if (c.company == null || c.network == null)
				{
					Error(c, "company/network", "both references are required");
					continue;
				}

				// Collect the node and route IDs that the contract may reference.
				var ids = new HashSet<string>(c.network.nodes.Where(x => x != null).Select(n => n.id)
											   .Concat(c.network.routes?.extraIds ?? Array.Empty<string>()));

				// Require at least one objective, and each objective to target a real node with a completion rule.
				if (c.objectives == null || c.objectives.Length == 0)
					Error(c, "objectives", "contract needs at least one objective");
				else
					foreach (var goal in c.objectives)
						if (goal == null || !ids.Contains(goal.nodeId) || goal.complete == null)
							Error(c, "objectives", "missing target node or completion condition");

				// Walk the contract's conditions to check named-node prerequisites.
				Walk(c, "contract", new HashSet<object>(), 0, true, (obj, path) =>
				{
					// Require named prerequisites to exist on the contract's network.
					if (obj is NamedNodeCondition named && !ids.Contains(named.nodeId))
						Error(c, path, "missing prerequisite node " + named.nodeId);
				}, _ => { });

				// Require the contract's payout and difficulty formulas.
				if (c.credits == null || c.xp == null || c.difficulty == null || c.seed == null)
					Error(c, "formulas", "contract formulas are required");
			}

			// Read the rig rules once for the checks below.
			var rig = root.rig;

			// Require a registered starting chassis.
			if (rig.startingChassis == null || root.chassis.Length == 0)
				Error(rig, "startingChassis", "a registered starting chassis is required");

			// Validate sale fractions, resource maxima, and stacking limits.
			if (rig.saleFraction < 0 || rig.saleFraction > 1 || rig.researchSaleFraction < 0 ||
				rig.researchSaleFraction > 1 || rig.minimumTraceFraction < 0 || rig.minimumTraceFraction > 1 ||
				rig.maximumCompute <= 0 || rig.maximumBandwidth <= 0 ||
				rig.memoryReclaim < 0)
				Error(rig, "limits", "invalid sale, resource or stacking limits");

			// Require chassis tiers to be unique.
			if (root.chassis.GroupBy(c => c.tier).Any(g => g.Count() > 1))
				Error(rig, "chassis", "tiers must be unique");

			// Validate each chassis's dimensions, costs, and capacities.
			foreach (var chassis in root.chassis)
				// Require positive dimensions and nonnegative costs and capacities.
				if (chassis.rows < 1 || chassis.columns < 1 || chassis.tier < 1 || chassis.price < 0 ||
					chassis.requiredJobs < 0 || chassis.slots < 0 || chassis.storageCapacity < 0 ||
					chassis.memoryPool < 0 || chassis.powerCapacity < 0 || chassis.coolingCapacity < 0)
					Error(chassis, "dimensions/capacities",
						  "dimensions must be positive; costs and capacities nonnegative");

			// Validate each module's footprint, cost, family, and rules.
			foreach (var module in root.modules)
			{
				// Require positive footprints and tiers, and nonnegative costs and capacities.
				if (module.width < 1 || module.height < 1 || module.tier < 1 || module.price < 0 ||
					module.powerDemand < 0 || module.heatOutput < 0 || module.powerCapacity < 0 ||
					module.coolingCapacity < 0)
					Error(module, "footprint/budgets", "invalid footprint, cost, tier or capacity");

				// Require the module to fit at least one chassis, with optional rotation.
				if (!root.chassis.Any(c => (module.width <= c.columns && module.height <= c.rows) ||
										   (module.rotatable && rig.allowRotation && module.height <= c.columns &&
											module.width <= c.rows)))
					Error(module, "footprint", "module cannot fit any chassis");

				// Require a family name and a known branch on each module.
				if (string.IsNullOrWhiteSpace(module.family) ||
					!new[] { "RECON", "INTRUSION", "STEALTH", "CONTROL", "CROSSOVER" }.Contains(module.branch))
					Error(module, "family/branch", "family and a valid branch are required");

				// Require rule arrays to be non-null on each module.
				if (module.requirements == null || module.modifiers == null)
					Error(module, "rules", "rule arrays cannot be null");
			}

			// Require power and cooling tiers to be unique within their families.
			if (root.modules.GroupBy(m => (m.family, m.tier))
					.Any(g => (g.Count() > 1 && g.Key.family == "power") ||
							  (g.Count() > 1 && g.Key.family == "cooling")))
				Error(rig, "modules", "power and cooling tiers must be unique within their families");

			// Check the starter build against chassis and module capacities.
			if (rig.startingChassis != null)
			{
				// Track occupied grid cells and the running power and cooling totals.
				var cells = new HashSet<(int, int)>();
				float power = 0,
					  heat = 0,
					  pc = rig.startingChassis.powerCapacity,
					  cc = rig.startingChassis.coolingCapacity;

				// Place each starter part on the grid and total its power and heat.
				foreach (var part in rig.startingParts)
				{
					// Reject starter parts that have no module.
					if (part.module == null)
					{
						Error(rig, "startingParts", "module reference is required");
						continue;
					}

					// Measure the part's footprint, respecting its rotation.
					var size = GameSimulation.PartSize(part.module, part.rotated);

					// Reject rotation on parts that cannot rotate or when rotation is disabled.
					if (part.rotated && (!part.module.rotatable || !rig.allowRotation))
						Error(rig, "startingParts", "rotation is unavailable");

					// Mark each grid cell the part covers, rejecting overlaps and out-of-bounds cells.
					for (var y = part.row; y < part.row + size.y; y++)
						for (var x = part.column; x < part.column + size.x; x++)
							if (y < 0 || x < 0 || y >= rig.startingChassis.rows || x >= rig.startingChassis.columns ||
								!cells.Add((y, x)))
								Error(rig, "startingParts", "overlap or out-of-bounds placement");

					// Add the part's power draw, heat, and capacities to the running totals.
					power += part.module.powerDemand;
					heat += part.module.heatOutput;
					pc += part.module.powerCapacity;
					cc += part.module.coolingCapacity;

					// Require starter modules to use research that is already unlocked.
					if (part.module.requirements.Any(r => r.condition is ResearchCondition gate &&
														  !root.career.initialTechnologies.Contains(gate.technology)))
						Error(rig, "startingParts", "starter module research must be initially unlocked");
				}

				// Reject starter builds that exceed the chassis power or cooling capacity.
				if (power > pc || heat > cc)
					Error(rig, "startingParts", "starter build exceeds power or cooling");
			}

			// Read the career rules for limit and outcome checks.
			var rules = root.career;

			// Require sane career, trace, and retention limits.
			if (rules.credits < 0 || rules.xp < 0 || rules.day < 1 || rules.traceLimit <= 0 || rules.maxTick <= 0 ||
				rules.reportLimit < 1 || rules.logLimit < 1 || rules.traceWarning < 0 ||
				rules.traceWarning >= rules.traceDanger ||
				rules.traceDanger >= rules.traceLimit)
				Error(rules, "limits", "invalid career, trace or retention limits");

			// Require positive simulation speeds.
			if (rules.simulationSpeeds == null || rules.simulationSpeeds.Length == 0 ||
				rules.simulationSpeeds.Any(s => s <= 0))
				Error(rules, "simulationSpeeds", "speeds must be positive");

			// Require unique stat IDs.
			if (rules.stats.GroupBy(s => s.id).Any(g => g.Count() > 1))
				Error(rules, "stats", "duplicate stat ID");

			// Require one outcome policy per operation outcome, each with a verdict and a valid day advance.
			if (rules.outcomes == null || rules.outcomes.Length != Enum.GetValues(typeof(OperationOutcome)).Length ||
				rules.outcomes.Select(o => o.outcome).Distinct().Count() != rules.outcomes.Length ||
				rules.outcomes.Any(o => o.dayAdvance < 0 || string.IsNullOrEmpty(o.verdict)))
				Error(rules, "outcomes",
					  "each operation outcome needs one policy, a verdict and a nonnegative day advance");

			// Require refund fractions between zero and one.
			if (rules.refundFraction < 0 || rules.refundFraction > 1)
				Error(rules, "refundFraction", "refund must be between zero and one");

			// Reject negative reward values and caps.
			if (root.rewards.archiveCredits < 0 || root.rewards.archiveXp < 0 || root.rewards.archiveXpCap < 0 ||
				root.rewards.quietCredits < 0 ||
				root.rewards.quietXp < 0)
				Error(root.rewards, "rewards", "reward values and caps cannot be negative");

			return errors;
		}

		// Recursively visits object graphs to validate references, formulas, and rule shapes.
		private static void Walk(object obj, string path, HashSet<object> stack, int depth, bool root,
								 Action<object, string> visit, Action<string> cycle)
		{
			// Skip null graph nodes.
			if (obj == null)
				return;

			// Report this node to the visitor.
			visit(obj, path);

			// Resolve the runtime type of the value.
			var type = obj.GetType();

			// Stop at strings, value types, and nested definitions owned by other walks.
			if (obj is string || type.IsValueType || (!root && obj is GameplayDefinition))
				return;

			// Report cycles and excessive depth before descending further.
			if (depth > 64 || !stack.Add(obj))
			{
				cycle(path);
				return;
			}

			// Walk collection elements by index, or public fields for objects.
			if (obj is IEnumerable items)
			{
				// Visit each element with its index path.
				var i = 0;
				foreach (var item in items)
					Walk(item, path + "[" + i++ + "]", stack, depth + 1, false, visit, cycle);
			}
			else
			{
				// Visit each public instance field by name.
				foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
					Walk(field.GetValue(obj), path + "." + field.Name, stack, depth + 1, false, visit, cycle);
			}

			// Release this node from the current path so sibling branches can visit it.
			stack.Remove(obj);
		}
	}
}