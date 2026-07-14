using Microsoft.AspNetCore.Mvc;

namespace suara_belajar.Models.Assessment
{
    public class SubmitAssessmentRequest
    {
        public string AssessmentId { get; set; }
        public List<SubmitAnswerDto> Answers { get; set; } = new();
    }

    public class SubmitAnswerDto
    {
        public string AssessmentQuestionId { get; set; }
        public List<string> SelectedOptionIds { get; set; } = new();
    }
}
