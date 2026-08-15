using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace StatCompression
{
    public sealed partial class StatCompressionSettings
    {
        public static float NormalizeParameter(CompressionMethod method, float parameter)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return Math.Max(0f, parameter);
                case CompressionMethod.Exponential:
                    return Math.Max(0.001f, parameter);
                case CompressionMethod.Logarithmic:
                    return Math.Max(1.001f, parameter);
                case CompressionMethod.SoftCap:
                    return Math.Max(0.001f, parameter);
                default:
                    return parameter;
            }
        }

        public static void NormalizeConfig(StatCompressionStatConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.method_t = NormalizeParameter(config.method, config.method_t);
            config.tScale = Math.Max(0.0001f, config.tScale);
            config.baseline = Math.Max(1e-10f, config.baseline);
            if (config.direction == StatCompressionDirection.LowerIsBetter)
            {
                config.thresholdFactor = Math.Max(0.0001f, config.thresholdFactor);
            }
        }

        private void MigrateLegacyGlobalFollowers()
        {
            var legacyGlobalMethod = method;
            if (bodyPartHealthConfig != null && bodyPartHealthConfig.method == legacyGlobalMethod)
            {
                bodyPartHealthConfig.method = CompressionMethod.FollowGlobal;
            }

            MigrateLegacyGlobalFollowers(specialDamageConfigs, legacyGlobalMethod);
            MigrateLegacyGlobalFollowers(specialHediffStageConfigs, legacyGlobalMethod);
            MigrateLegacyGlobalFollowers(statConfigs, legacyGlobalMethod);
        }

        private void EnsureInvariantsAfterLoadOrImport(
            bool legacyBodyPartHealthEnabled = false)
        {
            if (bodyPartHealthConfig == null)
            {
                bodyPartHealthConfig = SpecialCompressionConfigs.CreateBodyPartHealth();
                bodyPartHealthConfig.enabled = legacyBodyPartHealthEnabled;
            }

            bodyPartHealthConfig.defName = SpecialCompressionConfigs.BodyPartHealthDefName;
            EnsureSpecialDamageConfigs();
            EnsureSpecialHediffStageConfigs();
            statConfigs = statConfigs ?? new List<StatCompressionStatConfig>();
            activePresets = activePresets ?? new List<string>();
            EnsureObjectTargetFilter();
        }

        private void MigrateLoadedSettings()
        {
            if (settingsVersion < 1)
            {
                MigrateLegacyGlobalFollowers();
                settingsVersion = 1;
            }

            if (settingsVersion < CurrentSettingsVersion)
            {
                settingsVersion = CurrentSettingsVersion;
            }
        }

        private static void MigrateLegacyGlobalFollowers(
            List<StatCompressionStatConfig> configs,
            CompressionMethod legacyGlobalMethod)
        {
            if (configs == null)
            {
                return;
            }

            for (var i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                if (config != null && config.method == legacyGlobalMethod)
                {
                    config.method = CompressionMethod.FollowGlobal;
                }
            }
        }

        private static void MigrateLegacyImportedMethod(
            StatCompressionStatConfig config,
            CompressionMethod legacyGlobalMethod,
            int schemaVersion)
        {
            if (schemaVersion < 2 && config.method == legacyGlobalMethod)
            {
                config.method = CompressionMethod.FollowGlobal;
            }
        }

        private static void MigrateLegacyImportedElement(
            XElement element,
            StatCompressionStatConfig config,
            CompressionMethod legacyGlobalMethod)
        {
            if (element != null && config.method == legacyGlobalMethod)
            {
                config.method = CompressionMethod.FollowGlobal;
            }
        }

        private static void MigrateLegacyImportedSpecialElements(
            XElement parent,
            List<StatCompressionStatConfig> configs,
            CompressionMethod legacyGlobalMethod)
        {
            if (parent == null)
            {
                return;
            }

            var importedNames = new HashSet<string>(
                parent.Elements("Config")
                    .Select(element => SpecialCompressionConfigs.CanonicalizeId(
                        StatCompressionSettingsXml.Attr(element, "defName"))),
                StringComparer.Ordinal);
            for (var i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                if (importedNames.Contains(config.defName) && config.method == legacyGlobalMethod)
                {
                    config.method = CompressionMethod.FollowGlobal;
                }
            }
        }

        public static bool PassesStaticDefaultRules(StatDef stat)
        {
            if (stat == null)
            {
                return false;
            }

            if (!stat.showOnPawns)
            {
                return false;
            }

            if (stat.postProcessCurve != null)
            {
                return false;
            }

            if (HasExplicitMaxValue(stat))
            {
                return false;
            }

            return true;
        }

        private static bool HasExplicitMaxValue(StatDef stat)
        {
            return Math.Abs(stat.maxValue - DefaultMaxValue) > 0.001f;
        }
    }
}
