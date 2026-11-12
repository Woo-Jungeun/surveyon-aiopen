using Microsoft.AspNetCore.Mvc;
using SurveyonApi.Repositories;
using SurveyonApi.Models;
using SurveyonApi.Services;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Threading.Tasks;

namespace SurveyonApi.Controllers
{
    [ApiController]
    [Route("pro_list")]
    public class Pro_listController : BaseController
    {
        private readonly INewOldJsonService _newOldJsonService;

        public Pro_listController(IMainRepository mainRepository, INewOldJsonService newOldJsonService) : base(mainRepository)
        {
            _newOldJsonService = newOldJsonService;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 1. 문항 목록 조회 [POST /pro_list/select]
        //    원본: pro_list_api.aspx.cs gb=="select" → jsonHong.SelectQnumList(user, projectnum)
        //    원본 로직: Jsonload.cs L1189-L1369 → 배치 최적화 적용
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("select")]
        public async Task<IActionResult> Select([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum))
            {
                return await ReturnError("701");
            }

            string projectnum = req.projectnum.Trim();
            var ct = HttpContext.RequestAborted;

            try
            {
                // ── Step 1: 메인 쿼리 (1회) ── SQL Window Function으로 정렬 완료
                DataTable dtList = await _mainRepository.SelectProQuestionListAsync(projectnum, "", ct);

                if (dtList.Rows.Count == 0)
                {
                    return await ReturnResponse("777", new JObject { ["resultjson"] = new JArray() });
                }

                // ── Step 2: 배치 카운트 조회 (2회) ── 원본의 N+1 → 배치
                var exceldataCountDict = await _mainRepository.SelectExceldataCountBatchAsync(projectnum, ct);
                var pnInCountDict = await _mainRepository.SelectPnInCountBatchAsync(projectnum, ct);

                // ── Step 3: C# 로직 (DB 호출 없음) ──
                var listinfo = new JArray();
                var checkmergeqnum = new List<string>();
                var checkmergeqnumyn = new Dictionary<string, string>();
                var statusUpdates = new List<(string id, string status)>();

                foreach (DataRow row in dtList.Rows)
                {
                    string id = row["id"]?.ToString()?.TrimEnd() ?? "";
                    string projectnum2 = row["projectnum"]?.ToString()?.TrimEnd() ?? "";
                    string model = row["model"]?.ToString()?.TrimEnd() ?? "";
                    string qnum = row["qnum"]?.ToString()?.TrimEnd() ?? "";
                    string qnum_text = row["qnum_text"]?.ToString()?.TrimEnd() ?? "";
                    string question_fin = row["question_fin"]?.ToString()?.TrimEnd() ?? "";
                    string question_origin = row["question_origin"]?.ToString()?.TrimEnd() ?? "";
                    string status = row["status"]?.ToString()?.TrimEnd() ?? "";

                    // status_cnt 초기값
                    string status_cnt = row["status_cnt"]?.ToString()?.TrimEnd() ?? "";
                    if (status_cnt == "") status_cnt = "0";
                    string status_cnt_duplicated = row["status_cnt"]?.ToString()?.TrimEnd() ?? "";
                    if (status_cnt_duplicated == "") status_cnt_duplicated = "0";
                    string status_cnt_fin = row["status_cnt_fin"]?.ToString()?.TrimEnd() ?? "";
                    if (status_cnt_fin == "") status_cnt_fin = "0";

                    string merge_pn = row["merge_pn"]?.ToString()?.TrimEnd() ?? "";
                    string merge_qnum = row["merge_qnum"]?.ToString()?.TrimEnd() ?? "";
                    string merge_qnum_check = row["merge_qnum_check"]?.ToString()?.TrimEnd() ?? "";
                    string useYN = row["useYN"]?.ToString()?.TrimEnd() ?? "";

                    // ── qnum_join1~5 카운트 (원본: Jsonload.cs L1253-L1263) ──
                    int qnum_join_cnt_ = 0;
                    for (int i = 1; i <= 5; i++)
                    {
                        string colName = "qnum_join" + i.ToString();
                        try
                        {
                            string val = row[colName]?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(val)) qnum_join_cnt_++;
                        }
                        catch { }
                    }
                    string qnum_join_cnt = qnum_join_cnt_.ToString();

                    // ── merge_qnum 중복 체크 → useYN "머지" 처리 (원본: L1265-L1276) ──
                    if (!checkmergeqnum.Contains(merge_qnum))
                    {
                        if (useYN == "분석")
                        {
                            checkmergeqnum.Add(merge_qnum);
                            checkmergeqnumyn[merge_qnum] = useYN;
                        }
                    }
                    else
                    {
                        if (checkmergeqnumyn.ContainsKey(merge_qnum) && checkmergeqnumyn[merge_qnum] == "분석")
                        {
                            if (useYN == "분석") useYN = "머지";
                        }
                    }

                    string project_lock = row["project_lock"]?.ToString()?.TrimEnd() ?? "";
                    string lb_info = row["lb_info"]?.ToString()?.TrimEnd() ?? "";
                    string res_info = row["res_info"]?.ToString()?.TrimEnd() ?? "";

                    // ── tokens_cost 파싱 (원본: L1281-L1300, TryParse로 안전 처리) ──
                    float tokens_cost_fin = 0;
                    if (!string.IsNullOrEmpty(lb_info))
                    {
                        try
                        {
                            JObject json = JObject.Parse(lb_info);
                            string tokens_cost = json["tokens_cost"]?.ToString()?.TrimEnd() ?? "0";
                            if (double.TryParse(tokens_cost, out double tokens_costd))
                            {
                                tokens_cost_fin = (float)Math.Round(tokens_costd, 5);
                            }
                        }
                        catch { }
                    }
                    if (!string.IsNullOrEmpty(res_info))
                    {
                        try
                        {
                            JObject json = JObject.Parse(res_info);
                            string tokens_cost = json["tokens_cost"]?.ToString()?.TrimEnd() ?? "0";
                            if (double.TryParse(tokens_cost, out double tokens_costd))
                            {
                                tokens_cost_fin += (float)Math.Round(tokens_costd, 5);
                            }
                        }
                        catch { }
                    }

                    // ── 배치 딕셔너리에서 O(1) 카운트 조회 (원본: L1302-L1324 DB 호출 → 딕셔너리) ──
                    // 응답자수 (project_list.answer_origin)
                    string exceldataKey = $"{projectnum2}|{qnum}";
                    if (exceldataCountDict.TryGetValue(exceldataKey, out string? exceldataCnt))
                    {
                        status_cnt = exceldataCnt;
                    }
                    // 분석대상수 + 분석완료수 (project_list.merge_qnum = pn_in.qnum 1:1 매칭 기준)
                    if (!string.IsNullOrEmpty(merge_qnum) && pnInCountDict.TryGetValue(merge_qnum, out var pnInCounts))
                    {
                        status_cnt_duplicated = pnInCounts.originCount;
                        status_cnt_fin = pnInCounts.finCount;

                        // pn_in 데이터의 originCount가 "0"이거나 비어있으면 기본 응답자수(status_cnt)로 폴백
                        if (status_cnt_duplicated == "0" || string.IsNullOrEmpty(status_cnt_duplicated))
                        {
                            status_cnt_duplicated = status_cnt;
                        }
                    }
                    else
                    {
                        // pn_in 레코드가 아직 생성이 안 된 경우 기본 응답자수(status_cnt)를 분석대상수로 지정
                        status_cnt_duplicated = status_cnt;
                        status_cnt_fin = "0";
                    }

                    // ── status 자동 판별 (원본: L1325-L1340) ──
                    string status_text = "";
                    if (useYN != "머지")
                    {
                        if (int.TryParse(status_cnt_duplicated, out int dupCnt) &&
                            int.TryParse(status_cnt_fin, out int finCnt))
                        {
                            if (dupCnt != 0 && dupCnt == finCnt)
                                status_text = "완료";
                            else
                                status_text = "진행중";
                        }
                        else
                        {
                            status_text = "진행중";
                        }

                        // status 변경 시 업데이트 리스트에 추가 (루프 후 배치 UPDATE)
                        if (status != status_text)
                        {
                            statusUpdates.Add((id, status_text));
                        }
                    }

                    // ── tokens_text (원본: L1341-L1344) ──
                    string tokens_text = "";
                    if (useYN != "머지")
                    {
                        tokens_text = tokens_cost_fin.ToString();
                    }

                    // ── JSON 반환 객체 생성 (원본 필드 + UI 호환 필드) ──
                    var answerkeytemp = new JObject
                    {
                        // 원본 필드
                        ["id"] = id,
                        ["mapid"] = row["mapid"] != DBNull.Value && int.TryParse(row["mapid"]?.ToString(), out int mVal) ? mVal : 0,
                        ["model"] = model,
                        ["qnum"] = qnum,
                        ["qnum_text"] = qnum_text,
                        ["question_fin"] = question_fin,
                        ["question_origin"] = question_origin,
                        ["status_cnt"] = status_cnt,
                        ["status_cnt_duplicated"] = status_cnt_duplicated,
                        ["status_cnt_fin"] = status_cnt_fin,
                        ["status_text"] = status_text,
                        ["tokens_text"] = tokens_text,
                        ["useYN"] = useYN,
                        ["merge_qnum"] = merge_qnum,
                        ["merge_qnum_check"] = merge_qnum_check,
                        ["project_lock"] = project_lock,
                        ["qnum_join_cnt"] = qnum_join_cnt,

                        // UI 호환 필드 (ProListPage.jsx 빈 셀 방지)
                        ["response_count"] = status_cnt,
                        ["analysis_target"] = status_cnt_duplicated,
                        ["analysis_complete"] = status_cnt_fin,
                        ["status"] = status_text,
                        ["progress"] = status_text,
                        ["estimated_cost"] = tokens_text,
                        ["filter_yn"] = (qnum_join_cnt != "0" ? "설정" : "미설정")
                    };
                    listinfo.Add(answerkeytemp);
                }

                // ── Step 4: 요약 통계 집계 계산 (머지 문항 그룹 완료 전파 반영) ──
                int totalQnumCount = dtList.Rows.Count;
                int completedQnumCount = 0;
                int excludedQnumCount = 0;

                // 머지(merge_qnum) 그룹 내 대표 문항 완료 여부 맵 생성
                var completedGroupSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (JObject item in listinfo)
                {
                    string statusVal = item["status_text"]?.ToString() ?? "";
                    string mergeQnum = item["merge_qnum"]?.ToString() ?? "";
                    if (statusVal == "완료" && !string.IsNullOrEmpty(mergeQnum))
                    {
                        completedGroupSet.Add(mergeQnum);
                    }
                }

                foreach (JObject item in listinfo)
                {
                    string statusVal = item["status_text"]?.ToString() ?? "";
                    string useynVal = item["useYN"]?.ToString() ?? "";
                    string mergeQnum = item["merge_qnum"]?.ToString() ?? "";

                    bool isCompleted = statusVal == "완료" || (!string.IsNullOrEmpty(mergeQnum) && completedGroupSet.Contains(mergeQnum));
                    if (isCompleted) completedQnumCount++;
                    if (useynVal == "제외") excludedQnumCount++;
                }

                double completedRatio = totalQnumCount > 0 ? Math.Round((double)completedQnumCount / totalQnumCount * 100.0, 1) : 0.0;

                // ── Step 5: 조사DB vs 설문온DB 완료자 PID 비교 (servername: NEW/RPS/QM -> 소문자 변환) ──
                string servername = (req.servername ?? "").Trim().ToLower();
                if (string.IsNullOrEmpty(servername) && dtList.Rows.Count > 0)
                {
                    if (dtList.Columns.Contains("servername") && dtList.Rows[0]["servername"] != DBNull.Value)
                    {
                        servername = dtList.Rows[0]["servername"]?.ToString()?.Trim().ToLower() ?? "";
                    }
                    if (string.IsNullOrEmpty(servername) && dtList.Columns.Contains("projectpof") && dtList.Rows[0]["projectpof"] != DBNull.Value)
                    {
                        servername = dtList.Rows[0]["projectpof"]?.ToString()?.Trim().ToLower() ?? "";
                    }
                }

                var (isManual, surveyCount, surveyonCount, openRegisteredCount, aiWaitCount, onlyInSurvey, onlyInSurveyon, unregisteredOpen, onlyInOpen) =
                    await _mainRepository.GetCompletedPidDiffAsync(projectnum, servername, ct);

                bool isAllMatch = isManual
                    ? (surveyonCount == openRegisteredCount)
                    : (surveyCount == surveyonCount && surveyonCount == openRegisteredCount);

                int pidDiffCount = onlyInSurvey.Count + onlyInSurveyon.Count + unregisteredOpen.Count + onlyInOpen.Count;
                int diffCount = isAllMatch ? 0 : pidDiffCount;
                bool hasDiff = !isAllMatch;

                // ── Step 6: status 배치 UPDATE (1회) ──
                if (statusUpdates.Count > 0)
                {
                    await _mainRepository.UpdateListInfoBatchAsync(statusUpdates, ct);
                }

                // ── 성공 응답 (집계 정보 및 PID 비교 정보 포함) ──
                return await ReturnResponse("777", new JObject
                {
                    ["resultjson"] = listinfo,
                    ["completed_stats"] = new JObject
                    {
                        ["is_manual"] = isManual,
                        ["server_type"] = servername,
                        ["survey_count"] = surveyCount,
                        ["surveyon_count"] = surveyonCount,
                        ["open_registered_count"] = openRegisteredCount,
                        ["ai_wait_count"] = aiWaitCount,
                        ["is_all_match"] = isAllMatch,
                        ["diff_count"] = diffCount,
                        ["has_diff"] = hasDiff
                    },
                    ["summary"] = new JObject
                    {
                        ["total_qnum_count"] = totalQnumCount,
                        ["completed_qnum_count"] = completedQnumCount,
                        ["completed_ratio"] = completedRatio,
                        ["excluded_qnum_count"] = excludedQnumCount
                    }
                });
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[pro_list/select Error] {ex.Message}");
                return await ReturnError("761");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 1-1. 조사DB vs 설문온DB PID 차이 세부 조회 [POST /pro_list/pid_diff]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("pid_diff")]
        public async Task<IActionResult> PidDiff([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string servername = (req.servername ?? "").Trim().ToLower();

            if (string.IsNullOrEmpty(servername))
            {
                DataTable dtList = await _mainRepository.SelectProQuestionListAsync(projectnum, "", HttpContext.RequestAborted);
                if (dtList.Rows.Count > 0)
                {
                    servername = (dtList.Rows[0]["servername"]?.ToString()?.Trim()
                              ?? dtList.Rows[0]["projectpof"]?.ToString()?.Trim()
                              ?? "").ToLower();
                }
            }

            try
            {
                var (isManual, surveyCount, surveyonCount, openRegisteredCount, aiWaitCount, onlyInSurvey, onlyInSurveyon, unregisteredOpen, onlyInOpen) =
                    await _mainRepository.GetCompletedPidDiffAsync(projectnum, servername, HttpContext.RequestAborted);

                bool isAllMatch = isManual
                    ? (surveyonCount == openRegisteredCount)
                    : (surveyCount == surveyonCount && surveyonCount == openRegisteredCount);

                int pidDiffCount = onlyInSurvey.Count + onlyInSurveyon.Count + unregisteredOpen.Count + onlyInOpen.Count;
                int diffCount = isAllMatch ? 0 : pidDiffCount;

                return await ReturnResponse("777", new JObject
                {
                    ["is_manual"] = isManual,
                    ["server_type"] = servername,
                    ["survey_count"] = surveyCount,
                    ["surveyon_count"] = surveyonCount,
                    ["open_registered_count"] = openRegisteredCount,
                    ["ai_wait_count"] = aiWaitCount,
                    ["is_all_match"] = isAllMatch,
                    ["diff_count"] = diffCount,
                    ["only_in_survey"] = JArray.FromObject(onlyInSurvey),
                    ["only_in_surveyon"] = JArray.FromObject(onlyInSurveyon),
                    ["unregistered_open"] = JArray.FromObject(unregisteredOpen),
                    ["only_in_open"] = JArray.FromObject(onlyInOpen)
                });
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[pro_list/pid_diff Error] {ex.Message}");
                return await ReturnError("761");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 2. 개별 문항 컬럼 업데이트 [POST /pro_list/analysis]
        //    qnum/qid 및 value("1"/"0"/"분석"/"제외") 유연 수용
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("analysis")]
        public async Task<IActionResult> Analysis([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");

            string projectnum = req.projectnum.Trim();
            string columnname = string.IsNullOrEmpty(req.column) ? "useYN" : req.column;
            string val = req.value ?? "0";
            if (columnname == "useYN")
            {
                if (val == "1") val = "분석";
                else if (val == "0") val = "제외";
            }
            string qid = req.qid ?? "";
            string qnum = req.qnum ?? "";
            string merge_qnum = req.merge_qnum ?? "";

            bool success = false;
            if (!string.IsNullOrEmpty(merge_qnum))
            {
                success = await _mainRepository.UpdateListInfoByMergeQnumAsync(columnname, val, projectnum, merge_qnum, HttpContext.RequestAborted);
            }
            else if (!string.IsNullOrEmpty(qid))
            {
                success = await _mainRepository.UpdateListInfoByIdAsync(columnname, val, qid, HttpContext.RequestAborted);
            }
            else if (!string.IsNullOrEmpty(qnum))
            {
                success = await _mainRepository.UpdateQuestionAnalysisYnAsync(projectnum, qnum, val, false, HttpContext.RequestAborted);
            }

            if (success) return await ReturnResponse("777", new { message = "업데이트 완료" });
            return await ReturnError("761");
        }

        // ══════════════════════════════════════════════════════════════════════
        // 3. 전체 문항 컬럼 일괄 변경 [POST /pro_list/allanalysis]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("allanalysis")]
        public async Task<IActionResult> AllAnalysis([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string columnname = string.IsNullOrEmpty(req.column) ? "useYN" : req.column;
            string val = req.value ?? "0";
            if (columnname == "useYN")
            {
                if (val == "1") val = "분석";
                else if (val == "0") val = "제외";
            }

            bool success = false;
            if (columnname == "useYN")
            {
                success = await _mainRepository.UpdateQuestionAnalysisYnAsync(projectnum, "", val, true, HttpContext.RequestAborted);
            }
            else
            {
                success = await _mainRepository.UpdateListInfoByMergePnAsync(columnname, val, projectnum, HttpContext.RequestAborted);
            }

            if (success) return await ReturnResponse("777", new { message = "전체 업데이트 완료" });
            return await ReturnError("761");
        }

        // ══════════════════════════════════════════════════════════════════════
        // 4. 개별 문항 수정불가/가능 토글 [POST /pro_list/rowEdit]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("rowEdit")]
        public async Task<IActionResult> RowEdit([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");

            string projectnum = req.projectnum.Trim();
            string columnname = string.IsNullOrEmpty(req.column) ? "project_lock" : req.column;
            string val = req.value ?? "0";
            if (columnname == "project_lock")
            {
                if (val == "1") val = "수정가능";
                else if (val == "0") val = "수정불가";
            }
            string qid = req.qid ?? "";
            string qnum = req.qnum ?? "";
            string merge_qnum = req.merge_qnum ?? "";

            bool success = false;
            if (!string.IsNullOrEmpty(merge_qnum))
            {
                success = await _mainRepository.UpdateListInfoByMergeQnumAsync(columnname, val, projectnum, merge_qnum, HttpContext.RequestAborted);
            }
            else if (!string.IsNullOrEmpty(qid))
            {
                success = await _mainRepository.UpdateListInfoByIdAsync(columnname, val, qid, HttpContext.RequestAborted);
            }
            else if (!string.IsNullOrEmpty(qnum))
            {
                success = await _mainRepository.UpdateQuestionEditYnAsync(projectnum, qnum, val, false, HttpContext.RequestAborted);
            }

            if (success) return await ReturnResponse("777", new { message = "수정 상태 변경 완료" });
            return await ReturnError("761");
        }

        // ══════════════════════════════════════════════════════════════════════
        // 5. 전체 문항 수정불가/가능 일괄 변경 [POST /pro_list/allEdit]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("allEdit")]
        public async Task<IActionResult> AllEdit([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string columnname = string.IsNullOrEmpty(req.column) ? "project_lock" : req.column;
            string val = req.value ?? "0";
            if (columnname == "project_lock")
            {
                if (val == "1") val = "수정가능";
                else if (val == "0") val = "수정불가";
            }

            bool success = false;
            if (columnname == "project_lock")
            {
                success = await _mainRepository.UpdateQuestionEditYnAsync(projectnum, "", val, true, HttpContext.RequestAborted);
            }
            else
            {
                success = await _mainRepository.UpdateListInfoByMergePnAsync(columnname, val, projectnum, HttpContext.RequestAborted);
            }

            if (success) return await ReturnResponse("777", new { message = "전체 수정 상태 변경 완료" });
            return await ReturnError("761");
        }

        // ══════════════════════════════════════════════════════════════════════
        // 6. 문항통합 저장 [POST /pro_list/allmerge]
        //    원본: pro_list_api.aspx.cs L165-L184 (gb == "allmerge")
        //    원본 호출: foreach prop → update_list_info("merge_qnum", value, key)
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("allmerge")]
        public async Task<IActionResult> AllMerge([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();

            // 원본은 val이 JObject이고 각 property를 순회하며 merge_qnum 업데이트
            var valToken = req.data;
            if (valToken == null || valToken.Type == JTokenType.Null) return await ReturnError("701");

            try
            {
                if (valToken is JObject valObj)
                {
                    foreach (var prop in valObj.Properties())
                    {
                        string key = prop.Name;        // project_list.id
                        string value = prop.Value.ToString(); // merge_qnum 값
                        await _mainRepository.UpdateListInfoByIdAsync("merge_qnum", value, key, HttpContext.RequestAborted);
                    }
                }
                else
                {
                    // JArray 형태로 올 수도 있음 → 기존 SaveQuestionMergeAsync 호출
                    string jsonPayload = valToken.ToString();
                    bool success = await _mainRepository.SaveQuestionMergeAsync(projectnum, jsonPayload, HttpContext.RequestAborted);
                    if (!success) return await ReturnError("761");
                }

                return await ReturnResponse("777", new { message = "문항통합 저장 완료" });
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[pro_list/allmerge Error] {ex.Message}");
                return await ReturnError("761");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 7~13. 보기 추출/등록/응답추출/필터 (기존 로직 유지)
        // ══════════════════════════════════════════════════════════════════════

        // 7. 보기 추출(개발자용) [POST /pro_list/export_lb_excel]
        [HttpPost("export_lb_excel")]
        public async Task<IActionResult> ExportLbExcel([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();

            var arr = await _mainRepository.GetExportLbExcelJsonAsync(projectnum, HttpContext.RequestAborted);
            return await ReturnResponse("777", new JObject { ["resultjson"] = arr });
        }

        // 7-1. 보기 추출(개발자용) 엑셀(.xlsx) 파일 다운로드 [GET/POST /pro_list/export_lb_excel_file]
        [HttpGet("export_lb_excel_file")]
        [HttpPost("export_lb_excel_file")]
        public async Task<IActionResult> ExportLbExcelFile([FromQuery] string? projectnum, [FromBody] ProListRequest? req)
        {
            string pn = projectnum ?? req?.projectnum ?? "";
            if (string.IsNullOrEmpty(pn)) return await ReturnError("701");
            string cleanPn = pn.Trim();

            var arr = await _mainRepository.GetExportLbExcelJsonAsync(cleanPn, HttpContext.RequestAborted);

            try
            {
                // 1. Aspose.Cells 라이선스 적용
                string licPath = System.IO.Path.Combine(AppContext.BaseDirectory, "License", "Aspose.Total.lic");
                if (!System.IO.File.Exists(licPath)) licPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "License", "Aspose.Total.lic");

                if (System.IO.File.Exists(licPath))
                {
                    try { new Aspose.Cells.License().SetLicense(licPath); }
                    catch (Exception licEx) { Console.WriteLine($"[ExportLbExcelFile] License warning: {licEx.Message}"); }
                }

                // 2. Excel 샘플 템플릿 파일 경로 탐색
                string templatePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Excel", "HR_Sample_LB.xlsx");
                if (!System.IO.File.Exists(templatePath)) templatePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Excel", "HR_Sample_LB.xlsx");

                Aspose.Cells.Workbook wb;
                Aspose.Cells.Worksheet worksheet;

                if (System.IO.File.Exists(templatePath))
                {
                    wb = new Aspose.Cells.Workbook(templatePath);
                    worksheet = wb.Worksheets["pn"] ?? wb.Worksheets[0];
                }
                else
                {
                    wb = new Aspose.Cells.Workbook();
                    worksheet = wb.Worksheets[0];
                    worksheet.Cells[1, 0].Value = "문번호";
                    worksheet.Cells[1, 1].Value = "문항번호";
                    worksheet.Cells[1, 2].Value = "문항질문";
                    worksheet.Cells[1, 3].Value = "보기코드";
                    worksheet.Cells[1, 4].Value = "보기명(3단계)";
                    worksheet.Cells[1, 5].Value = "2단계코드";
                    worksheet.Cells[1, 6].Value = "2단계명";
                    worksheet.Cells[1, 7].Value = "1단계코드";
                    worksheet.Cells[1, 8].Value = "1단계명";
                }

                worksheet.Name = cleanPn;
                worksheet.Cells[0, 0].Value = cleanPn;

                int rowcount = 2; // Row 2부터 데이터 입력
                foreach (JObject item in arr)
                {
                    string qnum = item["qnum"]?.ToString() ?? "";
                    string questionOrigin = item["question_origin"]?.ToString() ?? "";
                    string lv123code = item["lv123code"]?.ToString() ?? "";
                    string lv3 = item["lv3"]?.ToString() ?? "";
                    string lv2code = item["lv2code"]?.ToString() ?? "";
                    string lv2 = item["lv2"]?.ToString() ?? "";
                    string lv1code = item["lv1code"]?.ToString() ?? "";
                    string lv1 = item["lv1"]?.ToString() ?? "";

                    worksheet.Cells[rowcount, 0].Value = qnum;
                    worksheet.Cells[rowcount, 1].Value = qnum;
                    worksheet.Cells[rowcount, 2].Value = questionOrigin;
                    worksheet.Cells[rowcount, 3].Value = lv123code;
                    worksheet.Cells[rowcount, 4].Value = lv3;

                    if (!string.IsNullOrEmpty(lv2code))
                    {
                        worksheet.Cells[rowcount, 5].Value = lv2code;
                        worksheet.Cells[rowcount, 6].Value = lv2;
                    }
                    if (!string.IsNullOrEmpty(lv1code))
                    {
                        worksheet.Cells[rowcount, 7].Value = lv1code;
                        worksheet.Cells[rowcount, 8].Value = lv1;
                    }
                    rowcount++;
                }

                using var ms = new System.IO.MemoryStream();
                wb.Save(ms, Aspose.Cells.SaveFormat.Xlsx);
                byte[] excelBytes = ms.ToArray();

                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{cleanPn}_보기추출_개발자용.xlsx");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExportLbExcelFile ERROR] {ex}");
                return await ReturnError("761", "엑셀 파일 생성 중 오류가 발생했습니다: " + ex.Message);
            }
        }

        // 8. 보기 추출(DP용) [POST /pro_list/export_lb_dp]
        [HttpPost("export_lb_dp")]
        public async Task<IActionResult> ExportLbDp([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();

            var (scriptText, _) = await _mainRepository.GetExportLbDpScriptWithProjectNameAsync(projectnum, HttpContext.RequestAborted);
            if (string.IsNullOrEmpty(scriptText))
            {
                return await ReturnError("720", "추출할 데이터가 없습니다.");
            }

            DataTable dtLb = await _mainRepository.SelectQuestionLabelsAsync(projectnum, "dp", HttpContext.RequestAborted);
            var arr = new JArray();
            foreach (DataRow row in dtLb.Rows)
            {
                arr.Add(new JObject
                {
                    ["projectnum"] = row["projectnum"]?.ToString() ?? "",
                    ["qnum"] = row["qnum"]?.ToString() ?? "",
                    ["result_json"] = row["result_json"]?.ToString() ?? ""
                });
            }
            return await ReturnResponse("777", new JObject
            {
                ["resultjson"] = arr,
                ["script_text"] = scriptText
            });
        }

        // 8-1. 보기 추출(DP용) STP 텍스트 파일 다운로드 [GET/POST /pro_list/export_lb_dp_file]
        [HttpGet("export_lb_dp_file")]
        [HttpPost("export_lb_dp_file")]
        public async Task<IActionResult> ExportLbDpFile([FromQuery] string? projectnum, [FromBody] ProListRequest? req)
        {
            string pn = projectnum ?? req?.projectnum ?? "";
            if (string.IsNullOrEmpty(pn)) return await ReturnError("701");

            var (scriptText, projectName) = await _mainRepository.GetExportLbDpScriptWithProjectNameAsync(pn.Trim(), HttpContext.RequestAborted);
            if (string.IsNullOrEmpty(scriptText))
            {
                return await ReturnError("720", "추출할 데이터가 없습니다.");
            }

            byte[] preamble = System.Text.Encoding.UTF8.GetPreamble();
            byte[] body = System.Text.Encoding.UTF8.GetBytes(scriptText);
            byte[] fileBytes = preamble.Concat(body).ToArray();

            string sanitizedName = !string.IsNullOrWhiteSpace(projectName)
                ? projectName.Replace("/", "_").Replace("\\", "_").Trim()
                : $"{pn.Trim()}_DP";

            return File(fileBytes, "text/plain; charset=utf-8", $"{sanitizedName}.txt");
        }

        // 9. 보기등록(ALL) [POST /pro_list/import_lb_all]
        [HttpPost("import_lb_all")]
        public async Task<IActionResult> ImportLbAll([FromBody] ProListImportLbAllRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string user = req.user?.Trim() ?? "";
            string jsonLbPayload = req.data_lb?.ToString() ?? "";
            if (string.IsNullOrEmpty(jsonLbPayload)) return await ReturnError("701");

            bool success = await _mainRepository.ImportQuestionLabelsAllAsync(projectnum, user, jsonLbPayload, HttpContext.RequestAborted);
            if (success) return await ReturnResponse("777", new { message = "보기 일괄 등록 완료" });
            return await ReturnError("761");
        }

        // 9-1. 보기등록(ALL) 엑셀 파일 업로드 [POST /pro_list/import_lb_all_excel]
        [HttpPost("import_lb_all_excel")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportLbAllExcel([FromForm] ProListImportLbAllExcelRequest req)
        {
            var file = req?.file;
            if (file == null || file.Length == 0)
            {
                if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                {
                    file = Request.Form.Files.FirstOrDefault();
                }
            }

            string pn = req?.projectnum ?? Request.Form["projectnum"].FirstOrDefault() ?? Request.Form["pn"].FirstOrDefault() ?? "";
            string userVal = req?.user ?? Request.Form["user"].FirstOrDefault() ?? "";

            if (file == null || file.Length == 0 || string.IsNullOrWhiteSpace(pn))
            {
                return await ReturnError("701", "엑셀 파일 및 프로젝트 번호가 필요합니다.");
            }

            try
            {
                string licPath = System.IO.Path.Combine(AppContext.BaseDirectory, "License", "Aspose.Total.lic");
                if (!System.IO.File.Exists(licPath)) licPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "License", "Aspose.Total.lic");
                if (System.IO.File.Exists(licPath))
                {
                    try { new Aspose.Cells.License().SetLicense(licPath); } catch { }
                }

                using var stream = file.OpenReadStream();
                var wb = new Aspose.Cells.Workbook(stream);
                var sheet = wb.Worksheets[0];

                int maxCol = sheet.Cells.MaxDataColumn;
                int maxRow = sheet.Cells.MaxDataRow;

                if (maxCol < 0 || maxRow < 2)
                {
                    return await ReturnError("701", "엑셀 파일에 유효한 데이터가 없습니다.");
                }

                // 2행 (Index 1) 헤더 및 기본 컬럼 위치 매핑
                int varCol = 0, codeCol = 3, lv3Col = 4, lv2codeCol = 5, lv2Col = 6, lv1codeCol = 7, lv1Col = 8;
                for (int c = 0; c <= maxCol; c++)
                {
                    string hName = sheet.Cells[1, c].StringValue?.Trim()?.ToLower() ?? "";
                    if (hName == "변수명" || hName == "qnum" || hName == "variable") varCol = c;
                    else if (hName == "코드" || hName == "code" || hName == "lv123code") codeCol = c;
                    else if (hName == "라벨" || hName == "label" || hName == "lv3") lv3Col = c;
                    else if (hName == "1단계cd" || hName == "1단계코드" || hName == "lv2code") lv2codeCol = c;
                    else if (hName == "1단계nm" || hName == "1단계명칭" || hName == "1단계라벨" || hName == "lv2") lv2Col = c;
                    else if (hName == "2단계cd" || hName == "2단계코드" || hName == "lv1code") lv1codeCol = c;
                    else if (hName == "2단계nm" || hName == "2단계명칭" || hName == "2단계라벨" || hName == "lv1") lv1Col = c;
                }

                var grouped = new Dictionary<string, List<JObject>>();
                int totalItemsCount = 0;

                // 3행 (Index 2)부터 실제 보기 데이터 파싱
                for (int r = 2; r <= maxRow; r++)
                {
                    string varName = sheet.Cells[r, varCol].StringValue?.Trim() ?? "";
                    if (string.IsNullOrEmpty(varName)) continue;

                    string codeVal = sheet.Cells[r, codeCol].StringValue?.Trim() ?? "";
                    string lv3Val = sheet.Cells[r, lv3Col].StringValue?.Trim() ?? "";
                    string lv2codeVal = sheet.Cells[r, lv2codeCol].StringValue?.Trim() ?? "";
                    string lv2Val = sheet.Cells[r, lv2Col].StringValue?.Trim() ?? "";
                    string lv1codeVal = sheet.Cells[r, lv1codeCol].StringValue?.Trim() ?? "";
                    string lv1Val = sheet.Cells[r, lv1Col].StringValue?.Trim() ?? "";

                    var item = new JObject
                    {
                        ["lv3"] = lv3Val,
                        ["lv123code"] = codeVal,
                        ["lv2code"] = lv2codeVal,
                        ["lv2"] = lv2Val,
                        ["lv1code"] = lv1codeVal,
                        ["lv1"] = lv1Val
                    };

                    if (!grouped.ContainsKey(varName))
                    {
                        grouped[varName] = new List<JObject>();
                    }
                    grouped[varName].Add(item);
                    totalItemsCount++;
                }

                if (grouped.Count == 0)
                {
                    return await ReturnError("701", "파싱할 보기 데이터가 없습니다.");
                }

                var dataLbArr = new JArray();
                foreach (var g in grouped)
                {
                    var varObj = new JObject
                    {
                        [g.Key] = JArray.FromObject(g.Value)
                    };
                    dataLbArr.Add(varObj);
                }

                bool success = await _mainRepository.ImportQuestionLabelsAllAsync(pn.Trim(), userVal.Trim(), dataLbArr.ToString(), HttpContext.RequestAborted);
                if (success)
                {
                    return await ReturnResponse("777", new
                    {
                        message = "보기 일괄 등록 완료",
                        variables_count = grouped.Count,
                        items_count = totalItemsCount
                    });
                }
                return await ReturnError("761");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImportLbAllExcel ERROR] {ex.Message}\n{ex.StackTrace}");
                return await ReturnError("700", $"엑셀 파싱 중 오류 발생: {ex.Message}");
            }
        }

        // 10. 응답추출(ALL) [POST /pro_list/export_data_all]
        [HttpPost("export_data_all")]
        public async Task<IActionResult> ExportDataAll([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();

            var arr = await _mainRepository.GetExportDataAllJsonAsync(projectnum, HttpContext.RequestAborted);
            return await ReturnResponse("777", new JObject { ["resultjson"] = arr });
        }

        // 10-1. 응답추출(ALL) 엑셀(.xlsx) 파일 다운로드 [GET/POST /pro_list/export_data_all_file]
        [HttpGet("export_data_all_file")]
        [HttpPost("export_data_all_file")]
        public async Task<IActionResult> ExportDataAllFile([FromQuery] string? projectnum, [FromBody] ProListRequest? req)
        {
            string pn = projectnum ?? req?.projectnum ?? "";
            if (string.IsNullOrEmpty(pn)) return await ReturnError("701");
            string cleanPn = pn.Trim();

            DataTable dtExport = await _mainRepository.GetExportDataAllTableAsync(cleanPn, HttpContext.RequestAborted);

            try
            {
                // 1. Aspose.Cells 라이선스 적용
                string licPath = System.IO.Path.Combine(AppContext.BaseDirectory, "License", "Aspose.Total.lic");
                if (!System.IO.File.Exists(licPath)) licPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "License", "Aspose.Total.lic");

                if (System.IO.File.Exists(licPath))
                {
                    try { new Aspose.Cells.License().SetLicense(licPath); }
                    catch (Exception licEx) { Console.WriteLine($"[ExportDataAllFile] License warning: {licEx.Message}"); }
                }

                // 2. Excel 샘플 템플릿 파일 경로 탐색
                string templatePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Excel", "HR_Sample_ExportDataAll.xlsx");
                if (!System.IO.File.Exists(templatePath)) templatePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Excel", "HR_Sample_ExportDataAll.xlsx");

                Aspose.Cells.Workbook wb;
                Aspose.Cells.Worksheet worksheet;

                if (System.IO.File.Exists(templatePath))
                {
                    wb = new Aspose.Cells.Workbook(templatePath);
                    worksheet = wb.Worksheets["pn"] ?? wb.Worksheets[0];
                }
                else
                {
                    wb = new Aspose.Cells.Workbook();
                    worksheet = wb.Worksheets[0];
                }

                worksheet.Name = cleanPn;

                // 3. 컬럼 헤더 기입
                // 3-1. 변수명 행 (Row 0): DisplayHeader 기입
                for (int col = 0; col < dtExport.Columns.Count; col++)
                {
                    string headerText = dtExport.Columns[col].ExtendedProperties["DisplayHeader"]?.ToString()
                                        ?? dtExport.Columns[col].ColumnName;
                    worksheet.Cells[0, col].PutValue(headerText);
                }

                // 3-2. 문항내용 행 (Row 1): QuestionOrigin 기입 및 자동 줄바꿈 설정
                for (int col = 0; col < dtExport.Columns.Count; col++)
                {
                    string qoText = dtExport.Columns[col].ExtendedProperties["QuestionOrigin"]?.ToString() ?? "";
                    var cell = worksheet.Cells[1, col];
                    cell.PutValue(qoText);

                    if (!string.IsNullOrEmpty(qoText))
                    {
                        Aspose.Cells.Style style = cell.GetStyle();
                        style.IsTextWrapped = true;
                        cell.SetStyle(style);
                    }
                }

                // 4. 데이터 기입 (Row 2 ~ N+1)
                for (int r = 0; r < dtExport.Rows.Count; r++)
                {
                    for (int col = 0; col < dtExport.Columns.Count; col++)
                    {
                        worksheet.Cells[r + 2, col].PutValue(dtExport.Rows[r][col]?.ToString() ?? "");
                    }
                }

                // 5. 열 너비 및 행 높이 조절
                worksheet.AutoFitColumns();

                for (int col = 0; col < dtExport.Columns.Count; col++)
                {
                    if (col == 0)
                    {
                        // PID 컬럼: 기본 AutoFit 유지하되 최소 12 확보
                        if (worksheet.Cells.GetColumnWidth(col) < 12)
                            worksheet.Cells.SetColumnWidth(col, 12);
                    }
                    else
                    {
                        // 문항 및 데이터 컬럼: 엑셀 기준 너비 18 설정
                        worksheet.Cells.SetColumnWidth(col, 18);
                    }
                }

                // 문항레이블 줄바꿈에 따른 행 높이 자동 조절
                worksheet.AutoFitRow(1);

                using var ms = new System.IO.MemoryStream();
                wb.Save(ms, Aspose.Cells.SaveFormat.Xlsx);
                byte[] excelBytes = ms.ToArray();

                return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{cleanPn}_응답추출_전체.xlsx");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExportDataAllFile ERROR] {ex}");
                return await ReturnError("761", "엑셀 파일 생성 중 오류가 발생했습니다: " + ex.Message);
            }
        }

        // 11. 필터문항 설정 - 전체 일괄 저장 [POST /pro_list/filter_update_all]
        [HttpPost("filter_update_all")]
        public async Task<IActionResult> FilterUpdateAll([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            var selectedVars = ExtractSelectedVars(req);

            bool success = await _mainRepository.UpdateFilterAllAsync(projectnum, selectedVars, HttpContext.RequestAborted);
            if (success) return await ReturnResponse("777", new { message = "전체 분석문항 필터 일괄 설정 저장 완료" });
            return await ReturnError("761");
        }

        // 12. 필터문항 설정 - 각문항별 저장 [POST /pro_list/filter_update_single]
        [HttpPost("filter_update_single")]
        public async Task<IActionResult> FilterUpdateSingle([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string qnum = !string.IsNullOrEmpty(req.qnum) ? req.qnum : (req.qid ?? "");
            var selectedVars = ExtractSelectedVars(req);

            bool success = await _mainRepository.UpdateFilterSingleAsync(projectnum, qnum, selectedVars, HttpContext.RequestAborted);
            if (success) return await ReturnResponse("777", new { message = "필터 문항별 설정 저장 완료" });
            return await ReturnError("761");
        }

        [HttpPost("filter_select_qnum")]
        public async Task<IActionResult> FilterSelectQnum([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string qnum = !string.IsNullOrEmpty(req.qnum) ? req.qnum : (req.qid ?? "");

            JArray listinfo = await _mainRepository.GetFilterSelectQnumListAsync(projectnum, qnum, HttpContext.RequestAborted);
            return await ReturnResponse("777", listinfo);
        }

        // ══════════════════════════════════════════════════════════════════════
        // 14. 문항최종 일괄 수정 [POST /pro_list/batch_question_fin]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("batch_question_fin")]
        public async Task<IActionResult> BatchQuestionFin([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();

            var finMap = new Dictionary<string, string>();
            if (req.data is JObject jo)
            {
                foreach (var prop in jo.Properties())
                {
                    finMap[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }
            else if (req.data is JArray ja)
            {
                foreach (JObject item in ja)
                {
                    string id = item["id"]?.ToString() ?? "";
                    string qFin = item["question_fin"]?.ToString() ?? item["value"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id)) finMap[id] = qFin;
                }
            }

            try
            {
                bool success = await _mainRepository.BatchUpdateQuestionFinAsync(projectnum, finMap, HttpContext.RequestAborted);
                if (success) return await ReturnResponse("777", new { message = "문항최종 일괄 수정 완료" });
                return await ReturnError("761");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[pro_list/batch_question_fin Error] {ex.Message}");
                return await ReturnError("761");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 15. 다중 선택 문항 일괄 설정 (분석/제외, 수정 가능/잠금) [POST /pro_list/bulk_update]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("bulk_update")]
        public async Task<IActionResult> BulkUpdate([FromBody] ProListRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum)) return await ReturnError("701");
            string projectnum = req.projectnum.Trim();
            string columnName = string.IsNullOrEmpty(req.column) ? "useYN" : req.column;
            string val = req.value ?? "0";

            if (columnName == "useYN")
            {
                if (val == "1") val = "분석";
                else if (val == "0") val = "제외";
            }
            else if (columnName == "project_lock")
            {
                if (val == "1") val = "수정가능";
                else if (val == "0") val = "수정불가";
            }

            var intIds = new List<int>();
            if (req.ids != null)
            {
                foreach (var s in req.ids)
                {
                    if (int.TryParse(s, out int idVal)) intIds.Add(idVal);
                }
            }
            var mergeQnums = req.merge_qnums ?? new List<string>();

            try
            {
                bool success = await _mainRepository.BulkUpdateQuestionColumnAsync(columnName, val, projectnum, intIds, mergeQnums, HttpContext.RequestAborted);
                if (success) return await ReturnResponse("777", new { message = "일괄 설정 완료" });
                return await ReturnError("761");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[pro_list/bulk_update Error] {ex.Message}");
                return await ReturnError("761");
            }
        }

        private List<string> ExtractSelectedVars(ProListRequest req)
        {
            var list = new List<string>();
            if (req.data != null)
            {
                if (req.data is JObject jo && jo["selected"] is JArray arrSel)
                {
                    foreach (var item in arrSel) list.Add(item.ToString().Trim());
                }
                else if (req.data is JArray arrData)
                {
                    foreach (var item in arrData)
                    {
                        if (item is JObject rowObj && rowObj["qnum_variable"] != null)
                            list.Add(rowObj["qnum_variable"]!.ToString().Trim());
                        else
                            list.Add(item.ToString().Trim());
                    }
                }
            }
            if (list.Count == 0 && !string.IsNullOrEmpty(req.datajson))
            {
                try
                {
                    var arr = JArray.Parse(req.datajson);
                    foreach (JObject rowObj in arr)
                    {
                        string v = rowObj["qnum_variable"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(v)) list.Add(v);
                    }
                }
                catch { }
            }
            return list;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 문항 삭제 [POST /pro_list/qnum_delete]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("qnum_delete")]
        public async Task<IActionResult> DeleteQnum([FromBody] ProListQnumDeleteRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.projectnum))
            {
                return await ReturnError("701");
            }

            if ((req.qnums == null || req.qnums.Count == 0) && (req.ids == null || req.ids.Count == 0))
            {
                return await ReturnError("701");
            }

            var ct = HttpContext.RequestAborted;
            try
            {
                bool deleted = await _mainRepository.DeleteProjectListQuestionsAsync(req.projectnum, req.qnums, req.ids, ct);
                if (!deleted)
                {
                    return await ReturnError("761");
                }

                // 동기화: NewOldCheckAsync 호출하여 pn_in 및 동기화 테이블 집계 반영
                await _newOldJsonService.NewOldCheckAsync(req.projectnum, "", "", ct);

                return await ReturnResponse("777", new JObject { ["message"] = "문항 삭제가 완료되었습니다." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[qnum_delete ERROR] projectnum={req.projectnum}: {ex.Message}\n{ex.StackTrace}");
                return await ReturnError("761");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 17. 한큐분석 범용 옵션 조회 [GET /pro_list/option_all]
        // ══════════════════════════════════════════════════════════════════════
        [HttpGet("option_all")]
        [HttpPost("option_all")]
        public async Task<IActionResult> GetOptionAll([FromQuery] string? projectnum, [FromQuery] string? user, [FromBody] JObject? body = null)
        {
            string pn = projectnum ?? body?["projectnum"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(pn)) return await ReturnError("701");
            var ct = HttpContext.RequestAborted;
            try
            {
                string? rawJson = await _mainRepository.GetOptionJsonAllAsync(pn.Trim(), ct);
                JToken? parsed = null;
                if (!string.IsNullOrWhiteSpace(rawJson))
                {
                    try
                    {
                        var arr = JToken.Parse(rawJson);
                        if (arr is JArray jarr && jarr.Count > 0)
                        {
                            parsed = jarr[0];
                        }
                        else
                        {
                            parsed = arr;
                        }
                    }
                    catch { }
                }

                return await ReturnResponse("777", new JObject
                {
                    ["exists"] = parsed != null,
                    ["data"] = parsed ?? new JObject
                    {
                        ["lvcode"] = "3",
                        ["model_select"] = "GPT-5.2",
                        ["result_lang"] = "Korean",
                        ["temperature"] = 0.2,
                        ["open_item_lv1"] = "0~50",
                        ["open_item_lv2"] = "0",
                        ["open_item_lv3"] = "0",
                        ["prompt_string"] = ""
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[pro_list/option_all ERROR] {ex.Message}");
                return await ReturnError("761");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 18. 한큐분석 범용 옵션 저장 [POST /pro_list/option_all/save]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("option_all/save")]
        public async Task<IActionResult> SaveOptionAll([FromBody] JObject req)
        {
            if (req == null) return await ReturnError("701");
            string projectnum = req["projectnum"]?.ToString()?.Trim() ?? "";
            string user = req["user"]?.ToString()?.Trim() ?? "";
            var settings = req["settings"] as JObject ?? req["option_json_all"] as JObject;

            if (string.IsNullOrWhiteSpace(projectnum)) return await ReturnError("701");
            var ct = HttpContext.RequestAborted;

            try
            {
                JArray arrToSave;
                if (settings != null)
                {
                    arrToSave = new JArray { settings };
                }
                else if (req["option_json_all"] is JArray rawArr)
                {
                    arrToSave = rawArr;
                }
                else
                {
                    return await ReturnError("701");
                }

                string jsonStr = arrToSave.ToString(Newtonsoft.Json.Formatting.None);
                bool success = await _mainRepository.SaveOptionJsonAllAsync(projectnum, jsonStr, user, ct);

                if (success)
                {
                    return await ReturnResponse("777", new JObject { ["message"] = "범용 옵션이 성공적으로 저장되었습니다." });
                }
                return await ReturnError("761");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[pro_list/option_all/save ERROR] {ex.Message}");
                return await ReturnError("761");
            }
        }
    }
}


