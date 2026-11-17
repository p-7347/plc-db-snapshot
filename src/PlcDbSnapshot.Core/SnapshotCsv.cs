using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PlcDbSnapshot.Core
{
    /// <summary>
    /// Reads and writes snapshots as CSV that opens directly in Excel (UTF-8 with BOM).
    /// Layout:
    /// <code>
    /// # PlcDbSnapshot 1
    /// # Plc=192.168.0.1
    /// # Time=2026-11-17T14:30:00+09:00
    /// Tag,DataType,Value,Status
    /// MyDb.Count,Int,12,OK
    /// </code>
    /// Lines starting with '#' before the header are metadata (key=value).
    /// </summary>
    public static class SnapshotCsv
    {
        public const string Magic = "# PlcDbSnapshot 1";
        public static readonly string[] Header = { "Tag", "DataType", "Value", "Status" };

        public static void Write(Snapshot snapshot, string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                Write(snapshot, w);
            }
        }

        public static void Write(Snapshot snapshot, TextWriter w)
        {
            w.WriteLine(Magic);
            foreach (var kv in snapshot.Metadata)
            {
                w.WriteLine("# " + kv.Key + "=" + OneLine(kv.Value));
            }
            w.WriteLine(string.Join(",", Header));
            foreach (var e in snapshot.Entries)
            {
                w.WriteLine(Csv.Join(e.Tag, e.DataType, e.Value, e.Status));
            }
        }

        public static Snapshot Read(string path)
        {
            using (var r = new StreamReader(path, Encoding.UTF8, true))
            {
                return Read(r);
            }
        }

        public static Snapshot Read(TextReader r)
        {
            var s = new Snapshot();
            string line;
            var headerSeen = false;
            var lineNo = 0;
            while ((line = r.ReadLine()) != null)
            {
                lineNo++;
                if (!headerSeen)
                {
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        var body = line.TrimStart('#').Trim();
                        var eq = body.IndexOf('=');
                        if (eq > 0) s.Metadata[body.Substring(0, eq).Trim()] = body.Substring(eq + 1).Trim();
                        continue;
                    }
                    if (line.Trim().Length == 0) continue;
                    var cols = Csv.Split(line);
                    if (!cols.Take(Header.Length).SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("스냅샷 CSV 형식이 아닙니다 (헤더 'Tag,DataType,Value,Status' 없음, " + lineNo + "번째 줄).");
                    }
                    headerSeen = true;
                    continue;
                }
                if (line.Length == 0) continue;
                var f = Csv.Split(line);
                if (f.Count < 1 || f[0].Length == 0) continue;
                s.Entries.Add(new SnapshotEntry(
                    f[0],
                    f.Count > 1 ? f[1] : "",
                    f.Count > 2 ? f[2] : "",
                    f.Count > 3 ? f[3] : SnapshotEntry.StatusOk));
            }
            if (!headerSeen)
            {
                throw new InvalidDataException("스냅샷 CSV 형식이 아닙니다 (헤더 없음).");
            }
            return s;
        }

        private static string OneLine(string v)
        {
            return (v ?? "").Replace("\r", " ").Replace("\n", " ");
        }
    }

    /// <summary>Minimal RFC 4180 CSV helpers (comma, double-quote escaping).</summary>
    public static class Csv
    {
        public static string Escape(string v)
        {
            if (v == null) return "";
            var needs = v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || v.StartsWith(" ") || v.EndsWith(" ");
            return needs ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        public static string Join(params string[] values)
        {
            return string.Join(",", values.Select(Escape));
        }

        public static List<string> Split(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }
    }
}
