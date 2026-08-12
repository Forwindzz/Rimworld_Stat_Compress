using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace StatCompression
{
    public sealed class StatCompressionBenchmarkComponent : GameComponent
    {
        private bool scheduled;

        public StatCompressionBenchmarkComponent(Game game)
        {
        }

        public override void StartedNewGame()
        {
            ScheduleIfEnabled();
        }

        public override void LoadedGame()
        {
            ScheduleIfEnabled();
        }

        private void ScheduleIfEnabled()
        {
            if (scheduled || StatCompressionMod.Settings?.benchmarkOnGameLoad != true)
            {
                return;
            }

            scheduled = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var map = Find.CurrentMap;
                if (map == null || map.mapPawns.FreeColonistsSpawnedCount == 0)
                {
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] Benchmark cancelled: no spawned free colonist was available.");
                    return;
                }

                StatCompressionBenchmark.Run(map.mapPawns.FreeColonistsSpawned[0]);
            });
        }
    }

    internal static class StatCompressionBenchmark
    {
        private const int Iterations = 10000;
        private const int WarmupIterations = 256;

        private sealed class Candidate
        {
            public StatDef stat;
            public float original;
            public bool triggersCompression;
        }

        private struct BackendResult
        {
            public CompressionBackend backend;
            public float directValue;
            public double directMilliseconds;
            public double directNanosecondsPerCall;
            public double fullMilliseconds;
            public double fullNanosecondsPerCall;
            public double checksum;
        }

        public static void Run(Pawn pawn)
        {
            var settings = StatCompressionMod.Settings;
            if (settings == null || pawn == null)
            {
                return;
            }

            if (!settings.enabled)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Benchmark skipped because stat compression is disabled.");
                return;
            }

            try
            {
                var candidate = SelectCandidate(settings, pawn);
                if (candidate == null)
                {
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] Benchmark cancelled: no enabled pawn stat could be evaluated for {pawn.LabelShortCap}.");
                    return;
                }

                var request = StatRequest.For(pawn);
                var previousBackend = StatCompressionRuntime.ActiveBackend;
                var results = new List<BackendResult>(3);
                try
                {
                    results.Add(MeasureBackend(settings, request, candidate, CompressionBackend.Generic));
                    results.Add(MeasureBackend(settings, request, candidate, CompressionBackend.CompiledStatic));
                    results.Add(MeasureBackend(settings, request, candidate, CompressionBackend.DynamicMethod));
                }
                finally
                {
                    StatCompressionRuntime.SetActiveBackend(previousBackend);
                }

                LogResults(pawn, candidate, results);
                Messages.Message(
                    StatCompressionText.T("StatCompression_BenchmarkCompleted", candidate.stat.LabelCap),
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }
            catch (Exception ex)
            {
                Log.Error($"[{StatCompressionConstants.DisplayName}] Benchmark failed.\n{ex}");
            }
        }

        private static Candidate SelectCandidate(StatCompressionSettings settings, Pawn pawn)
        {
            var request = StatRequest.For(pawn);
            var triggered = new List<Candidate>();
            var fallback = new List<Candidate>();

            foreach (var config in settings.StatConfigs)
            {
                if (config == null || !config.enabled)
                {
                    continue;
                }

                var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
                if (stat == null)
                {
                    continue;
                }

                try
                {
                    if (!stat.Worker.ShouldShowFor(request))
                    {
                        continue;
                    }

                    var original = GetUncompressedValue(stat, request);
                    var candidate = new Candidate
                    {
                        stat = stat,
                        original = original,
                        triggersCompression = StatCompressionRuntime.TryComputeCompressedValue(
                            settings,
                            config,
                            original,
                            out var compressed) && Math.Abs(original - compressed) > 0.000001f
                    };
                    fallback.Add(candidate);
                    if (candidate.triggersCompression)
                    {
                        triggered.Add(candidate);
                    }
                }
                catch
                {
                    // A compatibility benchmark should skip stats that third-party workers cannot evaluate here.
                }
            }

            var source = triggered.Count > 0 ? triggered : fallback;
            if (source.Count == 0)
            {
                return null;
            }

            var random = new Random(Environment.TickCount ^ pawn.thingIDNumber);
            return source[random.Next(source.Count)];
        }

        private static BackendResult MeasureBackend(
            StatCompressionSettings settings,
            StatRequest request,
            Candidate candidate,
            CompressionBackend backend)
        {
            StatCompressionRuntime.SetActiveBackend(backend);

            var directValue = candidate.original;
            for (var i = 0; i < WarmupIterations; i++)
            {
                directValue = StatCompressionRuntime.ComputeForBackend(
                    settings,
                    backend,
                    candidate.stat.index,
                    candidate.original);
            }

            var start = Stopwatch.GetTimestamp();
            double checksum = 0d;
            for (var i = 0; i < Iterations; i++)
            {
                directValue = StatCompressionRuntime.ComputeForBackend(
                    settings,
                    backend,
                    candidate.stat.index,
                    candidate.original);
                checksum += directValue;
            }

            var directTicks = Stopwatch.GetTimestamp() - start;

            for (var i = 0; i < WarmupIterations; i++)
            {
                checksum += candidate.stat.Worker.GetValue(request, true);
            }

            start = Stopwatch.GetTimestamp();
            for (var i = 0; i < Iterations; i++)
            {
                checksum += candidate.stat.Worker.GetValue(request, true);
            }

            var fullTicks = Stopwatch.GetTimestamp() - start;
            return new BackendResult
            {
                backend = backend,
                directValue = directValue,
                directMilliseconds = ToMilliseconds(directTicks),
                directNanosecondsPerCall = ToNanosecondsPerCall(directTicks),
                fullMilliseconds = ToMilliseconds(fullTicks),
                fullNanosecondsPerCall = ToNanosecondsPerCall(fullTicks),
                checksum = checksum
            };
        }

        private static float GetUncompressedValue(StatDef stat, StatRequest request)
        {
            StatCompressionRuntime.BeginSuppression();
            try
            {
                return stat.Worker.GetValue(request, true);
            }
            finally
            {
                StatCompressionRuntime.EndSuppression();
            }
        }

        private static void LogResults(Pawn pawn, Candidate candidate, List<BackendResult> results)
        {
            Log.Message(
                $"[{StatCompressionConstants.DisplayName}] Benchmark: pawn={pawn.LabelShortCap}, stat={candidate.stat.defName}, " +
                $"original={candidate.original:R}, triggersCompression={candidate.triggersCompression}, iterations={Iterations}.");

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] Benchmark {result.backend}: " +
                    $"direct={result.directMilliseconds:F3} ms ({result.directNanosecondsPerCall:F1} ns/call), " +
                    $"fullStat={result.fullMilliseconds:F3} ms ({result.fullNanosecondsPerCall:F1} ns/call), " +
                    $"result={result.directValue:R}, checksum={result.checksum:R}.");
            }

            var reference = results[0].directValue;
            for (var i = 1; i < results.Count; i++)
            {
                if (!NearlyEqual(reference, results[i].directValue))
                {
                    Log.Error(
                        $"[{StatCompressionConstants.DisplayName}] Benchmark output mismatch: " +
                        $"Generic={reference:R}, {results[i].backend}={results[i].directValue:R}.");
                }
            }
        }

        private static bool NearlyEqual(float left, float right)
        {
            if (float.IsNaN(left) || float.IsNaN(right))
            {
                return float.IsNaN(left) && float.IsNaN(right);
            }

            if (left.Equals(right))
            {
                return true;
            }

            return Math.Abs(left - right) <= 0.0001f * Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
        }

        private static double ToMilliseconds(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }

        private static double ToNanosecondsPerCall(long ticks)
        {
            return ticks * 1000000000d / Stopwatch.Frequency / Iterations;
        }
    }
}
