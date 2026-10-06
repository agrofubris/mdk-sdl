using System.Globalization;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;

namespace Mdk.Game.DevTools;

/// <summary>The console's commands on a level: their arguments parsed, checked and handed to the
/// <see cref="ICommandTarget"/>; bad ones print the usage.</summary>
public static class ConsoleCommands
{
    private const string All = "all";
    private const int Coordinates = 3;
    private const float MaxTimeScale = 10f;

    /// <summary>"give all": every item (as many as the slots hold), every sniper round, health.</summary>
    private static readonly string[] AllPickups =
        ["SW_HBOMB", "SW_GATT", "SW_TWIST", "SW_THUMP", "SW_INTER", "SW_HOME", "SW_SGREN", "SW_HGREN", "SW_LGREN", "SW_BONES", "SW_H150"];

    private static readonly Dictionary<string, AntiAliasing> AntiAliasings = new()
    {
        ["off"] = AntiAliasing.Off,
        ["2x"] = AntiAliasing.X2,
        ["4x"] = AntiAliasing.X4,
    };

    public static void Register(CommandRegistry registry, ICommandTarget target)
    {
        registry.Add(new Command("map", "map <level> [arena]", args => Map(target, args)));
        registry.Add(new Command("teleport", "teleport <x y z | arena [x y z]>", args => Teleport(target, args), ["tp"]));
        registry.Add(new Command("pos", "pos", _ => Pos(target.Where())));
        registry.Add(new Command("noclip", "noclip", _ => $"noclip {Name(target.ToggleNoclip())}"));
        registry.Add(new Command("god", "god", _ => $"god {Name(target.ToggleGod())}"));
        registry.Add(new Command("give", "give <all | SW_...>", args => Give(target, args)));
        registry.Add(new Command("health", "health <n>", args => Health(target, args)));
        registry.Add(new Command("kill", "kill", _ => $"Killed {target.KillEnemies()} enemies"));
        registry.Add(new Command("save", "save <slot>", args => Save(target, args)));
        registry.Add(new Command("load", "load <slot>", args => Load(target, args)));
        registry.Add(new Command("difficulty", "difficulty <easy|normal|hard>", args => SetDifficulty(target, args)));
        registry.Add(new Command("look", "look <original|enhanced>", args => Look(target, args)));
        registry.Add(new Command("aa", "aa <off|2x|4x>", args => AntiAlias(target, args)));
        registry.Add(new Command("timescale", "timescale <x>", args => TimeScale(target, args)));
        registry.Add(new Command("fps", "fps", _ => $"overlay {Name(target.ToggleOverlay())}"));
        registry.Add(new Command("quit", "quit", _ => Quit(target)));
    }

    /// <summary>Where Kurt is, and the command line options that start there.
    /// <code>
    ///   level 3 arena ARENA_1 at -4.00 0.50 195.00 yaw 90
    ///   --level=3 --at=-4.00,0.50,195.00,90
    ///   --level=3 --teleport=ARENA_1,-4.00,0.50,195.00
    ///   --level=3 --console="tp ARENA_1 -4.00 0.50 195.00"
    /// </code></summary>
    private static string Pos(Placement at)
    {
        var (x, y, z) = (Format(at.Feet.X), Format(at.Feet.Y), Format(at.Feet.Z));
        var yaw = at.Yaw.ToString("0", CultureInfo.InvariantCulture);
        var level = $"--level={at.Level}";
        return $"level {at.Level} arena {at.Arena} at {x} {y} {z} yaw {yaw}\n" +
            $"{level} --at={x},{y},{z},{yaw}\n" +
            $"{level} --teleport={at.Arena},{x},{y},{z}\n" +
            $"{level} --console=\"tp {at.Arena} {x} {y} {z}\"";
    }

    private static string Map(ICommandTarget target, string[] args)
    {
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], CultureInfo.InvariantCulture, out var level))
        {
            return Usage("map <level> [arena]");
        }

        var arena = args.Length > 1 ? args[1].ToUpperInvariant() : "";
        return target.Map(level, arena) ? $"Loading level {level} {arena}".TrimEnd() : $"No level {level}";
    }

    /// <summary>"tp ARENA", "tp x y z", "tp ARENA x y z"; commas work as spaces (--teleport's form).</summary>
    private static string Teleport(ICommandTarget target, string[] args)
    {
        var words = string.Join(' ', args).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var numbers = words.Select(w => float.TryParse(w, CultureInfo.InvariantCulture, out var n) ? n : (float?)null).ToList();
        var arena = words.Length != 0 && numbers[0] == null ? words[0].ToUpperInvariant() : "";
        var coordinates = numbers.Skip(arena.Length != 0 ? 1 : 0).ToList();
        var point = coordinates.Count == Coordinates && coordinates.All(n => n != null)
            ? new Vector3(coordinates[0]!.Value, coordinates[1]!.Value, coordinates[2]!.Value)
            : (Vector3?)null;
        var valid = point != null || (arena.Length != 0 && coordinates.Count == 0);
        if (!valid)
        {
            return Usage("tp <x y z | arena [x y z]>");
        }

        return target.Teleport(arena, point) ? Pos(target.Where()).Split('\n')[0] : $"No arena {arena}";
    }

    private static string Give(ICommandTarget target, string[] args)
    {
        if (args.Length != 1)
        {
            return Usage("give <all | SW_...>");
        }

        if (args[0].Equals(All, StringComparison.OrdinalIgnoreCase))
        {
            var taken = AllPickups.Count(target.Give);
            return $"Took {taken} pickups";
        }

        var pickup = args[0].ToUpperInvariant();
        if (!Inventory.IsPickup(pickup))
        {
            return $"Not a pickup: {pickup}";
        }

        return target.Give(pickup) ? $"Took {pickup}" : $"Can't take {pickup}";
    }

    private static string Health(ICommandTarget target, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], CultureInfo.InvariantCulture, out var health) || health < 0)
        {
            return Usage("health <n>");
        }

        target.SetHealth(health);
        return $"health {health}";
    }

    private static string Save(ICommandTarget target, string[] args)
    {
        if (args.Length != 1)
        {
            return Usage("save <slot>");
        }

        return target.Save(args[0]) ? $"Saved {args[0]}" : "Can't save now";
    }

    private static string Load(ICommandTarget target, string[] args)
    {
        if (args.Length != 1)
        {
            return Usage("load <slot>");
        }

        return target.Load(args[0]) ? $"Loading {args[0]}" : $"No saved game {args[0]}";
    }

    private static string SetDifficulty(ICommandTarget target, string[] args)
    {
        if (args.Length != 1 || !Enum.TryParse<Difficulty>(args[0], ignoreCase: true, out var difficulty) || !Enum.IsDefined(difficulty))
        {
            return Usage("difficulty <easy|normal|hard>");
        }

        target.SetDifficulty(difficulty);
        return $"difficulty {Lower(difficulty)}";
    }

    private static string Look(ICommandTarget target, string[] args)
    {
        if (args.Length != 1 || !Enum.TryParse<Graphics>(args[0], ignoreCase: true, out var look) || !Enum.IsDefined(look))
        {
            return Usage("look <original|enhanced>");
        }

        return target.SetLook(look) ? $"look {Lower(look)}" : $"look {Lower(look)} from the next level";
    }

    private static string AntiAlias(ICommandTarget target, string[] args)
    {
        if (args.Length != 1 || !AntiAliasings.TryGetValue(args[0].ToLowerInvariant(), out var antiAliasing))
        {
            return Usage("aa <off|2x|4x>");
        }

        target.SetAntiAliasing(antiAliasing);
        return $"aa {args[0].ToLowerInvariant()}";
    }

    private static string TimeScale(ICommandTarget target, string[] args)
    {
        if (args.Length != 1 || !float.TryParse(args[0], CultureInfo.InvariantCulture, out var scale) || scale is <= 0f or > MaxTimeScale)
        {
            return Usage($"timescale <x> (0 to {MaxTimeScale})");
        }

        target.SetTimeScale(scale);
        return $"timescale {scale.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Quit(ICommandTarget target)
    {
        target.Quit();
        return "";
    }

    private static string Usage(string usage) => $"Usage: {usage}";

    private static string Name(Switch value) => Lower(value);

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Format(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
