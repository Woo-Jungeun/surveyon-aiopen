using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using SurveyonApi.Repositories;
using SurveyonApi.Models;
using System.Data;

namespace SurveyonApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TokenCostController : BaseController
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public TokenCostController(
            IMainRepository mainRepository,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
            : base(mainRepository)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }


        [HttpGet("list")]
        [HttpPost("list")]
        public async Task<IActionResult> GetTokenCostList([FromHeader(Name = "X-Internal-Key")] string? internalKey)
        {
            try
            {
                
                string user = ""; // 필요한 경우 사용자 정보 가져오기
                DataTable dt = await _mainRepository.select_project_list_async(user, 115, "", "", "", "", HttpContext.RequestAborted);

                // projectnum을 키로 사용하여 데이터 집계
                var aggregatedData = new Dictionary<string, Dictionary<string, object>>();

                foreach (DataRow row in dt.Rows)
                {
                    string pn = row["projectnum"]?.ToString()?.TrimEnd() ?? "";
                    if (string.IsNullOrEmpty(pn)) continue;

                    string lb_info = row["lb_info"]?.ToString()?.TrimEnd() ?? "";
                    string res_info = row["res_info"]?.ToString()?.TrimEnd() ?? "";
                    
                    float tokens_cost_fin_lb = 0;
                    float tokens_cost_fin_res = 0;

                    if (!string.IsNullOrEmpty(lb_info))
                    {
                        try
                        {
                            JObject json = JObject.Parse(lb_info);
                            string tokens_cost = json["tokens_cost"]?.ToString()?.TrimEnd() ?? "0";
                            if (double.TryParse(tokens_cost, out double tokens_costd))
                            {
                                tokens_cost_fin_lb = (float)Math.Round(tokens_costd, 5);
                            }
                        }
                        catch { /* JSON 파싱 실패 시 무시 */ }
                    }

                    if (!string.IsNullOrEmpty(res_info))
                    {
                        try
                        {
                            JObject json = JObject.Parse(res_info);
                            string tokens_cost = json["tokens_cost"]?.ToString()?.TrimEnd() ?? "0";
                            if (double.TryParse(tokens_cost, out double tokens_costd))
                            {
                                tokens_cost_fin_res = (float)Math.Round(tokens_costd, 5);
                            }
                        }
                        catch { /* JSON 파싱 실패 시 무시 */ }
                    }

                    if (!aggregatedData.ContainsKey(pn))
                    {
                        // 새로운 프로젝트 추가
                        aggregatedData[pn] = new Dictionary<string, object>
                        {
                            { "id", row["id"]?.ToString() ?? "" },
                            { "projectpof", row["projectpof"]?.ToString() ?? "" },
                            { "projectnum", pn },
                            { "projectname", row["projectname"]?.ToString() ?? "" },
                            { "register_userid", row["register_userid"]?.ToString() ?? "" },
                            { "register_date", row["register_date"]?.ToString() ?? "" },
                            { "servername", row["servername"]?.ToString() ?? "" },
                            { "project_use_name", row["use_name"]?.ToString() ?? "" },
                            { "project_update_date", row["update_date"]?.ToString() ?? "" },
                            { "project_status", row["status"]?.ToString() ?? "" },
                            { "usergroup", row["use_group"]?.ToString() ?? "" },
                            { "tokens_cost_sum", 0f }
                        };
                    }

                    // 비용 합산
                    float currentSum = (float)aggregatedData[pn]["tokens_cost_sum"];
                    aggregatedData[pn]["tokens_cost_sum"] = currentSum + tokens_cost_fin_lb + tokens_cost_fin_res;
                }

                // 결과 리스트로 변환
                var resultList = aggregatedData.Values.Select(item => 
                {
                    // float 값을 문자열로 변환하여 포맷 맞춤
                    item["tokens_cost_sum"] = item["tokens_cost_sum"].ToString();
                    return item;
                }).ToList();

                return await ReturnResponse("777", new
                {
                    success = "777",
                    message = "정상 접속",
                    contents = "success",
                    resultjson = resultList
                });
            }
            catch (Exception)
            {
                return await ReturnError("761");
            }
        }
    }
}
