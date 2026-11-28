using Microsoft.AspNetCore.Mvc;

namespace suara_belajar.Models
{
    public class MonitoringDto : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
