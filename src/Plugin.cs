using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace LooplerCarLoader;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "renokk.loopler.carloader";
    public const string Name = "Loopler Car Loader";
    public const string Version = "0.1.0";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        var dir = Path.Combine(Paths.ConfigPath, "LooplerCars");
        Patches.Cars = CarFile.LoadAll(dir);
        Patches.RunFile = Path.Combine(Paths.ConfigPath, "LooplerCarLoader.run.txt");

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Patches));
        AddComponent<CarouselFix>();
        Log.LogInfo($"{Name} {Version} loaded {Patches.Cars.Count} car file(s) from {dir}, {harmony.GetPatchedMethods().Count()} hooks");
    }
}
