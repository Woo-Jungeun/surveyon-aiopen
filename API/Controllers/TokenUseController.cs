using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using SurveyonApi.Repositories;
using SurveyonApi.Models;
using System.Net.Http.Headers;
using System.Globalization;

namespace SurveyonApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TokenUseController : BaseController
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public TokenUseController(
            IMainRepository mainRepository,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
            : base(mainRepository)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        ///DB 충전금액 조회
        /// </summary>
        [HttpPost("chargecostselect")]
        public async Task<IActionResult> SelectChargeCost(
            [FromBody] ChargeCostSelectRequest req,
            [FromHeader(Name = "X-Internal-Key")] string? internalKey)
        {
            try
            {
                decimal chargeCost = await _mainRepository.GetChargeCostAsync(req.apigubun, HttpContext.RequestAborted);
                
                if (chargeCost > 0)
                {
                        return await ReturnResponse("777", new
                        {
                            chargecost = chargeCost,
                        });
                }
                else
                {
                    return await ReturnError("761", "업데이트 실패: 해당 apigubun을 찾을 수 없거나 변경된 내용이 없습니다.");
                }
            }
            catch (Exception)
            {
                return await ReturnError("761");
            }
        }

        

        /// <summary>
        /// OpenAI API 비용 조회 (월별, 년별)
        /// </summary>
        [HttpPost("chargecostup")]
        public async Task<IActionResult> UpdateChargeCost([FromBody] ChargeCostUpdateRequest req)
        {
            try
            {
                bool result = await _mainRepository.UpdateChargeCostAsync(req.chargecostInput, req.apigubun, HttpContext.RequestAborted);
                
                if (result)
                {
                    return await ReturnResponse("777", new { message = "업데이트 성공" });
                }
                else
                {
                    return await ReturnError("761", "업데이트 실패: 해당 apigubun을 찾을 수 없거나 변경된 내용이 없습니다.");
                }
            }
            catch (Exception)
            {
                return await ReturnError("761");
            }
        }

        [HttpPost("list")]
        public async Task<IActionResult> GetTokenUsageList(
            [FromBody] TokenUsageRequest req)
        {
            try
            {
                var apiKey = _configuration["OpenAI:AdminApiKey"] ?? _configuration["OpenAI:ApiKey"] ?? "";
                string apigubun = req.apigubun;

                if (string.IsNullOrEmpty(apiKey))
                    return await ReturnError("771");

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                // ---- 기간 계산 ----
                var (startUtc, endUtcExclusive, _) = ResolveDateRangeUtc(req.startDate, req.endDate);
                long startUnix = new DateTimeOffset(startUtc).ToUnixTimeSeconds();
                long endUnix = new DateTimeOffset(endUtcExclusive).ToUnixTimeSeconds();

                var dailyCostList = new List<UsageItem>();

                // 1) Costs 조회 (USD)
                // 우선순위 1: 공식 Org Costs API
                var url = BuildUsageCostsUrl(startUnix, endUnix, "1d", null);
                var (ok, json, _) = await TryGetJsonAsync(client, url);

                if (ok && json != null)
                {
                    dailyCostList = BuildDailyFromBuckets(json, valueSelector: SumUsdFromCostResults, unit: "USD");
                }
                else
                {
                    // 우선순위 2: 비공식 Dashboard Billing Usage API (sk-proj 키 대응)
                    var (dashOk, dashJson, _) = await TryGetDashboardBillingUsageAsync(client, startUtc, endUtcExclusive);
                    if (dashOk && dashJson != null)
                    {
                        dailyCostList = BuildDailyFromDashboard(dashJson);
                    }
                }

                var (monthlyCostList, yearlyCostList) = BuildMonthYear(dailyCostList);

                // 잔액 계산
                decimal chargeCost = await _mainRepository.GetChargeCostAsync(apigubun, HttpContext.RequestAborted);
                decimal yearlyTotal = yearlyCostList.Sum(x => x.Value);
                decimal monthlyTotal = monthlyCostList.Sum(x => x.Value);
                decimal balanceCost = chargeCost - yearlyTotal;

                return await ReturnResponse("777", new
                {
                     monthly = monthlyTotal,
                     yearly = yearlyTotal,
                     balancecost = balanceCost
                });
            }
            catch (Exception)
            {
                return await ReturnError("761");
            }
        }

        // ----------------------------
        // Models
        // ----------------------------
        public class UsageItem
        {
            public string Date { get; set; } = "";
            public decimal Value { get; set; }
            public string Unit { get; set; } = "";
        }

        // ----------------------------
        // Helpers
        // ----------------------------
        private static (DateTime startUtc, DateTime endUtcExclusive, string note) ResolveDateRangeUtc(string? startDate, string? endDate)
        {
            DateTime now = DateTime.UtcNow;
            DateTime defaultStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime defaultEndExclusive = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);

            var note = "";
            bool okStart = TryParseYmdToUtc(startDate, out var s);
            bool okEnd = TryParseYmdToUtc(endDate, out var e);

            var startUtc = okStart ? s : defaultStart;
            var endUtcExclusive = okEnd ? e.AddDays(1) : defaultEndExclusive;

            if (endUtcExclusive <= startUtc)
            {
                startUtc = defaultStart;
                endUtcExclusive = defaultEndExclusive;
                note = "날짜 범위가 올바르지 않아 기본값으로 설정했습니다.";
            }

            return (startUtc, endUtcExclusive, note);
        }

        private static bool TryParseYmdToUtc(string? ymd, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(ymd)) return false;
            if (!DateTime.TryParseExact(ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return false;
            utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }

        private static string BuildUsageCostsUrl(long startUnix, long endUnix, string bucketWidth, string? groupByCsv)
        {
            var url = $"https://api.openai.com/v1/organization/costs?start_time={startUnix}&end_time={endUnix}&bucket_width={bucketWidth}";
            if (!string.IsNullOrWhiteSpace(groupByCsv))
                url += $"&group_by={Uri.EscapeDataString(groupByCsv)}";
            return url;
        }

        private static async Task<(bool ok, JObject? json, string err)> TryGetJsonAsync(HttpClient client, string url)
        {
            try
            {
                var res = await client.GetAsync(url);
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                    return (false, null, $"{(int)res.StatusCode} {res.ReasonPhrase} | {body}");
                return (true, JObject.Parse(body), "");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private static async Task<(bool ok, JObject? json, string err)> TryGetDashboardBillingUsageAsync(HttpClient client, DateTime start, DateTime end)
        {
            try
            {
                var url = $"https://api.openai.com/v1/dashboard/billing/usage?start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}";
                return await TryGetJsonAsync(client, url);
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        private static List<UsageItem> BuildDailyFromDashboard(JObject root)
        {
            var list = new List<UsageItem>();
            var dailyCosts = root["daily_costs"] as JArray;
            if (dailyCosts == null) return list;
            foreach (var item in dailyCosts)
            {
                double timestamp = item["timestamp"]?.Value<double>() ?? 0;
                if (timestamp <= 0) continue;
                var date = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).ToLocalTime().ToString("yyyy-MM-dd");
                decimal costInCents = 0;
                var lineItems = item["line_items"] as JArray;
                if (lineItems != null)
                {
                    foreach (var line in lineItems) costInCents += line["cost"]?.Value<decimal>() ?? 0;
                }
                list.Add(new UsageItem { Date = date, Value = costInCents / 100m, Unit = "USD" });
            }
            return list.OrderBy(x => x.Date).ToList();
        }

        private static List<UsageItem> BuildDailyFromBuckets(JObject root, Func<JToken, decimal> valueSelector, string unit)
        {
            var list = new List<UsageItem>();
            var data = root["data"] as JArray;
            if (data == null) return list;
            foreach (var bucket in data)
            {
                long startUnix = bucket["start_time"]?.Value<long>() ?? 0;
                if (startUnix <= 0) continue;
                var date = DateTimeOffset.FromUnixTimeSeconds(startUnix).UtcDateTime.ToString("yyyy-MM-dd");
                list.Add(new UsageItem { Date = date, Value = valueSelector(bucket), Unit = unit });
            }
            return list.OrderBy(x => x.Date).ToList();
        }

        private static (List<UsageItem> monthly, List<UsageItem> yearly) BuildMonthYear(List<UsageItem> daily)
        {
            var monthly = daily.GroupBy(x => x.Date.Substring(0, 7)).Select(g => new UsageItem { Date = g.Key, Value = g.Sum(x => x.Value), Unit = g.First().Unit }).OrderBy(x => x.Date).ToList();
            var yearly = daily.GroupBy(x => x.Date.Substring(0, 4)).Select(g => new UsageItem { Date = g.Key, Value = g.Sum(x => x.Value), Unit = g.First().Unit }).OrderBy(x => x.Date).ToList();
            return (monthly, yearly);
        }

        private static decimal SumUsdFromCostResults(JToken bucket)
        {
            decimal sum = 0m;
            var results = bucket["results"] as JArray;
            if (results == null) return 0m;
            foreach (var r in results)
            {
                var amount = r["amount"];
                if (amount != null) { var value = amount["value"]?.Value<decimal>(); if (value.HasValue) sum += value.Value; }
                sum += (r["cost"]?.Value<decimal>() ?? 0m);
                sum += (r["usd"]?.Value<decimal>() ?? 0m);
            }
            return sum;
        }
        public class TokenUsageRequest
        {
            public string? startDate { get; set; }
            public string? endDate { get; set; }
            public string? apigubun { get; set; }
        }

        public class ChargeCostUpdateRequest
        {
            public decimal chargecostInput { get; set; }
            public string apigubun { get; set; } = "openai";
        }
        public class ChargeCostSelectRequest
        {
            public string apigubun { get; set; } = "openai";
        }
    }
}