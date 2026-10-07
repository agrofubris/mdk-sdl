using System.Text;
using System.Text.Json;

namespace Mdk.Game.Flow;

/// <summary>A saved game's kind (the original's <c>GAME</c> type): at the level's start, before the
/// level (its briefing first), or a full snapshot (F2).</summary>
public enum SaveKind { LevelStart = 3, BeforeLevel = 6, Snapshot = 1003 }

/// <summary>A saved game: its kind, level (LEVELn), health, deaths, the air strike and when; a full
/// snapshot also holds the level's state (JSON: <c>{"kurt": ..., "level": ...}</c>).</summary>
public sealed record SaveGame(SaveKind Kind, int Level, int Health, int Deaths, bool StrikeUsed, string Time, string? State = null)
{
    public const int FullHealth = 100;
    private const int FirstLevel = 3;
    private const int LastLevel = 8;

    /// <summary>As the Godot port writes it: <c>{"type": 3, "level": 7, "health": 100, "deaths": 0,
    /// "strike_used": false, "time": "..."}</c>.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("type", (int)Kind);
            json.WriteNumber("level", Level);
            json.WriteNumber("health", Health);
            json.WriteNumber("deaths", Deaths);
            json.WriteBoolean("strike_used", StrikeUsed);
            json.WriteString("time", Time);
            if (State != null)
            {
                json.WritePropertyName("snapshot");
                json.WriteRawValue(State);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A save from its JSON, or null when it's invalid (no level 3-8).</summary>
    public static SaveGame? Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("level", out var levelValue))
            {
                return null;
            }

            var level = (int)levelValue.GetDouble();
            if (level is < FirstLevel or > LastLevel)
            {
                return null;
            }

            var kind = (SaveKind)Number(root, "type", (int)SaveKind.LevelStart);
            var strike = root.TryGetProperty("strike_used", out var s) && s.ValueKind == JsonValueKind.True;
            var time = root.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            var state = root.TryGetProperty("snapshot", out var snapshot) && snapshot.ValueKind == JsonValueKind.Object ? snapshot.GetRawText() : null;
            return new SaveGame(kind, level, Number(root, "health", FullHealth), Number(root, "deaths", 0), strike, time, state);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Number(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? (int)value.GetDouble() : fallback;
}

/// <summary>The saved games: <c>&lt;NAME&gt;.sav</c> files of JSON in a directory (game_state.gd).
/// Dying writes <see cref="LastGame"/>, which only lasts for the session.</summary>
public sealed class SaveGames(string directory)
{
    public const string LastGame = "LASTGAME";
    private const string Extension = ".sav";

    public string Directory => directory;

    /// <summary>Writes a save; false when it can't.</summary>
    public bool Write(string name, SaveGame save)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(PathOf(name), save.ToJson());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A save, or null when it's missing or invalid.</summary>
    public SaveGame? Read(string name)
    {
        var path = PathOf(name);
        return File.Exists(path) ? SaveGame.Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>Deletes a save, if there is one.</summary>
    public void Delete(string name)
    {
        if (File.Exists(PathOf(name)))
        {
            File.Delete(PathOf(name));
        }
    }

    /// <summary>The saves' names, sorted.</summary>
    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        return System.IO.Directory.GetFiles(directory, "*" + Extension)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The save quick load takes: <paramref name="session"/> (this session's last full
    /// save) while it exists, else the newest file; null when there is none.</summary>
    public string? QuickSlot(string? session)
    {
        if (session != null && File.Exists(PathOf(session)))
        {
            return session;
        }

        return List().OrderByDescending(name => File.GetLastWriteTimeUtc(PathOf(name))).FirstOrDefault();
    }

    private string PathOf(string name) => Path.Combine(directory, name + Extension);

    /// <summary>The saves of a user folder (<c>saves/</c> in it).</summary>
    public static SaveGames In(string userFolder) => new(Path.Combine(userFolder, SavesFolder));

    private const string SavesFolder = "saves";
}
