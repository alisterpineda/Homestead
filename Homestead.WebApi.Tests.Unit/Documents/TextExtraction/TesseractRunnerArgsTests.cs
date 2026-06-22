using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class TesseractRunnerArgsTests
{
    // Returns the element immediately following the given flag, asserting the flag is present.
    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = IndexOf(args, flag);
        Assert.True(index >= 0, $"Expected flag '{flag}' to be present in: {string.Join(' ', args)}");
        Assert.True(index + 1 < args.Count, $"Expected a value after '{flag}'");
        return args[index + 1];
    }

    private static int IndexOf(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count; i++)
            if (args[i] == flag)
                return i;
        return -1;
    }

    [Fact]
    public void BuildArguments_DefaultsWithoutDpi_OmitsDpiAndSetsPsmOem()
    {
        var args = TesseractRunner.BuildArguments("eng", "tessdata", dpi: null, psm: 3, oem: 3);

        Assert.Equal("eng", ValueAfter(args, "-l"));
        Assert.Equal("tessdata", ValueAfter(args, "--tessdata-dir"));
        Assert.Equal("3", ValueAfter(args, "--psm"));
        Assert.Equal("3", ValueAfter(args, "--oem"));
        Assert.DoesNotContain("--dpi", args);
    }

    [Fact]
    public void BuildArguments_WithDpi_AppendsDpiFlag()
    {
        var args = TesseractRunner.BuildArguments("eng", "tessdata", dpi: 300, psm: 3, oem: 3);

        Assert.Equal("300", ValueAfter(args, "--dpi"));
    }

    [Fact]
    public void BuildArguments_CustomPsmOem_ReflectedInArguments()
    {
        var args = TesseractRunner.BuildArguments("eng", "tessdata", dpi: null, psm: 6, oem: 1);

        Assert.Equal("6", ValueAfter(args, "--psm"));
        Assert.Equal("1", ValueAfter(args, "--oem"));
    }

    [Fact]
    public void BuildArguments_MultiLanguage_PassedAfterLanguageFlag()
    {
        var args = TesseractRunner.BuildArguments("eng+fra", "tessdata", dpi: null, psm: 3, oem: 3);

        Assert.Equal("eng+fra", ValueAfter(args, "-l"));
    }
}
