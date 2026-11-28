using AudiobookSystem.Filters;
using Microsoft.AspNetCore.Mvc;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class DashboardController : Controller
    {
        [Route("admin/dashboard-secret")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/Dashboard/Index.cshtml");
        }
    }
}
