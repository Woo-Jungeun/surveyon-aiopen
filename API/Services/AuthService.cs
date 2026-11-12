using SurveyonApi.Repositories;
using SurveyonApi.Filters;

namespace SurveyonApi.Services
{
    public interface IAuthService
    {
        Task<(bool IsValid, string Code, string Message, string? UserId)> ValidateTokenAsync(string? internalKey, string? token, string? auth, string? userIdFromBody = null);
    }

    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly IMainRepository _mainRepository;

        public AuthService(IConfiguration configuration, IMainRepository mainRepository)
        {
            _configuration = configuration;
            _mainRepository = mainRepository;
        }

        public async Task<(bool IsValid, string Code, string Message, string? UserId)> ValidateTokenAsync(string? internalKey, string? token, string? auth, string? userIdFromBody = null)
        {
            // 1. Internal Key 확인 (internalKey가 제공되고 올바른 경우 성공 처리)
            var expectedKey = _configuration["InternalAuth:InternalKey"];
            if (!string.IsNullOrWhiteSpace(internalKey))
            {
                if (!string.IsNullOrWhiteSpace(expectedKey) && internalKey == expectedKey)
                {
                    string? internalUserId = userIdFromBody;
                    string? effToken = token?.Trim();
                    if (string.IsNullOrWhiteSpace(effToken) && !string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        effToken = auth.Substring("Bearer ".Length).Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(effToken) && TokenStore.TryGetValid(effToken, out var internalInfo))
                    {
                        internalUserId = internalInfo.UserId;
                    }
                    return (true, "777", "Success (Internal Key)", internalUserId ?? "SYSTEM");
                }
                else
                {
                    return (false, "704", "Internal key invalid", null);
                }
            }

            // 2. 헤더에서 토큰 추출 (X-Auth-Token 또는 Authorization: Bearer <token> 유연 지원)
            string? effectiveToken = token?.Trim();
            if (string.IsNullOrWhiteSpace(effectiveToken) && !string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                effectiveToken = auth.Substring("Bearer ".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(effectiveToken))
            {
                return (false, "704", "Token missing", null);
            }

            // 3. Authorization 헤더와 X-Auth-Token 헤더가 모두 존재하는 경우 값 일치 여부 확인
            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var authHeader = auth.Substring("Bearer ".Length).Trim();
                if (authHeader != effectiveToken)
                {
                    return (false, "704", "Token mismatch", null);
                }
            }

            // 4. TokenStore (인메모리) 유효성 검사 및 UserId 가져오기
            string? userId = userIdFromBody;
            bool skipDbCheck = false;

            if (TokenStore.TryGetValid(effectiveToken, out var info))
            {
                userId = info.UserId;
                skipDbCheck = true;
            }

            // 5. DB 더블 체크 (TokenStore 미존재 또는 AppPool 재활용 시 실행)
            if (!skipDbCheck)
            {
                try
                {
                    bool isDbValid = false;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        isDbValid = await _mainRepository.SelectUserCheckAsync(userId, effectiveToken);
                    }

                    // userId가 없거나 기존 DB 검사가 실패한 경우: loginkey로 DB 직접 대조 (2차 Fallback)
                    if (!isDbValid)
                    {
                        var dbUser = await _mainRepository.SelectUserByLoginKeyAsync(effectiveToken);
                        if (!string.IsNullOrEmpty(dbUser))
                        {
                            userId = dbUser;
                            isDbValid = true;

                            // 성공한 토큰은 TokenStore 인메모리 캐시 재등록
                            TokenStore.Add(effectiveToken, userId, TimeSpan.FromMinutes(3650));
                        }
                    }

                    if (!isDbValid)
                    {
                        return (false, "704", "Invalid Token (DB Check Failed)", null);
                    }
                }
                catch
                {
                    return (false, "704", "Token Validation Error", null);
                }
            }

            if (string.IsNullOrEmpty(userId))
            {
                return (false, "704", "User ID not found", null);
            }

            // 모든 검증 통과 -> 777 리턴
            return (true, "777", "Success", userId);
        }
    }
}
