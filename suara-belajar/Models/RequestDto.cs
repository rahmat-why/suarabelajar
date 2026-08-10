namespace AudiobookSystem.Models
{
    public class RequestDto
    {
        public int Draw { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public string Data { get; set; }
        public string Status { get; set; }
        public string Package { get; set; }
        public string Series { get; set; }
        public string Audiobook { get; set; }
        public string Quiz { get; set; }
        public string Summary { get; set; }

    }

}
