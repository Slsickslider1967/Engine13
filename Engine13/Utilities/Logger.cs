using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ImGuiNET;

namespace Engine13.Utilities
{
    public static class Logger
    {
        private static readonly Dictionary<string, StreamWriter> _csvWriters = new();
        private static readonly Dictionary<string, bool> _headerWritten = new();
        private static readonly object _syncLock = new();

        public static void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
        public static void LogSimKey(string key) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SIMKEY: {key}");

        public static void LogError(string msg)
        { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ERROR: {msg}"); Console.ResetColor(); }

        public static void LogWarning(string msg)
        { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] WARNING: {msg}"); Console.ResetColor(); }

        public static void InitCSV(string filename, params string[] headers)
        {
            lock (_syncLock)
            {
                if (_csvWriters.ContainsKey(filename)) { LogWarning($"CSV file '{filename}' already initialized"); return; }
                try
                {
                    var writer = new StreamWriter($"{filename}.csv", false, Encoding.UTF8);
                    _csvWriters[filename] = writer;
                    if (headers is { Length: > 0 }) { writer.WriteLine(string.Join(",", headers)); writer.Flush(); _headerWritten[filename] = true; }
                    else _headerWritten[filename] = false;
                    Log($"CSV logger initialized: {filename}.csv");
                }
                catch (Exception ex) { LogError($"Failed to initialize CSV '{filename}': {ex.Message}"); }
            }
        }

        public static void LogCSV(string filename, params object[] values)
        {
            lock (_syncLock)
            {
                if (!_csvWriters.TryGetValue(filename, out var writer)) { LogWarning($"CSV file '{filename}' not initialized"); return; }
                try { writer.WriteLine(string.Join(",", Array.ConvertAll(values, v => EscapeCSV(v?.ToString() ?? "")))); writer.Flush(); }
                catch (Exception ex) { LogError($"Failed to write to CSV '{filename}': {ex.Message}"); }
            }
        }

        public static void LogCSVWithTimestamp(string filename, params object[] values)
        {
            var ts = new object[values.Length + 1];
            ts[0] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Array.Copy(values, 0, ts, 1, values.Length);
            LogCSV(filename, ts);
        }

        public static void CloseCSV(string filename)
        {
            lock (_syncLock)
            {
                if (_csvWriters.TryGetValue(filename, out var w))
                { w.Flush(); w.Close(); w.Dispose(); _csvWriters.Remove(filename); _headerWritten.Remove(filename); Log($"CSV logger closed: {filename}.csv"); }
            }
        }

        public static void CloseAllCSV()
        {
            lock (_syncLock)
            {
                foreach (var w in _csvWriters.Values) { w.Flush(); w.Close(); w.Dispose(); }
                _csvWriters.Clear(); _headerWritten.Clear(); Log("All CSV loggers closed");
            }
        }

        public static void LogPerformance(string filename, string metric, double value, string unit = "ms")
        {
            if (!_csvWriters.ContainsKey(filename)) InitCSV(filename, "Timestamp", "Metric", "Value", "Unit");
            LogCSV(filename, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), metric, value, unit);
        }

        private static string EscapeCSV(string value)
            => string.IsNullOrEmpty(value) ? value
             : (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
               ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
