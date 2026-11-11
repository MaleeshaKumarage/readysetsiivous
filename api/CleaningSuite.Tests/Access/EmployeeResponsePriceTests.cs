using System.Reflection;
using System.Text.RegularExpressions;
using CleaningSuite.Application.Access;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;

namespace CleaningSuite.Tests.Access;

// Employees see schedules, work and offers, but never prices or pay. These records are what the employee endpoints return.
public class EmployeeResponsePriceTests
{
    private static readonly Regex MoneyName = new("price|rate|total|pay|gross|net|vat", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData(typeof(EmployeeSelfResponse))]
    [InlineData(typeof(OwnWorkResponse))]
    [InlineData(typeof(OperationalBookingResponse))]
    [InlineData(typeof(OperationalShiftResponse))]
    [InlineData(typeof(MyShiftOccurrenceDto))]
    [InlineData(typeof(QualityCycleFormDto))]
    [InlineData(typeof(PartnerCleanerOfferDto))]
    // Branch visits are shown to managers too, and the occurrence list reaches manager screens: neither carries a price.
    [InlineData(typeof(CleaningSuite.Application.Shifts.BranchVisitDto))]
    [InlineData(typeof(CleaningSuite.Application.Shifts.BranchMainDto))]
    [InlineData(typeof(CleaningSuite.Application.Shifts.ShiftOccurrenceDto))]
    public void Employee_facing_responses_carry_no_price_or_pay_fields(Type response)
    {
        var names = response.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, name => MoneyName.IsMatch(name));
    }
}
