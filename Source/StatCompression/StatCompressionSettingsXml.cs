using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionSettingsXml
    {
        public const int CurrentVersion = 2;
        public const string RootName = "StatCompressionSettings";

        public static bool TryGetRoot(XDocument document, out XElement root, out string error)
        {
            root = document?.Root;
            if (root == null || root.Name != RootName)
            {
                error = $"expected root {RootName}";
                return false;
            }

            var versionText = Attr(root, "version");
            if (!versionText.NullOrEmpty() &&
                (!int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ||
                 version < 1 || version > CurrentVersion))
            {
                error = $"unsupported schema version {versionText}";
                return false;
            }

            error = null;
            return true;
        }

        public static int VersionOf(XElement root)
        {
            return int.TryParse(
                Attr(root, "version"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var version)
                ? version
                : 1;
        }

        public static void ReadGlobal(XElement element, DefaultGlobalSettings target)
        {
            if (element == null || target == null)
            {
                return;
            }

            if (bool.TryParse(Attr(element, "enabled"), out var enabled))
            {
                target.enabled = enabled;
            }

            if (bool.TryParse(Attr(element, "showInfoCardSettingsButton"), out var showInfoCardSettingsButton))
            {
                target.showInfoCardSettingsButton = showInfoCardSettingsButton;
            }

            if (Enum.TryParse(Attr(element, "stage"), out CompressionStage stage))
            {
                target.stage = stage;
            }

            if (bool.TryParse(Attr(element, "autoFallbackToGlobalPostfix"), out var fallback))
            {
                target.autoFallbackToGlobalPostfix = fallback;
            }

            if (Enum.TryParse(Attr(element, "method"), out CompressionMethod method) &&
                method != CompressionMethod.FollowGlobal)
            {
                target.method = method;
            }

            if (TryParseFloat(Attr(element, "parameter"), out var parameter))
            {
                target.parameter = parameter;
            }

            if (TryParseFloat(Attr(element, "thresholdFactor"), out var thresholdFactor))
            {
                target.thresholdFactor = thresholdFactor;
            }
        }

        public static void ReadBodyPartHealth(XElement element, StatCompressionStatConfig target)
        {
            if (element == null)
            {
                return;
            }

            ApplyStatElement(element, target);
            target.defName = SpecialCompressionConfigs.BodyPartHealthDefName;
        }

        public static int ReadSpecialDamageConfigs(
            XElement element,
            System.Collections.Generic.IList<StatCompressionStatConfig> targets)
        {
            return ReadSpecialConfigs(element, targets, false);
        }

        public static int ReadSpecialHediffStageConfigs(
            XElement element,
            System.Collections.Generic.IList<StatCompressionStatConfig> targets)
        {
            return ReadSpecialConfigs(element, targets, true);
        }

        public static bool TryReadDefaultStat(
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

        public static void ApplyStatElement(XElement element, StatCompressionStatConfig config)
        {
            if (bool.TryParse(Attr(element, "enabled"), out var enabled)) config.enabled = enabled;
            if (Enum.TryParse(Attr(element, "method"), out CompressionMethod method)) config.method = method;
            if (TryParseFloat(Attr(element, "method_t"), out var methodT)) config.method_t = methodT;
            if (TryParseFloat(Attr(element, "tScale"), out var tScale)) config.tScale = tScale;
            if (TryParseFloat(Attr(element, "baseline"), out var baseline)) config.baseline = baseline;
            if (TryParseFloat(Attr(element, "thresholdFactor"), out var threshold)) config.thresholdFactor = threshold;
            if (Enum.TryParse(Attr(element, "direction"), out StatCompressionDirection direction)) config.direction = direction;
        }

        public static bool TryReadConfig(
            XElement element,
            out StatCompressionStatConfig config,
            out string error)
        {
            config = null;
            error = null;
            var defName = SpecialCompressionConfigs.CanonicalizeId(Attr(element, "defName"));
            if (defName.NullOrEmpty())
            {
                error = "missing defName";
                return false;
            }

            config = new StatCompressionStatConfig { defName = defName };
            ApplyStatElement(element, config);
            NormalizeConfigForXml(config);
            return true;
        }

        public static XDocument CreateDocument(StatCompressionSettings settings)
        {
            return new XDocument(
                new XElement(
                    RootName,
                    new XAttribute("version", CurrentVersion),
                    new XElement(
                        "Global",
                        new XAttribute("enabled", settings.enabled),
                        new XAttribute("showInfoCardSettingsButton", settings.showInfoCardSettingsButton),
                        new XAttribute("stage", settings.stage),
                        new XAttribute("autoFallbackToGlobalPostfix", settings.autoFallbackToGlobalPostfix),
                        new XAttribute("method", settings.method),
                        new XAttribute("parameter", FormatFloat(settings.parameter)),
                        new XAttribute("thresholdFactor", FormatFloat(settings.thresholdFactor))),
                    new XElement(
                        "BodyPartHealth",
                        ConfigAttributes(settings.BodyPartHealthConfig)),
                    new XElement(
                        "SpecialDamageConfigs",
                        settings.SpecialDamageConfigs.Select(config =>
                            new XElement("Config", ConfigAttributes(config)))),
                    new XElement(
                        "SpecialHediffStageConfigs",
                        settings.SpecialHediffStageConfigs.Select(config =>
                            new XElement("Config", ConfigAttributes(config)))),
                    new XElement(
                        "ActivePresets",
                        settings.activePresets
                            .Where(name => !name.NullOrEmpty())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                            .Select(name => new XElement("Preset", new XAttribute("name", name)))),
                    new XElement(
                        "Stats",
                        settings.statConfigs
                            .Where(config => config != null && !config.defName.NullOrEmpty())
                            .OrderBy(config => config.defName, StringComparer.Ordinal)
                            .Select(CreateStatElement))));
        }

        public static string Attr(XElement element, string name)
        {
            return element?.Attribute(name)?.Value ?? string.Empty;
        }

        public static System.Collections.Generic.List<string> ReadActivePresets(XElement element)
        {
            if (element == null)
            {
                return new System.Collections.Generic.List<string>();
            }

            return element.Elements("Preset")
                .Select(preset => Attr(preset, "name"))
                .Where(name => !name.NullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static XElement CreateConfigElement(
            string elementName,
            StatCompressionStatConfig config)
        {
            return new XElement(elementName, ConfigAttributes(config));
        }

        private static XElement CreateStatElement(StatCompressionStatConfig config)
        {
            return new XElement(
                "Stat",
                ConfigAttributes(config));
        }

        private static int ReadSpecialConfigs(
            XElement element,
            System.Collections.Generic.IList<StatCompressionStatConfig> targets,
            bool hediffStage)
        {
            if (element == null || targets == null)
            {
                return 0;
            }

            var updated = 0;
            var byName = targets.ToDictionary(config => config.defName, StringComparer.Ordinal);
            foreach (var configElement in element.Elements("Config"))
            {
                var defName = SpecialCompressionConfigs.CanonicalizeId(Attr(configElement, "defName"));
                if (!byName.TryGetValue(defName, out var config))
                {
                    continue;
                }

                ApplyStatElement(configElement, config);
                config.direction = hediffStage
                    ? SpecialCompressionConfigs.DirectionForHediffStage(defName)
                    : StatCompressionDirection.HigherIsBetter;
                updated++;
            }

            return updated;
        }

        private static object[] ConfigAttributes(StatCompressionStatConfig config)
        {
            return new object[]
            {
                new XAttribute("defName", config.defName),
                new XAttribute("enabled", config.enabled),
                new XAttribute("method", config.method),
                new XAttribute("method_t", FormatFloat(config.method_t)),
                new XAttribute("tScale", FormatFloat(config.tScale)),
                new XAttribute("baseline", FormatFloat(config.baseline)),
                new XAttribute("thresholdFactor", FormatFloat(config.thresholdFactor)),
                new XAttribute("direction", config.direction)
            };
        }

        private static void NormalizeConfigForXml(StatCompressionStatConfig config)
        {
            StatCompressionSettings.NormalizeConfig(config);
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class DefaultGlobalSettings
    {
        public bool enabled = true;
        public bool showInfoCardSettingsButton = true;
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
