using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Inventory;
using CleaningSuite.Domain.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Supply requests for admins and managers: review the queue and approve, decline or fulfil each request.</summary>
[ApiController]
[Route("api/v1/admin/supply-requests")]
[Authorize(Policy = RealmRoleAuthorization.PolicyPrefix + "operations")]
public class SupplyRequestAdminController(SupplyRequestService requests) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var parsed = ParseStatus(status);
        return Ok(await requests.ListForReviewAsync(parsed, skip, take, ct));
    }

    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] SupplyDecisionBody body, CancellationToken ct) =>
        Ok(await requests.DecideAsync(ReviewerName(), id, body.Version, ParseDecision(body.Decision), body.Note, ct));

    private string ReviewerName() => User.Identity?.Name is { Length: > 0 } name ? name : "Office";

    private static SupplyRequestStatus? ParseStatus(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : Enum.TryParse<SupplyRequestStatus>(value, ignoreCase: true, out var status) ? status
        : throw new FluentValidation.ValidationException("Choose a valid status.");

    private static SupplyDecision ParseDecision(string value) => value.ToLowerInvariant() switch
    {
        "approve" => SupplyDecision.Approve,
        "decline" => SupplyDecision.Decline,
        "fulfil" => SupplyDecision.Fulfil,
        _ => throw new FluentValidation.ValidationException("Choose approve, decline or fulfil."),
    };

    public sealed record SupplyDecisionBody(string Decision, Guid Version, string? Note);
}
