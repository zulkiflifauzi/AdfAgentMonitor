using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AdfAgentMonitor.Api.Authentication;

/// <summary>
/// Action filter that enforces <c>X-Api-Key</c> header authentication.
/// Apply with <c>[ServiceFilter(typeof(ApiKeyAuthFilter))]</c>.
/// Returns <c>401 Unauthorized</c> when the header is absent or does not match
/// the value configured in <see cref="ApiSettings.ApiKey"/>.
/// </summary>
public class ApiKeyAuthFilter(IOptions<ApiSettings> options) : IAsyncActionFilter
{
    private const string HeaderName = "X-Api-Key";

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var configuredKey = options.Value.ApiKey;

        if (string.IsNullOrEmpty(configuredKey))
        {
            // Fail closed: if no key is configured, deny all requests so a
            // misconfigured deployment is never silently open.
            context.Result = new ObjectResult(new { error = "API key authentication is not configured." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var providedKey)
            || !string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(
                new { error = $"A valid {HeaderName} header is required." });
            return;
        }

        await next();
    }
}
