using System.Text.Json;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Processing;

// In-flight jobs persisted before the Scope field existed have payload JSON without a "Scope" member.
// These must deserialize to ClassificationScope.All (full classify), not None, or already-queued jobs
// would silently become no-ops. This proves System.Text.Json applies the positional-record default
// value for the absent member. GetPayload/SetPayload use default JsonSerializer options (PascalCase).
public class ClassifyPayloadBackCompatTests
{
    [Fact]
    public void ClassifyDocumentPayload_MissingScope_DefaultsToAll()
    {
        var json = $$"""{"DocumentId":"{{Guid.NewGuid()}}","AiProviderId":null}""";

        var payload = JsonSerializer.Deserialize<ClassifyDocumentPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal(ClassificationScope.All, payload!.Scope);
    }

    [Fact]
    public void BulkClassifyPayload_MissingScope_DefaultsToAll()
    {
        var json = $$"""{"DocumentIds":["{{Guid.NewGuid()}}"],"AiProviderId":null}""";

        var payload = JsonSerializer.Deserialize<BulkClassifyPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal(ClassificationScope.All, payload!.Scope);
    }

    [Fact]
    public void ClassifyDocumentPayload_ExplicitScope_RoundTrips()
    {
        var original = new ClassifyDocumentPayload(Guid.NewGuid(), null, ClassificationScope.Summary);

        var roundTripped = JsonSerializer.Deserialize<ClassifyDocumentPayload>(
            JsonSerializer.Serialize(original));

        Assert.Equal(ClassificationScope.Summary, roundTripped!.Scope);
    }
}
