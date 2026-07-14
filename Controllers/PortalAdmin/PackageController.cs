using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Models;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class PackageController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public PackageController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        [Route("admin/package/index")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/Package/Index.cshtml");
        }

        [Route("admin/package/create")]
        public IActionResult Create()
        {
            return View("~/Views/PortalAdmin/Package/Create.cshtml");
        }

        [Route("admin/package/edit/{id}")]
        public IActionResult Edit(string id)
        {
            return View("~/Views/PortalAdmin/Package/Edit.cshtml");
        }

        // ===============================
        // LOAD (list, search, filter, pagination)
        // ===============================
        [HttpPost]
        [Route("admin/package/load")]
        public IActionResult Load([FromBody] RequestDto req)
        {
            int totalRecords = 0;
            int filteredRecords = 0;
            int totalActive = 0;
            int totalDeleted = 0;
            int totalSeries = 0;
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string search = req.Data?.ToString() ?? "";
                string statusFilter = req.Status?.ToString() ?? "";
                string searchPattern = $"%{search}%";

                // 1. Total records (all packages, no filter)
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM mst_package", conn))
                {
                    totalRecords = (int)cmd.ExecuteScalar();
                }

                // 2. Build WHERE clause
                string whereClause = "WHERE (package_id LIKE @Search OR name LIKE @Search)";

                if (statusFilter == "Active")
                    whereClause += " AND deleted_date IS NULL";
                else if (statusFilter == "Deleted")
                    whereClause += " AND deleted_date IS NOT NULL";
                // If "" (ALL), no additional condition

                // 3. Filtered records count (applies search + status filter)
                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM mst_package {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                // 4. Active / Deleted / Series counts (respects current search term, ignores status filter for cards)
                using (var cmd = new SqlCommand($@"
            SELECT
                SUM(CASE WHEN deleted_date IS NULL THEN 1 ELSE 0 END) AS ActiveCount,
                SUM(CASE WHEN deleted_date IS NOT NULL THEN 1 ELSE 0 END) AS DeletedCount,
                SUM(CASE WHEN is_series = 1 THEN 1 ELSE 0 END) AS SeriesCount
            FROM mst_package
            WHERE (package_id LIKE @Search OR name LIKE @Search)", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalActive = reader["ActiveCount"] != DBNull.Value ? Convert.ToInt32(reader["ActiveCount"]) : 0;
                        totalDeleted = reader["DeletedCount"] != DBNull.Value ? Convert.ToInt32(reader["DeletedCount"]) : 0;
                        totalSeries = reader["SeriesCount"] != DBNull.Value ? Convert.ToInt32(reader["SeriesCount"]) : 0;
                    }
                }

                // 5. Fetch paginated data
                string sql = $@"
            SELECT
                package_id,
                name,
                logo_image,
                is_series,
                deleted_date,
                created_date,
                updated_date,
                CASE WHEN deleted_date IS NULL THEN 'Active' ELSE 'Deleted' END AS Status
            FROM mst_package
            {whereClause}
            ORDER BY package_id ASC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    cmd.Parameters.AddWithValue("@Skip", req.Skip);
                    cmd.Parameters.AddWithValue("@Take", req.Take);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new
                        {
                            package_id = reader["package_id"].ToString(),
                            name = reader["name"]?.ToString(),
                            logo_image = reader["logo_image"]?.ToString(),
                            is_series = reader["is_series"] != DBNull.Value && Convert.ToBoolean(reader["is_series"]),
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
                    totalSeries,
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
                    totalSeries = 0,
                    data = new List<object>(),
                    error = ex.Message // Remove in production for security
                });
            }
        }

        // ===============================
        // GET BY ID (populate form Edit)
        // ===============================
        [HttpGet]
        [Route("admin/package/get/{id}")]
        public IActionResult Get(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "SELECT package_id, name, logo_image, is_series FROM mst_package WHERE package_id = @PackageId", conn);
                cmd.Parameters.AddWithValue("@PackageId", id);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var data = new
                    {
                        package_id = reader["package_id"].ToString(),
                        name = reader["name"]?.ToString(),
                        logo_image = reader["logo_image"]?.ToString(),
                        is_series = reader["is_series"] != DBNull.Value && Convert.ToBoolean(reader["is_series"])
                    };

                    return Json(new ResponseDto { Code = 200, Message = "Success", Data = data });
                }

                return Json(new ResponseDto { Code = 404, Message = "Package not found." });
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
        [Route("admin/package/save")]
        public IActionResult Save([FromForm] PackageSaveRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.PackageId) || string.IsNullOrWhiteSpace(req.Name))
                {
                    return Json(new ResponseDto { Code = 400, Message = "Package ID and Name are required." });
                }

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string logoFileName = null;

                // Upload logo file if provided
                if (req.LogoFile != null && req.LogoFile.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_env.WebRootPath, "Package");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    string extension = Path.GetExtension(req.LogoFile.FileName);
                    logoFileName = $"{req.PackageId}{extension}";
                    string filePath = Path.Combine(uploadsFolder, logoFileName);

                    // Hapus file logo lama kalau ekstensinya beda (misal ganti .png -> .jpg)
                    // supaya tidak ada file sisa menumpuk dengan nama package_id yang sama
                    var oldFiles = Directory.GetFiles(uploadsFolder, $"{req.PackageId}.*");
                    foreach (var oldFile in oldFiles)
                    {
                        if (!oldFile.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                            System.IO.File.Delete(oldFile);
                    }

                    // FileMode.Create otomatis overwrite kalau file lama dengan nama & ekstensi sama masih ada
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        req.LogoFile.CopyTo(stream);
                    }
                }

                if (req.IsEdit)
                {
                    // ===== UPDATE =====
                    string sql = logoFileName != null
                        ? @"UPDATE mst_package
                            SET name = @Name, is_series = @IsSeries, logo_image = @LogoImage, updated_date = GETDATE()
                            WHERE package_id = @PackageId"
                        : @"UPDATE mst_package
                            SET name = @Name, is_series = @IsSeries, updated_date = GETDATE()
                            WHERE package_id = @PackageId";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Name", req.Name);
                    cmd.Parameters.AddWithValue("@IsSeries", req.IsSeries);
                    cmd.Parameters.AddWithValue("@PackageId", req.PackageId);
                    if (logoFileName != null)
                        cmd.Parameters.AddWithValue("@LogoImage", logoFileName);

                    int rows = cmd.ExecuteNonQuery();

                    if (rows == 0)
                        return Json(new ResponseDto { Code = 404, Message = "Package not found." });

                    return Json(new ResponseDto { Code = 200, Message = "Package updated successfully." });
                }
                else
                {
                    // ===== CREATE =====
                    using (var checkCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM mst_package WHERE package_id = @PackageId", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@PackageId", req.PackageId);
                        int exists = (int)checkCmd.ExecuteScalar();
                        if (exists > 0)
                            return Json(new ResponseDto { Code = 400, Message = "Package ID already exists." });
                    }

                    using var cmd = new SqlCommand(@"
                        INSERT INTO mst_package (package_id, name, logo_image, is_series, deleted_date, created_date, updated_date)
                        VALUES (@PackageId, @Name, @LogoImage, @IsSeries, NULL, GETDATE(), NULL)", conn);

                    cmd.Parameters.AddWithValue("@PackageId", req.PackageId);
                    cmd.Parameters.AddWithValue("@Name", req.Name);
                    cmd.Parameters.AddWithValue("@LogoImage", (object)logoFileName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IsSeries", req.IsSeries);

                    cmd.ExecuteNonQuery();

                    return Json(new ResponseDto { Code = 200, Message = "Package created successfully." });
                }
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // DELETE (soft delete)
        // ===============================
        [HttpPost]
        [Route("admin/package/delete")]
        public IActionResult Delete([FromBody] RequestDto req)
        {
            try
            {
                string packageId = req.Package;

                if (string.IsNullOrWhiteSpace(packageId))
                    return Json(new ResponseDto { Code = 400, Message = "Package ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE mst_package SET deleted_date = GETDATE() WHERE package_id = @PackageId AND deleted_date IS NULL", conn);
                cmd.Parameters.AddWithValue("@PackageId", packageId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Package not found or already deleted." });

                return Json(new ResponseDto { Code = 200, Message = "Package deleted successfully." });
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
        [Route("admin/package/restore")]
        public IActionResult Restore([FromBody] RequestDto req)
        {
            try
            {
                string packageId = req.Package;

                if (string.IsNullOrWhiteSpace(packageId))
                    return Json(new ResponseDto { Code = 400, Message = "Package ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE mst_package SET deleted_date = NULL WHERE package_id = @PackageId AND deleted_date IS NOT NULL", conn);
                cmd.Parameters.AddWithValue("@PackageId", packageId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Package not found or already active." });

                return Json(new ResponseDto { Code = 200, Message = "Package restored successfully." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }
    }
}