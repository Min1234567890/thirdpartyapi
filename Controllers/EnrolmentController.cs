using Microsoft.AspNetCore.Mvc;
using ThirdpartyAPI.Models;
using ThirdpartyAPI.Services;

namespace ThirdpartyAPI.Controllers;

[ApiController]
[Route("api")]
public class EnrolmentController : ControllerBase
{
    private readonly EnrolmentService _enrol;
    private readonly VerifyService _verify;
    private readonly HealthService _health;

    public EnrolmentController(EnrolmentService enrol, VerifyService verify, HealthService health)
    {
        _enrol = enrol;
        _verify = verify;
        _health = health;
    }

    [HttpPost("enrol")]
    public IActionResult Enrol([FromBody] EnrolmentRequest request)
    {
        if (request == null)
            return BadRequest(new EnrolmentResponse { Result = "FAILURE", Message = "Request body is required." });

        var r = _enrol.Enrol(request);
        return r.Result switch
        {
            "SUCCESS" => Ok(r),
            "USER_NOT_FOUND" or "ALREADY_ENROLLED" => Ok(r),
            _ => UnprocessableEntity(r)
        };
    }

    [HttpPost("verify")]
    public IActionResult Verify([FromBody] VerifyRequest request)
    {
        if (request == null)
            return BadRequest(new VerifyResponse { Result = "FAILURE", Message = "Request body is required." });

        var r = _verify.Verify(request);
        return r.Result switch
        {
            "VERIFIED" => Ok(r),
            "USER_NOT_FOUND" or "USER_NOT_ENROLLED" => NotFound(r),
            "NOT_VERIFIED" => UnprocessableEntity(r),
            _ => UnprocessableEntity(r)
        };
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(_health.Check());
}
