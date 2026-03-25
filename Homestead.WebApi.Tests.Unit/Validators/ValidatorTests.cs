using Homestead.WebApi.Features.Admin.Groups.CreateGroup;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.CreateTextExtractionEngine;
using Homestead.WebApi.Features.Admin.Groups.UpdateGroup;
using Homestead.WebApi.Features.Documents.Comments.CreateComment;
using Homestead.WebApi.Features.Documents.Parties.CreateParty;
using Homestead.WebApi.Features.Documents.Parties.UpdateParty;
using Homestead.WebApi.Domain.Settings;

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
