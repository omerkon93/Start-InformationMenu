using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AdminInfoTools.Services
{
    public static class CsvExportService
    {
        public static void Export<T>(IEnumerable<T> items, string filePath)
        {
            if (items == null || !items.Any())
            {
                throw new ArgumentException("No data to export.");
            }

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                // Write header
                var header = string.Join(",", properties.Select(p => p.Name));
                sw.WriteLine(header);

                // Write rows
                foreach (var item in items)
                {
                    var values = properties.Select(p =>
                    {
                        var value = p.GetValue(item, null);
                        var stringValue = value?.ToString() ?? "";
                        // Escape quotes and wrap in quotes if it contains a comma or quote
                        return stringValue.Contains(',') || stringValue.Contains('"') ? $"\"{stringValue.Replace("\"", "\"\"")}\"" : stringValue;
                    });
                    sw.WriteLine(string.Join(",", values));
                }
            }
        }
    }
}