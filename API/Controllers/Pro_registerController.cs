using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SurveyonApi.Repositories;
using SurveyonApi.Models;
using SurveyonApi.Services;
using Newtonsoft.Json.Linq;
using Npgsql;
using System.Data;
using System.Text.RegularExpressions;

namespace SurveyonApi.Controllers
{
    public class ProRegisterDbSelectRequest
    {
        public string projectnum { get; set; } = "";
        public string server { get; set; } = "rps";
        public string user { get; set; } = "";
    }

    public class ProRegisterDbEnterRequest
    {
        public string projectnum { get; set; } = "";
        public string server { get; set; } = "rps";
        public string pof { get; set; } = "";
        public string proname { get; set; } = "";
        public string user { get; set; } = "";
    }

    public class ProRegisterExcelEnterRequest
    {
        public string projectnum { get; set; } = "";
        public string projectname { get; set; } = "";
        public string projectpof { get; set; } = "";
        public string model { get; set; } = "설문온";
        public string user { get; set; } = "";
        public JArray? data { get; set; }
    }

    [ApiController]
    [Route("pro_register")]
    public class Pro_registerController : BaseController
    {
        private readonly IPostgreLib _postgreLib;
        private readonly INewOldJsonService _newOldJsonService;

        public Pro_registerController(IMainRepository mainRepository, IPostgreLib postgreLib, INewOldJsonService newOldJsonService) : base(mainRepository)
        {
            _postgreLib = postgreLib;
            _newOldJsonService = newOldJsonService;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 1. 오픈 변수 목록 및 등록 현황 조회 [POST /pro_register/db_select]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("db_select")]
        public async Task<IActionResult> DbSelect([FromBody] ProRegisterDbSelectRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum))
            {
                return await ReturnError("701");
            }

            string projectnum = req.projectnum.Trim();
            string server = string.IsNullOrEmpty(req.server) ? "rps" : req.server.Trim().ToLower();
            string user = req.user?.Trim() ?? "";
            var ct = HttpContext.RequestAborted;

            try
            {
                JArray listArr = await _mainRepository.GetProRegisterDbSelectInfoAsync(projectnum, server, user, ct);
                return await ReturnResponse("777", listArr);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex) when (ex.Message.Contains("Query was cancelled") || ct.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                return await ReturnError("771", ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 2. DB 기반 오픈 데이터 추가/신규 등록 [POST /pro_register/db_enter]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("db_enter")]
        public async Task<IActionResult> DbEnter([FromBody] ProRegisterDbEnterRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.projectnum))
            {
                return await ReturnError("701");
            }

            string projectnum = req.projectnum.Trim();
            string server = string.IsNullOrEmpty(req.server) ? "rps" : req.server.Trim();
            string pof = req.pof?.Trim() ?? "";
            string proname = req.proname?.Trim() ?? "";
            string user = req.user?.Trim() ?? "";
            var ct = HttpContext.RequestAborted;

            try
            {
                var (success, addedQCount, updatedQCount, addedPidCount) = await _mainRepository.InsertOpenDataAsync(
                    projectnum: projectnum,
                    server: server,
                    pof: pof,
                    proname: proname,
                    user: user,
                    isIncremental: true,
                    ct: ct
                );

                if (success)
                {
                    string message = $"오픈데이터가 성공적으로 등록되었습니다. (신규 문항: {addedQCount}건, 기존 문항 PID 추가/업데이트: {updatedQCount}건, 총 PID 추가: {addedPidCount}명)";
                    return await ReturnResponse("777", new JObject 
                    { 
                        ["message"] = message,
                        ["added_q_count"] = addedQCount,
                        ["updated_q_count"] = updatedQCount,
                        ["added_pid_count"] = addedPidCount
                    });
                }

                return await ReturnError("767");
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex) when (ex.Message.Contains("Query was cancelled") || ct.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                return await ReturnError("767");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 3. 샘플 엑셀 파일 다운로드 [POST /pro_register/excel_sample, GET 호환]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("excel_sample")]
        [HttpGet("excel_sample")]
        [AllowAnonymous]
        public async Task<IActionResult> ExcelSample()
        {
            try
            {
                string samplePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Excel", "HR_DB_Upload_Sample.xlsx");
                if (!System.IO.File.Exists(samplePath))
                {
                    samplePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Excel", "HR_DB_Upload_Sample.xlsx");
                }

                if (!System.IO.File.Exists(samplePath))
                {
                    return NotFound(new { success = false, message = "샘플 엑셀 파일을 찾을 수 없습니다." });
                }

                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(samplePath);
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "HR_DB_Upload_Sample.xlsx");
            }
            catch (Exception ex)
            {
                return await ReturnError("771", ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 4. 업로드된 엑셀 파일 파싱 (1행: 질문명, 2행: 컬럼변수명, 1열: id 고정) [POST /pro_register/excel_parse]
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("excel_parse")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ExcelParse(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                if (Request.HasFormContentType && Request.Form.Files.Count > 0)
                {
                    file = Request.Form.Files.FirstOrDefault();
                }
            }

            if (file == null || file.Length == 0)
            {
                return await ReturnError("701", "업로드할 엑셀 파일을 선택하세요.");
            }

            try
            {
                // Aspose.Cells 라이선스 적용
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

                if (maxCol < 0 || maxRow < 1)
                {
                    return await ReturnError("701", "엑셀 파일에 유효한 데이터가 없습니다.");
                }

                var columns = new JArray();
                string idColumnName = "id";

                // 0번째 열(컬럼 0)이 id 인지 확인
                string row1Col0 = sheet.Cells[0, 0].StringValue?.Trim() ?? "";
                string row2Col0 = sheet.Cells[1, 0].StringValue?.Trim() ?? "";
                if (!string.IsNullOrEmpty(row2Col0)) idColumnName = row2Col0;
                else if (!string.IsNullOrEmpty(row1Col0)) idColumnName = row1Col0;

                // 1번째 열부터 문항 컬럼 파싱
                for (int c = 1; c <= maxCol; c++)
                {
                    string qTitle = sheet.Cells[0, c].StringValue?.Trim() ?? "";
                    string qVar = sheet.Cells[1, c].StringValue?.Trim() ?? "";

                    if (string.IsNullOrEmpty(qVar) && string.IsNullOrEmpty(qTitle)) continue;

                    string finalVar = !string.IsNullOrEmpty(qVar) ? qVar : qTitle;
                    string finalTitle = !string.IsNullOrEmpty(qTitle) ? qTitle : finalVar;

                    columns.Add(new JObject
                    {
                        ["col_index"] = c,
                        ["qnum"] = finalVar,
                        ["qnum_text"] = finalTitle
                    });
                }

                // 샘플 데이터 행 추출 (최대 10행)
                var rowsData = new JArray();
                for (int r = 2; r <= Math.Min(maxRow, 20); r++)
                {
                    string pid = sheet.Cells[r, 0].StringValue?.Trim() ?? "";
                    if (string.IsNullOrEmpty(pid)) continue;

                    var rowObj = new JObject { ["pid"] = pid };
                    foreach (JObject colObj in columns)
                    {
                        int cIdx = (int)colObj["col_index"]!;
                        string qKey = (string)colObj["qnum"]!;
                        rowObj[qKey] = sheet.Cells[r, cIdx].StringValue?.Trim() ?? "";
                    }
                    rowsData.Add(rowObj);
                }

                return await ReturnResponse("777", new JObject
                {
                    ["id_column"] = idColumnName,
                    ["columns"] = columns,
                    ["sample_rows"] = rowsData
                });
            }
            catch (Exception ex)
            {
                return await ReturnError("764", ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 5. 엑셀 기반 문항/응답 등록 [POST /pro_register/excel_enter]
        //    - Multipart/form-data: 엑셀 파일 직접 업로드 & 서버 일괄 파싱 (413 방지, 전체 응답자 등록)
        //    - Application/json: 기존 레거시 JSON 요청 하위 호환
        // ══════════════════════════════════════════════════════════════════════
        [HttpPost("excel_enter")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> ExcelEnter()
        {
            string projectnum = "";
            string projectname = "";
            string projectpof = "";
            string model = "excel";
            string user = "";
            var dict = new Dictionary<string, JArray>(StringComparer.OrdinalIgnoreCase);
            var orderedQuestions = new List<(string Qnum, string QnumText)>();
            var seenQnums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ct = HttpContext.RequestAborted;

            try
            {
                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync(ct);
                    IFormFile? file = form.Files["file"] ?? form.Files.FirstOrDefault();
                    projectnum = form["projectnum"].FirstOrDefault()?.Trim() ?? form["pn"].FirstOrDefault()?.Trim() ?? "";
                    projectname = form["projectname"].FirstOrDefault()?.Trim() ?? "";
                    projectpof = form["projectpof"].FirstOrDefault()?.Trim() ?? "";
                    model = form["model"].FirstOrDefault()?.Trim() ?? "excel";
                    user = form["user"].FirstOrDefault()?.Trim() ?? "";
                    string selectedQnumsRaw = form["selected_qnums"].FirstOrDefault()?.Trim() ?? "";

                    if (file != null && file.Length > 0)
                    {
                        // Aspose.Cells 라이선스 적용
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

                        if (maxCol < 0 || maxRow < 1)
                        {
                            return await ReturnError("701", "엑셀 파일에 유효한 데이터가 없습니다.");
                        }

                        // 선택된 문항 필터링 세트 구성
                        HashSet<string>? targetQnums = null;
                        if (!string.IsNullOrEmpty(selectedQnumsRaw))
                        {
                            targetQnums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            try
                            {
                                if (selectedQnumsRaw.TrimStart().StartsWith("["))
                                {
                                    var arr = JArray.Parse(selectedQnumsRaw);
                                    foreach (var item in arr)
                                    {
                                        string s = item.ToString()?.Trim() ?? "";
                                        if (!string.IsNullOrEmpty(s)) targetQnums.Add(s);
                                    }
                                }
                                else
                                {
                                    var parts = selectedQnumsRaw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var p in parts)
                                    {
                                        string s = p.Trim();
                                        if (!string.IsNullOrEmpty(s)) targetQnums.Add(s);
                                    }
                                }
                            }
                            catch { }
                        }

                        // 문항 컬럼 인덱스 매핑 (0행: qTitle, 1행: qVar) - 엑셀 시트 1열부터 maxCol까지 순서 보존
                        var targetColumns = new List<(int ColIndex, string Qnum, string QnumText)>();
                        for (int c = 1; c <= maxCol; c++)
                        {
                            string qTitle = sheet.Cells[0, c].StringValue?.Trim() ?? "";
                            string qVar = sheet.Cells[1, c].StringValue?.Trim() ?? "";
                            if (string.IsNullOrEmpty(qVar) && string.IsNullOrEmpty(qTitle)) continue;

                            string finalVar = !string.IsNullOrEmpty(qVar) ? qVar : qTitle;
                            string finalTitle = !string.IsNullOrEmpty(qTitle) ? qTitle : finalVar;

                            if (targetQnums == null || targetQnums.Count == 0 || targetQnums.Contains(finalVar))
                            {
                                targetColumns.Add((c, finalVar, finalTitle));
                                if (seenQnums.Add(finalVar))
                                {
                                    orderedQuestions.Add((finalVar, finalTitle));
                                }
                            }
                        }

                        // 전체 응답자 행 파싱 (2행부터 maxRow까지)
                        for (int r = 2; r <= maxRow; r++)
                        {
                            string pid = sheet.Cells[r, 0].StringValue?.Trim() ?? "";
                            if (string.IsNullOrEmpty(pid)) continue;

                            foreach (var col in targetColumns)
                            {
                                string ans = sheet.Cells[r, col.ColIndex].StringValue ?? "";
                                if (string.IsNullOrWhiteSpace(ans)) continue; // 비어있는 답변은 제외하여 DB 저장 및 인덱스 최적화

                                if (!dict.TryGetValue(col.Qnum, out var list))
                                {
                                    list = new JArray();
                                    dict[col.Qnum] = list;
                                }

                                list.Add(new JObject
                                {
                                    ["pid"] = pid,
                                    ["qnum"] = col.Qnum,
                                    ["qnum_text"] = col.QnumText,
                                    ["answer"] = ans.Trim()
                                });
                            }
                        }
                    }
                    else if (form.ContainsKey("data"))
                    {
                        string dataJson = form["data"].FirstOrDefault() ?? "";
                        if (!string.IsNullOrEmpty(dataJson))
                        {
                            var arr = JArray.Parse(dataJson);
                            foreach (var item in arr)
                            {
                                string q = item["qnum"]?.ToString()?.Trim() ?? "";
                                string qt = item["qnum_text"]?.ToString()?.Trim() ?? q;
                                if (!string.IsNullOrEmpty(q) && seenQnums.Add(q))
                                {
                                    orderedQuestions.Add((q, qt));
                                }
                            }
                            dict = arr
                                .Where(o => o["qnum"] != null && !string.IsNullOrWhiteSpace(o["answer"]?.ToString()))
                                .GroupBy(o => (string)o["qnum"]!)
                                .ToDictionary(g => g.Key, g => new JArray(g));
                        }
                    }
                }
                else
                {
                    using var reader = new System.IO.StreamReader(Request.Body);
                    var bodyText = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        var body = JToken.Parse(bodyText);
                        JArray? originalArray = null;

                        if (body is JObject obj)
                        {
                            projectnum = obj.Value<string>("projectnum")?.Trim() ?? "";
                            projectname = obj.Value<string>("projectname")?.Trim() ?? "";
                            projectpof = obj.Value<string>("projectpof")?.Trim() ?? "";
                            model = obj.Value<string>("model")?.Trim() ?? "excel";
                            user = obj.Value<string>("user")?.Trim() ?? "";
                            if (obj["data"] is JArray arr) originalArray = arr;
                        }
                        else if (body is JArray arr)
                        {
                            originalArray = arr;
                        }

                        if (originalArray != null)
                        {
                            foreach (var item in originalArray)
                            {
                                string q = item["qnum"]?.ToString()?.Trim() ?? "";
                                string qt = item["qnum_text"]?.ToString()?.Trim() ?? q;
                                if (!string.IsNullOrEmpty(q) && seenQnums.Add(q))
                                {
                                    orderedQuestions.Add((q, qt));
                                }
                            }
                            dict = originalArray
                                .Where(o => o["qnum"] != null && !string.IsNullOrWhiteSpace(o["answer"]?.ToString()))
                                .GroupBy(o => (string)o["qnum"]!)
                                .ToDictionary(g => g.Key, g => new JArray(g));
                        }
                    }
                }

                // Fallbacks for projectnum
                if (string.IsNullOrEmpty(projectnum))
                {
                    projectnum = Request.Query["projectnum"].FirstOrDefault()?.Trim()
                        ?? Request.Query["pn"].FirstOrDefault()?.Trim()
                        ?? "";
                }

                // Fallbacks for user
                if (string.IsNullOrEmpty(user))
                {
                    user = HttpContext.Items["UserId"]?.ToString()?.Trim()
                        ?? Request.Query["user"].FirstOrDefault()?.Trim()
                        ?? Request.Headers["user"].FirstOrDefault()?.Trim()
                        ?? "";
                }

                if (string.IsNullOrEmpty(projectnum) || dict.Count == 0)
                {
                    return await ReturnError("701", "등록할 유효한 엑셀 데이터가 없거나 프로젝트 번호(projectnum)가 누락되었습니다.");
                }

                string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string username = "";
                if (!string.IsNullOrEmpty(user))
                {
                    var userInfo = await _mainRepository.SelectProLoginAsync(user, ct);
                    username = userInfo.Username ?? "";
                }

                // 1. PID 중복 체크
                var duplicateMap = new Dictionary<string, List<string>>();
                foreach (var kv in dict)
                {
                    string qnumKey = kv.Key;
                    JArray rows = kv.Value;

                    var duplicatePids = rows
                        .GroupBy(r => r["pid"]?.ToString()?.Trim() ?? "")
                        .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();

                    if (duplicatePids.Count > 0)
                    {
                        duplicateMap[qnumKey] = duplicatePids;
                    }
                }

                if (duplicateMap.Count > 0)
                {
                    JObject duplicateResult = new JObject(
                        duplicateMap.Select(kv => new JProperty(kv.Key, new JArray(kv.Value)))
                    );
                    return await ReturnError("769", $"PID 중복 데이터가 존재합니다: {duplicateResult.ToString(Newtonsoft.Json.Formatting.None)}");
                }

                // 2. 엑셀 원본 컬럼 순서(orderedQuestions)대로 qnum별 DB 저장 (mapid 순차 부여)
                // 만약 dict에 존재하는 문항 중 orderedQuestions에 빠진 문항이 있다면 끝에 추가
                foreach (var kv in dict)
                {
                    if (seenQnums.Add(kv.Key))
                    {
                        string fallbackText = kv.Value.Count > 0 ? (kv.Value[0]["qnum_text"]?.ToString()?.Trim() ?? kv.Key) : kv.Key;
                        orderedQuestions.Add((kv.Key, fallbackText));
                    }
                }

                int mapid = 0;
                string pattern = @"^(.*?)\.\s*(.*)";

                foreach (var qMeta in orderedQuestions)
                {
                    string qnumKey = qMeta.Qnum;
                    if (!dict.TryGetValue(qnumKey, out var rows) || rows == null || rows.Count == 0)
                    {
                        continue;
                    }

                    mapid++;

                    string question_origin = !string.IsNullOrEmpty(qMeta.QnumText) ? qMeta.QnumText : (rows[0].Value<string>("qnum_text")?.Trim() ?? qnumKey);
                    string qnum_text = qnumKey;
                    string question_fin = question_origin;

                    Match match = Regex.Match(question_origin, pattern);
                    if (match.Success)
                    {
                        qnum_text = match.Groups[1].Value;
                        question_fin = match.Groups[2].Value;
                    }
                    else
                    {
                        question_fin = question_origin;
                    }

                    question_fin = Regex.Replace(question_fin, @"[^\p{L}\p{N}\s\-_?!$%&*\[\]{}]", "");

                    JArray transformed = new JArray();
                    foreach (JObject row in rows.OfType<JObject>())
                    {
                        string pid = row.Value<string>("pid")?.Trim() ?? "";
                        string originRaw = row.Value<string>("answer") ?? "";
                        if (string.IsNullOrWhiteSpace(originRaw)) continue;

                        string origin = new string(
                            originRaw.Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t').ToArray()
                        ).Trim();

                        string fin = Regex.Replace(origin, @"[^\p{L}\p{N}\s.,!?@#$%^&*()\[\]{}\-_=+~`|:;""'<>\\\/]", "").Replace("CanDK", "").ToLower();
                        if (string.IsNullOrEmpty(fin)) fin = "none";

                        JObject newItem = new JObject
                        {
                            ["pid"] = pid,
                            ["answer_fin"] = fin,
                            ["insert_date"] = datetime,
                            ["answer_origin"] = origin
                        };
                        transformed.Add(newItem);
                    }

                    if (transformed.Count > 0)
                    {
                        JObject newAnswerObj = new JObject
                        {
                            [qnumKey] = transformed
                        };
                        string newAnswerJson = newAnswerObj.ToString(Newtonsoft.Json.Formatting.None);
                        string finalAnswerJson = newAnswerJson;

                        // 기존 project_list 존재 여부 확인 후 동일 qnumKey 문항에 대해서만 PID 병합 및 merge_qnum 중복 검사
                        DataTable existingRowDt = await _mainRepository.select_project_list_async(user, 100, "", projectnum, "", "", ct);
                        DataRow? targetRow = existingRowDt.Rows
                            .Cast<DataRow>()
                            .FirstOrDefault(r => string.Equals(r["qnum"]?.ToString()?.Trim(), qnumKey, StringComparison.OrdinalIgnoreCase));

                        if (targetRow != null)
                        {
                            string existingAnswerJson = targetRow["answer_origin"]?.ToString() ?? "";
                            finalAnswerJson = MainRepository.MergeJsonbByPid(existingAnswerJson, newAnswerJson);
                        }

                        // finalAnswerJson이 단일 JArray 구조인 경우 {"qnumKey": [...]} 객체 구조로 최종 래핑 보장
                        try
                        {
                            JToken parsedToken = JToken.Parse(finalAnswerJson);
                            if (parsedToken is JArray arr)
                            {
                                finalAnswerJson = new JObject { [qnumKey] = arr }.ToString(Newtonsoft.Json.Formatting.None);
                            }
                        }
                        catch { }

                        // merge_qnum 중복 방지: 동일 qnumKey 수정 건이 아니면서 이미 다른 문항에서 사용 중인 merge_qnum인 경우 유니크 접미사(_new1) 대입
                        string targetMergeQnum = qnum_text;
                        string? existingMergeQnumInTarget = targetRow != null && targetRow.Table.Columns.Contains("merge_qnum") ? targetRow["merge_qnum"]?.ToString() : null;
                        if (targetRow != null && !string.IsNullOrEmpty(existingMergeQnumInTarget))
                        {
                            targetMergeQnum = existingMergeQnumInTarget.Trim();
                        }
                        else
                        {
                            var existingMergeQnums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (DataRow r in existingRowDt.Rows)
                            {
                                string rQnum = r.Table.Columns.Contains("qnum") ? r["qnum"]?.ToString()?.Trim() ?? "" : "";
                                if (string.Equals(rQnum, qnumKey, StringComparison.OrdinalIgnoreCase)) continue;

                                if (r.Table.Columns.Contains("merge_qnum"))
                                {
                                    string rMergeQnum = r["merge_qnum"]?.ToString()?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(rMergeQnum)) existingMergeQnums.Add(rMergeQnum);
                                }

                                if (r.Table.Columns.Contains("merge_qnum_check"))
                                {
                                    string rMergeQnumCheck = r["merge_qnum_check"]?.ToString()?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(rMergeQnumCheck)) existingMergeQnums.Add(rMergeQnumCheck);
                                }
                            }

                            if (existingMergeQnums.Contains(qnum_text))
                            {
                                int suffixIndex = 1;
                                while (existingMergeQnums.Contains($"{qnum_text}_new{suffixIndex}"))
                                {
                                    suffixIndex++;
                                }
                                targetMergeQnum = $"{qnum_text}_new{suffixIndex}";
                            }
                        }

                        bool inserted = await _mainRepository.ProjectListInsertDbAsync(
                            projectnum: projectnum,
                            projectname: projectname,
                            projectpof: projectpof,
                            model: model, // "excel"
                            qnum: qnumKey,
                            qnum_text: qnum_text,
                            question_fin: question_fin,
                            question_origin: question_origin,
                            user: user,
                            use_name: username,
                            load_fin: "excel",
                            answer: finalAnswerJson,
                            servername: "",
                            mapid: mapid,
                            merge_qnum: targetMergeQnum,
                            ct: ct
                        );

                        if (!inserted)
                        {
                            return await ReturnError("761");
                        }
                    }
                }

                // surveyon_variables가 존재하는 프로젝트인 경우 원본 맵 순서로 mapid 동기화
                try
                {
                    await _mainRepository.SyncProjectListMapIdFromSurveyonVariablesAsync(projectnum, ct);
                }
                catch (Exception exSync)
                {
                    Console.WriteLine($"[ExcelEnter] SyncProjectListMapId warning: {exSync.Message}");
                }

                // NewOldCheckAsync 호출하여 pn_in 및 동기화 테이블 집계 반영
                try
                {
                    await _newOldJsonService.NewOldCheckAsync(projectnum, "", "", ct);
                }
                catch (Exception exNewOld)
                {
                    Console.WriteLine($"[ExcelEnter] NewOldCheck warning: {exNewOld.Message}");
                }

                return await ReturnResponse("777", new JObject { ["message"] = "엑셀 오픈데이터가 성공적으로 등록되었습니다." });
            }
            catch (Exception ex)
            {
                return await ReturnError("761", ex.Message);
            }
        }
    }
}
