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
            // PDF input is off in the test host by default (TestAiSettings), so this is the text + render path.
            Assert.Null(FakeAiClassificationService.LastPdf);
        }
        finally
        {
            FakeAiClassificationService.ResetLastCall();
        }
    }

    [Fact]
    public async Task UploadWithAutoClassify_PdfInputOn_SendsThePdf_AndNoRender()
    {
        var client = await CreateAdminClientAsync();

        var setResponse = await SendJsonPatchAsync(client, "/api/v1/documents/settings", new[]
        {
            new { op = "replace", path = "/autoClassifyOnUpload", value = (object)true }
        });
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        FakeAiClassificationService.ResetLastCall();
        TestAiSettings.Current = TestAiSettings.Default with { PdfInput = true };
        try
        {
            await UploadAndWaitAsync(client, "pdf-classify.pdf");

            // A one-page, unencrypted PDF well under the caps passes the gate: the classifier gets the PDF's
            // bytes (a PDF file starts with "%PDF") in place of a page render.
            Assert.Equal(ClassificationScope.All, FakeAiClassificationService.LastScope);
            Assert.NotNull(FakeAiClassificationService.LastPdf);
            Assert.Equal("%PDF"u8.ToArray(), FakeAiClassificationService.LastPdf![..4]);
            Assert.Null(FakeAiClassificationService.LastPageImage);
        }
        finally
        {
            TestAiSettings.Reset();
            FakeAiClassificationService.ResetLastCall();
        }
    }
}
