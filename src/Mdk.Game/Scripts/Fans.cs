using System.Numerics;
using System.Text.Json.Nodes;
using Mdk.Formats;

namespace Mdk.Game.Scripts;

/// <summary>Fans (updrafts) of the arenas (a port of godot-mdk's <c>fans.gd</c>; arena+0x45e):
/// created on the arenas' type-7 hotspots by fan_create (0x413a94), they lift Kurt, objects and
/// pieces inside their box (updraft_query 0x413c24 → 0x413d14). Each tick, with a chance of 1 in 8,
/// a still fire spark appears just above the bottom of the box for the updraft to lift (0x414230).
/// See godot-mdk docs/engine.md ("Fans and conveyors").
/// <code>
///        z1 + 5 ┌───────┐  the box reaches 5 units above the top
///           z1  │ ↑ ↑ ↑ │  type 6: full strength up to 5 below z1, then fading; wobble near z1
///               │ ↑ ↑ ↑ │
///     z0 - 0.5  └───────┘  sparks at z0 + 0.25
/// </code></summary>
public sealed class Fans(IReadOnlyList<Dti.ArenaEntry> arenas, Random rng)
{
    /// <summary>Who asks: Kurt, an object, or a spark or piece; a fan acts on those whose bit is in its mask.</summary>
    public const int MaskKurt = 1;
    public const int MaskObjects = 2;
    public const int MaskEffects = 8;
    /// <summary>The DTI records fans stand on.</summary>
    private const uint FanHotspot = 7;
    /// <summary>The bottom of the box is lowered by half a unit.</summary>
    private const float BoxDrop = 0.5f;
    /// <summary>The fan whose strength operand is the time to rise through the box (every level's).</summary>
    private const int TypeTimed = 6;
    /// <summary>Bit 0 of the mask: enabled (fan_enable).</summary>
    private const int Enabled = 1;
    /// <summary>A spark with a chance of 1 in 8 a tick, just above the bottom of the box.</summary>
    private const int SparkChance = 8;
    private const float SparkLift = 0.25f;
    /// <summary>Type 6 fans lift at full strength up to 5 units below their top, then less and less
    /// (0.2 per unit); the box reaches 5 units above the top.</summary>
    private const float Top = 5f;
    private const float TopFade = 0.2f;
    /// <summary>In the last 2 units a wobble of ±2 u/s (0.1 per query) is added (0x490d7c).</summary>
    private const float WobbleZone = 2f;
    private const float WobbleLimit = 2f;
    private const float WobbleStep = 0.1f;
    /// <summary>The vertical speed rises by (target + 64) × dt towards the fan's speed.</summary>
    private const float LiftAcceleration = 64f;

    public enum Power { Off, On }

    private sealed class Fan
    {
        public string Name = "";
        public string Arena = "";
        public int Param;
        public int Type;
        /// <summary>Upward speed at full strength (u/s).</summary>
        public float Strength;
        public int Mask = -1;
        public Vector3 BoxStart;
        public Vector3 BoxEnd;
    }

    /// <summary>A fan's still spark: (arena, point).</summary>
    public Action<string, Vector3>? Spark;
    /// <summary>Whether an arena's objects run (only their fans let out sparks).</summary>
    public Func<string, bool> IsLive = _ => true;

    private readonly List<Fan> _fans = [];
    private float _wobble;
    private bool _wobbleUp;

    public int Count => _fans.Count;

    /// <summary>fan_create (0x413a94): a fan on the arena's hotspot <paramref name="hotspot"/>. With
    /// type 6, <paramref name="strength"/> is the time to rise through the box.</summary>
    public void Create(string arena, int hotspot, string name, int param, int type, float strength)
    {
        var record = arenas.FirstOrDefault(a => a.Name == arena)?.Records.FirstOrDefault(r => r.Type == FanHotspot && r.Id == hotspot);
        if (record == null)
        {
            Console.Error.WriteLine($"Cannot find fan hotspot id {hotspot} for {name}");
            return;
        }

        var fan = new Fan
        {
            Name = name,
            Arena = arena,
            Param = param,
            Type = type,
            Strength = strength,
            BoxStart = record.Position - new Vector3(0f, 0f, BoxDrop),
            BoxEnd = record.BoxEnd,
        };
        if (type == TypeTimed)
        {
            fan.Strength = (record.BoxEnd.Z - record.Position.Z) / (strength - BoxDrop);
        }

        _fans.Add(fan);
    }

    /// <summary>The fans for a full save.</summary>
    public JsonArray Snapshot() => new(_fans.Select(f => (JsonNode?)new JsonObject
    {
        ["name"] = f.Name,
        ["arena"] = f.Arena,
        ["param"] = f.Param,
        ["type"] = f.Type,
        ["strength"] = f.Strength,
        ["mask"] = f.Mask,
        ["box"] = new JsonArray(f.BoxStart.X, f.BoxStart.Y, f.BoxStart.Z, f.BoxEnd.X, f.BoxEnd.Y, f.BoxEnd.Z),
    }).ToArray());

    /// <summary>The fans of a full save, as they were.</summary>
    public void Restore(JsonArray data)
    {
        _fans.Clear();
        foreach (var entry in data.OfType<JsonObject>())
        {
            var box = entry["box"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray();
            _fans.Add(new Fan
            {
                Name = entry["name"]!.GetValue<string>(),
                Arena = entry["arena"]!.GetValue<string>(),
                Param = entry["param"]!.GetValue<int>(),
                Type = entry["type"]!.GetValue<int>(),
                Strength = entry["strength"]!.GetValue<float>(),
                Mask = entry["mask"]!.GetValue<int>(),
                BoxStart = new Vector3(box[0], box[1], box[2]),
                BoxEnd = new Vector3(box[3], box[4], box[5]),
            });
        }
    }

    /// <summary>fan_remove (0x413fa0).</summary>
    public void Remove(string arena, string name)
    {
        var index = _fans.FindIndex(f => f.Arena == arena && f.Name == name);
        if (index >= 0)
        {
            _fans.RemoveAt(index);
        }
    }

    /// <summary>fan_enable (0x4140e4): sets or clears bit 0 of the fan's mask.</summary>
    public void Enable(string arena, string name, Power power)
    {
        foreach (var fan in _fans.Where(f => f.Arena == arena && f.Name == name))
        {
            fan.Mask = power == Power.On ? fan.Mask | Enabled : fan.Mask & ~Enabled;
        }
    }

    /// <summary>Each tick: the fans of the live arenas let out their sparks (enabled or not).</summary>
    public void Update()
    {
        foreach (var fan in _fans)
        {
            if (!IsLive(fan.Arena) || rng.Next(SparkChance) != 0)
            {
                continue;
            }

            var x = fan.BoxStart.X + (float)rng.NextDouble() * (fan.BoxEnd.X - fan.BoxStart.X);
            var y = fan.BoxStart.Y + (float)rng.NextDouble() * (fan.BoxEnd.Y - fan.BoxStart.Y);
            Spark?.Invoke(fan.Arena, new Vector3(x, y, fan.BoxStart.Z + SparkLift));
        }
    }

    /// <summary>The vertical speed (u/s) for something at <paramref name="point"/> going up or down at
    /// <paramref name="vz"/>, or NaN when no fan of the arena holds it (updraft_query).</summary>
    public float Query(string arena, Vector3 point, float vz, int mask, float dt)
    {
        var found = false;
        foreach (var fan in _fans)
        {
            if (fan.Arena != arena || (fan.Mask & mask) == 0 || !Holds(fan, point))
            {
                continue;
            }

            found = true;
            vz = Lift(fan, point.Z, vz, dt);
        }

        return found ? vz : float.NaN;
    }

    private static bool Holds(Fan fan, Vector3 point) =>
        point.X >= fan.BoxStart.X && point.X <= fan.BoxEnd.X
        && point.Y >= fan.BoxStart.Y && point.Y <= fan.BoxEnd.Y
        && point.Z >= fan.BoxStart.Z && point.Z <= fan.BoxEnd.Z + Top;

    /// <summary>One fan's effect (0x413d14): a target speed by type and height, approached at
    /// (target + 64) u/s².</summary>
    private float Lift(Fan fan, float z, float vz, float dt)
    {
        var height = fan.Param == 0 ? (z - fan.BoxStart.Z) / (fan.BoxEnd.Z - fan.BoxStart.Z) : 1f;
        // Types 1-5 fade with the relative height (param ≠ 0: as at the top).
        var factor = fan.Type switch
        {
            1 => 1f - height * height,
            2 => (1f - height) * (1f - height),
            3 => 1f - height,
            4 => 1f - height * height * height,
            5 => MathF.Pow(1f - height, 3f),
            TypeTimed when fan.BoxEnd.Z - z < Top => 1f - (z - (fan.BoxEnd.Z - Top)) * TopFade,
            _ => 1f,
        };
        var target = fan.Strength * factor;
        if (fan.Type == TypeTimed)
        {
            if (fan.BoxEnd.Z - z < WobbleZone)
            {
                target += Wobble();
            }

            // A faster upward speed is halved towards the target.
            if (target < vz)
            {
                vz = (vz + target) * 0.5f;
            }
        }
        else if (factor < 0f)
        {
            target = -LiftAcceleration;
        }

        return vz < target ? MathF.Min(vz + (target + LiftAcceleration) * dt, target) : vz;
    }

    /// <summary>Swings the shared wobble between ±2 by 0.1 per call.</summary>
    private float Wobble()
    {
        _wobble += _wobbleUp ? WobbleStep : -WobbleStep;
        if (!_wobbleUp && _wobble < -WobbleLimit)
        {
            _wobbleUp = true;
        }
        else if (_wobbleUp && _wobble > WobbleLimit)
        {
            _wobbleUp = false;
        }

        return _wobble;
    }
}
