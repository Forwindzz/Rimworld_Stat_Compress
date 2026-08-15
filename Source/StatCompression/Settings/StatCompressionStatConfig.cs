using Verse;

namespace StatCompression
{
    public sealed class StatCompressionStatConfig : IExposable
    {
        public string defName;
        public bool enabled;
        public CompressionMethod method = CompressionMethod.Logarithmic;
        public float method_t = 2f;
        public float tScale = 1f;
        public float baseline;
        public float thresholdFactor = 1f;
        public StatCompressionDirection direction = StatCompressionDirection.HigherIsBetter;

        public StatCompressionStatConfig()
        {
        }

        public StatCompressionStatConfig(
            string defName,
            bool enabled,
            CompressionMethod method,
            float methodT,
            float tScale,
            float baseline,
            float thresholdFactor,
            StatCompressionDirection direction)
        {
            this.defName = defName;
            this.enabled = enabled;
            this.method = method;
            this.method_t = methodT;
            this.tScale = tScale;
            this.baseline = baseline;
            this.thresholdFactor = thresholdFactor;
            this.direction = direction;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref method, "method", CompressionMethod.Logarithmic);
            Scribe_Values.Look(ref method_t, "method_t", 2f);
            Scribe_Values.Look(ref tScale, "tScale", 1f);
            Scribe_Values.Look(ref baseline, "baseline", 0f);
            Scribe_Values.Look(ref thresholdFactor, "thresholdFactor", 1f);
            Scribe_Values.Look(ref direction, "direction", StatCompressionDirection.HigherIsBetter);

            var legacyManualBaseline = 0f;
            Scribe_Values.Look(ref legacyManualBaseline, "manualBaseline", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && baseline == 0f && legacyManualBaseline != 0f)
            {
                baseline = legacyManualBaseline;
            }
        }

        public void CopyFrom(StatCompressionStatConfig source)
        {
            defName = source.defName;
            enabled = source.enabled;
            method = source.method;
            method_t = source.method_t;
            tScale = source.tScale;
            baseline = source.baseline;
            thresholdFactor = source.thresholdFactor;
            direction = source.direction;
        }
    }
}
