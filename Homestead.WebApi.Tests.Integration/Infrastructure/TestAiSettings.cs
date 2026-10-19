using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// The <see cref="AiInputSettings"/> the test host resolves: the production defaults, except that PDF input is off,
/// so uploads exercise the text + page-render path (<c>AiImageRenderTests</c>) unless a test turns it on.
/// <see cref="HomesteadApiFactory"/> registers <see cref="AiInputSettings"/> as a transient over <see cref="Current"/>,
/// so a test may swap the value for the duration of one call — static (and safe) for the same reason as
/// <see cref="FakeAiClassificationService"/>'s gate: the suite runs serially. Always restore with
/// <see cref="Reset"/> in a finally.
/// </summary>
public static class TestAiSettings
{
    public static readonly AiInputSettings Default = AiInputSettings.Default with { PdfInput = false };

    public static AiInputSettings Current { get; set; } = Default;

    public static void Reset() => Current = Default;
}
