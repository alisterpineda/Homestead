using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class AiImageRenderTests : IntegrationTestBase
{
    public AiImageRenderTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task UploadWithAutoClassify_RendersPage1Image_AndClassifierReceivesBytes()
    {
        var client = await CreateAdminClientAsync();

        // Auto-classify folds a classify run (scope All) into the upload job.
        var setResponse = await SendJsonPatchAsync(client, "/api/v1/documents/settings", new[]
        {
            new { op = "replace", path = "/autoClassifyOnUpload", value = (object)true }
        });
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        FakeAiClassificationService.ResetLastCall();
        try
        {
            await UploadAndWaitAsync(client, "image-classify.pdf");

            // The classifier received the rendered page-1 image for the full (All) upload scope.
            Assert.Equal(ClassificationScope.All, FakeAiClassificationService.LastScope);
            Assert.NotNull(FakeAiClassificationService.LastPageImage);
            Assert.True(FakeAiClassificationService.LastPageImage!.Length > 0);

            // A cached AI render PNG was produced under {CachePath}/ai-renders/{hash[0:4]}/{hash}.png.
            var aiRendersDir = Path.Combine(Factory.CachePath, "ai-renders");
            Assert.True(Directory.Exists(aiRendersDir), "ai-renders cache directory was not created");
            var renders = Directory.GetFiles(aiRendersDir, "*.png", SearchOption.AllDirectories);
            Assert.NotEmpty(renders);
        }
        finally
        {
            FakeAiClassificationService.ResetLastCall();
        }
    }
}
