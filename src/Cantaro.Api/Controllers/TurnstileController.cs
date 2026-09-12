using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/auth/turnstile")]
public sealed class TurnstileController(IOptions<TurnstileOptions> options) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public ActionResult<TurnstileConfigurationResponse> GetConfiguration()
    {
        var value = options.Value;
        return Ok(new TurnstileConfigurationResponse(value.IsEnabled, value.IsEnabled ? value.SiteKey : null));
    }
}

public sealed record TurnstileConfigurationResponse(bool Enabled, string? SiteKey);
