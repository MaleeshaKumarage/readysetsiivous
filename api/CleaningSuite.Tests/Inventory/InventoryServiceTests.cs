using CleaningSuite.Application.Common;
using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Inventory;
using FluentValidation;

namespace CleaningSuite.Tests.Inventory;

public class InventoryServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly InventoryActor Actor = new(Guid.NewGuid(), "Maleesha Test");
    private static readonly byte[] Photo = [1, 2, 3];

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private sealed class FakeValidator : IUploadContentValidator
    {
        public string ValidateServiceImage(ReadOnlyMemory<byte> content) => ".png";
        public void ValidateAgreementPdf(ReadOnlyMemory<byte> content) { }
        public void ValidateGeneratedAgreementPdf(ReadOnlyMemory<byte> content) { }
        public void ValidateSignaturePng(ReadOnlyMemory<byte> content) { }
        public int ValidateQualityCyclePhotos(IReadOnlyList<CleaningSuite.Domain.QualityCycle.QualityCyclePhoto>? photos, IReadOnlyList<string>? photoUrls) => 0;
    }

    private sealed class FakeRepository : IInventoryRepository
    {
        public List<InventoryLocation> Locations { get; } = new();
        public List<InventoryItem> Items { get; } = new();
        public List<InventoryUsage> Usage { get; } = new();
        public int SaveCount { get; private set; }
        public InventoryItem? RaceWinner { get; set; }
        public Task<IReadOnlyList<InventoryLocation>> ListLocationsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InventoryLocation>>(Locations.ToList());
        public Task<IReadOnlyList<InventoryItem>> ListItemsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InventoryItem>>(Items.ToList());
        public Task<InventoryLocation?> GetLocationAsync(Guid id, CancellationToken ct) => Task.FromResult(Locations.SingleOrDefault(x => x.Id == id));
        public Task<InventoryItem?> GetItemAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<InventoryUsage?> GetUsageAsync(Guid id, CancellationToken ct) => Task.FromResult(Usage.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<InventoryUsage>> ListUsageAsync(InventoryUsageQuery q, CancellationToken ct)
        {
            IEnumerable<InventoryUsage> rows = Usage;
            if (q.EmployeeId is { } e) rows = rows.Where(x => x.EmployeeId == e);
            if (q.FromUtc is { } f) rows = rows.Where(x => x.TakenUtc >= f);
            if (q.ToUtc is { } t) rows = rows.Where(x => x.TakenUtc < t);
            return Task.FromResult<IReadOnlyList<InventoryUsage>>(rows.OrderByDescending(x => x.TakenUtc).Skip(q.Skip).Take(q.Take).ToList());
        }
        public Task<IReadOnlyList<InventoryUsage>> ListUsageWithPhotosTakenBeforeAsync(DateTime cutoffUtc, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InventoryUsage>>(Usage.Where(x => x.TakenUtc < cutoffUtc && x.PhotosPurgedUtc is null).OrderBy(x => x.TakenUtc).Take(take).ToList());
        public Task SaveAsync<T>(T document, CancellationToken ct) where T : BaseDocument
        {
            SaveCount++;
            if (document is InventoryItem && RaceWinner is { } winner)
            {
                // Simulate another request inserting the same name after our read, before our save.
                RaceWinner = null;
                Items.Add(winner);
                throw new InventoryDuplicateNameException("duplicate");
            }
            document.Version = Guid.NewGuid();
            switch (document)
            {
                case InventoryLocation l when !Locations.Contains(l): Locations.Add(l); break;
                case InventoryItem i when !Items.Contains(i): Items.Add(i); break;
                case InventoryUsage u when !Usage.Contains(u): Usage.Add(u); break;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFiles : IInventoryFileStore
    {
        public Dictionary<Guid, byte[]> Saved { get; } = new();
        public Func<InventoryPhoto, bool> FailWhen { get; set; } = _ => false;
        public Task SaveAsync(Guid usageId, InventoryPhoto photo, byte[] content, CancellationToken ct)
        {
            if (FailWhen(photo)) throw new IOException("disk full");
            Saved[photo.Id] = content;
            return Task.CompletedTask;
        }
        public Task<byte[]?> ReadAsync(Guid usageId, InventoryPhoto photo, CancellationToken ct) => Task.FromResult(Saved.GetValueOrDefault(photo.Id));
        public Task DeleteAsync(Guid usageId, InventoryPhoto photo)
        {
            Saved.Remove(photo.Id);
            return Task.CompletedTask;
        }
    }

    private static (InventoryService Service, FakeRepository Repo, FakeFiles Files) Build(DateTime? now = null)
    {
        var repo = new FakeRepository();
        var files = new FakeFiles();
        var service = new InventoryService(repo, files, new FakeValidator(), new FixedClock(now ?? Now));
        return (service, repo, files);
    }

    private static InventoryLocation Location(FakeRepository repo, string name = "Main store", bool active = true)
    {
        var location = new InventoryLocation { Name = name, IsActive = active, Version = Guid.NewGuid() };
        repo.Locations.Add(location);
        return location;
    }

    private static InventoryItem Item(FakeRepository repo, string name = "Keys", bool active = true, bool verified = true)
    {
        var item = new InventoryItem { Name = name, Unit = "pcs", IsActive = active, IsVerified = verified, Version = Guid.NewGuid() };
        repo.Items.Add(item);
        return item;
    }

    [Fact]
    public async Task Entry_requires_at_least_one_photo()
    {
        var (service, repo, _) = Build();
        var location = Location(repo);
        var item = Item(repo);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RecordUsageAsync(Actor, location.Id, item.Id, 2, null, [], default));
        Assert.Empty(repo.Usage);
    }

    [Fact]
    public async Task Entry_allows_at_most_five_photos()
    {
        var (service, repo, _) = Build();
        var location = Location(repo);
        var item = Item(repo);
        var photos = Enumerable.Range(0, InventoryService.MaxPhotosPerEntry + 1).Select(_ => Photo).ToList();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RecordUsageAsync(Actor, location.Id, item.Id, 1, null, photos, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public async Task Quantity_must_be_between_one_and_the_maximum(int quantity)
    {
        var (service, repo, _) = Build();
        var location = Location(repo);
        var item = Item(repo);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RecordUsageAsync(Actor, location.Id, item.Id, quantity, null, [Photo], default));
    }

    [Fact]
    public async Task Inactive_location_or_item_is_rejected()
    {
        var (service, repo, _) = Build();
        var activeLocation = Location(repo);
        var inactiveLocation = Location(repo, "Old store", active: false);
        var activeItem = Item(repo);
        var inactiveItem = Item(repo, "Retired", active: false);

        await Assert.ThrowsAsync<ValidationException>(() => service.RecordUsageAsync(Actor, inactiveLocation.Id, activeItem.Id, 1, null, [Photo], default));
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordUsageAsync(Actor, activeLocation.Id, inactiveItem.Id, 1, null, [Photo], default));
    }

    [Fact]
    public async Task Recorded_entry_snapshots_names_and_uses_server_time()
    {
        var (service, repo, files) = Build();
        var location = Location(repo, "Laundry");
        var item = Item(repo, "Bedsheets");

        var dto = await service.RecordUsageAsync(Actor, location.Id, item.Id, 3, "  for room 12 ", [Photo, Photo], default);

        var saved = Assert.Single(repo.Usage);
        Assert.Equal(Now, saved.TakenUtc);
        Assert.Equal("Maleesha Test", saved.EmployeeName);
        Assert.Equal("Laundry", saved.LocationName);
        Assert.Equal("Bedsheets", saved.ItemName);
        Assert.Equal("for room 12", saved.Note);
        Assert.Equal(2, dto.PhotoIds.Count);
        Assert.Equal(2, files.Saved.Count);
        Assert.All(saved.Photos, p => Assert.Equal(".png", p.Extension));
    }

    [Fact]
    public async Task Failed_photo_write_removes_already_stored_photos_and_saves_nothing()
    {
        var (service, repo, files) = Build();
        var location = Location(repo);
        var item = Item(repo);
        files.FailWhen = photo => files.Saved.Count == 1;

        await Assert.ThrowsAsync<IOException>(() =>
            service.RecordUsageAsync(Actor, location.Id, item.Id, 1, null, [Photo, Photo], default));

        Assert.Empty(repo.Usage);
        Assert.Empty(files.Saved);
    }

    [Fact]
    public async Task Employee_adding_an_existing_active_item_gets_that_item_back()
    {
        var (service, repo, _) = Build();
        var existing = Item(repo, "Tissues");

        var dto = await service.AddItemFromEmployeeAsync(Actor, "  tissues ", null, default);

        Assert.Equal(existing.Id, dto.Id);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Employee_added_item_is_unverified_until_an_admin_verifies_it()
    {
        var (service, repo, _) = Build();

        var created = await service.AddItemFromEmployeeAsync(Actor, "Mop heads", "pcs", default);
        Assert.False(created.IsVerified);

        var verified = await service.VerifyItemAsync(created.Id, created.Version, default);
        Assert.True(verified.IsVerified);
    }

    [Fact]
    public async Task Employee_add_that_loses_a_race_returns_the_item_that_won()
    {
        var (service, repo, _) = Build();
        repo.RaceWinner = new InventoryItem { Name = "Mop heads", Unit = "pcs", IsVerified = false, IsActive = true, Version = Guid.NewGuid() };

        var dto = await service.AddItemFromEmployeeAsync(Actor, "mop heads", null, default);

        Assert.Equal(repo.Items.Single().Id, dto.Id);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Open_ended_date_range_at_the_calendar_limit_does_not_overflow()
    {
        var (service, _, _) = Build();

        var rows = await service.ListUsageAsync(null, null, DateOnly.MaxValue, 0, 10, default);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Employee_adding_a_name_that_is_inactive_gets_a_conflict()
    {
        var (service, repo, _) = Build();
        Item(repo, "Old keys", active: false);

        await Assert.ThrowsAsync<InventoryConflictException>(() =>
            service.AddItemFromEmployeeAsync(Actor, "Old keys", null, default));
    }

    [Fact]
    public async Task Admin_cannot_create_a_duplicate_location_name()
    {
        var (service, repo, _) = Build();
        Location(repo, "Main store");

        await Assert.ThrowsAsync<InventoryConflictException>(() => service.CreateLocationAsync("main STORE", default));
    }

    [Fact]
    public async Task Stale_version_is_rejected_when_updating()
    {
        var (service, repo, _) = Build();
        var item = Item(repo, "Tissues");

        await Assert.ThrowsAsync<InventoryConflictException>(() =>
            service.UpdateItemAsync(item.Id, "Tissues", "box", true, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Employees_cannot_read_another_employees_photo()
    {
        var (service, repo, _) = Build();
        var location = Location(repo);
        var item = Item(repo);
        var dto = await service.RecordUsageAsync(Actor, location.Id, item.Id, 1, null, [Photo], default);

        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ReadPhotoAsync(dto.Id, dto.PhotoIds[0], other, default));

        var (bytes, contentType) = await service.ReadPhotoAsync(dto.Id, dto.PhotoIds[0], Actor.EmployeeId, default);
        Assert.Equal(Photo, bytes);
        Assert.Equal("image/png", contentType);
    }

    [Fact]
    public async Task Retention_deletes_photos_older_than_three_months_and_keeps_the_entry()
    {
        var old = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var recent = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var (service, repo, files) = Build();
        var location = Location(repo);
        var item = Item(repo);
        var oldDto = await service.RecordUsageAsync(Actor, location.Id, item.Id, 1, "old note", [Photo], default);
        var oldEntry = repo.Usage.Single();
        oldEntry.TakenUtc = old;
        var recentEntry = new InventoryUsage { EmployeeId = Actor.EmployeeId, EmployeeName = "x", ItemName = "Keys", LocationName = "Main store", TakenUtc = recent, Photos = [new InventoryPhoto(Guid.NewGuid(), ".png")] };
        repo.Usage.Add(recentEntry);

        var purged = await service.PurgeExpiredPhotosAsync(Now, 200, default);

        Assert.Equal(1, purged);
        Assert.Empty(oldEntry.Photos);
        Assert.NotNull(oldEntry.PhotosPurgedUtc);
        Assert.Equal("old note", oldEntry.Note);
        Assert.Equal(oldDto.Id, oldEntry.Id);
        Assert.Single(recentEntry.Photos);
        Assert.Null(recentEntry.PhotosPurgedUtc);
    }

    [Fact]
    public async Task Csv_export_escapes_quotes_and_neutralises_formulas()
    {
        var (service, repo, _) = Build();
        repo.Usage.Add(new InventoryUsage
        {
            EmployeeName = "=HYPERLINK(\"x\")", LocationName = "Store", ItemName = "Keys \"master\"", Unit = "pcs",
            Quantity = 2, Note = "+1", TakenUtc = Now, Photos = [new InventoryPhoto(Guid.NewGuid(), ".png")],
        });

        var csv = await service.ExportCsvAsync(null, null, null, default);

        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Contains("\"Keys \"\"master\"\"\"", csv);
        Assert.Contains("\"'+1\"", csv);
    }

    [Fact]
    public async Task Date_range_end_must_not_be_before_start()
    {
        var (service, _, _) = Build();
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListUsageAsync(null, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 1), 0, 10, default));
    }

    [Fact]
    public void Helsinki_day_starts_at_the_previous_utc_evening_in_winter()
    {
        var utc = InventoryService.HelsinkiDayStartUtc(new DateOnly(2026, 1, 15));
        Assert.Equal(new DateTime(2026, 1, 14, 22, 0, 0, DateTimeKind.Utc), utc);
    }
}
