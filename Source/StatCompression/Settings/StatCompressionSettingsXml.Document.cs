using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace StatCompression
{
    internal static partial class StatCompressionSettingsXml
    {
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
                    CreateObjectTargetFilterElement(settings.ObjectTargetFilter),
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

        private static XElement CreateObjectTargetFilterElement(
            ObjectTargetFilterSettings settings)
        {
            return new XElement(
                "ObjectTargetFilter",
                new XAttribute("enabled", settings.enabled),
                new XAttribute("playerColonists", settings.playerColonists),
                new XAttribute("playerOtherPawns", settings.playerOtherPawns),
                new XAttribute("hostilePawns", settings.hostilePawns),
                new XAttribute("nonHostilePawns", settings.nonHostilePawns),
                new XAttribute("factionlessPawns", settings.factionlessPawns),
                CreateStringList("RaceDefs", "Def", settings.raceDefNames, false),
                CreateStringList("FactionDefs", "Def", settings.factionDefNames, false),
                CreateStringList(
                    "SourceMods",
                    "Mod",
                    settings.sourceModPackageIds,
                    true));
        }

        private static XElement CreateStringList(
            string rootName,
            string itemName,
            System.Collections.Generic.IEnumerable<string> values,
            bool packageIdAttribute)
        {
            var comparer = packageIdAttribute
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            return new XElement(
                rootName,
                values
                    .Where(value => !value.NullOrEmpty())
                    .Distinct(comparer)
                    .OrderBy(value => value, comparer)
                    .Select(value => packageIdAttribute
                        ? new XElement(itemName, new XAttribute("packageId", value))
                        : new XElement(itemName, value)));
        }

        private static System.Collections.Generic.List<string> ReadStringList(
            XElement root,
            string itemName,
            bool packageIdAttribute)
        {
            if (root == null)
            {
                return new System.Collections.Generic.List<string>();
            }

            var comparer = packageIdAttribute
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            return root.Elements(itemName)
                .Select(element => packageIdAttribute
                    ? Attr(element, "packageId")
                    : element.Value)
                .Where(value => !value.NullOrEmpty())
                .Distinct(comparer)
                .ToList();
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
}
