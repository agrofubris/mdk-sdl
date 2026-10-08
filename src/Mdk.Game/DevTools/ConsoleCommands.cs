using System.Globalization;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;

namespace Mdk.Game.DevTools;

/// <summary>The console's commands: their arguments parsed, checked and handed to the
/// <see cref="ICommandTarget"/>; bad ones print the usage. Those of the level answer
/// <see cref="NotInLevel"/> on the other screens.</summary>
public static class ConsoleCommands
{
    private const string All = "all";
    private const int Coordinates = 3;
    private const float MaxTimeScale = 10f;
    private const string NotInLevel = "Not in a level";
    /// <summary>God mode, noclip and onehit switched outside a level.</summary>
    private const string NextLevel = " from the next level";

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
        registry.Add(new Command("teleport", "teleport <x y z | arena [x y z]>", args => InLevel(target, level => Teleport(level, args)), ["tp"]));
        registry.Add(new Command("pos", "pos", _ => InLevel(target, level => Pos(level.Where()))));
        registry.Add(new Command("noclip", "noclip", _ => Session(target, "noclip", target.ToggleNoclip())));
        registry.Add(new Command("god", "god", _ => Session(target, "god", target.ToggleGod())));
        registry.Add(new Command("onehit", "onehit", _ => Session(target, "onehit", target.ToggleOneHit())));
        registry.Add(new Command("give", "give <all | SW_...>", args => InLevel(target, level => Give(level, args))));
        registry.Add(new Command("health", "health <n>", args => InLevel(target, level => Health(level, args))));
        registry.Add(new Command("kill", "kill", _ => InLevel(target, level => $"Killed {level.KillEnemies()} enemies")));
        registry.Add(new Command("save", "save <slot>", args => InLevel(target, level => Save(level, args))));
        registry.Add(new Command("load", "load <slot>", args => Load(target, args)));
        registry.Add(new Command("difficulty", "difficulty <easy|normal|hard>", args => SetDifficulty(target, args)));
        registry.Add(new Command("look", "look <original|enhanced>", args => Look(target, args)));
        registry.Add(new Command("aa", "aa <off|2x|4x>", args => AntiAlias(target, args)));
        registry.Add(new Command("stereo", "stereo <off|sbs|crossview|int|intr> [separation] [convergence]", args => Stereo(target, args)));
        registry.Add(new Command("timescale", "timescale <x>", args => TimeScale(target, args)));
        registry.Add(new Command("fps", "fps", _ => $"overlay {Name(target.ToggleOverlay())}"));
        registry.Add(new Command("mods", "mods", _ => Mods(target)));
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
    private static string Teleport(ILevelTarget target, string[] args)
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

    private static string Give(ILevelTarget target, string[] args)
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

    private static string Health(ILevelTarget target, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], CultureInfo.InvariantCulture, out var health) || health < 0)
        {
            return Usage("health <n>");
        }

        target.SetHealth(health);
        return $"health {health}";
    }

    private static string Save(ILevelTarget target, string[] args)
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

        return target.SetLook(look) ? $"look {Lower(look)}" : $"look {Lower(look)}{NextLevel}";
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

    /// <summary>The stereo mode, and the eyes' separation and convergence when given.</summary>
    private static string Stereo(ICommandTarget target, string[] args)
    {
        const string usage = "stereo <off|sbs|crossview|int|intr> [separation] [convergence]";
        if (args.Length is < 1 or > 3 || !StereoModes.TryParse(args[0], out var stereo))
        {
            return Usage(usage);
        }

        float? separation = null;
        float? convergence = null;
        if (args.Length > 1 && float.TryParse(args[1], CultureInfo.InvariantCulture, out var separationValue))
        {
            separation = separationValue;
        }
        else if (args.Length > 1)
        {
            return Usage(usage);
        }

        if (args.Length > 2 && float.TryParse(args[2], CultureInfo.InvariantCulture, out var convergenceValue))
        {
            convergence = convergenceValue;
        }
        else if (args.Length > 2)
        {
            return Usage(usage);
        }

        target.SetStereo(stereo, separation, convergence);
        return $"stereo {args[0].ToLowerInvariant()}";
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

    /// <summary>The mods found (on or off), then what the level took from them.
    /// <code>
    ///   Clean walls (clean-walls) on, priority 0
    ///   HD textures (Real-ESRGAN) (hd-textures) off, priority -100
    ///   mods 1: 14 images, 1 models
    ///   images: WALL1 WALL2 ...
    /// </code></summary>
    private static string Mods(ICommandTarget target)
    {
        var list = target.ModList();
        IEnumerable<string> lines = list.Count > 0 ? list : ["No mods in mods/"];
        if (target.Level is { } level)
        {
            lines = lines.Concat(level.Mods.Lines());
        }

        return string.Join('\n', lines);
    }

    private static string Quit(ICommandTarget target)
    {
        target.Quit();
        return "";
    }

    /// <summary>A command of the level: run on it, or <see cref="NotInLevel"/>.</summary>
    private static string InLevel(ICommandTarget target, Func<ILevelTarget, string> run) =>
        target.Level is { } level ? run(level) : NotInLevel;

    /// <summary>"god on"; outside a level "god on from the next level".</summary>
    private static string Session(ICommandTarget target, string name, Switch value) =>
        $"{name} {Name(value)}{(target.Level == null ? NextLevel : "")}";

    private static string Usage(string usage) => $"Usage: {usage}";

    private static string Name(Switch value) => Lower(value);

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Format(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
