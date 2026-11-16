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

        /// <summary>UID taken from a dump file name like "hf-mf-C787CDB4-dump-003.bin".</summary>
        public static string UidFromDumpName(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return null;
            var m = Regex.Match(pathOrName, @"hf-mf-([0-9A-Fa-f]{8,})-", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>Mifare Classic variant inferred from the dump size.</summary>
        public static string MifareTypeFromSize(long bytes)
        {
            switch (bytes)
            {
                case 320: return "Mifare Mini";
                case 1024: return "Mifare Classic 1K";
                case 2048: return "Mifare Classic 2K";
                case 4096: return "Mifare Classic 4K";
                default: return bytes > 0 ? "Mifare Classic" : null;
            }
        }

        public static string DumpFile(string output)
        {
            var m = Regex.Match(output ?? "", @"(hf-mf-[0-9A-Fa-f]+-dump(?:-\d+)?\.(?:bin|json))");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// The absolute path pm3 reports for the saved binary dump, e.g.
        /// "[+] Saved 1024 bytes to binary file `/root/hf-mf-XXXX-dump-001.bin`".
        /// pm3 saves into its working/home directory, which is not always where we run it, so the
        /// reported path is the only reliable way to find the file.
        /// </summary>
        public static string SavedBinPath(string output)
        {
            var m = Regex.Match(output ?? "",
                @"Saved\s+\d+\s+bytes\s+to\s+binary\s+file\s+`?([^`\r\n]+?\.bin)`?");
            if (m.Success) return m.Groups[1].Value.Trim();

            // Fallback: any absolute path to a *-dump*.bin mentioned in the output.
            m = Regex.Match(output ?? "", @"(/\S+?-dump(?:-\d+)?\.bin)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        public static string SavedJsonPath(string output)
        {
            var m = Regex.Match(output ?? "",
                @"Saved\s+to\s+json\s+file\s+`?([^`\r\n]+?\.json)`?");
            return m.Success ? m.Groups[1].Value.Trim() : null;
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
