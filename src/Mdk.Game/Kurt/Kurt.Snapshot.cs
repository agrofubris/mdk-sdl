using System.Numerics;
using System.Text.Json.Nodes;

namespace Mdk.Game.Kurt;

/// <summary>Kurt for a full save (the original's <c>DAMP</c> and <c>PLAY</c>; kurt.gd snapshot):
/// where he is, his health, his clip and his pickups. He comes back standing still.</summary>
public sealed partial class Kurt
{
    public JsonObject Snapshot() => new()
    {
        ["position"] = new JsonArray(Feet.X, Feet.Y, Feet.Z),
        ["yaw"] = Yaw,
        ["health"] = Health,
        ["invulnerable"] = Invulnerable,
        ["clip_rounds"] = Scope.ClipRounds,
        ["clip_time"] = Scope.ClipTime,
        ["inventory"] = new JsonObject
        {
            ["slots"] = new JsonArray(Inventory.Slots.Select(s => (JsonNode?)new JsonArray((int)s.Item, s.Count)).ToArray()),
            ["selected"] = Inventory.Selected,
            ["ammo"] = new JsonArray(Inventory.Ammo.Select(a => (JsonNode?)a).ToArray()),
            ["selected_ammo"] = Inventory.SelectedAmmo,
            ["super_chain_gun"] = Inventory.SuperChainGun,
        },
    };

    public void Restore(JsonObject data)
    {
        var p = data["position"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray();
        Teleport(new Vector3(p[0], p[1], p[2]), data["yaw"]!.GetValue<float>());
        Health = data["health"]!.GetValue<int>();
        Invulnerable = data["invulnerable"]!.GetValue<float>();
        Scope.ClipRounds = data["clip_rounds"]!.GetValue<int>();
        Scope.ClipTime = data["clip_time"]!.GetValue<float>();
        var inventory = data["inventory"]!.AsObject();
        var slots = inventory["slots"]!.AsArray().Select(s => new Inventory.Slot((Inventory.Item)s![0]!.GetValue<int>(), s[1]!.GetValue<int>()));
        Inventory.Restore(slots, inventory["selected"]!.GetValue<int>(), inventory["ammo"]!.AsArray().Select(a => a!.GetValue<int>()).ToList(),
            inventory["selected_ammo"]!.GetValue<int>(), inventory["super_chain_gun"]!.GetValue<int>());
    }
}
