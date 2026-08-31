using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using suara_belajar.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class SeriesController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        public SeriesController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        [Route("admin/series/index")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/Series/Index.cshtml");
        }

        [Route("admin/series/create")]
        public IActionResult Create()
        {
            return View("~/Views/PortalAdmin/Series/Create.cshtml");
        }

        [Route("admin/series/edit/{id}")]
        public IActionResult Edit(string id)
        {
            return View("~/Views/PortalAdmin/Series/Edit.cshtml");
        }

        // ===============================
        // LOAD (list, search, filter status + package, pagination)
        // ===============================
        [HttpPost]
        [Route("admin/series/load")]
        public IActionResult Load([FromBody] RequestDto req)
        {
            int totalRecords = 0;
            int filteredRecords = 0;
            int totalActive = 0;
            int totalDeleted = 0;
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string search = req.Data?.ToString() ?? "";
                string statusFilter = req.Status?.ToString() ?? "";
                string packageFilter = req.Package?.ToString() ?? "";
                string searchPattern = $"%{search}%";

                // 1. Total records (all series, no filter)
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM txn_series", conn))
                {
                    totalRecords = (int)cmd.ExecuteScalar();
                }

                // 2. Build WHERE clause
                string whereClause = "WHERE (s.name LIKE @Search)";

                if (!string.IsNullOrEmpty(packageFilter))
                    whereClause += " AND s.package_id = @PackageId";

                string whereClauseWithStatus = whereClause;
                if (statusFilter == "Active")
                    whereClauseWithStatus += " AND s.deleted_date IS NULL";
                else if (statusFilter == "Deleted")
                    whereClauseWithStatus += " AND s.deleted_date IS NOT NULL";
                // If "" (ALL), no additional condition

                // 3. Filtered records count (applies search + package + status filter)
                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM txn_series s {whereClauseWithStatus}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@PackageId", packageFilter);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                // 4. Active / Deleted counts (respects search + package filter, ignores status filter for cards)
                using (var cmd = new SqlCommand($@"
            SELECT
                SUM(CASE WHEN s.deleted_date IS NULL THEN 1 ELSE 0 END) AS ActiveCount,
                SUM(CASE WHEN s.deleted_date IS NOT NULL THEN 1 ELSE 0 END) AS DeletedCount
            FROM txn_series s
            {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@PackageId", packageFilter);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalActive = reader["ActiveCount"] != DBNull.Value ? Convert.ToInt32(reader["ActiveCount"]) : 0;
                        totalDeleted = reader["DeletedCount"] != DBNull.Value ? Convert.ToInt32(reader["DeletedCount"]) : 0;
                    }
                }

                // 5. Fetch paginated data (join mst_package untuk nama package)
                string sql = $@"
            SELECT
                s.series_id,
                s.package_id,
                p.name AS package_name,
                s.name,
                s.cover_image,
                s.sequence,
                s.deleted_date,
                s.created_date,
                s.updated_date,
                CASE WHEN s.deleted_date IS NULL THEN 'Active' ELSE 'Deleted' END AS Status
            FROM txn_series s
            LEFT JOIN mst_package p ON s.package_id = p.package_id
            {whereClauseWithStatus}
            ORDER BY s.series_id ASC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@PackageId", packageFilter);
                    cmd.Parameters.AddWithValue("@Skip", req.Skip);
                    cmd.Parameters.AddWithValue("@Take", req.Take);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new
                        {
                            series_id = reader["series_id"].ToString(),
                            package_id = reader["package_id"].ToString(),
                            package_name = reader["package_name"]?.ToString(),
                            name = reader["name"]?.ToString(),
                            cover_image = reader["cover_image"] == DBNull.Value ? null : reader["cover_image"]?.ToString(),
                            sequence = Convert.ToInt32(reader["sequence"]),
                            deleted_date = reader["deleted_date"] == DBNull.Value ? null : (DateTime?)reader["deleted_date"],
                            created_date = reader["created_date"] == DBNull.Value ? null : (DateTime?)reader["created_date"],
                            updated_date = reader["updated_date"] == DBNull.Value ? null : (DateTime?)reader["updated_date"],
                            status = reader["Status"].ToString()
                        });
                    }
                }

                return Json(new
                {
                    draw = req.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = filteredRecords,
                    totalActive,
                    totalDeleted,
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
                    totalDeleted = 0,
                    data = new List<object>(),
                    error = ex.Message // Remove in production for security
                });
            }
        }

        // ===============================
        // GET BY ID (populate form Edit)
        // ===============================
        [HttpGet]
        [Route("admin/series/get/{id}")]
        public IActionResult Get(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "SELECT series_id, package_id, name, cover_image, sequence FROM txn_series WHERE series_id = @SeriesId", conn);
                cmd.Parameters.AddWithValue("@SeriesId", id);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var data = new
                    {
                        series_id = reader["series_id"].ToString(),
                        package_id = reader["package_id"].ToString(),
                        name = reader["name"]?.ToString(),
                        cover_image = reader["cover_image"] == DBNull.Value
                            ? null
                            : reader["cover_image"]?.ToString(),
                        sequence = Convert.ToInt32(reader["sequence"])
                    };

                    return Json(new ResponseDto { Code = 200, Message = "Success", Data = data });
                }

                return Json(new ResponseDto { Code = 404, Message = "Series not found." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // SAVE (create / update)
        // ===============================
        [HttpPost]
        [Route("admin/series/save")]
        public IActionResult Save([FromForm] SeriesSaveRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.SeriesId) ||
                    string.IsNullOrWhiteSpace(req.PackageId) ||
                    string.IsNullOrWhiteSpace(req.Name) ||
                    req.Sequence <= 0)
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Series ID, Package, Name and Sequence are required."
                    });
                }

                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                // ==========================================
                // VALIDATE PACKAGE
                // ==========================================
                string packageId;

                using (var checkPkg = new SqlCommand(@"
            SELECT package_id
            FROM mst_package
            WHERE package_id = @PackageId
              AND deleted_date IS NULL
        ", conn))
                {
                    checkPkg.Parameters.AddWithValue("@PackageId", req.PackageId);

                    var result = checkPkg.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message = "Selected package is invalid or inactive."
                        });
                    }

                    packageId = result.ToString();
                }

                // ==========================================
                // FOLDER IMAGE
                // wwwroot/Audiobook/image/{package_id}
                // ==========================================
                string imageFolder = Path.Combine(
                    _env.WebRootPath,
                    "Audiobook",
                    "image",
                    packageId
                );

                if (!Directory.Exists(imageFolder))
                    Directory.CreateDirectory(imageFolder);

                // ==========================================
                // GET OLD PACKAGE
                // penting kalau package diganti saat edit
                // ==========================================
                string oldPackageId = null;

                if (req.IsEdit)
                {
                    using var getOld = new SqlCommand(@"
                SELECT package_id
                FROM txn_series
                WHERE series_id = @SeriesId
            ", conn);

                    getOld.Parameters.AddWithValue("@SeriesId", req.SeriesId);

                    var oldResult = getOld.ExecuteScalar();

                    if (oldResult != null && oldResult != DBNull.Value)
                        oldPackageId = oldResult.ToString();
                }

                // ==========================================
                // UPLOAD COVER
                // ==========================================
                string coverFileName = null;

                if (req.CoverFile != null && req.CoverFile.Length > 0)
                {
                    string ext = Path.GetExtension(req.CoverFile.FileName);

                    // Batasi extension
                    var allowedExtensions = new[]
                    {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

                    if (!allowedExtensions.Contains(
                        ext,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message = "Cover image must be JPG, JPEG, PNG, or WEBP."
                        });
                    }

                    coverFileName = $"{req.SeriesId}{ext}";

                    string coverPath = Path.Combine(
                        imageFolder,
                        coverFileName
                    );

                    // Hapus cover lama dengan extension berbeda
                    var oldCovers = Directory.GetFiles(
                        imageFolder,
                        $"{req.SeriesId}.*"
                    );

                    foreach (var oldFile in oldCovers)
                    {
                        if (!oldFile.Equals(
                            coverPath,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            System.IO.File.Delete(oldFile);
                        }
                    }

                    using var stream = new FileStream(
                        coverPath,
                        FileMode.Create
                    );

                    req.CoverFile.CopyTo(stream);
                }

                // ==========================================
                // UPDATE
                // ==========================================
                if (req.IsEdit)
                {
                    string sql;

                    if (coverFileName != null)
                    {
                        sql = @"
                    UPDATE txn_series
                    SET
                        package_id = @PackageId,
                        name = @Name,
                        sequence = @Sequence,
                        cover_image = @CoverImage,
                        updated_date = GETDATE()
                    WHERE series_id = @SeriesId";
                    }
                    else
                    {
                        sql = @"
                    UPDATE txn_series
                    SET
                        package_id = @PackageId,
                        name = @Name,
                        sequence = @Sequence,
                        updated_date = GETDATE()
                    WHERE series_id = @SeriesId";
                    }

                    using var cmd = new SqlCommand(sql, conn);

                    cmd.Parameters.AddWithValue(
                        "@SeriesId",
                        req.SeriesId
                    );

                    cmd.Parameters.AddWithValue(
                        "@PackageId",
                        req.PackageId
                    );

                    cmd.Parameters.AddWithValue(
                        "@Name",
                        req.Name
                    );

                    cmd.Parameters.AddWithValue(
                        "@Sequence",
                        req.Sequence
                    );

                    if (coverFileName != null)
                    {
                        cmd.Parameters.AddWithValue(
                            "@CoverImage",
                            coverFileName
                        );
                    }

                    int rows = cmd.ExecuteNonQuery();

                    if (rows == 0)
                    {
                        return Json(new ResponseDto
                        {
                            Code = 404,
                            Message = "Series not found."
                        });
                    }

                    // ==========================================
                    // PINDAH COVER JIKA PACKAGE BERUBAH
                    // ==========================================
                    if (!string.IsNullOrEmpty(oldPackageId) &&
                        oldPackageId != packageId)
                    {
                        string oldFolder = Path.Combine(
                            _env.WebRootPath,
                            "Audiobook",
                            "image",
                            oldPackageId
                        );

                        string newFolder = Path.Combine(
                            _env.WebRootPath,
                            "Audiobook",
                            "image",
                            packageId
                        );

                        if (!Directory.Exists(newFolder))
                            Directory.CreateDirectory(newFolder);

                        if (Directory.Exists(oldFolder))
                        {
                            var oldFiles = Directory.GetFiles(
                                oldFolder,
                                $"{req.SeriesId}.*"
                            );

                            foreach (var oldFile in oldFiles)
                            {
                                string fileName =
                                    Path.GetFileName(oldFile);

                                string newFile =
                                    Path.Combine(
                                        newFolder,
                                        fileName
                                    );

                                if (System.IO.File.Exists(newFile))
                                    System.IO.File.Delete(newFile);

                                System.IO.File.Move(
                                    oldFile,
                                    newFile
                                );
                            }
                        }
                    }

                    return Json(new ResponseDto
                    {
                        Code = 200,
                        Message = "Series updated successfully."
                    });
                }

                // ==========================================
                // CREATE
                // ==========================================

                using (var checkSeries = new SqlCommand(@"
            SELECT COUNT(*)
            FROM txn_series
            WHERE series_id = @SeriesId
        ", conn))
                {
                    checkSeries.Parameters.AddWithValue(
                        "@SeriesId",
                        req.SeriesId
                    );

                    if ((int)checkSeries.ExecuteScalar() > 0)
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message = "Series ID already exists."
                        });
                    }
                }

                using (var cmd = new SqlCommand(@"
            INSERT INTO txn_series
            (
                series_id,
                package_id,
                name,
                cover_image,
                sequence,
                created_date,
                updated_date,
                deleted_date
            )
            VALUES
            (
                @SeriesId,
                @PackageId,
                @Name,
                @CoverImage,
                @Sequence,
                GETDATE(),
                NULL,
                NULL
            )
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@SeriesId",
                        req.SeriesId
                    );

                    cmd.Parameters.AddWithValue(
                        "@PackageId",
                        req.PackageId
                    );

                    cmd.Parameters.AddWithValue(
                        "@Name",
                        req.Name
                    );

                    cmd.Parameters.AddWithValue(
                        "@CoverImage",
                        (object)coverFileName ?? DBNull.Value
                    );

                    cmd.Parameters.AddWithValue(
                        "@Sequence",
                        req.Sequence
                    );

                    cmd.ExecuteNonQuery();
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Series created successfully."
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

        // ===============================
        // DELETE (soft delete)
        // ===============================
        [HttpPost]
        [Route("admin/series/delete")]
        public IActionResult Delete([FromBody] RequestDto req)
        {
            try
            {
                string seriesId = req.Series;
                if (string.IsNullOrWhiteSpace(seriesId))
                    return Json(new ResponseDto { Code = 400, Message = "Series ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE txn_series SET deleted_date = GETDATE() WHERE series_id = @SeriesId AND deleted_date IS NULL", conn);
                cmd.Parameters.AddWithValue("@SeriesId", seriesId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Series not found or already deleted." });

                return Json(new ResponseDto { Code = 200, Message = "Series deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // RESTORE
        // ===============================
        [HttpPost]
        [Route("admin/series/restore")]
        public IActionResult Restore([FromBody] RequestDto req)
        {
            try
            {
                string seriesId = req.Series;

                if (string.IsNullOrWhiteSpace(seriesId))
                    return Json(new ResponseDto { Code = 400, Message = "Series ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE txn_series SET deleted_date = NULL WHERE series_id = @SeriesId AND deleted_date IS NOT NULL", conn);
                cmd.Parameters.AddWithValue("@SeriesId", seriesId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Series not found or already active." });

                return Json(new ResponseDto { Code = 200, Message = "Series restored successfully." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // DROPDOWN LIST PACKAGE
        // ===============================
        [HttpGet]
        [Route("admin/package/list-active")]
        public IActionResult ListActive()
        {
            var list = new List<object>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            using var cmd = new SqlCommand(@"
        SELECT package_id, name
        FROM mst_package
        WHERE deleted_date IS NULL
        ORDER BY name", conn);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new
                {
                    package_id = reader["package_id"].ToString(),
                    name = reader["name"].ToString()
                });
            }

            return Json(new ResponseDto
            {
                Code = 200,
                Message = "Success",
                Data = list
            });
        }
    }
}