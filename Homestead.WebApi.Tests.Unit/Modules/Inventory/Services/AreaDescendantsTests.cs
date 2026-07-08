using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Services;

public class AreaDescendantsTests
{
    [Fact]
    public void InclusiveSubtree_IncludesTheRootItself()
    {
        var root = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null };

        var subtree = AreaDescendants.InclusiveSubtree(root, parents);

        Assert.True(subtree.SetEquals([root]));
    }

    [Fact]
    public void InclusiveSubtree_IncludesTheRootEvenWhenAbsentFromTheMap()
    {
        var root = Guid.NewGuid();

        var subtree = AreaDescendants.InclusiveSubtree(root, new Dictionary<Guid, Guid?>());

        Assert.True(subtree.SetEquals([root]));
    }

    [Fact]
    public void InclusiveSubtree_RollsInDescendantsAcrossMultipleLevels()
    {
        // house ← basement ← bedroom ; house ← garage. Filtering "house" gathers the whole tree;
        // filtering "basement" gathers only basement + bedroom (not the sibling garage).
        var house = Guid.NewGuid();
        var basement = Guid.NewGuid();
        var bedroom = Guid.NewGuid();
        var garage = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?>
        {
            [house] = null,
            [basement] = house,
            [bedroom] = basement,
            [garage] = house,
        };

        Assert.True(AreaDescendants.InclusiveSubtree(house, parents).SetEquals([house, basement, bedroom, garage]));
        Assert.True(AreaDescendants.InclusiveSubtree(basement, parents).SetEquals([basement, bedroom]));
        Assert.True(AreaDescendants.InclusiveSubtree(bedroom, parents).SetEquals([bedroom]));
    }

    [Fact]
    public void InclusiveSubtree_IsCycleGuarded()
    {
        // A pathological cycle (a↔b) must not loop forever; both nodes are returned once.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [a] = b, [b] = a };

        var subtree = AreaDescendants.InclusiveSubtree(a, parents);

        Assert.True(subtree.SetEquals([a, b]));
    }
}
