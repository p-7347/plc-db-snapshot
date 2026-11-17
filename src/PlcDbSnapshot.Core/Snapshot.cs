using System;
using System.Collections.Generic;

namespace PlcDbSnapshot.Core
{
    /// <summary>One tag value captured from the PLC.</summary>
    public sealed class SnapshotEntry
    {
        public SnapshotEntry(string tag, string dataType, string value, string status)
        {
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
            DataType = dataType ?? "";
            Value = value ?? "";
            Status = string.IsNullOrEmpty(status) ? StatusOk : status;
        }

        public const string StatusOk = "OK";

        /// <summary>Full symbolic path, e.g. <c>MyDb.Station[2].Count</c>. The first segment is the DB name.</summary>
        public string Tag { get; }
        public string DataType { get; }
        public string Value { get; }
        /// <summary><c>OK</c>, or a short read error text.</summary>
        public string Status { get; }

        public bool IsOk => Status == StatusOk;

        /// <summary>The block name (first path segment) of <see cref="Tag"/>.</summary>
        public string Block => TagPath.BlockOf(Tag);
    }

    /// <summary>A set of tag values read at one point in time, plus where and when it was read.</summary>
    public sealed class Snapshot
    {
        public Snapshot()
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Entries = new List<SnapshotEntry>();
        }

        public const string KeyPlc = "Plc";
        public const string KeyTime = "Time";
        public const string KeyNote = "Note";
        public const string KeyBlocks = "Blocks";

        /// <summary>Free key/value header (PLC address, time, note, ...).</summary>
        public Dictionary<string, string> Metadata { get; }
        public List<SnapshotEntry> Entries { get; }

        public string Get(string key)
        {
            return Metadata.TryGetValue(key, out var v) ? v : "";
        }
    }

    public static class TagPath
    {
        /// <summary>Returns the first segment of a symbolic tag path (the DB or area name).</summary>
        public static string BlockOf(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            if (tag[0] == '"')
            {
                var end = tag.IndexOf('"', 1);
                return end > 0 ? tag.Substring(1, end - 1) : tag;
            }
            var dot = tag.IndexOf('.');
            var bracket = tag.IndexOf('[');
            var cut = dot < 0 ? bracket : (bracket < 0 ? dot : Math.Min(dot, bracket));
            return cut < 0 ? tag : tag.Substring(0, cut);
        }
    }
}
