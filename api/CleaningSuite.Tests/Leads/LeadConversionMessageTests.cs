using CleaningSuite.Infrastructure.Persistence;
using Xunit;

namespace CleaningSuite.Tests.Leads;

public class LeadConversionMessageTests
{
    [Fact]
    public void A_duplicate_business_id_on_conversion_says_where_to_go_next()
    {
        Assert.Contains("No new customer was created", LeadRepository.DuplicateCustomerMessage);
        Assert.Contains("Open that customer", LeadRepository.DuplicateCustomerMessage);
        Assert.Contains("change this lead's business ID", LeadRepository.DuplicateCustomerMessage);
    }
}
