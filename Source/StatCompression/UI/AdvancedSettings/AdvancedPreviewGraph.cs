using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed partial class AdvancedPreviewComponent
    {
        private void DrawPreviewGraph(Rect rect)
        {
            if (rect.height < 90f || rect.width < 220f)
            {
                return;
            }

            var graph = state.Graph;
            var plot = new Rect(rect.x + 46f, rect.y + 20f, rect.width - 56f, rect.height - 46f);
            Widgets.DrawBoxSolid(plot, new Color(0.08f, 0.09f, 0.1f, 0.72f));

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(plot.x, rect.y, plot.width * 0.5f, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewGraphYAxis"));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(
                new Rect(plot.x + plot.width * 0.5f, rect.y, plot.width * 0.5f, 18f),
                StatCompressionText.T(
                    graph.SignedAxis
                        ? "StatCompression_AdvancedPreviewGraphXAxisSigned"
                        : "StatCompression_AdvancedPreviewGraphXAxis"));

            var gridColor = new Color(0.55f, 0.57f, 0.59f, 0.28f);
            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4f;
                var y = Mathf.Lerp(plot.yMax, plot.y, fraction);
                Widgets.DrawLine(
                    new Vector2(plot.x, y),
                    new Vector2(plot.xMax, y),
                    gridColor,
                    1f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(
                    new Rect(rect.x, y - 9f, 42f, 18f),
                    FormatAxisPercent(Mathf.Lerp(graph.YMin, graph.YMax, fraction)));
            }

            if (graph.SignedAxis)
            {
                for (var i = 0; i <= 4; i++)
                {
                    var fraction = i / 4f;
                    var axisValue = Mathf.Lerp(graph.AxisMin, graph.AxisMax, fraction);
                    DrawInputGridLine(
                        plot,
                        fraction,
                        FormatAxisPercent(InverseInputAxis(axisValue, true)),
                        gridColor);
                }
            }
            else
            {
                var firstDecade = (int)Math.Ceiling(graph.AxisMin);
                var lastDecade = (int)Math.Floor(graph.AxisMax);
                for (var decade = firstDecade; decade <= lastDecade; decade++)
                {
                    DrawInputGridLine(
                        plot,
                        (decade - graph.AxisMin) / (graph.AxisMax - graph.AxisMin),
                        FormatAxisPercent((float)Math.Pow(10d, decade)),
                        gridColor);
                }
            }

            if (graph.ThresholdPercent >= graph.MinInput &&
                graph.ThresholdPercent <= graph.MaxInput)
            {
                var thresholdX = Mathf.Lerp(
                    plot.x,
                    plot.xMax,
                    (TransformInputAxis(graph.ThresholdPercent, graph.SignedAxis) - graph.AxisMin) /
                    (graph.AxisMax - graph.AxisMin));
                Widgets.DrawLine(
                    new Vector2(thresholdX, plot.y),
                    new Vector2(thresholdX, plot.yMax),
                    new Color(0.95f, 0.72f, 0.25f, 0.8f),
                    1.5f);
            }

            var curveColor = new Color(0.35f, 0.78f, 0.92f, 1f);
            var hasPrevious = false;
            var previous = Vector2.zero;
            for (var i = 0; i <= GraphSegmentCount; i++)
            {
                if (!graph.Valid[i])
                {
                    hasPrevious = false;
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(plot.x, plot.xMax, i / (float)GraphSegmentCount),
                    Mathf.Lerp(
                        plot.yMax,
                        plot.y,
                        Mathf.InverseLerp(graph.YMin, graph.YMax, graph.Outputs[i])));
                if (hasPrevious)
                {
                    Widgets.DrawLine(previous, point, curveColor, 2f);
                }

                previous = point;
                hasPrevious = true;
            }

            for (var i = 0; i < state.Percents.Length; i++)
            {
                var inputPercent = state.Percents[i];
                var outputPercent = state.MappedPercents[i];
                if (float.IsNaN(outputPercent) || float.IsInfinity(outputPercent))
                {
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(
                        plot.x,
                        plot.xMax,
                        (TransformInputAxis(inputPercent, graph.SignedAxis) - graph.AxisMin) /
                        (graph.AxisMax - graph.AxisMin)),
                    Mathf.Lerp(
                        plot.yMax,
                        plot.y,
                        Mathf.InverseLerp(graph.YMin, graph.YMax, outputPercent)));
                Widgets.DrawBoxSolid(
                    new Rect(point.x - 2f, point.y - 2f, 4f, 4f),
                    Color.white);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWordWrap;
        }

    }
}
