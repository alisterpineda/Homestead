using Homestead.WebApi.Modules.Mcp;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Tests.Unit.Mcp;

// The MCP door is opt-in: no section → absent (endpoint unmapped, one Information line); a section that cannot work →
// unavailable with a reason (one Warning); otherwise ready. PublicUrl is the RFC 9728 resource and is required
// outside Development only, where it is inferred from the request.
public class McpConfigurationTests
{
    private static McpOptions Valid() => new()
    {
        Authority = "https://auth.example.com/application/o/homestead-mcp/",
        Audience = "homestead-mcp",
        PublicUrl = "https://homestead.example.com/mcp",
    };

    [Fact]
    public void NothingSet_IsAbsent()
    {
        Assert.Equal(FeatureAvailability.Absent, McpConfiguration.Validate(new McpOptions(), isDevelopment: false));
    }

    [Fact]
    public void WhitespaceOnly_IsAbsent()
    {
        var options = new McpOptions { Authority = " ", Audience = "", MetadataAddress = "  ", PublicUrl = "\t" };

        Assert.Equal(FeatureAvailability.Absent, McpConfiguration.Validate(options, isDevelopment: false));
    }

    [Fact]
    public void AdditionalScopesAlone_IsAbsent()
    {
        var options = new McpOptions { AdditionalScopes = ["groups"] };

        Assert.Equal(FeatureAvailability.Absent, McpConfiguration.Validate(options, isDevelopment: false));
    }

    [Fact]
    public void FullConfig_IsReady()
    {
        Assert.Equal(FeatureAvailability.Ready, McpConfiguration.Validate(Valid(), isDevelopment: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auth.example.com")]
    [InlineData("/realms/homestead")]
    public void AuthorityMissingOrNotAbsolute_IsUnavailable(string? authority)
    {
        var options = Valid();
        options.Authority = authority;

        var availability = McpConfiguration.Validate(options, isDevelopment: false);

        Assert.Equal(FeatureAvailability.Unavailable("Mcp:Authority must be an absolute URI."), availability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void AudienceBlank_IsUnavailable(string? audience)
    {
        var options = Valid();
        options.Audience = audience;

        var availability = McpConfiguration.Validate(options, isDevelopment: false);

        Assert.Equal(FeatureAvailability.Unavailable("Mcp:Audience is required."), availability);
    }

    [Fact]
    public void PublicUrlMissing_OutsideDevelopment_IsUnavailable()
    {
        var options = Valid();
        options.PublicUrl = null;

        var availability = McpConfiguration.Validate(options, isDevelopment: false);

        Assert.Equal(
            FeatureAvailability.Unavailable("Mcp:PublicUrl is required outside Development (the public URL of /mcp)."),
            availability);
    }

    [Fact]
    public void PublicUrlMissing_InDevelopment_IsReady()
    {
        var options = Valid();
        options.PublicUrl = null;

        Assert.Equal(FeatureAvailability.Ready, McpConfiguration.Validate(options, isDevelopment: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicUrlNotAbsolute_IsUnavailable_InAnyEnvironment(bool isDevelopment)
    {
        var options = Valid();
        options.PublicUrl = "homestead.example.com/mcp";

        var availability = McpConfiguration.Validate(options, isDevelopment);

        Assert.Equal(FeatureAvailability.Unavailable("Mcp:PublicUrl must be an absolute URI."), availability);
    }

    [Fact]
    public void MetadataAddressNotAbsolute_IsUnavailable()
    {
        var options = Valid();
        options.MetadataAddress = ".well-known/openid-configuration";

        var availability = McpConfiguration.Validate(options, isDevelopment: false);

        Assert.Equal(FeatureAvailability.Unavailable("Mcp:MetadataAddress must be an absolute URI."), availability);
    }

    [Fact]
    public void MetadataAddressAbsolute_IsReady()
    {
        var options = Valid();
        options.MetadataAddress = "https://auth.example.com/application/o/homestead-mcp/.well-known/openid-configuration";

        Assert.Equal(FeatureAvailability.Ready, McpConfiguration.Validate(options, isDevelopment: false));
    }

    [Fact]
    public void OnlyMetadataAddressSet_IsPresent_AndUnavailable()
    {
        var options = new McpOptions { MetadataAddress = "https://auth.example.com/.well-known/openid-configuration" };

        var availability = McpConfiguration.Validate(options, isDevelopment: true);

        Assert.True(availability.IsPresent);
        Assert.False(availability.IsConfigured);
    }

    [Fact]
    public void ResolveScopes_NoAdditional_IsTheBase()
    {
        Assert.Equal(["openid", "profile", "email"], McpConfiguration.ResolveScopes(new McpOptions()));
    }

    [Fact]
    public void ResolveScopes_AppendsAdditional_Distinct_WithoutOfflineAccess()
    {
        var options = new McpOptions { AdditionalScopes = ["groups", "email", "offline_access", "groups"] };

        Assert.Equal(["openid", "profile", "email", "groups"], McpConfiguration.ResolveScopes(options));
    }
}
