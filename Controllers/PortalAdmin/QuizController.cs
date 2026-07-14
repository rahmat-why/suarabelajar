using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using suara_belajar.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Linq;

namespace suara_belajar.Controllers.PortalAdmin
{
    [AuthorizeAdmin]
    public class QuizController : Controller
    {
        private readonly IConfiguration _config;

        public QuizController(IConfiguration config)
        {
            _config = config;
        }

        [Route("admin/quiz/index")]
        public IActionResult Index()
        {
            return View("~/Views/PortalAdmin/Quiz/Index.cshtml");
        }

        [Route("admin/quiz/create")]
        public IActionResult Create()
        {
            return View("~/Views/PortalAdmin/Quiz/Create.cshtml");
        }

        [Route("admin/quiz/edit/{id}")]
        public IActionResult Edit(string id)
        {
            return View("~/Views/PortalAdmin/Quiz/Edit.cshtml");
        }

        // ===============================
        // LOAD (list, search, filter audiobook, pagination)
        // ===============================
        [HttpPost]
        [Route("admin/quiz/load")]
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

                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM mst_quiz", conn))
                {
                    totalRecords = (int)cmd.ExecuteScalar();
                }

                string whereClause = "WHERE (q.title LIKE @Search)";
                if (!string.IsNullOrEmpty(audiobookFilter))
                    whereClause += " AND q.audiobook_id = @AudiobookId";

                using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM mst_quiz q {whereClause}", conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchPattern);
                    if (!string.IsNullOrEmpty(audiobookFilter))
                        cmd.Parameters.AddWithValue("@AudiobookId", audiobookFilter);
                    filteredRecords = (int)cmd.ExecuteScalar();
                }

                string sql = $@"
                    SELECT
                        q.quiz_id,
                        q.audiobook_id,
                        a.title AS audiobook_title,
                        q.title,
                        q.minimum_point,
                        (SELECT COUNT(*) FROM mst_quiz_question qq WHERE qq.quiz_id = q.quiz_id) AS question_count
                    FROM mst_quiz q
                    LEFT JOIN txn_audiobook a ON q.audiobook_id = a.audiobook_id
                    {whereClause}
                    ORDER BY q.title ASC
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
                        list.Add(new
                        {
                            quiz_id = reader["quiz_id"].ToString(),
                            audiobook_id = reader["audiobook_id"]?.ToString(),
                            audiobook_title = reader["audiobook_title"]?.ToString(),
                            title = reader["title"]?.ToString(),
                            minimum_point = reader["minimum_point"] == DBNull.Value ? 0 : Convert.ToInt32(reader["minimum_point"]),
                            question_count = reader["question_count"] == DBNull.Value ? 0 : Convert.ToInt32(reader["question_count"])
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
        // GET BY ID (populate form Edit, termasuk questions + options)
        // ===============================
        [HttpGet]
        [Route("admin/quiz/get/{id}")]
        public IActionResult Get(string id)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                QuizDetail quiz = null;

                using (var cmd = new SqlCommand(@"
                    SELECT quiz_id, audiobook_id, title, minimum_point
                    FROM mst_quiz
                    WHERE quiz_id = @QuizId", conn))
                {
                    cmd.Parameters.AddWithValue("@QuizId", id);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        quiz = new QuizDetail
                        {
                            QuizId = reader["quiz_id"].ToString(),
                            AudiobookId = reader["audiobook_id"]?.ToString(),
                            Title = reader["title"]?.ToString(),
                            MinimumPoint = Convert.ToInt32(reader["minimum_point"])
                        };
                    }
                }

                if (quiz == null)
                    return Json(new ResponseDto { Code = 404, Message = "Quiz not found." });

                using (var cmd = new SqlCommand(@"
                    SELECT quiz_question_id, question, question_type, point
                    FROM mst_quiz_question
                    WHERE quiz_id = @QuizId
                    ORDER BY quiz_question_id", conn))
                {
                    cmd.Parameters.AddWithValue("@QuizId", id);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        quiz.Questions.Add(new QuizQuestionDto
                        {
                            QuizQuestionId = reader["quiz_question_id"].ToString(),
                            Question = reader["question"]?.ToString(),
                            QuestionType = reader["question_type"]?.ToString(),
                            Point = Convert.ToInt32(reader["point"])
                        });
                    }
                }

                foreach (var q in quiz.Questions)
                {
                    using var cmd = new SqlCommand(@"
                        SELECT quiz_option_id, option_text, is_correct
                        FROM mst_quiz_option
                        WHERE quiz_question_id = @QuizQuestionId
                        ORDER BY quiz_option_id", conn);
                    cmd.Parameters.AddWithValue("@QuizQuestionId", q.QuizQuestionId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        q.Options.Add(new QuizOptionDto
                        {
                            QuizOptionId = reader["quiz_option_id"].ToString(),
                            OptionText = reader["option_text"]?.ToString(),
                            IsCorrect = Convert.ToBoolean(reader["is_correct"])
                        });
                    }
                }

                return Json(new ResponseDto { Code = 200, Message = "Success", Data = quiz });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // SAVE (create / update) - JSON body, replace-all strategy utk question/option
        // ===============================
        [HttpPost]
        [Route("admin/quiz/save")]
        public IActionResult Save([FromBody] QuizSaveRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.QuizId) || string.IsNullOrWhiteSpace(req.AudiobookId)
                    || string.IsNullOrWhiteSpace(req.Title))
                {
                    return Json(new ResponseDto { Code = 400, Message = "Quiz ID, Audiobook, and Title are required." });
                }

                if (req.Questions == null || req.Questions.Count == 0)
                    return Json(new ResponseDto { Code = 400, Message = "At least 1 question is required." });

                foreach (var q in req.Questions)
                {
                    if (string.IsNullOrWhiteSpace(q.Question))
                        return Json(new ResponseDto { Code = 400, Message = "Question text cannot be empty." });

                    if (q.Options == null || q.Options.Count < 2)
                        return Json(new ResponseDto { Code = 400, Message = $"Question '{q.Question}' needs at least 2 options." });

                    if (!q.Options.Any(o => o.IsCorrect))
                        return Json(new ResponseDto { Code = 400, Message = $"Question '{q.Question}' needs at least 1 correct answer." });
                }

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();
                using var trans = conn.BeginTransaction();

                try
                {
                    using (var checkAudiobook = new SqlCommand(
                        "SELECT COUNT(*) FROM txn_audiobook WHERE audiobook_id = @AudiobookId AND deleted_date IS NULL", conn, trans))
                    {
                        checkAudiobook.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                        int exists = (int)checkAudiobook.ExecuteScalar();
                        if (exists == 0)
                        {
                            trans.Rollback();
                            return Json(new ResponseDto { Code = 400, Message = "Selected audiobook is invalid or inactive." });
                        }
                    }

                    if (req.IsEdit)
                    {
                        using (var cmd = new SqlCommand(@"
                            UPDATE mst_quiz
                            SET audiobook_id = @AudiobookId, title = @Title, minimum_point = @MinimumPoint
                            WHERE quiz_id = @QuizId", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                                cmd.Parameters.AddWithValue("@Title", req.Title);
                                cmd.Parameters.AddWithValue("@MinimumPoint", req.MinimumPoint);
                                cmd.Parameters.AddWithValue("@QuizId", req.QuizId);

                                int rows = cmd.ExecuteNonQuery();
                                if (rows == 0)
                                {
                                    trans.Rollback();
                                    return Json(new ResponseDto { Code = 404, Message = "Quiz not found." });
                                }
                            }

                            // Hapus option dulu (child), baru question (parent)
                            using (var cmd = new SqlCommand(@"
                                DELETE o FROM mst_quiz_option o
                                INNER JOIN mst_quiz_question q ON o.quiz_question_id = q.quiz_question_id
                                WHERE q.quiz_id = @QuizId", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@QuizId", req.QuizId);
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = new SqlCommand(
                                "DELETE FROM mst_quiz_question WHERE quiz_id = @QuizId", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@QuizId", req.QuizId);
                                cmd.ExecuteNonQuery();
                            }
                    }
                    else
                    {
                        using (var checkCmd = new SqlCommand(
                            "SELECT COUNT(*) FROM mst_quiz WHERE quiz_id = @QuizId", conn, trans))
                        {
                            checkCmd.Parameters.AddWithValue("@QuizId", req.QuizId);
                            int exists = (int)checkCmd.ExecuteScalar();
                            if (exists > 0)
                            {
                                trans.Rollback();
                                return Json(new ResponseDto { Code = 400, Message = "Quiz ID already exists." });
                            }
                        }

                        using (var cmd = new SqlCommand(@"
                            INSERT INTO mst_quiz (quiz_id, audiobook_id, title, minimum_point)
                            VALUES (@QuizId, @AudiobookId, @Title, @MinimumPoint)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@QuizId", req.QuizId);
                            cmd.Parameters.AddWithValue("@AudiobookId", req.AudiobookId);
                            cmd.Parameters.AddWithValue("@Title", req.Title);
                            cmd.Parameters.AddWithValue("@MinimumPoint", req.MinimumPoint);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // Insert question + option (fresh, ID baru pakai GUID)
                    foreach (var q in req.Questions)
                    {
                        string questionId = Guid.NewGuid().ToString();

                        using (var cmd = new SqlCommand(@"
                            INSERT INTO mst_quiz_question (quiz_question_id, quiz_id, question, question_type, point)
                            VALUES (@QuestionId, @QuizId, @Question, @QuestionType, @Point)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@QuestionId", questionId);
                            cmd.Parameters.AddWithValue("@QuizId", req.QuizId);
                            cmd.Parameters.AddWithValue("@Question", q.Question);
                            cmd.Parameters.AddWithValue("@QuestionType", string.IsNullOrEmpty(q.QuestionType) ? "single_choice" : q.QuestionType);
                            cmd.Parameters.AddWithValue("@Point", q.Point);
                            cmd.ExecuteNonQuery();
                        }

                        foreach (var o in q.Options)
                        {
                            using var cmd = new SqlCommand(@"
                                INSERT INTO mst_quiz_option (quiz_option_id, quiz_question_id, option_text, is_correct)
                                VALUES (@OptionId, @QuestionId, @OptionText, @IsCorrect)", conn, trans);
                            cmd.Parameters.AddWithValue("@OptionId", Guid.NewGuid().ToString());
                            cmd.Parameters.AddWithValue("@QuestionId", questionId);
                            cmd.Parameters.AddWithValue("@OptionText", o.OptionText);
                            cmd.Parameters.AddWithValue("@IsCorrect", o.IsCorrect);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    trans.Commit();

                    return Json(new ResponseDto
                    {
                        Code = 200,
                        Message = req.IsEdit ? "Quiz updated successfully." : "Quiz created successfully."
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

        // ===============================
        // DELETE (HARD DELETE - cascade ke question & option)
        // ===============================
        [HttpPost]
        [Route("admin/quiz/delete")]
        public IActionResult Delete([FromBody] RequestDto req)
        {
            try
            {
                string quizId = req.Quiz;

                if (string.IsNullOrWhiteSpace(quizId))
                    return Json(new ResponseDto { Code = 400, Message = "Quiz ID is required." });

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new SqlCommand(
                    "DELETE FROM mst_quiz WHERE quiz_id = @QuizId", conn);
                cmd.Parameters.AddWithValue("@QuizId", quizId);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return Json(new ResponseDto { Code = 404, Message = "Quiz not found." });

                return Json(new ResponseDto { Code = 200, Message = "Quiz deleted permanently." });
            }
            catch (Exception ex)
            {
                return Json(new ResponseDto { Code = 500, Message = ex.Message });
            }
        }

        // ===============================
        // DROPDOWN LIST AUDIOBOOK (active)
        // ===============================
        [HttpGet]
        [Route("admin/quiz/list-audiobook")]
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