using Mdk.Game.Kurt;
using Item = Mdk.Game.Kurt.Inventory.Item;

namespace Mdk.Game.Tests;

/// <summary>Kurt's pickups (inventory.gd, godot-mdk docs/gameplay.md "Pickups").</summary>
public class InventoryTests
{
    private const string Grenade = "SW_HBOMB";
    private const string SuperChainGun = "SW_GATT";
    private static readonly string[] FiveItems = ["SW_DUMMY", "SW_INTER", "SW_TWIST", "SW_THUMP", "SW_KEY"];

    private static string Collect(Inventory inventory, string pickup)
    {
        var health = Inventory.MaxHealth;
        return inventory.Collect(pickup, ref health);
    }

    [Theory]
    [InlineData(Difficulty.Easy, 10)]
    [InlineData(Difficulty.Normal, 6)]
    [InlineData(Difficulty.Hard, 2)]
    public void GrenadesStackInOneSlot(Difficulty difficulty, int count)
    {
        var inventory = new Inventory { Difficulty = difficulty };
        Collect(inventory, Grenade);
        Collect(inventory, Grenade);

        var slot = Assert.Single(inventory.Slots);
        Assert.Equal(new Inventory.Slot(Item.Grenade, count), slot);
    }

    [Fact]
    public void SixthItemIsLeft()
    {
        var inventory = new Inventory();
        foreach (var pickup in FiveItems)
        {
            Assert.NotEqual("", Collect(inventory, pickup));
        }

        Assert.Equal("", Collect(inventory, "SW_SEAL"));
        Assert.Equal(Inventory.MaxSlots, inventory.Slots.Count);
    }

    [Fact]
    public void NewItemIsSelectedButNotTheSuperChainGun()
    {
        var inventory = new Inventory();
        Collect(inventory, "SW_DUMMY");
        Collect(inventory, "SW_INTER");
        Assert.Equal(Item.InterestingBomb, inventory.SelectedItem);

        Collect(inventory, SuperChainGun);
        Assert.Equal(Item.InterestingBomb, inventory.SelectedItem);
        Assert.Equal(200, inventory.SuperChainGun);
    }

    [Fact]
    public void SelectionWrapsAndUsingTheLastOneRemovesTheSlot()
    {
        var inventory = new Inventory();
        Collect(inventory, "SW_DUMMY");
        Collect(inventory, "SW_TWIST");
        inventory.SelectNext(1);
        Assert.Equal(Item.Dummy, inventory.SelectedItem);
        inventory.SelectNext(-1);
        Assert.Equal(Item.Tornado, inventory.SelectedItem);

        inventory.Consume();
        Assert.Equal(Item.Dummy, Assert.Single(inventory.Slots).Item);
        Assert.Equal(0, inventory.Selected);
    }

    [Fact]
    public void SuperChainGunSlotGoesWhenItsTimeIsUp()
    {
        var inventory = new Inventory { Difficulty = Difficulty.Hard };
        Collect(inventory, SuperChainGun);
        inventory.TickSuperChainGun(99);
        Assert.Single(inventory.Slots);

        inventory.TickSuperChainGun(1);
        Assert.Empty(inventory.Slots);
        Assert.Equal(0, inventory.SuperChainGun);
    }

    [Theory]
    [InlineData(Difficulty.Normal, 8)]
    [InlineData(Difficulty.Hard, 4)]
    public void AmmoIsHalvedOnHard(Difficulty difficulty, int amount)
    {
        var inventory = new Inventory { Difficulty = difficulty };
        Assert.Equal("COLLECT", Collect(inventory, "SW_HOME"));
        Assert.Equal(amount, inventory.Ammo[0]);
        Assert.Equal(1, inventory.SelectedAmmo);
    }

    [Theory]
    [InlineData("SW_H25", 95, 100)]
    [InlineData("SW_H25", 150, 150)]
    [InlineData("SW_H50", 20, 70)]
    [InlineData("SW_H100", 20, 100)]
    [InlineData("SW_H150", 120, 150)]
    public void HealthPickupsHeal(string pickup, int before, int after)
    {
        var health = before;
        Assert.Equal("APPLE", new Inventory().Collect(pickup, ref health));
        Assert.Equal(after, health);
    }
}
