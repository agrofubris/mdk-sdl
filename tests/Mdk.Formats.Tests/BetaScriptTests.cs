using Mdk.Formats.Scripts;

namespace Mdk.Formats.Tests;

/// <summary>The 1996 demo's bytecode (<see cref="ScriptDialect.Beta1996"/>) on synthetic scripts:
/// every opcode's length against the reference decoder's table (godot-mdk
/// <c>tools/python/beta96.py</c> <c>LAYOUTS</c>, copied here as is), and the operands made for the
/// retail handlers.</summary>
public class BetaScriptTests
{
    private const int Target = 0x40;
    private const int FileBase = 4;

    /// <summary>beta96.py's <c>LAYOUTS</c>.</summary>
    private static readonly Dictionary<int, string[]> Reference = new()
    {
        [1] = [], [2] = ["data"], [3] = ["data"], [4] = ["op4"], [5] = ["act"], [6] = [], [8] = ["s16"], [9] = [],
        [10] = ["pstr", "u8", "act"], [11] = ["u8"], [12] = ["rep"], [13] = ["act"], [14] = ["u16", "u8", "act"], [15] = [],
        [16] = ["u16"], [17] = ["act"], [18] = ["f32", "act"], [19] = [], [20] = [], [21] = [], [22] = ["act"], [23] = [],
        [24] = ["u8", "pstr"], [25] = ["pstr"], [26] = ["pstr"], [27] = ["act"], [28] = ["act"], [29] = ["u8", "pstr", "code"],
        [31] = ["reps"], [32] = ["reps"], [34] = ["act"], [35] = ["u8"], [36] = ["u8"], [37] = ["act"], [38] = ["cond", "act"],
        [39] = ["val"], [40] = ["val"], [41] = [], [42] = ["pstr", "act"], [43] = ["f32", "f32"], [44] = ["act"],
        [45] = ["cond", "act"], [46] = ["act"], [47] = ["f32", "act"], [48] = ["f32", "act"], [49] = ["u8", "act"],
        [50] = ["val"], [51] = ["val"], [52] = ["val"], [53] = ["val"], [54] = ["cond", "act"], [57] = ["u16", "u8", "act"],
        [58] = ["val"], [59] = ["data"], [60] = [], [61] = ["op61"], [62] = ["cond", "act"], [63] = ["u8"], [64] = ["val"],
        [65] = ["u8", "u8", "f32"], [66] = ["u8", "u8", "f32"], [67] = ["u8", "u8", "cond", "act"], [68] = ["u8", "u8"],
        [69] = ["u8", "u8"], [70] = ["u8", "u8"], [71] = ["u8", "u8", "act"], [72] = ["u8", "u8", "act"], [73] = ["u8"],
        [74] = ["u8", "u8", "pstr"], [75] = [], [76] = ["code"], [77] = ["pstr"], [78] = ["f32", "f32", "f32"],
        [79] = ["f32", "f32", "f32"], [80] = ["f32", "f32", "f32"], [81] = ["val"], [82] = ["val"], [83] = ["val"],
        [84] = ["val"], [85] = ["u8"], [86] = ["f32", "f32", "f32", "pstr", "code"], [87] = ["u16", "u16", "u8", "act"],
        [88] = ["u8"], [89] = ["pstr"], [90] = ["pstr", "u8"], [91] = ["val"], [92] = ["u8", "act"],
        [128] = ["pstr", "u8", "u8"], [129] = ["reps"], [130] = [], [131] = ["u8"], [0xF0] = ["rep"], [0xF1] = [],
    };

    /// <summary>Sample operands of a reference code: a goto action, a literal value, a "between"
    /// comparison, two targets, two names, a command 7 to selector 5, a part to fire from.</summary>
    private static void Write(Bytes b, string code)
    {
        switch (code)
        {
            case "u8":
                b.U8(7);
                break;
            case "u16" or "s16":
                b.S16(2);
                break;
            case "f32":
                b.F32(1.5f);
                break;
            case "pstr":
                b.Pascal("abc");
                break;
            case "data" or "code":
                b.U32(Target);
                break;
            case "act":
                b.U8(0x0C).U32(Target);
                break;
            case "val":
                b.U8(3).F32(2f);
                break;
            case "cond":
                b.U8(7).F32(1f, 2f);
                break;
            case "rep":
                b.U8(2).U32(Target, Target + 1);
                break;
            case "reps":
                b.U8(2).Pascal("ab").Pascal("c");
                break;
            case "op4":
                b.U8(7, 0x0C).U32(Target).U8(5).Pascal("xg").U8(3);
                break;
            case "op61":
                b.U8(1).Pascal("gun").U8(1).F32(10f, 50f, 0f);
                break;
        }
    }

    private static Instruction Decode(int opcode, Action<Bytes>? operands = null)
    {
        var b = new Bytes().U8(opcode);
        if (operands != null)
        {
            operands(b);
        }
        else
        {
            foreach (var code in Reference[opcode])
            {
                Write(b, code);
            }
        }

        return ScriptDecoder.Beta(b.ToArray()).Decode(0) ?? throw new InvalidDataException($"opcode {opcode}");
    }

    [Fact]
    public void EveryOpcodeHasTheReferenceLength()
    {
        foreach (var (opcode, codes) in Reference)
        {
            var b = new Bytes().U8(opcode);
            foreach (var code in codes)
            {
                Write(b, code);
            }

            Assert.Equal((opcode, b.Position), (opcode, Decode(opcode).Next));
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(33)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(93)]
    [InlineData(0xFC)]
    public void RetailOnlyOpcodesAreInvalid(int opcode) =>
        Assert.Null(ScriptDecoder.Beta([(byte)opcode, 0, 0, 0, 0, 0, 0, 0, 0]).Decode(0));

    [Theory]
    [InlineData(0x0C, BranchAction.Kind.Goto)]
    [InlineData(0xF0, BranchAction.Kind.Gosub)]
    [InlineData(0xF1, BranchAction.Kind.Return)]
    [InlineData(0xFC, BranchAction.Kind.None)]
    public void ActionsHaveTheirOwnCodes(int code, BranchAction.Kind kind)
    {
        var ins = Decode(17, b => b.U8(code).U32(Target));
        Assert.Equal(kind, ins.Action!.Type);
        if (kind is BranchAction.Kind.Goto or BranchAction.Kind.Gosub)
        {
            Assert.Equal(Target + FileBase, ins.Action.Target);
        }
    }

    [Fact]
    public void GosubAndReturnAreRetailOpcodes()
    {
        var gosub = Decode(0xF0);
        Assert.Equal(252, gosub.Opcode);
        Assert.Equal([Target + FileBase, Target + 1 + FileBase], ScriptDecoder.Targets(gosub));
        Assert.Equal(253, Decode(0xF1).Opcode);
    }

    [Fact]
    public void OperandsAreMadeForTheRetailHandlers()
    {
        Assert.Equal([-2], Decode(21).Operands);
        Assert.Equal([1], Decode(23).Operands);
        Assert.Equal([1, "ABC"], Decode(77).Operands);
        Assert.Equal([0, null, "ABC"], Decode(89).Operands);
        Assert.Equal([0, 2f], Decode(81).Operands);
        Assert.Equal(8, Decode(92).Operands[0]);
        Assert.Equal(new object?[] { 0, new object?[] { "AB", "C" } }, Decode(129).Operands);
    }

    [Fact]
    public void OwnOpcodesGetNumbersAboveTheRetailOnes()
    {
        Assert.Equal(BetaOpcodes.IfField108, Decode(5).Opcode);
        Assert.Equal(BetaOpcodes.IfAlarmEnded, Decode(28).Opcode);
        Assert.Equal(BetaOpcodes.Nothing, Decode(41).Opcode);
        Assert.Equal(BetaOpcodes.Nothing, Decode(90).Opcode);
        Assert.Equal(BetaOpcodes.FollowPath, Decode(2).Opcode);
    }

    [Fact]
    public void CommandObjectsSelectByAByteId()
    {
        var ins = Decode(4);
        Assert.Equal(7, ins.Operands[0]);
        Assert.Equal(BranchAction.Kind.Goto, ((BranchAction)ins.Operands[1]!).Type);
        Assert.Equal(5, ins.Operands[2]);
        Assert.Equal(new object?[] { "XG", 3 }, ins.Operands[3]);
        Assert.Equal([Target + FileBase], ScriptDecoder.Targets(ins));
    }

    [Fact]
    public void FireIsABoltFromAPart()
    {
        var ins = Decode(61);
        Assert.Equal(BetaOpcodes.Fire, ins.Opcode);
        Assert.Equal(new object?[] { 1, "GUN" }, ins.Operands[0]);
        Assert.Equal([1, 10f, 50f, 0f], ins.Operands[1..]);
    }
}
