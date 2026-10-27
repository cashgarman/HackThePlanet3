using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Applies gameplay term highlighting from the loaded text palette to UI strings.</summary>
	[NoAutoStaticsCleanup]
	public static class GameplayRichText
	{
		// Loaded text palette used to build highlight rules.
		private static GameplayTextPalette palette;

		// Sorted highlight rules derived from the active palette.
		private static TermRule[] rules = Array.Empty<TermRule>();

		/// <summary>Gets or sets the palette used to build highlight rules; null triggers reload from content.</summary>
		public static GameplayTextPalette Palette
		{
			get => palette ??= LoadPalette();
			set
			{
				// Store the palette and rebuild the highlight rules from it.
				palette = value;
				rules = BuildRules(palette);
			}
		}

		/// <summary>Clears cached palette and rules so the next format pass reloads from <see cref="GameData" />.</summary>
		public static void ReloadPalette()
		{
			// Drop the cached palette and rules.
			palette = null;
			rules = Array.Empty<TermRule>();
		}

		/// <summary>Wraps matching phrases in rich-text color tags without overlapping earlier matches.</summary>
		/// <param name="text">Plain source text.</param>
		/// <returns>Rich-text string, or the original when no rules match.</returns>
		public static string Format(string text)
		{
			// Leave empty text untouched.
			if (string.IsNullOrEmpty(text))
				return text;

			// Load the rules and skip when there are none.
			EnsureRules();
			if (rules.Length == 0)
				return text;

			// Track which characters are already claimed and collect the matches.
			var covered = new bool[text.Length];
			var hits = new List<(int start, int length, string value, TermRule rule)>();

			// Apply rules in sorted order, longest phrase first, skipping overlaps.
			foreach (var rule in rules)
			{
				// Regex rules match anywhere in the text.
				if (rule.Regex)
				{
					// Claim each regex match that does not overlap an earlier one.
					foreach (Match match in Regex.Matches(text, rule.Phrase,
														  RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
					{
						if (match.Length == 0 || Overlaps(covered, match.Index, match.Length))
							continue;
						Mark(covered, match.Index, match.Length);
						hits.Add((match.Index, match.Length, match.Value, rule));
					}

					continue;
				}

				// Plain phrases match case-insensitively and extend over plurals.
				for (var index = 0; index < text.Length;)
				{
					var found = text.IndexOf(rule.Phrase, index, StringComparison.OrdinalIgnoreCase);
					if (found < 0)
						break;

					// Extend the match over a trailing plural when present.
					var matchLength = MatchLength(text, found, rule.Phrase.Length);

					// Skip matches that overlap an earlier highlight.
					if (Overlaps(covered, found, matchLength))
					{
						index = found + 1;
						continue;
					}

					// Claim the match and record it.
					Mark(covered, found, matchLength);
					hits.Add((found, matchLength, text.Substring(found, matchLength), rule));
					index = found + matchLength;
				}
			}

			// Return the original text when nothing matched.
			if (hits.Count == 0)
				return text;

			// Sort hits from the end so earlier indexes stay valid while wrapping.
			hits.Sort((a, b) => b.start.CompareTo(a.start));

			// Replace each hit with its color-tagged version.
			var builder = new StringBuilder(text);
			foreach (var hit in hits)
				builder.Remove(hit.start, hit.length).Insert(hit.start, Wrap(hit.value, hit.rule));

			return builder.ToString();
		}

		/// <summary>Strips Unity rich-text markup for layout measurement.</summary>
		/// <param name="richText">Text that may contain color or bold tags.</param>
		/// <returns>Visible characters only.</returns>
		public static string Plain(string richText)
		{
			return string.IsNullOrEmpty(richText) ? richText : Regex.Replace(richText, "<.*?>", string.Empty);
		}

		// Loads the palette and builds highlight rules when the cache is empty.
		private static void EnsureRules()
		{
			// Rebuild the rules only when they are missing.
			if (rules.Length != 0)
				return;

			// Load the palette if needed, then rebuild the rules.
			palette ??= LoadPalette();
			rules = BuildRules(palette);
		}

		// Reads the text palette asset from the active content bundle.
		private static GameplayTextPalette LoadPalette()
		{
			return GameData.Current.Get<GameplayTextPalette>("Text Palette");
		}

		// Converts palette term styles into sorted, non-overlapping highlight rules.
		private static TermRule[] BuildRules(GameplayTextPalette source)
		{
			// Fall back to an empty term list when the palette is missing.
			var configured = source?.terms;
			configured ??= Array.Empty<GameplayTextPalette.TermStyle>();

			// Keep only the terms that have a phrase to match.
			var list = new List<TermRule>(configured.Length);
			foreach (var term in configured)
				if (!string.IsNullOrWhiteSpace(term?.phrase))
					list.Add(new TermRule(term));

			// Sort longest phrase first so longer matches win overlaps; ties sort alphabetically.
			list.Sort((a, b) =>
			{
				var length = b.Phrase.Length.CompareTo(a.Phrase.Length);
				return length != 0 ? length : string.CompareOrdinal(a.Phrase, b.Phrase);
			});

			return list.ToArray();
		}

		// Returns whether any character in the range is already claimed by a prior match.
		private static bool Overlaps(bool[] covered, int start, int length)
		{
			// Report whether any character in the range is already claimed.
			for (var i = start; i < start + length; i++)
				if (covered[i])
					return true;

			return false;
		}

		// Marks every character in the range as covered by a highlight match.
		private static void Mark(bool[] covered, int start, int length)
		{
			// Claim every character in the range.
			for (var i = start; i < start + length; i++)
				covered[i] = true;
		}

		// Wraps matched text in color and optional bold rich-text tags.
		private static string Wrap(string value, TermRule rule)
		{
			// Convert the rule color to a hex string.
			var hex = ColorUtility.ToHtmlStringRGB(rule.Color);

			// Wrap the value in color tags, adding bold when the rule asks for it.
			return rule.Bold
				? "<color=#" + hex + "><b>" + value + "</b></color>"
				: "<color=#" + hex + ">" + value + "</color>";
		}

		// Returns phrase length extended over a trailing plural when applicable.
		private static int MatchLength(string text, int start, int phraseLength)
		{
			// Empty phrases cannot match anything.
			if (phraseLength <= 0)
				return phraseLength;

			// Extend the match over a trailing plural when present.
			return ExtendForPlural(text, start, phraseLength);
		}

		// Extends a phrase match to include a trailing s or es plural suffix.
		private static int ExtendForPlural(string text, int start, int length)
		{
			// Nothing follows the match, so there is no plural to add.
			var end = start + length;
			if (end >= text.Length)
				return length;

			// Only letters that are not already plural can take an 's'.
			var last = text[start + length - 1];
			if (!char.IsLetter(last) || last is 's' or 'S')
				return length;

			// Sibilant endings take "es" instead of "s".
			if (EndsWithSibilant(text, start, length))
			{
				// Extend by "es" when it is followed by a word boundary.
				if (end + 1 < text.Length && text[end] is 's' or 'S' && text[end + 1] is 'e' or 'E')
				{
					var extended = length + 2;
					return end + 2 >= text.Length || !char.IsLetter(text[end + 2]) ? extended : length;
				}

				return length;
			}

			// Otherwise extend by 's' when the 's' ends the word.
			if (text[end] is not ('s' or 'S'))
				return length;
			var withS = length + 1;
			return end + 1 >= text.Length || !char.IsLetter(text[end + 1]) ? withS : length;
		}

		// Returns whether the matched word ends in a sibilant that takes an es plural.
		private static bool EndsWithSibilant(string text, int start, int length)
		{
			// Empty matches have no ending to check.
			if (length <= 0)
				return false;

			// Read the last letter, ignoring case.
			var last = char.ToLowerInvariant(text[start + length - 1]);

			// Endings in s, x, or z are always sibilant.
			if (last is 's' or 'x' or 'z')
				return true;

			// "sh" and "ch" endings are sibilant too.
			if (length >= 2)
			{
				var prev = char.ToLowerInvariant(text[start + length - 2]);
				return last == 'h' && prev is 's' or 'c';
			}

			return false;
		}

		// Compiled highlight rule with phrase, color, and match mode for rich-text formatting.
		private readonly struct TermRule
		{
			public readonly string Phrase;
			public readonly bool Regex;
			public readonly Color Color;
			public readonly bool Bold;

			public TermRule(GameplayTextPalette.TermStyle style)
			{
				// Copy the style fields into the rule.
				Phrase = style.phrase ?? "";
				Regex = style.regex;
				Color = style.color;
				Bold = style.bold;
			}
		}
	}
}