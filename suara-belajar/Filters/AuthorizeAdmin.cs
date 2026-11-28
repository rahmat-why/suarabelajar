using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AudiobookSystem.Filters
{
    public class AuthorizeAdminAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // AllowAnonymous → skip
            if (context.ActionDescriptor.EndpointMetadata
                .Any(m => m is AllowAnonymousAttribute))
            {
                return;
            }

            var session = context.HttpContext.Session;
            bool isAdmin = session?.GetString("ADMIN_ACTIVE") == "1";

            if (isAdmin)
                return;

            // API vs Page
            var path = context.HttpContext.Request.Path.Value?.ToLower() ?? "";
            var accept = context.HttpContext.Request.Headers["Accept"].ToString();

            bool isApi =
                path.StartsWith("/admin/") &&
                accept.Contains("application/json");

            if (isApi)
            {
                context.Result = new UnauthorizedResult();
            }
            else
            {
                context.Result = new RedirectToActionResult(
                    "Index",
                    "Login",
                    null
                );
            }
        }
    }
}