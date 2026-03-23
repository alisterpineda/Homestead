using Homestead.WebApi.Features.Admin.v1.CreateGroup;
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;
using Homestead.WebApi.Features.Admin.v1.UpdateGroup;
using Homestead.WebApi.Features.Documents.v1.CreateComment;
using Homestead.WebApi.Features.Documents.v1.CreateParty;
using Homestead.WebApi.Features.Documents.v1.UpdateParty;
using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Tests.Unit.Validators;

public class CreateCommentRequestValidatorTests
{
    private readonly CreateCommentRequestValidator _sut = new();

    [Fact]
    public async Task EmptyBody_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateCommentRequest(""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task ValidBody_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateCommentRequest("A comment"));
        Assert.True(result.IsValid);
    }
}

public class CreatePartyRequestValidatorTests
{
    private readonly CreatePartyRequestValidator _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var result = await _sut.ValidateAsync(new CreatePartyRequest { Name = name! });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreatePartyRequest { Name = "Acme Corp" });
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

public class CreateGroupRequestValidatorTests
{
    private readonly CreateGroupRequestValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupRequest { Name = "  " });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateGroupRequest { Name = "Engineering" });
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

public class CreateTextExtractionEngineRequestValidatorTests
{
    private readonly CreateTextExtractionEngineRequestValidator _sut = new();

    [Fact]
    public async Task WhitespaceName_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateTextExtractionEngineRequest
        {
            Name = "   ",
            EngineType = TextExtractionEngineType.Tesseract
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateTextExtractionEngineRequest
        {
            Name = "My Engine",
            EngineType = TextExtractionEngineType.Tesseract
        });
        Assert.True(result.IsValid);
    }
}
