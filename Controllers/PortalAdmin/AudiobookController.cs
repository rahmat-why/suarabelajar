using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class AudiobookController : Controller
    {
        private readonly IConfiguration _config;

        public AudiobookController(IConfiguration config)
        {
            _config = config;
        }

        [Route("admin/audiobook-jagobacain")]
        public IActionResult IndexJagobacain()
        {
            return View("~/Views/PortalAdmin/Audiobook/jagobacain.cshtml");
        }

        [Route("admin/audiobook-islambercerita")]
        public IActionResult IndexIslambercerita()
        {
            return View("~/Views/PortalAdmin/Audiobook/islambercerita.cshtml");
        }

        [HttpPost]
        [Route("admin/audiobook/load-jagobacain")]
        public IActionResult LoadAudiobookJagobacain([FromBody] RequestDto req)
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
                string searchPattern = $"%{search}%";

                // 1. Total records (all audiobooks, no filter)
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM txn_audiobook_jagobacain", conn))
                {
                    totalRecords = (int)cmd.ExecuteScalar();
                }

                // 2. Build WHERE clause with proper parentheses to fix precedence issue
                string whereClause = "WHERE (title LIKE @Search OR description LIKE @Search)";

                if (statusFilter == "Active")
                    whereClause += " AND deleted_date IS NULL";
                else if (statusFilter == "Deleted")
                    whereClause += " AND deleted_date IS NOT NULL";
                // If "" (ALL), no additional condition

                // 3. Filtered records count (applies search + status filter)
                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM txn_audiobook_jagobacain {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                // 4. Active / Deleted counts (respects current search term, ignores status filter for cards)
                using (var cmd = new SqlCommand($@"
            SELECT
                SUM(CASE WHEN deleted_date IS NULL THEN 1 ELSE 0 END) AS ActiveCount,
                SUM(CASE WHEN deleted_date IS NOT NULL THEN 1 ELSE 0 END) AS DeletedCount
            FROM txn_audiobook_jagobacain
            WHERE (title LIKE @Search OR description LIKE @Search)", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalActive = reader["ActiveCount"] != DBNull.Value ? Convert.ToInt32(reader["ActiveCount"]) : 0;
                        totalDeleted = reader["DeletedCount"] != DBNull.Value ? Convert.ToInt32(reader["DeletedCount"]) : 0;
                    }
                }

                // 5. Fetch paginated data
                string sql = $@"
            SELECT
                audiobook_id,
                title,
                description,
                cover_image,
                duration,
                created_date,
                deleted_date,
                CASE WHEN deleted_date IS NULL THEN 'Active' ELSE 'Deleted' END AS Status
            FROM txn_audiobook_jagobacain
            {whereClause}
            ORDER BY created_date DESC
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
                            audiobook_id = reader["audiobook_id"],
                            title = reader["title"]?.ToString(),
                            description = reader["description"]?.ToString(),
                            cover_image = reader["cover_image"]?.ToString(),
                            duration = reader["duration"] != DBNull.Value ? Convert.ToInt32(reader["duration"]) : (int?)null,
                            created_date = reader["created_date"],
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
                // Log the exception in production (consider using ILogger)
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

        [HttpPost]
        [Route("admin/audiobook/load-islambercerita")]
        public IActionResult LoadAudiobookIslambercerita([FromBody] RequestDto req)
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
                string searchPattern = $"%{search}%";

                // ===============================
                // 1. Total records (ALL)
                // ===============================
                using (var cmd = new SqlCommand(
                    "SELECT COUNT(*) FROM txn_audiobook_islambercerita", conn))
                {
                    totalRecords = (int)cmd.ExecuteScalar();
                }

                // ===============================
                // 2. WHERE clause
                // ===============================
                string whereClause = @"
WHERE
(
    a.title LIKE @Search OR
    a.description LIKE @Search OR
    s.name LIKE @Search
)";

                if (statusFilter == "Active")
                    whereClause += " AND a.deleted_date IS NULL";
                else if (statusFilter == "Deleted")
                    whereClause += " AND a.deleted_date IS NOT NULL";

                // ===============================
                // 3. Filtered count
                // ===============================
                using (var cmd = new SqlCommand($@"
SELECT COUNT(*)
FROM txn_audiobook_islambercerita a
LEFT JOIN txn_series_islambercerita s
    ON a.series_id = s.series_id
{whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                // ===============================
                // 4. Active / Deleted counts
                // ===============================
                using (var cmd = new SqlCommand(@"
SELECT
    SUM(CASE WHEN a.deleted_date IS NULL THEN 1 ELSE 0 END) AS ActiveCount,
    SUM(CASE WHEN a.deleted_date IS NOT NULL THEN 1 ELSE 0 END) AS DeletedCount
FROM txn_audiobook_islambercerita a
LEFT JOIN txn_series_islambercerita s
    ON a.series_id = s.series_id
WHERE
(
    a.title LIKE @Search OR
    a.description LIKE @Search OR
    s.name LIKE @Search
)", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalActive = reader["ActiveCount"] != DBNull.Value
                            ? Convert.ToInt32(reader["ActiveCount"])
                            : 0;

                        totalDeleted = reader["DeletedCount"] != DBNull.Value
                            ? Convert.ToInt32(reader["DeletedCount"])
                            : 0;
                    }
                }

                // ===============================
                // 5. Fetch paginated data
                // ===============================
                string sql = $@"
SELECT
    a.audiobook_id,
    a.title,
    a.description,
    a.cover_image,
    a.duration,
    a.created_date,
    s.name AS series_name,
    CASE
        WHEN a.deleted_date IS NULL THEN 'Active'
        ELSE 'Deleted'
    END AS Status
FROM txn_audiobook_islambercerita a
LEFT JOIN txn_series_islambercerita s
    ON a.series_id = s.series_id
{whereClause}
ORDER BY a.created_date DESC
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
                            audiobook_id = reader["audiobook_id"],
                            title = reader["title"]?.ToString(),
                            description = reader["description"]?.ToString(),
                            cover_image = reader["cover_image"]?.ToString(),
                            duration = reader["duration"] != DBNull.Value
                                ? Convert.ToInt32(reader["duration"])
                                : (int?)null,
                            created_date = reader["created_date"],
                            series_name = reader["series_name"]?.ToString(),
                            status = reader["Status"].ToString()
                        });
                    }
                }

                // ===============================
                // 6. Response
                // ===============================
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
                    error = ex.Message
                });
            }
        }
    }
}