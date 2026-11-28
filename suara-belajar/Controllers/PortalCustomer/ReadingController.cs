using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Filters;
using suara_belajar.Models;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalCustomer
{
    [AuthorizeCustomer]
    public class ReadingController : Controller
    {
        private readonly IConfiguration _config;

        public ReadingController(IConfiguration config)
        {
            _config = config;
        }

        [Route("customer/reading")]
        public IActionResult Index()
        {
            return View("~/Views/PortalCustomer/Audiobook/Reading.cshtml");
        }

        // Helper: cookie REDEEM_CODE (serial_number) -> txn_code.code_id
        private string GetCodeId(SqlConnection conn)
        {
            string serial = Request.Cookies["REDEEM_CODE"];
            if (string.IsNullOrWhiteSpace(serial)) return null;

            using var cmd = new SqlCommand(
                "SELECT code_id FROM txn_code WHERE serial_number = @Serial", conn);
            cmd.Parameters.AddWithValue("@Serial", serial);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        // ===============================
        // GET READING
        // - Ambil summary (mst_summary) utk audiobook ini
        // - Kalau belum ada txn_reading utk code_id+audiobook ini -> insert baru (start_date = now)
        // - Kalau sudah ada -> pakai yang lama (gak bikin baru tiap refresh)
        // ===============================
        [HttpGet]
        [Route("customer/reading/get")]
        public IActionResult GetReading(string audiobookId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(audiobookId))
                    return Json(new ResponseDto { Code = 400, Message = "Audiobook ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string codeId = GetCodeId(conn);
                if (string.IsNullOrWhiteSpace(codeId))
                    return Json(new ResponseDto { Code = 401, Message = "Redeem code tidak ditemukan." });

                // 1. Ambil summary utk audiobook ini
                string summaryId = null, description = null, audiobookTitle = null;

                using (var cmd = new SqlCommand(@"
                    SELECT s.summary_id, s.description, a.title
                    FROM mst_summary s
                    INNER JOIN txn_audiobook a ON s.audiobook_id = a.audiobook_id
                    WHERE s.audiobook_id = @AudiobookId", conn))
                {
                    cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        summaryId = reader["summary_id"].ToString();
                        description = reader["description"]?.ToString();
                        audiobookTitle = reader["title"]?.ToString();
                    }
                }

                if (summaryId == null)
                    return Json(new ResponseDto { Code = 404, Message = "Belum ada bacaan untuk audiobook ini." });

                // 2. Cek apakah code_id ini sudah punya record reading utk audiobook ini
                string readingId = null;
                DateTime? startDate = null, finishDate = null;

                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1 reading_id, start_date, finish_date
                    FROM txn_reading
                    WHERE code_id = @CodeId AND audiobook_id = @AudiobookId
                    ORDER BY start_date DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@CodeId", codeId);
                    cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        readingId = reader["reading_id"].ToString();
                        startDate = reader["start_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["start_date"]);
                        finishDate = reader["finish_date"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["finish_date"]);
                    }
                }

                // 3. Kalau belum pernah mulai baca -> insert baru
                if (readingId == null)
                {
                    readingId = Guid.NewGuid().ToString();
                    startDate = DateTime.Now;

                    using var cmd = new SqlCommand(@"
                        INSERT INTO txn_reading (reading_id, code_id, audiobook_id, summary_id, start_date, finish_date)
                        VALUES (@ReadingId, @CodeId, @AudiobookId, @SummaryId, @StartDate, NULL)", conn);
                    cmd.Parameters.AddWithValue("@ReadingId", readingId);
                    cmd.Parameters.AddWithValue("@CodeId", codeId);
                    cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);
                    cmd.Parameters.AddWithValue("@SummaryId", summaryId);
                    cmd.Parameters.AddWithValue("@StartDate", startDate);
                    cmd.ExecuteNonQuery();
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = new
                    {
                        reading_id = readingId,
                        audiobook_title = audiobookTitle,
                        description,
                        start_date = startDate,
                        finish_date = finishDate,
                        is_finished = finishDate.HasValue
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // FINISH READING
        // ===============================
        [HttpPost]
        [Route("customer/reading/finish")]
        public IActionResult FinishReading([FromBody] FinishReadingRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.ReadingId))
                    return Json(new ResponseDto { Code = 400, Message = "Reading ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                string codeId = GetCodeId(conn);
                if (string.IsNullOrWhiteSpace(codeId))
                    return Json(new ResponseDto { Code = 401, Message = "Redeem code tidak ditemukan." });

                // Pastikan reading ini emang milik code_id yang lagi login (jangan bisa nyelesain punya orang lain)
                using (var checkCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM txn_reading WHERE reading_id = @ReadingId AND code_id = @CodeId", conn))
                {
                    checkCmd.Parameters.AddWithValue("@ReadingId", req.ReadingId);
                    checkCmd.Parameters.AddWithValue("@CodeId", codeId);
                    int exists = (int)checkCmd.ExecuteScalar();
                    if (exists == 0)
                        return Json(new ResponseDto { Code = 404, Message = "Reading not found." });
                }

                using (var cmd = new SqlCommand(@"
                    UPDATE txn_reading
                    SET finish_date = GETDATE()
                    WHERE reading_id = @ReadingId AND finish_date IS NULL", conn))
                {
                    cmd.Parameters.AddWithValue("@ReadingId", req.ReadingId);
                    cmd.ExecuteNonQuery();
                }

                return Json(new ResponseDto { Code = 200, Message = "Selamat, kamu sudah menyelesaikan bacaan ini." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }
    }
}