namespace SurveyonApi.Infrastructure
{
    public class AuthOptions
    {
        public string AuthCoreBaseUrl { get; set; } = "https://stest.hrc.kr/APIs/o/";
        public string ValidatePath { get; set; } = "login/token/validate";
        public string HeaderName { get; set; } = "X-Auth-Token";
        public int CacheSeconds { get; set; } = 30;
        public bool DisableAuth { get; set; } = false;
        public string InternalKey { get; set; } = "cGZeWyxlli5N4FRSwMAsbbRoNzJEhFkocore";
        public string InternalKeyHeaderName { get; set; } = "X-Internal-Key";
    }

    public class ValidateRequest
    {
        public string? user { get; set; } = "";
    }

    public class AuthValidationResultJson
    {
        public bool Valid { get; set; }
        public string? UserId { get; set; } = "";
    }

    public class AuthValidationResponse
    {
        public string Success { get; set; } = "";
        public string Message { get; set; } = "";
        public AuthValidationResultJson? Resultjson { get; set; }
    }
}
