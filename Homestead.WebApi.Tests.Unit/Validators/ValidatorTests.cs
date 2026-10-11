using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;
using Homestead.WebApi.Modules.Admin.Features.Groups.CreateGroup;
using Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;
using Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;
using Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;
using Homestead.WebApi.Modules.Documents.Features.Parties.CreateParty;
using Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;

namespace Homestead.WebApi.Tests.Unit.Validators;

public class CreateCommentCommandValidatorTests
{
    private readonly CreateCommentCommandValidator _sut = new();

    [Fact]
    public async Task EmptyBody_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateCommentCommand(""), TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task ValidBody_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateCommentCommand("A comment"), TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }
}

public class CreatePartyCommandValidatorTests
{
    private readonly CreatePartyCommandValidator _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var result = await _sut.ValidateAsync(new CreatePartyCommand { Name = name! }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreatePartyCommand { Name = "Acme Corp" }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }
}

public class UpdatePartyRequestValidatorTests
{
    private readonly UpdatePartyRequestValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdatePartyRequest { Name = "   " }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new UpdatePartyRequest { Name = "Acme" }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }
}

public class CreateGroupCommandValidatorTests
{
    private readonly CreateGroupCommandValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand { Name = "  " }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand { Name = "Engineering" }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NameExceeds256Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand { Name = new string('a', 257) }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task DescriptionExceeds1024Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand
        {
            Name = "Valid",
            Description = new string('a', 1025)
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Description");
    }

    [Fact]
    public async Task NullDescription_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand
        {
            Name = "Valid",
            Description = null
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }
}

public class UpdateGroupRequestValidatorTests
{
    private readonly UpdateGroupRequestValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupRequest { Name = " " }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupRequest { Name = "Engineering" }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NameExceeds256Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupRequest { Name = new string('a', 257) }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task DescriptionExceeds1024Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupRequest
        {
            Name = "Valid",
            Description = new string('a', 1025)
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Description");
    }
}

public class AddGroupMemberRequestValidatorTests
{
    private readonly AddGroupMemberRequestValidator _sut = new();

    [Fact]
    public async Task EmptyUserId_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new AddGroupMemberRequest
        {
            UserId = Guid.Empty,
            AccessLevel = GroupAccessLevel.Editor
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserId");
    }

    [Fact]
    public async Task InvalidAccessLevel_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new AddGroupMemberRequest
        {
            UserId = Guid.NewGuid(),
            AccessLevel = (GroupAccessLevel)99
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AccessLevel");
    }

    [Fact]
    public async Task ValidRequest_IsValid()
    {
        var result = await _sut.ValidateAsync(new AddGroupMemberRequest
        {
            UserId = Guid.NewGuid(),
            AccessLevel = GroupAccessLevel.Editor
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }
}

public class UpdateGroupMemberRequestValidatorTests
{
    private readonly UpdateGroupMemberRequestValidator _sut = new();

    [Fact]
    public async Task InvalidAccessLevel_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new UpdateGroupMemberRequest { AccessLevel = (GroupAccessLevel)99 }, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AccessLevel");
    }

    [Fact]
    public async Task ValidAccessLevel_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Viewer }, TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }
}
