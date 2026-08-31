namespace suara_belajar.Models.Assessment
{
    public class SubmitAssessmentRequest
    {
        public string SeriesId { get; set; }

        public string QuizId { get; set; }

        public List<SubmitAssessmentAnswer> Answers { get; set; }
    }


    public class SubmitAssessmentAnswer
    {
        public string AssessmentQuestionId { get; set; }

        public List<string> SelectedOptionIds { get; set; }
    }
}