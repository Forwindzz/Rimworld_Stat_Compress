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
