using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Filters;
using System.Data.SqlClient;
using System.Linq;

namespace AudiobookSystem.Controllers.PortalCustomer
{
    public class AudiobookController : Controller
    {
        // ===============================
        // STYLING VERSION CONSTANTS
        // (must match mst_package.explorer_styling_version values)
        // ===============================
        private const string NONSERIES_V1 = "NONSERIES_V1";
        private const string SERIES_V1 = "SERIES_V1";
        private const string SERIES_V2 = "SERIES_V2";

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
                SELECT package_id, name, logo_image, is_series, explorer_styling_version
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
                    is_series = Convert.ToBoolean(reader["is_series"]),
                    explorer_styling_version = reader["explorer_styling_version"] == DBNull.Value
                        ? null
                        : reader["explorer_styling_version"].ToString()
                };
            }
            return null;
        }

        // ===============================
        // HELPER: pilih view Explorer berdasarkan is_series + explorer_styling_version
        // ===============================
        private string ResolveExplorerViewPath(dynamic package)
        {
            // Fallback kalau package tidak ditemukan / belum di-set versinya
            if (package == null)
            {
                return "/Views/PortalCustomer/Audiobook/ExplorerNonSeriesV1.cshtml";
            }

            bool isSeries = package.is_series;
            string version = package.explorer_styling_version;

            if (!isSeries)
            {
                // Baru ada 1 styling utk non-series
                return "/Views/PortalCustomer/Audiobook/ExplorerNonSeriesV1.cshtml";
            }

            switch (version)
            {
                case SERIES_V2:
                    return "/Views/PortalCustomer/Audiobook/ExplorerSeriesV2.cshtml";

                case SERIES_V1:
                default:
                    return "/Views/PortalCustomer/Audiobook/ExplorerSeriesV1.cshtml";
            }
        }

        // ===============================
        // HELPER: resolve code_id from REDEEM_CODE cookie
        // (same lookup used in StartListening)
        // ===============================
        private string ResolveCodeId(SqlConnection conn)
        {
            string serial = Request.Cookies["REDEEM_CODE"];
            if (string.IsNullOrWhiteSpace(serial)) return null;

            using var cmd = new SqlCommand(
                "SELECT code_id FROM txn_code WHERE serial_number = @serial", conn);
            cmd.Parameters.AddWithValue("@serial", serial);

            var result = cmd.ExecuteScalar();
            return (result == null || result == DBNull.Value) ? null : result.ToString();
        }

        // ===============================
        // HELPER: progress % for one series, for this customer
        //   listening 50% + reading 20% + quiz-pass 30%
        //   (only used by SERIES_V2 explorer)
        // ===============================
        private int CalculateSeriesProgress(SqlConnection conn, string codeId, string seriesId)
        {
            if (string.IsNullOrWhiteSpace(codeId)) return 0;

            int totalAudiobooks;
            using (var cmd = new SqlCommand(@"
                SELECT COUNT(*) FROM txn_audiobook
                WHERE series_id = @SeriesId AND deleted_date IS NULL", conn))
            {
                cmd.Parameters.AddWithValue("@SeriesId", seriesId);
                totalAudiobooks = (int)cmd.ExecuteScalar();
            }

            if (totalAudiobooks == 0) return 0;

            int listeningDone;
            using (var cmd = new SqlCommand(@"
                SELECT COUNT(DISTINCT tl.audiobook_id)
                FROM txn_listening tl
                INNER JOIN txn_audiobook a ON a.audiobook_id = tl.audiobook_id
                WHERE a.series_id = @SeriesId
                  AND tl.code_id = @CodeId
                  AND tl.finish_date IS NOT NULL", conn))
            {
                cmd.Parameters.AddWithValue("@SeriesId", seriesId);
                cmd.Parameters.AddWithValue("@CodeId", codeId);
                listeningDone = (int)cmd.ExecuteScalar();
            }

            int readingDone;
            using (var cmd = new SqlCommand(@"
                SELECT COUNT(DISTINCT tr.audiobook_id)
                FROM txn_reading tr
                INNER JOIN txn_audiobook a ON a.audiobook_id = tr.audiobook_id
                WHERE a.series_id = @SeriesId
                  AND tr.code_id = @CodeId
                  AND tr.finish_date IS NOT NULL", conn))
            {
                cmd.Parameters.AddWithValue("@SeriesId", seriesId);
                cmd.Parameters.AddWithValue("@CodeId", codeId);
                readingDone = (int)cmd.ExecuteScalar();
            }

            bool quizPassed;
            using (var cmd = new SqlCommand(@"
                SELECT COUNT(*) FROM txn_assessment
                WHERE series_id = @SeriesId
                  AND code_id = @CodeId
                  AND is_pass = 1", conn))
            {
                cmd.Parameters.AddWithValue("@SeriesId", seriesId);
                cmd.Parameters.AddWithValue("@CodeId", codeId);
                quizPassed = (int)cmd.ExecuteScalar() > 0;
            }

            double listeningPct = (double)listeningDone / totalAudiobooks;
            double readingPct = (double)readingDone / totalAudiobooks;
            double quizPct = quizPassed ? 1.0 : 0.0;

            double progress = (listeningPct * 50) + (readingPct * 20) + (quizPct * 30);

            return (int)Math.Round(progress);
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

            dynamic package = GetPackageInfo(conn, packageId);
            ViewBag.Package = package;

            string viewPath = ResolveExplorerViewPath(package);

            return View(viewPath);
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
                    Secure = false,
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
        // EXPLORER LOAD
        // Dispatches by (is_series, explorer_styling_version):
        //   - !is_series           -> LoadNonSeriesV1
        //   - is_series, SERIES_V1 -> LoadSeriesV1
        //   - is_series, SERIES_V2 -> LoadSeriesV2 (includes per-series progress)
        // ===============================
        [AuthorizeCustomer]
        [HttpPost]
        [Route("/customer/explorer")]
        public IActionResult ExplorerLoad([FromBody] RequestDto req)
        {
            try
            {
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                // ===============================
                // GET PACKAGE ID
                // ===============================
                string packageId = Request.Cookies["PACKAGE"];

                if (string.IsNullOrWhiteSpace(packageId))
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Package not found."
                    });
                }

                // ===============================
                // GET PACKAGE TYPE + STYLING VERSION
                // ===============================
                bool isSeries;
                string stylingVersion;

                using (var cmd = new SqlCommand(@"
            SELECT is_series, explorer_styling_version
            FROM mst_package
            WHERE package_id = @PackageId
              AND deleted_date IS NULL
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@PackageId",
                        packageId
                    );

                    using var reader = cmd.ExecuteReader();

                    if (!reader.Read())
                    {
                        return Json(new ResponseDto
                        {
                            Code = 404,
                            Message = "Invalid package."
                        });
                    }

                    isSeries = Convert.ToBoolean(reader["is_series"]);
                    stylingVersion = reader["explorer_styling_version"] == DBNull.Value
                        ? null
                        : reader["explorer_styling_version"].ToString();
                }

                int skip = req.Skip < 0
                    ? 0
                    : req.Skip;

                int take = req.Take;

                // =========================================================
                // NON-SERIES PACKAGE
                // =========================================================
                if (!isSeries)
                {
                    return LoadNonSeriesV1(conn, packageId, skip, take);
                }

                // =========================================================
                // SERIES PACKAGE — dispatch by styling version
                // =========================================================
                switch (stylingVersion)
                {
                    case SERIES_V2:
                        return LoadSeriesV2(conn, packageId, skip, take);

                    case SERIES_V1:
                    default:
                        return LoadSeriesV1(conn, packageId, skip, take);
                }
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto
                {
                    Code = 500,
                    Message = ex.Message
                });
            }
        }

        // =========================================================
        // NONSERIES_V1: flat audiobook grid
        // =========================================================
        private IActionResult LoadNonSeriesV1(SqlConnection conn, string packageId, int skip, int reqTake)
        {
            int take = reqTake > 0 ? reqTake : 8;

            var items = new List<object>();

            using var cmd = new SqlCommand(@"
                SELECT
                    a.audiobook_id,
                    a.title,
                    a.cover_image
                FROM txn_audiobook a
                INNER JOIN txn_series s
                    ON a.series_id = s.series_id
                WHERE s.package_id = @PackageId
                  AND a.deleted_date IS NULL
                  AND s.deleted_date IS NULL
                ORDER BY a.title ASC
                OFFSET @Skip ROWS
                FETCH NEXT @Take ROWS ONLY
            ", conn);

            cmd.Parameters.AddWithValue("@PackageId", packageId);
            cmd.Parameters.AddWithValue("@Skip", skip);
            cmd.Parameters.AddWithValue("@Take", take);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new
                {
                    item_type = "audiobook",
                    audiobook_id = reader["audiobook_id"]?.ToString(),
                    title = reader["title"]?.ToString(),
                    cover_image = reader["cover_image"]?.ToString()
                });
            }

            return Json(new ResponseDto
            {
                Code = 200,
                Message = "Success",
                Data = items
            });
        }

        // =========================================================
        // SERIES_V1: each series contains audiobooks + trailing quiz
        // =========================================================
        private IActionResult LoadSeriesV1(SqlConnection conn, string packageId, int skip, int reqTake)
        {
            int seriesTake = reqTake > 0 ? reqTake : 2;

            var seriesList = new List<dynamic>();

            // =========================================================
            // GET SERIES
            // =========================================================
            using (var cmd = new SqlCommand(@"
            SELECT
                series_id,
                name,
                cover_image,
                sequence
            FROM txn_series
            WHERE package_id = @PackageId
              AND deleted_date IS NULL
            ORDER BY sequence
            OFFSET @Skip ROWS
            FETCH NEXT @Take ROWS ONLY
        ", conn))
            {
                cmd.Parameters.AddWithValue("@PackageId", packageId);
                cmd.Parameters.AddWithValue("@Skip", skip);
                cmd.Parameters.AddWithValue("@Take", seriesTake);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    seriesList.Add(new
                    {
                        series_id = reader["series_id"]?.ToString(),
                        series_name = reader["name"]?.ToString(),
                        cover_image = reader["cover_image"] == DBNull.Value
                            ? null
                            : reader["cover_image"]?.ToString(),
                        sequence = reader["sequence"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["sequence"]),
                        items = new List<object>()
                    });
                }
            }

            // =========================================================
            // GET AUDIOBOOK + QUIZ FOR EACH SERIES
            // =========================================================
            foreach (var series in seriesList)
            {
                // =====================================================
                // GET AUDIOBOOKS
                // =====================================================
                using (var cmd = new SqlCommand(@"
                SELECT
                    audiobook_id,
                    title,
                    cover_image
                FROM txn_audiobook
                WHERE series_id = @SeriesId
                  AND deleted_date IS NULL
                ORDER BY created_date DESC
            ", conn))
                {
                    cmd.Parameters.AddWithValue("@SeriesId", series.series_id);

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        series.items.Add(new
                        {
                            item_type = "audiobook",
                            audiobook_id = reader["audiobook_id"]?.ToString(),
                            title = reader["title"]?.ToString(),
                            cover_image = reader["cover_image"]?.ToString()
                        });
                    }
                }

                // =====================================================
                // GET QUIZ BASED ON SERIES
                // =====================================================
                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1
                        quiz_id,
                        title,
                        minimum_point,
                        notes1,
                        notes2
                    FROM mst_quiz
                    WHERE series_id = @SeriesId
                    ORDER BY quiz_id
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@SeriesId", series.series_id);

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        // =================================================
                        // APPEND QUIZ AS LAST ITEM
                        // =================================================
                        series.items.Add(new
                        {
                            item_type = "quiz",
                            quiz_id = reader["quiz_id"]?.ToString(),
                            title = reader["title"]?.ToString(),
                            minimum_point = reader["minimum_point"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(reader["minimum_point"]),
                            notes1 = reader["notes1"] == DBNull.Value
                                ? null
                                : reader["notes1"]?.ToString(),
                            notes2 = reader["notes2"] == DBNull.Value
                                ? null
                                : reader["notes2"]?.ToString()
                        });
                    }
                }
            }

            return Json(new ResponseDto
            {
                Code = 200,
                Message = "Success",
                Data = seriesList
            });
        }

        // =========================================================
        // SERIES_V2: tab-style explorer (pills switch which series'
        // items are shown). Same series+items shape as V1, PLUS a
        // per-series "progress" field:
        //   progress = (listening% * 50) + (reading% * 20) + (quizPass ? 30 : 0)
        //
        // IMPORTANT: the Select(...) projection below is executed
        // eagerly with .ToList() BEFORE the method returns. LINQ
        // queries are lazy by default — if we passed the raw
        // IEnumerable into Data, CalculateSeriesProgress would only
        // run during JSON serialization, which happens *after* this
        // method (and its `using var conn`) has already returned and
        // closed the connection, causing:
        //   "ExecuteScalar requires an open and available Connection."
        // =========================================================
        private IActionResult LoadSeriesV2(SqlConnection conn, string packageId, int skip, int reqTake)
        {
            int seriesTake = reqTake > 0 ? reqTake : 2;
            string codeId = ResolveCodeId(conn);

            var seriesList = new List<dynamic>();

            // =========================================================
            // GET SERIES
            // =========================================================
            using (var cmd = new SqlCommand(@"
                SELECT
                    series_id,
                    name,
                    sequence
                FROM txn_series
                WHERE package_id = @PackageId
                  AND deleted_date IS NULL
                ORDER BY sequence
                OFFSET @Skip ROWS
                FETCH NEXT @Take ROWS ONLY
            ", conn))
            {
                cmd.Parameters.AddWithValue("@PackageId", packageId);
                cmd.Parameters.AddWithValue("@Skip", skip);
                cmd.Parameters.AddWithValue("@Take", seriesTake);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    seriesList.Add(new
                    {
                        series_id = reader["series_id"]?.ToString(),
                        series_name = reader["name"]?.ToString(),
                        sequence = reader["sequence"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["sequence"]),
                        items = new List<object>()
                    });
                }
            }

            // =========================================================
            // GET AUDIOBOOK + QUIZ FOR EACH SERIES
            // =========================================================
            foreach (var series in seriesList)
            {
                using (var cmd = new SqlCommand(@"
                    SELECT
                        audiobook_id,
                        title,
                        cover_image
                    FROM txn_audiobook
                    WHERE series_id = @SeriesId
                      AND deleted_date IS NULL
                    ORDER BY created_date DESC
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@SeriesId", series.series_id);

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        series.items.Add(new
                        {
                            item_type = "audiobook",
                            audiobook_id = reader["audiobook_id"]?.ToString(),
                            title = reader["title"]?.ToString(),
                            cover_image = reader["cover_image"]?.ToString()
                        });
                    }
                }

                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1
                        quiz_id,
                        title,
                        minimum_point,
                        notes1,
                        notes2
                    FROM mst_quiz
                    WHERE series_id = @SeriesId
                    ORDER BY quiz_id
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@SeriesId", series.series_id);

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        series.items.Add(new
                        {
                            item_type = "quiz",
                            quiz_id = reader["quiz_id"]?.ToString(),
                            title = reader["title"]?.ToString(),
                            minimum_point = reader["minimum_point"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(reader["minimum_point"]),
                            notes1 = reader["notes1"] == DBNull.Value
                                ? null
                                : reader["notes1"]?.ToString(),
                            notes2 = reader["notes2"] == DBNull.Value
                                ? null
                                : reader["notes2"]?.ToString()
                        });
                    }
                }
            }

            // =========================================================
            // ATTACH PROGRESS PER SERIES
            // .ToList() forces this to run NOW, while conn is open —
            // see note above the method signature.
            // =========================================================
            var result = seriesList.Select(s => new
            {
                series_id = s.series_id,
                series_name = s.series_name,
                sequence = s.sequence,
                progress = CalculateSeriesProgress(conn, codeId, (string)s.series_id),
                items = s.items
            }).ToList();

            return Json(new ResponseDto
            {
                Code = 200,
                Message = "Success",
                Data = result
            });
        }

        // ===============================
        // LISTENING TRACKING (txn_listening)
        // ===============================

        // Called when playback actually starts (fresh audio.src load).
        // Resolves code_id from the REDEEM_CODE cookie (serial number)
        // against txn_code, then inserts a new open listening session.
        [AuthorizeCustomer]
        [HttpPost]
        [Route("/customer/listening/start")]
        public IActionResult StartListening([FromBody] RequestDto req)
        {
            string audiobookId = req.Data?.ToString();

            if (string.IsNullOrWhiteSpace(audiobookId))
                return Json(new ResponseDto
                {
                    Code = 400,
                    Message = "Invalid audiobook id"
                });

            string serial = Request.Cookies["REDEEM_CODE"];

            if (string.IsNullOrWhiteSpace(serial))
                return Json(new ResponseDto
                {
                    Code = 400,
                    Message = "Redeem code not found"
                });

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string codeId;

                using (var cmd = new SqlCommand(
                    "SELECT code_id FROM txn_code WHERE serial_number = @serial", conn))
                {
                    cmd.Parameters.AddWithValue("@serial", serial);

                    var result = cmd.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                        return Json(new ResponseDto
                        {
                            Code = 404,
                            Message = "Code not found"
                        });

                    codeId = result.ToString();
                }

                string listeningId = Guid.NewGuid().ToString();

                using (var cmd = new SqlCommand(@"
                    INSERT INTO txn_listening (listening_id, code_id, audiobook_id, start_date)
                    VALUES (@listeningId, @codeId, @audiobookId, GETDATE());
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@listeningId", listeningId);
                    cmd.Parameters.AddWithValue("@codeId", codeId);
                    cmd.Parameters.AddWithValue("@audiobookId", audiobookId);

                    cmd.ExecuteNonQuery();
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = new { listening_id = listeningId }
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto
                {
                    Code = 500,
                    Message = ex.Message
                });
            }
        }

        // Called when playback finishes naturally (audio "ended" event).
        // Only stamps finish_date once — a session that's already
        // finished is left alone.
        [AuthorizeCustomer]
        [HttpPost]
        [Route("/customer/listening/finish")]
        public IActionResult FinishListening([FromBody] RequestDto req)
        {
            string listeningId = req.Data?.ToString();

            if (string.IsNullOrWhiteSpace(listeningId))
                return Json(new ResponseDto
                {
                    Code = 400,
                    Message = "Invalid listening id"
                });

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(@"
                    UPDATE txn_listening
                    SET finish_date = GETDATE()
                    WHERE listening_id = @id
                      AND finish_date IS NULL
                ", conn);

                cmd.Parameters.AddWithValue("@id", listeningId);
                cmd.ExecuteNonQuery();

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success"
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto
                {
                    Code = 500,
                    Message = ex.Message
                });
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
            // LEFT JOIN mst_summary buat nentuin has_reading (reading
            // content disimpan per audiobook_id di sana).
            string sql = @"
        SELECT a.audiobook_id, a.title, a.description, a.cover_image, a.duration,
               CASE WHEN sm.summary_id IS NOT NULL THEN 1 ELSE 0 END AS has_reading
        FROM txn_audiobook a
        INNER JOIN txn_series s ON a.series_id = s.series_id
        LEFT JOIN mst_summary sm ON sm.audiobook_id = a.audiobook_id
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
                        : "00:00",
                    has_reading = Convert.ToBoolean(reader["has_reading"])
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

            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Audiobook", "audio", packageId, $"{id}.mp3");

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