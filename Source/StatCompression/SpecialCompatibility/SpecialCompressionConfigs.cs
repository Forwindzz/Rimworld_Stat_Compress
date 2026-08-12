namespace StatCompression
{
    internal static class SpecialCompressionConfigs
    {
        public const string Prefix = "[SP]";
        public const string BodyPartHealthDefName = "[SP] 部位HP";

        public static bool IsSpecial(string defName)
        {
            return defName != null && defName.StartsWith(Prefix);
        }

        public static string LabelFor(string defName)
        {
            return defName == BodyPartHealthDefName
                ? StatCompressionText.T("StatCompression_SP_BodyPartHealth_Label")
                : defName;
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
    }
}
