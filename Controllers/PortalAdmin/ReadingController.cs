using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Models;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class ReadingController : Controller
    {
        private readonly IConfiguration _config;

        public ReadingController(IConfiguration config)
        {
            _config = config;
        }

        [Route("admin/reading/index")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/Reading/Index.cshtml");
        }

        [Route("admin/reading/create")]
        public IActionResult Create()
        {
            return View("~/Views/PortalAdmin/Reading/Create.cshtml");
        }

        [Route("admin/reading/edit/{id}")]
        public IActionResult Edit(string id)
        {
            return View("~/Views/PortalAdmin/Reading/Edit.cshtml");
        }

        // ===============================
        // LOAD (list, search by audiobook title, filter audiobook, pagination)
        // ===============================
        [HttpPost]
        [Route("admin/reading/load")]
        public IActionResult Load([FromBody] RequestDto req)
        {
            int totalRecords = 0;
            int filteredRecords = 0;
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string search = req.Data?.ToString() ?? "";
                string audiobookFilter = req.Audiobook?.ToString() ?? "";
                string searchPattern = $"%{search}%";

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM mst_summary", conn))
                    totalRecords = (int)cmd.ExecuteScalar();

                string whereClause = "WHERE (a.title LIKE @Search)";
                if (!string.IsNullOrEmpty(audiobookFilter))
                    whereClause += " AND s.audiobook_id = @AudiobookId";

                using (var cmd = new SqlCommand($@"
                    SELECT COUNT(*)
                    FROM mst_summary s
                    LEFT JOIN txn_audiobook a ON s.audiobook_id = a.audiobook_id
                    {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(audiobookFilter))
                        cmd.Parameters.AddWithValue("@AudiobookId", audiobookFilter);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                string sql = $@"
                    SELECT
                        s.summary_id,
                        s.audiobook_id,
                        a.title AS audiobook_title,
                        s.description
                    FROM mst_summary s
                    LEFT JOIN txn_audiobook a ON s.audiobook_id = a.audiobook_id
                    {whereClause}
                    ORDER BY a.title ASC
                    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(audiobookFilter))
                        cmd.Parameters.AddWithValue("@AudiobookId", audiobookFilter);
                    cmd.Parameters.AddWithValue("@Skip", req.Skip);
                    cmd.Parameters.AddWithValue("@Take", req.Take);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string desc = reader["description"]?.ToString() ?? "";
                        string plainText = System.Text.RegularExpressions.Regex.Replace(desc, "<.*?>", " ").Trim();
                        string preview = plainText.Length > 120 ? plainText.Substring(0, 120) + "..." : plainText;

                        list.Add(new
                        {
                            summary_id = reader["summary_id"].ToString(),
                            audiobook_id = reader["audiobook_id"]?.ToString(),
                            audiobook_title = reader["audiobook_title"]?.ToString() ?? "-",
                            description_preview = preview
                        });
                    }
                }

                return Json(new
                {
                    draw = req.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = filteredRecords,
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
                    data = new List<object>(),
                    error = ex.Message
                });
            }
        }

        // ===============================
        // GET BY ID (populate form Edit)
        // ===============================
        [HttpGet]
        [Route("admin/reading/get/{id}")]
        public IActionResult Get(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(@"
                    SELECT summary_id, audiobook_id, description
                    FROM mst_summary
                    WHERE summary_id = @Id", conn);
                cmd.Parameters.AddWithValue("@Id", id);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return Json(new ResponseDto { Code = 404, Message = "Summary not found." });

                var detail = new SummaryDetail
                {
                    SummaryId = reader["summary_id"].ToString(),
                    AudiobookId = reader["audiobook_id"]?.ToString(),
                    Description = reader["description"]?.ToString()
                };

                return Json(new ResponseDto { Code = 200, Message = "Success", Data = detail });
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
        [Route("admin/reading/save")]
        public IActionResult Save([FromBody] SummarySaveRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.SummaryId) || string.IsNullOrWhiteSpace(req.AudiobookId))
                    return Json(new ResponseDto { Code = 400, Message = "Summary ID and Audiobook are required." });

                if (string.IsNullOrWhiteSpace(req.Description))
                    return Json(new ResponseDto { Code = 400, Message = "Description cannot be empty." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using (var checkAudiobook = new SqlCommand(
                    "SELECT COUNT(*) FROM txn_audiobook WHERE audiobook_id = @AudiobookId AND deleted_date IS NULL", conn))
                {
                    checkAudiobook.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                    int exists = (int)checkAudiobook.ExecuteScalar();
                    if (exists == 0)
                        return Json(new ResponseDto { Code = 400, Message = "Selected audiobook is invalid or inactive." });
                }

                if (req.IsEdit)
                {
                    using var cmd = new SqlCommand(@"
                        UPDATE mst_summary
                        SET audiobook_id = @AudiobookId, description = @Description
                        WHERE summary_id = @SummaryId", conn);
                    cmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                    cmd.Parameters.AddWithValue("@Description", req.Description);
                    cmd.Parameters.AddWithValue("@SummaryId", req.SummaryId);

                    int rows = cmd.ExecuteNonQuery();
                    if (rows == 0)
                        return Json(new ResponseDto { Code = 404, Message = "Summary not found." });
                }
                else
                {
                    using (var checkCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM mst_summary WHERE summary_id = @SummaryId", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@SummaryId", req.SummaryId);
                        int exists = (int)checkCmd.ExecuteScalar();
                        if (exists > 0)
                            return Json(new ResponseDto { Code = 400, Message = "Summary ID already exists." });
                    }

                    using (var checkDup = new SqlCommand(
                        "SELECT COUNT(*) FROM mst_summary WHERE audiobook_id = @AudiobookId", conn))
                    {
                        checkDup.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                        int exists = (int)checkDup.ExecuteScalar();
                        if (exists > 0)
                            return Json(new ResponseDto { Code = 400, Message = "Audiobook ini sudah punya summary. Edit yang sudah ada, jangan buat baru." });
                    }

                    using var cmd = new SqlCommand(@"
                        INSERT INTO mst_summary (summary_id, audiobook_id, description)
                        VALUES (@SummaryId, @AudiobookId, @Description)", conn);
                    cmd.Parameters.AddWithValue("@SummaryId", req.SummaryId);
                    cmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                    cmd.Parameters.AddWithValue("@Description", req.Description);
                    cmd.ExecuteNonQuery();
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = req.IsEdit ? "Summary updated successfully." : "Summary created successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // DELETE
        // ===============================
        [HttpPost]
        [Route("admin/reading/delete")]
        public IActionResult Delete([FromBody] RequestDto req)
        {
            try
            {
                string summaryId = req.Summary; ;
                if (string.IsNullOrWhiteSpace(summaryId))
                    return Json(new ResponseDto { Code = 400, Message = "Summary ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "DELETE FROM mst_summary WHERE summary_id = @Id", conn);
                cmd.Parameters.AddWithValue("@Id", summaryId);

                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Summary not found." });

                return Json(new ResponseDto { Code = 200, Message = "Summary deleted." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // DROPDOWN LIST AUDIOBOOK (reuse pola quiz)
        // ===============================
        [HttpGet]
        [Route("admin/reading/list-audiobook")]
        public IActionResult ListAudiobook()
        {
            var list = new List<object>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            using var cmd = new SqlCommand(@"
                SELECT audiobook_id, title
                FROM txn_audiobook
                WHERE deleted_date IS NULL
                ORDER BY title", conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    audiobook_id = reader["audiobook_id"].ToString(),
                    title = reader["title"].ToString()
                });
            }

            return Json(new ResponseDto { Code = 200, Message = "Success", Data = list });
        }
    }
}