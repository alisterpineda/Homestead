using System.Text;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Documents;

// Direct unit tests for the head+tail document-text sampler (Tier 3). SampleText is internal-static and
// pure, so it is exercised here without the IChatClient seam — the boundary/surrogate logic is the part
// most worth pinning. The elision marker is matched by its distinctive inner substring (the const itself
// is private to the service).
public class AiClassificationSamplerTests
{
    private const string MarkerCore = "[...document text omitted...]";

    [Fact]
    public void NullText_ReturnsEmpty_NoMarker()
    {
        var result = AiClassificationService.SampleText(null, 1_000);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void EmptyText_ReturnsEmpty_NoMarker()
    {
        Assert.Equal(string.Empty, AiClassificationService.SampleText("", 1_000));
    }

    [Fact]
    public void ShorterThanBudget_ReturnedVerbatim_NoMarker()
    {
        const string text = "A short document body.";
        var result = AiClassificationService.SampleText(text, 1_000);

        Assert.Equal(text, result);
        Assert.DoesNotContain(MarkerCore, result);
    }

    [Fact]
    public void ExactlyAtBudget_ReturnedVerbatim_NoMarker()
    {
        var text = new string('a', 1_000);
        var result = AiClassificationService.SampleText(text, 1_000);

        Assert.Equal(text, result);          // <= budget, so untouched
        Assert.DoesNotContain(MarkerCore, result);
    }

    [Theory]
    [InlineData(1)]    // one char over
    [InlineData(33)]   // exactly the marker length over — still not worth fragmenting
    public void MarginallyOverBudget_SingleSlice_NoMarker(int over)
    {
        var text = new string('a', 1_000 + over);
        var result = AiClassificationService.SampleText(text, 1_000);

        Assert.DoesNotContain(MarkerCore, result);   // nothing meaningful elided -> no marker
        Assert.True(result.Length <= 1_000, $"expected <= budget, got {result.Length}");
    }

    [Fact]
    public void SubstantiallyOverBudget_KeepsHeadAndTail_DropsMiddle_MarkerOnce()
    {
        // Distinct, whitespace-free sentinels at the very start, the exact middle, and the very end.
        var text = "HEAD_SENTINEL" + new string('a', 12_000) + "MIDDLE_SENTINEL" + new string('b', 12_000) + "TAIL_SENTINEL";

        var result = AiClassificationService.SampleText(text, 12_000);

        Assert.Contains("HEAD_SENTINEL", result);                       // head window preserved
        Assert.Contains("TAIL_SENTINEL", result);                       // tail window preserved — the whole point
        Assert.DoesNotContain("MIDDLE_SENTINEL", result);               // middle is the deliberate sacrifice
        Assert.Equal(1, CountOccurrences(result, MarkerCore));          // exactly one elision marker
        Assert.True(result.Length <= 12_000, $"expected <= budget, got {result.Length}");
    }

    [Fact]
    public void NoWhitespace_FallsBackToHardCut_StillProducesHeadTail()
    {
        // A single unbroken run (e.g. base64 / dense single-line OCR): no whitespace to snap to, so the
        // sampler hard-cuts at the budget offsets. Must still yield a head+tail with a marker.
        var text = new string('x', 40_000);
        var result = AiClassificationService.SampleText(text, 12_000);

        Assert.Equal(1, CountOccurrences(result, MarkerCore));
        Assert.True(result.Length <= 12_000);
    }

    [Fact]
    public void SnapsToWhitespace_HeadEndsOnWordBoundary()
    {
        // "word " repeats, so a clean whitespace boundary is always within a few chars of any cut. The head
        // slice must therefore end on a complete word, never a fragment like "wor".
        var text = string.Concat(Enumerable.Repeat("word ", 6_000));   // 30_000 chars
        var result = AiClassificationService.SampleText(text, 12_000);

        var headPart = result[..result.IndexOf(MarkerCore, StringComparison.Ordinal)].TrimEnd();
        Assert.EndsWith("word", headPart);
        Assert.DoesNotContain("wor\n", headPart);   // not cut mid-word
    }

    [Fact]
    public void SurrogatePairStraddlingCuts_AreNeverSplit()
    {
        // Body of astral-plane emoji (each "😀" is a UTF-16 surrogate pair, no whitespace). The leading "X"
        // offsets every pair onto an odd index, so the head cut and tail start land *inside* pairs — without
        // the surrogate guards a raw substring would orphan a half and UTF-8 would replace it with U+FFFD.
        var text = "X" + string.Concat(Enumerable.Repeat("😀", 6_000));   // 1 + 12_000 code units

        var result = AiClassificationService.SampleText(text, 1_000);

        Assert.Equal(1, CountOccurrences(result, MarkerCore));
        AssertWellFormed(result);
    }

    [Fact]
    public void SurrogateAtBudgetCut_OnSingleSlicePath_IsNeverSplit()
    {
        // Marginally-over path also slices the text — verify it too keeps surrogate pairs whole.
        var text = "X" + string.Concat(Enumerable.Repeat("😀", 510));   // 1 + 1_020 code units, 20 over budget
        var result = AiClassificationService.SampleText(text, 1_000);

        Assert.DoesNotContain(MarkerCore, result);   // marginal overage -> single slice
        AssertWellFormed(result);
    }

    [Fact]
    public void LoneHighSurrogateAtSnapBoundary_IsDropped_NotOrphanedIntoOutput()
    {
        // Malformed source: a lone high surrogate (no low half) sitting immediately before a whitespace snap
        // point. A naive guard that only checks for a *splittable pair* would leave this orphan as the last head
        // char; the head cut must drop it so the emitted text stays well-formed.
        var body = new string('a', 196) + '\uD800' + ' ' + new string('b', 6_000);
        var result = AiClassificationService.SampleText(body, 333);

        Assert.Equal(1, CountOccurrences(result, MarkerCore));
        AssertWellFormed(result);
    }

    // A substring that splits a surrogate pair leaves a lone surrogate, which UTF-8 encoding replaces with
    // U+FFFD — so a clean encode/decode round-trip proves every code point survived whole.
    private static void AssertWellFormed(string s)
    {
        var roundTrip = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(s));
        Assert.Equal(s, roundTrip);
        Assert.DoesNotContain('�', s);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
