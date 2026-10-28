using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cashg.GraphExplorer;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
	/// <summary>One manifest row mapping a JSON file to a content category and object key.</summary>
	[Serializable]
	public sealed class ContentEntry
	{
		/// <summary>Unique object key (GUID without dashes) used in the bundle dictionary.</summary>
		public string key, category, file, type;
	}

	/// <summary>Top-level manifest listing every JSON asset in a content directory.</summary>
	[Serializable]
	public sealed class ContentManifest
	{
		/// <summary>Manifest format version; only version 1 is supported.</summary>
		public int schemaVersion = 1;

		/// <summary>Registered content files and their categories.</summary>
		public ContentEntry[] entries = Array.Empty<ContentEntry>();
	}

	/// <summary>A fully resolved, owned graph of JSON-populated ScriptableObjects.</summary>
	public sealed class ContentBundle : IDisposable
	{
		/// <summary>Load or validation errors collected while building the bundle.</summary>
		public readonly List<string> Errors = new();

		/// <summary>Raw JSON text keyed by relative file path.</summary>
		public readonly Dictionary<string, string> Files = new();

		/// <summary>Manifest entry keys that failed to parse or populate.</summary>
		public readonly HashSet<string> InvalidEntries = new();

		/// <summary>Materialized ScriptableObjects keyed by manifest entry key.</summary>
		public readonly Dictionary<string, ScriptableObject> Objects = new();

		/// <summary>Parsed manifest for this bundle.</summary>
		public ContentManifest Manifest;

		/// <summary>Gets the resolved game root definition.</summary>
		public GameDefinition Game => Get<GameDefinition>("Game");

		/// <summary>Destroys all owned ScriptableObject instances in <see cref="Objects" />.</summary>
		public void Dispose()
		{
			// Destroy each owned object that is still alive.
			foreach (var obj in Objects.Values)
				if (obj != null)
				{
					// Destroy at runtime, or immediately when running in the editor.
					if (Application.isPlaying)
						Object.Destroy(obj);
					else
						Object.DestroyImmediate(obj);
				}

			// Drop the references to the destroyed objects.
			Objects.Clear();
		}

		/// <summary>Gets the single ScriptableObject registered for a manifest category.</summary>
		/// <typeparam name="T">Expected asset type for that category.</typeparam>
		/// <param name="category">Manifest category name from <see cref="ContentJson.Categories" />.</param>
		/// <returns>The matching object instance.</returns>
		public T Get<T>(string category) where T : ScriptableObject
		{
			return (T)Objects[Manifest.entries.Single(e => e.category == category).key];
		}

		/// <summary>Resolves a cross-reference id to a gameplay definition of the expected type.</summary>
		/// <param name="id">Definition id from JSON.</param>
		/// <param name="expected">Required definition type.</param>
		/// <returns>The matching definition, or null when not found.</returns>
		public ScriptableObject Resolve(string id, Type expected)
		{
			return Objects.Values.OfType<GameplayDefinition>()
						  .SingleOrDefault(d => d.id == id && expected.IsInstanceOfType(d));
		}
	}

	/// <summary>Shared strict codec. Only the explicitly registered content types may be materialized.</summary>
	[NoAutoStaticsCleanup]
	public static class ContentJson
	{
		// Types that may appear as top-level manifest entries.
		/// <summary>ScriptableObject types that may appear as top-level manifest entries.</summary>
		public static readonly Type[] DefinitionTypes =
		{
			typeof(GameDefinition), typeof(CompanyDefinition), typeof(ContractDefinition), typeof(NetworkDefinition),
			typeof(NodeTypeDefinition), typeof(ActionDefinition), typeof(TechnologyDefinition),
			typeof(HardwareDefinition),
			typeof(StatusDefinition), typeof(CareerDefinition), typeof(RewardDefinition), typeof(GameplayTextPalette),
			typeof(GraphVisualSettings), typeof(GraphNodeLabelSettings), typeof(RigChassisDefinition),
			typeof(RigModuleDefinition), typeof(RigRulesDefinition), typeof(GraphAtmosphereSettings)
		};

		// Concrete rule classes that may appear inside content graphs.
		/// <summary>Polymorphic rule types serialized with a <c>type</c> discriminator.</summary>
		public static readonly Type[] RuleTypes =
		{
			typeof(ConstantNumber), typeof(StatNumber), typeof(InputNumber), typeof(BinaryNumber),
			typeof(ConditionalNumber), typeof(RandomNumber),
			typeof(AlwaysCondition), typeof(AllCondition), typeof(AnyCondition), typeof(NotCondition),
			typeof(NodeStateCondition),
			typeof(RigNeighborCondition), typeof(NodeTagCondition), typeof(ActionCondition), typeof(ResearchCondition),
			typeof(NamedNodeCondition), typeof(CompareCondition),
			typeof(SetStateEffect), typeof(RevealEffect), typeof(TraceEffect), typeof(ApplyStatusEffect),
			typeof(SecurityEffect),
			typeof(CounterEffect), typeof(ConditionalEffect), typeof(LogEffect), typeof(RecoverLoreEffect),
			typeof(DefenseCondition), typeof(DefenseEffect)
		};

		// Plain value and nested types that may appear inside definitions.
		// Registered nested and value types allowed in strict JSON encoding.
		private static readonly Type[] ValueTypes =
		{
			typeof(RigStarterPart), typeof(ContentManifest), typeof(ContentEntry), typeof(NamedStat),
			typeof(Requirement), typeof(Modifier), typeof(Trigger), typeof(Aura), typeof(NodeSelector),
			typeof(StoryOption), typeof(EvidenceCase), typeof(EvidenceAnswer), typeof(ObjectiveDefinition), typeof(ContractActionRule), typeof(ContractDefenseDefinition), typeof(LoreFragment), typeof(HardwareTier),
			typeof(NetworkNode), typeof(NetworkEdge),
			typeof(RouteTier), typeof(ParallelRouteLayout), typeof(CareerRank), typeof(OutcomeRule), typeof(RewardRule),
			typeof(GameplayTextPalette.TermStyle),
			typeof(Vector2), typeof(Vector3), typeof(Color)
		};

		// Manifest category names in the order used by CategoryType.
		/// <summary>Manifest category labels aligned with <see cref="DefinitionTypes" />.</summary>
		public static readonly string[] Categories =
		{
			"Game", "Companies", "Contracts", "Networks", "Node Types", "Actions", "Technologies", "Hardware",
			"Statuses", "Career", "Rewards", "Text Palette", "Graph Visuals", "Network Visuals", "Graph Labels",
			"Chassis", "Modules", "Rig Rules", "Atmosphere Settings"
		};

		/// <summary>Maps a manifest category name to its ScriptableObject type.</summary>
		/// <param name="category">Category from <see cref="Categories" />.</param>
		/// <returns>The type used when creating manifest entries for the category.</returns>
		/// <exception cref="InvalidDataException">The category is not registered.</exception>
		public static Type CategoryType(string category)
		{
			// Find the category's position in the manifest label list.
			var index = Array.IndexOf(Categories, category);

			// Reject category labels that are not registered.
			if (index < 0)
				throw new InvalidDataException("Unknown category: " + category);

			// Map the label index to its type, sharing one slot for the two visual categories.
			return DefinitionTypes[index > 13 ? index - 1 : index == 13 ? 12 : index];
		}

		/// <summary>Lists public serialized fields eligible for strict JSON encoding on a type.</summary>
		/// <param name="type">Definition, rule, or value type to inspect.</param>
		/// <returns>Fields in deterministic order for stable JSON output.</returns>
		public static FieldInfo[] Fields(Type type)
		{
			return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
					   .Where(f => !f.IsNotSerialized && (f.DeclaringType == typeof(GameplayDefinition) ||
														  DefinitionTypes.Contains(f.DeclaringType) ||
														  RuleTypes.Contains(f.DeclaringType) ||
														  ValueTypes.Contains(f.DeclaringType)))
					   .OrderBy(f => f.DeclaringType == typeof(GameplayDefinition) ? 0 : 1).ThenBy(f => f.MetadataToken)
					   .ToArray();
		}

		/// <summary>
		///     Determines whether a type is a polymorphic rule base (<see cref="Number" />, <see cref="Condition" />, or
		///     <see cref="Effect" />).
		/// </summary>
		/// <param name="t">Type to test.</param>
		/// <returns>True when the type participates in rule discrimination.</returns>
		public static bool IsRule(Type t)
		{
			return t == typeof(Number) || t == typeof(Condition) || t == typeof(Effect);
		}

		/// <summary>Serializes a content object graph to indented JSON using catalog reference ids.</summary>
		/// <param name="value">Root object to encode.</param>
		/// <returns>UTF-8 JSON text with trailing newline.</returns>
		/// <exception cref="InvalidDataException">The graph exceeds depth limits or uses unsupported types.</exception>
		public static string Encode(object value)
		{
			// Buffer the encoded JSON in memory.
			using (var stream = new MemoryStream())
			{
				// Write the value as indented UTF-8 JSON.
				using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
				{
					// Walk the object graph from the root.
					Write(writer, value, true, 0);
				}

				// Decode the buffered bytes and append a trailing newline.
				return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
			}
		}

		// Recursively writes a content value graph to JSON with depth and type guards.
		private static void Write(Utf8JsonWriter w, object value, bool root, int depth)
		{
			// Reject graphs nested deeper than the supported limit.
			if (depth > 64)
				throw new InvalidDataException("Content graph exceeds 64 levels.");

			// Write null values as JSON null.
			if (value == null)
			{
				w.WriteNullValue();
				return;
			}

			// Resolve the runtime type of the value.
			var type = value.GetType();

			// Write nested definition references as their id only.
			if (!root && value is GameplayDefinition reference)
			{
				w.WriteStringValue(reference.id);
				return;
			}

			// Write strings directly.
			if (value is string str)
			{
				w.WriteStringValue(str);
				return;
			}

			// Write booleans directly.
			if (value is bool flag)
			{
				w.WriteBooleanValue(flag);
				return;
			}

			// Write enums by their name.
			if (type.IsEnum)
			{
				w.WriteStringValue(value.ToString());
				return;
			}

			// Write integers as JSON numbers.
			if (value is int i)
			{
				w.WriteNumberValue(i);
				return;
			}

			// Write floats as JSON numbers.
			if (value is float f)
			{
				w.WriteNumberValue(f);
				return;
			}

			// Write collections element by element.
			if (value is IEnumerable items)
			{
				w.WriteStartArray();
				foreach (var item in items)
					Write(w, item, false, depth + 1);
				w.WriteEndArray();
				return;
			}

			// Ensure the type is registered before writing it as an object.
			EnsureAllowed(type);
			w.WriteStartObject();

			// Emit the discriminator for polymorphic rule types.
			if (RuleTypes.Contains(type))
				w.WriteString("type", type.Name);

			// Write each serialized field as a named property.
			foreach (var field in Fields(type))
			{
				w.WritePropertyName(field.Name);
				Write(w, field.GetValue(value), false, depth + 1);
			}

			// Close the JSON object.
			w.WriteEndObject();
		}

		// Throws when a type is not registered as content, rule, or value data.
		private static void EnsureAllowed(Type type)
		{
			// Reject types that are not registered as content, rule, or value types.
			if (!DefinitionTypes.Contains(type) && !RuleTypes.Contains(type) && !ValueTypes.Contains(type))
				throw new InvalidDataException("Unsupported content type: " + type.Name);
		}

		/// <summary>Deserializes JSON into a new instance without cross-reference resolution.</summary>
		/// <typeparam name="T">Target type with a parameterless constructor.</typeparam>
		/// <param name="json">Strict content JSON.</param>
		/// <returns>Populated instance.</returns>
		/// <exception cref="InvalidDataException">JSON violates content schema rules.</exception>
		public static T Decode<T>(string json) where T : new()
		{
			// Parse the JSON text into a read-only document.
			using (var doc = JsonDocument.Parse(json))
			{
				// Read the root element into a new instance of T.
				return (T)Read(doc.RootElement, typeof(T), null, "$", null, 0);
			}
		}

		/// <summary>Deserializes JSON into an existing object, resolving gameplay definition references from a bundle.</summary>
		/// <param name="json">Strict content JSON.</param>
		/// <param name="target">Object to populate in place.</param>
		/// <param name="bundle">Bundle used to resolve id references.</param>
		/// <exception cref="InvalidDataException">JSON violates content schema rules.</exception>
		public static void Populate(string json, object target, ContentBundle bundle)
		{
			// Parse the JSON text into a read-only document.
			using (var doc = JsonDocument.Parse(json))
			{
				// Read the root element into the existing target.
				Read(doc.RootElement, target.GetType(), target, "$", bundle, 0);
			}
		}

		// Deserializes one JSON value into a target type with bundle reference resolution.
		private static object Read(JsonElement e, Type type, object target, string path, ContentBundle bundle,
								   int depth)
		{
			// Reject nesting deeper than the supported limit.
			if (depth > 64)
				throw new InvalidDataException(path + ": exceeds 64 nesting levels");

			// Handle JSON null values before any type-specific parsing.
			if (e.ValueKind == JsonValueKind.Null)
			{
				// Reject null for value types, arrays, rules, and non-reference types.
				if (type.IsValueType || type.IsArray || IsRule(type) ||
					(type != typeof(string) && !typeof(GameplayDefinition).IsAssignableFrom(type)))
					throw new InvalidDataException(
						path + ": null value is not permitted; use an empty array or a valid rule/object");

				return null;
			}

			// Resolve definition references by id from the bundle.
			if (target == null && typeof(GameplayDefinition).IsAssignableFrom(type))
			{
				var id = e.GetString();
				return bundle?.Resolve(id, type) ??
					   throw new InvalidDataException(path + ": unknown " + type.Name + " reference '" + id + "'");
			}

			// Read string, boolean, and integer values directly.
			if (type == typeof(string))
				return e.GetString();
			if (type == typeof(bool))
				return e.GetBoolean();
			if (type == typeof(int))
				return e.GetInt32();

			// Read floats and reject non-finite values.
			if (type == typeof(float))
			{
				var f = e.GetSingle();
				if (float.IsNaN(f) || float.IsInfinity(f))
					throw new InvalidDataException(path + ": non-finite number");

				return f;
			}

			// Read enum values by name and reject unknown names.
			if (type.IsEnum)
			{
				// Read the enum name from the JSON string.
				var name = e.GetString();

				// Reject names that are not defined on the enum.
				if (!Enum.IsDefined(type, name))
					throw new InvalidDataException(path + ": unknown " + type.Name + " '" + name + "'");

				// Parse the validated enum name.
				return Enum.Parse(type, name);
			}

			// Read arrays element by element, tracking each element's index.
			if (type.IsArray)
			{
				// Allocate the array and a running element index.
				var array = Array.CreateInstance(type.GetElementType(), e.GetArrayLength());
				var n = 0;

				// Read each element at its index.
				foreach (var item in e.EnumerateArray())
				{
					// Read the element and store it in the array.
					array.SetValue(Read(item, type.GetElementType(), null, path + "[" + n + "]", bundle, depth + 1), n);
					n++;
				}

				return array;
			}

			// Resolve polymorphic rule types from their discriminator name.
			if (IsRule(type))
			{
				// Read the discriminator name from the JSON object.
				var name = e.GetProperty("type").GetString();

				// Reject discriminator names that are not registered rule types.
				type = RuleTypes.SingleOrDefault(t => t.Name == name && type.IsAssignableFrom(t)) ??
					   throw new InvalidDataException(path + ".type: unsupported rule '" + name + "'");
			}

			// Reject types that are not registered before populating them.
			EnsureAllowed(type);

			// Reuse the target or create a new instance of the type.
			target ??= Activator.CreateInstance(type);

			// Index the serialized fields by name and track which ones the JSON provides.
			var fields = Fields(type).ToDictionary(f => f.Name);
			var seen = new HashSet<string>();

			// Read each JSON property into its matching field.
			foreach (var property in e.EnumerateObject())
			{
				// Reject duplicate property names.
				if (!seen.Add(property.Name))
					throw new InvalidDataException(path + "." + property.Name + ": duplicate property");

				// Skip the rule discriminator, which was consumed earlier.
				if (property.Name == "type" && RuleTypes.Contains(type))
					continue;

				// Reject properties that do not match a serialized field.
				if (!fields.TryGetValue(property.Name, out var field))
					throw new InvalidDataException(path + "." + property.Name + ": unknown property");

				// Assign the decoded value to the field, wrapping failures with the field path.
				try
				{
					// Read the property value using the field's declared type.
					field.SetValue(
						target,
						Read(property.Value, field.FieldType, null, path + "." + field.Name, bundle, depth + 1));
				}
				catch (Exception ex)
				{
					throw new InvalidDataException(path + "." + field.Name + ": " + ex.Message, ex);
				}
			}

			// Require every serialized field to be present in the JSON.
			foreach (var field in fields.Values)
				if (!seen.Contains(field.Name) && !(type == typeof(ContractDefinition) &&
					(new[] { "defenses", "paceMultiplier", "prerequisites", "requiredFlags", "offerDays", "isEnding", "optional", "choices", "evidence", "revelation" }.Contains(field.Name))))
					throw new InvalidDataException(path + "." + field.Name + ": required property missing");

			return target;
		}

		/// <summary>Resolves a relative JSON path under a content directory, rejecting escapes and reparse points.</summary>
		/// <param name="directory">Root content folder on disk.</param>
		/// <param name="relative">Relative path from the manifest.</param>
		/// <returns>Full normalized path guaranteed to stay under the directory.</returns>
		/// <exception cref="InvalidDataException">The path is invalid, escapes the root, or uses unsupported links.</exception>
		public static string SafePath(string directory, string relative)
		{
			// Reject empty, rooted, or backslash-separated paths.
			if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\'))
				throw new InvalidDataException("Invalid data path: " + relative);

			// Normalize the content root with a trailing separator.
			var root =
				Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
				Path.DirectorySeparatorChar;

			// Resolve the requested path under the root.
			var full = Path.GetFullPath(Path.Combine(root, relative));

			// Reject paths that resolve outside the content root.
			if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("Data path escapes directory: " + relative);

			// Do not follow symlinks/junctions outside the content tree.
			for (var p = full; p != null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
				if ((File.Exists(p) || Directory.Exists(p)) &&
					(File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException("Linked data paths are unsupported: " + relative);

			return full;
		}

		/// <summary>Loads manifest entries, materializes assets, populates graphs, and optionally validates.</summary>
		/// <param name="directory">Folder containing manifest.json and category files.</param>
		/// <param name="overrides">Optional in-memory JSON replacements keyed by relative file path.</param>
		/// <param name="validate">When true, throws on the first validation error after load.</param>
		/// <returns>A bundle owning all created ScriptableObjects.</returns>
		/// <exception cref="InvalidDataException">Manifest, paths, or validation failed.</exception>
		public static ContentBundle Load(string directory, IDictionary<string, string> overrides = null,
										 bool validate = true)
		{
			// Create the bundle that will own every loaded object.
			var bundle = new ContentBundle();

			// Read a file from the overrides first, otherwise from disk through the safe path resolver.
			string ReadFile(string file)
			{
				return overrides != null && overrides.TryGetValue(file, out var text)
					? text
					: File.ReadAllText(SafePath(directory, file));
			}

			// Load and validate the manifest, then materialize each entry.
			try
			{
				// Decode the manifest and keep its raw text.
				bundle.Files["manifest.json"] = ReadFile("manifest.json");
				bundle.Manifest = Decode<ContentManifest>(bundle.Files["manifest.json"]);

				// Reject unsupported manifest versions and missing entry lists.
				if (bundle.Manifest.schemaVersion != 1 || bundle.Manifest.entries == null)
					throw new InvalidDataException("manifest.json: unsupported schemaVersion or missing entries");

				// Track manifest keys and paths so duplicates are rejected.
				var keys = new HashSet<string>();
				var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var entry in bundle.Manifest.entries)
				{
					// Reject null rows, malformed keys, and duplicate keys or files.
					if (entry == null || !Guid.TryParseExact(entry.key, "N", out _) || !keys.Add(entry.key) ||
						!paths.Add(entry.file))
						throw new InvalidDataException("manifest.json: duplicate/invalid key or file");

					// Confirm the entry path stays under the content directory.
					SafePath(directory, entry.file);

					// Reject unknown categories, reserved names, and category-type mismatches.
					if (entry.file == "manifest.json" || !entry.file.EndsWith(".json", StringComparison.Ordinal) ||
						!Categories.Contains(entry.category) ||
						CategoryType(entry.category).Name != entry.type)
						throw new InvalidDataException(entry.file + ": invalid category/type/path");

					// Create a hidden ScriptableObject for this entry.
					var obj = ScriptableObject.CreateInstance(CategoryType(entry.category));
					obj.hideFlags = HideFlags.HideAndDontSave;
					obj.name = entry.file;
					bundle.Objects.Add(entry.key, obj);

					// Read the entry file, keep its JSON, and record the definition id.
					try
					{
						var json = ReadFile(entry.file);
						bundle.Files[entry.file] = json;

						// Copy the definition id onto gameplay definitions so references can resolve.
						if (obj is GameplayDefinition definition)
							using (var document = JsonDocument.Parse(json))
							{
								definition.id = document.RootElement.GetProperty("id").GetString();
							}
					}
					catch (Exception ex)
					{
						// Record the failed entry and its error message.
						bundle.InvalidEntries.Add(entry.key);
						bundle.Errors.Add(entry.file + ": " + ex.Message);
					}
				}

				// Populate each readable entry against the bundle's cross-references.
				foreach (var entry in bundle.Manifest.entries)
				{
					// Skip entries whose files could not be read.
					if (!bundle.Files.TryGetValue(entry.file, out var json))
						continue;

					// Populate the object graph from the entry's JSON.
					try
					{
						Populate(json, bundle.Objects[entry.key], bundle);
					}
					catch (Exception ex)
					{
						// Record the failed entry and its error message.
						bundle.InvalidEntries.Add(entry.key);
						bundle.Errors.Add(entry.file + ": " + ex.Message);
					}
				}

				// Run cross-entry validation only when every entry loaded cleanly.
				if (bundle.Errors.Count == 0)
					bundle.Errors.AddRange(Validate(bundle));

				// Throw when validation is requested and errors remain.
				if (validate && bundle.Errors.Count > 0)
					throw new InvalidDataException(string.Join("\n", bundle.Errors));

				return bundle;
			}
			catch (Exception ex)
			{
				// Release any objects created before the failure.
				bundle.Dispose();

				// Wrap the failure with the directory context.
				throw new InvalidDataException("Content load failed in " + directory + "\n" + ex.Message, ex);
			}
		}

		/// <summary>Runs manifest, game-definition, registration, range, and palette checks on a loaded bundle.</summary>
		/// <param name="bundle">Bundle to validate.</param>
		/// <returns>Validation error lines; empty when no issues are found.</returns>
		public static IEnumerable<string> Validate(ContentBundle bundle)
		{
			// Collect validation error lines.
			var errors = new List<string>();

			// Require exactly one manifest entry for each core category.
			foreach (var category in new[]
					 {
						 "Game", "Career", "Rewards", "Text Palette", "Graph Visuals", "Network Visuals",
						 "Graph Labels", "Rig Rules", "Atmosphere Settings"
					 })
				if (bundle.Manifest.entries.Count(e => e.category == category) != 1)
					errors.Add("manifest.json: expected one " + category + " entry");

			// Stop early when core manifest entries are missing.
			if (errors.Count > 0)
				return errors;

			// Validate the game root through the definition validator.
			try
			{
				// Append every game-level validation error.
				errors.AddRange(GameDefinitionValidator.Validate(bundle.Game));
			}
			catch (Exception ex)
			{
				// Report unexpected validator failures as game errors.
				errors.Add("Game: " + ex.Message);
			}

			// Collect every definition that the game root registers.
			var registered = new HashSet<GameplayDefinition>(GameDefinitionValidator.Definitions(bundle.Game));

			// Report definitions that exist in the bundle but are not registered under the root.
			foreach (var definition in bundle.Objects.Values.OfType<GameplayDefinition>())
				if (!registered.Contains(definition))
					errors.Add(definition.name + ": not registered in Game root");

			// Check range and minimum attributes on every serialized field.
			foreach (var obj in bundle.Objects.Values)
				foreach (var field in Fields(obj.GetType()))
				{
					// Read the RangeAttribute bounds for this field, if any.
					var range = field.GetCustomAttribute<RangeAttribute>();

					// Enforce the range bounds when the attribute is present.
					if (range != null)
					{
						// Compare the field value against the range.
						var value = Convert.ToSingle(field.GetValue(obj));
						if (value < range.min || value > range.max)
							errors.Add(obj.name + "." + field.Name + ": outside " + range.min + ".." + range.max);
					}

					// Read the MinAttribute lower bound for this field, if any.
					var minimum = field.GetCustomAttribute<MinAttribute>();

					// Enforce the minimum when the attribute is present.
					if (minimum != null && Convert.ToSingle(field.GetValue(obj)) < minimum.min)
						errors.Add(obj.name + "." + field.Name + ": below minimum " + minimum.min);
				}

			foreach (var term in bundle.Get<GameplayTextPalette>("Text Palette").terms ??
								 Array.Empty<GameplayTextPalette.TermStyle>())
				// Require a nonempty phrase, then compile regex phrases to catch syntax errors.
				if (term == null || string.IsNullOrWhiteSpace(term.phrase))
					errors.Add("Text Palette.terms: nonempty phrase required");
				else if (term.regex)
					try
					{
						// Compile the pattern with a short timeout to catch invalid regex.
						_ = new Regex(term.phrase, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
					}
					catch (Exception ex)
					{
						// Report the regex compile failure.
						errors.Add("Text Palette.terms: " + ex.Message);
					}

			// Read the graph label settings and check their ranges.
			var labels = bundle.Get<GraphNodeLabelSettings>("Graph Labels");

			// Reject inverted widths, negative radii, and negative fade times.
			if (labels.MinWidth > labels.MaxWidth || labels.RevealInnerRadius < 0 ||
				labels.RevealOuterRadius < labels.RevealInnerRadius ||
				labels.RevealFadeSeconds < 0)
				errors.Add("Graph Labels: invalid width, fade or proximity ranges");

			return errors;
		}
	}

	/// <summary>Process-wide access to loaded content and GraphExplorer presentation resources.</summary>
	[NoAutoStaticsCleanup]
	public static class GameData
	{
		// Lazily loaded content bundle cached for the active data directory.
		private static ContentBundle current;

		/// <summary>Gets the on-disk data directory adjacent to the Unity project.</summary>
		public static string DirectoryPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "data");

		/// <summary>Gets the lazily loaded and validated content bundle for the active data pack.</summary>
		public static ContentBundle Current => current ??= ContentJson.Load(DirectoryPath);

		/// <summary>Gets whether the player launched with <c>--validate-content</c> for a headless check.</summary>
		public static bool ValidationLaunch => Environment.GetCommandLineArgs().Contains("--validate-content");

		// Headless entry point that validates content and exits when launched with --validate-content.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void ValidateLaunch()
		{
			// Skip this entry point unless the launch flag is present.
			if (!ValidationLaunch)
				return;

			// Read the command line and locate the optional report path.
			var args = Environment.GetCommandLineArgs();
			var index = Array.IndexOf(args, "--validate-content");
			var report = index + 1 < args.Length ? args[index + 1] : null;

			// Load and validate the content, then fingerprint every entry.
			try
			{
				// Keep the loaded content open for the duration of the check.
				using (var content = ContentJson.Load(DirectoryPath))
				{
					// Build the catalog from the loaded game root.
					using (var catalog = new GameCatalog(content.Game))
					{
						// Start the report with the pass line and the definition count.
						var lines = new List<string>
							{ "PASS / " + DirectoryPath, "Definitions: " + content.Manifest.entries.Length };

						// A read-only diagnostic mode for verifying installed/modded data without opening or writing a career.
						lines.AddRange(
							content.Manifest.entries.Select(e => e.file + " " +
																 ContentFingerprint(
																	 ContentJson.Encode(content.Objects[e.key]))));

						// Write the report file when a path was provided.
						if (!string.IsNullOrWhiteSpace(report))
							File.WriteAllLines(report, lines);

						// Log the report to the console.
						Debug.Log(string.Join("\n", lines));
					}
				}

				// Exit successfully after validation.
				Application.Quit(0);
			}
			catch (Exception ex)
			{
				// Record the failure in the report file when a path was provided.
				if (!string.IsNullOrWhiteSpace(report))
					File.WriteAllText(report, "FAIL / " + ex.Message);

				// Log the exception and exit with a failure code.
				Debug.LogException(ex);
				Application.Quit(1);
			}
		}

		// Returns the SHA-256 hex digest of UTF-8 JSON for validation reports.
		private static string ContentFingerprint(string json)
		{
			// Hash the UTF-8 JSON text with SHA-256.
			using (var sha = SHA256.Create())
			{
				// Return the hex digest without separators.
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
			}
		}

		/// <summary>Disposes the cached bundle and reloads dependent presentation state.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Reset()
		{
			// Release the cached bundle and clear the reference.
			current?.Dispose();
			current = null;

			// Refresh rich-text styling from the reloaded content.
			GameplayRichText.ReloadPalette();
		}

		/// <summary>Builds GraphExplorer resource settings from graph or network visual assets in the current bundle.</summary>
		/// <param name="network">When true, uses network visual settings; otherwise workstation graph visuals.</param>
		/// <returns>Configuration with visual and label settings bound for rendering.</returns>
		public static GraphResourceConfiguration Presentation(bool network)
		{
			// Start an empty resource set for GraphExplorer.
			var resources = new GraphResourceConfiguration();

			// Bind the graph visual settings for the requested surface.
			resources.Set("GraphVisualSettings",
						  Current.Get<GraphVisualSettings>(network ? "Network Visuals" : "Graph Visuals"));
			resources.Set("GraphNodeLabelSettings", Current.Get<GraphNodeLabelSettings>("Graph Labels"));

			return resources;
		}
	}
}
