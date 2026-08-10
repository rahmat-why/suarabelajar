using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using suara_belajar.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class AudiobookController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public AudiobookController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        [Route("admin/audiobook/index")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/Audiobook/Index.cshtml");
        }

        [Route("admin/audiobook/create")]
        public IActionResult Create()
        {
            return View("~/Views/PortalAdmin/Audiobook/Create.cshtml");
        }

        [Route("admin/audiobook/edit/{id}")]
        public IActionResult Edit(string id)
        {
            return View("~/Views/PortalAdmin/Audiobook/Edit.cshtml");
        }

        // ===============================
        // LOAD (list, search, filter status + series, pagination)
        // ===============================
        [HttpPost]
        [Route("admin/audiobook/load")]
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
                string seriesFilter = req.Package?.ToString() ?? ""; // reuse field "Package" utk series_id filter
                string searchPattern = $"%{search}%";

                // 1. Total records (all audiobooks, no filter)
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM txn_audiobook", conn))
                {
                    totalRecords = (int)cmd.ExecuteScalar();
                }

                // 2. Build WHERE clause
                string whereClause = "WHERE (a.title LIKE @Search OR a.description LIKE @Search)";

                if (!string.IsNullOrEmpty(seriesFilter))
                    whereClause += " AND a.series_id = @SeriesId";

                string whereClauseWithStatus = whereClause;
                if (statusFilter == "Active")
                    whereClauseWithStatus += " AND a.deleted_date IS NULL";
                else if (statusFilter == "Deleted")
                    whereClauseWithStatus += " AND a.deleted_date IS NOT NULL";
                // If "" (ALL), no additional condition

                // 3. Filtered records count
                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM txn_audiobook a {whereClauseWithStatus}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(seriesFilter))
                        cmd.Parameters.AddWithValue("@SeriesId", seriesFilter);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                // 4. Active / Deleted counts (respects search + series filter, ignores status filter for cards)
                using (var cmd = new SqlCommand($@"
            SELECT
                SUM(CASE WHEN a.deleted_date IS NULL THEN 1 ELSE 0 END) AS ActiveCount,
                SUM(CASE WHEN a.deleted_date IS NOT NULL THEN 1 ELSE 0 END) AS DeletedCount
            FROM txn_audiobook a
            {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(seriesFilter))
                        cmd.Parameters.AddWithValue("@SeriesId", seriesFilter);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalActive = reader["ActiveCount"] != DBNull.Value ? Convert.ToInt32(reader["ActiveCount"]) : 0;
                        totalDeleted = reader["DeletedCount"] != DBNull.Value ? Convert.ToInt32(reader["DeletedCount"]) : 0;
                    }
                }

                // 5. Fetch paginated data (join txn_series + mst_package untuk label)
                string sql = $@"
                    SELECT
                        a.audiobook_id,
                        a.series_id,
                        s.name AS series_name,
                        s.package_id,
                        p.name AS package_name,
                        a.title,
                        a.description,
                        a.cover_image,
                        a.duration,
                        a.deleted_date,
                        a.created_date,
                        a.updated_date,
                        CASE WHEN a.deleted_date IS NULL THEN 'Active' ELSE 'Deleted' END AS Status
                    FROM txn_audiobook a
                    LEFT JOIN txn_series s ON a.series_id = s.series_id
                    LEFT JOIN mst_package p ON s.package_id = p.package_id
                    {whereClauseWithStatus}
                    ORDER BY a.audiobook_id ASC
                    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(seriesFilter))
                        cmd.Parameters.AddWithValue("@SeriesId", seriesFilter);
                    cmd.Parameters.AddWithValue("@Skip", req.Skip);
                    cmd.Parameters.AddWithValue("@Take", req.Take);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new
                        {
                            audiobook_id = reader["audiobook_id"].ToString(),
                            series_id = reader["series_id"]?.ToString(),
                            series_name = reader["series_name"]?.ToString(),
                            package_id = reader["package_id"]?.ToString(),
                            package_name = reader["package_name"]?.ToString(),
                            title = reader["title"]?.ToString(),
                            description = reader["description"]?.ToString(),
                            cover_image = reader["cover_image"]?.ToString(),
                            duration = reader["duration"]?.ToString(),
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
        [Route("admin/audiobook/get/{id}")]
        public IActionResult Get(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(@"
                    SELECT audiobook_id, series_id, title, description, cover_image, duration
                    FROM txn_audiobook
                    WHERE audiobook_id = @AudiobookId", conn);
                cmd.Parameters.AddWithValue("@AudiobookId", id);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var data = new
                    {
                        audiobook_id = reader["audiobook_id"].ToString(),
                        series_id = reader["series_id"]?.ToString(),
                        title = reader["title"]?.ToString(),
                        description = reader["description"]?.ToString(),
                        cover_image = reader["cover_image"]?.ToString(),
                        duration = reader["duration"]?.ToString()
                    };

                    return Json(new ResponseDto { Code = 200, Message = "Success", Data = data });
                }

                return Json(new ResponseDto { Code = 404, Message = "Audiobook not found." });
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
        [Route("admin/audiobook/save")]
        public IActionResult Save([FromForm] AudiobookSaveRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.AudiobookId) || string.IsNullOrWhiteSpace(req.SeriesId)
                    || string.IsNullOrWhiteSpace(req.Title))
                {
                    return Json(new ResponseDto { Code = 400, Message = "Audiobook ID, Series, and Title are required." });
                }

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                // Pastikan series_id valid & masih active, sekaligus ambil package_id untuk struktur folder audio
                string packageName;

                using (var checkSeries = new SqlCommand(@"
                    SELECT p.package_id
                    FROM txn_series s
                    INNER JOIN mst_package p
                        ON s.package_id = p.package_id
                    WHERE s.series_id = @SeriesId
                      AND s.deleted_date IS NULL", conn))
                {
                    checkSeries.Parameters.AddWithValue("@SeriesId", req.SeriesId);

                    var result = checkSeries.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message = "Selected series is invalid or inactive."
                        });
                    }

                    packageName = result.ToString();
                }

                // Ambil package_id LAMA (sebelum update) untuk keperluan pindah folder audio kalau package berubah
                string oldPackageName = null;

                if (req.IsEdit)
                {
                    using var getOld = new SqlCommand(@"
                        SELECT p.package_id
                        FROM txn_audiobook a
                        INNER JOIN txn_series s
                            ON a.series_id = s.series_id
                        INNER JOIN mst_package p
                            ON s.package_id = p.package_id
                        WHERE a.audiobook_id = @AudiobookId", conn);

                    getOld.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);

                    var oldResult = getOld.ExecuteScalar();

                    if (oldResult != null && oldResult != DBNull.Value)
                        oldPackageName = oldResult.ToString();
                }

                string coverFileName = null;
                string coverFolder = Path.Combine(_env.WebRootPath, "Audiobook", "image", packageName);
                // Audio disimpan per-package: wwwroot/Audiobook/audio/{package_id}/{audiobook_id}.mp3
                string audioFolder = Path.Combine(_env.WebRootPath, "Audiobook", "audio", packageName);

                if (!Directory.Exists(coverFolder)) Directory.CreateDirectory(coverFolder);
                if (!Directory.Exists(audioFolder)) Directory.CreateDirectory(audioFolder);

                // ===== Upload cover (opsional) =====
                if (req.CoverFile != null && req.CoverFile.Length > 0)
                {
                    string ext = Path.GetExtension(req.CoverFile.FileName);
                    coverFileName = $"{req.AudiobookId}{ext}";
                    string coverPath = Path.Combine(coverFolder, coverFileName);

                    // Hapus cover lama dengan ekstensi berbeda (misal ganti .png -> .jpg)
                    var oldCovers = Directory.GetFiles(coverFolder, $"{req.AudiobookId}.*");
                    foreach (var oldFile in oldCovers)
                    {
                        if (!oldFile.Equals(coverPath, StringComparison.OrdinalIgnoreCase))
                            System.IO.File.Delete(oldFile);
                    }

                    using var stream = new FileStream(coverPath, FileMode.Create);
                    req.CoverFile.CopyTo(stream);
                }

                // ===== Upload audio (opsional, konvensi nama {audiobook_id}.mp3) =====
                if (req.AudioFile != null && req.AudioFile.Length > 0)
                {
                    string audioPath = Path.Combine(audioFolder, $"{req.AudiobookId}.mp3");
                    using var stream = new FileStream(audioPath, FileMode.Create);
                    req.AudioFile.CopyTo(stream);
                }

                if (req.IsEdit)
                {
                    // ===== UPDATE =====
                    string sql = coverFileName != null
                        ? @"UPDATE txn_audiobook
                            SET series_id = @SeriesId, title = @Title, description = @Description,
                                duration = @Duration, cover_image = @CoverImage, updated_date = GETDATE()
                            WHERE audiobook_id = @AudiobookId"
                        : @"UPDATE txn_audiobook
                            SET series_id = @SeriesId, title = @Title, description = @Description,
                                duration = @Duration, updated_date = GETDATE()
                            WHERE audiobook_id = @AudiobookId";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@SeriesId", req.SeriesId);
                    cmd.Parameters.AddWithValue("@Title", req.Title);
                    cmd.Parameters.AddWithValue("@Description", (object)req.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Duration", (object)req.Duration ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                    if (coverFileName != null)
                        cmd.Parameters.AddWithValue("@CoverImage", coverFileName);

                    int rows = cmd.ExecuteNonQuery();

                    if (rows == 0)
                        return Json(new ResponseDto { Code = 404, Message = "Audiobook not found." });

                    if (oldPackageName != packageName && req.AudioFile == null)
                    {
                        string oldFolder = Path.Combine(
                            _env.WebRootPath,
                            "Audiobook",
                            "audio",
                            oldPackageName
                        );

                        string newFolder = Path.Combine(
                            _env.WebRootPath,
                            "Audiobook",
                            "audio",
                            packageName
                        );

                        if (!Directory.Exists(newFolder))
                            Directory.CreateDirectory(newFolder);

                        string oldFile = Path.Combine(oldFolder, $"{req.AudiobookId}.mp3");
                        string newFile = Path.Combine(newFolder, $"{req.AudiobookId}.mp3");

                        if (System.IO.File.Exists(oldFile))
                        {
                            if (System.IO.File.Exists(newFile))
                                System.IO.File.Delete(newFile);

                            System.IO.File.Move(oldFile, newFile);
                        }
                    }

                    if (oldPackageName != packageName && req.CoverFile == null)
                    {
                        string oldFolder = Path.Combine(
                            _env.WebRootPath,
                            "Audiobook",
                            "image",
                            oldPackageName
                        );

                        string newFolder = Path.Combine(
                            _env.WebRootPath,
                            "Audiobook",
                            "image",
                            packageName
                        );

                        if (!Directory.Exists(newFolder))
                            Directory.CreateDirectory(newFolder);

                        var oldFiles = Directory.GetFiles(oldFolder, $"{req.AudiobookId}.*");

                        foreach (var oldFile in oldFiles)
                        {
                            string fileName = Path.GetFileName(oldFile);
                            string newFile = Path.Combine(newFolder, fileName);

                            if (System.IO.File.Exists(newFile))
                                System.IO.File.Delete(newFile);

                            System.IO.File.Move(oldFile, newFile);
                        }
                    }

                    return Json(new ResponseDto { Code = 200, Message = "Audiobook updated successfully." });
                }
                else
                {
                    // ===== CREATE =====
                    using (var checkCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM txn_audiobook WHERE audiobook_id = @AudiobookId", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                        int exists = (int)checkCmd.ExecuteScalar();
                        if (exists > 0)
                            return Json(new ResponseDto { Code = 400, Message = "Audiobook ID already exists." });
                    }

                    using var cmd = new SqlCommand(@"
                        INSERT INTO txn_audiobook
                            (audiobook_id, series_id, title, description, cover_image, duration, deleted_date, created_date, updated_date)
                        VALUES
                            (@AudiobookId, @SeriesId, @Title, @Description, @CoverImage, @Duration, NULL, GETDATE(), NULL)", conn);

                    cmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                    cmd.Parameters.AddWithValue("@SeriesId", req.SeriesId);
                    cmd.Parameters.AddWithValue("@Title", req.Title);
                    cmd.Parameters.AddWithValue("@Description", (object)req.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@CoverImage", (object)coverFileName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Duration", (object)req.Duration ?? DBNull.Value);

                    cmd.ExecuteNonQuery();

                    return Json(new ResponseDto { Code = 200, Message = "Audiobook created successfully." });
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
        [Route("admin/audiobook/delete")]
        public IActionResult Delete([FromBody] RequestDto req)
        {
            try
            {
                string audiobookId = req.Audiobook;

                if (string.IsNullOrWhiteSpace(audiobookId))
                    return Json(new ResponseDto { Code = 400, Message = "Audiobook ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE txn_audiobook SET deleted_date = GETDATE() WHERE audiobook_id = @AudiobookId AND deleted_date IS NULL", conn);
                cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Audiobook not found or already deleted." });

                return Json(new ResponseDto { Code = 200, Message = "Audiobook deleted successfully." });
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
        [Route("admin/audiobook/restore")]
        public IActionResult Restore([FromBody] RequestDto req)
        {
            try
            {
                string audiobookId = req.Audiobook;

                if (string.IsNullOrWhiteSpace(audiobookId))
                    return Json(new ResponseDto { Code = 400, Message = "Audiobook ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "UPDATE txn_audiobook SET deleted_date = NULL WHERE audiobook_id = @AudiobookId AND deleted_date IS NOT NULL", conn);
                cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Audiobook not found or already active." });

                return Json(new ResponseDto { Code = 200, Message = "Audiobook restored successfully." });
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
        [Route("admin/audiobook/list-active")]
        public IActionResult ListActive()
        {
            var list = new List<object>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));   
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT series_id, name
                FROM txn_series
                WHERE deleted_date IS NULL
                ORDER BY name", conn);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new
                {
                    series_id = reader["series_id"].ToString(),
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