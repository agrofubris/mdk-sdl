using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Formats.Scripts;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>The script VM on tiny hand-assembled programs, with level 7's runtime around it (no window).</summary>
public class ScriptVmTests
{
    private const int Level = 7;
    // Opcodes (docs/script_opcodes.md).
    private const int SetRestart = 1;
    private const int Goto = 12;
    private const int Wait = 64;
    private const int SetVar = 65;
    private const int AddVar = 66;
    private const int IfVar = 67;
    private const int SetFlag = 68;
    private const int IfFlagSet = 71;
    private const int Gosub = 252;
    private const int Return = 253;
    // Operand values.
    private const int Global = 0;
    private const int Own = 2;
    private const int LessThan = 1;
    private const int Literal = 3;
    private const byte ActionGoto = 0x0C;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);
    private static readonly ScriptRuntime Runtime = CreateRuntime();

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>A program's bytes: code starts after a gap (offset 0 means "no target"), targets are
    /// written as stored in a CMI (file offset - 4).</summary>
    private sealed class Code
    {
        private const int FileBase = 4;
        private const int Start = 8;
        private readonly List<byte> _bytes = [.. new byte[Start]];
        private readonly Dictionary<string, int> _labels = [];
        private readonly List<(int At, string Label)> _fixups = [];

        public int Entry => Start;

        public Code Label(string name)
        {
            _labels[name] = _bytes.Count;
            return this;
        }

        public Code U8(int value)
        {
            _bytes.Add((byte)value);
            return this;
        }

        public Code F32(float value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
            return this;
        }

        public Code Target(string label)
        {
            _fixups.Add((_bytes.Count, label));
            _bytes.AddRange(new byte[sizeof(int)]);
            return this;
        }

        public Code End() => U8(ScriptDecoder.End);

        public byte[] Build()
        {
            var bytes = _bytes.ToArray();
            foreach (var (at, label) in _fixups)
            {
                BitConverter.GetBytes(_labels[label] - FileBase).CopyTo(bytes, at);
            }

            return bytes;
        }
    }

    private static (ScriptVm Vm, MdkObject Obj) Load(Code code)
    {
        var vm = new ScriptVm(Runtime, new ScriptDecoder(code.Build()));
        return (vm, new MdkObject { Restart = code.Entry });
    }

    [Fact]
    public void LoopCountsWithVariablesAndGoto()
    {
        var code = new Code()
            .U8(SetVar).U8(Own).U8(0).F32(0f)
            .Label("loop")
            .U8(AddVar).U8(Own).U8(0).F32(1f)
            .U8(IfVar).U8(Own).U8(0).U8(LessThan).F32(5f).U8(ActionGoto).Target("loop")
            .U8(SetVar).U8(Global).U8(1).F32(7f)
            .End();
        var (vm, obj) = Load(code);

        vm.Run(obj);

        Assert.Equal(5f, obj.Variables[0]);
        Assert.Equal(7f, Runtime.GlobalVariables[1]);
        Assert.NotEqual(0, obj.Restart);
    }

    [Fact]
    public void GosubReturnsAfterTheCall()
    {
        var code = new Code()
            .U8(Gosub).U8(1).Target("sub")
            .U8(SetVar).U8(Own).U8(1).F32(2f)
            .End()
            .Label("sub")
            .U8(SetVar).U8(Own).U8(0).F32(1f)
            .U8(Return)
            .End();
        var (vm, obj) = Load(code);

        vm.Run(obj);

        Assert.Equal(1f, obj.Variables[0]);
        Assert.Equal(2f, obj.Variables[1]);
        Assert.Empty(obj.GosubReturns);
        Assert.Equal(code.Entry, obj.Restart);
    }

    [Fact]
    public void ReturnWithoutGosubStopsTheScript()
    {
        var (vm, obj) = Load(new Code().U8(Return).End());

        vm.Run(obj);

        Assert.Equal(0, obj.Restart);
    }

    [Fact]
    public void WaitResumesAfterItsTicks()
    {
        // 0.09 s: the script goes on at the 4th tick (30 ticks per second).
        const float Seconds = 0.09f;
        var code = new Code()
            .U8(SetRestart)
            .U8(AddVar).U8(Own).U8(0).F32(1f)
            .U8(Wait).U8(Literal).F32(Seconds)
            .U8(AddVar).U8(Own).U8(0).F32(10f)
            .End();
        var (vm, obj) = Load(code);

        vm.Run(obj);
        Assert.Equal(1f, obj.Variables[0]);
        vm.Run(obj);
        vm.Run(obj);
        Assert.Equal(1f, obj.Variables[0]);
        vm.Run(obj);
        Assert.Equal(11f, obj.Variables[0]);
    }

    [Fact]
    public void FlagsBranchAndGotoZeroStops()
    {
        const int Bit = 3;
        var code = new Code()
            .U8(SetFlag).U8(Own).U8(Bit)
            .U8(IfFlagSet).U8(Own).U8(Bit).U8(ActionGoto).Target("set")
            .End()
            .Label("set")
            .U8(SetVar).U8(Own).U8(2).F32(4f)
            .U8(Goto).U8(1).U8(0).U8(0).U8(0).U8(0)
            .End();
        var (vm, obj) = Load(code);

        vm.Run(obj);

        Assert.Equal(1 << Bit, obj.ScriptFlags);
        Assert.Equal(4f, obj.Variables[2]);
        Assert.Equal(0, obj.Restart);
    }

    [Fact]
    public void UnimplementedConditionsAreFalse()
    {
        // if_cheat_key (13) never holds: the goto isn't taken.
        const int IfCheatKey = 13;
        var code = new Code()
            .U8(IfCheatKey).U8(ActionGoto).Target("cheat")
            .U8(SetVar).U8(Own).U8(0).F32(1f)
            .End()
            .Label("cheat")
            .U8(SetVar).U8(Own).U8(0).F32(2f)
            .End();
        var (vm, obj) = Load(code);

        vm.Run(obj);

        Assert.Equal(1f, obj.Variables[0]);
    }
}
