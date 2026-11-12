using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using SurveyonApi.Infrastructure;

namespace SurveyonApi.Filters
{
    public class AuthCheckFilter : IAsyncAuthorizationFilter
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly AuthOptions _opt;
        private readonly ILogger<AuthCheckFilter> _logger;

        public AuthCheckFilter(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            IOptions<AuthOptions> opt,
            ILogger<AuthCheckFilter> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _opt = opt.Value;
            _logger = logger;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (HttpMethods.IsOptions(context.HttpContext.Request.Method))
                return;

            var path = context.HttpContext.Request.Path.Value ?? "";
            if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) || 
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
                return;

            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                return;

            if (_opt.DisableAuth)
                return;

            var request = context.HttpContext.Request;

            // 1. Internal Key 확인 (내부 서비스 간 호출 통과)
            string? internalKey = request.Headers[_opt.InternalKeyHeaderName].FirstOrDefault()
                ?? request.Headers["X-Internal-Key"].FirstOrDefault()
                ?? request.Query["X-Internal-Key"].FirstOrDefault()
                ?? request.Query["internalKey"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(internalKey) && 
                !string.IsNullOrWhiteSpace(_opt.InternalKey) && 
                internalKey == _opt.InternalKey)
            {
                context.HttpContext.Items["UserId"] = "SYSTEM";
                return;
            }

            // 2. 토큰 추출 (X-Auth-Token, Authorization Bearer, Query string 순서)
            string token = "";
            if (request.Headers.TryGetValue(_opt.HeaderName, out var extractedToken) && !string.IsNullOrWhiteSpace(extractedToken.ToString()))
            {
                token = extractedToken.ToString().Trim();
            }
            else if (request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader.ToString()))
            {
                var raw = authHeader.ToString().Trim();
                token = raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? raw.Substring("Bearer ".Length).Trim() : raw;
            }
            else if (request.Query.TryGetValue(_opt.HeaderName, out var qToken) && !string.IsNullOrWhiteSpace(qToken.ToString()))
            {
                token = qToken.ToString().Trim();
            }
            else if (request.Query.TryGetValue("token", out var qToken2) && !string.IsNullOrWhiteSpace(qToken2.ToString()))
            {
                token = qToken2.ToString().Trim();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("AuthCheck failed: missing token. Path={Path}", path);
                context.Result = new UnauthorizedObjectResult(new { Message = "인증 토큰이 필요합니다.", ErrorCode = "401" });
                return;
            }

            // 3. 인메모리 캐시 확인 (최근 CacheSeconds(기본 30초) 내 검증된 토큰은 즉시 통과)
            var cacheKey = "AUTH_OK_" + token;
            if (_cache.TryGetValue(cacheKey, out string? cachedUserId) && !string.IsNullOrWhiteSpace(cachedUserId))
            {
                context.HttpContext.Items["UserId"] = cachedUserId;
                return;
            }

            // 4. Request Body에서 user 추출 (선택적)
            var userIdFromBody = await ResolveUserIdFromBodyAsync(context);

            // 5. AuthCore 중앙 서버에 token/validate 원격 조회
            try
            {
                var client = _httpClientFactory.CreateClient("AuthCore");
                using var req = new HttpRequestMessage(HttpMethod.Post, _opt.ValidatePath);
                var jsonContent = JsonSerializer.Serialize(new ValidateRequest { user = userIdFromBody });
                req.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                req.Headers.Add(_opt.HeaderName, token.Trim());
                if (!string.IsNullOrWhiteSpace(_opt.InternalKey))
                {
                    req.Headers.Add(_opt.InternalKeyHeaderName, _opt.InternalKey);
                }

                using var res = await client.SendAsync(req, context.HttpContext.RequestAborted);

                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AuthCheck failed: rejected by AuthCore. Path={Path} Status={StatusCode}", path, res.StatusCode);
                    context.Result = new UnauthorizedObjectResult(new { Message = "유효하지 않거나 만료된 인증 토큰입니다.", ErrorCode = "401" });
                    return;
                }

                var resString = await res.Content.ReadAsStringAsync(context.HttpContext.RequestAborted);
                var resModel = JsonSerializer.Deserialize<AuthValidationResponse>(resString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (resModel == null || resModel.Success != "777" || resModel.Resultjson == null || !resModel.Resultjson.Valid)
                {
                    _logger.LogWarning("AuthCheck failed: invalid token response. Path={Path}", path);
                    context.Result = new UnauthorizedObjectResult(new { Message = "인증 검증 실패: 유효하지 않은 토큰입니다.", ErrorCode = "401" });
                    return;
                }

                var resolvedUserId = string.IsNullOrWhiteSpace(resModel.Resultjson.UserId) ? userIdFromBody : resModel.Resultjson.UserId;
                context.HttpContext.Items["UserId"] = resolvedUserId;
                _cache.Set(cacheKey, resolvedUserId, TimeSpan.FromSeconds(Math.Max(1, _opt.CacheSeconds)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuthCheck exception on path={Path} (AuthCore: {Url}/{Path})", path, _opt.AuthCoreBaseUrl, _opt.ValidatePath);
                context.Result = new ObjectResult(new { Message = "인증 서비스 연동 중 오류가 발생했습니다: " + ex.Message, ErrorCode = "500" }) { StatusCode = 500 };
            }
        }

        private static async Task<string> ResolveUserIdFromBodyAsync(AuthorizationFilterContext context)
        {
            if (!HttpMethods.IsPost(context.HttpContext.Request.Method)) return string.Empty;
            try
            {
                context.HttpContext.Request.EnableBuffering();
                context.HttpContext.Request.Body.Position = 0;
                using var reader = new StreamReader(context.HttpContext.Request.Body, Encoding.UTF8, false, 1024, true);
                var body = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);
                context.HttpContext.Request.Body.Position = 0;
                if (string.IsNullOrWhiteSpace(body)) return string.Empty;
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return string.Empty;
                if (doc.RootElement.TryGetProperty("user", out var p)) return p.GetString() ?? string.Empty;
                return string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
