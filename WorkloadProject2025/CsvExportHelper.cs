using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace YourApp.Services
{
    public static class CsvExportHelper
    {
        // Builds CSV bytes (UTF8 BOM) for small/medium exports
        public static byte[] BuildCsv<T>(IEnumerable<T> rows, IEnumerable<string> headers, Func<T, IEnumerable<string>> rowToStrings)
        {
            var sb = new StringBuilder();
            // header row
            sb.AppendLine(string.Join(",", headers));
            foreach (var r in rows)
            {
                var fields = rowToStrings(r);
                sb.AppendLine(BuildCsvLine(fields));
            }

            // return UTF8 with BOM so Excel opens consistently
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            var outb = new byte[bom.Length + body.Length];
            bom.CopyTo(outb, 0);
            body.CopyTo(outb, bom.Length);
            return outb;
        }

        // Escape one CSV line from fields
        public static string BuildCsvLine(IEnumerable<string> fields)
        {
            var escaped = new List<string>();
            foreach (var f in fields)
            {
                var s = f ?? string.Empty;
                if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                {
                    s = "\"" + s.Replace("\"", "\"\"") + "\"";
                }
                escaped.Add(s);
            }

            return string.Join(",", escaped);
        }

        public static FileContentResult CsvFileResult(byte[] csvBytes, string fileName)
        {
            return new FileContentResult(csvBytes, "text/csv; charset=utf-8")
            {
                FileDownloadName = fileName
            };
        }
    }
}