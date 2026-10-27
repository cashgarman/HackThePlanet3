using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>One-time conversion helpers and rule builders for legacy gameplay ScriptableObject import.</summary>
	public static class DefaultGameplayAssets
	{
		/// <summary>Gets the Resources folder where imported gameplay definitions are created.</summary>
		public const string Folder = "Assets/HackThePlanet/Resources/HackThePlanet/Gameplay";

		// Creates a typed gameplay ScriptableObject asset on disk with the given id and title.
		private static T Asset<T>(string id, string title) where T : GameplayDefinition
		{
			// Create the ScriptableObject instance and fill in its id, title, and name.
			var value = ScriptableObject.CreateInstance<T>();
			value.id = id;
			value.title = title;
			value.name = id;

			// Save the instance as a gameplay asset named after its type and id.
			AssetDatabase.CreateAsset(value, Folder + "/" + typeof(T).Name + "-" + id + ".asset");

			return value;
		}

		/// <summary>Creates a constant numeric rule with the given value.</summary>
		/// <param name="x">Literal value.</param>
		/// <returns>A <see cref="ConstantNumber" /> instance.</returns>
		public static Number N(float x)
		{
			return new ConstantNumber(x);
		}

		/// <summary>Creates a numeric rule reading a simulation input.</summary>
		/// <param name="x">Input kind to read.</param>
		/// <param name="key">Optional counter, status, or hardware key.</param>
		/// <returns>An <see cref="InputNumber" /> instance.</returns>
		public static Number I(NumberInput x, string key = null)
		{
			return new InputNumber { input = x, key = key };
		}

		/// <summary>Creates a numeric rule reading a named career or operation stat.</summary>
		/// <param name="key">Stat identifier.</param>
		/// <returns>A <see cref="StatNumber" /> instance.</returns>
		public static Number S(string key)
		{
			return new StatNumber { stat = key };
		}

		/// <summary>Creates a binary numeric rule combining two operands.</summary>
		/// <param name="op">Arithmetic operation.</param>
		/// <param name="a">Left operand.</param>
		/// <param name="b">Right operand.</param>
		/// <returns>A <see cref="BinaryNumber" /> instance.</returns>
		public static Number B(Arithmetic op, Number a, Number b)
		{
			return new BinaryNumber { operation = op, left = a, right = b };
		}

		/// <summary>Creates a condition testing a node state flag.</summary>
		/// <param name="s">State to require.</param>
		/// <returns>A <see cref="NodeStateCondition" /> instance.</returns>
		public static Condition State(NodeState s)
		{
			return new NodeStateCondition { state = s };
		}

		/// <summary>Creates a condition requiring a semantic node tag.</summary>
		/// <param name="s">Tag name.</param>
		/// <returns>A <see cref="NodeTagCondition" /> instance.</returns>
		public static Condition Tag(string s)
		{
			return new NodeTagCondition { tag = s };
		}

		/// <summary>Negates another condition.</summary>
		/// <param name="c">Inner condition.</param>
		/// <returns>A <see cref="NotCondition" /> instance.</returns>
		public static Condition Not(Condition c)
		{
			return new NotCondition { condition = c };
		}

		/// <summary>Combines conditions with logical AND.</summary>
		/// <param name="c">Child conditions evaluated in order.</param>
		/// <returns>An <see cref="AllCondition" /> instance.</returns>
		public static Condition All(params Condition[] c)
		{
			return new AllCondition { conditions = c };
		}

		/// <summary>Combines conditions with logical OR.</summary>
		/// <param name="c">Child conditions evaluated in order.</param>
		/// <returns>An <see cref="AnyCondition" /> instance.</returns>
		public static Condition Any(params Condition[] c)
		{
			return new AnyCondition { conditions = c };
		}

		/// <summary>Compares two numeric rules with the given relation.</summary>
		/// <param name="a">Left operand.</param>
		/// <param name="op">Comparison operator.</param>
		/// <param name="b">Right operand.</param>
		/// <returns>A <see cref="CompareCondition" /> instance.</returns>
		public static Condition Compare(Number a, Comparison op, Number b)
		{
			return new CompareCondition { left = a, comparison = op, right = b };
		}

		/// <summary>Selects between two numeric rules based on a condition.</summary>
		/// <param name="when">Condition controlling the choice.</param>
		/// <param name="yes">Value when the condition passes.</param>
		/// <param name="no">Value when the condition fails.</param>
		/// <returns>A <see cref="ConditionalNumber" /> instance.</returns>
		public static Number Choose(Condition when, Number yes, Number no)
		{
			return new ConditionalNumber { when = when, yes = yes, no = no };
		}

		/// <summary>Creates a stat modifier rule with optional gating condition and priority.</summary>
		/// <param name="stat">Stat identifier to adjust.</param>
		/// <param name="op">Arithmetic operation applied to the stat.</param>
		/// <param name="value">Operand value or formula.</param>
		/// <param name="when">Optional condition; defaults to always true.</param>
		/// <param name="priority">Application order among modifiers.</param>
		/// <returns>A configured <see cref="Modifier" />.</returns>
		public static Modifier Mod(string stat, Arithmetic op, Number value, Condition when = null, int priority = 0)
		{
			return new Modifier
			{
				stat = stat, operation = op, value = value, when = when ?? new AlwaysCondition(), priority = priority
			};
		}

		/// <summary>Creates a player-facing requirement pairing a condition with failure copy.</summary>
		/// <param name="c">Condition that must pass.</param>
		/// <param name="message">Message shown when the condition fails.</param>
		/// <returns>A <see cref="Requirement" /> instance.</returns>
		public static Requirement Req(Condition c, string message)
		{
			return new Requirement { condition = c, message = message };
		}

		/// <summary>Creates a condition that matches the selected or running action.</summary>
		/// <param name="a">Action definition to match.</param>
		/// <returns>An <see cref="ActionCondition" /> instance.</returns>
		public static Condition Action(ActionDefinition a)
		{
			return new ActionCondition { action = a };
		}

		/// <summary>Creates a log effect with the given message template.</summary>
		/// <param name="message">Message written to the operation log.</param>
		/// <returns>A <see cref="LogEffect" /> instance.</returns>
		public static Effect Log(string message)
		{
			return new LogEffect { message = message };
		}

		/// <summary>Creates a node selector with optional filter condition.</summary>
		/// <param name="scope">Selection scope for targets.</param>
		/// <param name="filter">Optional filter; defaults to always true.</param>
		/// <returns>A <see cref="NodeSelector" /> instance.</returns>
		public static NodeSelector Select(SelectionScope scope, Condition filter = null)
		{
			return new NodeSelector { scope = scope, filter = filter ?? new AlwaysCondition() };
		}

		/// <summary>Imports baseline gameplay assets from Artifacts/gameplay-baseline.json when Game.asset is absent.</summary>
		/// <exception cref="InvalidOperationException">Game.asset already exists.</exception>
		public static void Import()
		{
			// Stop if the root asset exists, so authored content is never overwritten.
			if (AssetDatabase.LoadAssetAtPath<GameDefinition>(Folder + "/Game.asset") != null)
				throw new InvalidOperationException(
					"Game.asset already exists. Edit the existing assets; importing must never overwrite authored content.");

			// Make sure the gameplay folder exists and is visible to Unity.
			Directory.CreateDirectory(Folder);
			AssetDatabase.Refresh();

			// Read the captured baseline snapshot from disk.
			var baseline = JsonUtility.FromJson<GameplaySnapshot>(File.ReadAllText("Artifacts/gameplay-baseline.json"));

			// Create the root game definition and fill in its identity.
			var root = ScriptableObject.CreateInstance<GameDefinition>();
			root.id = "game";
			root.title = "Hack The Planet";
			root.name = "Game";

			// Save the root game asset.
			AssetDatabase.CreateAsset(root, Folder + "/Game.asset");

			// Create one company asset for each baseline company name.
			root.companies = baseline.companies.Select((name, i) => Asset<CompanyDefinition>("company-" + i, name))
									 .ToArray();

			// Create a node type asset for each node kind.
			root.nodeTypes = Enum.GetValues(typeof(NodeKind)).Cast<NodeKind>().Select(kind =>
			{
				// Create the node type asset from the kind's name.
				var type = Asset<NodeTypeDefinition>(kind.ToString().ToLowerInvariant(), kind.ToString());

				// Set the glyph, color, tag, and description for this node type.
				type.glyph = kind;
				type.color = Palette.Kind(kind);
				type.tags = new[] { type.id };
				type.description = baseline.prototypes.First(n => n.kind == kind).detail;

				// Pick the security rule: gateways are open, firewalls scale with difficulty, relays are low, others roll 1-2.
				type.security = kind == NodeKind.Gateway ? N(0) :
					kind == NodeKind.Firewall ? B(Arithmetic.Add, N(3), I(NumberInput.Difficulty)) :
					kind == NodeKind.Relay ? N(1) : new RandomNumber { minimum = 1, maximum = 2 };

				return type;
			}).ToArray();

			NodeTypeDefinition Type(NodeKind glyph)
			{
				return root.nodeTypes.First(t => t.glyph == glyph);
			}

			// Define the two defense statuses: suppression disables defenses and protection blocks trace.
			var offline = Asset<StatusDefinition>("suppression", "Defense suppression");
			offline.disablesDefense = true;
			var protection = Asset<StatusDefinition>("trace-protection", "Trace protection");
			protection.preventsTrace = true;

			// Register both statuses on the root game asset.
			root.statuses = new[] { offline, protection };

			// Collect every action asset as it is created.
			var actions = new List<ActionDefinition>();

			// Create an action asset with its shortcut, timing, trace, and default requirements.
			ActionDefinition Make(string id, string title, KeyCode key, string keyLabel, float duration, float trace,
								  Condition visible = null)
			{
				// Create the action asset and set its identity and shortcut.
				var a = Asset<ActionDefinition>(id, title);
				a.legacyGroup = id;
				a.shortcut = key;
				a.shortcutLabel = keyLabel;

				// Set the duration, trace cost, and visibility rule.
				a.duration = N(duration);
				a.baseTrace = N(trace);
				a.visible = visible ?? new AlwaysCondition();
				a.tags = new[] { id };

				// Require the target to be discovered and reachable from a controlled neighbor.
				a.requirements = new[]
				{
					Req(State(NodeState.Known), "Scan to discover this system."),
					Req(State(NodeState.Reachable), "Control a connected neighbor first.")
				};

				// Require discovery before the action can be queued.
				a.queueRequirements = new[] { Req(State(NodeState.Known), "Discover this system first.") };

				// Track the action in the shared list and return it.
				actions.Add(a);
				return a;
			}

			// Create the scan action, available from the start.
			var scan = Make("scan", "Scan connections", KeyCode.Alpha1, "1", 3, 2);
			scan.initiallyAvailable = true;

			// Scanning is only allowed on systems that have not been scanned yet.
			scan.requirements = scan.requirements.Concat(new[]
			{
				Req(Not(State(NodeState.Scanned)), "Already scanned. Its surrounding connections are known.")
			}).ToArray();

			// On completion, mark the target scanned and reveal the surrounding routes.
			scan.effects = new[]
			{
				new SetStateEffect { state = NodeState.Scanned }, new RevealEffect { radius = S("scanRadius") },
				Log("SCAN COMPLETE // New routes mapped around {target}.")
			};

			// Create the hack action, available from the start and allowed to be queued ahead.
			var hack = Make("hack", "Hack system", KeyCode.Alpha2, "2", 1, 1);
			hack.initiallyAvailable = true;
			hack.futureNeighborQueue = true;

			// Block hacking systems that are already controlled, for both direct and queued use.
			hack.requirements = hack.requirements
									.Concat(new[]
									{
										Req(Not(State(NodeState.Controlled)), "You already control this system.")
									}).ToArray();

			hack.queueRequirements = hack.queueRequirements
										 .Concat(new[]
										 {
											 Req(Not(State(NodeState.Controlled)), "You already control this system.")
										 }).ToArray();

			// Work out the hack cost from the target's security level.
			Number HackCost()
			{
				return B(Arithmetic.Add, N(3), B(Arithmetic.Multiply, I(NumberInput.Security), N(1.7f)));
			}

			// Use the hack cost for both the duration and the trace.
			hack.duration = B(Arithmetic.Divide, HackCost(), S("compute"));
			hack.baseTrace = HackCost();

			// Build the effects of taking control: mark it controlled and reveal the connected routes.
			Effect[] ControlEffects()
			{
				return new[]
				{
					new SetStateEffect { state = NodeState.Controlled },
					new RevealEffect { radius = S("revealRadius") }, Log("CONTROL ESTABLISHED // {target}.")
				};
			}

			// Give hack the control effects.
			hack.effects = ControlEffects();

			// Create the download action, which works on objectives and optional archives.
			var download = Make("download", "Download files", KeyCode.Alpha3, "3", 1, 1,
								Any(State(NodeState.Objective), State(NodeState.Bonus)));

			download.initiallyAvailable = true;

			// Require a controlled node, an uncollected payload, and a non-hub target.
			download.requirements = download.requirements.Concat(new[]
			{
				Req(State(NodeState.Controlled), "Hack this system before recovering its files."),
				Req(Not(State(NodeState.Collected)), "Files already secured."),
				Req(Any(State(NodeState.Objective), State(NodeState.Bonus)),
					"No contract files or optional archives on this node."),
				Req(Not(Tag("securityhub")), "Control the hub to satisfy this objective.")
			}).ToArray();

			// Scale download time with node storage, falling back to objective or archive defaults.
			download.duration = B(Arithmetic.Divide,
								  Choose(Compare(I(NumberInput.NodeStorage), Comparison.Greater, N(0)),
										 I(NumberInput.NodeStorage), Choose(State(NodeState.Objective), N(9), N(7))),
								  S("bandwidth"));

			// Objectives cost more trace than optional archives.
			download.baseTrace = Choose(State(NodeState.Objective), N(8), N(6));

			// Collect the payload, then log whether it was an objective or an optional archive.
			download.effects = new Effect[]
			{
				new SetStateEffect { state = NodeState.Collected },
				new ConditionalEffect
					{ when = State(NodeState.Objective), effects = new[] { Log("OBJECTIVE SECURED // {target}.") } },
				new ConditionalEffect
				{
					when = Not(State(NodeState.Objective)),
					effects = new[]
						{ Log("OPTIONAL ARCHIVE // +{archiveCredits} cr / +{archiveXp} XP on successful extraction.") }
				},
				new RecoverLoreEffect()
			};

			// Create the disrupt and blackout actions, which share a shortcut key.
			var disrupt = Make("disrupt", "Disrupt scanner", KeyCode.Alpha4, "4", 2.5f, 2, Tag("scanner"));
			var blackout = Make("blackout", "Blackout hub", KeyCode.Alpha4, "4", 2.5f, 2, Tag("securityhub"));
			blackout.legacyGroup = "disrupt";

			// Give both suppression actions the same defense requirement and suppression effect.
			foreach (var a in new[] { disrupt, blackout })
			{
				// Require the target defense to still be active.
				a.requirements = a.requirements
								  .Concat(new[]
								  {
									  Req(State(NodeState.DefenseActive), "This defense is already offline.")
								  }).ToArray();

				// Suppress the defense for 60 seconds and log it.
				a.effects = new[]
				{
					new ApplyStatusEffect { status = offline, duration = N(60) }, Log("DEFENSE SUPPRESSED // {target}.")
				};
			}

			// Create the bridge action: a firewall that needs two controlled neighbors and costs no trace.
			var bridge = Make("bridge", "Bridge firewall", KeyCode.B, "B", 2, 0, Tag("firewall"));
			bridge.traceFree = true;

			// Require an uncontrolled target and at least two controlled neighbors.
			bridge.requirements = bridge.requirements.Concat(new[]
			{
				Req(Not(State(NodeState.Controlled)), "You already control this system."),
				Req(
					Compare(new InputNumber { input = NumberInput.NeighborCount, filter = State(NodeState.Controlled) },
							Comparison.GreaterOrEqual, N(2)), "Requires a firewall with two controlled neighbors.")
			}).ToArray();

			// Bridge takes control of the target when it completes.
			bridge.effects = ControlEffects();

			// Create the backdoor action, which can be used once per operation at no trace cost.
			var backdoor = Make("backdoor", "Use backdoor", KeyCode.R, "R", 1, 0,
								Any(Tag("workstation"), Tag("relay")));

			backdoor.traceFree = true;
			backdoor.useLimit = N(1);

			// Require a discovered, uncontrolled target.
			backdoor.requirements = new[]
			{
				Req(State(NodeState.Known), "Discover this system first."),
				Req(Not(State(NodeState.Controlled)), "You already control this system.")
			};

			// Backdoor takes control of the target when it completes.
			backdoor.effects = ControlEffects();

			// Create the decoy action, an operator-only process that cuts trace and is not queueable.
			var decoy = Make("decoy", "Decoy process", KeyCode.None, "", 0, 0);
			decoy.operatorTarget = true;
			decoy.queueable = false;
			decoy.traceFree = true;
			decoy.useLimit = N(1);

			// Decoy has no target requirements.
			decoy.requirements = Array.Empty<Requirement>();

			// Decoy lowers trace and applies trace protection to the operator.
			decoy.effects = new[]
			{
				new TraceEffect { amount = N(-15) },
				new ApplyStatusEffect { operatorStatus = true, status = protection, duration = N(20) },
				Log("DECOY ACTIVE // Trace reduced. Trace growth suspended.")
			};

			// Register the full action list on the root asset.
			root.actions = actions.ToArray();

			// Create a technology asset for each baseline technology and copy its fields.
			root.technologies = baseline.technologies.Select(t =>
			{
				// Create the technology asset and copy its baseline fields.
				var d = Asset<TechnologyDefinition>(t.id, t.title);
				d.description = t.description;
				d.branch = t.branch;
				d.cost = t.cost;
				d.special = t.special;
				d.position = t.position;
				return d;
			}).ToArray();

			// Look up a technology definition by id.
			TechnologyDefinition Tech(string id)
			{
				return root.technologies.First(t => t.id == id);
			}

			// Link each technology to its parent technologies.
			foreach (var t in baseline.technologies)
				Tech(t.id).parents = t.parents.Select(Tech).ToArray();

			// Start the waypoint chain from the base technology.
			var waypointParent = Tech("base");

			// List the waypoint tier descriptions in order.
			string[] waypointDescriptions =
			{
				"Place one waypoint before connecting; its marker remains visible before discovery.",
				"Place two waypoints and change their colours.", "Place three waypoints and add text labels.",
				"Place four waypoints and show one shortest gateway route through undiscovered systems."
			};

			// Create the four waypoint tiers, chaining each to the previous one.
			for (var tier = 1; tier <= 4; tier++)
			{
				// Create the waypoint tier asset with its title.
				var waypoint = Asset<TechnologyDefinition>("waypoints-" + tier,
														   "Waypoints " + new[] { "I", "II", "III", "IV" }[tier - 1]);

				// Set the branch, cost, tier, position, and description for this tier.
				waypoint.branch = "RECON";
				waypoint.cost = tier * 30;
				waypoint.waypointTier = tier;
				waypoint.position = new Vector2(220 + (tier - 1) * 180, -75);
				waypoint.description = waypointDescriptions[tier - 1];

				// Make this tier depend on the previous one, then add it to the tree.
				waypoint.parents = new[] { waypointParent };
				waypointParent = waypoint;
				root.technologies = root.technologies.Concat(new[] { waypoint }).ToArray();
			}

			// Grant each research-unlocked action to its technology.
			foreach (var pair in new (TechnologyDefinition tech, ActionDefinition action)[]
					 {
						 (Tech("disrupt"), disrupt), (Tech("blackout"), blackout), (Tech("bridge"), bridge),
						 (Tech("backdoor"), backdoor), (Tech("decoy"), decoy)
					 })
				pair.tech.grants = new[] { pair.action };

			// Show bridge and backdoor only once their research is equipped.
			bridge.visible = All(Tag("firewall"),
								 new ResearchCondition { technology = Tech("bridge"), equipped = true });

			backdoor.visible = All(Any(Tag("workstation"), Tag("relay")),
								   new ResearchCondition { technology = Tech("backdoor"), equipped = true });

			// Show decoy once its research is equipped.
			decoy.visible = new ResearchCondition { technology = Tech("decoy"), equipped = true };

			// Wide scan widens the scan radius.
			Tech("wide").modifiers = new[] { Mod("scanRadius", Arithmetic.Override, N(3)) };

			// Signal halves scan time and removes scan trace.
			Tech("signal").modifiers = new[]
			{
				Mod("duration", Arithmetic.Multiply, N(.5f), Action(scan)),
				Mod("baseTrace", Arithmetic.Override, N(0), Action(scan))
			};

			// Efficient hack shortens hack time.
			Tech("efficient").modifiers = new[] { Mod("duration", Arithmetic.Multiply, N(.75f), Action(hack)) };

			// Chain raises the queue capacity and enables future queueing.
			Tech("chain").modifiers = new[]
				{ Mod("queueCapacity", Arithmetic.Override, N(3)), Mod("futureQueue", Arithmetic.Override, N(1)) };

			// Selective shortens and reduces trace on objective downloads.
			Tech("selective").modifiers = new[]
			{
				Mod("duration", Arithmetic.Multiply, N(.55f), All(Action(download), State(NodeState.Objective))),
				Mod("baseTrace", Arithmetic.Multiply, N(.7f), All(Action(download), State(NodeState.Objective)))
			};

			// Quiet reduces hack trace.
			Tech("quiet").modifiers = new[] { Mod("baseTrace", Arithmetic.Multiply, N(.7f), Action(hack)) };

			// Ghost makes hacks trace-free when they target a relay with no relay controls yet.
			Tech("ghost").modifiers = new[]
			{
				Mod("traceFree", Arithmetic.Override, N(1),
					All(Action(hack), Tag("relay"),
						Compare(I(NumberInput.Counter, "relay-controls"), Comparison.Equal, N(0))))
			};

			// Passive extends the reveal radius around relays.
			Tech("passive").modifiers = new[] { Mod("revealRadius", Arithmetic.Override, N(3), Tag("relay")) };

			// Weakpoint reduces trace on scanned hacks.
			Tech("weakpoint").modifiers = new[]
				{ Mod("baseTrace", Arithmetic.Multiply, N(.6f), All(Action(hack), State(NodeState.Scanned))) };

			// Buffer reduces overall trace.
			Tech("buffer").modifiers = new[] { Mod("trace", Arithmetic.Multiply, N(.85f)) };

			// Blueprint reveals the objectives when an operation starts.
			Tech("blueprint").triggers = new[]
			{
				new Trigger
				{
					on = RuleEvent.OperationStarted,
					effects = new Effect[]
					{
						new RevealEffect
							{ targets = Select(SelectionScope.All, State(NodeState.Objective)), radius = N(1) }
					}
				}
			};

			// Cascade reduces firewall security once a security hub is taken down.
			Tech("cascade").triggers = new[]
			{
				new Trigger
				{
					on = RuleEvent.ActionCompleted, onceKey = "cascade",
					when = All(Tag("securityhub"), Not(State(NodeState.DefenseActive))),
					effects = new[]
					{
						new SecurityEffect { targets = Select(SelectionScope.All, Tag("firewall")), amount = N(-2) },
						Log("SYSTEM CASCADE // Firewall security reduced across the network.")
					}
				}
			};

			// Swap the hard-coded percentages in the descriptions for live modifier values.
			Tech("efficient").description = Tech("efficient").description.Replace("25%", "{modifier:0:reduction}%");
			Tech("quiet").description = Tech("quiet").description.Replace("30%", "{modifier:0:reduction}%");
			Tech("weakpoint").description = Tech("weakpoint").description.Replace("40%", "{modifier:0:reduction}%");
			Tech("buffer").description = Tech("buffer").description.Replace("15%", "{modifier:0:reduction}%");

			Tech("selective").description = Tech("selective").description.Replace("55%", "{modifier:0:percent}%")
															 .Replace("30%", "{modifier:1:reduction}%");

			// Register the root triggers: reveal the entry on start and count relay controls.
			root.triggers = new[]
			{
				new Trigger
				{
					on = RuleEvent.OperationStarted,
					effects = new Effect[]
						{ new RevealEffect { targets = Select(SelectionScope.Entry), radius = N(1) } }
				},
				new Trigger
				{
					on = RuleEvent.ControlEstablished, when = Tag("relay"),
					effects = new Effect[] { new CounterEffect { key = "relay-controls" } }
				}
			};

			// Scanners add trace pressure while they neighbor controlled systems.
			Type(NodeKind.Scanner).auras = new[]
			{
				new Aura
				{
					id = "scanner-pressure", stack = true,
					source = All(State(NodeState.DefenseActive),
								 Compare(
									 new InputNumber
										 { input = NumberInput.NeighborCount, filter = State(NodeState.Controlled) },
									 Comparison.Greater, N(0))),
					modifier = Mod("tracePressure", Arithmetic.Add, N(.18f))
				}
			};

			// Security hubs raise the security of adjacent firewalls and databases.
			Type(NodeKind.SecurityHub).auras = new[]
			{
				new Aura
				{
					id = "hub-protection", adjacentOnly = true, source = State(NodeState.DefenseActive),
					modifier = Mod("security", Arithmetic.Add, N(2), Any(Tag("firewall"), Tag("database")))
				}
			};

			// Create the corporate network asset that holds the layout and node placement.
			var network = Asset<NetworkDefinition>("corporate-network", "Corporate infrastructure");

			// Set the layout, entry point, preferred node, and preview node of the network.
			network.layout = NetworkLayout.ParallelRoutes;
			network.entryNode = "home";
			network.preferredNode = "n2";
			network.previewNode = "n22";

			// Build each network node from its baseline prototype, adding security modifiers for non-home nodes.
			network.nodes = baseline.prototypes.Select(n => new NetworkNode
			{
				id = n.id, label = n.label,
				detail = n.kind == NodeKind.Gateway ? n.detail : null,
				type = Type(n.kind), column = n.column, row = n.row, x = n.x,
				y = n.y, known = n.known, controlled = n.controlled,
				securityModifiers = n.id == "home"
					? Array.Empty<Modifier>()
					: new[]
					{
						// Security rises with difficulty and column depth.
						Mod("security", Arithmetic.Add,
							B(Arithmetic.Subtract, I(NumberInput.Difficulty),
							  N(1)),
							Compare(I(NumberInput.Column),
									Comparison.GreaterOrEqual, N(4))),

						// Firewalls gain extra security after the company has won a contract.
						Mod("security", Arithmetic.Add, N(1),
							All(Tag("firewall"),
								Compare(I(NumberInput.CompanyWins),
										Comparison.Greater, N(0))))
					}
			}).ToArray();

			// Get the route settings that shape the generated paths.
			var r = network.routes;

			// Set the entrance, destination, sensor attachment, and the node lists for each route.
			r.entrance = "n2";
			r.destination = "n22";
			r.sensorAttachment = "n6";
			r.directRoute = new[] { "n6", "n10", "n14", "n18" };
			r.sensors = new[] { "n5", "n11", "watch-a", "watch-b" };
			r.secondaryTargets = new[] { "n9", "n19", "n20" };
			r.archives = new[] { "n13", "n21", "archive-c" };

			// Reserve the fixed nodes and archives so the generator does not place other content on them.
			r.reserved = new[]
			{
				"home", "n2", "n22", "n9", "n19", "n20", "n6", "n10", "n14", "n18", "n5", "n11", "watch-a", "watch-b"
			}.Concat(r.archives).ToArray();

			// Bind node types to the route roles and define the extra nodes added to the network.
			r.relay = Type(NodeKind.Relay);
			r.workstation = Type(NodeKind.Workstation);
			r.scanner = Type(NodeKind.Scanner);
			r.archive = Type(NodeKind.Database);
			r.extraIds = new[] { "watch-a", "watch-b", "archive-c" };
			r.extraTypes = new[] { r.scanner, r.scanner, r.archive };
			r.extraLabels = new[] { "Ingress sensor", "Deep packet sensor", "Sealed correspondence" };

			// Define the three network size tiers with their node counts and lane lengths.
			r.tiers = new[]
			{
				new RouteTier { nodeCount = 32, lengths = new[] { 4, 6, 8 }, sensorsPerLane = 1 },
				new RouteTier { nodeCount = 44, lengths = new[] { 4, 5, 6, 8 }, sensorsPerLane = 2 },
				new RouteTier { nodeCount = 56, lengths = new[] { 4, 5, 6, 7, 9 }, sensorsPerLane = 2 }
			};

			// Work out the direct route's security from the step, firewalls, and company wins.
			r.directSecurity = B(Arithmetic.Add,
								 Choose(Compare(I(NumberInput.Step), Comparison.Equal, N(0)), N(1), N(2)),
								 Choose(Tag("firewall"),
										B(Arithmetic.Add, B(Arithmetic.Subtract, I(NumberInput.Difficulty), N(1)),
										  Choose(Compare(I(NumberInput.CompanyWins), Comparison.Greater, N(0)), N(1),
												 N(0))), N(0)));

			// Work out the safe lane security, which alternates with the step count.
			r.routeSecurity = Choose(Compare(I(NumberInput.SafeLane), Comparison.Equal, N(1)), N(1),
									 B(Arithmetic.Add, N(1), B(Arithmetic.Modulo, I(NumberInput.Step), N(2))));

			// Place workstations on every third step.
			r.workstationWhen = Compare(B(Arithmetic.Modulo, I(NumberInput.Step), N(3)), Comparison.Equal, N(1));

			// Register the network on the root game asset.
			root.networks = new[] { network };

			// Create a contract asset for each baseline contract.
			root.contracts = baseline.contracts.Select((job, index) =>
			{
				// Create the contract asset from the baseline id and title.
				var d = Asset<ContractDefinition>(job.id, job.title);

				// Copy the descriptive text, company, network, and tag fields onto the contract.
				d.description = job.description;
				d.company = root.companies[Array.IndexOf(baseline.companies, job.company)];
				d.network = network;
				d.client = job.client;
				d.tag = job.tag;
				d.objective = job.objective;
				d.condition = job.condition;
				d.aftermath = job.aftermath;
				d.displayKind = job.kind;
				d.recurring = job.recurring;
				d.requiredWins = job.requiredWins;
				d.quietLimit = job.quietLimit;

				// Pay recurring contracts from a day-scaled credit formula, others from their fixed amount.
				d.credits = job.recurring
					? B(Arithmetic.Add, N(600), B(Arithmetic.Multiply, I(NumberInput.Day), N(35)))
					: N(job.credits);

				// Set the XP reward.
				d.xp = N(job.xp);

				// Seed recurring contracts from the day and index, others from their fixed seed.
				d.seed = job.recurring
					? B(Arithmetic.Add, B(Arithmetic.Multiply, I(NumberInput.Day), N(71)), N((index - 6) * 37))
					: N(job.seed);

				// Scale recurring difficulty with the day, clamped between 1 and 3.
				d.difficulty = job.recurring
					? B(Arithmetic.Minimum, N(3),
						B(Arithmetic.Maximum, N(1),
						  B(Arithmetic.Add, N(1),
							B(Arithmetic.Floor, B(Arithmetic.Divide, I(NumberInput.Day), N(5)), N(0)))))
					: N(job.difficulty);

				// Start with the main objective on n22, labelled by the contract kind.
				var objectives = new List<ObjectiveDefinition>
				{
					new()
					{
						nodeId = "n22", action = download,
						actionLabel = job.kind == JobKind.Alter ? "Correct record" :
							job.kind == JobKind.Investigate ? "Recover evidence" :
							job.kind == JobKind.Disrupt ? "Erase targeting index" : "Download files",
						complete = State(NodeState.Collected)
					}
				};

				// Alter contracts also require the correction on n20.
				if (job.kind == JobKind.Alter)
					objectives.Add(new ObjectiveDefinition
					{
						nodeId = "n20", action = download, actionLabel = "Correct record",
						complete = State(NodeState.Collected)
					});

				// Investigate contracts also require recovering evidence from n9 and n19.
				if (job.kind == JobKind.Investigate)
					foreach (var id in new[] { "n9", "n19" })
						objectives.Add(new ObjectiveDefinition
						{
							nodeId = id, action = download, actionLabel = "Recover evidence",
							complete = State(NodeState.Collected)
						});

				// Disrupt contracts require taking control of the security hub, then erasing the index.
				if (job.kind == JobKind.Disrupt)
				{
					// Add the control-the-hub objective on n14.
					objectives.Add(new ObjectiveDefinition
									   { nodeId = "n14", action = hack, complete = State(NodeState.Controlled) });

					// Block the download until the hub on n14 is controlled.
					d.actionRules = new[]
					{
						new ContractActionRule
						{
							action = download,
							requirements = new[]
							{
								Req(new NamedNodeCondition { nodeId = "n14", condition = State(NodeState.Controlled) },
									"Take control of Security hub before erasing the index.")
							}
						}
					};
				}

				// Save the objectives and give the contract its share of the lore fragments.
				d.objectives = objectives.ToArray();
				d.lore = baseline.lore.Skip(index * 3).Take(3).ToArray();

				return d;
			}).ToArray();

			// Create the three hardware categories: CPU, bandwidth, and memory.
			root.hardware = new[]
			{
				Asset<HardwareDefinition>("cpu", "Parallel processor"),
				Asset<HardwareDefinition>("bandwidth", "Fiber uplink"),
				Asset<HardwareDefinition>("memory", "Memory expansion")
			};

			// Name the stat, category, and label for each hardware item, matched by index.
			string[] statNames = { "compute", "bandwidth", "slots" },
					 categories = { "PROCESSING", "BANDWIDTH", "ABILITY CAPACITY" },
					 labels = { "COMPUTE", "BANDWIDTH", "ABILITY SLOTS" };

			// Configure each hardware item's labels, description, tiers, and modifiers.
			for (var i = 0; i < root.hardware.Length; i++)
			{
				// Set the category, label, and displayed stat for this hardware.
				var h = root.hardware[i];
				h.category = categories[i];
				h.statLabel = labels[i];
				h.displayedStat = statNames[i];

				// Describe the memory expansion differently from the performance upgrades.
				h.description = i == 2
					? "Additional room for equipped special abilities."
					: "Improves performance for every operation.";

				// Build the purchase tiers, with memory having two tiers and the others three.
				h.tiers = (i == 2 ? new[] { 600, 1400 } : new[] { 400, 900, 1600 })
						  .Select(price => new HardwareTier
						  {
							  price = price,
							  modifiers = new[]
								  { Mod(statNames[i], Arithmetic.Add, N(i == 0 ? .2f : i == 1 ? .3f : 1)) }
						  }).ToArray();
			}

			// Create the delivery reward asset and its reward rules.
			root.rewards = Asset<RewardDefinition>("delivery", "Delivery rewards");

			// Define the three reward rules: base delivery, bonus archives, and a quiet-run bonus.
			root.rewards.rules = new[]
			{
				new RewardRule { credits = I(NumberInput.ContractCredits), xp = I(NumberInput.ContractXp) },
				new RewardRule
				{
					credits = B(Arithmetic.Multiply, I(NumberInput.BonusFiles), I(NumberInput.ArchiveCredits)),
					xp = B(Arithmetic.Minimum, I(NumberInput.ArchiveXpCap),
						   B(Arithmetic.Multiply, I(NumberInput.BonusFiles), I(NumberInput.ArchiveXp)))
				},
				new RewardRule
				{
					when = Compare(I(NumberInput.Trace), Comparison.Less, I(NumberInput.QuietLimit)),
					credits = I(NumberInput.QuietCredits), xp = I(NumberInput.QuietXp)
				}
			};

			// Create the career rules asset and set its starting technology and welcome message.
			root.career = Asset<CareerDefinition>("career", "Career rules");
			root.career.initialTechnologies = new[] { Tech("base") };

			root.career.welcome =
				"EXCHANGE ONLINE // A new operator has entered the network. Your first contract is waiting.";

			// Define the starting stat values for the career.
			root.career.stats = new[]
			{
				new NamedStat("slots", 2), new NamedStat("queueCapacity", 1), new NamedStat("compute", 1),
				new NamedStat("bandwidth", 1), new NamedStat("scanRadius", 2), new NamedStat("revealRadius", 1),
				new NamedStat("traceGrowth", 1), new NamedStat("minimumSecurity", 1), new NamedStat("futureQueue", 0),
				new NamedStat("traceFree", 0), new NamedStat("tracePressure", 0), new NamedStat("idleTracePerSecond", 0)
			};

			// Define the career ranks and the number of jobs needed to reach each one.
			root.career.ranks = new[]
			{
				new CareerRank { jobs = 0, title = "NEW SIGNAL" }, new CareerRank { jobs = 1, title = "FREELANCER" },
				new CareerRank { jobs = 3, title = "TRUSTED OPERATOR" },
				new CareerRank { jobs = 6, title = "GHOST IN THE NETWORK" }
			};

			// Mark every definition dirty so Unity saves the new values.
			foreach (var asset in GameDefinitionValidator.Definitions(root))
				EditorUtility.SetDirty(asset);

			// Validate the full definition set before saving it.
			GameDefinitionValidator.ThrowIfInvalid(root);
			AssetDatabase.SaveAssets();

			// Re-apply the authored balance refinements on top of the imported values.
			BalanceRefinement.Apply();

			// Write the import result to the artifact log.
			File.WriteAllText("Artifacts/gameplay-import.txt",
							  "PASS / All gameplay definitions imported as Unity assets with current career tuning.");
		}

		/// <summary>Validates and saves imported gameplay ScriptableObjects under <see cref="Folder" />.</summary>
		public static void SaveCatalog()
		{
			// Load the root game asset that anchors the catalog.
			var root = Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");

			// Validate the catalog before saving it.
			GameDefinitionValidator.ThrowIfInvalid(root);

			// Mark every definition dirty so Unity saves the changes.
			foreach (var asset in GameDefinitionValidator.Definitions(root))
				EditorUtility.SetDirty(asset);
			AssetDatabase.SaveAssets();
		}
	}
}