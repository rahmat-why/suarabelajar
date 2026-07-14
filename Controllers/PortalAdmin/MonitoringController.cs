using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Models;
using suara_belajar.Models.Monitoring;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class MonitoringController : Controller
    {
        private readonly IConfiguration _config;

        public MonitoringController(IConfiguration config)
        {
            _config = config;
        }

        [Route("admin/monitoring/assessment")]
        public IActionResult Assessment()
        {
            return View("~/Views/PortalAdmin/Monitoring/Assessment.cshtml");
        }

        // ===============================
        // LOAD (list + search + filter package/status + pagination + summary count)
        // ===============================
        [HttpPost]
        [Route("admin/monitoring/assessment/load")]
        public IActionResult LoadAssessment([FromBody] RequestDto req)
        {
            int totalRecords = 0;
            int filteredRecords = 0;
            int totalPass = 0, totalFail = 0, totalPending = 0;
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM txn_assessment", conn))
                    totalRecords = (int)cmd.ExecuteScalar();

                string search = req.Data?.ToString() ?? "";
                string packageFilter = req.Package?.ToString() ?? "";
                string statusFilter = req.Status?.ToString() ?? "";
                string searchPattern = $"%{search}%";

                string baseFrom = @"
                    FROM txn_assessment ta
                    INNER JOIN txn_code c ON ta.code_id = c.code_id
                    LEFT JOIN txn_audiobook a ON ta.audiobook_id = a.audiobook_id
                    LEFT JOIN mst_package p ON c.package_id = p.package_id
                    LEFT JOIN mst_quiz q ON ta.quiz_id = q.quiz_id";

                string whereClause = "WHERE (c.serial_number LIKE @Search OR a.title LIKE @Search)";

                if (!string.IsNullOrEmpty(packageFilter))
                    whereClause += " AND c.package_id = @Package";

                if (statusFilter == "Lulus")
                    whereClause += " AND ta.is_pass = 1";
                else if (statusFilter == "BelumLulus")
                    whereClause += " AND ta.is_pass = 0";
                else if (statusFilter == "BelumSubmit")
                    whereClause += " AND ta.is_pass IS NULL";

                using (var cmd = new SqlCommand($"SELECT COUNT(*) {baseFrom} {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@Package", packageFilter);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                using (var cmd = new SqlCommand($@"
                    SELECT
                        SUM(CASE WHEN ta.is_pass = 1 THEN 1 ELSE 0 END) AS PassCount,
                        SUM(CASE WHEN ta.is_pass = 0 THEN 1 ELSE 0 END) AS FailCount,
                        SUM(CASE WHEN ta.is_pass IS NULL THEN 1 ELSE 0 END) AS PendingCount
                    {baseFrom} {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@Package", packageFilter);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalPass = reader["PassCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["PassCount"]);
                        totalFail = reader["FailCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["FailCount"]);
                        totalPending = reader["PendingCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["PendingCount"]);
                    }
                }

                string sql = $@"
                    SELECT
                        ta.assessment_id,
                        c.serial_number,
                        p.name AS package_name,
                        a.title AS audiobook_title,
                        q.title AS quiz_title,
                        ta.total_point,
                        ta.minimum_point,
                        ta.is_pass
                    {baseFrom}
                    {whereClause}
                    ORDER BY c.serial_number ASC
                    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@Package", packageFilter);
                    cmd.Parameters.AddWithValue("@Skip", req.Skip);
                    cmd.Parameters.AddWithValue("@Take", req.Take);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        bool? isPass = reader["is_pass"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(reader["is_pass"]);
                        string status = isPass == null ? "Belum Submit" : (isPass.Value ? "Lulus" : "Belum Lulus");

                        list.Add(new
                        {
                            assessment_id = reader["assessment_id"].ToString(),
                            serial_number = reader["serial_number"]?.ToString(),
                            package_name = reader["package_name"]?.ToString(),
                            audiobook_title = reader["audiobook_title"]?.ToString() ?? "-",
                            quiz_title = reader["quiz_title"]?.ToString() ?? "-",
                            total_point = reader["total_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["total_point"]),
                            minimum_point = reader["minimum_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["minimum_point"]),
                            status
                        });
                    }
                }

                return Json(new
                {
                    draw = req.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = filteredRecords,
                    totalPass,
                    totalFail,
                    totalPending,
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
                    totalPass = 0,
                    totalFail = 0,
                    totalPending = 0,
                    data = new List<object>(),
                    error = ex.Message
                });
            }
        }

        // ===============================
        // DETAIL (soal + jawaban 1 attempt, buat modal)
        // ===============================
        [HttpGet]
        [Route("admin/monitoring/assessment/detail/{id}")]
        public IActionResult DetailAssessment(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                object header = null;

                using (var cmd = new SqlCommand(@"
                    SELECT ta.assessment_id, c.serial_number, a.title AS audiobook_title,q.title AS quiz_title,
                           ta.total_point, ta.minimum_point, ta.is_pass
                    FROM txn_assessment ta
                    INNER JOIN txn_code c ON ta.code_id = c.code_id
                    LEFT JOIN txn_audiobook a ON ta.audiobook_id = a.audiobook_id
                    LEFT JOIN mst_quiz q ON ta.quiz_id = q.quiz_id
                    WHERE ta.assessment_id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using var reader = cmd.ExecuteReader();
                    if (!reader.Read())
                        return Json(new ResponseDto { Code = 404, Message = "Assessment not found." });

                    bool? isPass = reader["is_pass"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(reader["is_pass"]);
                    header = new
                    {
                        assessment_id = reader["assessment_id"].ToString(),
                        serial_number = reader["serial_number"]?.ToString(),
                        audiobook_title = reader["audiobook_title"]?.ToString() ?? "-",
                        quiz_title = reader["quiz_title"]?.ToString() ?? "-",
                        total_point = reader["total_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["total_point"]),
                        minimum_point = reader["minimum_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["minimum_point"]),
                        status = isPass == null ? "Belum Submit" : (isPass.Value ? "Lulus" : "Belum Lulus")
                    };
                }

                var questions = new List<AssessmentDetailQuestion>();

                using (var cmd = new SqlCommand(@"
                    SELECT assessment_question_id, question, question_type, point
                    FROM txn_assessment_question
                    WHERE assessment_id = @Id
                    ORDER BY assessment_question_id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        questions.Add(new AssessmentDetailQuestion
                        {
                            AssessmentQuestionId = reader["assessment_question_id"].ToString(),
                            Question = reader["question"]?.ToString(),
                            QuestionType = reader["question_type"]?.ToString(),
                            Point = reader["point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["point"])
                        });
                    }
                }

                foreach (var q in questions)
                {
                    using var cmd = new SqlCommand(@"
                        SELECT option_text, is_correct, is_selected
                        FROM txn_assessment_option
                        WHERE assessment_question_id = @QId
                        ORDER BY assessment_option_id", conn);
                    cmd.Parameters.AddWithValue("@QId", q.AssessmentQuestionId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        q.Options.Add(new AssessmentDetailOption
                        {
                            OptionText = reader["option_text"]?.ToString(),
                            IsCorrect = Convert.ToBoolean(reader["is_correct"]),
                            IsSelected = reader["is_selected"] != DBNull.Value && Convert.ToBoolean(reader["is_selected"])
                        });
                    }
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = new { header, questions }
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        [Route("admin/monitoring/reading")]
        public IActionResult Reading()
        {
            return View("~/Views/PortalAdmin/Monitoring/Reading.cshtml");
        }

        // ===============================
        // LOAD (list + search + filter package/status + pagination + summary count)
        // ===============================
        [HttpPost]
        [Route("admin/monitoring/reading/load")]
        public IActionResult LoadReading([FromBody] RequestDto req)
        {
            int totalRecords = 0;
            int filteredRecords = 0;
            int totalFinished = 0, totalOngoing = 0;
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM txn_reading", conn))
                    totalRecords = (int)cmd.ExecuteScalar();

                string search = req.Data?.ToString() ?? "";
                string packageFilter = req.Package?.ToString() ?? "";
                string statusFilter = req.Status?.ToString() ?? "";
                string searchPattern = $"%{search}%";

                string baseFrom = @"
            FROM txn_reading tr
            INNER JOIN txn_code c ON tr.code_id = c.code_id
            LEFT JOIN txn_audiobook a ON tr.audiobook_id = a.audiobook_id
            LEFT JOIN mst_package p ON c.package_id = p.package_id";

                string whereClause = "WHERE (c.serial_number LIKE @Search OR a.title LIKE @Search)";

                if (!string.IsNullOrEmpty(packageFilter))
                    whereClause += " AND c.package_id = @Package";

                if (statusFilter == "Selesai")
                    whereClause += " AND tr.finish_date IS NOT NULL";
                else if (statusFilter == "Proses")
                    whereClause += " AND tr.finish_date IS NULL";

                using (var cmd = new SqlCommand($"SELECT COUNT(*) {baseFrom} {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@Package", packageFilter);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                using (var cmd = new SqlCommand($@"
            SELECT
                SUM(CASE WHEN tr.finish_date IS NOT NULL THEN 1 ELSE 0 END) AS FinishedCount,
                SUM(CASE WHEN tr.finish_date IS NULL THEN 1 ELSE 0 END) AS OngoingCount
            {baseFrom} {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@Package", packageFilter);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalFinished = reader["FinishedCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["FinishedCount"]);
                        totalOngoing = reader["OngoingCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["OngoingCount"]);
                    }
                }

                string sql = $@"
            SELECT
                tr.reading_id,
                c.serial_number,
                p.name AS package_name,
                a.title AS audiobook_title,
                tr.start_date,
                tr.finish_date
            {baseFrom}
            {whereClause}
            ORDER BY tr.start_date DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(packageFilter))
                        cmd.Parameters.AddWithValue("@Package", packageFilter);
                    cmd.Parameters.AddWithValue("@Skip", req.Skip);
                    cmd.Parameters.AddWithValue("@Take", req.Take);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        DateTime? startDate = reader["start_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["start_date"]);
                        DateTime? finishDate = reader["finish_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["finish_date"]);
                        string status = finishDate.HasValue ? "Selesai" : "Sedang Membaca";

                        list.Add(new
                        {
                            reading_id = reader["reading_id"].ToString(),
                            serial_number = reader["serial_number"]?.ToString(),
                            package_name = reader["package_name"]?.ToString(),
                            audiobook_title = reader["audiobook_title"]?.ToString() ?? "-",
                            start_date = startDate,
                            finish_date = finishDate,
                            status
                        });
                    }
                }

                return Json(new
                {
                    draw = req.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = filteredRecords,
                    totalFinished,
                    totalOngoing,
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
                    totalFinished = 0,
                    totalOngoing = 0,
                    data = new List<object>(),
                    error = ex.Message
                });
            }
        }

        // ===============================
        // DETAIL (info reading + preview bacaan, buat modal)
        // ===============================
        [HttpGet]
        [Route("admin/monitoring/reading/detail/{id}")]
        public IActionResult DetailReading(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(@"
            SELECT tr.reading_id, c.serial_number, a.title AS audiobook_title,
                   tr.start_date, tr.finish_date, s.description
            FROM txn_reading tr
            INNER JOIN txn_code c ON tr.code_id = c.code_id
            LEFT JOIN txn_audiobook a ON tr.audiobook_id = a.audiobook_id
            LEFT JOIN mst_summary s ON tr.summary_id = s.summary_id
            WHERE tr.reading_id = @Id", conn);
                cmd.Parameters.AddWithValue("@Id", id);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return Json(new ResponseDto { Code = 404, Message = "Reading not found." });

                DateTime? startDate = reader["start_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["start_date"]);
                DateTime? finishDate = reader["finish_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["finish_date"]);

                var header = new ReadingDetailHeader
                {
                    ReadingId = reader["reading_id"].ToString(),
                    SerialNumber = reader["serial_number"]?.ToString(),
                    AudiobookTitle = reader["audiobook_title"]?.ToString() ?? "-",
                    StartDate = startDate,
                    FinishDate = finishDate,
                    Status = finishDate.HasValue ? "Selesai" : "Sedang Membaca",
                    Description = reader["description"]?.ToString() ?? ""
                };

                return Json(new ResponseDto { Code = 200, Message = "Success", Data = header });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // DROPDOWN LIST PACKAGE (buat filter)
        // ===============================
        [HttpGet]
        [Route("admin/monitoring/list-package")]
        public IActionResult ListPackage()
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

            return Json(new ResponseDto { Code = 200, Message = "Success", Data = list });
        }
    }
}