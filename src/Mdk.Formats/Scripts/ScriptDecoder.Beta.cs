namespace Mdk.Formats.Scripts;

/// <summary>The bytecode of a CMI file: the retail game's, or the 1996 demo's earlier one.</summary>
public enum ScriptDialect { Retail, Beta1996 }

/// <summary>The 1996 demo's opcodes that have no retail equivalent, numbered above 255 (see
/// <see cref="ScriptDecoder"/>, godot-mdk <c>beta_script_decoder.gd</c>).</summary>
public static class BetaOpcodes
{
    /// <summary>Opcodes 41 (nothing) and 90 (<c>name, u8</c>: an arena by name; in no script).</summary>
    public const int Nothing = 300;
    /// <summary><c>follow_path</c> (2): <c>[path]</c>. A path stopped by opcode 21 goes on; the
    /// object turns along it.</summary>
    public const int FollowPath = 302;
    /// <summary>Opcode 5: branches when the object's field <c>+0x108</c> is 0 (in no script).</summary>
    public const int IfField108 = 305;
    /// <summary>Opcode 28: branches while an object that sounded the alarm was just deleted
    /// (<c>0xe21de</c>, 10 ticks).</summary>
    public const int IfAlarmEnded = 328;
    /// <summary><c>fire</c> (61): <c>[origin, aim, range, accuracy, ?]</c>, a bolt without a script.</summary>
    public const int Fire = 361;
}

/// <summary>The 1996 demo's scripts (<c>MDKDEMO.EXE</c> 0x47f6c, godot-mdk docs/beta96.md), decoded
/// into the instructions the retail VM runs: opcodes 1-92 and 128-131 have the retail numbers and
/// mostly the retail operands; branch actions are <c>0x0C</c> goto, <c>0xF0</c> gosub, <c>0xF1</c>
/// return; gosub and return are the opcodes 0xF0 and 0xF1. Where an opcode differs, the operands the
/// retail handler expects are made here; names are made upper case.</summary>
public sealed partial class ScriptDecoder
{
    private const byte BetaGoto = 0x0C;
    private const byte BetaGosub = 0xF0;
    private const byte BetaReturn = 0xF1;
    private const int RetailGosub = 252;
    private const int RetailReturn = 253;
    private const int SelectorById = 5;
    private const int CommandGoto = 7;
    private const int CommandMove = 43;
    /// <summary>Opcodes after which a script doesn't go on: end, stop, goto, return.</summary>
    private static readonly int[] BetaStops = [End, 9, 12, BetaReturn];

    /// <summary>Demo opcode to its operands and, when it isn't the same, the retail opcode. Opcodes 2,
    /// 4 and 61 are read by <see cref="DecodeSpecial"/>.</summary>
    private static readonly Dictionary<int, (string[] Codes, int Opcode)> BetaLayouts = new()
    {
        [1] = ([], 1), [3] = (["data32"], 3), [5] = (["action"], BetaOpcodes.IfField108), [6] = ([], 6),
        [8] = (["s16"], 8), [9] = ([], 9), [10] = (["pstr", "u8", "action"], 10), [11] = (["u8"], 11),
        [12] = (["repeat:code32"], 12), [13] = (["action"], 13), [14] = (["u16", "u8", "action"], 14),
        [15] = ([], 15), [16] = (["u16"], 16), [17] = (["action"], 17), [18] = (["f32", "action"], 18),
        [19] = ([], 19), [20] = ([], 20), [21] = ([], 21), [22] = (["action"], 22), [23] = ([], 23),
        [24] = (["u8", "pstr"], 24), [25] = (["pstr"], 25), [26] = (["pstr"], 26), [27] = (["action"], 27),
        [28] = (["action"], BetaOpcodes.IfAlarmEnded), [29] = (["u8", "pstr", "code32"], 29),
        [31] = (["repeat:pstr"], 31), [32] = (["repeat:pstr"], 32), [34] = (["action"], 34), [35] = (["u8"], 35),
        [36] = (["u8"], 36), [37] = (["action"], 37), [38] = (["cond", "action"], 38), [39] = (["value"], 39),
        [40] = (["value"], 40), [41] = ([], BetaOpcodes.Nothing), [42] = (["pstr", "action"], 42),
        [43] = (["f32", "f32"], 43), [44] = (["action"], 44), [45] = (["cond", "action"], 45), [46] = (["action"], 46),
        [47] = (["f32", "action"], 47), [48] = (["f32", "action"], 48), [49] = (["u8", "action"], 49),
        [50] = (["value"], 50), [51] = (["value"], 51), [52] = (["value"], 52), [53] = (["value"], 53),
        [54] = (["cond", "action"], 54), [57] = (["u16", "u8", "action"], 57), [58] = (["value"], 58),
        [59] = (["data32"], 59), [60] = ([], 60), [62] = (["cond", "action"], 62), [63] = (["u8"], 63),
        [64] = (["value"], 64), [65] = (["u8", "u8", "f32"], 65), [66] = (["u8", "u8", "f32"], 66),
        [67] = (["u8", "u8", "cond", "action"], 67), [68] = (["u8", "u8"], 68), [69] = (["u8", "u8"], 69),
        [70] = (["u8", "u8"], 70), [71] = (["u8", "u8", "action"], 71), [72] = (["u8", "u8", "action"], 72),
        [73] = (["u8"], 73), [74] = (["u8", "u8", "pstr"], 74), [75] = ([], 75), [76] = (["code32"], 76),
        [77] = (["pstr"], 77), [78] = (["f32", "f32", "f32"], 78), [79] = (["f32", "f32", "f32"], 79),
        [80] = (["f32", "f32", "f32"], 80), [81] = (["value"], 81), [82] = (["value"], 82), [83] = (["value"], 83),
        [84] = (["value"], 84), [85] = (["u8"], 85), [86] = (["f32", "f32", "f32", "pstr", "code32"], 86),
        [87] = (["u16", "u16", "u8", "action"], 87), [88] = (["u8"], 88), [89] = (["pstr"], 89),
        [90] = (["pstr", "u8"], BetaOpcodes.Nothing), [91] = (["value"], 91), [92] = (["u8", "action"], 92),
        [128] = (["pstr", "u8", "u8"], 128), [129] = (["repeat:pstr"], 129), [130] = ([], 130), [131] = (["u8"], 131),
        [BetaGosub] = (["repeat:code32"], RetailGosub), [BetaReturn] = ([], RetailReturn),
    };

    private ScriptDialect _dialect = ScriptDialect.Retail;
    /// <summary>The demo's path offsets to the converted ones (<see cref="Cmi.BetaPaths"/>).</summary>
    private IReadOnlyDictionary<int, int> _paths = new Dictionary<int, int>();

    private ScriptDecoder(byte[] bytes, ScriptDialect dialect, IReadOnlyDictionary<int, int> paths) : this(bytes)
    {
        _dialect = dialect;
        _paths = paths;
    }

    /// <summary>The decoder of a CMI file's scripts, in its dialect.</summary>
    public static ScriptDecoder For(Cmi cmi) =>
        cmi.Dialect == ScriptDialect.Retail ? new ScriptDecoder(cmi.Bytes) : new ScriptDecoder(cmi.Bytes, cmi.Dialect, cmi.BetaPaths);

    /// <summary>A decoder of the demo's bytecode, its paths not converted (tests, path search).</summary>
    internal static ScriptDecoder Beta(byte[] bytes) => new(bytes, ScriptDialect.Beta1996, new Dictionary<int, int>());

    /// <summary>The paths the demo's scripts follow (the raw offsets of opcode 2), found by walking
    /// every script from its entry point, in the order met.</summary>
    internal static List<int> BetaPaths(Cmi cmi)
    {
        const int FollowPath = 2;
        var bytes = cmi.Bytes;
        var decoder = Beta(bytes);
        var paths = new List<int>();
        var seen = new HashSet<int>();
        var todo = new Stack<int>(cmi.EntryPoints());
        while (todo.Count != 0)
        {
            var pc = todo.Pop();
            while (pc > 0 && pc < bytes.Length && seen.Add(pc))
            {
                int opcode = bytes[pc];
                if (opcode == FollowPath && Bin.U32(bytes, pc + 1) is var raw and not 0 && !paths.Contains((int)raw + FileBase))
                {
                    paths.Add((int)raw + FileBase);
                }

                if (decoder.Decode(pc) is not { } ins)
                {
                    break;
                }

                foreach (var target in Targets(ins))
                {
                    todo.Push(target);
                }

                if (BetaStops.Contains(opcode))
                {
                    break;
                }

                pc = ins.Next;
            }
        }

        return paths;
    }

    private Instruction? DecodeBeta(int pc)
    {
        int opcode = bytes[pc];
        _p = pc + 1;
        _action = null;
        if (opcode == End)
        {
            return new Instruction(pc, opcode, [], _p, null);
        }

        var operands = new List<object?>();
        if (DecodeSpecial(opcode, operands) is { } special)
        {
            return new Instruction(pc, special, [.. operands], _p, _action);
        }

        if (!BetaLayouts.TryGetValue(opcode, out var layout))
        {
            return null;
        }

        foreach (var code in layout.Codes)
        {
            operands.Add(ReadBeta(code, operands));
        }

        return new Instruction(pc, layout.Opcode, [.. RetailOperands(opcode, operands)], _p, _action);
    }

    /// <summary>Opcodes 2, 4 and 61, whose operands depend on earlier ones. Returns the opcode to
    /// run, or null for the others.</summary>
    private int? DecodeSpecial(int opcode, List<object?> o)
    {
        const int FollowPath = 2;
        const int CommandObjects = 4;
        const int Fire = 61;
        switch (opcode)
        {
            case FollowPath:
                o.Add(_paths.GetValueOrDefault(Offset()));
                return BetaOpcodes.FollowPath;
            case CommandObjects:
            {
                // The selector's id is a byte.
                int command = U8();
                object? arguments = command switch
                {
                    CommandGoto => ReadBetaAction(),
                    CommandMove => Floats(2),
                    _ => null,
                };
                int selector = U8();
                var selected = new List<object?>();
                if (selector is 2 or 4 or SelectorById)
                {
                    selected.Add(BetaString());
                }

                if (selector == SelectorById)
                {
                    selected.Add((int)U8());
                }

                o.AddRange([command, arguments, selector, selected.ToArray()]);
                return CommandObjects;
            }
            case Fire:
            {
                // Where from (a reference point or a part), whether it's aimed, then three floats.
                int mode = U8();
                object?[] origin = [mode, mode == 0 ? (int)U8() : BetaString()];
                o.AddRange([origin, (int)U8(), F32(), F32(), F32()]);
                return BetaOpcodes.Fire;
            }
        }

        return null;
    }

    /// <summary>The operands the retail handlers take, where the demo's differ.</summary>
    private static List<object?> RetailOperands(int opcode, List<object?> o)
    {
        const int StopHere = -2;
        switch (opcode)
        {
            case 21:
                // Stops the path where it is.
                return [StopHere];
            case 23:
                return [1];
            case 77:
                return [1, o[0]];
            case 81:
                // Not multiplied by the frame time.
                return [0, o[0]];
            case 89:
                // Played where the object is.
                return [0, null, o[0]];
            case 92:
                // The frame itself, not the frame + 1.
                return [(int)o[0]! + 1, o[1]];
            case 129:
                // No mode.
                return [0, ((object?[])o[0]!).Select(entry => ((object?[])entry!)[0]).ToArray()];
        }

        return o;
    }

    private object? ReadBeta(string code, List<object?> prev)
    {
        if (code == "action")
        {
            return ReadBetaAction();
        }

        if (code == "pstr")
        {
            return BetaString();
        }

        if (!code.StartsWith(RepeatPrefix))
        {
            return Read(code, prev);
        }

        int count = U8();
        var item = code[RepeatPrefix.Length..];
        var items = new object?[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = new[] { ReadBeta(item, prev) };
        }

        return items;
    }

    private BranchAction ReadBetaAction()
    {
        var action = U8();
        _action = action switch
        {
            BetaGosub => new BranchAction(BranchAction.Kind.Gosub, Offset()),
            BetaGoto => new BranchAction(BranchAction.Kind.Goto, Offset()),
            BetaReturn => new BranchAction(BranchAction.Kind.Return),
            _ => new BranchAction(BranchAction.Kind.None, Raw: action),
        };
        return _action;
    }

    /// <summary>Names are in lower case in the demo's scripts; the port's are in upper case.</summary>
    private string BetaString() => PascalString().ToUpperInvariant();
}
