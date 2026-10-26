using System;
using UnityEngine;

namespace HackThePlanet
{
    [CreateAssetMenu(menuName = "Hack The Planet/Gameplay Text Palette", fileName = "GameplayTextPalette")]
    public sealed class GameplayTextPalette : ScriptableObject
    {
        [Serializable]
        public sealed class TermStyle
        {
            [Tooltip("Plain phrase or regular expression when Use Regex is enabled.")]
            public string phrase;
            public bool regex;
            public Color color = Color.white;
            public bool bold = true;
        }

        public TermStyle[] terms = DefaultTerms();

        public static TermStyle[] DefaultTerms() => new[]
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
            Term("BONUS DATA", Palette.Cyan, bold: false),
            Term("DATA COLLECTED", Palette.Lime, bold: false),
            Term("ARCHIVE", Palette.Hex("D9D0B1"), bold: false),
            Term("Scanner", Palette.Kind(NodeKind.Scanner)),
            Term("Gateway", Palette.Kind(NodeKind.Gateway)),
            Term("Workstation", Palette.Kind(NodeKind.Workstation)),
            Term("Relay", Palette.Kind(NodeKind.Relay)),
            Term("Firewall", Palette.Kind(NodeKind.Firewall)),
            Term("Security hub", Palette.Kind(NodeKind.SecurityHub)),
            Term("Database", Palette.Kind(NodeKind.Database)),
            Term("trace", Palette.Lime, bold: false),
            Term("Hack", Palette.Amber, bold: false),
            Term("Control tools", Palette.Branch("CONTROL"), bold: false),
        };

        private static TermStyle Term(string phrase, Color color, bool bold = true) =>
            new TermStyle { phrase = phrase, color = color, bold = bold };

        private static TermStyle Regex(string pattern, Color color, bool bold = true) =>
            new TermStyle { phrase = pattern, regex = true, color = color, bold = bold };
    }
}
