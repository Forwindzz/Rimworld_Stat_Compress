using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace StatCompression
{
    internal static partial class StatCompressionSettingsXml
    {
        public const int CurrentVersion = 3;
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

        public static void ReadObjectTargetFilter(
            XElement element,
            ObjectTargetFilterSettings target)
        {
            if (element == null || target == null)
            {
                return;
            }

            if (bool.TryParse(Attr(element, "enabled"), out var enabled))
                target.enabled = enabled;
            if (bool.TryParse(Attr(element, "playerColonists"), out var playerColonists))
                target.playerColonists = playerColonists;
            if (bool.TryParse(Attr(element, "playerOtherPawns"), out var playerOtherPawns))
                target.playerOtherPawns = playerOtherPawns;
            if (bool.TryParse(Attr(element, "hostilePawns"), out var hostilePawns))
                target.hostilePawns = hostilePawns;
            if (bool.TryParse(Attr(element, "nonHostilePawns"), out var nonHostilePawns))
                target.nonHostilePawns = nonHostilePawns;
            if (bool.TryParse(Attr(element, "factionlessPawns"), out var factionlessPawns))
                target.factionlessPawns = factionlessPawns;

            target.raceDefNames = ReadStringList(element.Element("RaceDefs"), "Def", false);
            target.factionDefNames = ReadStringList(element.Element("FactionDefs"), "Def", false);
            target.sourceModPackageIds = ReadStringList(
                element.Element("SourceMods"),
                "Mod",
                true);
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

            var enabledText = Attr(element, "enabled");
            if (!enabledText.NullOrEmpty() && !bool.TryParse(enabledText, out _))
            {
                error = $"invalid enabled for {defName}";
                return false;
            }

            var methodText = Attr(element, "method");
            if (!methodText.NullOrEmpty() &&
                (!Enum.TryParse(methodText, out CompressionMethod method) ||
                 !Enum.IsDefined(typeof(CompressionMethod), method)))
            {
                error = $"invalid method for {defName}";
                return false;
            }

            var directionText = Attr(element, "direction");
            if (!directionText.NullOrEmpty() &&
                (!Enum.TryParse(directionText, out StatCompressionDirection direction) ||
                 !Enum.IsDefined(typeof(StatCompressionDirection), direction)))
            {
                error = $"invalid direction for {defName}";
                return false;
            }

            var numericFields = new[]
            {
                "method_t",
                "tScale",
                "baseline",
                "thresholdFactor"
            };
            for (var i = 0; i < numericFields.Length; i++)
            {
                var value = Attr(element, numericFields[i]);
                if (!value.NullOrEmpty() && !TryParseFloat(value, out _))
                {
                    error = $"invalid {numericFields[i]} for {defName}";
                    return false;
                }
            }

            config = new StatCompressionStatConfig { defName = defName };
            ApplyStatElement(element, config);
            NormalizeConfigForXml(config);
            return true;
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
