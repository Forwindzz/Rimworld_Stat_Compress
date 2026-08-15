using UnityEngine;
using Verse;

namespace StatCompression
{
    public sealed class StatCompressionMod : Mod
    {
        public static StatCompressionSettings Settings { get; private set; }
        public static ModContentPack ContentPack { get; private set; }
        private static StatCompressionMod Instance { get; set; }

        public StatCompressionMod(ModContentPack content) : base(content)
        {
            Instance = this;
            ContentPack = content;
            Settings = GetSettings<StatCompressionSettings>();
            StatCompressionMainSettingsPanel.Configure(Settings);

            LongEventHandler.ExecuteWhenFinished(InitializeAfterDefsLoaded);
        }

        public override string SettingsCategory()
        {
            return StatCompressionText.T("StatCompression_SettingsTitle");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            StatCompressionMainSettingsPanel.Draw(inRect);
        }

        public override void WriteSettings()
        {
            StatCompressionSettingsEditor.CommitPending(Settings);
            base.WriteSettings();
        }

        internal static void PersistSettings()
        {
            if (Instance != null)
            {
                Instance.WriteSettings();
                return;
            }

            StatCompressionSettingsEditor.CommitPending(Settings);
        }

        private void InitializeAfterDefsLoaded()
        {
            var needsInitialSetup = Settings.NeedsInitialSetup;
            Settings.EnsureStatConfigs();
            var seedResult = StatCompressionDefaultPresetSeeder.EnsureLocalTemplates();
            StatCompressionPresetRepository.Refresh();
            var migratedLegacyActivePreset =
                !seedResult.Failed &&
                StatCompressionDefaultPresetSeeder.MigrateLegacyActivePresetName(Settings);
            var appliedCount = 0;
            var skippedMissingConfigs = 0;
            if (needsInitialSetup &&
                !seedResult.Failed &&
                StatCompressionDefaultPresetSeeder.TryApplyDefaults(
                    Settings,
                    out appliedCount,
                    out skippedMissingConfigs))
            {
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] Applied {appliedCount} default presets " +
                    $"for a new global configuration; skippedMissingConfigs={skippedMissingConfigs}.");
            }

            if (seedResult.CreatedCount > 0)
            {
                if (!needsInitialSetup)
                {
                    Log.Message(
                        $"[{StatCompressionConstants.DisplayName}] Created {seedResult.CreatedCount} " +
                        "local default presets; existing global configuration was left unchanged.");
                }
                else if (appliedCount > 0)
                {
                    Log.Message(
                        $"[{StatCompressionConstants.DisplayName}] Created {seedResult.CreatedCount} " +
                        $"local default presets; applied {appliedCount} for a new global configuration; " +
                        $"skippedMissingConfigs={skippedMissingConfigs}.");
                }
            }

            if (needsInitialSetup)
            {
                Settings.CompleteInitialSetup();
            }

            StatCompressionRuntimeCoordinator.Initialize(Settings);

            if (needsInitialSetup || migratedLegacyActivePreset)
            {
                WriteSettings();
            }

            StatCompressionBootstrap.PatchAll();
        }
    }
}
