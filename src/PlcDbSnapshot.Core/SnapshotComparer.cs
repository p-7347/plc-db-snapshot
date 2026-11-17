using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PlcDbSnapshot.Core
{
    public enum DiffKind
    {
        Same,
        Changed,
        OnlyInA,
        OnlyInB,
        ReadError
    }

    public sealed class DiffRow
    {
        public string Tag { get; set; } = "";
        public string Block => TagPath.BlockOf(Tag);
        public string DataType { get; set; } = "";
        public string ValueA { get; set; } = "";
        public string ValueB { get; set; } = "";
        public DiffKind Kind { get; set; }

        public static string KindText(DiffKind k)
        {
            switch (k)
            {
                case DiffKind.Same: return "같음";
                case DiffKind.Changed: return "변경";
                case DiffKind.OnlyInA: return "A에만 있음";
                case DiffKind.OnlyInB: return "B에만 있음";
                case DiffKind.ReadError: return "읽기 오류";
            }
            return k.ToString();
        }
    }

    public sealed class CompareOptions
    {
        /// <summary>Tags matching this regex are skipped entirely (e.g. timers, heartbeat counters).</summary>
        public string IgnoreTagRegex { get; set; } = "";
        /// <summary>Real/LReal values whose absolute difference is at or below this count as equal. 0 = exact.</summary>
        public double RealTolerance { get; set; }
        /// <summary>Include rows whose values are equal (normally off).</summary>
        public bool IncludeSame { get; set; }
    }

    public sealed class CompareResult
    {
        public List<DiffRow> Rows { get; } = new List<DiffRow>();
        public int Same { get; set; }
        public int Changed { get; set; }
        public int OnlyInA { get; set; }
        public int OnlyInB { get; set; }
        public int ReadErrors { get; set; }
        public int Ignored { get; set; }

        public string Summary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "변경 {0} / A에만 {1} / B에만 {2} / 읽기오류 {3} / 같음 {4}{5}",
                Changed, OnlyInA, OnlyInB, ReadErrors, Same,
                Ignored > 0 ? " / 제외 " + Ignored : "");
        }
    }

    public static class SnapshotComparer
    {
        public static CompareResult Compare(Snapshot a, Snapshot b, CompareOptions options = null)
        {
            options = options ?? new CompareOptions();
            Regex ignore = null;
            if (!string.IsNullOrWhiteSpace(options.IgnoreTagRegex))
            {
                ignore = new Regex(options.IgnoreTagRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            var mapB = new Dictionary<string, SnapshotEntry>(StringComparer.Ordinal);
            foreach (var e in b.Entries) mapB[e.Tag] = e;

            var result = new CompareResult();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var ea in a.Entries)
            {
                if (!seen.Add(ea.Tag)) continue;
                if (ignore != null && ignore.IsMatch(ea.Tag)) { result.Ignored++; continue; }

                if (!mapB.TryGetValue(ea.Tag, out var eb))
                {
                    result.OnlyInA++;
                    result.Rows.Add(new DiffRow { Tag = ea.Tag, DataType = ea.DataType, ValueA = Shown(ea), Kind = DiffKind.OnlyInA });
                    continue;
                }

                var row = new DiffRow
                {
                    Tag = ea.Tag,
                    DataType = string.IsNullOrEmpty(ea.DataType) ? eb.DataType : ea.DataType,
                    ValueA = Shown(ea),
                    ValueB = Shown(eb)
                };

                if (!ea.IsOk || !eb.IsOk)
                {
                    row.Kind = DiffKind.ReadError;
                    result.ReadErrors++;
                    result.Rows.Add(row);
                }
                else if (ValuesEqual(ea.Value, eb.Value, row.DataType, options.RealTolerance))
                {
                    row.Kind = DiffKind.Same;
                    result.Same++;
                    if (options.IncludeSame) result.Rows.Add(row);
                }
                else
                {
                    row.Kind = DiffKind.Changed;
                    result.Changed++;
                    result.Rows.Add(row);
                }
            }

            foreach (var eb in b.Entries)
            {
                if (seen.Contains(eb.Tag)) continue;
                seen.Add(eb.Tag);
                if (ignore != null && ignore.IsMatch(eb.Tag)) { result.Ignored++; continue; }
                result.OnlyInB++;
                result.Rows.Add(new DiffRow { Tag = eb.Tag, DataType = eb.DataType, ValueB = Shown(eb), Kind = DiffKind.OnlyInB });
            }

            return result;
        }

        public static bool ValuesEqual(string a, string b, string dataType, double realTolerance)
        {
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (realTolerance > 0 && IsReal(dataType)
                && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)
                && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
            {
                return Math.Abs(da - db) <= realTolerance;
            }
            return false;
        }

        private static bool IsReal(string dataType)
        {
            return string.Equals(dataType, "Real", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dataType, "LReal", StringComparison.OrdinalIgnoreCase);
        }

        private static string Shown(SnapshotEntry e)
        {
            return e.IsOk ? e.Value : "(" + e.Status + ")";
        }

        public static void WriteCsv(CompareResult result, Snapshot a, Snapshot b, string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                w.WriteLine("# PlcDbSnapshot compare");
                w.WriteLine("# A=" + Describe(a));
                w.WriteLine("# B=" + Describe(b));
                w.WriteLine("# " + result.Summary());
                w.WriteLine("Result,Block,Tag,DataType,A,B");
                foreach (var r in result.Rows)
                {
                    w.WriteLine(Csv.Join(DiffRow.KindText(r.Kind), r.Block, r.Tag, r.DataType, r.ValueA, r.ValueB));
                }
            }
        }

        public static string Describe(Snapshot s)
        {
            var parts = new[] { s.Get(Snapshot.KeyPlc), s.Get(Snapshot.KeyTime), s.Get(Snapshot.KeyNote) }
                .Where(p => !string.IsNullOrEmpty(p));
            var text = string.Join(" ", parts);
            return text.Length == 0 ? "(정보 없음)" : text;
        }
    }
}
