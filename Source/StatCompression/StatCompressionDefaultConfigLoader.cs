using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionDefaultConfigLoader
    {
        private const string RelativePath = "Data/DefaultSettings.xml";

        private static DefaultSettingsPreset preset;

        public static bool TryGet(string defName, out DefaultStatConfigRecord record)
        {
            EnsureLoaded();
            return preset.recordsByDefName.TryGetValue(defName, out record);
        }

        public static void ApplyGlobalDefaults(StatCompressionSettings settings)
        {
            EnsureLoaded();
            var global = preset.global;
            settings.enabled = global.enabled;
            settings.stage = global.stage;
            settings.autoFallbackToGlobalPostfix = global.autoFallbackToGlobalPostfix;
            settings.method = global.method;
            settings.parameter = global.parameter;
            settings.thresholdFactor = global.thresholdFactor;
        }

        private static void EnsureLoaded()
        {
            if (preset != null)
            {
                return;
            }

            preset = LoadPreset();
        }

        private static DefaultSettingsPreset LoadPreset()
        {
            var result = DefaultSettingsPreset.CreateFallback();
            var contentPack = StatCompressionMod.ContentPack;
            if (contentPack == null)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Cannot load default settings before ModContentPack is available.");
                return result;
            }

            var path = Path.Combine(contentPack.RootDir, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Default settings XML not found: {path}");
                return result;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Failed to read default settings XML: {path}\n{ex}");
                return result;
            }

            var root = document.Root;
            if (root == null || root.Name != "StatCompressionSettings")
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Invalid default settings XML root: {path}");
                return result;
            }

            ParseGlobal(root.Element("Global"), result.global);
            var statsElement = root.Element("Stats");
            if (statsElement != null)
            {
                foreach (var element in statsElement.Elements("Stat"))
                {
                    if (!TryParseRecord(element, out var record, out var error))
                    {
                        Log.Warning($"[{StatCompressionConstants.DisplayName}] Skipping invalid default stat config: {error}");
                        continue;
                    }

                    result.recordsByDefName[record.defName] = record;
                }
            }

            Log.Message($"[{StatCompressionConstants.DisplayName}] Loaded default settings XML: stats={result.recordsByDefName.Count}, path={path}");
            return result;
        }

        private static void ParseGlobal(XElement element, DefaultGlobalSettings global)
        {
            if (element == null)
            {
                return;
            }

            if (bool.TryParse(Attr(element, "enabled"), out var enabled))
            {
                global.enabled = enabled;
            }

            if (Enum.TryParse(Attr(element, "stage"), out CompressionStage stage))
            {
                global.stage = stage;
            }

            if (bool.TryParse(Attr(element, "autoFallbackToGlobalPostfix"), out var fallback))
            {
                global.autoFallbackToGlobalPostfix = fallback;
            }

            if (Enum.TryParse(Attr(element, "method"), out CompressionMethod method))
            {
                global.method = method;
            }

            if (TryParseFloat(Attr(element, "parameter"), out var parameter))
            {
                global.parameter = parameter;
            }

            if (TryParseFloat(Attr(element, "thresholdFactor"), out var thresholdFactor))
            {
                global.thresholdFactor = thresholdFactor;
            }
        }

        private static bool TryParseRecord(
            XElement element,
            out DefaultStatConfigRecord record,
            out string error)
        {
            record = null;
            error = null;
            var defName = Attr(element, "defName");
            if (defName.NullOrEmpty())
            {
                error = "missing defName";
                return false;
            }

            if (!bool.TryParse(Attr(element, "enabled"), out var enabled))
            {
                error = $"invalid enabled for {defName}";
                return false;
            }

            if (!Enum.TryParse(Attr(element, "method"), out CompressionMethod method))
            {
                error = $"invalid method for {defName}";
                return false;
            }

            if (!TryParseFloat(Attr(element, "baseline"), out var baseline))
            {
                error = $"invalid baseline for {defName}";
                return false;
            }

            if (!Enum.TryParse(Attr(element, "direction"), out StatCompressionDirection direction))
            {
                error = $"invalid direction for {defName}";
                return false;
            }

            if (!TryParseFloat(Attr(element, "method_t"), out var methodT))
            {
                methodT = 2f;
            }

            if (!TryParseFloat(Attr(element, "tScale"), out var tScale))
            {
                tScale = 1f;
            }

            if (!TryParseFloat(Attr(element, "thresholdFactor"), out var thresholdFactor))
            {
                thresholdFactor = 1f;
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
                direction = direction
            };
            return true;
        }

        private static string Attr(XElement element, string name)
        {
            return element.Attribute(name)?.Value ?? string.Empty;
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    internal sealed class DefaultSettingsPreset
    {
        public readonly DefaultGlobalSettings global = new DefaultGlobalSettings();
        public readonly Dictionary<string, DefaultStatConfigRecord> recordsByDefName =
            new Dictionary<string, DefaultStatConfigRecord>(StringComparer.Ordinal);

        public static DefaultSettingsPreset CreateFallback()
        {
            return new DefaultSettingsPreset();
        }
    }

    internal sealed class DefaultGlobalSettings
    {
        public bool enabled = true;
        public CompressionStage stage = CompressionStage.BeforePostProcessCurve;
        public bool autoFallbackToGlobalPostfix = true;
        public CompressionMethod method = CompressionMethod.Logarithmic;
        public float parameter = 2f;
        public float thresholdFactor = 1f;
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
    }
}
