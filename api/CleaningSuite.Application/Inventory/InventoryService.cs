using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Inventory;
using FluentValidation;

namespace CleaningSuite.Application.Inventory;

/// <summary>A name or version conflict in the inventory catalog or log. The message says what to do next.</summary>
public sealed class InventoryConflictException(string message) : Exception(message);

public interface IInventoryRepository
{
    Task<IReadOnlyList<InventoryLocation>> ListLocationsAsync(CancellationToken ct);
    Task<IReadOnlyList<InventoryItem>> ListItemsAsync(CancellationToken ct);
    Task<InventoryLocation?> GetLocationAsync(Guid id, CancellationToken ct);
    Task<InventoryItem?> GetItemAsync(Guid id, CancellationToken ct);
    Task<InventoryUsage?> GetUsageAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<InventoryUsage>> ListUsageAsync(InventoryUsageQuery query, CancellationToken ct);
    /// <summary>Entries taken before the cutoff whose photos have not been purged yet, oldest first.</summary>
    Task<IReadOnlyList<InventoryUsage>> ListUsageWithPhotosTakenBeforeAsync(DateTime cutoffUtc, int take, CancellationToken ct);
    Task SaveAsync<T>(T document, CancellationToken ct) where T : BaseDocument;
}

public interface IInventoryFileStore
{
    Task SaveAsync(Guid usageId, InventoryPhoto photo, byte[] content, CancellationToken ct);
    Task<byte[]?> ReadAsync(Guid usageId, InventoryPhoto photo, CancellationToken ct);
    Task DeleteAsync(Guid usageId, InventoryPhoto photo);
}

public sealed record InventoryUsageQuery(Guid? EmployeeId, DateTime? FromUtc, DateTime? ToUtc, int Skip, int Take);

public sealed record InventoryActor(Guid EmployeeId, string EmployeeName);

public sealed record InventoryLocationDto(Guid Id, string Name, bool IsActive, Guid Version);

public sealed record InventoryItemDto(Guid Id, string Name, string Unit, bool IsVerified, bool IsActive, Guid Version);

public sealed record InventoryUsageDto(
    Guid Id, Guid EmployeeId, string EmployeeName, Guid LocationId, string LocationName,
    Guid ItemId, string ItemName, string Unit, int Quantity, string Note, DateTime TakenUtc,
    IReadOnlyList<Guid> PhotoIds, bool PhotosPurged, Guid Version);

/// <summary>Inventory catalog, the take log, and photo retention. Employees log takes; admins manage the catalog and review the log.</summary>
public sealed class InventoryService(IInventoryRepository repository, IInventoryFileStore files, IUploadContentValidator uploads, TimeProvider clock)
{
    public const int MaxPhotosPerEntry = 5;
    public const int MaxQuantity = 10000;
    public const int MaxNoteLength = 500;
    public const int MaxNameLength = 80;
    public const int MaxUnitLength = 20;
    public const int MaxExportRows = 5000;
    public const string DefaultUnit = "pcs";
    public const string VersionConflictMessage = "This was changed by someone else. Refresh the page and try again.";

    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

    /// <summary>Photos are kept for three months after the entry is logged.</summary>
    public static DateTime PhotoRetentionCutoff(DateTime nowUtc) => nowUtc.AddMonths(-3);

    /// <summary>Converts a Helsinki calendar day to the UTC instant at which that day starts.</summary>
    public static DateTime HelsinkiDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Helsinki);

    // ---- Locations ----

    public async Task<IReadOnlyList<InventoryLocationDto>> ListLocationsAsync(bool includeInactive, CancellationToken ct) =>
        (await repository.ListLocationsAsync(ct))
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(LocationDto).ToArray();

    public async Task<InventoryLocationDto> CreateLocationAsync(string name, CancellationToken ct)
    {
        var clean = CleanName(name, "Location");
        var existing = await repository.ListLocationsAsync(ct);
        EnsureUniqueName(existing.Select(x => x.Name), clean, "location");
        var location = new InventoryLocation { Name = clean, CreatedUtc = clock.GetUtcNow().UtcDateTime, UpdatedUtc = clock.GetUtcNow().UtcDateTime };
        await repository.SaveAsync(location, ct);
        return LocationDto(location);
    }

    public async Task<InventoryLocationDto> UpdateLocationAsync(Guid id, string name, bool isActive, Guid version, CancellationToken ct)
    {
        var location = await repository.GetLocationAsync(id, ct) ?? throw new NotFoundException("Inventory location", id);
        RequireVersion(location.Version, version);
        var clean = CleanName(name, "Location");
        var others = (await repository.ListLocationsAsync(ct)).Where(x => x.Id != id).Select(x => x.Name);
        EnsureUniqueName(others, clean, "location");
        location.Name = clean;
        location.IsActive = isActive;
        location.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(location, ct);
        return LocationDto(location);
    }

    // ---- Items ----

    public async Task<IReadOnlyList<InventoryItemDto>> ListItemsAsync(bool includeInactive, CancellationToken ct) =>
        (await repository.ListItemsAsync(ct))
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ItemDto).ToArray();

    /// <summary>
    /// An employee picks "add new item". If an active item with the same name exists, that item is returned
    /// instead of creating a duplicate. The new item is unverified until an admin reviews it.
    /// </summary>
    public async Task<InventoryItemDto> AddItemFromEmployeeAsync(InventoryActor actor, string name, string? unit, CancellationToken ct)
    {
        var clean = CleanName(name, "Item");
        var existing = (await repository.ListItemsAsync(ct)).FirstOrDefault(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.IsActive) return ItemDto(existing);
            throw new InventoryConflictException($"An inactive item named \"{existing.Name}\" already exists. Ask an admin to reactivate it or choose another name.");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var item = new InventoryItem
        {
            Name = clean, Unit = CleanUnit(unit), IsVerified = false, CreatedByEmployeeId = actor.EmployeeId,
            CreatedUtc = now, UpdatedUtc = now,
        };
        await repository.SaveAsync(item, ct);
        return ItemDto(item);
    }

    public async Task<InventoryItemDto> CreateItemAsync(string name, string? unit, CancellationToken ct)
    {
        var clean = CleanName(name, "Item");
        var existing = await repository.ListItemsAsync(ct);
        EnsureUniqueName(existing.Select(x => x.Name), clean, "item");
        var now = clock.GetUtcNow().UtcDateTime;
        var item = new InventoryItem { Name = clean, Unit = CleanUnit(unit), IsVerified = true, CreatedUtc = now, UpdatedUtc = now };
        await repository.SaveAsync(item, ct);
        return ItemDto(item);
    }

    public async Task<InventoryItemDto> UpdateItemAsync(Guid id, string name, string? unit, bool isActive, Guid version, CancellationToken ct)
    {
        var item = await repository.GetItemAsync(id, ct) ?? throw new NotFoundException("Inventory item", id);
        RequireVersion(item.Version, version);
        var clean = CleanName(name, "Item");
        var others = (await repository.ListItemsAsync(ct)).Where(x => x.Id != id).Select(x => x.Name);
        EnsureUniqueName(others, clean, "item");
        item.Name = clean;
        item.Unit = CleanUnit(unit);
        item.IsActive = isActive;
        item.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(item, ct);
        return ItemDto(item);
    }

    public async Task<InventoryItemDto> VerifyItemAsync(Guid id, Guid version, CancellationToken ct)
    {
        var item = await repository.GetItemAsync(id, ct) ?? throw new NotFoundException("Inventory item", id);
        RequireVersion(item.Version, version);
        item.IsVerified = true;
        item.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
        await repository.SaveAsync(item, ct);
        return ItemDto(item);
    }

    // ---- Take log ----

    /// <summary>Logs a take. The entry is saved only with 1 to 5 valid photos, and the timestamp is set by the server.</summary>
    public async Task<InventoryUsageDto> RecordUsageAsync(InventoryActor actor, Guid locationId, Guid itemId, int quantity, string? note,
        IReadOnlyList<byte[]> photos, CancellationToken ct)
    {
        if (photos.Count == 0) throw new FluentValidation.ValidationException("Attach at least one photo before saving.");
        if (photos.Count > MaxPhotosPerEntry) throw new FluentValidation.ValidationException($"Attach up to {MaxPhotosPerEntry} photos per entry.");
        if (quantity < 1 || quantity > MaxQuantity) throw new FluentValidation.ValidationException($"Quantity must be between 1 and {MaxQuantity:N0}.");
        var cleanNote = (note ?? "").Trim();
        if (cleanNote.Length > MaxNoteLength) throw new FluentValidation.ValidationException($"Note must be {MaxNoteLength} characters or fewer.");

        var location = await repository.GetLocationAsync(locationId, ct);
        if (location is null || !location.IsActive) throw new FluentValidation.ValidationException("Choose an active location.");
        var item = await repository.GetItemAsync(itemId, ct);
        if (item is null || !item.IsActive) throw new FluentValidation.ValidationException("Choose an active item.");

        var extensions = photos.Select(p => uploads.ValidateServiceImage(p)).ToArray();
        var now = clock.GetUtcNow().UtcDateTime;
        var usage = new InventoryUsage
        {
            EmployeeId = actor.EmployeeId, EmployeeName = actor.EmployeeName,
            LocationId = location.Id, LocationName = location.Name,
            ItemId = item.Id, ItemName = item.Name, Unit = item.Unit,
            Quantity = quantity, Note = cleanNote, TakenUtc = now, CreatedUtc = now, UpdatedUtc = now,
        };

        var stored = new List<InventoryPhoto>();
        try
        {
            for (var i = 0; i < photos.Count; i++)
            {
                var photo = new InventoryPhoto(Guid.NewGuid(), extensions[i]);
                await files.SaveAsync(usage.Id, photo, photos[i], ct);
                stored.Add(photo);
            }
            usage.Photos = stored;
            await repository.SaveAsync(usage, ct);
        }
        catch
        {
            foreach (var photo in stored) await files.DeleteAsync(usage.Id, photo);
            throw;
        }
        return UsageDto(usage);
    }

    public async Task<IReadOnlyList<InventoryUsageDto>> ListUsageAsync(Guid? employeeId, DateOnly? from, DateOnly? to, int skip, int take, CancellationToken ct)
    {
        var (fromUtc, toUtc) = DateRange(from, to);
        var rows = await repository.ListUsageAsync(new InventoryUsageQuery(employeeId, fromUtc, toUtc, skip, take), ct);
        return rows.Select(UsageDto).ToArray();
    }

    public async Task<string> ExportCsvAsync(Guid? employeeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (fromUtc, toUtc) = DateRange(from, to);
        var rows = await repository.ListUsageAsync(new InventoryUsageQuery(employeeId, fromUtc, toUtc, 0, MaxExportRows), ct);
        var lines = new List<string> { string.Join(',', "Taken (Helsinki)", "Employee", "Location", "Item", "Quantity", "Unit", "Note", "Photos") };
        foreach (var row in rows)
        {
            lines.Add(string.Join(',',
                Csv(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.TakenUtc, DateTimeKind.Utc), Helsinki).ToString("yyyy-MM-dd HH:mm")),
                Csv(row.EmployeeName), Csv(row.LocationName), Csv(row.ItemName),
                row.Quantity.ToString(), Csv(row.Unit), Csv(row.Note),
                row.PhotosPurgedUtc is not null ? "deleted by retention" : row.Photos.Count.ToString()));
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Returns a photo. When employeeId is set, the entry must belong to that employee or the photo is treated as not found.</summary>
    public async Task<(byte[] Bytes, string ContentType)> ReadPhotoAsync(Guid usageId, Guid photoId, Guid? employeeId, CancellationToken ct)
    {
        var usage = await repository.GetUsageAsync(usageId, ct) ?? throw new NotFoundException("Inventory entry", usageId);
        if (employeeId is { } owner && usage.EmployeeId != owner) throw new NotFoundException("Inventory entry", usageId);
        var photo = usage.Photos.SingleOrDefault(x => x.Id == photoId) ?? throw new NotFoundException("Inventory photo", photoId);
        var bytes = await files.ReadAsync(usageId, photo, ct) ?? throw new NotFoundException("Inventory photo", photoId);
        return (bytes, photo.Extension switch { ".jpg" => "image/jpeg", ".png" => "image/png", _ => "image/webp" });
    }

    /// <summary>Deletes photos older than the retention cutoff. Entry text is kept. Returns how many entries were purged.</summary>
    public async Task<int> PurgeExpiredPhotosAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
    {
        var due = await repository.ListUsageWithPhotosTakenBeforeAsync(PhotoRetentionCutoff(nowUtc), batchSize, ct);
        foreach (var usage in due)
        {
            foreach (var photo in usage.Photos) await files.DeleteAsync(usage.Id, photo);
            usage.Photos = new();
            usage.PhotosPurgedUtc = nowUtc;
            usage.UpdatedUtc = nowUtc;
            await repository.SaveAsync(usage, ct);
        }
        return due.Count;
    }

    // ---- helpers ----

    private static (DateTime? FromUtc, DateTime? ToUtc) DateRange(DateOnly? from, DateOnly? to)
    {
        if (from is { } f && to is { } t && t < f) throw new FluentValidation.ValidationException("The end date must be on or after the start date.");
        return (from is { } fd ? HelsinkiDayStartUtc(fd) : null, to is { } td ? HelsinkiDayStartUtc(td.AddDays(1)) : null);
    }

    private static string CleanName(string? name, string label)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length < 2 || clean.Length > MaxNameLength)
            throw new FluentValidation.ValidationException($"{label} name must be between 2 and {MaxNameLength} characters.");
        return clean;
    }

    private static string CleanUnit(string? unit)
    {
        var clean = string.IsNullOrWhiteSpace(unit) ? DefaultUnit : unit.Trim();
        if (clean.Length > MaxUnitLength) throw new FluentValidation.ValidationException($"Unit must be {MaxUnitLength} characters or fewer.");
        return clean;
    }

    private static void EnsureUniqueName(IEnumerable<string> existingNames, string candidate, string kind)
    {
        if (existingNames.Any(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase)))
            throw new InventoryConflictException($"A {kind} named \"{candidate}\" already exists. Choose another name, or reactivate the existing {kind}.");
    }

    private static void RequireVersion(Guid current, Guid expected)
    {
        if (current != expected) throw new InventoryConflictException(VersionConflictMessage);
    }

    private static string Csv(string value)
    {
        // Prefix formula-like values so a spreadsheet does not run them as formulas.
        var safe = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private static InventoryLocationDto LocationDto(InventoryLocation x) => new(x.Id, x.Name, x.IsActive, x.Version);
    private static InventoryItemDto ItemDto(InventoryItem x) => new(x.Id, x.Name, x.Unit, x.IsVerified, x.IsActive, x.Version);
    private static InventoryUsageDto UsageDto(InventoryUsage x) => new(
        x.Id, x.EmployeeId, x.EmployeeName, x.LocationId, x.LocationName, x.ItemId, x.ItemName, x.Unit,
        x.Quantity, x.Note, x.TakenUtc, x.Photos.Select(p => p.Id).ToArray(), x.PhotosPurgedUtc is not null, x.Version);
}
