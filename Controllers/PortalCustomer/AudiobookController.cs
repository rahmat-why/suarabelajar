using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Filters;
using System.Data.SqlClient;

namespace AudiobookSystem.Controllers.PortalCustomer
{
    public class AudiobookController : Controller
    {
        private readonly IConfiguration _config;

        public AudiobookController(IConfiguration config)
        {
            _config = config;
        }

        // ===============================
        // HELPER: ambil info package aktif dari cookie PACKAGE
        // (dipakai buat header layout & konten Explorer)
        // ===============================
        private object GetPackageInfo(SqlConnection conn, string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;

            using var cmd = new SqlCommand(@"
                SELECT package_id, name, logo_image, is_series
                FROM mst_package
                WHERE package_id = @PackageId
                  AND deleted_date IS NULL", conn);
            cmd.Parameters.AddWithValue("@PackageId", packageId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new
                {
                    package_id = reader["package_id"].ToString(),
                    name = reader["name"].ToString(),
                    logo_image = reader["logo_image"] == DBNull.Value ? null : reader["logo_image"].ToString(),
                    is_series = Convert.ToBoolean(reader["is_series"])
                };
            }
            return null;
        }

        // ===============================
        // VIEWS
        // ===============================

        [AllowAnonymous]
        public IActionResult RedeemCode()
        {
            return View("/Views/PortalCustomer/Audiobook/RedeemCode.cshtml");
        }

        [AllowAnonymous]
        [Route("/trial")]
        public IActionResult ExplorerTrial()
        {
            return View("/Views/PortalCustomer/Audiobook/Trial/Explorer.cshtml");
        }

        [AllowAnonymous]
        [Route("/trial/player")]
        public IActionResult TrialPlayer()
        {
            return View("/Views/PortalCustomer/Audiobook/Trial/Player.cshtml");
        }

        [AuthorizeCustomer]
        public IActionResult Explorer()
        {
            string packageId = Request.Cookies["PACKAGE"];

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            ViewBag.Package = GetPackageInfo(conn, packageId);

            return View("/Views/PortalCustomer/Audiobook/Explorer.cshtml");
        }

        [AuthorizeCustomer]
        public IActionResult Player()
        {
            string packageId = Request.Cookies["PACKAGE"];

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            ViewBag.Package = GetPackageInfo(conn, packageId);

            return View("/Views/PortalCustomer/Audiobook/Player.cshtml");
        }

        // ===============================
        // 1. REDEEM
        // ===============================
        [HttpPost]
        [AllowAnonymous]
        [Route("/customer/redeem")]
        public IActionResult Redeem([FromBody] RequestDto req)
        {
            var response = new ResponseDto();

            try
            {
                // ===============================
                // 0. RATE LIMIT (MAX 5)
                // ===============================
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                if (RedeemRateLimiter.IsLimited(ipAddress))
                {
                    response.Code = 429;
                    response.Message = "Too many attempts. Please try again later.";
                    return Json(response);
                }

                // ===============================
                // 1. Validate input
                // ===============================
                string serial = req.Data?.ToString();
                if (string.IsNullOrWhiteSpace(serial))
                {
                    response.Code = 400;
                    response.Message = "Invalid serial number";
                    return Json(response);
                }

                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection"));

                conn.Open();

                // ===============================
                // 2. Validate redeem code
                // ===============================
                string checkSql = @"
            SELECT expired_date, used_date, package_id
            FROM txn_code
            WHERE serial_number = @serial";

                string packageId;

                using (var cmd = new SqlCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@serial", serial);

                    using var reader = cmd.ExecuteReader();
                    if (!reader.Read())
                    {
                        response.Code = 404;
                        response.Message = "Code not found";
                        return Json(response);
                    }

                    if (reader["expired_date"] != DBNull.Value &&
                        Convert.ToDateTime(reader["expired_date"]) < DateTime.Now)
                    {
                        response.Code = 410;
                        response.Message = "Code expired";
                        return Json(response);
                    }

                    // Kolom "package" di txn_code sekarang menyimpan package_id
                    // (FK ke mst_package.package_id), bukan nama string lagi.
                    packageId = reader["package_id"]?.ToString();
                }

                if (string.IsNullOrWhiteSpace(packageId))
                {
                    response.Code = 500;
                    response.Message = "Code has no package assigned";
                    return Json(response);
                }

                // ===============================
                // 2b. Pastikan package masih valid & active
                // ===============================
                using (var checkPkg = new SqlCommand(
                    "SELECT COUNT(*) FROM mst_package WHERE package_id = @PackageId AND deleted_date IS NULL", conn))
                {
                    checkPkg.Parameters.AddWithValue("@PackageId", packageId);
                    int pkgExists = (int)checkPkg.ExecuteScalar();
                    if (pkgExists == 0)
                    {
                        response.Code = 400;
                        response.Message = "Package for this code is invalid or inactive";
                        return Json(response);
                    }
                }

                // ===============================
                // 3. Mark code as used
                // ===============================
                string updateSql = @"
            UPDATE txn_code
            SET used_date = GETDATE()
            WHERE serial_number = @serial";

                using (var cmd = new SqlCommand(updateSql, conn))
                {
                    cmd.Parameters.AddWithValue("@serial", serial);
                    cmd.ExecuteNonQuery();
                }

                // ===============================
                // 4. Set cookies
                // ===============================
                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.Now.AddDays(30)
                };

                Response.Cookies.Append("CUSTOMER_ACTIVE", "1", cookieOptions);
                Response.Cookies.Append("REDEEM_CODE", serial, cookieOptions);
                Response.Cookies.Append("PACKAGE", packageId, cookieOptions);

                // ===============================
                // 5. BrowserKey (persistent)
                // ===============================
                string browserKey = Request.Cookies["BrowserKey"];
                if (string.IsNullOrEmpty(browserKey))
                {
                    browserKey = Guid.NewGuid().ToString();
                    Response.Cookies.Append("BrowserKey", browserKey, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTimeOffset.Now.AddYears(1)
                    });
                }

                // ===============================
                // 6. Device & Browser Info
                // ===============================
                string deviceKey = Guid.NewGuid().ToString();
                string browserInfo = Request.Headers["User-Agent"].ToString();
                string deviceInfo =
                    $"{Request.Headers["sec-ch-ua-platform"]} | {Request.Headers["sec-ch-ua-mobile"]}";

                // ===============================
                // 7. Insert redeem & get ID
                // ===============================
                string insertRedeemSql = @"
            INSERT INTO txn_redeem
                (SerialNumber, BrowserKey, DeviceKey, RedeemedAt, BrowserInfo, DeviceInfo)
            VALUES
                (@serial, @browserKey, @deviceKey, GETDATE(), @browserInfo, @deviceInfo);

            SELECT CAST(SCOPE_IDENTITY() AS INT);
        ";

                int redeemId;
                using (var cmd = new SqlCommand(insertRedeemSql, conn))
                {
                    cmd.Parameters.AddWithValue("@serial", serial);
                    cmd.Parameters.AddWithValue("@browserKey", browserKey);
                    cmd.Parameters.AddWithValue("@deviceKey", deviceKey);
                    cmd.Parameters.AddWithValue("@browserInfo", browserInfo);
                    cmd.Parameters.AddWithValue("@deviceInfo", deviceInfo);

                    redeemId = (int)cmd.ExecuteScalar();
                }

                // ===============================
                // 8. Save redeem ID to session
                // ===============================
                HttpContext.Session.SetInt32("REDEEM_ID", redeemId);

                response.Code = 200;
                response.Message = "Redeem successful";
            }
            catch (Exception ex)
            {
                response.Code = 500;
                response.Message = ex.Message;
            }

            return Json(response);
        }

        // ===============================
        // 2. EXPLORER LOAD (unified: flat atau grouped by series,
        //    tergantung mst_package.is_series)
        // ===============================
        [AuthorizeCustomer]
        [HttpPost]
        [Route("/customer/explorer")]
        public IActionResult ExplorerLoad([FromBody] RequestDto req)
        {
            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            conn.Open();

            string packageId = Request.Cookies["PACKAGE"];
            if (string.IsNullOrEmpty(packageId))
                return Json(new ResponseDto { Code = 400, Message = "Package not found" });

            bool isSeries;
            using (var cmdPkg = new SqlCommand(@"
                SELECT is_series
                FROM mst_package
                WHERE package_id = @PackageId
                  AND deleted_date IS NULL", conn))
            {
                cmdPkg.Parameters.AddWithValue("@PackageId", packageId);
                var result = cmdPkg.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return Json(new ResponseDto { Code = 400, Message = "Invalid package" });

                isSeries = Convert.ToBoolean(result);
            }

            int skip = req.Skip < 0 ? 0 : req.Skip;

            // =========================
            // FLAT (non-series package, mis. jagobacain)
            // =========================
            if (!isSeries)
            {
                int take = req.Take > 0 ? req.Take : 8;
                var items = new List<object>();

                string sql = @"
            SELECT a.audiobook_id, a.title, a.cover_image
            FROM txn_audiobook a
            INNER JOIN txn_series s ON a.series_id = s.series_id
            WHERE s.package_id = @PackageId
              AND a.deleted_date IS NULL
              AND s.deleted_date IS NULL
            ORDER BY a.title ASC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@PackageId", packageId);
                cmd.Parameters.AddWithValue("@Skip", skip);
                cmd.Parameters.AddWithValue("@Take", take);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new
                    {
                        audiobook_id = reader["audiobook_id"].ToString(),
                        title = reader["title"].ToString(),
                        cover_image = reader["cover_image"].ToString()
                    });
                }

                return Json(new ResponseDto { Code = 200, Data = items });
            }

            // =========================
            // GROUPED BY SERIES (mis. islambercerita)
            // =========================
            {
                int take = req.Take > 0 ? req.Take : 2; // jumlah series per page

                string sql = @"
            ;WITH SeriesPaged AS (
                SELECT series_id, name, sequence
                FROM txn_series
                WHERE package_id = @PackageId
                  AND deleted_date IS NULL
                ORDER BY sequence
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            )
            SELECT 
                s.series_id,
                s.name AS series_name,
                a.audiobook_id,
                a.title,
                a.cover_image
            FROM SeriesPaged s
            LEFT JOIN txn_audiobook a
                ON s.series_id = a.series_id
                AND a.deleted_date IS NULL
            ORDER BY s.sequence, a.created_date DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@PackageId", packageId);
                cmd.Parameters.AddWithValue("@Skip", skip);
                cmd.Parameters.AddWithValue("@Take", take);

                using var reader = cmd.ExecuteReader();

                var seriesMap = new Dictionary<string, dynamic>();

                while (reader.Read())
                {
                    string seriesId = reader["series_id"].ToString();

                    if (!seriesMap.ContainsKey(seriesId))
                    {
                        seriesMap[seriesId] = new
                        {
                            series_id = seriesId,
                            series_name = reader["series_name"].ToString(),
                            items = new List<object>()
                        };
                    }

                    if (reader["audiobook_id"] != DBNull.Value)
                    {
                        seriesMap[seriesId].items.Add(new
                        {
                            audiobook_id = reader["audiobook_id"].ToString(),
                            title = reader["title"].ToString(),
                            cover_image = reader["cover_image"].ToString()
                        });
                    }
                }

                return Json(new ResponseDto { Code = 200, Data = seriesMap.Values.ToList() });
            }
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("/trial/explorer")]
        public IActionResult ExplorerTrial([FromBody] RequestDto req)
        {
            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            conn.Open();

            int skip = req.Skip < 0 ? 0 : req.Skip;

            int take = 8;
            var items = new List<object>();

            string sql = @"
        SELECT audiobook_id, title, cover_image
        FROM txn_audiobook_trial";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@Take", take);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new
                {
                    audiobook_id = reader["audiobook_id"].ToString(),
                    title = reader["title"].ToString(),
                    cover_image = reader["cover_image"].ToString()
                });
            }

            return Json(new ResponseDto
            {
                Code = 200,
                Data = items
            });
        }

        // ===============================
        // 3. PLAYER DATA (unified)
        // ===============================
        [AuthorizeCustomer]
        [HttpPost]
        [Route("/customer/player")]
        public IActionResult PlayerLoad([FromBody] RequestDto req)
        {
            string id = req.Data?.ToString();
            if (string.IsNullOrEmpty(id))
                return Json(new ResponseDto
                {
                    Code = 400,
                    Message = "Invalid audiobook id"
                });

            string packageId = Request.Cookies["PACKAGE"];

            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            conn.Open();

            // Join lewat series -> package supaya audiobook yg diminta
            // dipastikan memang milik package yg lagi aktif di cookie.
            string sql = @"
        SELECT a.audiobook_id, a.title, a.description, a.cover_image, a.duration
        FROM txn_audiobook a
        INNER JOIN txn_series s ON a.series_id = s.series_id
        WHERE a.audiobook_id = @id
          AND a.deleted_date IS NULL
          AND s.package_id = @PackageId";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@PackageId", (object)packageId ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return Json(new ResponseDto
                {
                    Code = 404,
                    Message = "Audiobook not found"
                });

            return Json(new ResponseDto
            {
                Code = 200,
                Message = "Success",
                Data = new
                {
                    audiobook_id = reader["audiobook_id"].ToString(),
                    title = reader["title"].ToString(),
                    description = reader["description"].ToString(),
                    cover_image = reader["cover_image"].ToString(),
                    duration = reader["duration"] != DBNull.Value
                        ? reader["duration"].ToString()
                        : "00:00"
                }
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("/trial/player")]
        public IActionResult TrialPlayerLoad([FromBody] RequestDto req)
        {
            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            conn.Open();

            string sql;

            // default: trial
            sql = @"
        SELECT audiobook_id, title, description, cover_image, duration
        FROM txn_audiobook_trial";

            using var cmd = new SqlCommand(sql, conn);

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return Json(new ResponseDto
                {
                    Code = 404,
                    Message = "Audiobook not found"
                });

            return Json(new ResponseDto
            {
                Code = 200,
                Message = "Success",
                Data = new
                {
                    audiobook_id = reader["audiobook_id"].ToString(),
                    title = reader["title"].ToString(),
                    description = reader["description"].ToString(),
                    cover_image = reader["cover_image"].ToString(),
                    duration = reader["duration"] != DBNull.Value
                        ? reader["duration"].ToString()
                        : "00:00"
                }
            });
        }

        // ===============================
        // 4. STREAM AUDIO (unified: folder = mst_package.name)
        // ===============================
        [AuthorizeCustomer]
        [HttpGet]
        [Route("/customer/playaudio/{id}")]
        public IActionResult PlayAudio(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return NotFound();

            if (!Request.Cookies.TryGetValue("BrowserKey", out var browserKey))
                return Unauthorized();

            string packageId = Request.Cookies["PACKAGE"];
            string packageName;

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using (var cmd = new SqlCommand("SELECT COUNT(1) FROM txn_redeem WHERE BrowserKey=@b", conn))
                {
                    cmd.Parameters.AddWithValue("@b", browserKey);
                    if ((int)cmd.ExecuteScalar() == 0) return Unauthorized();
                }

                // Ambil nama folder (mst_package.name) lewat audiobook -> series -> package,
                // sekaligus pastikan audiobook ini emang milik package aktif di cookie.
                using var cmdPkg = new SqlCommand(@"
                    SELECT p.name
                    FROM txn_audiobook a
                    INNER JOIN txn_series s ON a.series_id = s.series_id
                    INNER JOIN mst_package p ON s.package_id = p.package_id
                    WHERE a.audiobook_id = @id
                      AND s.package_id = @PackageId
                      AND a.deleted_date IS NULL", conn);
                cmdPkg.Parameters.AddWithValue("@id", id);
                cmdPkg.Parameters.AddWithValue("@PackageId", (object)packageId ?? DBNull.Value);

                var result = cmdPkg.ExecuteScalar();
                if (result == null || result == DBNull.Value) return NotFound();

                packageName = result.ToString();
            }
            catch { return StatusCode(500); }

            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Audiobook", "audio", packageName, $"{id}.mp3");

            if (!System.IO.File.Exists(path)) return NotFound();

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            // EnableRangeProcessing handles the 206 Partial Content status for iOS
            return new FileStreamResult(stream, "audio/mpeg")
            {
                EnableRangeProcessing = true,
                LastModified = System.IO.File.GetLastWriteTimeUtc(path),
                EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{id}\"")
            };
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("/trial/playaudio/{id}")]
        public IActionResult TrialPlayAudio(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return NotFound();

            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Audiobook", "audio", "trial", $"{id}.mp3");

            if (!System.IO.File.Exists(path)) return NotFound();

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            // EnableRangeProcessing handles the 206 Partial Content status for iOS
            return new FileStreamResult(stream, "audio/mpeg")
            {
                EnableRangeProcessing = true,
                LastModified = System.IO.File.GetLastWriteTimeUtc(path),
                EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{id}\"")
            };
        }

        [HttpGet("/customer/session-check")]
        [AllowAnonymous]
        public IActionResult CheckSession()
        {
            bool active = Request.Cookies["CUSTOMER_ACTIVE"] == "1";
            return Json(new { active });
        }

        [HttpPost]
        [Route("/customer/logout")]
        public IActionResult Logout()
        {
            int? redeemId = HttpContext.Session.GetInt32("REDEEM_ID");

            if (redeemId.HasValue)
            {
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection"));

                conn.Open();

                string sql = @"
            UPDATE txn_redeem
            SET LogoutAt = GETDATE()
            WHERE Id = @id
              AND LogoutAt IS NULL";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", redeemId.Value);
                cmd.ExecuteNonQuery();
            }

            // Clear session
            HttpContext.Session.Remove("REDEEM_ID");

            // Clear cookies
            Response.Cookies.Delete("CUSTOMER_ACTIVE");
            Response.Cookies.Delete("REDEEM_CODE");
            Response.Cookies.Delete("PACKAGE");
            Response.Cookies.Delete("BrowserKey");

            return RedirectToAction("RedeemCode", "Audiobook");
        }
    }
}