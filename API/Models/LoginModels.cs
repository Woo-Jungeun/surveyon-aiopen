namespace SurveyonApi.Models
{
    public class LoginRequest
    {
        public string user { get; set; } = string.Empty;
        public string pass { get; set; } = string.Empty;
    }

    public class LoginRequest_customer
    {
        public string user { get; set; } = string.Empty;
    }

    public class ValidateRequest
    {
        public string user { get; set; } = string.Empty;
        public string? token { get; set; }
        public string? loginkey { get; set; }
    }
}
