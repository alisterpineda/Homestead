using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Admin.Groups.AddGroupMember;
using Homestead.WebApi.Features.Admin.Groups.CreateGroup;
using Homestead.WebApi.Features.Admin.Groups.UpdateGroup;
using Homestead.WebApi.Features.Admin.Groups.UpdateGroupMember;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.CreateTextExtractionEngine;
using Homestead.WebApi.Features.Documents.Comments.CreateComment;
using Homestead.WebApi.Features.Documents.Parties.CreateParty;
using Homestead.WebApi.Features.Documents.Parties.UpdateParty;

namespace Homestead.WebApi.Tests.Unit.Validators;

public class CreateCommentCommandValidatorTests
{
    private readonly CreateCommentCommandValidator _sut = new();

    [Fact]
    public async Task EmptyBody_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateCommentCommand(""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task ValidBody_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateCommentCommand("A comment"));
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
        var result = await _sut.ValidateAsync(new CreatePartyCommand { Name = name! });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreatePartyCommand { Name = "Acme Corp" });
        Assert.True(result.IsValid);
    }
}

public class UpdatePartyCommandValidatorTests
{
    private readonly UpdatePartyCommandValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdatePartyCommand { Id = Guid.NewGuid(), Name = "   " });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new UpdatePartyCommand { Id = Guid.NewGuid(), Name = "Acme" });
        Assert.True(result.IsValid);
    }
}

public class CreateGroupCommandValidatorTests
{
    private readonly CreateGroupCommandValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand { Name = "  " });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand { Name = "Engineering" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NameExceeds256Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupCommand { Name = new string('a', 257) });
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
        });
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
        });
        Assert.True(result.IsValid);
    }
}

public class UpdateGroupCommandValidatorTests
{
    private readonly UpdateGroupCommandValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateGroupRequest { Name = " " }
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateGroupRequest { Name = "Engineering" }
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task NameExceeds256Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateGroupRequest { Name = new string('a', 257) }
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.Name");
    }

    [Fact]
    public async Task DescriptionExceeds1024Chars_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateGroupRequest
            {
                Name = "Valid",
                Description = new string('a', 1025)
            }
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.Description");
    }
}

public class AddGroupMemberCommandValidatorTests
{
    private readonly AddGroupMemberCommandValidator _sut = new();

    [Fact]
    public async Task EmptyUserId_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new AddGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            Request = new AddGroupMemberRequest
            {
                UserId = Guid.Empty,
                AccessLevel = GroupAccessLevel.Editor
            }
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.UserId");
    }

    [Fact]
    public async Task InvalidAccessLevel_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new AddGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            Request = new AddGroupMemberRequest
            {
                UserId = Guid.NewGuid(),
                AccessLevel = (GroupAccessLevel)99
            }
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.AccessLevel");
    }

    [Fact]
    public async Task ValidCommand_IsValid()
    {
        var result = await _sut.ValidateAsync(new AddGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            Request = new AddGroupMemberRequest
            {
                UserId = Guid.NewGuid(),
                AccessLevel = GroupAccessLevel.Editor
            }
        });
        Assert.True(result.IsValid);
    }
}

public class UpdateGroupMemberCommandValidatorTests
{
    private readonly UpdateGroupMemberCommandValidator _sut = new();

    [Fact]
    public async Task InvalidAccessLevel_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Request = new UpdateGroupMemberRequest { AccessLevel = (GroupAccessLevel)99 }
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.AccessLevel");
    }

    [Fact]
    public async Task ValidAccessLevel_IsValid()
    {
        var result = await _sut.ValidateAsync(new UpdateGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Viewer }
        });
        Assert.True(result.IsValid);
    }
}

public class CreateTextExtractionEngineCommandValidatorTests
{
    private readonly CreateTextExtractionEngineCommandValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateTextExtractionEngineCommand
        {
            Name = "   ",
            EngineType = TextExtractionEngineType.Tesseract
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateTextExtractionEngineCommand
        {
            Name = "My Engine",
            EngineType = TextExtractionEngineType.Tesseract
        });
        Assert.True(result.IsValid);
    }
}
