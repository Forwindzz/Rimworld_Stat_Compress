using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionDefaultConfigLoader
    {
        private const string RelativePath = "Data/DefaultStatConfigs.tsv";

        private static Dictionary<string, DefaultStatConfigRecord> recordsByDefName;

        public static bool TryGet(string defName, out DefaultStatConfigRecord record)
        {
            EnsureLoaded();
            return recordsByDefName.TryGetValue(defName, out record);
        }

        private static void EnsureLoaded()
        {
            if (recordsByDefName != null)
            {
                return;
            }

            recordsByDefName = LoadRecords();
        }

        private static Dictionary<string, DefaultStatConfigRecord> LoadRecords()
        {
            var records = new Dictionary<string, DefaultStatConfigRecord>(StringComparer.Ordinal);
            var contentPack = StatCompressionMod.ContentPack;
            if (contentPack == null)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Cannot load default stat config table before ModContentPack is available.");
                return records;
            }

            var path = Path.Combine(contentPack.RootDir, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Default stat config table not found: {path}");
                return records;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Failed to read default stat config table: {path}\n{ex}");
                return records;
            }

            if (lines.Length == 0)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Default stat config table is empty: {path}");
                return records;
            }

            var headers = BuildHeaderIndex(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].NullOrEmpty())
                {
                    continue;
                }

                if (!TryParseRecord(lines[i], headers, out var record, out var error))
                {
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] Skipping invalid default stat config line {i + 1}: {error}");
                    continue;
                }

                records[record.defName] = record;
            }

            Log.Message($"[{StatCompressionConstants.DisplayName}] Loaded default stat config table: rows={records.Count}, path={path}");
            return records;
        }

        private static Dictionary<string, int> BuildHeaderIndex(string headerLine)
        {
            var headers = headerLine.Split('\t');
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Length; i++)
            {
                result[headers[i].Trim('\ufeff')] = i;
            }

            return result;
        }

        private static bool TryParseRecord(
            string line,
            Dictionary<string, int> headers,
            out DefaultStatConfigRecord record,
            out string error)
        {
            record = null;
            error = null;
            var columns = line.Split('\t');
            var defName = Get(columns, headers, "defName");
            if (defName.NullOrEmpty())
            {
                error = "missing defName";
                return false;
            }

            if (!bool.TryParse(Get(columns, headers, "enabled"), out var enabled))
            {
                error = $"invalid enabled for {defName}";
                return false;
            }

            if (!Enum.TryParse(Get(columns, headers, "method"), out CompressionMethod method))
            {
                error = $"invalid method for {defName}";
                return false;
            }

            if (!TryParseFloat(Get(columns, headers, "method_t"), out var methodT))
            {
                methodT = 2f;
            }

            if (!TryParseFloat(Get(columns, headers, "tScale"), out var tScale))
            {
                tScale = 1f;
            }

            if (!TryParseFloat(Get(columns, headers, "baseline"), out var baseline))
            {
                error = $"invalid baseline for {defName}";
                return false;
            }

            if (!TryParseFloat(Get(columns, headers, "thresholdFactor"), out var thresholdFactor))
            {
                thresholdFactor = 1f;
            }

            if (!Enum.TryParse(Get(columns, headers, "direction"), out StatCompressionDirection direction))
            {
                error = $"invalid direction for {defName}";
                return false;
            }

            record = new DefaultStatConfigRecord
            {
                defName = defName,
                enabled = enabled,
                method = method,
                methodT = methodT,
                tScale = tScale,
                baseline = baseline,
                thresholdFactor = thresholdFactor,
                direction = direction,
                source = Get(columns, headers, "source"),
                note = Get(columns, headers, "note")
            };
            return true;
        }

        private static string Get(string[] columns, Dictionary<string, int> headers, string name)
        {
            if (!headers.TryGetValue(name, out var index) || index < 0 || index >= columns.Length)
            {
                return string.Empty;
            }

            return columns[index];
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    internal sealed class DefaultStatConfigRecord
    {
        public string defName;
        public bool enabled;
        public CompressionMethod method;
        public float methodT;
        public float tScale;
        public float baseline;
        public float thresholdFactor;
        public StatCompressionDirection direction;
        public string source;
        public string note;
    }
}
