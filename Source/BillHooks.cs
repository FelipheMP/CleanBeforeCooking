using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace CleanKitchen;

[HarmonyPatch]
internal static class BillHooks
{
    private static readonly ConditionalWeakTable<JobDriver_DoBill, SavedCleanup> CleanupByDriver = new();

    private sealed class SavedCleanup
    {
        public KitchenCleanup Value;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    private static IEnumerable<Toil> WrapCookingStep(IEnumerable<Toil> __result, JobDriver_DoBill __instance)
    {
        foreach (Toil toil in __result)
        {
            if (toil.debugName == nameof(Toils_Recipe.MakeUnfinishedThingIfNeeded) &&
                toil.defaultCompleteMode == ToilCompleteMode.Instant &&
                KitchenCleanup.IsMealBill(__instance.job))
            {
                Wrap(toil, __instance);
            }

            yield return toil;
        }
    }

    private static void Wrap(Toil toil, JobDriver_DoBill driver)
    {
        var saved = CleanupByDriver.GetValue(driver, _ => new SavedCleanup());
        var vanillaInit = toil.initAction;

        // The save stores numeric toil indexes. Keep the original slot even when settings change.
        toil.defaultCompleteMode = ToilCompleteMode.Never;
        toil.initAction = () =>
        {
            saved.Value = KitchenCleanup.Plan(driver);
            if (saved.Value == null)
                ContinueBill();
        };
        toil.tickIntervalAction = delta =>
        {
            // An unmodded save can already be sitting on this normally instant step.
            if (saved.Value == null || saved.Value.Tick(driver, delta))
                ContinueBill();
        };
        toil.AddFinishAction(() =>
        {
            saved.Value?.Interrupt(driver);
            saved.Value = null;
        });

        void ContinueBill()
        {
            saved.Value = null;
            vanillaInit?.Invoke();
            driver.ReadyForNextToil();
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(JobDriver_DoBill), nameof(JobDriver_DoBill.ExposeData))]
    private static void SaveActiveCleanup(JobDriver_DoBill __instance)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            var saved = CleanupByDriver.GetValue(__instance, _ => new SavedCleanup());
            Scribe_Deep.Look(ref saved.Value, "feliphempKitchenCleanup");
        }
        else if (Scribe.mode == LoadSaveMode.Saving &&
            CleanupByDriver.TryGetValue(__instance, out var saved) && saved.Value != null)
        {
            Scribe_Deep.Look(ref saved.Value, "feliphempKitchenCleanup");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Notify_PatherFailed))]
    private static bool AllowVanillaPathFailure(JobDriver __instance)
    {
        // Losing the path to optional filth should skip it. A failed return to the stove
        // still uses the game's normal bill failure behavior.
        if (__instance is JobDriver_DoBill bill &&
            CleanupByDriver.TryGetValue(bill, out var saved) &&
            saved.Value is { WalkingToFilth: true } cleanup)
        {
            cleanup.PathFailed = true;
            return false;
        }

        return true;
    }
}
