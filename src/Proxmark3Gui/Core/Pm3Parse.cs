using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Proxmark3Gui.Core
{
    /// <summary>One recovered Mifare sector trailer.</summary>
    public class SectorKey
    {
        public int Sector;
        public int TrailerBlock;
        public string KeyA;
        public string KeyB;

        public bool IsDefaultA
        {
            get { return string.Equals(KeyA, "FFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsDefaultB
        {
            get { return string.Equals(KeyB, "FFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsDefault { get { return IsDefaultA && IsDefaultB; } }

        /// <summary>A readable data block inside the same sector.</summary>
        public int FirstBlock { get { return Math.Max(0, TrailerBlock - 3); } }

        public override string ToString()
        {
            return "sector " + Sector + " (block " + TrailerBlock + ")  A=" + KeyA + "  B=" + KeyB;
        }
    }

    /// <summary>Pulls the interesting values out of pm3's text output.</summary>
    public static class Pm3Parse
    {
        public static string Uid(string output)
        {
            var m = Regex.Match(output ?? "", @"UID:\s*([0-9A-Fa-f][0-9A-Fa-f ]{6,})");
            if (!m.Success) return null;
            return m.Groups[1].Value.Replace(" ", "").Trim().ToUpperInvariant();
        }

        /// <summary>UID taken from a `hf mf rdbl --blk 0` data row.</summary>
        public static string BlockZeroUid(string output)
        {
            var m = Regex.Match(output ?? "",
                @"\b0\s*\|\s*([0-9A-Fa-f]{2})\s+([0-9A-Fa-f]{2})\s+([0-9A-Fa-f]{2})\s+([0-9A-Fa-f]{2})");
            if (!m.Success) return null;
            return (m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value)
                .ToUpperInvariant();
        }

        public static string MagicCapabilities(string output)
        {
            var m = Regex.Match(output ?? "", @"Magic capabilities[\.\s]*([^\r\n]+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        public static string CardType(string output)
        {
            var m = Regex.Match(output ?? "", @"(?:Tech|Type|TYPE)[:\.\s]+([^\r\n]+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        public static string DumpFile(string output)
        {
            var m = Regex.Match(output ?? "", @"(hf-mf-[0-9A-Fa-f]+-dump(?:-\d+)?\.(?:bin|json))");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string KeyFile(string output)
        {
            var m = Regex.Match(output ?? "", @"(hf-mf-[0-9A-Fa-f]+-key(?:-\d+)?\.bin)");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string Em410xId(string output)
        {
            var m = Regex.Match(output ?? "", @"EM\s*410x\s*ID\s*([0-9A-Fa-f]+)");
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

            m = Regex.Match(output ?? "", @"EM\s*TAG\s*ID\s*:?\s*([0-9A-Fa-f]{10})");
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>The 16-sector key table rows printed by autopwn / fchk.</summary>
        public static List<SectorKey> SectorKeys(string output)
        {
            var list = new List<SectorKey>();
            foreach (var line in (output ?? "").Split('\n'))
            {
                var m = Regex.Match(line,
                    @"\|\s*(\d{3})\s*\|\s*(\d+)\s*\|\s*([0-9A-Fa-f]{12})\s*\|\s*\S\s*\|\s*([0-9A-Fa-f]{12})");
                if (!m.Success) continue;

                list.Add(new SectorKey
                {
                    Sector = int.Parse(m.Groups[1].Value),
                    TrailerBlock = int.Parse(m.Groups[2].Value),
                    KeyA = m.Groups[3].Value.ToUpperInvariant(),
                    KeyB = m.Groups[4].Value.ToUpperInvariant()
                });
            }
            return list;
        }

        /// <summary>True when autopwn had to fill in placeholders for keys it never found.</summary>
        public static bool KeysIncomplete(string output)
        {
            return (output ?? "").IndexOf("has been inserted for unknown keys",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool LooksLikeNoTag(string output)
        {
            string o = output ?? "";
            return o.IndexOf("No known/supported 13.56", StringComparison.OrdinalIgnoreCase) >= 0
                || o.IndexOf("iso14443a card select failed", StringComparison.OrdinalIgnoreCase) >= 0
                || o.IndexOf("No data found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool PortMissing(string output)
        {
            string o = output ?? "";
            return o.IndexOf("invalid serial port", StringComparison.OrdinalIgnoreCase) >= 0
                || o.IndexOf("Proxmark3 not found", StringComparison.OrdinalIgnoreCase) >= 0
                || o.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
