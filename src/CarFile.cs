using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace LooplerCarLoader;

// One car, read from one JSON file in BepInEx\config\LooplerCars\.
public class CarFile
{
    public string id, name, effect, sprite;
    public string @base = "Durabler";
    public bool keepBaseAbility;
    public int? durability;
    public Dictionary<string, float> stats = new();
    public int bonusUpgradeSlots;
    public bool extraGateCheckpoint;

    internal string path;
    internal readonly Dictionary<StatType, float> statMults = new();
    internal Car car;
    internal int baseIndex;

    static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Every valid car file in the folder, sorted by id. Bad files are logged and skipped.
    internal static List<CarFile> LoadAll(string dir)
    {
        Directory.CreateDirectory(dir);
        var cars = new List<CarFile>();
        foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var c = JsonSerializer.Deserialize<CarFile>(File.ReadAllText(path), Json);
                c.path = path;
                var error = c.Validate();
                if (error != null) Plugin.Logger.LogWarning($"{Rel(dir, path)}: {error}, skipped");
                else if (cars.Any(o => o.id.Equals(c.id, StringComparison.OrdinalIgnoreCase)))
                    Plugin.Logger.LogWarning($"{Rel(dir, path)}: id \"{c.id}\" is already used by another car, skipped");
                else cars.Add(c);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"{Rel(dir, path)}: {e.Message}, skipped"); }
        }
        return cars.OrderBy(c => c.id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Rel(string dir, string path) => Path.GetRelativePath(dir, path);

    string Validate()
    {
        if (string.IsNullOrWhiteSpace(id)) return "\"id\" is missing";
        if (string.IsNullOrWhiteSpace(name)) return "\"name\" is missing";
        if (durability is <= 0) return "\"durability\" must be above 0";
        foreach (var (key, value) in stats)
        {
            if (Enum.TryParse<StatType>(key, true, out var type)) statMults[type] = value;
            else Plugin.Logger.LogWarning($"{id}: unknown stat \"{key}\" ignored. Known: {string.Join(", ", Enum.GetNames<StatType>())}");
        }
        return null;
    }

    // A vanilla car's asset name is "<n>_<Name>", e.g. "3_Slotler".
    internal static bool IsNamed(Car car, string name) =>
        car.name.Substring(car.name.IndexOf('_') + 1).Equals(name, StringComparison.OrdinalIgnoreCase);

    // The PNG next to the JSON file, as a sprite with the base car's pixels per unit; null if it can't be read.
    internal Sprite LoadSprite(Sprite baseSprite)
    {
        if (string.IsNullOrWhiteSpace(sprite)) return null;
        var file = Path.Combine(Path.GetDirectoryName(path), sprite);
        if (!File.Exists(file)) { Plugin.Logger.LogWarning($"{id}: sprite \"{sprite}\" not found, using the base car's"); return null; }
        byte[] rgba;
        int w, h;
        try { rgba = Png.Decode(File.ReadAllBytes(file), out w, out h); }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"{id}: can't read \"{sprite}\" ({e.Message}), using the base car's");
            return null;
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = ((h - 1 - y) * w + x) * 4; // textures start at the bottom row
            tex.SetPixel(x, y, new Color32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]));
        }
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), baseSprite.pixelsPerUnit);
        s.name = "LCL_" + id;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
