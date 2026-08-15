namespace StatCompression
{
    internal static class StatCompressionRuntimeCoordinator
    {
        private static bool dirty;

        public static void MarkDirty()
        {
            dirty = true;
        }

        public static bool ApplyIfDirty(StatCompressionSettings settings)
        {
            if (!dirty)
            {
                return false;
            }

            Rebuild(settings);
            dirty = false;
            return true;
        }

        public static void Initialize(StatCompressionSettings settings)
        {
            Rebuild(settings);
            dirty = false;
        }

        private static void Rebuild(StatCompressionSettings settings)
        {
            settings.NormalizeForRuntime();
            settings.RebuildStatIndex();
            ObjectTargetFilterRuntime.Rebuild(settings.ObjectTargetFilter);
            StatCompressionRuntime.RebuildRuntimePlan(settings);
            BodyPartHealthCompressionModule.NotifySettingsChanged(settings);
            BaseDamageCompressionModule.NotifySettingsChanged(settings);
            HediffStageCompressionModule.NotifySettingsChanged(settings);
        }
    }
}
