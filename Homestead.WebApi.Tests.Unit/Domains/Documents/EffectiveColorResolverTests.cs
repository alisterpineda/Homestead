using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Tests.Unit.Domains.Documents;

public class EffectiveColorResolverTests
{
    [Fact]
    public void SingleTag_NoDescendants_SetsOwnColor()
    {
        var tag = MakeTag(color: "#ff0000");

        EffectiveColorResolver.Recompute(tag, parentEffectiveColor: null, []);

        Assert.Equal("#ff0000", tag.EffectiveColor);
    }

    [Fact]
    public void SingleTag_NoColor_InheritsParentEffectiveColor()
    {
        var tag = MakeTag(color: null);

        EffectiveColorResolver.Recompute(tag, parentEffectiveColor: "#0000ff", []);

        Assert.Equal("#0000ff", tag.EffectiveColor);
    }

    [Fact]
    public void SingleTag_NoColor_NoParent_EffectiveColorIsNull()
    {
        var tag = MakeTag(color: null);

        EffectiveColorResolver.Recompute(tag, parentEffectiveColor: null, []);

        Assert.Null(tag.EffectiveColor);
    }

    [Fact]
    public void Descendants_InheritParentEffectiveColor()
    {
        var parent = MakeTag(color: "#ff0000", path: "a");
        var child = MakeTag(color: null, path: "a/b", parentId: parent.Id);

        EffectiveColorResolver.Recompute(parent, parentEffectiveColor: null, [child]);

        Assert.Equal("#ff0000", parent.EffectiveColor);
        Assert.Equal("#ff0000", child.EffectiveColor);
    }

    [Fact]
    public void Descendant_WithOwnColor_StopsCascade()
    {
        var parent = MakeTag(color: "#ff0000", path: "a");
        var child = MakeTag(color: "#00ff00", path: "a/b", parentId: parent.Id);
        var grandchild = MakeTag(color: null, path: "a/b/c", parentId: child.Id);

        EffectiveColorResolver.Recompute(parent, parentEffectiveColor: null, [child, grandchild]);

        Assert.Equal("#ff0000", parent.EffectiveColor);
        Assert.Equal("#00ff00", child.EffectiveColor);
        Assert.Equal("#00ff00", grandchild.EffectiveColor);
    }

    [Fact]
    public void DeepCascade_ThroughMultipleLevels()
    {
        var root = MakeTag(color: "#ff0000", path: "a");
        var b = MakeTag(color: null, path: "a/b", parentId: root.Id);
        var c = MakeTag(color: null, path: "a/b/c", parentId: b.Id);
        var d = MakeTag(color: null, path: "a/b/c/d", parentId: c.Id);

        EffectiveColorResolver.Recompute(root, parentEffectiveColor: null, [b, c, d]);

        Assert.Equal("#ff0000", root.EffectiveColor);
        Assert.Equal("#ff0000", b.EffectiveColor);
        Assert.Equal("#ff0000", c.EffectiveColor);
        Assert.Equal("#ff0000", d.EffectiveColor);
    }

    [Fact]
    public void Mixed_SomeDescendantsHaveColors_SomeDont()
    {
        var root = MakeTag(color: "#ff0000", path: "a");
        var b = MakeTag(color: null, path: "a/b", parentId: root.Id);
        var c = MakeTag(color: "#00ff00", path: "a/c", parentId: root.Id);
        var d = MakeTag(color: null, path: "a/b/d", parentId: b.Id);
        var e = MakeTag(color: null, path: "a/c/e", parentId: c.Id);

        EffectiveColorResolver.Recompute(root, parentEffectiveColor: null, [b, c, d, e]);

        Assert.Equal("#ff0000", root.EffectiveColor);
        Assert.Equal("#ff0000", b.EffectiveColor);
        Assert.Equal("#00ff00", c.EffectiveColor);
        Assert.Equal("#ff0000", d.EffectiveColor);
        Assert.Equal("#00ff00", e.EffectiveColor);
    }

    private static Tag MakeTag(string? color = null, string path = "test", Guid? parentId = null)
    {
        return new Tag
        {
            Id = Guid.NewGuid(),
            Name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path,
            Path = path,
            Color = color,
            ParentId = parentId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
