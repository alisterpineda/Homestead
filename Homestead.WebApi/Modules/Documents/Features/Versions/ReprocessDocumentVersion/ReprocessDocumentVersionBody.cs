namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

// EngineKey is the caller's explicit pick — a configured engine instance key ("azure-layout"). Null (or
// an omitted body entirely) means "use the household default", which is why the endpoint binds the body
// as nullable and reads body?.EngineKey.
public record ReprocessDocumentVersionBody(string? EngineKey = null);
