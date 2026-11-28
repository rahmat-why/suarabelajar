using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AudiobookSystem.Filters
{
    public class AuthorizeCustomerAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // ===============================
            // 1. AllowAnonymous → SKIP
            // ===============================
            if (context.ActionDescriptor.EndpointMetadata
                .Any(m => m is AllowAnonymousAttribute))
            {
                return;
            }

            var httpContext = context.HttpContext;

            // ===============================
            // 2. Read from COOKIE (browser)
            // ===============================
            var cookies = httpContext.Request.Cookies;

            bool isActive = cookies["CUSTOMER_ACTIVE"] == "1";

            if (isActive)
                return;

            // ===============================
            // 3. Detect API vs PAGE
            // ===============================
            var path = httpContext.Request.Path.Value?.ToLower() ?? "";
            var accept = httpContext.Request.Headers["Accept"].ToString();

            bool isApi =
                path.StartsWith("/customer/") ||
                accept.Contains("application/json");

            // ===============================
            // 4. Handle result
            // ===============================
            if (isApi)
            {
                context.Result = new UnauthorizedResult();
            }
            else
            {
                context.Result = new RedirectToActionResult(
                    "RedeemCode",
                    "Audiobook",
                    null
                );
            }
        }
    }
}