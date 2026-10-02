using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers;

/// <summary>
/// Admin health probe. Deliberately unauthenticated (like the top-level
/// <c>/healthz</c> liveness endpoint) so orchestrators and load balancers can
/// poll it without a Keycloak token. This is the documented exception to the
/// admin-route auth convention.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/admin/health")]
public class HealthAdminController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        Ok(new
        {
            status = "ok",
            utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
        });
}
