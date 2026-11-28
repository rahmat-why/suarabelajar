using Microsoft.AspNetCore.Mvc;

namespace suara_belajar.Models.Monitoring
{
    public class AssessmentDetailQuestion
    {
        public string AssessmentQuestionId { get; set; }
        public string Question { get; set; }
        public string QuestionType { get; set; }
        public int Point { get; set; }
        public List<AssessmentDetailOption> Options { get; set; } = new();
    }

    public class AssessmentDetailOption
    {
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; }
        public bool IsSelected { get; set; }
    }

    public class ReadingDetailHeader
    {
        public string ReadingId { get; set; }
        public string SerialNumber { get; set; }
        public string AudiobookTitle { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? FinishDate { get; set; }
        public string Status { get; set; }
        public string Description { get; set; }
    }
}