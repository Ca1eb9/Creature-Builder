using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class CreatureSaveData
{
    public string creatureName;
    public string savedAt;
    public List<PartSaveEntry> parts = new List<PartSaveEntry>();
}

[System.Serializable]
public class PartSaveEntry
{
    public string partID;
    public int category;
    public Vector3 positionDelta;
    public Vector3 rotationDelta;
    public float scaleMultiplier;
}

public class CreatureSaveLoad : MonoBehaviour
{
    [Header("References")]
    public CreatureAssembler assembler;
    public BodyPartDatabase database;

    private const string SAVE_FOLDER = "Creatures";

    private string SaveDir => Path.Combine(Application.persistentDataPath, SAVE_FOLDER);

    private string GetSavePath(string creatureName)
    {
        string safeName = SanitizeFilename(creatureName);
        return Path.Combine(SaveDir, safeName + ".json");
    }

    /// <summary>True if a save file already exists for this name (used for overwrite warnings).</summary>
    public bool CreatureExists(string creatureName)
    {
        if (string.IsNullOrWhiteSpace(creatureName)) return false;
        return File.Exists(GetSavePath(creatureName));
    }

    public bool SaveCreature(string creatureName)
    {
        if (assembler == null)
        {
            Debug.LogError("CreatureSaveLoad: no CreatureAssembler assigned — cannot save.", this);
            return false;
        }
        if (string.IsNullOrWhiteSpace(creatureName))
        {
            Debug.LogWarning("Save aborted: empty name");
            return false;
        }

        var data = new CreatureSaveData
        {
            creatureName = creatureName,
            savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        foreach (BodyPartCategory cat in System.Enum.GetValues(typeof(BodyPartCategory)))
        {
            BodyPartData partData = assembler.GetEquippedData(cat);
            if (partData == null) continue;

            var ov = assembler.GetOverride(cat);
            data.parts.Add(new PartSaveEntry
            {
                partID = partData.PartID,
                category = (int)cat,
                positionDelta = ov.positionDelta,
                rotationDelta = ov.rotationDelta,
                scaleMultiplier = ov.scaleMultiplier
            });
        }

        try
        {
            Directory.CreateDirectory(SaveDir);
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(GetSavePath(creatureName), json);
            Debug.Log($"Saved creature '{creatureName}' to {GetSavePath(creatureName)}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Save failed: {e}");
            return false;
        }
    }

    public bool LoadCreature(string creatureName)
    {
        if (assembler == null || database == null)
        {
            Debug.LogError("CreatureSaveLoad: assembler or database not assigned — cannot load.", this);
            return false;
        }
        string path = GetSavePath(creatureName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"No saved creature at {path}");
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            CreatureSaveData data = JsonUtility.FromJson<CreatureSaveData>(json);

            assembler.ClearAll();

            // Equip torso first, then everything else, so torso-link works
            var ordered = data.parts.OrderBy(p =>
                (BodyPartCategory)p.category == BodyPartCategory.Torso ? 0 : 1).ToList();

            foreach (var entry in ordered)
            {
                BodyPartData partData = database.GetPartByID(entry.partID);
                if (partData == null)
                {
                    Debug.LogWarning($"Part with ID {entry.partID} not found in database. Skipping.");
                    continue;
                }
                assembler.EquipPart(partData, notify: false);
                assembler.SetOverride((BodyPartCategory)entry.category, new PartOverride
                {
                    positionDelta = entry.positionDelta,
                    rotationDelta = entry.rotationDelta,
                    scaleMultiplier = entry.scaleMultiplier
                }, notify: false);
            }

            // Single notify at the end — keeps load smooth instead of re-framing per part
            assembler.NotifyCreatureChanged();

            Debug.Log($"Loaded creature '{creatureName}'");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Load failed: {e}");
            return false;
        }
    }

    public List<string> ListSavedCreatures()
    {
        if (!Directory.Exists(SaveDir)) return new List<string>();
        return Directory.GetFiles(SaveDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n)
            .ToList();
    }

    public bool DeleteCreature(string creatureName)
    {
        string path = GetSavePath(creatureName);
        if (!File.Exists(path)) return false;
        try
        {
            File.Delete(path);
            // The thumbnail lives beside the save; without this it is orphaned
            // on disk forever, and a stale one would resurface if the same name
            // is used again before a new picture is captured.
            string thumb = GetThumbnailPath(creatureName);
            if (File.Exists(thumb)) File.Delete(thumb);
            return true;
        }
        catch (System.Exception e) { Debug.LogError($"Delete failed: {e}"); return false; }
    }

    // ---- Library card support: thumbnails + parts summary ----

    private string GetThumbnailPath(string creatureName)
        => Path.Combine(SaveDir, SanitizeFilename(creatureName) + ".png");

    /// <summary>Writes a PNG thumbnail next to the save file.</summary>
    public void SaveThumbnail(string creatureName, Texture2D tex)
    {
        if (tex == null) return;
        try
        {
            Directory.CreateDirectory(SaveDir);
            File.WriteAllBytes(GetThumbnailPath(creatureName), tex.EncodeToPNG());
        }
        catch (System.Exception e) { Debug.LogWarning($"Thumbnail save failed: {e.Message}"); }
    }

    /// <summary>Loads the saved thumbnail, or null if there isn't one.</summary>
    public Texture2D LoadThumbnail(string creatureName)
    {
        string p = GetThumbnailPath(creatureName);
        if (!File.Exists(p)) return null;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(File.ReadAllBytes(p));
            return tex;
        }
        catch { return null; }
    }

    /// <summary>"Saved 31 May" style stamp for a library card, from the save's own timestamp.</summary>
    public string GetSavedDate(string creatureName)
    {
        string path = GetSavePath(creatureName);
        if (!File.Exists(path)) return "";
        try
        {
            var data = JsonUtility.FromJson<CreatureSaveData>(File.ReadAllText(path));
            if (data != null && !string.IsNullOrEmpty(data.savedAt) &&
                System.DateTime.TryParse(data.savedAt, out var when))
                return "Saved " + when.ToString("d MMM");
        }
        catch { /* fall through to the file stamp */ }
        return "Saved " + File.GetLastWriteTime(path).ToString("d MMM");
    }

    /// <summary>"Cthulhu · Cow · Spider · …" — the equipped part names of a save, in category order.</summary>
    public string GetPartsSummary(string creatureName)
    {
        string path = GetSavePath(creatureName);
        if (!File.Exists(path)) return "";
        try
        {
            var data = JsonUtility.FromJson<CreatureSaveData>(File.ReadAllText(path));
            var names = data.parts
                .OrderBy(p => p.category)
                .Select(e =>
                {
                    var pd = database != null ? database.GetPartByID(e.partID) : null;
                    return pd != null ? pd.partName : "?";
                });
            return string.Join(" · ", names);
        }
        catch { return ""; }
    }

    // Windows reserved device names — illegal as filenames even with an extension.
    private static readonly HashSet<string> ReservedNames = new HashSet<string>(
        System.StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private string SanitizeFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Untitled";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        // Windows trims trailing dots/spaces from filenames, which can produce
        // surprises like "MyCreature." silently becoming "MyCreature".
        name = name.Trim().TrimEnd('.', ' ');

        // After all the stripping, we might be left with nothing or with a
        // dot-only string like ".." that's still a path-traversal hazard.
        if (string.IsNullOrEmpty(name)) return "Untitled";
        if (name.TrimStart('.').Length == 0) return "Untitled";

        // Reserved Windows device names can't be used even with extensions.
        if (ReservedNames.Contains(name)) return "_" + name;

        return name;
    }
}