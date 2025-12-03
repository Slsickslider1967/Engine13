using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Engine13.Utilities
{
    /// <summary>
    /// Thread-safe logging utility with console output and CSV file support.
    /// </summary>
    public static class Logger
    {
        private static readonly Dictionary<string, StreamWriter> _csvWriters = new();
        private static readonly Dictionary<string, bool> _headerWritten = new();
        private static readonly object _syncLock = new();

        /// <summary>Logs an informational message to console with timestamp.</summary>
        public static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        /// <summary>Logs a simulation key identifier.</summary>
        public static void LogSimKey(string key)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SIMKEY: {key}");
        }

        /// <summary>Logs an error message to console in red.</summary>
        public static void LogError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ERROR: {message}");
            Console.ResetColor();
        }

        /// <summary>Logs a warning message to console in yellow.</summary>
        public static void LogWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] WARNING: {message}");
            Console.ResetColor();
        }

        /// <summary>Initializes a new CSV log file with column headers.</summary>
        public static void InitCSV(string filename, params string[] headers)
        {
            lock (_syncLock)
            {
                if (_csvWriters.ContainsKey(filename))
                {
                    LogWarning($"CSV file '{filename}' already initialized");
                    return;
                }

                try
                {
                    string filepath = $"{filename}.csv";
                    var writer = new StreamWriter(filepath, false, Encoding.UTF8);
                    _csvWriters[filename] = writer;

                    if (headers is { Length: > 0 })
                    {
                        writer.WriteLine(string.Join(",", headers));
                        writer.Flush();
                        _headerWritten[filename] = true;
                    }
                    else
                    {
                        _headerWritten[filename] = false;
                    }

                    Log($"CSV logger initialized: {filepath}");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to initialize CSV '{filename}': {ex.Message}");
                }
            }
        }

        /// <summary>Writes a row of data to an initialized CSV file.</summary>
        public static void LogCSV(string filename, params object[] values)
        {
            lock (_syncLock)
            {
                if (!_csvWriters.TryGetValue(filename, out var writer))
                {
                    LogWarning($"CSV file '{filename}' not initialized. Call InitCSV first.");
                    return;
                }

                try
                {
                    var csvLine = string.Join(",", Array.ConvertAll(values, v => EscapeCSV(v?.ToString() ?? "")));
                    writer.WriteLine(csvLine);
                    writer.Flush();
                }
                catch (Exception ex)
                {
                    LogError($"Failed to write to CSV '{filename}': {ex.Message}");
                }
            }
        }

        /// <summary>Writes a timestamped row of data to an initialized CSV file.</summary>
        public static void LogCSVWithTimestamp(string filename, params object[] values)
        {
            var timestampedValues = new object[values.Length + 1];
            timestampedValues[0] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Array.Copy(values, 0, timestampedValues, 1, values.Length);
            LogCSV(filename, timestampedValues);
        }

        /// <summary>Closes a specific CSV file.</summary>
        public static void CloseCSV(string filename)
        {
            lock (_syncLock)
            {
                if (_csvWriters.TryGetValue(filename, out var writer))
                {
                    writer.Flush();
                    writer.Close();
                    writer.Dispose();
                    _csvWriters.Remove(filename);
                    _headerWritten.Remove(filename);
                    Log($"CSV logger closed: {filename}.csv");
                }
            }
        }

        /// <summary>Closes all open CSV files.</summary>
        public static void CloseAllCSV()
        {
            lock (_syncLock)
            {
                foreach (var writer in _csvWriters.Values)
                {
                    writer.Flush();
                    writer.Close();
                    writer.Dispose();
                }
                _csvWriters.Clear();
                _headerWritten.Clear();
                Log("All CSV loggers closed");
            }
        }

        /// <summary>Logs a performance metric to a dedicated CSV file.</summary>
        public static void LogPerformance(string filename, string metricName, double value, string unit = "ms")
        {
            if (!_csvWriters.ContainsKey(filename))
            {
                InitCSV(filename, "Timestamp", "Metric", "Value", "Unit");
            }
            LogCSV(filename, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), metricName, value, unit);
        }

        private static string EscapeCSV(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }
    }
}
