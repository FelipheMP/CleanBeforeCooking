using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace CleanKitchen;

// Only an in-progress cleanup is saved, as part of the current vanilla bill driver.
internal sealed class KitchenCleanup : IExposable
{
    private List<Thing> targets;
    private int nextTarget;
    private Thing currentTarget;
    private float cleaningWork;
    private bool returning;

    internal bool WalkingToFilth;
    internal bool PathFailed;

    public KitchenCleanup() { } // Scribe constructs this when loading an active cleanup.

    internal static bool IsMealBill(Job job)
    {
        RecipeDef recipe = job.RecipeDef;
        return recipe?.workSkill == SkillDefOf.Cooking &&
            recipe.products != null &&
            recipe.products.Exists(product =>
                product.thingDef.ingestible is { preferability: >= FoodPreferability.MealTerrible });
    }

    internal static KitchenCleanup Plan(JobDriver_DoBill driver)
    {
        Pawn cook = driver.pawn;
        if (!KitchenMod.Settings.Enabled || !cook.IsColonist)
            return null;

        if (!CanClean(cook))
        {
            KitchenMod.Trace(cook, "Cleanup skipped: pawn cannot clean under the current work settings.");
            return null;
        }

        if (!TryGetKitchen(driver, out IntVec3 origin, out Room room))
        {
            KitchenMod.Trace(cook, "Cleanup skipped: no indoor cooking spot.");
            return null;
        }

        var candidates = new List<Thing>();
        foreach (IntVec3 cell in GenRadial.RadialCellsAround(origin, KitchenMod.Settings.Radius, true))
        {
            if (!cell.InBounds(cook.Map) || cell.GetRoom(cook.Map) != room)
                continue;

            foreach (Thing thing in cell.GetThingList(cook.Map))
            {
                if (thing is not Filth filth)
                    continue;

                string reason = SkipReason(cook, filth, room);
                if (reason == null)
                    candidates.Add(filth);
                else
                    KitchenMod.Trace(cook, $"Skipped {filth.Label} at {cell}: {reason}.");
            }
        }

        candidates.Sort((a, b) => a.Position.DistanceToSquared(origin)
            .CompareTo(b.Position.DistanceToSquared(origin)));
        if (candidates.Count > KitchenMod.Settings.TargetLimit)
            candidates.RemoveRange(KitchenMod.Settings.TargetLimit, candidates.Count - KitchenMod.Settings.TargetLimit);

        if (candidates.Count == 0)
        {
            KitchenMod.Trace(cook, "Cleanup skipped: no eligible filth.");
            return null;
        }

        KitchenMod.Trace(cook, $"Scheduled {candidates.Count} filth target(s) before {driver.job.RecipeDef.label}.");
        return new KitchenCleanup { targets = candidates };
    }

    internal bool Tick(JobDriver_DoBill driver, int delta)
    {
        Pawn cook = driver.pawn;
        Building station = driver.job.targetA.Thing as Building;
        if (!returning && KitchenMod.Settings.Enabled && CanClean(cook) &&
            TryGetKitchen(driver, out _, out Room room))
        {
            if (currentTarget == null && WalkingToFilth)
            {
                KitchenMod.Trace(cook, "Skipped target: filth disappeared before arrival.");
                ReleaseCurrent(driver);
            }

            if (currentTarget == null && SelectNext(driver, room))
                return false;

            if (currentTarget is Filth filth)
            {
                CleanCurrent(driver, filth, room, delta);
                return false;
            }
        }

        // Recipe work expects the cook at the interaction cell, even after a target is skipped.
        if (!returning)
        {
            if (currentTarget != null)
                KitchenMod.Trace(cook, "Cleanup stopped before the current target was completed.");
            ReleaseCurrent(driver);
            returning = true;
        }

        if (station?.Spawned != true)
        {
            driver.EndJobWith(JobCondition.Incompletable);
            return false;
        }

        if (cook.CanReachImmediate(station, PathEndMode.InteractionCell))
            return true;

        // StartPath resolves InteractionCell to a cell destination.
        if (!cook.pather.Moving || cook.pather.Destination != (LocalTargetInfo)station.InteractionCell)
            cook.pather.StartPath(station, PathEndMode.InteractionCell);
        return false;
    }

    private bool SelectNext(JobDriver_DoBill driver, Room room)
    {
        Pawn cook = driver.pawn;
        while (targets != null && nextTarget < targets.Count)
        {
            Thing target = targets[nextTarget++];
            targets[nextTarget - 1] = null; // Save the active target separately from pending targets.
            if (target is not Filth filth)
                continue;

            string reason = SkipReason(cook, filth, room);
            if (reason != null || !cook.Reserve(filth, driver.job, errorOnFailed: false))
            {
                KitchenMod.Trace(cook, $"Skipped {filth.Label}: {reason ?? "reservation failed"}.");
                continue;
            }

            currentTarget = filth;
            cleaningWork = 0f;
            KitchenMod.Trace(cook, $"Started cleanup of {filth.Label} at {filth.Position}.");
            return true;
        }

        return false;
    }

    private void CleanCurrent(JobDriver_DoBill driver, Filth filth, Room room, int delta)
    {
        Pawn cook = driver.pawn;
        string reason = PathFailed ? "path failed" : SkipReason(cook, filth, room, checkReachability: false);
        if (reason != null)
        {
            KitchenMod.Trace(cook, $"Skipped {filth.Label}: {reason}.");
            ReleaseCurrent(driver);
            return;
        }

        if (!cook.CanReachImmediate(filth, PathEndMode.Touch))
        {
            if (!cook.pather.Moving || cook.pather.Destination != (LocalTargetInfo)filth)
            {
                WalkingToFilth = true;
                cook.pather.StartPath(filth, PathEndMode.Touch);
            }
            return;
        }

        if (WalkingToFilth)
        {
            cook.pather.StopDead();
            WalkingToFilth = false;
        }

        // Match vanilla's cleaning speed, terrain modifier, and one layer per work interval.
        float speed = cook.GetStatValue(StatDefOf.CleaningSpeed);
        if (speed <= 0f)
        {
            KitchenMod.Trace(cook, "Skipped target: cleaning speed is zero.");
            ReleaseCurrent(driver);
            return;
        }

        float terrainFactor = filth.Position.GetTerrain(cook.Map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
        cleaningWork += speed * delta / (terrainFactor > 0f ? terrainFactor : 1f);
        if (cleaningWork <= filth.def.filth.cleaningWorkToReduceThickness)
            return;

        cleaningWork = 0f;
        string label = filth.Label;
        filth.ThinFilth();
        if (!filth.Destroyed)
            return;

        cook.records.Increment(RecordDefOf.MessesCleaned);
        KitchenMod.Trace(cook, $"Completed cleanup of {label}.");
        ReleaseCurrent(driver);
    }

    private static bool CanClean(Pawn cook)
    {
        WorkTypeDef cleaning = WorkTypeDefOf.Cleaning;
        return !cook.WorkTypeIsDisabled(cleaning) &&
            (!KitchenMod.Settings.RequireCleaningWork || cook.workSettings?.WorkIsActive(cleaning) == true);
    }

    private static bool TryGetKitchen(JobDriver_DoBill driver, out IntVec3 origin, out Room room)
    {
        origin = IntVec3.Invalid;
        room = null;
        if (driver.job.targetA.Thing is not Building { Spawned: true } station ||
            !station.def.hasInteractionCell)
            return false;

        origin = station.InteractionCell;
        Map map = station.Map;
        if (!origin.InBounds(map) || !origin.Roofed(map))
            return false;

        room = origin.GetRoom(map);
        return room != null && !room.IsDoorway && !room.OutdoorsForWork;
    }

    private static string SkipReason(Pawn cook, Filth filth, Room room, bool checkReachability = true)
    {
        if (!filth.Spawned || filth.Map != cook.Map)
            return "filth already gone";
        if (filth.Position.GetRoom(cook.Map) != room)
            return "outside the cooking room";
        if (!cook.Map.areaManager.Home[filth.Position] || filth.Fogged())
            return "outside the visible Home area";
        if (!filth.Position.InAllowedArea(cook) || filth.IsForbidden(cook))
            return "forbidden or outside the pawn's allowed area";
        if (!cook.CanReserve(filth))
            return "reserved";
        if (checkReachability && !cook.CanReach(filth, PathEndMode.Touch, Danger.Some))
            return "unreachable at the allowed danger level";
        return null;
    }

    internal void Interrupt(JobDriver_DoBill driver)
    {
        KitchenMod.Trace(driver.pawn, "Cleanup interrupted before the bill resumed.");
        ReleaseCurrent(driver);
    }

    private void ReleaseCurrent(JobDriver_DoBill driver)
    {
        Pawn cook = driver.pawn;
        if (WalkingToFilth)
            cook.pather.StopDead();

        if (currentTarget is { Destroyed: false } && cook.Map is Map map &&
            map.reservationManager.ReservedBy(currentTarget, cook, driver.job))
            map.reservationManager.Release(currentTarget, cook, driver.job);

        currentTarget = null;
        WalkingToFilth = false;
        PathFailed = false;
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
        Scribe_Values.Look(ref nextTarget, "nextTarget");
        Scribe_References.Look(ref currentTarget, "currentTarget");
        Scribe_Values.Look(ref cleaningWork, "cleaningWork");
        Scribe_Values.Look(ref returning, "returning");

        Scribe_Values.Look(ref WalkingToFilth, "walkingToFilth");
        Scribe_Values.Look(ref PathFailed, "pathFailed");
    }
}
