namespace StatCompression
{
    internal static class SpecialCompressionConfigs
    {
        public const string Prefix = "[SP]";
        public const string BodyPartHealthDefName = "[SP] BodyPartHealth";
        public const string MeleeBaseDamageDefName = "[SP] MeleeBaseDamage";
        public const string RangedBaseDamageDefName = "[SP] RangedBaseDamage";
        public const string MeleeExtraDamageDefName = "[SP] MeleeExtraDamage";
        public const string RangedExtraDamageDefName = "[SP] RangedExtraDamage";
        public const string ExplosionDamageDefName = "[SP] ExplosionDamage";
        public const string OtherDamageDefName = "[SP] OtherDamage";
        public const string NaturalHealingFactorDefName = "[SP] NaturalHealingFactor";
        public const string RegenerationRateDefName = "[SP] RegenerationRate";
        public const string TotalBleedFactorDefName = "[SP] TotalBleedFactor";
        public const string HungerRateFactorDefName = "[SP] HungerRateFactor";
        public const string RestFallFactorDefName = "[SP] RestFallFactor";
        public const string FoodPoisoningChanceFactorDefName = "[SP] FoodPoisoningChanceFactor";

        private const string LegacyBodyPartHealthDefName = "[SP] 部位HP";
        private const string LegacyMeleeBaseDamageDefName = "[SP] 近战基础伤害";
        private const string LegacyRangedBaseDamageDefName = "[SP] 远程基础伤害";
        private const string LegacyMeleeExtraDamageDefName = "[SP] 近战额外伤害";
        private const string LegacyRangedExtraDamageDefName = "[SP] 远程额外伤害";
        private const string LegacyExplosionDamageDefName = "[SP] 爆炸伤害";
        private const string LegacyOtherDamageDefName = "[SP] 其他伤害";

        public static readonly string[] DamageDefNames =
        {
            MeleeBaseDamageDefName,
            RangedBaseDamageDefName,
            MeleeExtraDamageDefName,
            RangedExtraDamageDefName,
            ExplosionDamageDefName,
            OtherDamageDefName
        };

        public static readonly string[] HediffStageDefNames =
        {
            NaturalHealingFactorDefName,
            RegenerationRateDefName,
            TotalBleedFactorDefName,
            HungerRateFactorDefName,
            RestFallFactorDefName,
            FoodPoisoningChanceFactorDefName
        };

        public static bool IsSpecial(string defName)
        {
            return defName != null && defName.StartsWith(Prefix);
        }

        public static string CanonicalizeId(string defName)
        {
            switch (defName)
            {
                case LegacyBodyPartHealthDefName:
                    return BodyPartHealthDefName;
                case LegacyMeleeBaseDamageDefName:
                    return MeleeBaseDamageDefName;
                case LegacyRangedBaseDamageDefName:
                    return RangedBaseDamageDefName;
                case LegacyMeleeExtraDamageDefName:
                    return MeleeExtraDamageDefName;
                case LegacyRangedExtraDamageDefName:
                    return RangedExtraDamageDefName;
                case LegacyExplosionDamageDefName:
                    return ExplosionDamageDefName;
                case LegacyOtherDamageDefName:
                    return OtherDamageDefName;
                default:
                    return defName;
            }
        }

        public static string LabelFor(string defName)
        {
            defName = CanonicalizeId(defName);
            switch (defName)
            {
                case BodyPartHealthDefName:
                    return StatCompressionText.T("StatCompression_SP_BodyPartHealth_Label");
                case MeleeBaseDamageDefName:
                    return StatCompressionText.T("StatCompression_SP_MeleeBaseDamage_Label");
                case RangedBaseDamageDefName:
                    return StatCompressionText.T("StatCompression_SP_RangedBaseDamage_Label");
                case MeleeExtraDamageDefName:
                    return StatCompressionText.T("StatCompression_SP_MeleeExtraDamage_Label");
                case RangedExtraDamageDefName:
                    return StatCompressionText.T("StatCompression_SP_RangedExtraDamage_Label");
                case ExplosionDamageDefName:
                    return StatCompressionText.T("StatCompression_SP_ExplosionDamage_Label");
                case OtherDamageDefName:
                    return StatCompressionText.T("StatCompression_SP_OtherDamage_Label");
                case NaturalHealingFactorDefName:
                    return StatCompressionText.T("StatCompression_SP_NaturalHealingFactor_Label");
                case RegenerationRateDefName:
                    return StatCompressionText.T("StatCompression_SP_RegenerationRate_Label");
                case TotalBleedFactorDefName:
                    return StatCompressionText.T("StatCompression_SP_TotalBleedFactor_Label");
                case HungerRateFactorDefName:
                    return StatCompressionText.T("StatCompression_SP_HungerRateFactor_Label");
                case RestFallFactorDefName:
                    return StatCompressionText.T("StatCompression_SP_RestFallFactor_Label");
                case FoodPoisoningChanceFactorDefName:
                    return StatCompressionText.T("StatCompression_SP_FoodPoisoningChanceFactor_Label");
                default:
                    return defName;
            }
        }

        public static bool IsDamage(string defName)
        {
            defName = CanonicalizeId(defName);
            for (var i = 0; i < DamageDefNames.Length; i++)
            {
                if (DamageDefNames[i] == defName)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsHediffStage(string defName)
        {
            defName = CanonicalizeId(defName);
            for (var i = 0; i < HediffStageDefNames.Length; i++)
            {
                if (HediffStageDefNames[i] == defName)
                {
                    return true;
                }
            }

            return false;
        }

        public static StatCompressionDirection DirectionForHediffStage(string defName)
        {
            return defName == NaturalHealingFactorDefName || defName == RegenerationRateDefName
                ? StatCompressionDirection.HigherIsBetter
                : StatCompressionDirection.LowerIsBetter;
        }

        public static StatCompressionStatConfig CreateBodyPartHealth()
        {
            return new StatCompressionStatConfig(
                BodyPartHealthDefName,
                false,
                CompressionMethod.Logarithmic,
                2f,
                1f,
                1f,
                1f,
                StatCompressionDirection.HigherIsBetter);
        }

        public static System.Collections.Generic.List<StatCompressionStatConfig> CreateDamageConfigs()
        {
            return new System.Collections.Generic.List<StatCompressionStatConfig>
            {
                CreateDamage(MeleeBaseDamageDefName, 20f),
                CreateDamage(RangedBaseDamageDefName, 20f),
                CreateDamage(MeleeExtraDamageDefName, 10f),
                CreateDamage(RangedExtraDamageDefName, 10f),
                CreateDamage(ExplosionDamageDefName, 50f),
                CreateDamage(OtherDamageDefName, 10f)
            };
        }

        public static System.Collections.Generic.List<StatCompressionStatConfig> CreateHediffStageConfigs()
        {
            var result = new System.Collections.Generic.List<StatCompressionStatConfig>(HediffStageDefNames.Length);
            for (var i = 0; i < HediffStageDefNames.Length; i++)
            {
                var defName = HediffStageDefNames[i];
                result.Add(new StatCompressionStatConfig(
                    defName,
                    false,
                    CompressionMethod.Logarithmic,
                    2f,
                    1f,
                    1f,
                    1f,
                    DirectionForHediffStage(defName)));
            }

            return result;
        }

        private static StatCompressionStatConfig CreateDamage(string defName, float baseline)
        {
            return new StatCompressionStatConfig(
                defName,
                false,
                CompressionMethod.Logarithmic,
                2f,
                1f,
                baseline,
                1f,
                StatCompressionDirection.HigherIsBetter);
        }
    }
}
