using Microsoft.AspNetCore.Mvc;

namespace CleaningSuite.Api.Controllers
{
    [ApiController]
    [Route("api/v1/admin")]
    public class AdminController : ControllerBase
    {
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new { status = "ok" });
        }
    }
}
