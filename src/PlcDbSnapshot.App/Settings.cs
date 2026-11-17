using System;
using System.IO;

namespace PlcDbSnapshot.App
{
    /// <summary>Tiny per-user settings (last IP) and the default snapshot folder.</summary>
    internal static class Settings
    {
        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlcDbSnapshot");

        public static string Load(string key, string fallback)
        {
            try
            {
                var p = Path.Combine(Dir, key + ".txt");
                return File.Exists(p) ? File.ReadAllText(p).Trim() : fallback;
            }
            catch { return fallback; }
        }

        public static void Save(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, key + ".txt"), value ?? "");
            }
            catch { /* settings are a convenience only */ }
        }

        /// <summary>Documents\PlcDbSnapshot, created on demand.</summary>
        public static string SnapshotFolder()
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlcDbSnapshot");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }
}
