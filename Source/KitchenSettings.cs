using HarmonyLib;
using UnityEngine;
using Verse;

namespace CleanKitchen;

public sealed class KitchenMod : Mod
{
    internal const string PackageId = "feliphemp.cleanbeforecooking";
    internal static KitchenSettings Settings { get; private set; }

    public KitchenMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<KitchenSettings>();
        new Harmony(PackageId).PatchAll(typeof(KitchenMod).Assembly);
    }

    public override string SettingsCategory() => "Clean Before Cooking";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled("Clean before cooking", ref Settings.Enabled);
        listing.Label($"Radius around the cooking spot: {Settings.Radius} tiles");
        Settings.Radius = Mathf.RoundToInt(listing.Slider(Settings.Radius, 1, 12));
        listing.Label($"Maximum filth targets per bill: {Settings.TargetLimit}");
        Settings.TargetLimit = Mathf.RoundToInt(listing.Slider(Settings.TargetLimit, 1, 20));
        listing.CheckboxLabeled("Require Cleaning in the Work tab", ref Settings.RequireCleaningWork,
            "Pawns incapable of cleaning always skip cleanup.");
        listing.CheckboxLabeled("Write cleanup decisions to the game log", ref Settings.DebugLogging);
        if (listing.ButtonText("Restore defaults"))
            Settings.Reset();
        listing.End();
    }

    internal static void Trace(Pawn pawn, string message)
    {
        if (Settings.DebugLogging)
            Log.Message($"[Clean Kitchen] {pawn.LabelShortCap}: {message}");
    }
}

internal sealed class KitchenSettings : ModSettings
{
    public bool Enabled = true;
    public int Radius = 5;
    public int TargetLimit = 5;
    public bool RequireCleaningWork;
    public bool DebugLogging;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref Enabled, "enabled", true);
        Scribe_Values.Look(ref Radius, "radius", 5);
        Scribe_Values.Look(ref TargetLimit, "targetLimit", 5);
        Scribe_Values.Look(ref RequireCleaningWork, "requireCleaningWork", false);
        Scribe_Values.Look(ref DebugLogging, "debugLogging", false);
        Radius = Mathf.Clamp(Radius, 1, 12);
        TargetLimit = Mathf.Clamp(TargetLimit, 1, 20);
    }

    public void Reset()
    {
        Enabled = true;
        Radius = 5;
        TargetLimit = 5;
        RequireCleaningWork = false;
        DebugLogging = false;
    }
}
