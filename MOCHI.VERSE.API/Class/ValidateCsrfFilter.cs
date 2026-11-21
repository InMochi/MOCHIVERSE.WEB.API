using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Enforces antiforgery validation on all state-changing requests and returns 403 APIResponse on failure.
/// </summary>
public sealed class ValidateCsrfFilter : IAsyncAuthorizationFilter
{
    private readonly IAntiforgery _antiforgery;

    public ValidateCsrfFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) ||
            HttpMethods.IsHead(method) ||
            HttpMethods.IsOptions(method) ||
            HttpMethods.IsTrace(method))
        {
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(
                Master.CreateAPIResponse(false, "Invalid or missing CSRF token."))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
