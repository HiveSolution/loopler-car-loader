using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LooplerCarLoader;

static class Patches
{
    internal static List<CarFile> Cars = new();
    internal static string RunFile;
    static readonly Dictionary<int, CarFile> byIndex = new();
    static int vanillaCount = -1;

    static CarFile Modded(int index) => byIndex.TryGetValue(index, out var c) ? c : null;
    static CarFile Modded(Car car) => car == null ? null : Modded(car.carIndex);

    // ---- Adding the cars --------------------------------------------------

    // Cars are appended after the game's own, before CarManager sets itself up.
    [HarmonyPrefix, HarmonyPatch(typeof(CarManager), nameof(CarManager.Awake))]
    static void Inject(CarManager __instance)
    {
        var cars = __instance.cars;
        var vanilla = new List<Car>();
        foreach (var c in cars) if (!c.name.StartsWith("LCL_")) vanilla.Add(c);
        if (vanilla.Count < cars.Count) return; // this list already has them
        vanillaCount = vanilla.Count;
        byIndex.Clear();

        foreach (var file in Cars)
        {
            var baseCar = vanilla.FirstOrDefault(c => CarFile.IsNamed(c, file.@base));
            if (baseCar == null)
            {
                Plugin.Logger.LogWarning($"{file.id}: base car \"{file.@base}\" doesn't exist, skipped. Known: {string.Join(", ", vanilla.Select(c => c.name.Substring(c.name.IndexOf('_') + 1)))}");
                continue;
            }
            file.car ??= Create(file, baseCar, vanilla.First(c => c.TryCast<Blue_Car>() != null));
            file.baseIndex = baseCar.carIndex;
            file.car.carIndex = vanillaCount + byIndex.Count;
            byIndex[file.car.carIndex] = file;
            cars.Add(file.car);
        }
        Plugin.Logger.LogInfo($"{byIndex.Count} modded car(s) added: {string.Join(", ", byIndex.Values.Select(c => $"{c.name} (#{c.car.carIndex}, base {c.@base})"))}");
    }

    // ScriptableObject.CreateInstance returns null for Car, so cars are copies: of the base
    // car to keep its ability subclass, or else of Durabler (Blue_Car overrides nothing).
    static Car Create(CarFile file, Car baseCar, Car plain)
    {
        Car car;
        if (file.keepBaseAbility) car = UnityEngine.Object.Instantiate(baseCar);
        else
        {
            car = UnityEngine.Object.Instantiate(plain);
            car.carName = baseCar.carName;
            car.carEffect = baseCar.carEffect;
            car.carSprite = baseCar.carSprite;
            car.lockedSprite = baseCar.lockedSprite;
            car.carTrigger = baseCar.carTrigger;
            car.durabilityMax = baseCar.durabilityMax;
            car.durabilityStart = baseCar.durabilityStart;
        }
        car.name = "LCL_" + file.id;
        car.hideFlags = HideFlags.DontUnloadUnusedAsset;
        car.excludeFromDemo = true;
        if (file.durability is int d) car.durabilityMax = car.durabilityStart = d;
        var sprite = file.LoadSprite(baseCar.carSprite);
        if (sprite != null) car.carSprite = sprite;
        return car;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Car), nameof(Car.GetLocalizedName))]
    static bool Name(Car __instance, ref string __result)
    {
        if (Modded(__instance) is not { } c) return true;
        __result = c.name;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Car), nameof(Car.GetLocalizedEffect))]
    static bool Effect(Car __instance, ref string __result)
    {
        if (Modded(__instance) is not { } c) return true;
        __result = c.effect ?? "";
        return false;
    }

    // ---- Abilities --------------------------------------------------------

    // Car's own virtuals are trivial one-liners that IL2CPP folds together with unrelated
    // functions, so patching them hooks half the game. CarManager's wrappers are safe.

    [HarmonyPostfix, HarmonyPatch(typeof(CarManager), nameof(CarManager.GetCarBaseStatMultiplier))]
    static void BaseStat(CarManager __instance, StatType statType, ref float __result)
    {
        if (Modded(__instance.car) is { } c && c.statMults.TryGetValue(statType, out var m)) __result *= m;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CarManager), nameof(CarManager.GetBonusUpgradeSlots))]
    static void Slots(CarManager __instance, ref int __result)
    {
        if (Modded(__instance.car) is { } c) __result += c.bonusUpgradeSlots;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CarManager), nameof(CarManager.HasExtraGateCheckpoint))]
    static void GateCheckpoint(CarManager __instance, ref bool __result)
    {
        if (Modded(__instance.car) is { } c) __result |= c.extraGateCheckpoint;
    }

    // ---- Car select -------------------------------------------------------

    static bool inMoveCar;

    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.MoveCar))]
    static void MoveCarStart() => inMoveCar = true;

    [HarmonyPostfix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.MoveCar))]
    static void MoveCarEnd() => inMoveCar = false;

    // The car carousel wraps at the vanilla car count, passed in from MoveCar.
    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.MoveCarousel))]
    static void CarouselCount(ref int count)
    {
        if (inMoveCar && count == vanillaCount) count += byIndex.Count;
    }

    // The game draws the car slots with indices wrapped at the vanilla count, and draws
    // incoming slots before the move ends. So once a picker settles (index or slot order
    // changed), its three slots are redrawn: modded cars by hand, vanilla ones by the game.
    static readonly List<TrackChoice> pickers = new();
    static readonly Dictionary<IntPtr, (int, IntPtr, IntPtr, IntPtr)> drawn = new();

    [HarmonyPostfix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.Awake))]
    static void PickerAwake(TrackChoice __instance) => pickers.Add(__instance);

    // Called every frame by CarouselFix.
    internal static void FixPickers()
    {
        if (byIndex.Count == 0) return;
        pickers.RemoveAll(p => p == null || p.WasCollected);
        int total = vanillaCount + byIndex.Count;
        foreach (var p in pickers)
        {
            var state = (p.carIndex, p.carLeft.Pointer, p.carCenter.Pointer, p.carRight.Pointer);
            if (drawn.TryGetValue(p.Pointer, out var last) && last == state) continue;
            drawn[p.Pointer] = state;
            int cur = p.carIndex;
            DrawSlot(p, p.carLeft, (cur - 1 + total) % total);
            DrawSlot(p, p.carCenter, cur);
            DrawSlot(p, p.carRight, (cur + 1) % total);
        }
    }

    static bool drawing;

    // The game's SetCarSlot reads per-car lists sized for the vanilla cars and throws on a
    // modded index (the carousel passes one during a move): draw those slots here instead.
    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.SetCarSlot))]
    static bool SlotDraw(RectTransform slot, int index)
    {
        if (Modded(index) is not { } c) return true;
        DrawModded(slot, c);
        return false;
    }

    // The game redrew a slot on its own (track switch, unlock check): fix the picker next frame.
    [HarmonyPostfix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.SetCarSlot))]
    static void SlotRedrawn(TrackChoice __instance)
    {
        if (!drawing) drawn.Remove(__instance.Pointer);
    }

    static void DrawSlot(TrackChoice p, RectTransform slot, int index)
    {
        drawing = true;
        try { p.SetCarSlot(slot, index); } finally { drawing = false; }
    }

    static void DrawModded(RectTransform slot, CarFile c)
    {
        foreach (var img in slot.GetComponentsInChildren<Image>(true))
        {
            if (img.name == "Car_Icon") img.sprite = c.car.carSprite;
            else if (img.name is "Completed_State" or "AllItems_State") img.gameObject.SetActive(false); // no progress for modded cars
        }
    }

    // Car tooltips. Hovering a car goes through ToggleTooltip, which gets the slot's last
    // vanilla car and checks "beat a run with the previous car" against the picker's index;
    // keyboard/controller focus goes through ShowCarouselTooltip with an unlocked flag.
    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.ShowCarouselTooltip))]
    static void CarTooltip(TrackChoice __instance, ref string itemName, ref string itemDesc, ref bool itemUnlocked, ref bool excludedFromDemo, bool isDifficulty)
    {
        if (isDifficulty || Modded(__instance.carIndex) is not { } c) return;
        itemName = c.name;
        itemDesc = c.effect ?? "";
        itemUnlocked = true;
        excludedFromDemo = false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.ToggleTooltip))]
    static void HoverTooltip(TrackChoice __instance, ref string name, ref string desc, ref Car car, ref bool excludedFromDemo)
    {
        if (car == null || Modded(__instance.carIndex) is not { } c) return;
        car = c.car;
        name = c.name;
        desc = c.effect ?? "";
        excludedFromDemo = false;
        // Check the lock as the starter car, which is always unlocked.
        swappedFrom = __instance.carIndex;
        __instance.carIndex = 0;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.ToggleTooltip))]
    static void HoverTooltipEnd(TrackChoice __instance)
    {
        if (swappedFrom < 0) return;
        __instance.carIndex = swappedFrom;
        swappedFrom = -1;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Profile), nameof(Profile.CanUnlockCarIndex))]
    static void Unlocked(int index, ref bool __result) => __result |= Modded(index) != null;

    // Hooks that swap an index for the duration of one call keep it here, not in Harmony's
    // __state (save hooks using __state crashed the game at start-up).
    static int swappedFrom = -1;

    // Modded cars are always unlocked: check the rest (track, difficulty) with the starter car.
    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.CheckIfCanStart))]
    static void CanStartPre(TrackChoice __instance)
    {
        if (Modded(__instance.carIndex) == null) return;
        swappedFrom = __instance.carIndex;
        __instance.carIndex = 0;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.CheckIfCanStart))]
    static void CanStartPost(TrackChoice __instance)
    {
        if (swappedFrom < 0) return;
        __instance.carIndex = swappedFrom;
        swappedFrom = -1;
    }

    // ---- The run ----------------------------------------------------------

    // The run itself only ever holds a vanilla car: RunData.carIndex is the base car's, so
    // Run.json is always safe without the mod. The modded car is kept in a side file as
    // "<run seed> <car id>" and swapped in only while CarManager.SetCar picks the car.
    static string runFile; // side file content, cached

    static void WriteRunFile(string content)
    {
        runFile = content;
        if (content == null) { if (File.Exists(RunFile)) File.Delete(RunFile); }
        else File.WriteAllText(RunFile, content);
    }

    // The modded car of the current run, or null.
    static CarFile RunCar()
    {
        var rd = RunData.instance;
        if (rd == null) return null;
        runFile ??= File.Exists(RunFile) ? File.ReadAllText(RunFile) : "";
        var parts = runFile.Split(' ', 2);
        if (parts.Length < 2 || parts[0] != ((int)rd.seed).ToString()) return null; // another run
        var c = Cars.FirstOrDefault(x => x.car != null && x.id.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
        return c != null && rd.carIndex == c.baseIndex ? c : null;
    }

    static CarFile starting;

    [HarmonyPrefix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.StartRun))]
    static void StartPre(TrackChoice __instance)
    {
        starting = Modded(__instance.carIndex);
        if (starting != null) __instance.carIndex = starting.baseIndex;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackChoice), nameof(TrackChoice.StartRun))]
    static void StartPost(TrackChoice __instance)
    {
        if (starting == null) { WriteRunFile(null); return; }
        __instance.carIndex = starting.car.carIndex;
        WriteRunFile($"{(int)RunData.instance.seed} {starting.id}");
        Plugin.Logger.LogInfo($"Run started with {starting.name} (seed {(int)RunData.instance.seed}, saved as {starting.@base})");
        starting = null;
    }

    static int setCarFrom = -1;

    [HarmonyPrefix, HarmonyPatch(typeof(CarManager), nameof(CarManager.SetCar))]
    static void SetCarPre()
    {
        if (RunCar() is not { } c) return;
        setCarFrom = c.baseIndex;
        RunData.instance.carIndex = c.car.carIndex;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CarManager), nameof(CarManager.SetCar))]
    static void SetCarPost(CarManager __instance)
    {
        if (setCarFrom < 0) return;
        RunData.instance.carIndex = setCarFrom;
        setCarFrom = -1;
        Plugin.Logger.LogInfo($"Driving {Modded(__instance.car)?.name ?? __instance.car.name}");
    }

    // ---- Profile ----------------------------------------------------------

    // The profile sees the base car; a modded run doesn't earn the base car's progress.
    [HarmonyPrefix, HarmonyPatch(typeof(Profile), nameof(Profile.MarkCarWon))]
    static bool MarkWon() => RunCar() == null;

    [HarmonyPrefix, HarmonyPatch(typeof(Profile), nameof(Profile.MarkCarDifficultyWon))]
    static bool MarkDiff() => RunCar() == null;

    [HarmonyPrefix, HarmonyPatch(typeof(Profile), nameof(Profile.MarkCarAllItemsDifficultyWon))]
    static bool MarkAllItems() => RunCar() == null;

    [HarmonyPrefix, HarmonyPatch(typeof(Profile), nameof(Profile.MarkHardestDifficultyCarWon))]
    static bool MarkHardest() => RunCar() == null;

    // ---- Leaderboards -----------------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(LeaderboardManager), nameof(LeaderboardManager.CanSubmitToLeaderboards))]
    static void NoLeaderboards(ref bool __result)
    {
        if (RunCar() != null) __result = false;
    }
}

