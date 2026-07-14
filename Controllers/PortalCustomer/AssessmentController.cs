using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Filters;
using suara_belajar.Models.Assessment;
using System.Data.SqlClient;

namespace suara_belajar.Controllers.PortalCustomer
{
    [AuthorizeCustomer]
    public class AssessmentController : Controller
    {
        private readonly IConfiguration _config;

        public AssessmentController(IConfiguration config)
        {
            _config = config;
        }

        [Route("customer/assessment")]
        public IActionResult Index()
        {
            return View("~/Views/PortalCustomer/Audiobook/Assessment.cshtml");
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

        [HttpGet]
        [Route("customer/assessment/get")]
        public IActionResult GetAssessment(string audiobookId)
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

                // 1. Ambil master quiz utk audiobook ini
                string quizId = null, quizTitle = null;
                int minimumPoint = 0;

                using (var cmd = new SqlCommand(@"
                    SELECT quiz_id, title, minimum_point
                    FROM mst_quiz
                    WHERE audiobook_id = @AudiobookId", conn))
                {
                    cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        quizId = reader["quiz_id"].ToString();
                        quizTitle = reader["title"]?.ToString();
                        minimumPoint = reader["minimum_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["minimum_point"]);
                    }
                }

                if (quizId == null)
                    return Json(new ResponseDto { Code = 404, Message = "Belum ada quiz untuk audiobook ini." });

                // 2. Cek apakah code_id ini sudah punya attempt utk audiobook ini
                string assessmentId = null;
                bool? isPass = null;
                int totalPointSaved = 0;

                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1 assessment_id, is_pass, total_point
                    FROM txn_assessment
                    WHERE code_id = @CodeId AND quiz_id = @QuizId
                    ORDER BY assessment_id DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@CodeId", codeId);
                    cmd.Parameters.AddWithValue("@QuizId", quizId);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        assessmentId = reader["assessment_id"].ToString();
                        isPass = reader["is_pass"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(reader["is_pass"]);
                        totalPointSaved = reader["total_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["total_point"]);
                    }
                }

                bool alreadySubmitted = isPass.HasValue;

                // 3. Kalau belum pernah attempt sama sekali -> buat baru + copy soal & opsi dari master
                if (assessmentId == null)
                {
                    assessmentId = Guid.NewGuid().ToString();

                    using (var cmd = new SqlCommand(@"
                        INSERT INTO txn_assessment (assessment_id, code_id, audiobook_id, quiz_id, minimum_point, total_point, is_pass)
                        VALUES (@AssessmentId, @CodeId, @AudiobookId, @QuizId, @MinimumPoint, 0, NULL)", conn))
                    {
                        cmd.Parameters.AddWithValue("@AssessmentId", assessmentId);
                        cmd.Parameters.AddWithValue("@CodeId", codeId);
                        cmd.Parameters.AddWithValue("@AudiobookId", audiobookId);
                        cmd.Parameters.AddWithValue("@QuizId", quizId);
                        cmd.Parameters.AddWithValue("@MinimumPoint", minimumPoint);
                        cmd.ExecuteNonQuery();
                    }

                    var questionIds = new List<string>();
                    using (var cmd = new SqlCommand(
                        "SELECT quiz_question_id FROM mst_quiz_question WHERE quiz_id = @QuizId", conn))
                    {
                        cmd.Parameters.AddWithValue("@QuizId", quizId);
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                            questionIds.Add(reader["quiz_question_id"].ToString());
                    }

                    foreach (var oldQuestionId in questionIds)
                    {
                        string newQuestionId = Guid.NewGuid().ToString();

                        using (var cmd = new SqlCommand(@"
                            INSERT INTO txn_assessment_question (assessment_question_id, assessment_id, question, question_type, point)
                            SELECT @NewId, @AssessmentId, question, question_type, point
                            FROM mst_quiz_question WHERE quiz_question_id = @OldId", conn))
                        {
                            cmd.Parameters.AddWithValue("@NewId", newQuestionId);
                            cmd.Parameters.AddWithValue("@AssessmentId", assessmentId);
                            cmd.Parameters.AddWithValue("@OldId", oldQuestionId);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SqlCommand(@"
                            INSERT INTO txn_assessment_option (assessment_option_id, assessment_question_id, option_text, is_correct, is_selected)
                            SELECT NEWID(), @NewQuestionId, option_text, is_correct, 0
                            FROM mst_quiz_option WHERE quiz_question_id = @OldQuestionId", conn))
                        {
                            cmd.Parameters.AddWithValue("@NewQuestionId", newQuestionId);
                            cmd.Parameters.AddWithValue("@OldQuestionId", oldQuestionId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    isPass = null;
                    totalPointSaved = 0;
                }

                // 4. Ambil soal + opsi dari txn (bukan dari master lagi), TANPA expose is_correct ke client
                var questions = new List<QuestionItem>();

                using (var cmd = new SqlCommand(@"
                    SELECT assessment_question_id, question, question_type, point
                    FROM txn_assessment_question
                    WHERE assessment_id = @AssessmentId
                    ORDER BY assessment_question_id", conn))
                {
                    cmd.Parameters.AddWithValue("@AssessmentId", assessmentId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        questions.Add(new QuestionItem
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
                        SELECT assessment_option_id, option_text, is_selected
                        FROM txn_assessment_option
                        WHERE assessment_question_id = @QuestionId
                        ORDER BY assessment_option_id", conn);
                    cmd.Parameters.AddWithValue("@QuestionId", q.AssessmentQuestionId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        q.Options.Add(new OptionItem
                        {
                            AssessmentOptionId = reader["assessment_option_id"].ToString(),
                            OptionText = reader["option_text"]?.ToString(),
                            IsSelected = reader["is_selected"] != DBNull.Value && Convert.ToBoolean(reader["is_selected"])
                        });
                    }
                }

                int totalPoint = questions.Sum(q => q.Point);

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = new
                    {
                        assessment_id = assessmentId,
                        quiz_title = quizTitle,
                        total_question = questions.Count,
                        total_point = totalPoint,
                        minimum_point = minimumPoint,
                        already_submitted = alreadySubmitted,
                        is_pass = isPass,
                        score = totalPointSaved,
                        questions = questions.Select(q => new
                        {
                            assessment_question_id = q.AssessmentQuestionId,
                            question = q.Question,
                            question_type = q.QuestionType,
                            point = q.Point,
                            options = q.Options.Select(o => new
                            {
                                assessment_option_id = o.AssessmentOptionId,
                                option_text = o.OptionText,
                                is_selected = o.IsSelected
                            })
                        })
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // SUBMIT ASSESSMENT
        // - Simpan opsi yang dipilih
        // - Hitung total_point (soal dianggap benar kalau semua opsi correct kepilih & gak ada opsi salah yg kepilih)
        // - Update is_pass
        // ===============================
        [HttpPost]
        [Route("customer/assessment/submit")]
        public IActionResult Submit([FromBody] SubmitAssessmentRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.AssessmentId))
                    return Json(new ResponseDto { Code = 400, Message = "Assessment ID is required." });

                if (req.Answers == null || req.Answers.Count == 0)
                    return Json(new ResponseDto { Code = 400, Message = "Jawaban tidak boleh kosong." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();
                using var trans = conn.BeginTransaction();

                try
                {
                    string audiobookId = null;
                    int minimumPoint = 0;
                    bool? currentIsPass = null;

                    using (var cmd = new SqlCommand(@"
                        SELECT audiobook_id, minimum_point, is_pass
                        FROM txn_assessment WITH (UPDLOCK, ROWLOCK)
                        WHERE assessment_id = @AssessmentId", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@AssessmentId", req.AssessmentId);
                        using var reader = cmd.ExecuteReader();
                        if (!reader.Read())
                        {
                            trans.Rollback();
                            return Json(new ResponseDto { Code = 404, Message = "Assessment not found." });
                        }
                        audiobookId = reader["audiobook_id"]?.ToString();
                        minimumPoint = reader["minimum_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["minimum_point"]);
                        currentIsPass = reader["is_pass"] == DBNull.Value ? (bool?)null : Convert.ToBoolean(reader["is_pass"]);
                    }

                    // Cuma blokir kalau udah PERNAH LULUS. Kalau belum lulus (false) atau belum pernah submit (null), boleh lanjut.
                    if (currentIsPass == true)
                    {
                        trans.Rollback();
                        return Json(new ResponseDto { Code = 400, Message = "Assessment ini sudah lulus, tidak bisa diisi ulang." });
                    }

                    // Reset dulu (jaga-jaga kalau ada retry request)
                    using (var cmd = new SqlCommand(@"
                        UPDATE o SET o.is_selected = 0
                        FROM txn_assessment_option o
                        INNER JOIN txn_assessment_question q ON o.assessment_question_id = q.assessment_question_id
                        WHERE q.assessment_id = @AssessmentId", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@AssessmentId", req.AssessmentId);
                        cmd.ExecuteNonQuery();
                    }

                    // Simpan opsi yang dipilih
                    foreach (var ans in req.Answers)
                    {
                        if (ans.SelectedOptionIds == null) continue;
                        foreach (var optId in ans.SelectedOptionIds)
                        {
                            using var cmd = new SqlCommand(@"
                                UPDATE txn_assessment_option
                                SET is_selected = 1
                                WHERE assessment_option_id = @OptionId AND assessment_question_id = @QuestionId", conn, trans);
                            cmd.Parameters.AddWithValue("@OptionId", optId);
                            cmd.Parameters.AddWithValue("@QuestionId", ans.AssessmentQuestionId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // Hitung skor
                    var questionPoints = new List<(string Id, int Point)>();
                    using (var cmd = new SqlCommand(@"
                        SELECT assessment_question_id, point
                        FROM txn_assessment_question
                        WHERE assessment_id = @AssessmentId", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@AssessmentId", req.AssessmentId);
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            questionPoints.Add((
                                reader["assessment_question_id"].ToString(),
                                reader["point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["point"])));
                        }
                    }

                    int totalPoint = 0;

                    foreach (var (questionId, point) in questionPoints)
                    {
                        using var cmd = new SqlCommand(@"
                            SELECT
                                SUM(CASE WHEN is_correct = 1 AND is_selected = 0 THEN 1 ELSE 0 END) AS missed_correct,
                                SUM(CASE WHEN is_correct = 0 AND is_selected = 1 THEN 1 ELSE 0 END) AS wrong_selected
                            FROM txn_assessment_option
                            WHERE assessment_question_id = @QuestionId", conn, trans);
                        cmd.Parameters.AddWithValue("@QuestionId", questionId);

                        using var reader = cmd.ExecuteReader();
                        if (reader.Read())
                        {
                            int missedCorrect = reader["missed_correct"] == DBNull.Value ? 0 : Convert.ToInt32(reader["missed_correct"]);
                            int wrongSelected = reader["wrong_selected"] == DBNull.Value ? 0 : Convert.ToInt32(reader["wrong_selected"]);

                            if (missedCorrect == 0 && wrongSelected == 0)
                                totalPoint += point;
                        }
                    }

                    bool isPass = totalPoint >= minimumPoint;

                    using (var cmd = new SqlCommand(@"
                        UPDATE txn_assessment
                        SET total_point = @TotalPoint, is_pass = @IsPass
                        WHERE assessment_id = @AssessmentId", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@TotalPoint", totalPoint);
                        cmd.Parameters.AddWithValue("@IsPass", isPass);
                        cmd.Parameters.AddWithValue("@AssessmentId", req.AssessmentId);
                        cmd.ExecuteNonQuery();
                    }

                    trans.Commit();

                    return Json(new ResponseDto
                    {
                        Code = 200,
                        Message = isPass ? "Selamat, kamu lulus!" : "Maaf, kamu belum lulus.",
                        Data = new
                        {
                            total_point = totalPoint,
                            minimum_point = minimumPoint,
                            is_pass = isPass
                        }
                    });
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // Helper class internal, gak perlu file terpisah kalau mau
        private class QuestionItem
        {
            public string AssessmentQuestionId { get; set; }
            public string Question { get; set; }
            public string QuestionType { get; set; }
            public int Point { get; set; }
            public List<OptionItem> Options { get; set; } = new();
        }

        private class OptionItem
        {
            public string AssessmentOptionId { get; set; }
            public string OptionText { get; set; }
            public bool IsSelected { get; set; }
        }
    }
}