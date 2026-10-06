using System.Numerics;
using System.Text.Json.Nodes;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Full saves' packing of objects (snapshot.gd): fields, references between objects and
/// to the arenas' script objects survive a round trip through JSON text.</summary>
public class SnapshotTests
{
    private const string ControllerKey = "arena:HMO_1";

    private static MdkObject Alien(string type) => new() { TypeName = type, Arena = "HMO_1" };

    [Fact]
    public void ObjectsComeBackAsTheyWere()
    {
        var controller = Alien("");
        var leader = Alien("XG");
        var follower = Alien("XS");
        leader.Position = new Vector3(1.5f, -2f, 300.25f);
        leader.Health = 42;
        leader.Variables[2] = 7.5f;
        leader.GosubReturns.AddRange([10, 20]);
        leader.AttachPoints = (1, 3);
        leader.DoorSounds = ["OPEN", "", "", "SHUT"];
        leader.RollingBasis = Matrix4x4.CreateRotationZ(0.5f);
        leader.MaxSpeed = float.PositiveInfinity;
        follower.Leader = leader;
        follower.Linked = controller;

        var fixedObjects = new Dictionary<string, MdkObject> { [ControllerKey] = controller };
        var packer = new Snapshot([leader, follower], fixedObjects, (_, _) => null);
        var text = new JsonArray(packer.Pack(leader), packer.Pack(follower)).ToJsonString();

        // Into new objects, as a load creates them.
        var leader2 = Alien("XG");
        var follower2 = Alien("XS");
        var unpacker = new Snapshot([leader2, follower2], fixedObjects, (_, _) => null);
        var data = JsonNode.Parse(text)!.AsArray();
        unpacker.Unpack(leader2, data[0]!.AsObject());
        unpacker.Unpack(follower2, data[1]!.AsObject());

        Assert.Equal(leader.Position, leader2.Position);
        Assert.Equal(42, leader2.Health);
        Assert.Equal(7.5f, leader2.Variables[2]);
        Assert.Equal([10, 20], leader2.GosubReturns);
        Assert.Equal((1, 3), leader2.AttachPoints);
        Assert.Equal("SHUT", leader2.DoorSounds[3]);
        Assert.Equal(leader.RollingBasis, leader2.RollingBasis);
        Assert.Equal(float.PositiveInfinity, leader2.MaxSpeed);
        Assert.Same(leader2, follower2.Leader);
        Assert.Same(controller, follower2.Linked);
        Assert.Equal(text, new JsonArray(unpacker.Pack(leader2), unpacker.Pack(follower2)).ToJsonString());
    }

    [Fact]
    public void HashIsStable()
    {
        Assert.Equal(Snapshot.Hash("{\"a\":1}"), Snapshot.Hash("{\"a\":1}"));
        Assert.NotEqual(Snapshot.Hash("{\"a\":1}"), Snapshot.Hash("{\"a\":2}"));
    }
}
