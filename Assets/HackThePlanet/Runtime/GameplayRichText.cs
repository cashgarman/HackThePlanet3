using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HackThePlanet
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameplayRichText
    {
        private static GameplayTextPalette palette;
        private static TermRule[] rules = Array.Empty<TermRule>();

        private readonly struct TermRule
        {
            public readonly string Phrase;
            public readonly bool Regex;
            public readonly Color Color;
            public readonly bool Bold;

            public TermRule(GameplayTextPalette.TermStyle style)
            {
                Phrase = style.phrase ?? "";
                Regex = style.regex;
                Color = style.color;
                Bold = style.bold;
            }
        }

        public static GameplayTextPalette Palette
        {
            get => palette ??= LoadPalette();
            set
            {
                palette = value;
                rules = BuildRules(palette);
            }
        }

        public static void ReloadPalette()
        {
            palette = null;
            rules = Array.Empty<TermRule>();
        }

        public static string Format(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            EnsureRules();
            if (rules.Length == 0) return text;

            var covered = new bool[text.Length];
            var hits = new List<(int start, int length, string value, TermRule rule)>();
            foreach (var rule in rules)
            {
                if (rule.Regex)
                {
                    foreach (Match match in Regex.Matches(text, rule.Phrase, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        if (match.Length == 0 || Overlaps(covered, match.Index, match.Length)) continue;
                        Mark(covered, match.Index, match.Length);
                        hits.Add((match.Index, match.Length, match.Value, rule));
                    }
                    continue;
                }

                for (int index = 0; index < text.Length;)
                {
                    int found = text.IndexOf(rule.Phrase, index, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    int matchLength = MatchLength(text, found, rule.Phrase.Length);
                    if (Overlaps(covered, found, matchLength))
                    {
                        index = found + 1;
                        continue;
                    }
                    Mark(covered, found, matchLength);
                    hits.Add((found, matchLength, text.Substring(found, matchLength), rule));
                    index = found + matchLength;
                }
            }

            if (hits.Count == 0) return text;
            hits.Sort((a, b) => b.start.CompareTo(a.start));
            var builder = new StringBuilder(text);
            foreach (var hit in hits)
                builder.Remove(hit.start, hit.length).Insert(hit.start, Wrap(hit.value, hit.rule));
            return builder.ToString();
        }

        public static string Plain(string richText) =>
            string.IsNullOrEmpty(richText) ? richText : Regex.Replace(richText, "<.*?>", string.Empty);

        private static void EnsureRules()
        {
            if (rules.Length != 0) return;
            palette ??= LoadPalette();
            rules = BuildRules(palette);
        }

        private static GameplayTextPalette LoadPalette()
        {
            return GameData.Current.Get<GameplayTextPalette>("Text Palette");
        }

        private static TermRule[] BuildRules(GameplayTextPalette source)
        {
            var configured = source?.terms;
            configured ??= Array.Empty<GameplayTextPalette.TermStyle>();
            var list = new List<TermRule>(configured.Length);
            foreach (var term in configured)
                if (!string.IsNullOrWhiteSpace(term?.phrase))
                    list.Add(new TermRule(term));
            list.Sort((a, b) =>
            {
                int length = b.Phrase.Length.CompareTo(a.Phrase.Length);
                return length != 0 ? length : string.CompareOrdinal(a.Phrase, b.Phrase);
            });
            return list.ToArray();
        }

        private static bool Overlaps(bool[] covered, int start, int length)
        {
            for (int i = start; i < start + length; i++)
                if (covered[i]) return true;
            return false;
        }

        private static void Mark(bool[] covered, int start, int length)
        {
            for (int i = start; i < start + length; i++)
                covered[i] = true;
        }

        private static string Wrap(string value, TermRule rule)
        {
            string hex = ColorUtility.ToHtmlStringRGB(rule.Color);
            return rule.Bold
                ? "<color=#" + hex + "><b>" + value + "</b></color>"
                : "<color=#" + hex + ">" + value + "</color>";
        }

        private static int MatchLength(string text, int start, int phraseLength)
        {
            if (phraseLength <= 0) return phraseLength;
            return ExtendForPlural(text, start, phraseLength);
        }

        private static int ExtendForPlural(string text, int start, int length)
        {
            int end = start + length;
            if (end >= text.Length) return length;

            char last = text[start + length - 1];
            if (!char.IsLetter(last) || last is 's' or 'S') return length;

            if (EndsWithSibilant(text, start, length))
            {
                if (end + 1 < text.Length && text[end] is 's' or 'S' && text[end + 1] is 'e' or 'E')
                {
                    int extended = length + 2;
                    return end + 2 >= text.Length || !char.IsLetter(text[end + 2]) ? extended : length;
                }
                return length;
            }

            if (text[end] is not ('s' or 'S')) return length;
            int withS = length + 1;
            return end + 1 >= text.Length || !char.IsLetter(text[end + 1]) ? withS : length;
        }

        private static bool EndsWithSibilant(string text, int start, int length)
        {
            if (length <= 0) return false;
            char last = char.ToLowerInvariant(text[start + length - 1]);
            if (last is 's' or 'x' or 'z') return true;
            if (length >= 2)
            {
                char prev = char.ToLowerInvariant(text[start + length - 2]);
                return last == 'h' && prev is 's' or 'c';
            }
            return false;
        }
    }
}
