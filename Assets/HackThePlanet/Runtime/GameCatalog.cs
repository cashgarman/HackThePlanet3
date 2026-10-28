using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
	/// <summary>Read-only view of an <see cref="ActionDefinition" /> for UI and tooling.</summary>
	public sealed class ActionInfo
	{
		// Cloned action definition backing this read-only metadata view.
		internal readonly ActionDefinition Definition;

		// Creates a metadata wrapper around the given action definition.
		internal ActionInfo(ActionDefinition definition)
		{
			// Keep a reference to the source definition for read-only access.
			Definition = definition;
		}

		/// <summary>Gets the stable action identifier.</summary>
		public string id => Definition.id;

		/// <summary>Gets the player-facing action title.</summary>
		public string title => Definition.title;

		/// <summary>Gets the action description shown in the workstation.</summary>
		public string description => Definition.description;

		/// <summary>Gets the keyboard shortcut that invokes this action when offered.</summary>
		public KeyCode shortcut => Definition.shortcut;

		/// <summary>Gets the caption displayed for <see cref="shortcut" />.</summary>
		public string shortcutLabel => Definition.shortcutLabel;

		/// <summary>Gets whether the action targets the operator instead of a network node.</summary>
		public bool operatorTarget => Definition.operatorTarget;

		/// <summary>Gets the workstation ordering priority; lower values appear earlier.</summary>
		public int priority => Definition.presentationPriority;
	}

	/// <summary>Read-only view of a <see cref="HardwareDefinition" /> for shop and stat UI.</summary>
	public sealed class HardwareInfo
	{
		// Cloned hardware definition backing this read-only metadata view.
		internal readonly HardwareDefinition Definition;

		// Creates a metadata wrapper around the given hardware definition.
		internal HardwareInfo(HardwareDefinition definition)
		{
			// Keep a reference to the source definition for read-only access.
			Definition = definition;
		}

		/// <summary>Gets the hardware line identifier.</summary>
		public string id => Definition.id;

		/// <summary>Gets the shop display name.</summary>
		public string title => Definition.title;

		/// <summary>Gets the hardware description.</summary>
		public string description => Definition.description;

		/// <summary>Gets the shop grouping label.</summary>
		public string category => Definition.category;

		/// <summary>Gets the label for the primary stat this hardware modifies.</summary>
		public string statLabel => Definition.statLabel;

		/// <summary>Gets the formatted stat name shown in the UI.</summary>
		public string displayedStat => Definition.displayedStat;

		/// <summary>Gets the number of purchasable tiers defined for this hardware.</summary>
		public int maxLevel => Definition.tiers.Length;
	}

	/// <summary>Read-only view of a <see cref="NodeTypeDefinition" /> for network presentation.</summary>
	public sealed class NodeTypeInfo
	{
		// Cloned node type definition backing this read-only metadata view.
		internal readonly NodeTypeDefinition Definition;

		// Creates a metadata wrapper around the given node type definition.
		internal NodeTypeInfo(NodeTypeDefinition definition)
		{
			// Keep a reference to the source definition for read-only access.
			Definition = definition;
		}

		/// <summary>Gets the node type identifier.</summary>
		public string id => Definition.id;

		/// <summary>Gets the role title shown in intel readouts.</summary>
		public string title => Definition.title;

		/// <summary>Gets the default description for nodes of this type.</summary>
		public string description => Definition.description;

		/// <summary>Gets the icon glyph used on the network map.</summary>
		public NodeKind glyph => Definition.glyph;

		/// <summary>Gets the accent color for this node type.</summary>
		public Color color => Definition.color;
	}

	// Compiles a private, detached object graph. Public APIs expose immutable metadata
	// or disposable descriptors; the simulation never changes these definitions.
	/// <summary>Validated, deep-cloned gameplay catalog used at runtime; definitions are not mutated in place.</summary>
	[NoAutoStaticsCleanup]
	public sealed class GameCatalog : IDisposable
	{
		// Cached default catalog built from the active content bundle.
		private static GameCatalog defaultCatalog;

		// Catalogs replaced by InvalidateDefault that simulations may still reference.
		private static readonly List<GameCatalog> retiredCatalogs = new();

		// Deep-cloned game root owned by this catalog instance.
		internal readonly GameDefinition Data;

		// Action definitions indexed by id for fast lookup.
		private readonly Dictionary<string, ActionDefinition> actions;

		// Cloned ScriptableObjects destroyed when the catalog is disposed.
		private readonly List<GameplayDefinition> clones = new();

		// Contract definitions indexed by id for fast lookup.
		private readonly Dictionary<string, ContractDefinition> contracts;

		// Network templates indexed by id for fast lookup.
		private readonly Dictionary<string, NetworkDefinition> networks;

		// Node type definitions indexed by id for fast lookup.
		private readonly Dictionary<string, NodeTypeDefinition> nodeTypes;

		// Technology definitions indexed by id for fast lookup.
		private readonly Dictionary<string, TechnologyDefinition> technologies;

		/// <summary>Builds a catalog from a validated <see cref="GameDefinition" /> root, cloning all nested assets.</summary>
		/// <param name="root">JSON-populated game root; must pass <see cref="GameDefinitionValidator.ThrowIfInvalid" />.</param>
		/// <exception cref="InvalidOperationException">The root is null or fails validation.</exception>
		public GameCatalog(GameDefinition root)
		{
			// Reject a missing root before any cloning begins.
			if (root == null)
				throw new InvalidOperationException("Missing JSON-populated gameplay root definition.");

			// Validate the root graph so the catalog only holds sound data.
			GameDefinitionValidator.ThrowIfInvalid(root);

			// Track source-to-clone mappings so shared references are copied once.
			var map = new Dictionary<GameplayDefinition, GameplayDefinition>();

			// Clone a source definition once, then remap its nested references to clones.
			GameplayDefinition Copy(GameplayDefinition source)
			{
				// Pass nulls through unchanged.
				if (source == null)
					return null;

				// Return the existing clone when the source was already copied.
				if (map.TryGetValue(source, out var existing))
					return existing;

				// Instantiate a hidden copy that keeps the source name.
				var copy = Object.Instantiate(source);
				copy.name = source.name;
				copy.hideFlags = HideFlags.HideAndDontSave;

				// Record the clone and its ownership so it can be disposed later.
				map.Add(source, copy);
				clones.Add(copy);

				// Point nested references at clones as well.
				Remap(copy, new HashSet<object>(), Copy);
				return copy;
			}

			// Clone the root and keep a typed reference to it.
			Data = (GameDefinition)Copy(root);

			// Index each definition collection by id for fast lookup.
			actions = Data.actions.ToDictionary(x => x.id);
			technologies = Data.technologies.ToDictionary(x => x.id);
			nodeTypes = Data.nodeTypes.ToDictionary(x => x.id);
			networks = Data.networks.ToDictionary(x => x.id);
			contracts = Data.contracts.ToDictionary(x => x.id);

			// Expose read-only metadata views over the cloned definitions.
			Actions = Array.AsReadOnly(Data.actions.Select(x => new ActionInfo(x)).ToArray());
			Hardware = Array.AsReadOnly(Data.hardware.Select(x => new HardwareInfo(x)).ToArray());
			NodeTypes = Array.AsReadOnly(Data.nodeTypes.Select(x => new NodeTypeInfo(x)).ToArray());
			Companies = Array.AsReadOnly(Data.companies.Select(x => x.title).ToArray());
		}

		/// <summary>Gets the lazily loaded catalog for the active content bundle.</summary>
		public static GameCatalog Default => defaultCatalog ??= new GameCatalog(GameData.Current.Game);

		/// <summary>Gets read-only metadata for every action in the pack.</summary>
		public IReadOnlyList<ActionInfo> Actions { get; }

		/// <summary>Gets read-only metadata for every hardware line in the pack.</summary>
		public IReadOnlyList<HardwareInfo> Hardware { get; }

		/// <summary>Gets read-only metadata for every node type in the pack.</summary>
		public IReadOnlyList<NodeTypeInfo> NodeTypes { get; }

		/// <summary>Gets company display titles in catalog order.</summary>
		public IReadOnlyList<string> Companies { get; }

		/// <summary>Enumerates research nodes with formatted descriptions for the UI.</summary>
		public IEnumerable<Technology> Technologies => Data.technologies.Select(DescribeTechnology);

		/// <summary>Gets the count of non-recurring story contracts.</summary>
		public int StoryCount => Data.contracts.Count(c => !c.recurring);

		/// <summary>Destroys cloned <see cref="ScriptableObject" /> instances held by this catalog.</summary>
		public void Dispose()
		{
			// Destroy each clone that is still alive.
			foreach (var clone in clones)
				if (clone != null)
				{
					// Destroy at runtime, or immediately when running in the editor.
					if (Application.isPlaying)
						Object.Destroy(clone);
					else
						Object.DestroyImmediate(clone);
				}

			// Forget the disposed clones.
			clones.Clear();
		}

		// Disposes cached and retired catalogs when Unity subsystems reset.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetDefault()
		{
			// Dispose the active catalog and clear the cache.
			defaultCatalog?.Dispose();
			defaultCatalog = null;

			// Dispose catalogs retired by InvalidateDefault.
			foreach (var old in retiredCatalogs)
				old.Dispose();
			retiredCatalogs.Clear();
		}

		// Retires the current default catalog so the next access rebuilds from fresh content.
		internal static void InvalidateDefault()
		{
			// Existing simulations retain their frozen catalog until the session ends.
			if (defaultCatalog != null)
				retiredCatalogs.Add(defaultCatalog);

			// Clear the cache so the next access builds a new catalog.
			defaultCatalog = null;
		}

		// Rewrites nested definition references to their clones while deep-copying a graph.
		private static void Remap(object obj, HashSet<object> visited,
								  Func<GameplayDefinition, GameplayDefinition> copy)
		{
			// Skip nulls, strings, value types, and objects already visited to avoid cycles.
			if (obj == null || obj is string || obj.GetType().IsValueType || !visited.Add(obj))
				return;

			// Rewrite list entries, recursing into nested objects.
			if (obj is IList list)
			{
				for (var i = 0; i < list.Count; i++)
					// Replace definition references with their clones.
					if (list[i] is GameplayDefinition d)
						list[i] = copy(d);
					else
						Remap(list[i], visited, copy);

				return;
			}

			// Walk each public instance field and remap its value.
			foreach (var f in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
			{
				// Read the field value before deciding how to rewrite it.
				var value = f.GetValue(obj);

				// Replace definition references with clones; recurse into everything else.
				if (value is GameplayDefinition d)
					f.SetValue(obj, copy(d));
				else
					Remap(value, visited, copy);
			}
		}

		// Resolves an action definition by id, or null when unknown.
		internal ActionDefinition Action(string id)
		{
			return id != null && actions.TryGetValue(id, out var value) ? value : null;
		}

		// Resolves a technology definition by id, or null when unknown.
		internal TechnologyDefinition Technology(string id)
		{
			return id != null && technologies.TryGetValue(id, out var value) ? value : null;
		}

		// Resolves a node type definition by id, throwing when the id is unknown.
		internal NodeTypeDefinition NodeType(string id)
		{
			return id != null && nodeTypes.TryGetValue(id, out var value)
				? value
				: throw new InvalidOperationException("Unknown node type: " + id);
		}

		// Resolves a network template by id, throwing when the id is unknown.
		internal NetworkDefinition Network(string id)
		{
			return id != null && networks.TryGetValue(id, out var value)
				? value
				: throw new InvalidOperationException("Unknown network: " + id);
		}

		// Resolves a contract definition by id, or null when unknown.
		internal ContractDefinition Contract(string id)
		{
			return id != null && contracts.TryGetValue(id, out var value) ? value : null;
		}

		// Returns the career's base value for a named stat id.
		internal float BaseStat(string id)
		{
			return Data.career.stats.FirstOrDefault(x => x.id == id)?.value ?? 0;
		}

		// Returns the company title for a contract id, or null when the contract is missing.
		internal string CompanyFor(string contractId)
		{
			return Contract(contractId)?.company.title;
		}

		/// <summary>Finds action metadata by identifier, or null when no match exists.</summary>
		/// <param name="id">Action id to resolve.</param>
		/// <returns>The matching <see cref="ActionInfo" />, or null.</returns>
		public ActionInfo FindAction(string id)
		{
			return Actions.FirstOrDefault(x => x.id == id);
		}

		// Builds a rule evaluation context bound to this catalog and career state.
		internal RuleContext Context(Career career, Contract contract = null)
		{
			return new RuleContext { Catalog = this, Career = career, Contract = contract };
		}

		/// <summary>Lists contracts available to a career, evaluating rewards and gating recurring jobs.</summary>
		/// <param name="career">Player progress used for win requirements and formula context.</param>
		/// <returns>Resolved contracts with scaled payouts and lore attached.</returns>
		public IReadOnlyList<Contract> Contracts(Career career)
		{
			return Array.AsReadOnly(Data.contracts.Where(d => (!d.recurring || career.completed.Count >= d.requiredWins) && (career.completed.Contains(d.id) || (d.prerequisites.All(career.completed.Contains) && d.requiredFlags.All(career.storyFlags.Contains))))
										.Select(d => DescribeContract(d, career)).ToArray());
		}

		// Materializes a playable contract with evaluated payouts, seed, and lore for a career.
		internal Contract DescribeContract(ContractDefinition d, Career career)
		{
			// Evaluate contract formulas against the career context.
			var context = Context(career);

			// Build the contract from its evaluated fields.
			var job = new Contract
			{
				id = d.id, title = d.title, description = d.description, client = d.client, company = d.company.title,
				tag = d.tag, objective = d.objective, condition = d.condition, aftermath = d.aftermath,
				kind = d.displayKind, requiredWins = d.requiredWins, quietLimit = d.quietLimit, recurring = d.recurring,
				networkId = d.network.id,
				credits = Mathf.RoundToInt(d.credits.Evaluate(context)), xp = Mathf.RoundToInt(d.xp.Evaluate(context)),
				difficulty = Mathf.RoundToInt(d.difficulty.Evaluate(context)),
				seed = Mathf.RoundToInt(d.seed.Evaluate(context))
			};

			// Scale the payouts by the intel tier multiplier.
			job.credits = Mathf.RoundToInt(job.credits * job.RewardMultiplier);
			job.xp = Mathf.RoundToInt(job.xp * job.RewardMultiplier);

			// Attach lore fragments tagged with their company and contract titles.
			job.lore = d.lore.Select(x => new LoreFragment
			{
				id = x.id, title = x.title, text = x.text, company = job.company, contract = job.title
			}).ToArray();

			return job;
		}

		// Builds UI-facing technology metadata with formatted description text.
		internal Technology DescribeTechnology(TechnologyDefinition d)
		{
			return new Technology(d.id, d.title, d.branch, d.cost, d.position.x, d.position.y, FormatTechnology(d),
								  d.special, d.parents.Select(x => x.id).ToArray())
				{ grants = d.grants.Select(x => x.id).ToArray() };
		}

		// Expands modifier tokens in a technology description into formatted numbers.
		private string FormatTechnology(TechnologyDefinition d)
		{
			// Replace each modifier token with its evaluated, formatted value.
			return Regex.Replace(d.description ?? "", @"\{modifier:(\d+):(value|reduction|percent)\}", match =>
			{
				// Parse the modifier index from the token.
				var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

				// Leave tokens that reference a missing modifier unchanged.
				if (index >= d.modifiers.Length)
					return match.Value;

				// Evaluate the modifier's formula against a fresh career.
				var value = d.modifiers[index].value.Evaluate(Context(new Career()));

				// Convert the value for reductions and percentages.
				switch (match.Groups[2].Value)
				{
					case "reduction": value = (1 - value) * 100; break;
					case "percent":   value *= 100; break;
				}

				return value.ToString("0.##", CultureInfo.InvariantCulture);
			});
		}

		// Substitutes catalog and context placeholders in player-facing message templates.
		internal string Format(string text, RuleContext c)
		{
			return (text ?? "").Replace("{target}", c.Node?.label ?? "operator")
							   .Replace("{archiveCredits}", Data.rewards.archiveCredits.ToString())
							   .Replace("{archiveXp}", Data.rewards.archiveXp.ToString())
							   .Replace("{archiveXpCap}", Data.rewards.archiveXpCap.ToString())
							   .Replace("{aftermath}", c.Contract?.aftermath ?? string.Empty)
							   .Replace("{quietCredits}", Data.rewards.quietCredits.ToString())
							   .Replace("{quietXp}", Data.rewards.quietXp.ToString());
		}

		/// <summary>Creates a new career initialized from pack defaults, starter rig, and welcome news.</summary>
		/// <returns>A career ready for simulation without an active operation.</returns>
		public Career CreateCareer()
		{
			// Read the pack's starting career defaults.
			var r = Data.career;

			// Build the career from pack defaults, the starter rig, and the welcome news.
			return new Career
			{
				initialized = true, researchSpent = 0, credits = r.credits, xp = r.xp, day = r.day,
				unlocked = r.initialTechnologies.Select(x => x.id).ToList(), news = new List<string> { r.welcome },
				chassisId = Data.rig.startingChassis.id,
				rigParts = Data.rig.startingParts.Select(p => new RigPart
				{
					instanceId = Guid.NewGuid().ToString("N"), modelId = p.module.id, row = p.row, column = p.column,
					rotated = p.rotated
				}).ToList()
			};
		}
	}
}