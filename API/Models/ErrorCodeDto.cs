namespace SurveyonApi.Models
{
    public class ErrorCodeDto
    {
        public string success { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string resultjson { get; set; } = string.Empty;
        public int HttpStatus { get; set; }
    }
}
