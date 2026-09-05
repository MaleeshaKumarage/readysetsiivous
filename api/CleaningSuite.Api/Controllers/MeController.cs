using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>Identity helper for the frontend: current realm and roles.</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var realm = HttpContext.Items["Realm"] as string ?? "";
        var roles = User.FindAll("realm_access")
            .Select(c => c.Value)
            .ToList();

        return Ok(new { realm, roles, sub = User.FindFirstValue("sub") });
    }
}
