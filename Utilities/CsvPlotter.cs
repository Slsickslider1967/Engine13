using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

namespace Engine13.Utilities
{
    // Simple CSV reader and transposer that exposes columns as float arrays for ImGui plotting.
    public class CsvPlotter
    {
        private readonly string _filePath;
        private List<float[]> _columns = new();

        public CsvPlotter(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public bool Loaded => _columns != null && _columns.Count > 0;

        public void Load()
        {
            _columns.Clear();

            if (!File.Exists(_filePath))
                return;

            string[] lines = File.ReadAllLines(_filePath);
            var rows = new List<float[]>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                var values = new List<float>();
                foreach (var p in parts)
                {
                    if (float.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    {
                        values.Add(v);
                    }
                }

                if (values.Count > 0)
                    rows.Add(values.ToArray());
            }

            if (rows.Count == 0)
                return;

            int cols = rows.Max(r => r.Length);
            // transpose rows -> columns
            _columns = new List<float[]>(cols);
            for (int c = 0; c < cols; c++)
            {
                var col = new float[rows.Count];
                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    col[r] = (c < row.Length) ? row[c] : float.NaN;
                }
                _columns.Add(col);
            }
        }

        // Returns null if column not available
        public float[]? GetSeries(int columnIndex)
        {
            if (!Loaded) return null;
            if (columnIndex < 0 || columnIndex >= _columns.Count) return null;
            return _columns[columnIndex];
        }

        public int ColumnCount => _columns?.Count ?? 0;

        public int RowCount => (_columns != null && _columns.Count > 0) ? _columns[0].Length : 0;
    }
}
