using Homestead.WebApi.Modules.Inventory.Features.Items.Notes.GetNoteSubjects;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class GetItemNoteSubjectsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetItemNoteSubjectsHandler _sut;

    public GetItemNoteSubjectsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetItemNoteSubjectsHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingItem_ReturnsNull()
    {
        Assert.Null(await _sut.Handle(new GetItemNoteSubjectsQuery(Guid.NewGuid()), Ct));
    }

    [Fact]
    public async Task Handle_NoNotes_ReturnsEmpty()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var result = await _sut.Handle(new GetItemNoteSubjectsQuery(item.Id), Ct);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_ReturnsDistinctSubjects_MostRecentlyUsedFirst_SkippingNulls()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var other = await ItemNoteTestData.AddItemAsync(_db, name: "Other");
        var now = DateTimeOffset.UtcNow;
        await ItemNoteTestData.AddNoteAsync(_db, item.Id, now.AddDays(-30), subject: "Oil change");
        await ItemNoteTestData.AddNoteAsync(_db, item.Id, now.AddDays(-20), subject: "Blade");
        // A second, more recent "Oil change" pulls that subject to the top without duplicating it.
        await ItemNoteTestData.AddNoteAsync(_db, item.Id, now.AddDays(-1), subject: "Oil change");
        await ItemNoteTestData.AddNoteAsync(_db, item.Id, now, body: "no subject here");
        await ItemNoteTestData.AddNoteAsync(_db, other.Id, now, subject: "Someone else's");

        var result = await _sut.Handle(new GetItemNoteSubjectsQuery(item.Id), Ct);

        Assert.Equal(["Oil change", "Blade"], result);
    }
}
