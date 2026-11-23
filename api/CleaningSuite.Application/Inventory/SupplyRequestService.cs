using CleaningSuite.Domain.Inventory;
using FluentValidation;

namespace CleaningSuite.Application.Inventory;

public interface ISupplyRequestRepository
{
    Task<SupplyRequest?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SupplyRequest>> ListAsync(SupplyRequestQuery query, CancellationToken ct);
    Task SaveAsync(SupplyRequest request, CancellationToken ct);
}

public sealed record SupplyRequestQuery(Guid? EmployeeId, SupplyRequestStatus? Status, int Skip, int Take);

public sealed record SupplyRequestLineInput(Guid ItemId, int Quantity);

public sealed record SupplyRequestDto(
    Guid Id, Guid EmployeeId, string EmployeeName, IReadOnlyList<SupplyRequestLine> Lines, string Note,
    string Status, string? DecisionNote, string? DecidedByName, DateTime? DecidedUtc, DateTime CreatedUtc, Guid Version);

/// <summary>What an admin or manager does to a request.</summary>
public enum SupplyDecision { Approve, Decline, Fulfil }

/// <summary>Supply requests: employees ask for catalogue items; admins and managers approve, decline and fulfil them.</summary>
public sealed class SupplyRequestService(IInventoryRepository inventory, ISupplyRequestRepository requests, TimeProvider clock)
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 1000;
    public const int MaxNoteLength = 500;
    public const string NotAvailableMessage = "One of the chosen items is no longer available. Refresh and choose again.";
    public const string GoneMessage = "That supply request no longer exists. Refresh the page.";

    public async Task<SupplyRequestDto> CreateAsync(InventoryActor actor, IReadOnlyList<SupplyRequestLineInput> lines, string? note, CancellationToken ct)
    {
        var trimmedNote = (note ?? "").Trim();
        if (trimmedNote.Length > MaxNoteLength) throw new ValidationException($"Keep the note to {MaxNoteLength} characters.");
        if (lines.Count == 0) throw new ValidationException("Choose at least one item.");
        if (lines.Count > MaxLines) throw new ValidationException($"A request can hold up to {MaxLines} items.");
        if (lines.Any(line => line.Quantity < 1 || line.Quantity > MaxQuantity))
            throw new ValidationException($"Quantity must be between 1 and {MaxQuantity}.");
        if (lines.GroupBy(line => line.ItemId).Any(group => group.Count() > 1))
            throw new ValidationException("Each item can appear once in a request. Change the quantity instead.");

        var resolved = new List<SupplyRequestLine>(lines.Count);
        foreach (var line in lines)
        {
            var item = await inventory.GetItemAsync(line.ItemId, ct);
            if (item is null || !item.IsActive) throw new ValidationException(NotAvailableMessage);
            resolved.Add(new SupplyRequestLine(item.Id, item.Name, item.Unit, line.Quantity));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var request = new SupplyRequest
        {
            EmployeeId = actor.EmployeeId,
            EmployeeName = actor.EmployeeName,
            Lines = resolved,
            Note = trimmedNote,
            Status = SupplyRequestStatus.Requested,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        await requests.SaveAsync(request, ct);
        return ToDto(request);
    }

    public async Task<IReadOnlyList<SupplyRequestDto>> ListOwnAsync(InventoryActor actor, CancellationToken ct) =>
        (await requests.ListAsync(new SupplyRequestQuery(actor.EmployeeId, null, 0, 200), ct)).Select(ToDto).ToList();

    public async Task<IReadOnlyList<SupplyRequestDto>> ListForReviewAsync(SupplyRequestStatus? status, int skip, int take, CancellationToken ct) =>
        (await requests.ListAsync(new SupplyRequestQuery(null, status, Math.Max(0, skip), Math.Clamp(take, 1, 200)), ct)).Select(ToDto).ToList();

    /// <summary>
    /// Applies one decision. The expected version must match the stored request, so two reviewers cannot both act on the same
    /// state. Each decision is allowed only from the status it moves on from.
    /// </summary>
    public async Task<SupplyRequestDto> DecideAsync(string reviewerName, Guid id, Guid expectedVersion, SupplyDecision decision, string? note, CancellationToken ct)
    {
        var request = await requests.GetAsync(id, ct) ?? throw new InventoryConflictException(GoneMessage);
        if (request.Version != expectedVersion) throw new InventoryConflictException(InventoryService.VersionConflictMessage);

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > MaxNoteLength }) throw new ValidationException($"Keep the note to {MaxNoteLength} characters.");

        var (from, to) = decision switch
        {
            SupplyDecision.Approve => (SupplyRequestStatus.Requested, SupplyRequestStatus.Approved),
            SupplyDecision.Decline => (SupplyRequestStatus.Requested, SupplyRequestStatus.Declined),
            _ => (SupplyRequestStatus.Approved, SupplyRequestStatus.Fulfilled),
        };
        if (request.Status != from)
            throw new InventoryConflictException($"This request is already {request.Status.ToString().ToLowerInvariant()}. Refresh the page.");

        var now = clock.GetUtcNow().UtcDateTime;
        request.Status = to;
        request.DecidedByName = reviewerName;
        request.DecidedUtc = now;
        request.DecisionNote = trimmedNote;
        request.UpdatedUtc = now;
        await requests.SaveAsync(request, ct);
        return ToDto(request);
    }

    private static SupplyRequestDto ToDto(SupplyRequest r) => new(
        r.Id, r.EmployeeId, r.EmployeeName, r.Lines, r.Note, r.Status.ToString(), r.DecisionNote, r.DecidedByName, r.DecidedUtc, r.CreatedUtc, r.Version);
}
