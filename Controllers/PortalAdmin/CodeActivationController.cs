using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Text.Json;

namespace AudiobookSystem.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class CodeActivationController : Controller
    {
        private readonly IConfiguration _config;

        public CodeActivationController(IConfiguration config)
        {
            _config = config;
        }

        [Route("admin/codeactivation")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/CodeActivation/Index.cshtml");
        }

        [Route("admin/codeactivation/generate")]
        public IActionResult Generate()
        {
            return View("~/Views/PortalAdmin/CodeActivation/Generate.cshtml");
        }

        [HttpPost]
        [Route("admin/codeactivation/generate")]
        public IActionResult Generate([FromBody] RequestDto request)
        {
            if (!int.TryParse(request.Data?.ToString(), out int total) || total < 1)
            {
                return Json(new ResponseDto
                {
                    Code = 400,
                    Message = "Invalid number"
                });
            }

            string package = request.Package?.ToString();
            if (string.IsNullOrEmpty(package))
                package = "jagobacain";

            var codes = new List<string>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            for (int i = 0; i < total; i++)
            {
                // === INTERNAL ID (20 char max) ===
                string codeId = GenerateRandom(20);

                // === SERIAL NUMBER (USER) ===
                string serial = "SB-" + GenerateRandom(8);

                using var cmd = new SqlCommand(@"
            INSERT INTO txn_code (code_id, serial_number, package_id)
            VALUES (@id, @sn, @pkg)", conn);

                cmd.Parameters.AddWithValue("@id", codeId);   // <= 20 char
                cmd.Parameters.AddWithValue("@sn", serial);   // SB-XXXXXXXX
                cmd.Parameters.AddWithValue("@pkg", package);

                cmd.ExecuteNonQuery();
                codes.Add(serial);
            }

            return Json(new ResponseDto
            {
                Code = 200,
                Message = $"{total} codes generated",
                Data = codes
            });
        }

        private static string GenerateRandom(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rand = new Random();
            return new string(Enumerable.Range(0, length)
                .Select(_ => chars[rand.Next(chars.Length)]).ToArray());
        }


        [HttpPost]
        [Route("admin/codeactivation/load")]
        public IActionResult LoadCodes([FromBody] RequestDto req)
        {
            int totalRecords = 0;
            int filteredRecords = 0;
            int totalActive = 0;
            int totalNotActive = 0;

            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                // ==========================================
                // 1. Total records (NO FILTER)
                // ==========================================
                using (var cmd = new SqlCommand(@"
            SELECT COUNT(*)
            FROM txn_code
        ", conn))
                {
                    totalRecords = Convert.ToInt32(cmd.ExecuteScalar());
                }


                // ==========================================
                // 2. Request params
                // ==========================================
                string search = req.Data?.ToString() ?? "";
                string statusFilter = req.Status?.ToString() ?? "";
                string packageFilter = req.Package?.ToString() ?? "";


                // ==========================================
                // 3. WHERE clause
                // ==========================================
                string whereClause = @"
            WHERE c.serial_number LIKE @Search
        ";

                // Filter berdasarkan package_id
                if (!string.IsNullOrEmpty(packageFilter))
                {
                    whereClause += @"
                AND c.package_id = @Package
            ";
                }

                // Filter status
                if (statusFilter == "Active")
                {
                    whereClause += @"
                AND c.used_date IS NULL
            ";
                }
                else if (statusFilter == "Not Active")
                {
                    whereClause += @"
                AND c.used_date IS NOT NULL
            ";
                }


                // ==========================================
                // 4. Filtered records
                // ==========================================
                using (var cmd = new SqlCommand($@"
            SELECT COUNT(*)
            FROM txn_code c
            LEFT JOIN mst_package p
                ON p.package_id = c.package_id

            {whereClause}
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@Search",
                        $"%{search}%"
                    );

                    if (!string.IsNullOrEmpty(packageFilter))
                    {
                        cmd.Parameters.AddWithValue(
                            "@Package",
                            packageFilter
                        );
                    }

                    filteredRecords = Convert.ToInt32(
                        cmd.ExecuteScalar()
                    );
                }


                // ==========================================
                // 5. Active / Not Active totals
                // ==========================================
                using (var cmd = new SqlCommand($@"
            SELECT
                SUM(
                    CASE
                        WHEN c.used_date IS NULL
                        THEN 1
                        ELSE 0
                    END
                ) AS ActiveCount,

                SUM(
                    CASE
                        WHEN c.used_date IS NOT NULL
                        THEN 1
                        ELSE 0
                    END
                ) AS NotActiveCount

            FROM txn_code c

            LEFT JOIN mst_package p
                ON p.package_id = c.package_id

            {whereClause}
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@Search",
                        $"%{search}%"
                    );

                    if (!string.IsNullOrEmpty(packageFilter))
                    {
                        cmd.Parameters.AddWithValue(
                            "@Package",
                            packageFilter
                        );
                    }

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        totalActive =
                            reader["ActiveCount"] != DBNull.Value
                                ? Convert.ToInt32(reader["ActiveCount"])
                                : 0;

                        totalNotActive =
                            reader["NotActiveCount"] != DBNull.Value
                                ? Convert.ToInt32(reader["NotActiveCount"])
                                : 0;
                    }
                }


                // ==========================================
                // 6. Paginated data
                // ==========================================
                string sql = $@"
            SELECT
                c.code_id,
                c.serial_number,
                c.expired_date,
                c.created_date,
                c.used_date,

                c.package_id,

                p.name AS package_name,

                CASE
                    WHEN c.used_date IS NULL
                    THEN 'Active'
                    ELSE 'Not Active'
                END AS Status

            FROM txn_code c

            LEFT JOIN mst_package p
                ON p.package_id = c.package_id

            {whereClause}

            ORDER BY c.created_date DESC

            OFFSET @Skip ROWS
            FETCH NEXT @Take ROWS ONLY
        ";


                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@Search",
                        $"%{search}%"
                    );

                    cmd.Parameters.AddWithValue(
                        "@Skip",
                        req.Skip
                    );

                    cmd.Parameters.AddWithValue(
                        "@Take",
                        req.Take
                    );

                    if (!string.IsNullOrEmpty(packageFilter))
                    {
                        cmd.Parameters.AddWithValue(
                            "@Package",
                            packageFilter
                        );
                    }


                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        list.Add(new
                        {
                            code_id = reader["code_id"],

                            serial_number =
                                reader["serial_number"],

                            package_id =
                                reader["package_id"],

                            package =
                                reader["package_name"],

                            expired_date =
                                reader["expired_date"],

                            created_date =
                                reader["created_date"],

                            used_date =
                                reader["used_date"],

                            status =
                                reader["Status"]
                        });
                    }
                }


                // ==========================================
                // 7. Response
                // ==========================================
                return Json(new
                {
                    draw = req.Draw,

                    recordsTotal =
                        totalRecords,

                    recordsFiltered =
                        filteredRecords,

                    totalActive,

                    totalNotActive,

                    data = list
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    draw = req.Draw,

                    recordsTotal = 0,

                    recordsFiltered = 0,

                    totalActive = 0,

                    totalNotActive = 0,

                    data = new List<object>(),

                    error = ex.Message
                });
            }
        }

        [HttpPost]
        [Route("admin/codeactivation/log")]
        public IActionResult GetRedeemLog([FromBody] JsonElement req)
        {
            // Extract SerialNumber safely
            string sn = req.GetProperty("SerialNumber").GetString();

            var list = new List<object>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            using var cmd = new SqlCommand(@"
        SELECT BrowserKey, RedeemedAt
        FROM txn_redeem
        WHERE SerialNumber = @SN
        ORDER BY RedeemedAt DESC", conn);
            cmd.Parameters.AddWithValue("@SN", sn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    BrowserKey = reader["BrowserKey"],
                    RedeemedAt = reader["RedeemedAt"]
                });
            }

            return Json(new { data = list });
        }

        [HttpGet]
        [Route("admin/codeactivation/packages")]
        public IActionResult GetPackages()
        {
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                string sql = @"
            SELECT 
                package_id,
                name
            FROM mst_package
            WHERE deleted_date IS NULL
            ORDER BY name ASC
        ";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new
                    {
                        package_id = reader["package_id"],
                        name = reader["name"]
                    });
                }

                return Json(new
                {
                    success = true,
                    data = list
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    data = new List<object>(),
                    error = ex.Message
                });
            }
        }
    }
}