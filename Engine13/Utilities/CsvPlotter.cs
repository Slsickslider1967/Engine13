using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

namespace Engine13.Utilities
{
    public class CsvPlotter(string filePath)
    {
        private List<float[]> _columns = new();
        public bool Loaded => _columns?.Count > 0;
        public int ColumnCount => _columns?.Count ?? 0;
        public int RowCount => (_columns?.Count > 0) ? _columns[0].Length : 0;

        public void Load()
        {
            _columns.Clear();
            if (!File.Exists(filePath)) return;

            var rows = File.ReadAllLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(',').Select(p => float.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : float.NaN).ToArray())
                .Where(r => r.Length > 0).ToList();
            if (rows.Count == 0) return;

            int cols = rows.Max(r => r.Length);
            _columns = Enumerable.Range(0, cols).Select(c => rows.Select(r => c < r.Length ? r[c] : float.NaN).ToArray()).ToList();
        }

        public float[]? GetSeries(int col) => Loaded && col >= 0 && col < _columns.Count ? _columns[col] : null;
    }
}
