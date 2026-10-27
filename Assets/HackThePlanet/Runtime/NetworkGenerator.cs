using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace HackThePlanet
{
	/// <summary>Runtime network instance produced from a contract and network template.</summary>
	public sealed class GeneratedNetwork
	{
		/// <summary>Ingress lane node id sequences from gateway to destination.</summary>
		public List<List<string>> approaches = new();

		/// <summary>Undirected links between generated nodes.</summary>
		public List<NetLink> links = new();

		/// <summary>Nodes with evaluated security, storage, and objective flags.</summary>
		public List<NetNode> nodes = new();
	}

	/// <summary>Builds playable network topology and node state from catalog templates and contract seeds.</summary>
	public static class NetworkGenerator
	{
		/// <summary>Generation algorithm version baked into save compatibility expectations.</summary>
		public const int Version = 4;

		/// <summary>Generates a network for a contract using <see cref="GameCatalog.Default" />.</summary>
		/// <param name="job">Resolved contract with seed and network id.</param>
		/// <param name="career">Career context for formulas and lore.</param>
		/// <returns>Nodes, links, and optional approach lanes for the operation.</returns>
		/// <exception cref="InvalidOperationException">The contract id is unknown in the catalog.</exception>
		public static GeneratedNetwork Generate(Contract job, Career career)
		{
			return Generate(job, career, GameCatalog.Default);
		}

		/// <summary>Generates a network from the contract's template, seed, and catalog definitions.</summary>
		/// <param name="job">Resolved contract with seed and network id.</param>
		/// <param name="career">Career context for formulas and lore.</param>
		/// <param name="catalog">Catalog supplying network templates and contracts.</param>
		/// <returns>Nodes, links, and optional approach lanes for the operation.</returns>
		/// <exception cref="InvalidOperationException">The contract id is unknown in the catalog.</exception>
		public static GeneratedNetwork Generate(Contract job, Career career, GameCatalog catalog)
		{
			// Resolve the contract definition for this network.
			var definition = catalog.Contract(job.id);

			// Reject contracts that are missing from the catalog.
			if (definition == null)
				throw new InvalidOperationException("Unknown network contract: " + job.id);

			// Resolve the network template, falling back to the contract's default network.
			var template = catalog.Network(job.networkId ?? definition.network.id);

			// Use saved lore when present, otherwise rebuild it from the contract.
			var lore = job.lore != null && job.lore.Length > 0
				? job.lore
				: catalog.DescribeContract(definition, career).lore;

			// Seed the random stream and rule context from the contract seed.
			var random = new Random(job.seed);
			var context = catalog.Context(career, job);
			context.Random = new Random(job.seed);

			// Create the empty result that nodes and links are added to.
			var result = new GeneratedNetwork();

			// Build a runtime node for each template node.
			foreach (var prototype in template.nodes)
			{
				// Copy the template fields onto a new runtime node.
				var node = new NetNode
				{
					id = prototype.id, label = prototype.label, typeId = prototype.type.id, kind = prototype.type.glyph,
					color = prototype.type.color,
					detail = string.IsNullOrEmpty(prototype.detail) ? prototype.type.description : prototype.detail,
					x = prototype.x, y = prototype.y, column = prototype.column, row = prototype.row,
					position = prototype.position, known = prototype.known, controlled = prototype.controlled,
					bonus = prototype.bonus
				};

				// Evaluate base security, then apply conditional modifiers in priority order.
				context.Node = node;
				var security = prototype.type.security.Evaluate(context);

				// Apply each matching security modifier.
				foreach (var modifier in prototype.securityModifiers.OrderBy(m => m.priority))
					if (modifier.when.Test(context))
						security = RuleMath.Apply(modifier.operation, security, modifier.value.Evaluate(context));

				// Store the rounded security on the node.
				node.security = Mathf.RoundToInt(security);

				// Bind lore to the node when the template references a lore index.
				if (prototype.loreIndex >= 0)
					node.loreId = prototype.loreIndex < lore.Length ? lore[prototype.loreIndex].id : null;

				// Add the finished node to the result.
				result.nodes.Add(node);
			}

			NetNode Find(string id)
			{
				return result.nodes.First(n => n.id == id);
			}

			// Mark each contract objective node.
			foreach (var objective in definition.objectives)
				Find(objective.nodeId).objective = true;

			// Authored layouts use the fixed template links.
			if (template.layout == NetworkLayout.Authored)
			{
				// Add each link whose condition passes for this instance.
				foreach (var edge in template.links)
					if (edge.when.Test(context))
						result.links.Add(new NetLink(edge.a, edge.b));

				// Size storage and return the authored network.
				SizeFiles(result, job, career, catalog, template);
				return result;
			}

			// Resolve the parallel-route tier for this difficulty.
			var config = template.routes;
			var difficulty = Mathf.Clamp(job.difficulty, 1, config.tiers.Length);
			var tier = config.tiers[difficulty - 1];
			var count = tier.nodeCount;

			// Copy the node type's glyph, color and description onto the node.
			void Type(NetNode node, NodeTypeDefinition type)
			{
				node.typeId = type.id;
				node.kind = type.glyph;
				node.color = type.color;
				node.detail = type.description;
			}

			// Create a node with default security, type it, and add it to the result.
			void Add(string id, NodeTypeDefinition type, string label)
			{
				var node = new NetNode { id = id, label = label, security = 1 };
				Type(node, type);
				result.nodes.Add(node);
			}

			// Add the fixed extra identities with their configured types and labels.
			for (var i = 0; i < config.extraIds.Length; i++)
				Add(config.extraIds[i], config.extraTypes[i], config.extraLabels[i]);

			// Fill the remaining node count with relay filler nodes.
			while (result.nodes.Count < count)
				Add(config.fillerPrefix + result.nodes.Count, config.relay,
					config.fillerLabel + (result.nodes.Count - config.fillerLabelOffset));

			// Reset every bonus flag, then read the archive ids from the template.
			foreach (var node in result.nodes)
				node.bonus = false;
			var archives = config.archives;

			// Mark each archive as a bonus node and bind its lore fragment.
			for (var i = 0; i < archives.Length; i++)
			{
				var node = Find(archives[i]);
				node.bonus = true;
				node.loreId = i < lore.Length ? lore[i].id : null;
			}

			// Track link keys so each pair of endpoints is added only once.
			var edges = new HashSet<string>();

			void Link(string a, string b)
			{
				// Build an order-independent key for the endpoint pair.
				var key = string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;

				// Add the link once, skipping self links and duplicates.
				if (a != b && edges.Add(key))
					result.links.Add(new NetLink(a, b));
			}

			// Collect reserved identities that stay out of the shuffled filler pool.
			var reserved = new HashSet<string>(config.reserved);

			// Shuffling the available identities varies topology while retaining contract identities.
			var pool = new Queue<NetNode>(result.nodes.Where(n => !reserved.Contains(n.id))
												.OrderBy(_ => random.Next()));

			// Read the per-lane hop counts and pick this run's rotation offset.
			var lengths = config.tiers.Select(t => t.lengths).ToArray();
			var rotation = (float)random.NextDouble() * Mathf.PI * 2;

			// Pin the gateway, entrance and destination to their template positions.
			Find(template.entryNode).position = config.entryPosition;
			Find(config.entrance).position = config.entrancePosition;
			Find(config.destination).position = config.destinationPosition;

			// Connect the gateway to the lane entrance.
			Link(template.entryNode, config.entrance);

			// Build one route per lane, walking from the entrance to the destination.
			for (var lane = 0; lane < lengths[difficulty - 1].Length; lane++)
			{
				// Size this lane and start its route at the gateway and entrance.
				var length = lengths[difficulty - 1][lane];
				var route = new List<string> { template.entryNode, config.entrance };

				// Walk each hop along the lane, assigning its node.
				for (var step = 0; step < length; step++)
				{
					// Take the direct-route node on lane zero, otherwise the next pooled node.
					var node = lane == 0 ? Find(config.directRoute[step]) : pool.Dequeue();

					// Speed comes from fewer systems; risk comes from persistent scanner exposure.
					if (lane > 0)
					{
						// Describe this hop to the rule context.
						context.Node = node;
						context.Step = step;
						context.Lane = lane;
						context.SafeLane = lane == lengths[difficulty - 1].Length - 1;

						// Assign the workstation or relay type from the placement rule.
						Type(node, config.workstationWhen.Test(context) ? config.workstation : config.relay);

						// Label the hop as maintenance or transit by lane and step.
						node.label =
							(lane == lengths[difficulty - 1].Length - 1 ? config.safeLaneLabel : config.transitLabel) +
							" " + lane + "." + (step + 1);

						// Set the hop security from the route formula.
						node.security = Mathf.RoundToInt(config.routeSecurity.Evaluate(context));
					}
					else
					{
						// Describe this direct-route hop to the rule context.
						context.Node = node;
						context.Step = step;
						context.Lane = lane;
						context.SafeLane = false;

						// Set the hop security from the direct-route formula.
						node.security = Mathf.RoundToInt(config.directSecurity.Evaluate(context));
					}

					// Copy the node type description onto this hop.
					node.detail = catalog.NodeType(node.typeId).description;

					// Place the hop on a curved arc between the entrance and destination.
					float t = (step + 1f) / (length + 1),
						  angle = rotation + lane * Mathf.PI * 2 / lengths[difficulty - 1].Length;
					var radius = config.radius + lane * config.laneRadius;
					node.position = new Vector3(Mathf.Lerp(config.entrancePosition.x, config.destinationPosition.x, t),
												Mathf.Cos(angle) * radius * Mathf.Sin(t * Mathf.PI),
												Mathf.Sin(angle) * radius * Mathf.Sin(t * Mathf.PI));
					node.position += new Vector3(Jitter(random), Jitter(random), Jitter(random)) * config.jitter;

					// Link this hop to the previous node on the route.
					Link(route.Last(), node.id);
					route.Add(node.id);
				}

				// Close the lane at the destination and record its approach.
				Link(route.Last(), config.destination);
				route.Add(config.destination);
				result.approaches.Add(route);
			}

			// Sensor leaves cannot become accidental traversal shortcuts. All four observe the fast ingress.
			var sensors = config.sensors;

			// Link each sensor leaf to the sensor attachment point.
			for (var i = 0; i < sensors.Length; i++)
				Link(config.sensorAttachment, sensors[i]);

			// Every secondary contract target has an approach from each independent lane.
			foreach (var id in config.secondaryTargets)
				foreach (var route in result.approaches)
					Link(route[route.Count - 2], id);

			// Crossovers connect only intermediate lanes, preserving the quiet and exposed extremes.
			if (result.approaches.Count > config.crossoverToLane + 1)
				Link(result.approaches[config.crossoverFromLane][config.crossoverStep],
					 result.approaches[config.crossoverToLane][config.crossoverStep]);

			// Attach a scanner sensor to each intermediate lane.
			for (var lane = 1; lane < result.approaches.Count - 1; lane++)
				for (var i = 0; i < tier.sensorsPerLane; i++)
				{
					// Take a pooled node and turn it into a scanner sensor.
					var sensor = pool.Dequeue();
					Type(sensor, config.scanner);
					sensor.label = config.sensorLabel + " " + lane + "." + (i + 1);
					sensor.detail = config.scanner.description;
					Link(result.approaches[lane][2], sensor.id);
				}

			// Connect each archive to an interior hop on a rotating lane.
			for (var i = 0; i < archives.Length; i++)
			{
				var route = result.approaches[(i + 1) % result.approaches.Count];
				Link(route[2 + i % (route.Count - 3)], archives[i]);
			}

			// Attach the remaining pooled nodes as workstations along random lanes.
			while (pool.Count > 0)
			{
				// Take the next pooled node and type it as a workstation.
				var node = pool.Dequeue();
				Type(node, config.workstation);
				node.security = 1;
				node.detail = config.workstation.description;

				// Pick a random lane and link the node to one of its interior hops.
				var route = result.approaches[1 + random.Next(result.approaches.Count - 1)];
				Link(route[2 + random.Next(route.Count - 3)], node.id);
			}

			// Place satellites around their connected sector, with deterministic minimum spacing.
			var placed = result.nodes
							   .Where(n => n.position != Vector3.zero || n.id == config.entrance ||
										   n.id == config.destination).ToList();

			// Position each unplaced node around its parent, retrying until it clears the spacing minimum.
			foreach (var node in result.nodes.Except(placed).ToArray())
			{
				// Find the link that connects this node to its parent.
				var edge = result.links.First(l => l.a == node.id || l.b == node.id);
				var parent = Find(edge.a == node.id ? edge.b : edge.a);

				// Try growing random offsets until the node clears the spacing minimum.
				for (var attempt = 0; attempt < config.placementAttempts; attempt++)
				{
					// Offset the node from its parent at a growing radius.
					var offset = new Vector3(Jitter(random), Jitter(random), Jitter(random));
					node.position = parent.position +
									offset.normalized * (config.satelliteRadius + attempt * config.satelliteGrowth);

					// Stop once the node clears every placed neighbor.
					if (placed.All(n => Vector3.Distance(n.position, node.position) >= config.minimumSpacing))
						break;
				}

				// Record the node as placed.
				placed.Add(node);
			}

			// Separate close neighbors without changing their sector or topology.
			for (var pass = 0; pass < config.separationPasses; pass++)
				for (var i = 0; i < result.nodes.Count; i++)
					for (var j = i + 1; j < result.nodes.Count; j++)
					{
						// Measure the gap between this node pair.
						var delta = result.nodes[j].position - result.nodes[i].position;

						// Skip pairs that are already far enough apart.
						if (delta.magnitude >= config.separationSpacing)
							continue;

						// Push the pair apart along their separating vector.
						var push = (delta.sqrMagnitude < .0001f ? Vector3.up : delta.normalized) *
								   (config.separationSpacing - delta.magnitude) * config.separationForce;
						result.nodes[i].position -= push;
						result.nodes[j].position += push;
					}

			// Compute payload and archive storage for the finished network.
			SizeFiles(result, job, career, catalog, template);
			return result;
		}

		// Assigns payload and archive storage sizes to each generated node from contract rules.
		private static void SizeFiles(GeneratedNetwork network, Contract job, Career career, GameCatalog catalog,
									  NetworkDefinition template)
		{
			// Resolve the contract definition for payload objectives.
			var definition = catalog.Contract(job.id);

			// Size storage for each node on the network.
			foreach (var node in network.nodes)
			{
				// Clear bonus flags on objective nodes.
				if (node.objective)
					node.bonus = false;

				// Flag the nodes that hold a payload objective.
				node.payload = definition.objectives.Any(o => o.nodeId == node.id && o.payload);

				// Build a rule context for this node.
				var context = catalog.Context(career, job);
				context.Node = node;
				context.Random = new Random(job.seed);

				// Set storage for payload and bonus archive nodes; others hold none.
				node.storage = node.payload || node.bonus
					? Mathf.Max(0, Mathf.Round(
									(node.payload ? template.payloadStorage : template.archiveStorage)
									.Evaluate(context)))
					: 0;
			}
		}

		// Returns a uniform random offset in the range [-1, 1] for layout jitter.
		private static float Jitter(Random random)
		{
			return (float)random.NextDouble() * 2 - 1;
		}

		// Version 1 saves deliberately remain on the old filename. No live network migration.
	}
}