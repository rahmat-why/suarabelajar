using AudiobookSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Models;
using System.Data.SqlClient;

namespace suara_belajar.Controllers
{
    public class LoginController : Controller
    {
        private readonly IConfiguration _config;

        public LoginController(IConfiguration config)
        {
            _config = config;
        }

        [AllowAnonymous]
        [HttpGet("/login")]
        public IActionResult Index()
        {
            return View("/Views/PortalAdmin/Login.cshtml");
        }

        [AllowAnonymous]
        [HttpPost("/login")]
        public IActionResult Login([FromBody] LoginDto req)
        {
            var response = new ResponseDto();

            if (string.IsNullOrEmpty(req?.Username) ||
                string.IsNullOrEmpty(req.Password))
            {
                response.Code = 400;
                response.Message = "Username and password are required";
                return Json(response);
            }

            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));

            conn.Open();

            string sql = @"
                SELECT user_id, name, username
                FROM mst_user
                WHERE username = @username AND password = @password";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@username", req.Username);
            cmd.Parameters.AddWithValue("@password", req.Password);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                response.Code = 401;
                response.Message = "Invalid username or password";
                return Json(response);
            }

            // ✅ CREATE ADMIN SESSION
            HttpContext.Session.SetString("ADMIN_ACTIVE", "1");
            HttpContext.Session.SetString("ADMIN_USER", reader["username"].ToString());

            response.Code = 200;
            response.Message = "Login successful";

            return Json(response);
        }

        [HttpGet("/logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index");
        }
    }
}