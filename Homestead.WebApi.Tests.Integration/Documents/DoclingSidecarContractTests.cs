using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Writer;

namespace Homestead.WebApi.Tests.Integration.Documents;

// The docling-serve contract, against a real sidecar. DoclingRunnerTests pins what Homestead SENDS; only a
// real server shows what it does with it. docling-serve ignores form fields it does not know, so if a future
// image drops or renames one, conversions keep succeeding and the stored text quietly changes shape — no
// other test would notice. Run this whenever the image tag in AppHost.cs and the README is bumped.
//
// Opt-in, because the CPU image is ~7.6 GB and loads its models for a minute or more before it answers:
// skipped unless HOMESTEAD_DOCLING_IMAGE names the image to test, e.g.
//
//   HOMESTEAD_DOCLING_IMAGE=ghcr.io/docling-project/docling-serve-cpu:v1.38.2 \
//     dotnet test --project Homestead.WebApi.Tests.Integration --filter-class "*DoclingSidecarContractTests"
//
// Not in the Postgres collection: it needs no database, only the engine as the app builds it from config.
public class DoclingSidecarContractTests(DoclingSidecarContractTests.Sidecar sidecar)
    : IClassFixture<DoclingSidecarContractTests.Sidecar>
{
    // Per conversion. The engine is called directly, not through TextExtractionHelper, so nothing else would
    // stop a task the image under test leaves running forever; the test's token is cancelled at this limit.
    // A few seconds per page on CPU, so far more than enough.
    private const int ConversionTimeoutMs = 180_000;

    private static CancellationToken CT => TestContext.Current.CancellationToken;

    // A delimiter row of single-dash cells: GFM needs no more than one dash per cell.
    private static readonly Regex CompactSeparator = new(@"^\|( - \|)+$");

    // Text followed by two or more spaces before the next pipe: a cell padded to its column's width. An
    // empty cell ("|  |") is not padding and is allowed.
    private static readonly Regex PaddedCell = new(@"[^ |] {2,}\|");

    [Fact(Timeout = ConversionTimeoutMs)]
    public async Task Convert_W9_StoresCompactTablesAndPageBreaks()
    {
        var engine = sidecar.EngineOrSkip();

        var output = await engine.ExtractTextAsync(Fixture("irs-form-w9.pdf"), "application/pdf", CT);

        var metadata = AssertSucceeded(output);
        Assert.Equal(6, metadata.PageCount);
        Assert.False(string.IsNullOrWhiteSpace(metadata.ServerVersion), "the sidecar reported no version");

        var text = output.Text!;
        Assert.Contains("Taxpayer Identification Number", text);

        // md_compact_tables: no cell padded to its column's width, and every delimiter row single dashes.
        // The W-9's instructions hold several tables, so their absence would itself be a regression.
        var tables = Tables(text);
        Assert.NotEmpty(tables);
        Assert.All(tables, table =>
        {
            Assert.True(table.Count >= 2, $"a table without a delimiter row: {table[0]}");
            Assert.Matches(CompactSeparator, table[1]);
            Assert.DoesNotContain(table, line => PaddedCell.IsMatch(line));
        });

        // md_page_break_placeholder: one marker between each pair of the six pages.
        Assert.Equal(5, Regex.Count(text, Regex.Escape("<!-- PageBreak -->")));
    }

    // image_export_mode=placeholder. The W-9 has no picture Docling detects, so it cannot show this; the
    // NIST cover page has one. Only that page is sent: the whole 43-page publication takes minutes on CPU.
    [Fact(Timeout = ConversionTimeoutMs)]
    public async Task Convert_PageWithAPicture_StoresAPlaceholderNotTheImage()
    {
        var engine = sidecar.EngineOrSkip();

        var coverPage = Path.Combine(Path.GetTempPath(), $"nist-cover-{Guid.NewGuid():N}.pdf");
        var publication = await File.ReadAllBytesAsync(Fixture("nist-sp-250-93-vacuum-calibration.pdf"), CT);
        await File.WriteAllBytesAsync(coverPage, PdfMerger.Merge([publication], [[1]]), CT);
        try
        {
            var output = await engine.ExtractTextAsync(coverPage, "application/pdf", CT);

            var metadata = AssertSucceeded(output);
            Assert.Equal(1, metadata.PageCount);
            Assert.Contains("<!-- image -->", output.Text);
            Assert.DoesNotContain("data:image", output.Text);
        }
        finally
        {
            File.Delete(coverPage);
        }
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Fixtures", name);

    private static DoclingTextExtractionMetadata AssertSucceeded(TextExtractionOutput output)
    {
        var metadata = Assert.IsType<DoclingTextExtractionMetadata>(output.Metadata);
        Assert.Equal(DoclingTextExtractionMetadata.SuccessStatus, metadata.ConversionStatus);
        Assert.False(string.IsNullOrWhiteSpace(output.Text));
        return metadata;
    }

    // Each run of consecutive pipe-table lines. Docling writes a header and delimiter row on every table,
    // so the delimiter is always the second line — told apart by position, not by content, since a data
    // row of dashes ("| - |", a common nil) looks the same.
    private static List<List<string>> Tables(string markdown)
    {
        var tables = new List<List<string>>();
        List<string>? current = null;
        foreach (var line in markdown.Split('\n'))
        {
            if (!line.StartsWith('|'))
            {
                current = null;
                continue;
            }

            if (current is null)
                tables.Add(current = []);
            current.Add(line);
        }

        return tables;
    }

    // One sidecar for the class: model loading dominates, so each test starting its own would double the
    // run. Starts nothing when the variable is unset, and the tests skip.
    public sealed class Sidecar : IAsyncLifetime
    {
        private const string ImageVariable = "HOMESTEAD_DOCLING_IMAGE";
        private const int Port = 5001;
        private const string ApiKey = "contract-test-key";

        private IContainer? _container;
        private ITextExtractionEngine? _engine;

        public ITextExtractionEngine EngineOrSkip()
        {
            Assert.SkipWhen(_engine is null,
                $"Set {ImageVariable} to a docling-serve image to run the sidecar contract tests.");
            return _engine!;
        }

        public async ValueTask InitializeAsync()
        {
            var image = Environment.GetEnvironmentVariable(ImageVariable);
            if (string.IsNullOrWhiteSpace(image))
                return;

            // Sized and locked as AppHost runs it: one worker (matching the engine's MaxConcurrency of 1),
            // four threads, an API key — so the X-Api-Key header is part of what is tested.
            _container = new ContainerBuilder(image)
                .WithPortBinding(Port, assignRandomHostPort: true)
                .WithEnvironment("DOCLING_SERVE_ENG_LOC_NUM_WORKERS", "1")
                .WithEnvironment("DOCLING_NUM_THREADS", "4")
                .WithEnvironment("DOCLING_SERVE_API_KEY", ApiKey)
                // docling-serve only answers once its models are loaded.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                    request => request.ForPort(Port).ForPath("/health"),
                    wait => wait.WithTimeout(TimeSpan.FromMinutes(5))))
                .Build();
            try
            {
                await _container.StartAsync(TestContext.Current.CancellationToken);
            }
            catch (Exception ex)
            {
                // The container is removed on disposal, and its logs with it: keep them, or a sidecar that
                // exits during startup leaves nothing to say why.
                var (stdout, stderr) = await _container.GetLogsAsync();
                throw new InvalidOperationException(
                    $"docling-serve ({image}) did not start.\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}", ex);
            }

            _engine = BuildEngine($"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}");
            Assert.True(_engine.IsConfigured, _engine.ConfigurationError);
        }

        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
                await _container.DisposeAsync();
        }

        // The engine as AddTextExtraction builds it from Ocr:Engines config, the shape AppHost forwards.
        private static ITextExtractionEngine BuildEngine(string endpoint)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ocr:Engines:docling:Type"] = "Docling",
                    ["Ocr:Engines:docling:Endpoint"] = endpoint,
                    ["Ocr:Engines:docling:ApiKey"] = ApiKey,
                    ["Ocr:Engines:docling:JobTimeout"] = "00:05:00"
                })
                .Build();

            var services = new ServiceCollection().AddLogging();
            services.AddTextExtraction(configuration);

            return services.BuildServiceProvider()
                .GetServices<ITextExtractionEngine>()
                .Single(engine => engine.Key == "docling");
        }
    }
}
