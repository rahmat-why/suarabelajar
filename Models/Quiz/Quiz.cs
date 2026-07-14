using System.Collections.Generic;

namespace suara_belajar.Models
{
    // ===================== OPTION =====================
    public class QuizOptionDto
    {
        public string QuizOptionId { get; set; }
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; }
    }

    // ===================== QUESTION =====================
    public class QuizQuestionDto
    {
        public string QuizQuestionId { get; set; }
        public string Question { get; set; }
        public string QuestionType { get; set; } // "single_choice" | "multiple_choice"
        public int Point { get; set; }
        public List<QuizOptionDto> Options { get; set; } = new List<QuizOptionDto>();
    }

    // ===================== DETAIL (get by id, populate form Edit) =====================
    public class QuizDetail
    {
        public string QuizId { get; set; }
        public string AudiobookId { get; set; }
        public string Title { get; set; }
        public int MinimumPoint { get; set; }
        public List<QuizQuestionDto> Questions { get; set; } = new List<QuizQuestionDto>();
    }

    // ===================== REQUEST: SAVE (create/update, JSON body) =====================
    public class QuizSaveRequest
    {
        public bool IsEdit { get; set; }
        public string QuizId { get; set; }
        public string AudiobookId { get; set; }
        public string Title { get; set; }
        public int MinimumPoint { get; set; }
        public List<QuizQuestionDto> Questions { get; set; } = new List<QuizQuestionDto>();
    }
}