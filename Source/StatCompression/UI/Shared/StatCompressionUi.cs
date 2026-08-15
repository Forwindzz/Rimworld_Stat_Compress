using UnityEngine;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionUi
    {
        private static readonly Color FoldoutBackground =
            new Color(0.12f, 0.16f, 0.20f, 0.72f);

        private static readonly Color FoldoutBackgroundExpanded =
            new Color(0.15f, 0.21f, 0.27f, 0.82f);

        private static readonly Color FoldoutAccent =
            new Color(0.42f, 0.66f, 0.86f, 0.9f);

        internal static bool DrawFoldoutBar(
            Rect rect,
            bool expanded,
            string label,
            string trailingText = null)
        {
            Widgets.DrawBoxSolid(
                rect,
                expanded ? FoldoutBackgroundExpanded : FoldoutBackground);
            Widgets.DrawBoxSolid(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                FoldoutAccent);
            Widgets.DrawHighlightIfMouseover(rect);

            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = FoldoutAccent;
            Widgets.Label(
                new Rect(rect.x + 8f, rect.y, 20f, rect.height),
                expanded ? "\u25BC" : "\u25B6");

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(rect.x + 36f, rect.y, rect.width - 44f, rect.height),
                label);

            if (!trailingText.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.gray;
                Widgets.Label(
                    new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f - 10f, rect.height),
                    trailingText);
            }

            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
            return Widgets.ButtonInvisible(rect, true);
        }

        internal static bool DrawNavigationBar(Rect rect, string label)
        {
            Widgets.DrawBoxSolid(rect, FoldoutBackground);
            Widgets.DrawBoxSolid(
                new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                FoldoutAccent);
            Widgets.DrawHighlightIfMouseover(rect);

            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(rect.x + 16f, rect.y, rect.width - 52f, rect.height),
                label);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = FoldoutAccent;
            Widgets.Label(
                new Rect(rect.xMax - 32f, rect.y, 20f, rect.height),
                "\u25B6");

            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
            return Widgets.ButtonInvisible(rect, true);
        }
    }
}
