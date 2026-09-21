using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Persistence;

/// <summary>
/// Guards the "keys are minted in exactly one place" convention: <see cref="UuidV7ValueGenerator"/> is
/// registered globally in <see cref="AppDbContext.OnModelCreating"/>, and entities leave <c>Id</c> unset.
/// The generator only fires when the key is still <c>default</c>, so a stray <c>Id = Guid.NewGuid()</c>
/// would silently bypass it — the source scan below is what turns that into a failing test.
/// </summary>
public class UuidV7PrimaryKeyTests
{
    [Fact]
    public void EveryGeneratedGuidPrimaryKey_UsesUuidV7Generator()
    {
        using var db = TestAppDbContext.Create();

        var offenders = db.Model.GetEntityTypes()
            .Where(t => t.BaseType is null)
            .Select(t => t.FindPrimaryKey())
            .Where(k => k is { Properties.Count: 1 })
            .Select(k => k!.Properties[0])
            .Where(p => p.ClrType == typeof(Guid) && !p.IsForeignKey())
            .Where(p => p.GetValueGeneratorFactory()?.Invoke(p, p.DeclaringType) is not UuidV7ValueGenerator)
            .Select(p => $"{p.DeclaringType.DisplayName()}.{p.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Add_AssignsVersion7Key_BeforeSaveChanges()
    {
        using var db = TestAppDbContext.Create();

        var document = new Document { Name = "unsaved", CreatedAt = DateTimeOffset.UtcNow };
        db.Documents.Add(document);

        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal(7, document.Id.Version);
    }

    [Fact]
    public void Add_KeepsAnExplicitlySetKey()
    {
        using var db = TestAppDbContext.Create();

        var id = Guid.NewGuid();
        var document = new Document { Id = id, Name = "explicit", CreatedAt = DateTimeOffset.UtcNow };
        db.Documents.Add(document);

        Assert.Equal(id, document.Id);
    }

    /// <summary>
    /// Non-key uses of <c>Guid.NewGuid()</c> (temp file names, staging upload names) are fine and listed here.
    /// A raw-SQL insert that must mint its own key uses <c>Guid.CreateVersion7()</c> instead, which this
    /// scan does not flag. Anything else is a primary key dodging the generator.
    /// </summary>
    private static readonly HashSet<string> AllowedNewGuidSites =
    [
        "Infrastructure/Backup/LocalDirectoryBackupTarget.cs",
        "Infrastructure/Ai/AiPageRenderService.cs",
        "Modules/Documents/Features/Documents/CreateDocument/CreateDocumentHandler.cs",
        "Modules/Documents/Features/Versions/CreateDocumentVersion/CreateDocumentVersionHandler.cs",
    ];

    [Fact]
    public void WebApiSource_DoesNotMintKeysWithNewGuid()
    {
        var root = FindWebApiRoot();

        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel => !AllowedNewGuidSites.Contains(rel))
            .Where(rel => File.ReadAllText(Path.Combine(root, rel)).Contains("Guid.NewGuid()"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindWebApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Homestead.WebApi", "Homestead.WebApi.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Homestead.WebApi from the test output directory.");
    }
}
