using System;
using System.Collections.Generic;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace SurveyonApi.Services
{
    public class RpsOpenProcessor
    {
        /// <summary>
        /// PostgreSQL 인라인 추출 Flat JSON ({"q1_op1": "1970", "q5_op2": "4"}) 고속 파싱 (Zero-Allocation)
        /// </summary>
        public static (Dictionary<string, object> result, Dictionary<string, object> resultint, Dictionary<string, object> resultall) ParseFlatOpen(string jsonString)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var resultint = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var resultall = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(jsonString))
            {
                return (result, resultint, resultall);
            }

            // 하위 호환성: 기존 중첩 JSON(ROOT 포함)인 경우 레거시 파서로 분기
            if (jsonString.IndexOf("\"ROOT\"", StringComparison.Ordinal) >= 0)
            {
                return ParseRpsOpen(jsonString);
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string key = prop.Name.Trim();
                    string valueStr = prop.Value.ValueKind == JsonValueKind.String
                        ? (prop.Value.GetString() ?? "").Trim()
                        : prop.Value.ToString().Trim();

                    ClassifyAndAddOpenValue(key, valueStr, result, resultint, resultall);
                }
            }
            catch
            {
                // 파싱 오류 시 빈 사전 반환
            }

            return (result, resultint, resultall);
        }

        public static (Dictionary<string, object> result, Dictionary<string, object> resultint, Dictionary<string, object> resultall) ParseRpsOpen(string jsonString)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var resultint = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var resultall = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(jsonString) || 
               (jsonString.IndexOf("_op", StringComparison.Ordinal) < 0 && jsonString.IndexOf("model", StringComparison.OrdinalIgnoreCase) < 0))
            {
                return (result, resultint, resultall);
            }

            try
            {
                JObject root = JObject.Parse(jsonString);
                var rootObj = root["ROOT"] as JObject;
                if (rootObj == null) return (result, resultint, resultall);

                // ROOT 아래 모든 항목 (Level 1 직속 오픈문항 또는 Level 2 섹션)
                foreach (var section in rootObj.Properties())
                {
                    var sectionObj = section.Value as JObject;
                    if (sectionObj == null) continue;

                    // 1) Level 1 직속 오픈문항: section 자체가 오픈문항인 경우 (예: ROOT.model, ROOT.adviserName_op1)
                    if ((section.Name.Contains("_op", StringComparison.OrdinalIgnoreCase) || section.Name.Contains("model", StringComparison.OrdinalIgnoreCase)) &&
                        sectionObj["@OPEN"] != null && !string.IsNullOrWhiteSpace(sectionObj["@OPEN"].ToString()) &&
                        !sectionObj["@OPEN"].ToString().Trim().Contains("data:image/png;base64"))
                    {
                        string key = section.Name.Trim();
                        string valueStr = sectionObj["@OPEN"].ToString().Trim();
                        ClassifyAndAddOpenValue(key, valueStr, result, resultint, resultall);
                    }

                    // 2) Level 2 하위 오픈문항: section(q550 등) 내부 속성 검사
                    foreach (var prop in sectionObj.Properties())
                    {
                        if ((prop.Name.Contains("_op", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("model", StringComparison.OrdinalIgnoreCase)) &&
                            prop.Value["@OPEN"] != null && !string.IsNullOrWhiteSpace(prop.Value["@OPEN"].ToString()) &&
                            !prop.Value["@OPEN"].ToString().Trim().Contains("data:image/png;base64"))
                        {
                            string key = $"{prop.Name}".Trim();
                            string valueStr = prop.Value["@OPEN"].ToString().Trim();
                            ClassifyAndAddOpenValue(key, valueStr, result, resultint, resultall);
                        }
                    }
                }

                return (result, resultint, resultall);
            }
            catch
            {   
                return (result, resultint, resultall);
            }
        }

        /// <summary>
        /// 단일 오픈문항 값 분류 (정수, 실수, 텍스트, CanDK 제거 공통 로직)
        /// </summary>
        public static void ClassifyAndAddOpenValue(string key, string valueStr, Dictionary<string, object> result, Dictionary<string, object> resultint, Dictionary<string, object> resultall)
        {
            if (valueStr.Contains("CanDK"))
            {
                valueStr = valueStr.Replace("∬CanDK", "").Replace("CanDK", "").Trim();
            }
            if (valueStr.Length > 0)
            {
                // 끝부분 불필요한 콤마/마침표 제거
                string valueStr2 = valueStr.Trim().TrimEnd(',').TrimEnd('.');
                string cleanNumStr = valueStr2.Replace(",", "").Replace(" ", "");

                // 콤마(,), 점(.)을 포함한 순수 숫자 오픈 응답 판별 (정수, 실수, 콤마 숫자)
                if (cleanNumStr.Length > 0 && int.TryParse(cleanNumStr, out int intValue))
                {
                    resultint[key] = intValue; // 정수형
                    resultall[key] = intValue;
                }
                else if (cleanNumStr.Length > 0 && long.TryParse(cleanNumStr, out long longValue))
                {
                    resultint[key] = longValue; // 큰 정수형
                    resultall[key] = longValue;
                }
                else if (cleanNumStr.Length > 0 && double.TryParse(cleanNumStr, System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out double doubleValue))
                {
                    resultint[key] = doubleValue; // 실수/소수점형 (콤마 및 소수점 포함)
                    resultall[key] = doubleValue;
                }
                else
                {
                    result[key] = valueStr; // 문자열형 (자유 텍스트)
                    resultall[key] = valueStr;
                }
            }
            else
            {
                resultall[key] = valueStr;
            }
        }

        /// <summary>
        /// 단순 무응답, 거절, 무의미 텍스트 여부 판별 (숫자 문항에 소수 응답자가 "없음", "모름" 등을 입력한 경우 처리)
        /// </summary>
        public static bool IsRefusalOrNonSubstantive(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string trimmed = text.Trim().Replace(" ", "").ToLowerInvariant();
            return trimmed switch
            {
                "없음" or "해당없음" or "해당사항없음" or "모름" or "잘모름" or "모르겠음"
                or "none" or "n/a" or "na" or "null" or "nil" or "no" or "-" or "--" or "x" or "." or "무"
                or "기억안남" or "몰라요" or "비공개" or "거부" => true,
                _ => false
            };
        }
    }
}
