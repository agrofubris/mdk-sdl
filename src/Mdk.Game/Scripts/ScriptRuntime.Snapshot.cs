using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Full saves (F2; script_runtime.gd snapshot/restore, godot-mdk docs/gameplay.md "Saving
/// and loading"): the runtime's variables, the arenas' states and script objects, the objects, the
/// fans, the bomb and the decoy, the triangle groups and the level's counts. Kurt is saved apart.
/// <code>
///   Snapshot ──► { fields, arenas: {name: {..., controller, hit_scripts}}, objects: [{type, arena, box?, variables}],
///                  fans, items, groups, stats }
///   Restore  ──► arena states ──► objects created (no scripts run) ──► references resolved ──► fields
/// </code></summary>
public sealed partial class ScriptRuntime
{
    private const string ControllerKey = "arena:";
    private const string HitScriptsKey = "hits:";

    /// <summary>Whether a full save may be made now (0x42b520(0)): no cutscene, the level not over,
    /// no strike out; the port also needs Kurt alive and not riding.</summary>
    public bool CanSnapshot() =>
        Cutscene == 0 && !LevelOver && EndLevel == null && !AirStrike.IsActive() && Rides.Ridden == null && Kurt.Health > 0
        && CurrentArena.Length != 0;

    /// <summary>Kurt and the level for a full save, as JSON (<c>{"kurt": ..., "level": ...}</c>).</summary>
    public string Capture() => new JsonObject { ["kurt"] = Kurt.Snapshot(), ["level"] = Snapshot() }.ToJsonString();

    /// <summary>Kurt and the level of a full save (<see cref="Capture"/>), before the first tick.</summary>
    public void Load(string json)
    {
        var data = JsonNode.Parse(json)!.AsObject();
        Restore(data["level"]!.AsObject());
        Kurt.Restore(data["kurt"]!.AsObject());

        // The first tick's move starts where Kurt is: it crosses no connection.
        _previousKurtPosition = Kurt.Feet;
    }

    /// <summary>The level's state for a full save.</summary>
    public JsonObject Snapshot()
    {
        var objects = Objects.Where(o => !o.Dead).ToList();
        var packer = new Snapshot(objects, FixedObjects(), FindAnimationNamed);
        var data = new JsonObject
        {
            ["global_variables"] = packer.Encode(GlobalVariables),
            ["global_flags"] = GlobalFlags,
            ["alarm_ticks"] = AlarmTicks,
            ["sky_mode"] = SkyMode,
            ["option"] = Option,
            ["town_ticks"] = TownTicks,
            ["current_arena"] = CurrentArena,
            ["second_arena"] = SecondArena,
            ["second_active"] = SecondActive,
            ["next_instance"] = _nextInstance,
            ["camera_track_pitch"] = packer.Encode(CameraTrackPitch),
            ["camera_track_ticks"] = CameraTrackTicks,
            ["shatter_point"] = packer.Encode(ShatterPoint),
            ["shatter_direction"] = packer.Encode(ShatterDirection),
            ["alien_target"] = packer.Encode(AlienTarget),
            ["strike_used"] = AirStrike.UsedUp,
            ["stats"] = packer.Pack(Stats),
        };

        var arenas = new JsonObject();
        foreach (var (name, state) in _arenas)
        {
            var entry = packer.Pack(state);
            entry["controller"] = packer.Pack(state.Controller);
            entry["hit_scripts"] = packer.Pack(state.HitScripts);
            arenas[name] = entry;
        }

        data["arenas"] = arenas;
        data["objects"] = new JsonArray(objects.Select(o => (JsonNode?)PackObject(packer, o)).ToArray());
        data["fans"] = Fans.Snapshot();
        data["items"] = new JsonObject { ["bomb"] = packer.Encode(Items.Bomb), ["decoy"] = packer.Encode(Items.Decoy) };
        data["groups"] = _groups.Snapshot();
        return data;
    }

    /// <summary>Puts the level back as <see cref="Snapshot"/> left it, before the first tick (objects
    /// are created again, their scripts carry on where they were).</summary>
    public void Restore(JsonObject data)
    {
        // The arenas' script objects exist first, so references to them resolve.
        var arenas = data["arenas"]!.AsObject();
        foreach (var (name, _) in arenas)
        {
            GetArenaState(name);
        }

        // The objects next, so that references between them resolve.
        var created = data["objects"]!.AsArray().Select(e => Recreate(e!.AsObject())).ToList();
        Objects.Clear();
        Objects.AddRange(created.OfType<MdkObject>());
        var packer = new Snapshot(created, FixedObjects(), FindAnimationNamed);

        RestoreFields(data, packer);
        packer.Unpack(Stats, data["stats"]!.AsObject());
        foreach (var (name, node) in arenas)
        {
            var entry = node!.AsObject();
            var state = GetArenaState(name);
            packer.Unpack(state, entry);
            packer.Unpack(state.Controller, entry["controller"]!.AsObject());
            packer.Unpack(state.HitScripts, entry["hit_scripts"]!.AsObject());
        }

        var objects = data["objects"]!.AsArray();
        for (var i = 0; i < created.Count; i++)
        {
            if (created[i] is { } obj)
            {
                packer.Unpack(obj, objects[i]!["variables"]!.AsObject());
            }
        }

        Fans.Restore(data["fans"]!.AsArray());
        var items = data["items"]!.AsObject();
        Items.Bomb = (MdkObject?)packer.Decode(items["bomb"], typeof(MdkObject), null);
        Items.Decoy = (MdkObject?)packer.Decode(items["decoy"], typeof(MdkObject), null);
        _groups.Restore(data["groups"]!.AsObject());

        // An arena reached by a teleport is shown again.
        ArenaEntered?.Invoke(CurrentArena);
        if (SecondArena.Length != 0)
        {
            ArenaEntered?.Invoke(SecondArena);
        }
    }

    private void RestoreFields(JsonObject data, Snapshot packer)
    {
        Array.Copy((float[])packer.Decode(data["global_variables"], typeof(float[]), null)!, GlobalVariables, GlobalVariables.Length);
        GlobalFlags = data["global_flags"]!.GetValue<int>();
        AlarmTicks = data["alarm_ticks"]!.GetValue<int>();
        SkyMode = data["sky_mode"]!.GetValue<int>();
        Option = data["option"]!.GetValue<int>();
        TownTicks = data["town_ticks"]!.GetValue<int>();
        CurrentArena = data["current_arena"]!.GetValue<string>();
        SecondArena = data["second_arena"]!.GetValue<string>();
        SecondActive = data["second_active"]!.GetValue<bool>();
        _nextInstance = data["next_instance"]!.GetValue<int>();
        CameraTrackPitch = (float)packer.Decode(data["camera_track_pitch"], typeof(float), null)!;
        CameraTrackTicks = data["camera_track_ticks"]!.GetValue<int>();
        ShatterPoint = (Vector3)packer.Decode(data["shatter_point"], typeof(Vector3), null)!;
        ShatterDirection = (Vector3)packer.Decode(data["shatter_direction"], typeof(Vector3), null)!;
        AlienTarget = (MdkObject?)packer.Decode(data["alien_target"], typeof(MdkObject), null);
        AirStrike.UsedUp = data["strike_used"]!.GetValue<bool>();
    }

    private JsonObject PackObject(Snapshot packer, MdkObject obj)
    {
        var entry = new JsonObject { ["type"] = obj.TypeName, ["arena"] = obj.Arena };
        if (_boxes.Contains(obj) && obj.Model != null)
        {
            entry["box"] = packer.Encode(obj.Model.Bounds.Max - obj.Model.Bounds.Min);
        }

        entry["variables"] = packer.Pack(obj);
        return entry;
    }

    /// <summary>An object of a full save, without its variables yet (null when its model is gone).</summary>
    private MdkObject? Recreate(JsonObject entry)
    {
        var arena = entry["arena"]!.GetValue<string>();
        var type = entry["type"]!.GetValue<string>();
        var controller = GetArenaState(arena).Controller;
        if (entry["box"] is JsonArray size)
        {
            var box = SpawnBox(controller, Vector3.Zero, new Vector3(size[0]!.GetValue<float>(), size[1]!.GetValue<float>(), size[2]!.GetValue<float>()), type, 0);
            Objects.Remove(box);
            return box;
        }

        var model = FindModel(arena, type);
        return model == null ? null : new MdkObject { Arena = arena, TypeName = type, Model = model };
    }

    /// <summary>The arenas' script objects, by key, for references in full saves.</summary>
    private Dictionary<string, MdkObject> FixedObjects()
    {
        var fixedObjects = new Dictionary<string, MdkObject>();
        foreach (var (name, state) in _arenas)
        {
            fixedObjects[ControllerKey + name] = state.Controller;
            fixedObjects[HitScriptsKey + name] = state.HitScripts;
        }

        return fixedObjects;
    }

    /// <summary>An animation by its name: one stored in the CMI (CMI_offset), one of the arena's
    /// models, or one of the items.</summary>
    private ModelAnimation? FindAnimationNamed(MdkObject? obj, string name)
    {
        if (name.StartsWith(CmiAnimationPrefix, StringComparison.Ordinal) && obj != null)
        {
            return GetAnimation(obj, int.Parse(name[CmiAnimationPrefix.Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return (obj != null ? FindArenaAnimation(obj.Arena, name) : null) ?? Items.GetAnimation(name);
    }
}
