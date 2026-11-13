# Clean Before Cooking

A RimWorld 1.7 mod by **FelipheMP**. When a colonist starts an indoor meal bill, they bring the ingredients to the workstation, clean nearby eligible filth, return to the interaction cell, and start recipe work.

## Install on Windows

1. Install and enable **Harmony** with RimWorld 1.7 support. For a manual installation, use the [Harmony release archive](https://github.com/pardeike/HarmonyRimWorld/releases).
2. Extract `CleanBeforeCooking.zip` into the game's `Mods` directory. Check that the resulting path is `RimWorld/Mods/CleanBeforeCooking/About/About.xml`.
3. Enable **Clean Before Cooking** in RimWorld's Mods screen, below Harmony, and restart the game when prompted.
4. Load a copy of your colony. Under Proton, use the `Mods` directory next to the Windows game executable. Steam's **Manage → Browse local files** opens the game folder.

## What the cook does

The check happens once per bill job, after RimWorld collects the ingredients and before the recipe starts. It applies to automatic and prioritized indoor cooking. A bill qualifies if it uses the Cooking skill and produces a food item with a meal-level food preference. This includes vanilla meals and cooked survival food. A modded meal must also use the standard bill sequence. Butchering, brewing, paste dispensers, outdoor campfires, and workstations without an interaction cell are outside that path.

The cook looks within the configured radius of the interaction cell, in the same roofed cooking room. Eligible filth must be in the visible Home area, within the pawn's allowed area, unforbidden, reachable, and available to reserve. Fresh filth is eligible immediately. The cook takes the nearest eligible targets up to the limit. If a target disappears or becomes invalid, the cook skips it. Cleanup uses the pawn's cleaning speed and the floor's cleaning time modifier. Once the pass ends, cooking proceeds. Dirt floors, room cleanliness scores, and new filth appearing after target selection do not trigger another pass in the current job.

### Mod settings

Open **Options → Mod Settings → Clean Before Cooking**.

| Setting | Default |
|---|---|
| Clean before cooking | On |
| Radius around the cooking spot | 5 tiles, adjustable from 1 to 12 |
| Maximum filth targets per bill | 5, adjustable from 1 to 20 |
| Require Cleaning in the Work tab | Off. Pawns incapable of Cleaning always skip. |
| Write cleanup decisions to the game log | Off |

With logging enabled, filter the developer log for `[Clean Kitchen]`. **Scheduled** counts the targets selected for that bill. **Started** means the pawn reserved a target. **Completed** means that pawn removed its final layer. Skips and interruptions have their own messages. The scheduled count can exceed completions if another pawn gets there first or the job ends early.

## Read the code in this order

1. **`About/About.xml`** tells RimWorld which mod this is, which version it supports, and that it needs Harmony.
2. **`Source/KitchenSettings.cs`** is the mod entry point. RimWorld creates the `Mod` object, which loads settings and registers the Harmony patches. `ExposeData` saves the player's options.
3. **`Source/BillHooks.cs`** wraps a *toil*, one step in a pawn job. The vanilla bill driver normally runs an immediate preparation step just before recipe work. This mod lets that same step wait for cleanup. Keeping the step at the same index matters because RimWorld saves a job's current step by number. The other hooks save active cleanup progress and let a failed walk to filth skip that target. A failed walk back to the stove uses the game's normal failure handling.
4. **`Source/KitchenCleanup.cs`** selects filth and runs the cleanup. It reserves one target at a time, checks eligibility again before cleaning, calls the game's `ThinFilth` method to remove layers, and sends the cook back to the station. RimWorld's bill job still owns the ingredients, recipe work, product storage, and completion count.

## Rebuild after editing

Install the **.NET 10 SDK**. Open a terminal in the source project directory containing this README and `Source/`. Point `RIMWORLD_DIR` at the Windows RimWorld 1.7 installation folder containing `Assembly-CSharp.dll`. Point `HARMONY_DLL` at Harmony's 1.7 `0Harmony.dll`. The examples assume a local Harmony install named `HarmonyMod`. For a Workshop install, substitute the actual path to the assembly under its `1.7/Assemblies` folder.

### Windows (PowerShell)

```powershell
$env:RIMWORLD_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$env:HARMONY_DLL = "$env:RIMWORLD_DIR\Mods\HarmonyMod\1.7\Assemblies\0Harmony.dll"
dotnet build .\Source\CleanKitchen.csproj -c Release
```

### Linux (Bash)

```bash
export RIMWORLD_DIR="$HOME/.local/share/Steam/steamapps/common/RimWorld"
export HARMONY_DLL="$RIMWORLD_DIR/Mods/HarmonyMod/1.7/Assemblies/0Harmony.dll"
dotnet build ./Source/CleanKitchen.csproj -c Release
```

The build writes `Assemblies/CleanKitchen.dll`. To package a build, put `About/`, that DLL under `Assemblies/`, and `README.md` inside a folder named `CleanBeforeCooking`, then ZIP that folder. Game and Harmony assemblies are supplied by the installation.

## Test on a copy of a save

1. Use **Save As** on your existing colony. Enable diagnostic logging and disable overlapping pre-cooking cleanup from other mods for the first run.
2. Put visible filth in a small roofed kitchen with Home coverage. Give an eligible colonist a simple meal bill and ingredients. Watch them deliver the ingredients, remove the filth, return to the interaction cell, and produce the meal. Check the bill count, ingredient consumption, and product storage. Repeat with **Prioritize cooking**.
3. Repeat with a clean room, a radius or target limit of 1, filth across a doorway, and an outdoor campfire. Confirm that a cook with Cleaning disabled in the Work tab still cleans with the default setting and skips once that setting is required. Try a pawn incapable of Cleaning and a pawn whose allowed area excludes the filth.
4. Use two cooks or an ordinary cleaner in the same room. Watch for duplicate work and check that unavailable filth is skipped. Draft a cook during the cleanup, then undraft and confirm the colony can cook again. Cancel or suspend a bill during a pass and check that it produces no meal.
5. Save and reload while walking to filth, while cleaning it, and while returning to the stove. Check that the pass resumes and the bill still completes once. To test adding the mod, load a copy made with a vanilla meal already in progress and verify the following meal starts with the cleanup check.
6. For removal, finish or cancel active cooking jobs, save a separate copy, disable the mod, and reload that copy. Settings are stored in RimWorld's mod configuration. The only extra save data belongs to an active cleanup, so remove the mod between cooking jobs.

Local build target: Windows RimWorld **1.7.4976 rev1043** with Harmony **3.0.1.0**. The gameplay checks above require the running game.
