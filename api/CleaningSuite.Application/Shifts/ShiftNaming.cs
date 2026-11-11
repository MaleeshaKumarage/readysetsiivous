using FluentValidation;
using CleaningSuite.Domain.Companies;
using FluentValidation.Results;

namespace CleaningSuite.Application.Shifts;

/// <summary>
/// One name pattern for every shift, whoever created it:
/// "Cleaning - {Street}, {PostalCode} {City} |{SizeM2}m2|{Rooms}H|{Beds}V". Cleaning type is set per visit.
/// The name is built from the property, so admin and partner bookings for the same property read the same.
/// </summary>
public static class ShiftNaming
{
    public static void EnsureDetails(Branch branch)
    {
        if (!branch.HasNameDetails)
            throw new ValidationException([new ValidationFailure("Property", "Add the property's size before scheduling a shift for it.")]);
    }

    public static string Build(Branch branch)
    {
        var address = string.Join(", ", new[] { branch.Street?.Trim(), $"{branch.PostalCode} {branch.City}".Trim() }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        // No address: the heading is just "Cleaning", with no dangling separator.
        var heading = address.Length == 0 ? "Cleaning" : $"Cleaning - {address}";
        return $"{heading} |{branch.SizeM2}m2|{Count(branch.RoomCount)}H|{Count(branch.BedCount)}V";
    }

    private static string Count(int? value) => value?.ToString() ?? "-";
}
