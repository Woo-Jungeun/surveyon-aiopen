using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using SurveyonApi.Repositories;

namespace SurveyonApi.Controllers
{
    public class BaseController : ControllerBase
    {
        protected readonly IMainRepository _mainRepository;

        public BaseController(IMainRepository mainRepository)
        {
            _mainRepository = mainRepository;
        }

        protected async Task<IActionResult> ReturnError(string code, string? message = null, string lang = "ko")
        {
            var errorInfo = await _mainRepository.GetErrorCodeAsync(code, lang, HttpContext.RequestAborted);

            if (errorInfo != null)
            {
                return StatusCode(errorInfo.HttpStatus, new 
                { 
                    success = errorInfo.success, 
                    Message = message ?? errorInfo.Message 
                });
            }

            // DB에 코드가 없을 경우 기본 오류 반환
            return StatusCode(500, new 
            { 
                success = code, 
                Message = message ?? "알 수 없는 오류가 발생했습니다." 
            });
        }

        protected async Task<IActionResult> ReturnResponse(string code, object? data = null, string lang = "ko")
        {
            var info = await _mainRepository.GetErrorCodeAsync(code, lang, HttpContext.RequestAborted);

            object? resultJson = data;

            if (data is JToken token)
            {
                resultJson = token.ToObject<object>(); // ⭐ 이게 핵심
            }
            if (info != null)
            {
                return StatusCode(info.HttpStatus, new 
                { 
                    success = info.success, 
                    Message = info.Message,
                    resultjson = resultJson
                });
            }

            // DB에 코드가 없을 경우 기본 성공(200)으로 처리하되 메시지는 없음
            return Ok(new 
            { 
                success = code, 
                Message = "메시지를 찾을 수 없습니다.",
                resultjson = resultJson
            });
        }
    }
}
