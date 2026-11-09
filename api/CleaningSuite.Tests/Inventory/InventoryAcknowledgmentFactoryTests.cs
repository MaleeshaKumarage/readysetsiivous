using CleaningSuite.Domain.Inventory;
using Xunit;

namespace CleaningSuite.Tests.Inventory;

public sealed class InventoryAcknowledgmentFactoryTests
{
    [Fact]
    public void ForUsage_uses_the_usage_id_and_starts_pending()
    {
        var usageId = Guid.NewGuid();

        var row = InventoryAcknowledgment.ForUsage(usageId);

        Assert.Equal(usageId, row.Id);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.RespondedUtc);
    }
}
