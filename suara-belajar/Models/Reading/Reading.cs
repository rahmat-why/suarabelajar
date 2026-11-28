using Microsoft.AspNetCore.Mvc;

namespace suara_belajar.Models
{
    public class SummaryDetail
    {
        public string SummaryId { get; set; }
        public string AudiobookId { get; set; }
        public string Description { get; set; }
    }

    public class SummarySaveRequest
    {
        public bool IsEdit { get; set; }
        public string SummaryId { get; set; }
        public string AudiobookId { get; set; }
        public string Description { get; set; }
    }

    public class FinishReadingRequest
    {
        public string ReadingId { get; set; }
    }
}
