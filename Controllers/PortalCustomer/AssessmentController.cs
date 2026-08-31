using AudiobookSystem.Filters;
using AudiobookSystem.Models;
using Microsoft.AspNetCore.Mvc;
using suara_belajar.Filters;
using suara_belajar.Models.Assessment;
using System.Data;
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


        // =========================================================
        // ASSESSMENT PAGE
        //
        // URL:
        // /Assessment?series_id=SERIES004
        // =========================================================
        [HttpGet]
        [Route("Assessment")]
        public IActionResult Index()
        {
            return View(
                "~/Views/PortalCustomer/Audiobook/Assessment.cshtml"
            );
        }


        // =========================================================
        // ASSESSMENT PREVIEW PAGE
        //
        // URL:
        // /AssessmentPreview?series_id=SERIES004
        // =========================================================
        [HttpGet]
        [Route("AssessmentPreview")]
        public IActionResult Preview()
        {
            return View(
                "~/Views/PortalCustomer/Audiobook/AssessmentPreview.cshtml"
            );
        }


        // =========================================================
        // GET ASSESSMENT PREVIEW DATA
        //
        // Returns basic quiz info (title / minimum_point / total
        // question) plus, if the current REDEEM_CODE user already
        // submitted this quiz before, their latest submission
        // (score + pass/fail + the notes1/notes2 snapshot that was
        // stored on that submission) so the frontend can show
        // "already submitted" instead of the Start button.
        //
        // "Latest" is determined by submit_date DESC.
        // =========================================================
        [HttpGet]
        [Route("customer/assessment/preview")]
        public IActionResult GetPreview(
            string seriesId
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(seriesId))
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Series ID is required."
                    });
                }

                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();


                // =====================================================
                // GET QUIZ BASIC INFO
                // =====================================================
                string quizId = null;
                string quizTitle = null;
                int minimumPoint = 0;
                string notes1 = null;
                string notes2 = null;
                int totalQuestion = 0;


                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1
                        quiz_id,
                        title,
                        minimum_point,
                        notes1,
                        notes2
                    FROM mst_quiz
                    WHERE series_id = @SeriesId
                    ORDER BY quiz_id
                ", conn))
                {
                    cmd.Parameters.Add(
                        "@SeriesId",
                        SqlDbType.VarChar
                    ).Value = seriesId;

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        quizId = reader["quiz_id"]?.ToString();
                        quizTitle = reader["title"]?.ToString();

                        minimumPoint =
                            reader["minimum_point"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(reader["minimum_point"]);

                        notes1 =
                            reader["notes1"] == DBNull.Value
                                ? null
                                : reader["notes1"]?.ToString();

                        notes2 =
                            reader["notes2"] == DBNull.Value
                                ? null
                                : reader["notes2"]?.ToString();
                    }
                }

                if (string.IsNullOrWhiteSpace(quizId))
                {
                    return Json(new ResponseDto
                    {
                        Code = 404,
                        Message = "Belum ada quiz untuk series ini."
                    });
                }


                // =====================================================
                // TOTAL QUESTION COUNT
                // =====================================================
                using (var cmd = new SqlCommand(@"
                    SELECT COUNT(*)
                    FROM mst_quiz_question
                    WHERE quiz_id = @QuizId
                ", conn))
                {
                    cmd.Parameters.Add(
                        "@QuizId",
                        SqlDbType.VarChar
                    ).Value = quizId;

                    var result = cmd.ExecuteScalar();

                    totalQuestion =
                        result == null || result == DBNull.Value
                            ? 0
                            : Convert.ToInt32(result);
                }


                // =====================================================
                // GET CODE ID (current logged in redeem code)
                // =====================================================
                string codeId = GetCodeId(conn, null);


                // =====================================================
                // GET LATEST SUBMISSION FOR THIS CODE + QUIZ
                //
                // Ordered by submit_date (actual submission
                // timestamp). notes1/notes2 always come live from
                // mst_quiz (fetched above), not from any snapshot,
                // so the preview always reflects the current notes
                // content.
                // =====================================================
                object latestAssessment = null;

                if (!string.IsNullOrWhiteSpace(codeId))
                {
                    using var cmd = new SqlCommand(@"
                        SELECT TOP 1
                            assessment_id,
                            total_point,
                            minimum_point,
                            is_pass,
                            submit_date
                        FROM txn_assessment
                        WHERE code_id = @CodeId
                          AND quiz_id = @QuizId
                        ORDER BY submit_date DESC
                    ", conn);

                    cmd.Parameters.AddWithValue("@CodeId", codeId);
                    cmd.Parameters.AddWithValue("@QuizId", quizId);

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        int latestTotalPoint =
                            reader["total_point"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(reader["total_point"]);

                        int latestMinimumPoint =
                            reader["minimum_point"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(reader["minimum_point"]);

                        bool latestIsPass =
                            reader["is_pass"] != DBNull.Value &&
                            Convert.ToBoolean(reader["is_pass"]);

                        DateTime? latestSubmitDate =
                            reader["submit_date"] == DBNull.Value
                                ? (DateTime?)null
                                : Convert.ToDateTime(reader["submit_date"]);

                        latestAssessment = new
                        {
                            assessment_id = reader["assessment_id"]?.ToString(),
                            total_point = latestTotalPoint,
                            minimum_point = latestMinimumPoint,
                            is_pass = latestIsPass,
                            notes = latestTotalPoint < 70 ? notes1 : notes2,
                            submit_date = latestSubmitDate
                        };
                    }
                }


                // =====================================================
                // RESPONSE
                // =====================================================
                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = new
                    {
                        series_id = seriesId,
                        quiz_id = quizId,
                        quiz_title = quizTitle,
                        minimum_point = minimumPoint,
                        total_question = totalQuestion,
                        notes1 = notes1,
                        notes2 = notes2,
                        has_submission = latestAssessment != null,
                        latest_assessment = latestAssessment
                    }
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


        // =========================================================
        // ASSESSMENT FEEDBACK PAGE (read-only detail of a past
        // submission, opened via the "View Feedback" button on
        // AssessmentPreview)
        //
        // URL:
        // /AssessmentFeedback?assessment_id=xxxxxxxx-...
        // =========================================================
        [HttpGet]
        [Route("AssessmentFeedback")]
        public IActionResult Feedback()
        {
            return View(
                "~/Views/PortalCustomer/Audiobook/AssessmentFeedback.cshtml"
            );
        }


        // =========================================================
        // GET ASSESSMENT RESULT (past submission detail)
        //
        // Used by the "View Feedback" button on AssessmentPreview
        // to re-open a previously submitted assessment's full
        // breakdown (per-question correct answers + reason_correct
        // + the notes snapshot), without re-submitting anything.
        // =========================================================
        [HttpGet]
        [Route("customer/assessment/result")]
        public IActionResult GetResult(
            string assessmentId
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(assessmentId))
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Assessment ID is required."
                    });
                }

                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();


                // =====================================================
                // GET ASSESSMENT HEADER
                // =====================================================
                string codeId = null;
                string quizId = null;
                string seriesId = null;
                int minimumPoint = 0;
                int totalPoint = 0;
                bool isPass = false;
                DateTime? submitDate = null;

                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1
                        code_id,
                        quiz_id,
                        series_id,
                        minimum_point,
                        total_point,
                        is_pass,
                        submit_date
                    FROM txn_assessment
                    WHERE assessment_id = @AssessmentId
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@AssessmentId", assessmentId);

                    using var reader = cmd.ExecuteReader();

                    if (!reader.Read())
                    {
                        return Json(new ResponseDto
                        {
                            Code = 404,
                            Message = "Assessment tidak ditemukan."
                        });
                    }

                    codeId = reader["code_id"] == DBNull.Value ? null : reader["code_id"]?.ToString();
                    quizId = reader["quiz_id"] == DBNull.Value ? null : reader["quiz_id"]?.ToString();
                    seriesId = reader["series_id"] == DBNull.Value ? null : reader["series_id"]?.ToString();

                    minimumPoint =
                        reader["minimum_point"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["minimum_point"]);

                    totalPoint =
                        reader["total_point"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["total_point"]);

                    isPass =
                        reader["is_pass"] != DBNull.Value &&
                        Convert.ToBoolean(reader["is_pass"]);

                    submitDate =
                        reader["submit_date"] == DBNull.Value
                            ? (DateTime?)null
                            : Convert.ToDateTime(reader["submit_date"]);
                }


                // =====================================================
                // OWNERSHIP CHECK
                //
                // Only the redeem-code owner of this assessment may
                // view it.
                // =====================================================
                string currentCodeId = GetCodeId(conn, null);

                if (
                    string.IsNullOrWhiteSpace(currentCodeId) ||
                    !string.Equals(currentCodeId, codeId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return Json(new ResponseDto
                    {
                        Code = 403,
                        Message = "Kamu tidak memiliki akses ke assessment ini."
                    });
                }


                // =====================================================
                // GET QUIZ TITLE + NOTES
                //
                // notes1 / notes2 always come live from mst_quiz
                // (source of truth), not from any snapshot, so
                // feedback always reflects the current notes content.
                // =====================================================
                string quizTitle = null;
                string notes1 = null;
                string notes2 = null;

                using (var cmd = new SqlCommand(@"
                    SELECT
                        title,
                        notes1,
                        notes2
                    FROM mst_quiz
                    WHERE quiz_id = @QuizId
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@QuizId", (object)quizId ?? DBNull.Value);

                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        quizTitle = reader["title"] == DBNull.Value ? null : reader["title"]?.ToString();
                        notes1 = reader["notes1"] == DBNull.Value ? null : reader["notes1"]?.ToString();
                        notes2 = reader["notes2"] == DBNull.Value ? null : reader["notes2"]?.ToString();
                    }
                }


                // =====================================================
                // GET QUESTIONS + OPTIONS
                // =====================================================
                var resultQuestions = new List<object>();

                using (var cmd = new SqlCommand(@"
                    SELECT
                        q.assessment_question_id,
                        q.question,
                        q.point,
                        q.reason_correct,

                        o.assessment_option_id,
                        o.option_text,
                        o.is_correct,
                        o.is_selected

                    FROM txn_assessment_question q

                    LEFT JOIN txn_assessment_option o
                        ON o.assessment_question_id = q.assessment_question_id

                    WHERE q.assessment_id = @AssessmentId

                    ORDER BY q.assessment_question_id
                ", conn))
                {
                    cmd.Parameters.AddWithValue("@AssessmentId", assessmentId);

                    var questionDictionary = new Dictionary<string, dynamic>();

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        string questionId = reader["assessment_question_id"]?.ToString();

                        if (!questionDictionary.ContainsKey(questionId))
                        {
                            questionDictionary[questionId] = new
                            {
                                assessment_question_id = questionId,

                                question =
                                    reader["question"] == DBNull.Value
                                        ? null
                                        : reader["question"]?.ToString(),

                                point =
                                    reader["point"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["point"]),

                                reason_correct =
                                    reader["reason_correct"] == DBNull.Value
                                        ? null
                                        : reader["reason_correct"]?.ToString(),

                                options = new List<object>()
                            };
                        }

                        if (reader["assessment_option_id"] != DBNull.Value)
                        {
                            var question = questionDictionary[questionId];

                            question.options.Add(new
                            {
                                assessment_option_id = reader["assessment_option_id"]?.ToString(),

                                option_text =
                                    reader["option_text"] == DBNull.Value
                                        ? null
                                        : reader["option_text"]?.ToString(),

                                is_correct =
                                    reader["is_correct"] != DBNull.Value &&
                                    Convert.ToBoolean(reader["is_correct"]),

                                is_selected =
                                    reader["is_selected"] != DBNull.Value &&
                                    Convert.ToBoolean(reader["is_selected"])
                            });
                        }
                    }

                    resultQuestions = questionDictionary.Values.Cast<object>().ToList();
                }


                // =====================================================
                // RESPONSE
                // =====================================================
                return Json(new ResponseDto
                {
                    Code = 200,
                    Message = "Success",
                    Data = new
                    {
                        assessment_id = assessmentId,
                        code_id = codeId,
                        series_id = seriesId,
                        quiz_id = quizId,
                        quiz_title = quizTitle,
                        total_point = totalPoint,
                        minimum_point = minimumPoint,
                        is_pass = isPass,
                        notes1 = notes1,
                        notes2 = notes2,
                        submit_date = submitDate,
                        questions = resultQuestions
                    }
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


        // =========================================================
        // GET CODE ID
        //
        // Cookie:
        // REDEEM_CODE
        //
        // serial_number
        //      ↓
        // txn_code.code_id
        // =========================================================
        private string GetCodeId(
            SqlConnection conn,
            SqlTransaction trans
        )
        {
            string serial =
                Request.Cookies["REDEEM_CODE"];

            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            using var cmd = new SqlCommand(@"
                SELECT code_id
                FROM txn_code
                WHERE serial_number = @Serial
            ", conn, trans);

            cmd.Parameters.AddWithValue(
                "@Serial",
                serial
            );

            var result =
                cmd.ExecuteScalar();

            if (
                result == null ||
                result == DBNull.Value
            )
            {
                return null;
            }

            return result.ToString();
        }


        // =========================================================
        // GET ASSESSMENT
        //
        // Returns quiz + questions + options.
        //
        // NOTE:
        // The IDs returned here (quiz_question_id, quiz_option_id)
        // are the OLD MASTER IDs from mst_quiz_question /
        // mst_quiz_option. The frontend must send these SAME IDs
        // back on Submit. Submit() is responsible for translating
        // them into the NEW txn_assessment_question /
        // txn_assessment_option IDs it creates.
        // =========================================================
        [HttpGet]
        [Route("customer/assessment/get")]
        public IActionResult GetAssessment(
            string seriesId
        )
        {
            try
            {
                // =====================================================
                // VALIDATE SERIES ID
                // =====================================================
                if (string.IsNullOrWhiteSpace(seriesId))
                {
                    return Json(new ResponseDto
                    {
                        Code = 400,
                        Message = "Series ID is required."
                    });
                }


                // =====================================================
                // OPEN DATABASE
                // =====================================================
                using var conn = new SqlConnection(
                    _config.GetConnectionString("DefaultConnection")
                );

                conn.Open();


                // =====================================================
                // GET QUIZ
                // ONLY READ FROM MST_QUIZ
                // =====================================================
                string quizId = null;
                string quizTitle = null;
                string notes1 = null;
                string notes2 = null;
                int minimumPoint = 0;


                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1
                        quiz_id,
                        title,
                        minimum_point,
                        notes1,
                        notes2
                    FROM mst_quiz
                    WHERE series_id = @SeriesId
                    ORDER BY quiz_id
                ", conn))
                {
                    cmd.Parameters.Add(
                        "@SeriesId",
                        SqlDbType.VarChar
                    ).Value = seriesId;


                    using var reader = cmd.ExecuteReader();


                    if (reader.Read())
                    {
                        quizId =
                            reader["quiz_id"]?.ToString();

                        quizTitle =
                            reader["title"]?.ToString();

                        minimumPoint =
                            reader["minimum_point"] == DBNull.Value
                                ? 0
                                : Convert.ToInt32(
                                    reader["minimum_point"]
                                );

                        notes1 =
                            reader["notes1"] == DBNull.Value
                                ? null
                                : reader["notes1"]?.ToString();

                        notes2 =
                            reader["notes2"] == DBNull.Value
                                ? null
                                : reader["notes2"]?.ToString();
                    }
                }


                // =====================================================
                // QUIZ NOT FOUND
                // =====================================================
                if (string.IsNullOrWhiteSpace(quizId))
                {
                    return Json(new ResponseDto
                    {
                        Code = 404,
                        Message = "Belum ada quiz untuk series ini."
                    });
                }


                // =====================================================
                // GET QUESTIONS
                // ONLY READ FROM MST_QUIZ_QUESTION
                // =====================================================
                var questions =
                    new List<QuestionItem>();


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
                    cmd.Parameters.Add(
                        "@QuizId",
                        SqlDbType.VarChar
                    ).Value = quizId;


                    using var reader = cmd.ExecuteReader();


                    while (reader.Read())
                    {
                        questions.Add(
                            new QuestionItem
                            {
                                QuizQuestionId =
                                    reader[
                                        "quiz_question_id"
                                    ]?.ToString(),

                                Question =
                                    reader[
                                        "question"
                                    ]?.ToString(),

                                QuestionType =
                                    reader[
                                        "question_type"
                                    ]?.ToString(),

                                Point =
                                    reader["point"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(
                                            reader["point"]
                                        ),

                                ReasonCorrect =
                                    reader[
                                        "reason_correct"
                                    ] == DBNull.Value
                                        ? null
                                        : reader[
                                            "reason_correct"
                                          ]?.ToString()
                            }
                        );
                    }
                }


                // =====================================================
                // GET OPTIONS
                // ONLY READ FROM MST_QUIZ_OPTION
                // =====================================================
                foreach (var question in questions)
                {
                    using var cmd = new SqlCommand(@"
                        SELECT
                            quiz_option_id,
                            option_text
                        FROM mst_quiz_option
                        WHERE quiz_question_id = @QuizQuestionId
                        ORDER BY quiz_option_id
                    ", conn);


                    cmd.Parameters.Add(
                        "@QuizQuestionId",
                        SqlDbType.VarChar
                    ).Value =
                        question.QuizQuestionId;


                    using var reader =
                        cmd.ExecuteReader();


                    while (reader.Read())
                    {
                        question.Options.Add(
                            new OptionItem
                            {
                                QuizOptionId =
                                    reader[
                                        "quiz_option_id"
                                    ]?.ToString(),

                                OptionText =
                                    reader[
                                        "option_text"
                                    ]?.ToString(),

                                // GET API NEVER RETURNS
                                // is_correct
                                IsSelected = false
                            }
                        );
                    }
                }


                // =====================================================
                // TOTAL POINT
                // =====================================================
                int totalPoint =
                    questions.Sum(
                        q => q.Point
                    );


                // =====================================================
                // RESPONSE
                // =====================================================
                return Json(
                    new ResponseDto
                    {
                        Code = 200,

                        Message = "Success",

                        Data = new
                        {
                            series_id =
                                seriesId,

                            quiz_id =
                                quizId,

                            quiz_title =
                                quizTitle,

                            notes1 =
                                notes1,

                            notes2 =
                                notes2,

                            total_question =
                                questions.Count,

                            total_point =
                                totalPoint,

                            minimum_point =
                                minimumPoint,

                            questions =
                                questions.Select(
                                    q => new
                                    {
                                        quiz_question_id =
                                            q.QuizQuestionId,

                                        question =
                                            q.Question,

                                        question_type =
                                            q.QuestionType,

                                        point =
                                            q.Point,

                                        reason_correct =
                                            q.ReasonCorrect,

                                        options =
                                            q.Options.Select(
                                                o => new
                                                {
                                                    quiz_option_id =
                                                        o.QuizOptionId,

                                                    option_text =
                                                        o.OptionText
                                                }
                                            )
                                    }
                                )
                        }
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

        // =========================================================
        // SUBMIT ASSESSMENT
        // =========================================================
        [HttpPost]
        [Route("customer/assessment/submit")]
        public IActionResult Submit(
            [FromBody] SubmitAssessmentRequest req
        )
        {
            try
            {
                // =====================================================
                // VALIDATE REQUEST
                // =====================================================
                if (
                    req == null ||
                    string.IsNullOrWhiteSpace(req.QuizId)
                )
                {
                    return Json(
                        new ResponseDto
                        {
                            Code = 400,
                            Message = "Quiz ID is required."
                        }
                    );
                }

                if (
                    req.Answers == null ||
                    req.Answers.Count == 0
                )
                {
                    return Json(
                        new ResponseDto
                        {
                            Code = 400,
                            Message = "Jawaban tidak boleh kosong."
                        }
                    );
                }


                // =====================================================
                // OPEN DATABASE
                // =====================================================
                using var conn = new SqlConnection(
                    _config.GetConnectionString(
                        "DefaultConnection"
                    )
                );

                conn.Open();

                using var trans =
                    conn.BeginTransaction();


                try
                {
                    // =================================================
                    // VARIABLES
                    // =================================================
                    string codeId = null;
                    string seriesId = null;

                    int minimumPoint = 0;

                    string notes1 = null;
                    string notes2 = null;


                    // =================================================
                    // GET CODE ID
                    // =================================================
                    codeId =
                        GetCodeId(
                            conn,
                            trans
                        );


                    if (
                        string.IsNullOrWhiteSpace(codeId)
                    )
                    {
                        trans.Rollback();

                        return Json(
                            new ResponseDto
                            {
                                Code = 401,
                                Message =
                                    "Redeem code tidak ditemukan."
                            }
                        );
                    }


                    // =================================================
                    // GET QUIZ INFORMATION
                    // =================================================
                    using (
                        var cmd =
                            new SqlCommand(@"
                        SELECT TOP 1
                            series_id,
                            minimum_point,
                            notes1,
                            notes2
                        FROM mst_quiz
                        WHERE quiz_id = @QuizId
                    ", conn, trans)
                    )
                    {
                        cmd.Parameters.AddWithValue(
                            "@QuizId",
                            req.QuizId
                        );

                        using (
                            var reader =
                                cmd.ExecuteReader()
                        )
                        {
                            if (!reader.Read())
                            {
                                return Json(
                                    new ResponseDto
                                    {
                                        Code = 404,
                                        Message =
                                            "Quiz tidak ditemukan."
                                    }
                                );
                            }

                            seriesId =
                                reader["series_id"] ==
                                DBNull.Value
                                    ? null
                                    : reader[
                                        "series_id"
                                      ]?.ToString();

                            minimumPoint =
                                reader["minimum_point"] ==
                                DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        reader[
                                            "minimum_point"
                                        ]
                                    );

                            notes1 =
                                reader["notes1"] ==
                                DBNull.Value
                                    ? null
                                    : reader[
                                        "notes1"
                                      ]?.ToString();

                            notes2 =
                                reader["notes2"] ==
                                DBNull.Value
                                    ? null
                                    : reader[
                                        "notes2"
                                      ]?.ToString();
                        }
                    }


                    // =================================================
                    // CREATE ASSESSMENT ID
                    // =================================================
                    string assessmentId =
                        Guid.NewGuid().ToString();


                    // =================================================
                    // INSERT ASSESSMENT
                    //
                    // notes1/notes2 are NOT stored here - they are
                    // always fetched live from mst_quiz wherever
                    // they're needed for display (see GetResult /
                    // GetPreview), so mst_quiz stays the single
                    // source of truth.
                    // =================================================
                    using (
                        var cmd =
                            new SqlCommand(@"
                        INSERT INTO txn_assessment
                        (
                            assessment_id,
                            code_id,
                            series_id,
                            quiz_id,
                            minimum_point,
                            total_point,
                            is_pass,
                            submit_date
                        )
                        VALUES
                        (
                            @AssessmentId,
                            @CodeId,
                            @SeriesId,
                            @QuizId,
                            @MinimumPoint,
                            0,
                            0,
                            GETDATE()
                        )
                    ", conn, trans)
                    )
                    {
                        cmd.Parameters.AddWithValue(
                            "@AssessmentId",
                            assessmentId
                        );

                        cmd.Parameters.AddWithValue(
                            "@CodeId",
                            codeId
                        );

                        cmd.Parameters.AddWithValue(
                            "@SeriesId",
                            (object)seriesId ??
                            DBNull.Value
                        );

                        cmd.Parameters.AddWithValue(
                            "@QuizId",
                            req.QuizId
                        );

                        cmd.Parameters.AddWithValue(
                            "@MinimumPoint",
                            minimumPoint
                        );

                        cmd.ExecuteNonQuery();
                    }


                    // =================================================
                    // QUESTION MAP
                    //
                    // OLD QUIZ QUESTION ID (mst_quiz_question)
                    //      ↓
                    // NEW ASSESSMENT QUESTION ID (txn_assessment_question)
                    // =================================================
                    var questionMap =
                        new Dictionary<
                            string,
                            string
                        >();


                    // =================================================
                    // OPTION MAP
                    //
                    // OLD QUIZ OPTION ID (mst_quiz_option)
                    //      ↓
                    // NEW ASSESSMENT OPTION ID (txn_assessment_option)
                    // =================================================
                    var optionMap =
                        new Dictionary<
                            string,
                            string
                        >();


                    // =================================================
                    // GET QUIZ QUESTIONS
                    // =================================================
                    var quizQuestions =
                        new List<QuizQuestionSnapshot>();


                    using (
                        var cmd =
                            new SqlCommand(@"
                        SELECT
                            quiz_question_id,
                            question,
                            question_type,
                            point,
                            reason_correct
                        FROM mst_quiz_question
                        WHERE quiz_id = @QuizId
                        ORDER BY quiz_question_id
                    ", conn, trans)
                    )
                    {
                        cmd.Parameters.AddWithValue(
                            "@QuizId",
                            req.QuizId
                        );

                        using (
                            var reader =
                                cmd.ExecuteReader()
                        )
                        {
                            while (reader.Read())
                            {
                                quizQuestions.Add(
                                    new QuizQuestionSnapshot
                                    {
                                        OldQuestionId =
                                            reader[
                                                "quiz_question_id"
                                            ]?.ToString(),

                                        Question =
                                            reader[
                                                "question"
                                            ] == DBNull.Value
                                                ? null
                                                : reader[
                                                    "question"
                                                  ],

                                        QuestionType =
                                            reader[
                                                "question_type"
                                            ] == DBNull.Value
                                                ? null
                                                : reader[
                                                    "question_type"
                                                  ],

                                        Point =
                                            reader[
                                                "point"
                                            ] == DBNull.Value
                                                ? 0
                                                : Convert.ToInt32(
                                                    reader[
                                                        "point"
                                                    ]
                                                ),

                                        ReasonCorrect =
                                            reader[
                                                "reason_correct"
                                            ] == DBNull.Value
                                                ? null
                                                : reader[
                                                    "reason_correct"
                                                  ]
                                    }
                                );
                            }
                        }
                    }


                    // =================================================
                    // INSERT QUESTION SNAPSHOT
                    // =================================================
                    foreach (
                        var quizQuestion
                        in quizQuestions
                    )
                    {
                        string oldQuestionId =
                            quizQuestion.OldQuestionId;

                        string newQuestionId =
                            Guid.NewGuid().ToString();


                        questionMap[
                            oldQuestionId
                        ] = newQuestionId;


                        using (
                            var cmd =
                                new SqlCommand(@"
                            INSERT INTO
                                txn_assessment_question
                            (
                                assessment_question_id,
                                assessment_id,
                                question,
                                question_type,
                                point,
                                reason_correct
                            )
                            VALUES
                            (
                                @QuestionId,
                                @AssessmentId,
                                @Question,
                                @QuestionType,
                                @Point,
                                @ReasonCorrect
                            )
                        ", conn, trans)
                        )
                        {
                            cmd.Parameters.AddWithValue(
                                "@QuestionId",
                                newQuestionId
                            );

                            cmd.Parameters.AddWithValue(
                                "@AssessmentId",
                                assessmentId
                            );

                            cmd.Parameters.AddWithValue(
                                "@Question",
                                (object)
                                    quizQuestion.Question ??
                                    DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@QuestionType",
                                (object)
                                    quizQuestion.QuestionType ??
                                    DBNull.Value
                            );

                            cmd.Parameters.AddWithValue(
                                "@Point",
                                quizQuestion.Point
                            );

                            cmd.Parameters.AddWithValue(
                                "@ReasonCorrect",
                                (object)
                                    quizQuestion.ReasonCorrect ??
                                    DBNull.Value
                            );

                            cmd.ExecuteNonQuery();
                        }
                    }


                    // =================================================
                    // COPY OPTIONS
                    // =================================================
                    foreach (
                        var question
                        in questionMap
                    )
                    {
                        string oldQuestionId =
                            question.Key;

                        string newQuestionId =
                            question.Value;


                        var quizOptions =
                            new List<QuizOptionSnapshot>();


                        // =================================================
                        // GET OPTIONS
                        // =================================================
                        using (
                            var cmd =
                                new SqlCommand(@"
                            SELECT
                                quiz_option_id,
                                option_text,
                                is_correct
                            FROM mst_quiz_option
                            WHERE quiz_question_id =
                                @QuizQuestionId
                        ", conn, trans)
                        )
                        {
                            cmd.Parameters.AddWithValue(
                                "@QuizQuestionId",
                                oldQuestionId
                            );

                            using (
                                var reader =
                                    cmd.ExecuteReader()
                            )
                            {
                                while (reader.Read())
                                {
                                    quizOptions.Add(
                                        new QuizOptionSnapshot
                                        {
                                            OldOptionId =
                                                reader[
                                                    "quiz_option_id"
                                                ]?.ToString(),

                                            OptionText =
                                                reader[
                                                    "option_text"
                                                ] == DBNull.Value
                                                    ? null
                                                    : reader[
                                                        "option_text"
                                                      ],

                                            IsCorrect =
                                                reader[
                                                    "is_correct"
                                                ] != DBNull.Value &&
                                                Convert.ToBoolean(
                                                    reader[
                                                        "is_correct"
                                                    ]
                                                )
                                        }
                                    );
                                }
                            }
                        }


                        // =================================================
                        // INSERT OPTIONS
                        // =================================================
                        foreach (
                            var option
                            in quizOptions
                        )
                        {
                            string newOptionId =
                                Guid.NewGuid().ToString();


                            optionMap[
                                option.OldOptionId
                            ] = newOptionId;


                            using (
                                var cmd =
                                    new SqlCommand(@"
                                INSERT INTO
                                    txn_assessment_option
                                (
                                    assessment_option_id,
                                    assessment_question_id,
                                    option_text,
                                    is_correct,
                                    is_selected
                                )
                                VALUES
                                (
                                    @OptionId,
                                    @QuestionId,
                                    @OptionText,
                                    @IsCorrect,
                                    0
                                )
                            ", conn, trans)
                            )
                            {
                                cmd.Parameters.AddWithValue(
                                    "@OptionId",
                                    newOptionId
                                );

                                cmd.Parameters.AddWithValue(
                                    "@QuestionId",
                                    newQuestionId
                                );

                                cmd.Parameters.AddWithValue(
                                    "@OptionText",
                                    (object)
                                        option.OptionText ??
                                        DBNull.Value
                                );

                                cmd.Parameters.AddWithValue(
                                    "@IsCorrect",
                                    option.IsCorrect
                                );

                                cmd.ExecuteNonQuery();
                            }
                        }
                    }


                    // =================================================
                    // SAVE USER ANSWERS
                    //
                    // *** FIX ***
                    //
                    // The frontend receives question/option IDs from
                    // GetAssessment(), which reads from the MASTER
                    // tables (mst_quiz_question / mst_quiz_option).
                    // Those are OLD IDs.
                    //
                    // The rows we can actually mark is_selected = 1 on
                    // live in txn_assessment_question /
                    // txn_assessment_option, which were just created
                    // above with brand NEW GUIDs.
                    //
                    // We MUST translate every incoming
                    // AssessmentQuestionId / SelectedOptionIds entry
                    // through questionMap / optionMap before updating.
                    // Skipping this step (as the previous version did)
                    // means the UPDATE below never matches any row,
                    // is_selected stays 0 for everything, and the
                    // score always comes out as 0.
                    // =================================================
                    foreach (
                        var answer
                        in req.Answers
                    )
                    {
                        if (
                            string.IsNullOrWhiteSpace(
                                answer.AssessmentQuestionId
                            )
                        )
                        {
                            continue;
                        }


                        if (
                            answer.SelectedOptionIds == null
                        )
                        {
                            continue;
                        }


                        // Translate OLD question id -> NEW
                        // assessment_question_id
                        if (
                            !questionMap.TryGetValue(
                                answer.AssessmentQuestionId,
                                out var newQuestionId
                            )
                        )
                        {
                            // Unknown / foreign question id for
                            // this quiz, skip it
                            continue;
                        }


                        foreach (
                            var optionId
                            in answer.SelectedOptionIds
                        )
                        {
                            if (
                                string.IsNullOrWhiteSpace(
                                    optionId
                                )
                            )
                            {
                                continue;
                            }


                            // Translate OLD option id -> NEW
                            // assessment_option_id
                            if (
                                !optionMap.TryGetValue(
                                    optionId,
                                    out var newOptionId
                                )
                            )
                            {
                                // Unknown / foreign option id,
                                // skip it
                                continue;
                            }


                            using (
                                var cmd =
                                    new SqlCommand(@"
                                UPDATE
                                    txn_assessment_option
                                SET
                                    is_selected = 1
                                WHERE
                                    assessment_option_id =
                                        @OptionId

                                AND
                                    assessment_question_id =
                                        @QuestionId
                            ", conn, trans)
                            )
                            {
                                cmd.Parameters.AddWithValue(
                                    "@OptionId",
                                    newOptionId
                                );

                                cmd.Parameters.AddWithValue(
                                    "@QuestionId",
                                    newQuestionId
                                );

                                cmd.ExecuteNonQuery();
                            }
                        }
                    }


                    // =================================================
                    // CALCULATE SCORE
                    // =================================================
                    int totalPoint = 0;


                    using (
                        var cmd =
                            new SqlCommand(@"
                        SELECT
                            q.assessment_question_id,
                            q.question,
                            q.point,
                            q.reason_correct,

                            SUM(
                                CASE
                                    WHEN o.is_correct = 1
                                     AND o.is_selected = 0
                                    THEN 1
                                    ELSE 0
                                END
                            ) AS missed_correct,

                            SUM(
                                CASE
                                    WHEN o.is_correct = 0
                                     AND o.is_selected = 1
                                    THEN 1
                                    ELSE 0
                                END
                            ) AS wrong_selected

                        FROM txn_assessment_question q

                        LEFT JOIN txn_assessment_option o
                            ON o.assessment_question_id =
                               q.assessment_question_id

                        WHERE q.assessment_id =
                            @AssessmentId

                        GROUP BY
                            q.assessment_question_id,
                            q.question,
                            q.point,
                            q.reason_correct

                        ORDER BY
                            q.assessment_question_id
                    ", conn, trans)
                    )
                    {
                        cmd.Parameters.AddWithValue(
                            "@AssessmentId",
                            assessmentId
                        );

                        using (
                            var reader =
                                cmd.ExecuteReader()
                        )
                        {
                            while (reader.Read())
                            {
                                int point =
                                    reader["point"] ==
                                    DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(
                                            reader["point"]
                                        );

                                int missedCorrect =
                                    reader[
                                        "missed_correct"
                                    ] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(
                                            reader[
                                                "missed_correct"
                                            ]
                                        );

                                int wrongSelected =
                                    reader[
                                        "wrong_selected"
                                    ] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(
                                            reader[
                                                "wrong_selected"
                                            ]
                                        );


                                if (
                                    missedCorrect == 0 &&
                                    wrongSelected == 0
                                )
                                {
                                    totalPoint += point;
                                }
                            }
                        }
                    }


                    // =================================================
                    // CHECK PASS
                    // =================================================
                    bool isPass =
                        totalPoint >= minimumPoint;


                    // =================================================
                    // UPDATE RESULT
                    // =================================================
                    using (
                        var cmd =
                            new SqlCommand(@"
                        UPDATE txn_assessment
                        SET
                            total_point = @TotalPoint,
                            is_pass = @IsPass
                        WHERE assessment_id =
                            @AssessmentId
                    ", conn, trans)
                    )
                    {
                        cmd.Parameters.AddWithValue(
                            "@TotalPoint",
                            totalPoint
                        );

                        cmd.Parameters.AddWithValue(
                            "@IsPass",
                            isPass
                        );

                        cmd.Parameters.AddWithValue(
                            "@AssessmentId",
                            assessmentId
                        );

                        cmd.ExecuteNonQuery();
                    }


                    // =================================================
                    // COMMIT FIRST
                    // =================================================
                    trans.Commit();


                    // =================================================
                    // GET RESULT DETAILS
                    //
                    // Use a NEW connection because transaction
                    // has already been committed.
                    //
                    // Returns, per question:
                    //   - question text
                    //   - point
                    //   - reason_correct
                    //   - all options with is_correct / is_selected
                    //     so the frontend can highlight what the user
                    //     picked vs. what was actually correct.
                    // =================================================
                    var resultQuestions =
                        new List<object>();


                    using (
                        var resultConn =
                            new SqlConnection(
                                _config.GetConnectionString(
                                    "DefaultConnection"
                                )
                            )
                    )
                    {
                        resultConn.Open();


                        using (
                            var cmd =
                                new SqlCommand(@"
                            SELECT
                                q.assessment_question_id,
                                q.question,
                                q.point,
                                q.reason_correct,

                                o.assessment_option_id,
                                o.option_text,
                                o.is_correct,
                                o.is_selected

                            FROM txn_assessment_question q

                            LEFT JOIN txn_assessment_option o
                                ON o.assessment_question_id =
                                   q.assessment_question_id

                            WHERE q.assessment_id =
                                @AssessmentId

                            ORDER BY
                                q.assessment_question_id
                        ", resultConn)
                        )
                        {
                            cmd.Parameters.AddWithValue(
                                "@AssessmentId",
                                assessmentId
                            );


                            var questionDictionary =
                                new Dictionary<
                                    string,
                                    dynamic
                                >();


                            using (
                                var reader =
                                    cmd.ExecuteReader()
                            )
                            {
                                while (reader.Read())
                                {
                                    string questionId =
                                        reader[
                                            "assessment_question_id"
                                        ]?.ToString();


                                    if (
                                        !questionDictionary.ContainsKey(
                                            questionId
                                        )
                                    )
                                    {
                                        questionDictionary[
                                            questionId
                                        ] =
                                            new
                                            {
                                                assessment_question_id =
                                                    questionId,

                                                question =
                                                    reader[
                                                        "question"
                                                    ] == DBNull.Value
                                                        ? null
                                                        : reader[
                                                            "question"
                                                          ]?.ToString(),

                                                point =
                                                    reader[
                                                        "point"
                                                    ] == DBNull.Value
                                                        ? 0
                                                        : Convert.ToInt32(
                                                            reader[
                                                                "point"
                                                            ]
                                                        ),

                                                reason_correct =
                                                    reader[
                                                        "reason_correct"
                                                    ] == DBNull.Value
                                                        ? null
                                                        : reader[
                                                            "reason_correct"
                                                          ]?.ToString(),

                                                options =
                                                    new List<object>()
                                            };
                                    }


                                    if (
                                        reader[
                                            "assessment_option_id"
                                        ] != DBNull.Value
                                    )
                                    {
                                        var question =
                                            questionDictionary[
                                                questionId
                                            ];


                                        question.options.Add(
                                            new
                                            {
                                                assessment_option_id =
                                                    reader[
                                                        "assessment_option_id"
                                                    ]?.ToString(),

                                                option_text =
                                                    reader[
                                                        "option_text"
                                                    ] == DBNull.Value
                                                        ? null
                                                        : reader[
                                                            "option_text"
                                                          ]?.ToString(),

                                                is_correct =
                                                    reader[
                                                        "is_correct"
                                                    ] != DBNull.Value &&
                                                    Convert.ToBoolean(
                                                        reader[
                                                            "is_correct"
                                                        ]
                                                    ),

                                                is_selected =
                                                    reader[
                                                        "is_selected"
                                                    ] != DBNull.Value &&
                                                    Convert.ToBoolean(
                                                        reader[
                                                            "is_selected"
                                                        ]
                                                    )
                                            }
                                        );
                                    }
                                }
                            }


                            resultQuestions =
                                questionDictionary
                                    .Values
                                    .Cast<object>()
                                    .ToList();
                        }
                    }


                    // =================================================
                    // RESPONSE
                    // =================================================
                    return Json(
                        new ResponseDto
                        {
                            Code = 200,

                            Message =
                                isPass
                                    ? "Selamat, kamu lulus!"
                                    : "Maaf, kamu belum lulus.",

                            Data = new
                            {
                                assessment_id =
                                    assessmentId,

                                code_id =
                                    codeId,

                                series_id =
                                    seriesId,

                                quiz_id =
                                    req.QuizId,

                                total_point =
                                    totalPoint,

                                minimum_point =
                                    minimumPoint,

                                is_pass =
                                    isPass,

                                notes1 =
                                    notes1,

                                notes2 =
                                    notes2,

                                questions =
                                    resultQuestions
                            }
                        }
                    );
                }
                catch
                {
                    try
                    {
                        trans.Rollback();
                    }
                    catch
                    {
                        // Ignore rollback error
                    }

                    throw;
                }
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

        // =========================================================
        // QUESTION ITEM
        // =========================================================
        private class QuestionItem
        {
            public string QuizQuestionId { get; set; }

            public string Question { get; set; }

            public string QuestionType { get; set; }

            public int Point { get; set; }

            public string ReasonCorrect { get; set; }

            public List<OptionItem> Options { get; set; } = new List<OptionItem>();
        }

        // =========================================================
        // OPTION ITEM
        // =========================================================
        private class OptionItem
        {
            public string QuizOptionId { get; set; }

            public string OptionText { get; set; }

            public bool IsSelected { get; set; }
        }

        public class QuizQuestionSnapshot
        {
            public string OldQuestionId { get; set; }

            public object Question { get; set; }

            public object QuestionType { get; set; }

            public int Point { get; set; }

            public object ReasonCorrect { get; set; }
        }


        public class QuizOptionSnapshot
        {
            public string OldOptionId { get; set; }

            public object OptionText { get; set; }

            public bool IsCorrect { get; set; }
        }
    }
}