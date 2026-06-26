namespace Homestead.WebApi.Infrastructure.Ai;

// Renders a document's first page to a PNG for AI classification. Distinct from the display-thumbnail
// path: PNG (lossless, model-readable) at a larger, configurable long edge rather than a small WebP.
public interface IDocumentPageRenderer
{
    // True when the content type can be rasterised to a page image (same set the thumbnail path supports).
    bool CanRender(string contentType);

    // Rasterises page 0, resizes its long edge to maxEdge, and returns PNG bytes. Returns null for an
    // unsupported content type or a decode/resize failure -> caller falls back to text-only classification.
    Task<byte[]?> RenderFirstPagePngAsync(string sourcePath, string contentType, int maxEdge, CancellationToken ct);
}
