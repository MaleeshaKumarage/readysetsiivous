using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Inventory;
using FluentValidation;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Inventory;

public sealed class SupplyRequestServiceTests
{
    private static readonly InventoryActor Anna = new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Anna Staff");
    private static readonly InventoryActor Ben = new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Ben Staff");

    private readonly Mock<IInventoryRepository> inventory = new();
    private readonly InMemorySupplyRequests store = new();
    private readonly Guid cloths = Guid.NewGuid();
    private readonly Guid bags = Guid.NewGuid();

    public SupplyRequestServiceTests()
    {
        Item(cloths, "Microfibre cloths", "pcs", active: true);
        Item(bags, "Bin liners", "roll", active: true);
    }

    private void Item(Guid id, string name, string unit, bool active) =>
        inventory.Setup(r => r.GetItemAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventoryItem { Id = id, Name = name, Unit = unit, IsActive = active });

    private SupplyRequestService Service() => new(inventory.Object, store, TimeProvider.System);

    private static IReadOnlyList<SupplyRequestLineInput> Lines(params (Guid item, int qty)[] lines) =>
        lines.Select(l => new SupplyRequestLineInput(l.item, l.qty)).ToList();

    // ---- Create ----

    [Fact]
    public async Task Create_copies_item_name_and_unit_and_starts_as_requested()
    {
        var dto = await Service().CreateAsync(Anna, Lines((cloths, 4), (bags, 2)), "  for the Riverside visit  ", CancellationToken.None);

        Assert.Equal("Requested", dto.Status);
        Assert.Equal("for the Riverside visit", dto.Note);
        Assert.Equal(Anna.EmployeeId, dto.EmployeeId);
        Assert.Equal(new[] { ("Microfibre cloths", "pcs", 4), ("Bin liners", "roll", 2) },
            dto.Lines.Select(l => (l.ItemName, l.Unit, l.Quantity)).ToArray());
    }

    [Fact]
    public async Task Create_rejects_an_empty_request()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines(), null, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(SupplyRequestService.MaxQuantity + 1)]
    public async Task Create_rejects_a_quantity_outside_the_allowed_range(int quantity)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines((cloths, quantity)), null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_the_same_item_twice()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines((cloths, 1), (cloths, 2)), null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_an_item_that_is_inactive_or_missing()
    {
        var retired = Guid.NewGuid();
        Item(retired, "Old polish", "pcs", active: false);
        var missing = Guid.NewGuid();

        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines((retired, 1)), null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines((missing, 1)), null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_too_many_items_and_a_long_note()
    {
        var many = Enumerable.Range(0, SupplyRequestService.MaxLines + 1).Select(_ => (Guid.NewGuid(), 1)).ToArray();
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines(many), null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => Service().CreateAsync(Anna, Lines((cloths, 1)), new string('x', SupplyRequestService.MaxNoteLength + 1), CancellationToken.None));
    }

    // ---- Own list ----

    [Fact]
    public async Task Own_list_shows_only_the_callers_requests()
    {
        var service = Service();
        await service.CreateAsync(Anna, Lines((cloths, 1)), null, CancellationToken.None);
        await service.CreateAsync(Ben, Lines((bags, 1)), null, CancellationToken.None);

        var mine = await service.ListOwnAsync(Anna, CancellationToken.None);

        Assert.Single(mine);
        Assert.Equal(Anna.EmployeeId, mine[0].EmployeeId);
    }

    // ---- Review ----

    [Fact]
    public async Task Approve_then_fulfil_records_the_reviewer_at_each_step()
    {
        var service = Service();
        var created = await service.CreateAsync(Anna, Lines((cloths, 2)), null, CancellationToken.None);

        var approved = await service.DecideAsync("Office Manager", created.Id, created.Version, SupplyDecision.Approve, "Done Monday", CancellationToken.None);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("Office Manager", approved.DecidedByName);
        Assert.Equal("Done Monday", approved.DecisionNote);

        var fulfilled = await service.DecideAsync("Office Manager", created.Id, approved.Version, SupplyDecision.Fulfil, null, CancellationToken.None);
        Assert.Equal("Fulfilled", fulfilled.Status);
        Assert.Null(fulfilled.DecisionNote);
    }

    [Fact]
    public async Task Decline_is_allowed_only_from_requested()
    {
        var service = Service();
        var created = await service.CreateAsync(Anna, Lines((cloths, 2)), null, CancellationToken.None);

        var declined = await service.DecideAsync("Manager", created.Id, created.Version, SupplyDecision.Decline, "Out of budget", CancellationToken.None);
        Assert.Equal("Declined", declined.Status);

        await Assert.ThrowsAsync<InventoryConflictException>(() =>
            service.DecideAsync("Manager", created.Id, declined.Version, SupplyDecision.Approve, null, CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_before_approval_is_refused()
    {
        var service = Service();
        var created = await service.CreateAsync(Anna, Lines((cloths, 2)), null, CancellationToken.None);

        await Assert.ThrowsAsync<InventoryConflictException>(() =>
            service.DecideAsync("Manager", created.Id, created.Version, SupplyDecision.Fulfil, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_stale_version_is_refused_so_two_reviewers_cannot_both_act()
    {
        var service = Service();
        var created = await service.CreateAsync(Anna, Lines((cloths, 2)), null, CancellationToken.None);
        await service.DecideAsync("First", created.Id, created.Version, SupplyDecision.Approve, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InventoryConflictException>(() =>
            service.DecideAsync("Second", created.Id, created.Version, SupplyDecision.Decline, null, CancellationToken.None));
        Assert.Equal(InventoryService.VersionConflictMessage, ex.Message);
    }

    [Fact]
    public async Task A_missing_request_is_reported_as_gone()
    {
        var ex = await Assert.ThrowsAsync<InventoryConflictException>(() =>
            Service().DecideAsync("Manager", Guid.NewGuid(), Guid.NewGuid(), SupplyDecision.Approve, null, CancellationToken.None));
        Assert.Equal(SupplyRequestService.GoneMessage, ex.Message);
    }

    [Fact]
    public async Task Review_list_filters_by_status()
    {
        var service = Service();
        var first = await service.CreateAsync(Anna, Lines((cloths, 1)), null, CancellationToken.None);
        await service.CreateAsync(Ben, Lines((bags, 1)), null, CancellationToken.None);
        await service.DecideAsync("Manager", first.Id, first.Version, SupplyDecision.Decline, null, CancellationToken.None);

        var requested = await service.ListForReviewAsync(SupplyRequestStatus.Requested, 0, 50, CancellationToken.None);

        Assert.Single(requested);
        Assert.Equal(Ben.EmployeeId, requested[0].EmployeeId);
    }

    /// <summary>Stores requests in memory. Each save gets a new version, as Marten does.</summary>
    private sealed class InMemorySupplyRequests : ISupplyRequestRepository
    {
        private readonly Dictionary<Guid, SupplyRequest> rows = new();

        public Task<SupplyRequest?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(rows.TryGetValue(id, out var row) ? row : null);

        public Task<IReadOnlyList<SupplyRequest>> ListAsync(SupplyRequestQuery query, CancellationToken ct)
        {
            IEnumerable<SupplyRequest> q = rows.Values;
            if (query.EmployeeId is { } employee) q = q.Where(x => x.EmployeeId == employee);
            if (query.Status is { } status) q = q.Where(x => x.Status == status);
            IReadOnlyList<SupplyRequest> page = q.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Skip(query.Skip).Take(query.Take).ToList();
            return Task.FromResult(page);
        }

        public Task SaveAsync(SupplyRequest request, CancellationToken ct)
        {
            request.Version = Guid.NewGuid();
            rows[request.Id] = request;
            return Task.CompletedTask;
        }
    }
}
