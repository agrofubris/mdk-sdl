namespace Mdk.Formats.Scripts;

/// <summary>What a branch operand does: goto, gosub, gosub one of two (then/else), or return.
/// Targets are absolute file offsets.</summary>
public sealed record BranchAction(BranchAction.Kind Type, int Target = 0, int Else = 0, int Raw = 0)
{
    public enum Kind { None, Goto, Gosub, GosubElse, Return }
}

/// <summary>A decoded instruction. Offsets (code32/data32 operands, branch targets) are absolute
/// file offsets (0 stays 0). Operands are <c>int</c>, <c>float</c>, <c>string</c>,
/// <see cref="BranchAction"/>, <see cref="Variable"/>, lists (<c>object?[]</c>) or null.</summary>
public sealed record Instruction(int Pc, int Opcode, object?[] Operands, int Next, BranchAction? Action);

/// <summary>A script variable operand: kind (0-2) and index.</summary>
public readonly record struct Variable(int Kind, int Index);

/// <summary>Decodes MDK script bytecode (<c>LEVELn.CMI</c>), like the Godot port's
/// <c>script_decoder.gd</c> and <c>mdk_script_dis.py</c> (validated against every script).</summary>
public sealed partial class ScriptDecoder(byte[] bytes)
{
    public const int End = 0xFF;
    private const int FileBase = 4;
    private const int LiteralKind = 3;
    private const byte ActionGosubElse = 0xFE;
    private const byte ActionGosub = 0xFC;
    private const byte ActionGoto = 0x0C;
    private const byte ActionReturn = 0xFD;
    private const string RepeatPrefix = "repeat:";

    private readonly Dictionary<int, Instruction?> _cache = [];
    private int _p;
    private BranchAction? _action;

    /// <summary>The instruction at file offset <paramref name="pc"/> (cached), or null for an invalid opcode.</summary>
    public Instruction? Decode(int pc)
    {
        if (_cache.TryGetValue(pc, out var cached))
        {
            return cached;
        }

        if (_dialect == ScriptDialect.Beta1996)
        {
            return _cache[pc] = DecodeBeta(pc);
        }

        int opcode = bytes[pc];
        _p = pc + 1;
        _action = null;
        var operands = new List<object?>();
        if (opcode != End)
        {
            if (!ScriptOpcodes.Operands.TryGetValue(opcode, out var codes))
            {
                return _cache[pc] = null;
            }

            foreach (var code in codes)
            {
                operands.Add(Read(code, operands));
            }
        }

        return _cache[pc] = new Instruction(pc, opcode, [.. operands], _p, _action);
    }

    /// <summary>Decodes every instruction reachable from <paramref name="starts"/> into the cache (a
    /// level's load: the scripts then run without decoding); returns how many. The retail dialect only.</summary>
    public int Preload(IEnumerable<int> starts)
    {
        if (_dialect != ScriptDialect.Retail)
        {
            return 0;
        }

        var seen = new HashSet<int>();
        var work = new Stack<int>(starts);
        while (work.Count > 0)
        {
            var p = work.Pop();
            while (seen.Add(p))
            {
                var ins = Decode(p);
                if (ins == null || ins.Opcode == End)
                {
                    break;
                }

                foreach (var target in Targets(ins))
                {
                    work.Push(target);
                }

                if (ScriptOpcodes.NoFallthrough.Contains(ins.Opcode))
                {
                    break;
                }

                p = ins.Next;
            }
        }

        return seen.Count;
    }

    /// <summary>The instructions decoded so far (after <see cref="Preload"/>: every reachable one).</summary>
    public IEnumerable<Instruction> Decoded => _cache.Values.OfType<Instruction>();

    /// <summary>The code offsets an instruction can continue to besides the next one.</summary>
    public static List<int> Targets(Instruction ins)
    {
        var targets = new List<int>();
        var codes = ScriptOpcodes.Operands.GetValueOrDefault(ins.Opcode, []);
        for (var i = 0; i < codes.Length; i++)
        {
            var code = codes[i];
            if (code == "code32" && ins.Operands[i] is int offset && offset != 0)
            {
                targets.Add(offset);
            }
            else if (code.StartsWith(RepeatPrefix) && ins.Operands[i] is object?[] items)
            {
                var itemCodes = code[RepeatPrefix.Length..].Split(',');
                foreach (var item in items.Cast<object?[]>())
                {
                    for (var k = 0; k < itemCodes.Length; k++)
                    {
                        if (itemCodes[k] == "code32" && item[k] is int target && target != 0)
                        {
                            targets.Add(target);
                        }
                    }
                }
            }
            else if (code == "op158_hit_target" && ins.Operands[i] is int hit && hit != 0)
            {
                targets.Add(hit);
            }
        }

        if (ins.Action is { Type: not BranchAction.Kind.None } action)
        {
            targets.AddRange(new[] { action.Target, action.Else }.Where(t => t != 0));
        }

        return targets;
    }

    private byte U8() => bytes[_p++];

    private float F32()
    {
        _p += 4;
        return Bin.F32(bytes, _p - 4);
    }

    private uint U32()
    {
        _p += 4;
        return Bin.U32(bytes, _p - 4);
    }

    private int Offset()
    {
        var value = U32();
        return value == 0 ? 0 : (int)value + FileBase;
    }

    private string PascalString()
    {
        int length = U8();
        _p += length;
        return Bin.Ascii(bytes, _p - length, length);
    }

    private object?[] Floats(int count)
    {
        var floats = new object?[count];
        for (var i = 0; i < count; i++)
        {
            floats[i] = F32();
        }

        return floats;
    }

    private BranchAction ReadAction()
    {
        var action = U8();
        _action = action switch
        {
            ActionGosubElse => new BranchAction(BranchAction.Kind.GosubElse, Offset(), Offset()),
            ActionGosub => new BranchAction(BranchAction.Kind.Gosub, Offset()),
            ActionGoto => new BranchAction(BranchAction.Kind.Goto, Offset()),
            ActionReturn => new BranchAction(BranchAction.Kind.Return),
            _ => new BranchAction(BranchAction.Kind.None, Raw: action),
        };
        return _action;
    }

    /// <summary>A variable, or a float literal (kind 3).</summary>
    private object ReadValue()
    {
        int kind = U8();
        return kind == LiteralKind ? F32() : new Variable(kind, U8());
    }

    private static object?[] Concat(params object?[][] parts) => parts.SelectMany(p => p).ToArray();

    private object? Read(string code, List<object?> prev)
    {
        switch (code)
        {
            case "u8": return (int)U8();
            case "s8": return (int)(sbyte)U8();
            case "u16": _p += 2; return (int)Bin.U16(bytes, _p - 2);
            case "s16": _p += 2; return (int)Bin.S16(bytes, _p - 2);
            case "u32": return (int)U32();
            case "s32": _p += 4; return Bin.S32(bytes, _p - 4);
            case "f32": return F32();
            case "pstr": return PascalString();
            case "code32":
            case "data32": return Offset();
            case "action": return ReadAction();
            case "value": return ReadValue();
            case "cond":
            {
                int op = U8();
                const int Between = 7;
                const int Outside = 8;
                return op is Between or Outside ? new object?[] { op, F32(), F32() } : new object?[] { op, F32() };
            }
        }

        // Layouts depending on earlier operands (complex_operand() in the Python tool).
        return code switch
        {
            "op4_command_args" => (int)prev[0]! switch
            {
                7 => ReadAction(),
                43 => Floats(2),
                _ => null,
            },
            "op4_selector_args" => SelectorArgs((int)prev[2]!),
            "op2_origin" => (int)prev[4]! == 0 ? Floats(3) : null,
            "op164_params" => (int)prev[0]! != 0 ? Floats(4) : null,
            "op42_part" => PartName(),
            "op249_sound" => (int)prev[0]! == 1 ? PascalString() : null,
            "op250_type_name" => (int)prev[0]! == 0xFF ? PascalString() : null,
            "op250_box" => Floats((int)prev[2]! == 2 ? 4 : 6),
            "op174_b" or "op175_b" => (int)prev[1]! is 7 or 8 ? F32() : null,
            "op129_parts" => (int)prev[0]! is 0 or 1 or 2 ? Enumerable.Range(0, U8()).Select(_ => (object?)PascalString()).ToArray() : null,
            "op132_position" => (int)prev[1]! == 0xFF ? Floats(3) : null,
            "op89_position" => Op89Position((int)prev[0]!),
            "op61_origin" => Op61Origin(),
            "op83_args" => Op83Args(),
            "op159_position" => ModeThenFloats(mode => mode is 1 or 2 ? 3 : 0),
            "op189_args" => ModeThenFloats(mode => mode switch { 0 => 2, 1 => 1, _ => 0 }),
            "op193_mode" => Op193Mode(),
            "op245_args" => Op245Args(),
            "op248_args" => Op248Args(),
            "op172_position" or "op178_position" => Op172Position(),
            "op173_target" => Op173Target(),
            "op180_offset" => ModeThenFloats(mode => mode != 0 ? 3 : 0),
            "op181_source" => Op181Source(),
            "op203_height" => ModeThenFloats(mode => mode == 1 ? 1 : 0),
            "op224_params" => ModeThenFloats(mode => mode != 0 ? 2 : 0),
            "op228_type_name" => Op228TypeName(),
            "op242_params" => ModeThenFloats(mode => mode != 0 ? 12 : 0),
            "op158_hit_target" => ((int)prev[2]! & 2) != 0 ? Offset() : null,
            _ when code.StartsWith(RepeatPrefix) => Repeat(code[RepeatPrefix.Length..].Split(','), prev),
            _ => throw new InvalidDataException($"Unknown operand code {code}"),
        };
    }

    private object?[] SelectorArgs(int selector)
    {
        var args = new List<object?>();
        if (selector is 6 or 10)
        {
            args.Add(F32());
        }

        if (selector is 2 or 4 or 5 or 6 or 7 or 10)
        {
            args.Add(PascalString());
        }

        if (selector == 5)
        {
            args.Add((int)U32());
        }

        return [.. args];
    }

    private object PartName()
    {
        var part = PascalString();
        return part.Length == 0 ? new object?[] { "", PascalString() } : part;
    }

    private object? Op89Position(int flags)
    {
        const int Point = 0x10;
        const int Index = 0x20;
        const int Offset = 0x40;
        if ((flags & Point) != 0)
        {
            return Floats(3);
        }

        if ((flags & Index) != 0)
        {
            return (int)U8();
        }

        return (flags & Offset) != 0 ? Floats(3) : null;
    }

    private object?[] Op61Origin()
    {
        int mode = U8();
        return [mode, mode == 0 ? (int)U8() : PascalString()];
    }

    private object Op83Args()
    {
        if (bytes[_p] != 0xFF)
        {
            return ReadValue();
        }

        _p++;
        return Floats(2);
    }

    /// <summary>A mode byte, then as many floats as the mode asks for.</summary>
    private object?[] ModeThenFloats(Func<int, int> count)
    {
        int mode = U8();
        return Concat([mode], Floats(count(mode)));
    }

    private object?[] Op193Mode()
    {
        int mode = U8();
        return mode == 3 ? [mode, PascalString()] : [mode];
    }

    private object?[] Op245Args()
    {
        if (bytes[_p] != 0)
        {
            return [PascalString()];
        }

        _p++;
        return [(int)U8(), PascalString()];
    }

    private object?[] Op248Args()
    {
        int mode = U8();
        var x = F32();
        return Concat([mode, x], Floats(mode == 0 ? 2 : 1));
    }

    private object?[] Op172Position()
    {
        int mode = U8();
        return mode == 3 ? [mode, (int)U8()] : Concat([mode], Floats(3));
    }

    private object?[] Op173Target()
    {
        var name = PascalString();
        return name.Length != 0 ? [name] : Concat(["", PascalString()], Floats(4));
    }

    private object?[] Op181Source()
    {
        int mode = U8();
        return mode switch
        {
            0 => [mode, (int)U8(), (int)U32()],
            1 => Concat([mode, (int)U8()], Floats(2), [(int)U32()]),
            _ => [mode],
        };
    }

    private object Op228TypeName()
    {
        if (bytes[_p] != 0)
        {
            return PascalString();
        }

        _p++;
        return new object?[] { "", (int)U8() };
    }

    private object?[] Repeat(string[] itemCodes, List<object?> prev)
    {
        int count = U8();
        var items = new object?[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = itemCodes.Select(c => Read(c, prev)).ToArray();
        }

        return items;
    }
}
