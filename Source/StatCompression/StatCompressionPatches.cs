using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatWorker_FinalizeValue_Patch
    {
        public static bool BeforePostProcessPatchApplied { get; private set; }
        public static int CurveBlockMatchCount { get; private set; }

        private static readonly MethodInfo CompressMethod =
            AccessTools.Method(typeof(StatWorker_FinalizeValue_Patch), nameof(CompressBeforePostProcess));

        private static readonly FieldInfo PostProcessCurveField =
            AccessTools.Field(typeof(StatDef), nameof(StatDef.postProcessCurve));

        private static readonly FieldInfo StatField =
            AccessTools.Field(typeof(StatWorker), "stat");

        private static readonly MethodInfo CurveEvaluateMethod =
            AccessTools.Method(typeof(SimpleCurve), nameof(SimpleCurve.Evaluate), new[] { typeof(float) });

        [HarmonyPriority(Priority.Last)]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var matches = FindCurveBlocks(codes);
            CurveBlockMatchCount = matches.Count;
            if (matches.Count != 1)
            {
                BeforePostProcessPatchApplied = false;
                return codes;
            }

            var insertionIndex = matches[0];

            var injected = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, StatField),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Call, CompressMethod)
            };

            injected[0].labels.AddRange(codes[insertionIndex].labels);
            injected[0].blocks.AddRange(codes[insertionIndex].blocks);
            codes[insertionIndex].labels.Clear();
            codes[insertionIndex].blocks.Clear();
            codes.InsertRange(insertionIndex, injected);
            BeforePostProcessPatchApplied = true;
            return codes;
        }

        private static List<int> FindCurveBlocks(List<CodeInstruction> codes)
        {
            var matches = new List<int>();
            for (var i = 0; i + 14 < codes.Count; i++)
            {
                if (!codes[i].IsLdarg(3) || !IsBranchFalse(codes[i + 1], out var firstTarget) ||
                    !codes[i + 2].IsLdarg(0) || !codes[i + 3].LoadsField(StatField) ||
                    !codes[i + 4].LoadsField(PostProcessCurveField) ||
                    !IsBranchFalse(codes[i + 5], out var secondTarget) ||
                    !codes[i + 6].IsLdarg(2) || !codes[i + 7].IsLdarg(0) ||
                    !codes[i + 8].LoadsField(StatField) || !codes[i + 9].LoadsField(PostProcessCurveField) ||
                    !codes[i + 10].IsLdarg(2) || codes[i + 11].opcode != OpCodes.Ldind_R4 ||
                    !codes[i + 12].Calls(CurveEvaluateMethod) || codes[i + 13].opcode != OpCodes.Stind_R4)
                {
                    continue;
                }

                var firstTargetIndex = FindLabelTarget(codes, firstTarget);
                var secondTargetIndex = FindLabelTarget(codes, secondTarget);
                if (firstTargetIndex != i + 14 || secondTargetIndex != firstTargetIndex)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        private static int FindLabelTarget(List<CodeInstruction> codes, Label label)
        {
            return codes.FindIndex(instruction => instruction.labels.Contains(label));
        }

        private static bool IsBranchFalse(CodeInstruction instruction, out Label target)
        {
            if ((instruction.opcode == OpCodes.Brfalse || instruction.opcode == OpCodes.Brfalse_S) &&
                instruction.operand is Label label)
            {
                target = label;
                return true;
            }

            target = default(Label);
            return false;
        }

        public static void CompressBeforePostProcess(StatDef stat, ref float value, bool applyPostProcess)
        {
            var settings = StatCompressionMod.Settings;
            if (!StatCompressionRuntime.CanRun(settings, applyPostProcess))
            {
                return;
            }

            StatCompressionRuntime.Compress(stat, ref value);
        }

    }

    internal static class StatWorker_GetValue_Patch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(StatDef ___stat, StatRequest req, bool applyPostProcess, ref float __result)
        {
            StatCompressionRuntime.CaptureExplanationRaw(___stat, req, applyPostProcess, __result);
            var settings = StatCompressionMod.Settings;
            if (!StatCompressionRuntime.CanRun(settings, applyPostProcess))
            {
                return;
            }

            StatCompressionRuntime.Compress(___stat, ref __result);
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetExplanationFinalizePart))]
    internal static class StatWorker_GetExplanationFinalizePart_Patch
    {
        public static void Prefix(
            StatDef ___stat,
            StatRequest req,
            out StatCompressionRuntime.ExplanationContext __state)
        {
            __state = StatCompressionRuntime.BeginExplanation(___stat, req);
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            float finalVal,
            StatCompressionRuntime.ExplanationContext __state,
            ref string __result)
        {
            if (__state == null)
            {
                return;
            }

            if (StatCompressionRuntime.TryBuildExplanation(__state, finalVal, out var explanation))
            {
                __result += "\n" + explanation;
            }
        }

        public static Exception Finalizer(
            Exception __exception,
            StatCompressionRuntime.ExplanationContext __state)
        {
            if (__state != null)
            {
                StatCompressionRuntime.EndExplanation(__state);
            }

            return __exception;
        }
    }
}
