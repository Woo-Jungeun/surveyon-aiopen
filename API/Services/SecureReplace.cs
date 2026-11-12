using System.Text.RegularExpressions;

namespace SurveyonApi.Services
{
    public class SecureReplace
    {
        public static string SanitizePrompt(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // 1) 이메일
            text = Regex.Replace(
                text,
                @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
                "[REDACTED_EMAIL]"
            );

            // 2) 한국 주민등록번호 (예: 900101-1234567)
            text = Regex.Replace(
                text,
                @"\b\d{6}-\d{7}\b",
                "[REDACTED_SSN]"
            );

            // 3) 전화번호 (예: 010-1234-5678, 01612345678)
            text = Regex.Replace(
                text,
                @"\b01[016789]-?\d{3,4}-?\d{4}\b",
                "[REDACTED_PHONE]"
            );

            return text;
        }
}
}