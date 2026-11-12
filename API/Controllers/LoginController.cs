using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SurveyonApi.Filters;
using SurveyonApi.Services;
using SurveyonApi.Models;
using SurveyonApi.Repositories;
using Newtonsoft.Json.Linq;
using System.Data;
using System;

namespace SurveyonApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Route("o/[controller]")]
    [Route("o/login")]
    [Route("api/[controller]")]
    [Route("api/login")]
    [AllowAnonymous] // 로그인/로그아웃 API는 인증 없이 접근 가능
    public class LoginController : BaseController 
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IAuthService _authService;

        public LoginController(
            IMainRepository mainRepository,
            IHttpClientFactory httpClientFactory, 
            IConfiguration configuration,
            IAuthService authService) : base(mainRepository)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _authService = authService;
        }

        private string GetAesKey() => _configuration["AES256:Key"] ?? "cGZeWyxlli5N4FRSwMAsbbRoNzJEhFko";
        private string GetAesIv()  => _configuration["AES256:IV"]  ?? "cGZeWyxlli5N4FRS";

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // 1. 헤더 토큰 가져오기
            string token = string.Empty;
            if (Request.Headers.TryGetValue("X-Auth-Token", out var tokenHeader))
            {
                token = tokenHeader.ToString();
            }

            // 2. TokenStore에서 제거 및 DB loginkey 초기화
            if (!string.IsNullOrEmpty(token))
            {
                if (TokenStore.TryGetValid(token, out var info))
                {
                    await _mainRepository.UpdateUserLoginKeyAsync(info.UserId, "", HttpContext.RequestAborted);
                }
                TokenStore.Remove(token);
            }

            // 3. 쿠키 및 세션 삭제
            foreach (var cookie in Request.Cookies.Keys)
            {
                Response.Cookies.Delete(cookie);
            }
            HttpContext.Session.Clear();

            return await ReturnResponse("777");
        }

        [HttpPost("check")]
        public async Task<IActionResult> CheckLogin([FromBody] LoginRequest request)
        {
            // [docs/main.md 지침] 필수 파라미터 누락 시 범용 에러 코드 703 반환
            if (request == null || string.IsNullOrEmpty(request.user) || string.IsNullOrEmpty(request.pass))
            {
                return await ReturnError("703");
            }

            string user = request.user.Trim();
            string pass = request.pass;

            string aesKey = GetAesKey();
            string aesIv  = GetAesIv();

            // 1. 테스트 계정 확인 (appsettings.json)
            string plainPass = pass;
            try
            {
                plainPass = AES256.Crypto.decryptAES256(pass, aesKey, aesIv);
            }
            catch { /* 복호화 실패 시 원문 사용 */ }

            var allowedUsers = _configuration.GetSection("TestAuth:AllowedUsers").Get<List<LoginRequest>>();
            var isTestUser = allowedUsers?.Any(u => u.user == user && u.pass == plainPass) ?? false;

            if (isTestUser)
            {
                var testToken = Guid.NewGuid().ToString("N");
                TokenStore.Add(testToken, user, TimeSpan.FromMinutes(3650));
                return Ok(new { Message = "로그인 성공 (테스트 인증)", Token = testToken, loginkey = testToken });
            }

            // 2. 입력 비밀번호 AES256 암호화 (DB 저장 비밀번호 대조용)
            string encryptedPass = pass;
            try
            {
                encryptedPass = AES256.Crypto.encryptAES256(pass, aesKey, aesIv);
            }
            catch
            {
                encryptedPass = pass;
            }

            // 3. PostgreSQL DB 계정 정보 조회
            DataTable dtUser = await _mainRepository.SelectProLoginDetailAsync(user, HttpContext.RequestAborted);

            // DB에 계정이 없는 경우: 사내 HPRO API를 통해 신규 계정 자동 생성 및 동기화
            if (dtUser == null || dtUser.Rows.Count == 0)
            {
                try
                {
                    DataTable hproData = await _mainRepository.select_user_hpro_api(user, "2", ct: HttpContext.RequestAborted);
                    if (hproData != null && hproData.Rows.Count > 0)
                    {
                        foreach (DataRow row in hproData.Rows)
                        {
                            string email = row["email"]?.ToString()?.TrimEnd() ?? "";
                            string[] parts = email.Split('@');
                            string id = parts[0];
                            string hproPass = row["사원번호"]?.ToString()?.TrimEnd() ?? "";
                            string position = row["부서"]?.ToString()?.TrimEnd() ?? "";
                            string username = row["이름"]?.ToString()?.TrimEnd() ?? "";

                            if (int.TryParse(hproPass, out int groupcode))
                            {
                                string passwordaes = AES256.Crypto.encryptAES256(hproPass, aesKey, aesIv);
                                await _mainRepository.InsertUserAsync(id, passwordaes, position, username, groupcode, null, HttpContext.RequestAborted);
                            }
                        }

                        // 계정 재조회
                        dtUser = await _mainRepository.SelectProLoginDetailAsync(user, HttpContext.RequestAborted);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoginController] HPRO Sync Exception: {ex.Message}");
                }
            }

            // [docs/main.md 지침] 사용자 미존재 및 비밀번호 오류 구분 없이 범용 에러 코드 702 반환 (User Enumeration 차단)
            if (dtUser == null || dtUser.Rows.Count == 0)
            {
                return await ReturnError("702");
            }

            DataRow userRow = dtUser.Rows[0];
            string dbPassword = userRow["password"]?.ToString() ?? "";

            // 4. 비밀번호 검증 (암호화 비교 / 평문 비교 / 복호화 대조)
            bool isPasswordMatch = (dbPassword == encryptedPass) || (dbPassword == pass);
            if (!isPasswordMatch)
            {
                try
                {
                    string decryptedDbPass = AES256.Crypto.decryptAES256(dbPassword, aesKey, aesIv);
                    if (decryptedDbPass == plainPass || decryptedDbPass == pass)
                    {
                        isPasswordMatch = true;
                    }
                }
                catch { }
            }

            if (!isPasswordMatch)
            {
                return await ReturnError("702");
            }

            // 5. 계정 만료일(expiration) 검증
            string expirationStr = userRow["expiration"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(expirationStr))
            {
                if (DateTime.TryParse(expirationStr, out DateTime expirationDate))
                {
                    if (expirationDate < DateTime.Now)
                    {
                        // [docs/main.md 지침] 계정 만료 시 774 에러 코드 반환
                        return await ReturnError("774");
                    }
                }
            }
            else
            {
                return await ReturnError("774");
            }

            // 6. 보안 토큰(loginkey) 난수 생성 및 발급 (사용자 ID 대신 무작위 GUID 토큰 사용)
            string loginkey = Guid.NewGuid().ToString("N");
            await _mainRepository.UpdateUserLoginKeyAsync(user, loginkey, HttpContext.RequestAborted);

            string usernameVal = userRow["username"]?.ToString()?.Trim() ?? user;
            string grouppositionVal = userRow["groupposition"]?.ToString()?.Trim() ?? "";
            string groupcodeVal = userRow["groupcode"]?.ToString()?.Trim() ?? "";

            TokenStore.Add(loginkey, user, TimeSpan.FromMinutes(3650));

            // [docs/main.md 지침] 777 성공 코드 및 output 호환 구조 반환
            var outputArray = new JArray
            {
                new JObject
                {
                    ["userid"] = user,
                    ["username"] = usernameVal,
                    ["groupposition"] = grouppositionVal,
                    ["expiration"] = expirationStr,
                    ["loginkey"] = loginkey,
                    ["groupcode"] = groupcodeVal
                }
            };

            return Ok(new
            {
                success = "777",
                message = "정상 접속",
                contents = "성공",
                Token = loginkey,
                output = outputArray
            });
        }

        [HttpPost("check/cs")]
        public async Task<IActionResult> CheckLogin_customer([FromBody] LoginRequest_customer request)
        {
            if (request == null || string.IsNullOrEmpty(request.user))
            {
                return await ReturnError("703");
            }

            try
            {
                var customerLoginResult = await _mainRepository.GetCustomerLoginInfoAsync(request.user, HttpContext.RequestAborted);
                
                if (customerLoginResult.isValid)
                {
                    return Ok(new
                    {
                        success = "777",
                        Message = "로그인 성공 (고객 인증)",
                        Token = customerLoginResult.loginkey.Trim(),
                        loginkey = customerLoginResult.loginkey.Trim(),
                        showmenu = customerLoginResult.showmenu.Trim(),
                        projectnum = customerLoginResult.projectnum.Trim(),
                        projectname = customerLoginResult.projectname.Trim(),
                        merge_pn = customerLoginResult.projectnum.Trim(),
                        page_id = customerLoginResult.page_id.Trim(),
                        page_title = customerLoginResult.page_title.Trim(),
                        groupcode = customerLoginResult.groupcode.Trim(),
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoginController] Exception: {ex.Message}");
            }

            return await ReturnError("703");
        }

        [HttpPost("token/validate")]
        [HttpPost("/token/validate")]
        [HttpPost("/o/token/validate")]
        [HttpPost("/o/api/login/token/validate")]
        [HttpPost("/APIs/o/token/validate")]
        [HttpPost("/APIs/o/login/token/validate")]
        [HttpPost("/APIs/o/api/login/token/validate")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateToken(
            [FromBody] ValidateRequest? request,
            [FromHeader(Name = "X-Auth-Token")] string? token,
            [FromHeader(Name = "Authorization")] string? auth,
            [FromHeader(Name = "X-Internal-Key")] string? internalKey)
        {
            if (string.IsNullOrWhiteSpace(internalKey))
            {
                internalKey = _configuration["InternalAuth:InternalKey"];
            }

            string? effectiveToken = token;
            if (string.IsNullOrWhiteSpace(effectiveToken) && request != null)
            {
                effectiveToken = !string.IsNullOrWhiteSpace(request.token) ? request.token : request.loginkey;
            }

            var result = await _authService.ValidateTokenAsync(internalKey, effectiveToken, auth, request?.user ?? "");

            if (result.IsValid && result.Code == "777")
            {
                 return await ReturnResponse("777", new { Valid = true, UserId = result.UserId });
            }
            else
            {
                return await ReturnResponse("704", new { Valid = false});
            }
        }

        [HttpPost("token/validatev2")]
        [HttpPost("/token/validatev2")]
        [HttpPost("/o/token/validatev2")]
        [HttpPost("/o/api/login/token/validatev2")]
        [HttpPost("/APIs/o/token/validatev2")]
        [HttpPost("/APIs/o/login/token/validatev2")]
        [HttpPost("/APIs/o/api/login/token/validatev2")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateTokenV2(
            [FromHeader(Name = "X-Auth-Token")] string? token,
            [FromHeader(Name = "Authorization")] string? auth,
            [FromHeader(Name = "hrc")] string? hrc,
            [FromHeader(Name = "X-Internal-Key")] string? internalKey)
        {
            if (string.IsNullOrWhiteSpace(internalKey))
            {
                internalKey = _configuration["InternalAuth:InternalKey"];
            }

            string aesKey = GetAesKey();
            string aesIv  = GetAesIv();
            string hrcfin = "";
            if (!string.IsNullOrEmpty(hrc))
            {
                try
                {
                    hrcfin = AES256.Crypto.decryptAES256(hrc, aesKey, aesIv);
                }
                catch
                {
                    hrcfin = hrc;
                }
            }

            var result = await _authService.ValidateTokenAsync(internalKey, token, auth, hrcfin);

            if (result.IsValid && result.Code == "777")
            {
                 return await ReturnResponse("777", new { Valid = true, UserId = result.UserId });
            }
            else
            {
                return await ReturnResponse("704", new { Valid = false});
            }
        }
    }
}
