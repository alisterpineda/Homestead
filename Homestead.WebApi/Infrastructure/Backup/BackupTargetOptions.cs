namespace Homestead.WebApi.Infrastructure.Backup;

// One options bag per configured target instance, discriminated by Kind — the config binder needs a
// single concrete type to bind each "Backup:Targets:{key}" section onto (same shape as AiProviderOptions
// and OcrEngineOptions). Per-kind required fields are validated in BackupConfiguration rather than split
// into subclasses.
public class BackupTargetOptions
{
    public const string SectionName = "Backup:Targets";

    // Nullable so "not configured" is distinguishable from Local, and — like the AI bag — required for
    // EVERY key: there is no built-in target whose kind its name could imply. A non-nullable enum would
    // bind an omitted Kind to default(0) = Local, so an operator who configures a future cloud target but
    // forgets Kind would get a target that quietly writes to a local directory under the name they chose.
    public BackupTargetKind? Kind { get; set; }

    // Local: the mirror root directory. Created on demand at first write.
    public string? Path { get; set; }
}
