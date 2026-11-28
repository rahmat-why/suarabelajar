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
        // LOAD (list, search, filter series, pagination)
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
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                string search = req.Data?.ToString() ?? "";
                string seriesFilter = req.Series?.ToString() ?? "";
                string searchPattern = $"%{search}%";

                // ===============================
                // TOTAL RECORDS
                // ===============================
                using (var cmd = new SqlCommand(@"
            SELECT COUNT(*)
            FROM mst_quiz q
        ", conn))
                {
                    totalRecords = Convert.ToInt32(
                        cmd.ExecuteScalar()
                    );
                }

                // ===============================
                // WHERE CLAUSE
                // ===============================
                string whereClause = @"
            WHERE q.title LIKE @Search
        ";

                if (!string.IsNullOrWhiteSpace(seriesFilter))
                {
                    whereClause += @"
                AND q.series_id = @SeriesId
            ";
                }

                // ===============================
                // FILTERED RECORDS
                // ===============================
                using (var cmd = new SqlCommand($@"
            SELECT COUNT(*)
            FROM mst_quiz q
            {whereClause}
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@Search",
                        searchPattern
                    );

                    if (!string.IsNullOrWhiteSpace(seriesFilter))
                    {
                        cmd.Parameters.AddWithValue(
                            "@SeriesId",
                            seriesFilter
                        );
                    }

                    filteredRecords = Convert.ToInt32(
                        cmd.ExecuteScalar()
                    );
                }

                // ===============================
                // LOAD DATA
                // ===============================
                string sql = $@"
            SELECT
                q.quiz_id,
                q.series_id,

                -- txn_series menggunakan kolom name
                s.name AS series_title,

                q.title,
                q.minimum_point,
                q.notes1,
                q.notes2,

                (
                    SELECT COUNT(*)
                    FROM mst_quiz_question qq
                    WHERE qq.quiz_id = q.quiz_id
                ) AS question_count

            FROM mst_quiz q

            LEFT JOIN txn_series s
                ON q.series_id = s.series_id

            {whereClause}

            ORDER BY q.title ASC

            OFFSET @Skip ROWS
            FETCH NEXT @Take ROWS ONLY
        ";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@Search",
                        searchPattern
                    );

                    if (!string.IsNullOrWhiteSpace(seriesFilter))
                    {
                        cmd.Parameters.AddWithValue(
                            "@SeriesId",
                            seriesFilter
                        );
                    }

                    cmd.Parameters.AddWithValue(
                        "@Skip",
                        req.Skip < 0 ? 0 : req.Skip
                    );

                    cmd.Parameters.AddWithValue(
                        "@Take",
                        req.Take > 0 ? req.Take : 10
                    );

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        list.Add(new
                        {
                            quiz_id =
                                reader["quiz_id"]?.ToString(),

                            series_id =
                                reader["series_id"] == DBNull.Value
                                    ? null
                                    : reader["series_id"]?.ToString(),

                            series_title =
                                reader["series_title"] == DBNull.Value
                                    ? ""
                                    : reader["series_title"]?.ToString(),

                            title =
                                reader["title"] == DBNull.Value
                                    ? ""
                                    : reader["title"]?.ToString(),

                            minimum_point =
                                reader["minimum_point"] == DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        reader["minimum_point"]
                                    ),

                            notes1 =
                                reader["notes1"] == DBNull.Value
                                    ? ""
                                    : reader["notes1"]?.ToString(),

                            notes2 =
                                reader["notes2"] == DBNull.Value
                                    ? ""
                                    : reader["notes2"]?.ToString(),

                            question_count =
                                reader["question_count"] == DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        reader["question_count"]
                                    )
                        });
                    }
                }

                // ===============================
                // RESPONSE
                // ===============================
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
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                QuizDetail quiz = null;

                // ===============================
                // GET QUIZ
                // ===============================
                using (var cmd = new SqlCommand(@"
            SELECT
                quiz_id,
                series_id,
                title,
                minimum_point,
                notes1,
                notes2
            FROM mst_quiz
            WHERE quiz_id = @QuizId
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@QuizId",
                        id
                    );

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        quiz = new QuizDetail
                        {
                            QuizId =
                                reader["quiz_id"]?.ToString(),

                            SeriesId =
                                reader["series_id"]?.ToString(),

                            Title =
                                reader["title"]?.ToString(),

                            MinimumPoint =
                                reader["minimum_point"] ==
                                DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        reader["minimum_point"]
                                    ),

                            Notes1 =
                                reader["notes1"] ==
                                DBNull.Value
                                    ? ""
                                    : reader["notes1"]?.ToString(),

                            Notes2 =
                                reader["notes2"] ==
                                DBNull.Value
                                    ? ""
                                    : reader["notes2"]?.ToString()
                        };
                    }
                }

                // ===============================
                // QUIZ NOT FOUND
                // ===============================
                if (quiz == null)
                {
                    return Json(
                        new ResponseDto
                        {
                            Code = 404,
                            Message = "Quiz not found."
                        }
                    );
                }

                // ===============================
                // GET QUESTIONS
                // ===============================
                using (var cmd = new SqlCommand(@"
            SELECT
                quiz_question_id,
                question,
                question_type,
                point,
                reason_correct
            FROM mst_quiz_question
            WHERE quiz_id = @QuizId
            ORDER BY quiz_question_id
        ", conn))
                {
                    cmd.Parameters.AddWithValue(
                        "@QuizId",
                        id
                    );

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        quiz.Questions.Add(
                            new QuizQuestionDto
                            {
                                QuizQuestionId =
                                    reader["quiz_question_id"]?.ToString(),

                                Question =
                                    reader["question"]?.ToString(),

                                QuestionType =
                                    reader["question_type"]?.ToString(),

                                Point =
                                    reader["point"] ==
                                    DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(
                                            reader["point"]
                                        ),

                                ReasonCorrect =
                                    reader["reason_correct"] ==
                                    DBNull.Value
                                        ? ""
                                        : reader["reason_correct"]?.ToString()
                            }
                        );
                    }
                }

                // ===============================
                // GET OPTIONS FOR EACH QUESTION
                // ===============================
                foreach (var q in quiz.Questions)
                {
                    using var cmd = new SqlCommand(@"
                SELECT
                    quiz_option_id,
                    option_text,
                    is_correct
                FROM mst_quiz_option
                WHERE quiz_question_id = @QuizQuestionId
                ORDER BY quiz_option_id
            ", conn);

                    cmd.Parameters.AddWithValue(
                        "@QuizQuestionId",
                        q.QuizQuestionId
                    );

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        q.Options.Add(
                            new QuizOptionDto
                            {
                                QuizOptionId =
                                    reader["quiz_option_id"]?.ToString(),

                                OptionText =
                                    reader["option_text"]?.ToString(),

                                IsCorrect =
                                    reader["is_correct"] ==
                                    DBNull.Value
                                        ? false
                                        : Convert.ToBoolean(
                                            reader["is_correct"]
                                        )
                            }
                        );
                    }
                }

                // ===============================
                // RESPONSE
                // ===============================
                return Json(
                    new ResponseDto
                    {
                        Code = 200,
                        Message = "Success",
                        Data = quiz
                    }
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new ResponseDto
                    {
                        Code = 500,
                        Message = ex.Message
                    }
                );
            }
        }

        // ===============================
        // SAVE (create / update) - JSON body
        // Replace-all strategy utk question/option
        // ===============================
        [HttpPost]
        [Route("admin/quiz/save")]
        public IActionResult Save([FromBody] QuizSaveRequest req)
        {
            try
            {
                // ===============================
                // VALIDATION
                // ===============================
                if (
                    string.IsNullOrWhiteSpace(req.QuizId) ||
                    string.IsNullOrWhiteSpace(req.SeriesId) ||
                    string.IsNullOrWhiteSpace(req.Title)
                )
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Quiz ID, Series, and Title are required."
                    });
                }

                // ===============================
                // VALIDATE QUESTIONS
                // ===============================
                if (req.Questions == null || req.Questions.Count == 0)
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "At least 1 question is required."
                    });
                }

                foreach (var q in req.Questions)
                {
                    // Validate question text
                    if (string.IsNullOrWhiteSpace(q.Question))
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message = "Question text cannot be empty."
                        });
                    }

                    // Validate minimum options
                    if (q.Options == null || q.Options.Count < 2)
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message =
                                $"Question '{q.Question}' needs at least 2 options."
                        });
                    }

                    // Validate correct answer
                    if (!q.Options.Any(o => o.IsCorrect))
                    {
                        return Json(new ResponseDto
                        {
                            Code = 400,
                            Message =
                                $"Question '{q.Question}' needs at least 1 correct answer."
                        });
                    }
                }

                // ===============================
                // DATABASE CONNECTION
                // ===============================
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                using var trans = conn.BeginTransaction();

                try
                {
                    // ===============================
                    // VALIDATE SERIES
                    // ===============================
                    using (var checkSeries = new SqlCommand(@"
                SELECT COUNT(*)
                FROM txn_series
                WHERE series_id = @SeriesId
                  AND deleted_date IS NULL
            ", conn, trans))
                    {
                        checkSeries.Parameters.AddWithValue(
                            "@SeriesId",
                            req.SeriesId
                        );

                        int exists = Convert.ToInt32(
                            checkSeries.ExecuteScalar()
                        );

                        if (exists == 0)
                        {
                            trans.Rollback();

                            return Json(new ResponseDto
                            {
                                Code = 400,
                                Message =
                                    "Selected series is invalid or inactive."
                            });
                        }
                    }

                    // ===============================
                    // UPDATE
                    // ===============================
                    if (req.IsEdit)
                    {
                        // ===============================
                        // UPDATE QUIZ
                        // ===============================
                        using (var cmd = new SqlCommand(@"
                    UPDATE mst_quiz
                    SET
                        series_id = @SeriesId,
                        title = @Title,
                        minimum_point = @MinimumPoint,
                        notes1 = @Notes1,
                        notes2 = @Notes2
                    WHERE quiz_id = @QuizId
                ", conn, trans))
                        {
                            cmd.Parameters.AddWithValue(
                                "@SeriesId",
                                req.SeriesId
                            );

                            cmd.Parameters.AddWithValue(
                                "@Title",
                                req.Title
                            );

                            cmd.Parameters.AddWithValue(
                                "@MinimumPoint",
                                req.MinimumPoint
                            );

                            cmd.Parameters.AddWithValue(
                                "@Notes1",
                                string.IsNullOrWhiteSpace(req.Notes1)
                                    ? (object)DBNull.Value
                                    : req.Notes1
                            );

                            cmd.Parameters.AddWithValue(
                                "@Notes2",
                                string.IsNullOrWhiteSpace(req.Notes2)
                                    ? (object)DBNull.Value
                                    : req.Notes2
                            );

                            cmd.Parameters.AddWithValue(
                                "@QuizId",
                                req.QuizId
                            );

                            int rows = cmd.ExecuteNonQuery();

                            if (rows == 0)
                            {
                                trans.Rollback();

                                return Json(new ResponseDto
                                {
                                    Code = 404,
                                    Message = "Quiz not found."
                                });
                            }
                        }

                        // ===============================
                        // DELETE OPTIONS FIRST
                        // ===============================
                        using (var cmd = new SqlCommand(@"
                    DELETE o
                    FROM mst_quiz_option o
                    INNER JOIN mst_quiz_question q
                        ON o.quiz_question_id =
                           q.quiz_question_id
                    WHERE q.quiz_id = @QuizId
                ", conn, trans))
                        {
                            cmd.Parameters.AddWithValue(
                                "@QuizId",
                                req.QuizId
                            );

                            cmd.ExecuteNonQuery();
                        }

                        // ===============================
                        // DELETE QUESTIONS
                        // ===============================
                        using (var cmd = new SqlCommand(@"
                    DELETE FROM mst_quiz_question
                    WHERE quiz_id = @QuizId
                ", conn, trans))
                        {
                            cmd.Parameters.AddWithValue(
                                "@QuizId",
                                req.QuizId
                            );

                            cmd.ExecuteNonQuery();
                        }
                    }
                    // ===============================
                    // CREATE
                    // ===============================
                    else
                    {
                        // ===============================
                        // CHECK QUIZ ID
                        // ===============================
                        using (var checkCmd = new SqlCommand(@"
                    SELECT COUNT(*)
                    FROM mst_quiz
                    WHERE quiz_id = @QuizId
                ", conn, trans))
                        {
                            checkCmd.Parameters.AddWithValue(
                                "@QuizId",
                                req.QuizId
                            );

                            int exists = Convert.ToInt32(
                                checkCmd.ExecuteScalar()
                            );

                            if (exists > 0)
                            {
                                trans.Rollback();

                                return Json(new ResponseDto
                                {
                                    Code = 400,
                                    Message = "Quiz ID already exists."
                                });
                            }
                        }

                        // ===============================
                        // INSERT QUIZ
                        // ===============================
                        using (var cmd = new SqlCommand(@"
                    INSERT INTO mst_quiz
                    (
                        quiz_id,
                        series_id,
                        title,
                        minimum_point,
                        notes1,
                        notes2
                    )
                    VALUES
                    (
                        @QuizId,
                        @SeriesId,
                        @Title,
                        @MinimumPoint,
                        @Notes1,
                        @Notes2
                    )
                ", conn, trans))
                        {
                            cmd.Parameters.AddWithValue(
                                "@QuizId",
                                req.QuizId
                            );

                            cmd.Parameters.AddWithValue(
                                "@SeriesId",
                                req.SeriesId
                            );

                            cmd.Parameters.AddWithValue(
                                "@Title",
                                req.Title
                            );

                            cmd.Parameters.AddWithValue(
                                "@MinimumPoint",
                                req.MinimumPoint
                            );

                            cmd.Parameters.AddWithValue(
                                "@Notes1",
                                string.IsNullOrWhiteSpace(req.Notes1)
                                    ? (object)DBNull.Value
                                    : req.Notes1
                            );

                            cmd.Parameters.AddWithValue(
                                "@Notes2",
                                string.IsNullOrWhiteSpace(req.Notes2)
                                    ? (object)DBNull.Value
                                    : req.Notes2
                            );

                            cmd.ExecuteNonQuery();
                        }
                    }

                    // ===============================
                    // INSERT QUESTIONS + OPTIONS
                    // ===============================
                    foreach (var q in req.Questions)
                    {
                        string questionId =
                            Guid.NewGuid().ToString();

                        // ===============================
                        // INSERT QUESTION
                        // ===============================
                        using (var cmd = new SqlCommand(@"
                    INSERT INTO mst_quiz_question
                    (
                        quiz_question_id,
                        quiz_id,
                        question,
                        question_type,
                        point,
                        reason_correct
                    )
                    VALUES
                    (
                        @QuestionId,
                        @QuizId,
                        @Question,
                        @QuestionType,
                        @Point,
                        @ReasonCorrect
                    )
                ", conn, trans))
                        {
                            cmd.Parameters.AddWithValue(
                                "@QuestionId",
                                questionId
                            );

                            cmd.Parameters.AddWithValue(
                                "@QuizId",
                                req.QuizId
                            );

                            cmd.Parameters.AddWithValue(
                                "@Question",
                                q.Question
                            );

                            cmd.Parameters.AddWithValue(
                                "@QuestionType",
                                string.IsNullOrEmpty(q.QuestionType)
                                    ? "single_choice"
                                    : q.QuestionType
                            );

                            cmd.Parameters.AddWithValue(
                                "@Point",
                                q.Point
                            );

                            cmd.Parameters.AddWithValue(
                                "@ReasonCorrect",
                                string.IsNullOrWhiteSpace(
                                    q.ReasonCorrect
                                )
                                    ? (object)DBNull.Value
                                    : q.ReasonCorrect
                            );

                            cmd.ExecuteNonQuery();
                        }

                        // ===============================
                        // INSERT OPTIONS
                        // ===============================
                        foreach (var o in q.Options)
                        {
                            using var cmd = new SqlCommand(@"
                        INSERT INTO mst_quiz_option
                        (
                            quiz_option_id,
                            quiz_question_id,
                            option_text,
                            is_correct
                        )
                        VALUES
                        (
                            @OptionId,
                            @QuestionId,
                            @OptionText,
                            @IsCorrect
                        )
                    ", conn, trans);

                            cmd.Parameters.AddWithValue(
                                "@OptionId",
                                Guid.NewGuid().ToString()
                            );

                            cmd.Parameters.AddWithValue(
                                "@QuestionId",
                                questionId
                            );

                            cmd.Parameters.AddWithValue(
                                "@OptionText",
                                o.OptionText
                            );

                            cmd.Parameters.AddWithValue(
                                "@IsCorrect",
                                o.IsCorrect
                            );

                            cmd.ExecuteNonQuery();
                        }
                    }

                    // ===============================
                    // COMMIT
                    // ===============================
                    trans.Commit();

                    return Json(new ResponseDto
                    {
                        Code = 200,
                        Message = req.IsEdit
                            ? "Quiz updated successfully."
                            : "Quiz created successfully."
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
                return Json(new ResponseDto
                {
                    Code = 500,
                    Message = ex.Message
                });
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
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Quiz ID is required."
                    });
                }

                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                using var cmd = new SqlCommand(@"
            DELETE FROM mst_quiz
            WHERE quiz_id = @QuizId
        ", conn);

                cmd.Parameters.AddWithValue(
                    "@QuizId",
                    quizId
                );

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                {
                    return Json(new ResponseDto
                    {
                        Code = 404,
                        Message = "Quiz not found."
                    });
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Quiz deleted permanently."
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
        // DROPDOWN LIST SERIES (active)
        // ===============================
        [HttpGet]
        [Route("admin/quiz/list-series")]
        public IActionResult ListSeries()
        {
            var list = new List<object>();

            try
            {
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();

                using var cmd = new SqlCommand(@"
            SELECT
                series_id,
                name
            FROM txn_series
            WHERE deleted_date IS NULL
            ORDER BY name ASC
        ", conn);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    list.Add(new
                    {
                        series_id = reader["series_id"]?.ToString(),
                        title = reader["name"]?.ToString()
                    });
                }

                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = list
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
    }
}