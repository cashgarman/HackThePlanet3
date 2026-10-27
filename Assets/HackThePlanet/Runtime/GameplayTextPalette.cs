using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Authoring asset defining phrase and regex rules for <see cref="GameplayRichText" /> highlighting.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Gameplay Text Palette", fileName = "GameplayTextPalette")]
	public sealed class GameplayTextPalette : ScriptableObject
	{
		/// <summary>Ordered term styles; longer phrases should sort ahead for overlap resolution at runtime.</summary>
		public TermStyle[] terms = DefaultTerms();

		/// <summary>Gets the built-in default term list used when content does not override the palette.</summary>
		/// <returns>Default intel, role, and action highlighting rules.</returns>
		public static TermStyle[] DefaultTerms()
		{
			// Built-in highlight rules for intel tiers, node roles, and actions.
			return new[]
			{
				Term("FULL INTEL", Palette.Cyan),
				Term("LIMITED INTEL", Palette.Cyan),
				Term("TOPOLOGY ONLY", Palette.Cyan),
				Term("NO INTEL", Palette.Amber),
				Term("DISCOVERED", Palette.Cyan),
				Term("NOT CONTROLLED", Palette.Amber),
				Term("CONTROLLED", Palette.Lime),
				Regex("SECURITY \\d+", Palette.Amber),
				Term("Detection", Palette.Red),
				Term("ACTIVE SENSOR", Palette.Red),
				Term("ACTIVE DEFENSE", Palette.Violet),
				Term("SUPPRESSED", Palette.Lime),
				Term("CONTRACT OBJECTIVE", Palette.Amber),
				Term("BONUS DATA", Palette.Cyan, false),
				Term("DATA COLLECTED", Palette.Lime, false),
				Term("ARCHIVE", Palette.Hex("D9D0B1"), false),
				Term("Scanner", Palette.Kind(NodeKind.Scanner)),
				Term("Gateway", Palette.Kind(NodeKind.Gateway)),
				Term("Workstation", Palette.Kind(NodeKind.Workstation)),
				Term("Relay", Palette.Kind(NodeKind.Relay)),
				Term("Firewall", Palette.Kind(NodeKind.Firewall)),
				Term("Security hub", Palette.Kind(NodeKind.SecurityHub)),
				Term("Database", Palette.Kind(NodeKind.Database)),
				Term("trace", Palette.Lime, false),
				Term("Hack", Palette.Amber, false),
				Term("Control tools", Palette.Branch("CONTROL"), false)
			};
		}

		// Builds a plain-phrase highlight rule with the given color and weight.
		private static TermStyle Term(string phrase, Color color, bool bold = true)
		{
			return new TermStyle { phrase = phrase, color = color, bold = bold };
		}

		// Builds a case-insensitive regex highlight rule with the given color and weight.
		private static TermStyle Regex(string pattern, Color color, bool bold = true)
		{
			return new TermStyle { phrase = pattern, regex = true, color = color, bold = bold };
		}

		/// <summary>One highlight rule mapping a phrase or regex to color and weight.</summary>
		[Serializable]
		public sealed class TermStyle
		{
			/// <summary>Plain phrase or regular expression when <see cref="regex" /> is true.</summary>
			[Tooltip("Plain phrase or regular expression when Use Regex is enabled.")]
			public string phrase;

			/// <summary>When true, <see cref="phrase" /> is interpreted as a case-insensitive regex.</summary>
			public bool regex;

			/// <summary>Rich-text color applied to matched text.</summary>
			public Color color = Color.white;

			/// <summary>When true, matched text is wrapped in bold tags.</summary>
			public bool bold = true;
		}
	}
}