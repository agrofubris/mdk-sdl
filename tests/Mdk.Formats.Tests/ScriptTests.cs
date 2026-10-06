using Mdk.Formats.Scripts;

namespace Mdk.Formats.Tests;

/// <summary>Decodes every script of the game, walking from each entry point like the Godot port's
/// <c>mdk_script_dis.py check</c>.</summary>
public class ScriptTests
{
    private const int FirstLevel = 3;
    private const int LastLevel = 8;
    /// <summary>Counts of <c>mdk_script_dis.py</c> over the same entry points (one bad opcode in
    /// data the scripts never reach at run time).</summary>
    private const int ExpectedDecoded = 99063;
    private const int ExpectedInvalid = 1;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");

    [DataFact]
    public void EveryScriptDecodes()
    {
        var decoded = 0;
        var invalid = 0;
        for (var level = FirstLevel; level <= LastLevel; level++)
        {
            var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{level}/LEVEL{level}.CMI"));
            var decoder = new ScriptDecoder(cmi.Bytes);
            foreach (var start in cmi.EntryPoints())
            {
                var (ok, bad) = Walk(decoder, start);
                decoded += ok;
                invalid += bad;
            }
        }

        Assert.Equal(ExpectedInvalid, invalid);
        Assert.Equal(ExpectedDecoded, decoded);
    }

    /// <summary>Instructions reachable from <paramref name="start"/>: decoded and invalid.</summary>
    private static (int Decoded, int Invalid) Walk(ScriptDecoder decoder, int start)
    {
        var seen = new HashSet<int>();
        var invalid = 0;
        var work = new Stack<int>([start]);
        while (work.Count > 0)
        {
            var p = work.Pop();
            while (seen.Add(p))
            {
                var ins = decoder.Decode(p);
                if (ins == null)
                {
                    invalid++;
                    break;
                }

                if (ins.Opcode == ScriptDecoder.End)
                {
                    break;
                }

                foreach (var target in ScriptDecoder.Targets(ins))
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

        return (seen.Count - invalid, invalid);
    }
}
