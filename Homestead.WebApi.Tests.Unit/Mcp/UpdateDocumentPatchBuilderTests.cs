using Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;
using Homestead.WebApi.Modules.Mcp;
using ModelContextProtocol;

namespace Homestead.WebApi.Tests.Unit.Mcp;

// update_document's one-patch composition: only fields whose value actually changes produce a replace op (any op on a
// field auto-declines its pending suggestions), sets are replaced whole, and an empty result is "Nothing to update."
public class UpdateDocumentPatchBuilderTests
{
    private static readonly ResolvedParty Hydro = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Toronto Hydro");
    private static readonly ResolvedParty Bank = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Big Bank");
    private static readonly ResolvedParty Insurer = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Acme Insurance");

    private static readonly DocumentEditState Current = new(
        Name: "Hydro bill — March 2024",
        DocumentDate: new DateOnly(2024, 3, 1),
        ExpiryDate: new DateOnly(2025, 3, 1),
        Notes: "Paid online.",
        TagPaths: ["Home/Utilities", "Finance/Bills"],
        Parties: [Hydro, Bank]);

    private static UpdateDocumentPatch Build(UpdateDocumentInput input, DocumentEditState? current = null) =>
        UpdateDocumentPatchBuilder.Build(current ?? Current, input);

    private static (string Op, string Path, object? Value)[] Ops(UpdateDocumentPatch patch) =>
        patch.Patch.Operations.Select(o => (o.op, o.path, o.value)).ToArray();

    // --- one field at a time: exactly one replace op, its value, its changed field ---

    public static TheoryData<string, UpdateDocumentInput, string, object?, string> SingleScalarChanges => new()
    {
        { "rename", new UpdateDocumentInput { Name = "Hydro bill — April 2024" }, "/name", "Hydro bill — April 2024", "name" },
        { "rename trims", new UpdateDocumentInput { Name = "  Hydro bill — April 2024 " }, "/name", "Hydro bill — April 2024", "name" },
        { "set document date", new UpdateDocumentInput { DocumentDate = "2024-04-02" }, "/documentDate", new DateOnly(2024, 4, 2), "document_date" },
        { "clear document date", new UpdateDocumentInput { DocumentDate = "" }, "/documentDate", null, "document_date" },
        { "set expiry date", new UpdateDocumentInput { ExpiryDate = "2026-01-31" }, "/expiryDate", new DateOnly(2026, 1, 31), "expiry_date" },
        { "clear expiry date", new UpdateDocumentInput { ExpiryDate = "" }, "/expiryDate", null, "expiry_date" },
        { "replace notes", new UpdateDocumentInput { Notes = "Disputed." }, "/notes", "Disputed.", "notes" },
        { "clear notes", new UpdateDocumentInput { Notes = "" }, "/notes", "", "notes" },
        { "append note", new UpdateDocumentInput { AppendNote = "Late fee waived." }, "/notes", "Paid online.\n\nLate fee waived.", "notes" },
    };

    [Theory]
    [MemberData(nameof(SingleScalarChanges))]
    public void OneChangedScalar_EmitsExactlyOneReplace(
        string _, UpdateDocumentInput input, string path, object? value, string changedField)
    {
        var patch = Build(input);

        Assert.Equal([("replace", path, value)], Ops(patch));
        Assert.Equal([changedField], patch.ChangedFields);
    }

    // --- unchanged values emit nothing ---

    public static TheoryData<string, UpdateDocumentInput> NoOpInputs => new()
    {
        { "nothing given", new UpdateDocumentInput() },
        { "same name", new UpdateDocumentInput { Name = "Hydro bill — March 2024" } },
        { "same name with padding", new UpdateDocumentInput { Name = " Hydro bill — March 2024  " } },
        { "same document date", new UpdateDocumentInput { DocumentDate = "2024-03-01" } },
        { "same expiry date", new UpdateDocumentInput { ExpiryDate = "2025-03-01" } },
        { "same notes", new UpdateDocumentInput { Notes = "Paid online." } },
        { "blank append note", new UpdateDocumentInput { AppendNote = "   " } },
        { "add a tag already present", new UpdateDocumentInput { AddTagPaths = ["Home/Utilities"] } },
        { "remove a tag not present", new UpdateDocumentInput { RemoveTagPaths = ["Home/Insurance"] } },
        { "add a party already linked", new UpdateDocumentInput { AddParties = [Hydro] } },
        { "remove a party not linked", new UpdateDocumentInput { RemovePartyNames = ["Acme Insurance"] } },
    };

    [Theory]
    [MemberData(nameof(NoOpInputs))]
    public void NothingChanges_IsNothingToUpdate(string _, UpdateDocumentInput input)
    {
        var e = Assert.Throws<McpException>(() => Build(input));
        Assert.Equal("Nothing to update.", e.Message);
    }

    [Fact]
    public void ClearingADateThatIsAlreadyEmpty_IsNothingToUpdate()
    {
        var undated = Current with { DocumentDate = null, ExpiryDate = null };

        var e = Assert.Throws<McpException>(() =>
            Build(new UpdateDocumentInput { DocumentDate = "", ExpiryDate = "" }, undated));

        Assert.Equal("Nothing to update.", e.Message);
    }

    [Fact]
    public void UnchangedFields_AreLeftOutOfAMixedPatch()
    {
        // The name and expiry date are given but equal: only the document date may be touched, so a pending name or
        // expiry suggestion stays pending.
        var patch = Build(new UpdateDocumentInput
        {
            Name = "Hydro bill — March 2024",
            DocumentDate = "2024-03-05",
            ExpiryDate = "2025-03-01",
        });

        Assert.Equal([("replace", "/documentDate", (object?)new DateOnly(2024, 3, 5))], Ops(patch));
        Assert.Equal(["document_date"], patch.ChangedFields);
    }

    // --- append_note composition ---

    [Theory]
    [InlineData("Paid online.", "Late fee waived.", "Paid online.\n\nLate fee waived.")]
    [InlineData("Paid online.\n", "Late fee waived.", "Paid online.\n\nLate fee waived.")]
    [InlineData("", "Late fee waived.", "Late fee waived.")]
    [InlineData("  \n", "Late fee waived.", "Late fee waived.")]
    [InlineData("Paid online.", "  Late fee waived. ", "Paid online.\n\nLate fee waived.")]
    public void AppendNote_JoinsWithABlankLine_AndNoLeadingSeparatorOnEmptyNotes(
        string currentNotes, string appendNote, string expected)
    {
        var patch = Build(new UpdateDocumentInput { AppendNote = appendNote }, Current with { Notes = currentNotes });

        Assert.Equal([("replace", "/notes", (object?)expected)], Ops(patch));
    }

    [Fact]
    public void NotesAndAppendNoteTogether_AreInvalid()
    {
        Assert.Throws<ArgumentException>(() =>
            Build(new UpdateDocumentInput { Notes = "Disputed.", AppendNote = "Late fee waived." }));
    }

    // --- dates are strict yyyy-MM-dd or "" ---

    [Theory]
    [InlineData("2024-13-01")]
    [InlineData("03/01/2024")]
    [InlineData("2024-3-1")]
    [InlineData("tomorrow")]
    public void ADateNotInYyyyMmDd_IsInvalidInput_NamingTheParameter(string value)
    {
        var e = Assert.Throws<ArgumentException>(() => Build(new UpdateDocumentInput { ExpiryDate = value }));
        Assert.Contains("expiry_date", e.Message);
    }

    // --- tag and party deltas replace the whole set, once ---

    [Fact]
    public void TagDelta_ReplacesTheWholeSet_RemovalCaseInsensitiveAndNormalised()
    {
        var patch = Build(new UpdateDocumentInput
        {
            AddTagPaths = ["Home/Utilities/Electricity"],
            RemoveTagPaths = [" /finance/BILLS/ "],
        });

        Assert.Equal(
            [("replace", "/tagPaths", (object?)new List<string> { "Home/Utilities", "Home/Utilities/Electricity" })],
            Ops(patch), OpComparer.Instance);
        Assert.Equal(["tags"], patch.ChangedFields);
    }

    [Fact]
    public void PartyDelta_ReplacesTheWholeSet_RemovalByNameCaseInsensitive()
    {
        var patch = Build(new UpdateDocumentInput
        {
            AddParties = [Insurer],
            RemovePartyNames = ["  big bank "],
        });

        Assert.Equal(
            [("replace", "/partyIds", (object?)new List<Guid> { Hydro.Id, Insurer.Id })],
            Ops(patch), OpComparer.Instance);
        Assert.Equal(["parties"], patch.ChangedFields);
    }

    [Fact]
    public void AddingAndRemovingTheSameTag_IsInvalid()
    {
        Assert.Throws<ArgumentException>(() => Build(new UpdateDocumentInput
        {
            AddTagPaths = ["Home/Insurance"],
            RemoveTagPaths = ["home/insurance"],
        }));
    }

    [Fact]
    public void AddingAndRemovingTheSameParty_IsInvalid()
    {
        Assert.Throws<ArgumentException>(() => Build(new UpdateDocumentInput
        {
            AddParties = [Insurer],
            RemovePartyNames = ["ACME insurance"],
        }));
    }

    [Fact]
    public void EverythingAtOnce_IsOnePatch_InModelOrder()
    {
        var patch = Build(new UpdateDocumentInput
        {
            Name = "Hydro bill — April 2024",
            DocumentDate = "2024-04-01",
            ExpiryDate = "",
            AppendNote = "Late fee waived.",
            AddTagPaths = ["Home/Utilities/Electricity"],
            RemoveTagPaths = ["Home/Utilities"],
            AddParties = [Insurer],
            RemovePartyNames = ["Toronto Hydro"],
        });

        Assert.Equal(
            [
                ("replace", "/name", "Hydro bill — April 2024"),
                ("replace", "/documentDate", new DateOnly(2024, 4, 1)),
                ("replace", "/expiryDate", null),
                ("replace", "/notes", "Paid online.\n\nLate fee waived."),
                ("replace", "/tagPaths", new List<string> { "Finance/Bills", "Home/Utilities/Electricity" }),
                ("replace", "/partyIds", new List<Guid> { Bank.Id, Insurer.Id }),
            ],
            Ops(patch), OpComparer.Instance);
        Assert.Equal(["name", "document_date", "expiry_date", "notes", "tags", "parties"], patch.ChangedFields);
    }

    [Fact]
    public void ThePatch_AppliesToTheHandlersModel()
    {
        // PatchDocumentHandler seeds a PatchDocumentModel from the document and applies the ops to it; the paths and
        // value types must resolve there.
        var patch = Build(new UpdateDocumentInput
        {
            Name = "Hydro bill — April 2024",
            DocumentDate = "2024-04-01",
            ExpiryDate = "",
            Notes = "Disputed.",
            AddTagPaths = ["Home/Utilities/Electricity"],
            RemovePartyNames = ["Big Bank"],
        });
        var model = new PatchDocumentModel
        {
            Name = Current.Name,
            DocumentDate = Current.DocumentDate,
            ExpiryDate = Current.ExpiryDate,
            Notes = Current.Notes,
            TagPaths = [.. Current.TagPaths],
            PartyIds = [.. Current.Parties.Select(p => p.Id)],
        };
        var errors = new List<string>();

        patch.Patch.ApplyTo(model, e => errors.Add(e.ErrorMessage));

        Assert.Empty(errors);
        Assert.Equal("Hydro bill — April 2024", model.Name);
        Assert.Equal(new DateOnly(2024, 4, 1), model.DocumentDate);
        Assert.Null(model.ExpiryDate);
        Assert.Equal("Disputed.", model.Notes);
        Assert.Equal(["Home/Utilities", "Finance/Bills", "Home/Utilities/Electricity"], model.TagPaths);
        Assert.Equal([Hydro.Id], model.PartyIds);
    }

    // Lists compare by content (a replace value is a fresh list).
    private sealed class OpComparer : IEqualityComparer<(string Op, string Path, object? Value)>
    {
        public static readonly OpComparer Instance = new();

        public bool Equals((string Op, string Path, object? Value) x, (string Op, string Path, object? Value) y) =>
            x.Op == y.Op && x.Path == y.Path && x.Value switch
            {
                IEnumerable<string> a when y.Value is IEnumerable<string> b => a.SequenceEqual(b),
                IEnumerable<Guid> a when y.Value is IEnumerable<Guid> b => a.SequenceEqual(b),
                _ => Equals(x.Value, y.Value),
            };

        public int GetHashCode((string Op, string Path, object? Value) obj) => HashCode.Combine(obj.Op, obj.Path);
    }
}
