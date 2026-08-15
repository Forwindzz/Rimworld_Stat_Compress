using System;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed class StatCompressionPresetNameWindow : Window
    {
        private readonly Action<string> accepted;
        private string name = string.Empty;

        public StatCompressionPresetNameWindow(Action<string> accepted)
        {
            this.accepted = accepted;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            optionalTitle = StatCompressionText.T("StatCompression_Preset_CreateTitle");
        }

        public override Vector2 InitialSize => new Vector2(460f, 170f);

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f),
                StatCompressionText.T("StatCompression_Preset_Name"));
            GUI.SetNextControlName("StatCompressionPresetName");
            name = Widgets.TextField(new Rect(inRect.x, inRect.y + 30f, inRect.width, 30f), name);
            GUI.FocusControl("StatCompressionPresetName");

            var buttonWidth = (inRect.width - 8f) / 2f;
            if (Widgets.ButtonText(
                    new Rect(inRect.x, inRect.yMax - 36f, buttonWidth, 32f),
                    StatCompressionText.T("StatCompression_Preset_Create")))
            {
                accepted(name);
                Close();
            }

            if (Widgets.ButtonText(
                    new Rect(inRect.x + buttonWidth + 8f, inRect.yMax - 36f, buttonWidth, 32f),
                    "CancelButton".Translate()))
            {
                Close();
            }
        }
    }
}
