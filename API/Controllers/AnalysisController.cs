using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using SurveyonApi.Models;
using SurveyonApi.Repositories;
using SurveyonApi.Services;

namespace SurveyonApi.Controllers
{
    // ══════════════════════════════════════════════════════════════════════
    // AI오픈분석 › 문항목록 › 분석 페이지 API [/analysis/*]
    //  레거시: option_edit_api / option_status_api (.aspx) → ASP.NET Core 이관
    //  보안:
    //   - 전 엔드포인트 AuthCheckFilter 적용 (레거시 option_edit_api는 토큰 검증이 주석 처리되어 있었음)
    //   - 요청 본문의 user가 토큰 사용자와 다르면 710 (타인 ID 사칭 차단)
    //   - 빈 본문 시 하드코딩 테스트 데이터로 실행되던 동작 제거 → 701
    //   - 예외 메시지는 응답에 노출하지 않고 서버 로그에만 기록
    // ══════════════════════════════════════════════════════════════════════
    [ApiController]
    [Route("analysis")]
    public class AnalysisController : BaseController
    {
        private static readonly Regex SafeId = new(@"^[\p{L}\p{N}_\-\.\(\)\s]{1,100}$", RegexOptions.Compiled);

        private readonly IAnalysisService _service;
        private readonly IAnalysisRepository _repo;
        private readonly INewOldJsonService _newOldJsonService;
        private readonly IWorkerService _workerService;
        private readonly ILogger<AnalysisController> _logger;

        public AnalysisController(IMainRepository mainRepository, IAnalysisService service, IAnalysisRepository repo,
            INewOldJsonService newOldJsonService, IWorkerService workerService, ILogger<AnalysisController> logger) : base(mainRepository)
        {
            _service = service;
            _repo = repo;
            _newOldJsonService = newOldJsonService;
            _workerService = workerService;
            _logger = logger;
        }

        // ── 1. 문항 설정 정보 [POST /analysis/info] (레거시 edit gb=info) ──
        [HttpPost("info")]
        public Task<IActionResult> Info([FromBody] AnalysisRequest req)
            => RunQuestionRead(req, "info", async (pn, qnum, user, ct) =>
            {
                if (await _repo.GetUserKeyInfoAsync(user, ct) == null) return null; // 702
                return await _service.BuildInfoAsync(pn, qnum, user, ct);
            });

        // ── 2. 응답자 탭 [POST /analysis/responses] (레거시 edit gb=in) ──
        [HttpPost("responses")]
        public Task<IActionResult> Responses([FromBody] AnalysisRequest req)
            => RunQuestionRead(req, "responses", async (pn, qnum, user, ct) =>
            {
                // 레거시와 동일: 조회 직전 project_list → pn_in 신규 응답 동기화
                int? qid = await _service.ResolveQidAsync(pn, qnum, ct);
                await _newOldJsonService.NewOldCheckAsync(pn, qnum, qid?.ToString() ?? "", ct);
                return await _service.BuildResponsesAsync(pn, qnum, ct);
            });

        // ── 3. 보기 탭 [POST /analysis/codes] (레거시 edit gb=lb) ──
        [HttpPost("codes")]
        public Task<IActionResult> Codes([FromBody] AnalysisRequest req)
            => RunQuestionRead(req, "codes", (pn, qnum, user, ct) => _service.BuildCodesAsync(pn, qnum, ct)!);

        // ── 4. rawdata 탭 [POST /analysis/rawdata] (레거시 edit gb=list) ──
        [HttpPost("rawdata")]
        public Task<IActionResult> Rawdata([FromBody] AnalysisRequest req)
            => RunQuestionRead(req, "rawdata", (pn, qnum, user, ct) => _service.BuildRawdataAsync(pn, qnum, ct)!);

        // ── 5. 분석 팝업 확인 [POST /analysis/popup_check] (레거시 edit gb=popupcheck) ──
        [HttpPost("popup_check")]
        public async Task<IActionResult> PopupCheck([FromBody] AnalysisRequest req)
        {
            var (error, pn, qnum, _) = await ValidateQuestionRequest(req);
            if (error != null) return error;
            var ct = HttpContext.RequestAborted;

            try
            {
                int? qid = await _service.ResolveQidAsync(pn, qnum, ct);
                if (qid == null) return await ReturnError("703");

                string checkyn = req.checkyn?.Trim() ?? "";
                if (checkyn == "1") await _repo.ClearPopupCheckAsync(qid.Value, ct);
                var status = await _repo.GetStatusByQidAsync(qid.Value, ct);
                string popup = status?.popupCheck ?? "";

                var (lvcode, dupDate) = await _service.GetHeaderInfoAsync(pn, qnum, ct);
                // 레거시 호환: lvcode 필드에 "checkyn:.., popupcheckvalue:.." 문자열을 담아 반환했음 → 동일 유지 + resultjson 구조화
                return await AnalysisOk(new JObject { ["checkyn"] = checkyn, ["popupcheckvalue"] = popup },
                    $"checkyn:{checkyn}, popupcheckvalue:{popup}", dupDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/popup_check] 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ── 6. 분석 진행 상태 [POST /analysis/status] (레거시 option_status_api) ──
        //    버그 수정: 레거시는 python_status가 비고 popup_check만 있을 때 응답을 보내지 않았음 → 항상 응답
        [HttpPost("status")]
        public async Task<IActionResult> Status([FromBody] AnalysisRequest req)
        {
            if (req == null || !int.TryParse(req.qid?.Trim(), out int qid)) return await ReturnError("701");
            var userError = await ValidateUser(req);
            if (userError != null) return userError;

            try
            {
                var status = await _repo.GetStatusByQidAsync(qid, HttpContext.RequestAborted);
                var info = await _mainRepository.GetErrorCodeAsync("777", "ko", HttpContext.RequestAborted);
                return Ok(new
                {
                    success = "777",
                    Message = info?.Message,
                    output = status?.pythonStatus ?? "",
                    popupcheck = status?.popupCheck ?? ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/status] 실패 qid={Qid}", qid);
                return await ReturnError("500");
            }
        }

        // ── 7. 설정 저장 [POST /analysis/save/info] ──
        [HttpPost("save/info")]
        public async Task<IActionResult> SaveInfo([FromBody] AnalysisSaveRequest req)
        {
            var (error, pn, qnum, user) = await ValidateQuestionRequest(req);
            if (error != null) return error;

            if (req.data is not JObject infoObj)
            {
                return await ReturnError("701");
            }

            try
            {
                bool ok = await _service.SaveInfoAsync(pn, qnum, user, infoObj, HttpContext.RequestAborted);
                if (!ok) return await ReturnError("500");
                var info = await _mainRepository.GetErrorCodeAsync("777", "ko", HttpContext.RequestAborted);
                return Ok(new { success = "777", message = info?.Message ?? "저장되었습니다." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/save/info] 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ── 8. 보기 저장 [POST /analysis/save/codes] ──
        [HttpPost("save/codes")]
        public async Task<IActionResult> SaveCodes([FromBody] AnalysisSaveRequest req)
        {
            var (error, pn, qnum, user) = await ValidateQuestionRequest(req);
            if (error != null) return error;

            if (req.data is not JArray codesArr)
            {
                return await ReturnError("701");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(req.lvcode))
                {
                    await _repo.UpdateLvcodeAsync(pn, qnum, req.lvcode, HttpContext.RequestAborted);
                }

                var (success, code, message, count) = await _service.SaveCodesAsync(pn, qnum, codesArr, HttpContext.RequestAborted);
                if (!success)
                {
                    return Ok(new { success = code, message, count = 0 });
                }

                var info = await _mainRepository.GetErrorCodeAsync("777", "ko", HttpContext.RequestAborted);
                return Ok(new { success = "777", message = info?.Message ?? "저장되었습니다.", count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/save/codes] 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ── 9. 응답 저장 (중복코드 제거 예외 처리 포함) [POST /analysis/save/responses] ──
        [HttpPost("save/responses")]
        public async Task<IActionResult> SaveResponses([FromBody] AnalysisSaveRequest req)
        {
            var (error, pn, qnum, user) = await ValidateQuestionRequest(req);
            if (error != null) return error;

            if (req.data is not JArray responsesArr)
            {
                return await ReturnError("701");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(req.lvcode))
                {
                    await _repo.UpdateLvcodeAsync(pn, qnum, req.lvcode, HttpContext.RequestAborted);
                }

                var (success, code, message, count) = await _service.SaveResponsesAsync(
                    pn, qnum, req.duplicate_answer ?? "n", responsesArr, HttpContext.RequestAborted);

                if (!success)
                {
                    return Ok(new { success = code, message, count = 0 });
                }

                var info = await _mainRepository.GetErrorCodeAsync("777", "ko", HttpContext.RequestAborted);
                string finalMessage = !string.IsNullOrWhiteSpace(message) ? message : (info?.Message ?? "저장되었습니다.");
                return Ok(new { success = "777", message = finalMessage, count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/save/responses] 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ── 10. DB 반영 비교 [POST /analysis/save/compare] ──
        [HttpPost("save/compare")]
        public async Task<IActionResult> SaveCompare([FromBody] AnalysisSaveRequest req)
        {
            var (error, pn, qnum, _) = await ValidateQuestionRequest(req);
            if (error != null) return error;

            var currentArr = req.data as JArray ?? new JArray();
            try
            {
                var diff = await _service.CompareResponsesAsync(pn, qnum, currentArr, HttpContext.RequestAborted);
                return Ok(new { success = "777", diff });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/save/compare] 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ── 11. 워커 실행 [POST /analysis/worker/start] ──
        [HttpPost("worker/start")]
        public async Task<IActionResult> WorkerStart([FromBody] WorkerStartRequest req)
        {
            _logger.LogInformation("[analysis/worker/start] 요청 수신 pn={Pn} qnum={Qnum} user={User} action={Action}",
                req?.projectnum, req?.qnum, req?.user, req?.action);

            var (error, pn, qnum, user) = await ValidateQuestionRequest(req);
            if (error != null)
            {
                _logger.LogWarning("[analysis/worker/start] ValidateQuestionRequest 실패 pn={Pn} qnum={Qnum} user={User}",
                    req?.projectnum, req?.qnum, req?.user);
                return error;
            }

            try
            {
                int? qid = null;
                if (!string.IsNullOrWhiteSpace(req.qid) && int.TryParse(req.qid, out int parsedQid))
                {
                    qid = parsedQid;
                }
                else
                {
                    qid = await _service.ResolveQidAsync(pn, qnum, HttpContext.RequestAborted);
                }

                if (!qid.HasValue)
                {
                    _logger.LogWarning("[analysis/worker/start] qid 찾을 수 없음 pn={Pn} qnum={Qnum}", pn, qnum);
                    return await ReturnError("703", "해당 문항의 대표 QID를 찾을 수 없습니다.");
                }

                string action = req.action?.Trim() ?? "analyze";

                // 레거시 option_setting.aspx.cs와 동일: 워커 실행 전 pn_in.answer_origin 원본 데이터 최신 동기화 보장
                await _newOldJsonService.NewOldCheckAsync(pn, qnum, qid.Value.ToString(), HttpContext.RequestAborted);

                // 레거시 create_json_option()과 동일: 워커 실행 전 "분석 설정"을 option_json에 자동 저장하여 Python 워커의 KeyError('data_type') 방지
                await _service.EnsureOptionJsonSavedAsync(pn, qnum, user, req.settings, action, HttpContext.RequestAborted);

                var flags = new WorkerCustomFlags
                {
                    Classified = action == "analyze" ? (!string.IsNullOrWhiteSpace(req.classified) ? req.classified.Trim() : "Y") : req.classified?.Trim(),
                    OpencodeCategory = action == "analyze" ? (!string.IsNullOrWhiteSpace(req.opencodeCategory) ? req.opencodeCategory.Trim() : "Y") : req.opencodeCategory?.Trim(),
                    Response = action == "respondent" ? (!string.IsNullOrWhiteSpace(req.response) ? req.response.Trim() : "Y") : req.response?.Trim(),
                    OpencodeResponse = action == "respondent" ? (!string.IsNullOrWhiteSpace(req.opencodeResponse) ? req.opencodeResponse.Trim() : "Y") : req.opencodeResponse?.Trim(),
                    RecallResponse = action == "empty_etc" ? "Y" : (action == "respondent" ? (!string.IsNullOrWhiteSpace(req.recallResponse) ? req.recallResponse.Trim() : "Y") : req.recallResponse?.Trim()),
                    TranslateResponse = action == "translate" ? (!string.IsNullOrWhiteSpace(req.translateResponse) ? req.translateResponse.Trim() : "Y") : req.translateResponse?.Trim()
                };

                string jobId = await _workerService.StartJobAsync(pn, qnum, qid.Value, action, flags, HttpContext.RequestAborted);

                return Ok(new { ok = true, success = "777", jobId, action });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/worker/start] 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ── 12. 워커 상태/로그 폴링 [GET /analysis/worker/status] ──
        [HttpGet("worker/status")]
        public IActionResult WorkerStatus([FromQuery] string job, [FromQuery] int offset = 0)
        {
            if (string.IsNullOrWhiteSpace(job)) return BadRequest(new { ok = false, error = "job required" });

            var status = _workerService.GetStatus(job, offset);
            return Ok(new
            {
                ok = true,
                job,
                exists = status.Exists,
                isCompleted = status.IsCompleted,
                hasError = status.HasError,
                exitCode = status.ExitCode,
                lastLine = status.LastLine,
                output = status.Output,
                nextOffset = status.NextOffset
            });
        }

        // ── 13. 워커 중지 [POST /analysis/worker/stop] ──
        [HttpPost("worker/stop")]
        public IActionResult WorkerStop([FromBody] WorkerStartRequest req)
        {
            string job = req.jobId ?? req.qid ?? req.action ?? "";
            bool stopped = _workerService.StopJob(job);
            return Ok(new { ok = true, stopped, job });
        }

        // ══════════════════════════════════════════════════════════════════
        // Phase D: 엑셀 다운로드 / 업로드 미리보기 / 보기 불러오기 엔드포인트
        // ══════════════════════════════════════════════════════════════════

        // ── 14. 엑셀 내보내기 [GET/POST /analysis/export/excel] ──
        [HttpGet("export/excel")]
        [HttpPost("export/excel")]
        public async Task<IActionResult> ExportExcel([FromQuery] string? projectnum, [FromQuery] string? qnum, [FromQuery] string? user)
        {
            string pn = (projectnum ?? "").Trim();
            string qn = (qnum ?? "").Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(qn))
                return await ReturnError("701");

            try
            {
                byte[] bytes = await _service.ExportExcelAsync(pn, qn, HttpContext.RequestAborted);
                string fileName = $"hrcopen_분석_{pn}_{qn}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/export/excel] 실패 pn={Pn} qnum={Qnum}", pn, qn);
                return await ReturnError("500");
            }
        }

        // ── 15. 엑셀 업로드 미리보기 [POST /analysis/import/excel-preview] ──
        [HttpPost("import/excel-preview")]
        public async Task<IActionResult> PreviewExcel([FromForm] IFormFile? file, [FromForm] string? projectnum, [FromForm] string? qnum)
        {
            string pn = (projectnum ?? "").Trim();
            string qn = (qnum ?? "").Trim();
            if (file == null || file.Length == 0 || string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(qn))
                return await ReturnError("701", "엑셀 파일 및 프로젝트/문항 정보가 필요합니다.");

            try
            {
                var preview = await _service.PreviewUploadedExcelCodesAsync(file, pn, qn, HttpContext.RequestAborted);
                return Ok(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/import/excel-preview] 파싱 실패 pn={Pn} qnum={Qnum}", pn, qn);
                return await ReturnError("500", "엑셀 파싱 중 오류가 발생했습니다.");
            }
        }

        // ── 16. 타 프로젝트/문항 검색 [GET /analysis/import/projects] ──
        [HttpGet("import/projects")]
        public async Task<IActionResult> SearchProjects([FromQuery] string? search)
        {
            try
            {
                var list = await _service.SearchImportProjectsAsync(search ?? "", HttpContext.RequestAborted);
                return Ok(new { ok = true, list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/import/projects] 검색 실패 search={Search}", search);
                return await ReturnError("500");
            }
        }

        // ── 17. 타 문항 보기 목록 조회 [GET /analysis/import/codes] ──
        [HttpGet("import/codes")]
        public async Task<IActionResult> GetImportCodes([FromQuery] string? target_projectnum, [FromQuery] string? target_qnum)
        {
            string targetPn = (target_projectnum ?? "").Trim();
            string targetQn = (target_qnum ?? "").Trim();
            if (string.IsNullOrEmpty(targetPn) || string.IsNullOrEmpty(targetQn))
                return await ReturnError("701");

            try
            {
                var codes = await _service.GetImportCodesAsync(targetPn, targetQn, HttpContext.RequestAborted);
                return Ok(new { ok = true, codes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/import/codes] 조회 실패 targetPn={TargetPn} targetQn={TargetQn}", targetPn, targetQn);
                return await ReturnError("500");
            }
        }

        // ── 18. 보기 가져오기 반영 [POST /analysis/import/apply] ──
        [HttpPost("import/apply")]
        public async Task<IActionResult> ApplyImportCodes([FromBody] AnalysisSaveRequest req)
        {
            var (error, pn, qnum, user) = await ValidateQuestionRequest(req);
            if (error != null) return error;

            if (req.data is not JArray arr || arr.Count == 0)
                return await ReturnError("701", "가져올 보기 데이터가 없습니다.");

            try
            {
                var (ok, added, dup) = await _service.ApplyImportCodesAsync(pn, qnum, arr, HttpContext.RequestAborted);
                return Ok(new { ok, addedCount = added, duplicateCount = dup });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/import/apply] 반영 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                return await ReturnError("500");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 공통 처리
        // ══════════════════════════════════════════════════════════════════

        /// <summary>문항 단위 조회 공통: 검증 → 실행(시간 측정) → 레거시 형식 응답(lvcode, duplicateRemoveDate 포함)</summary>
        private async Task<IActionResult> RunQuestionRead(AnalysisRequest req, string name,
            Func<string, string, string, CancellationToken, Task<JArray?>> work)
        {
            var (error, pn, qnum, user) = await ValidateQuestionRequest(req);
            if (error != null) return error;
            var ct = HttpContext.RequestAborted;
            var sw = Stopwatch.StartNew();

            try
            {
                var headerTask = _service.GetHeaderInfoAsync(pn, qnum, ct);
                var data = await work(pn, qnum, user, ct);
                if (data == null) return await ReturnError("702");
                var (lvcode, dupDate) = await headerTask;

                _logger.LogInformation("[analysis/{Name}] pn={Pn} qnum={Qnum} rows={Rows} {Ms}ms", name, pn, qnum, data.Count, sw.ElapsedMilliseconds);
                return await AnalysisOk(data, lvcode, dupDate);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[analysis/{Name}] 실패 pn={Pn} qnum={Qnum}", name, pn, qnum);
                return await ReturnError("500");
            }
        }

        /// <summary>레거시 option_edit_api 성공 응답 형식: success, Message, lvcode, duplicateRemoveDate, resultjson</summary>
        private async Task<IActionResult> AnalysisOk(JToken data, string lvcode, string duplicateRemoveDate)
        {
            var info = await _mainRepository.GetErrorCodeAsync("777", "ko", HttpContext.RequestAborted);
            var body = new JObject
            {
                ["success"] = info?.success ?? "777",
                ["message"] = info?.Message,
                ["lvcode"] = lvcode,
                ["duplicateRemoveDate"] = duplicateRemoveDate,
                ["resultjson"] = data
            };
            return Content(body.ToString(Newtonsoft.Json.Formatting.None), "application/json; charset=utf-8");
        }

        private async Task<(IActionResult? error, string pn, string qnum, string user)> ValidateQuestionRequest(AnalysisRequest req)
        {
            if (req == null)
            {
                _logger.LogWarning("[ValidateQuestionRequest] req is null");
                return (await ReturnError("701"), "", "", "");
            }
            string pn = req.projectnum?.Trim() ?? "";
            string qnum = req.qnum?.Trim() ?? "";
            if (pn == "" || qnum == "" || !SafeId.IsMatch(pn) || qnum.Length > 200)
            {
                _logger.LogWarning("[ValidateQuestionRequest] pn/qnum invalid: pn='{Pn}', qnum='{Qnum}', match={Match}", pn, qnum, SafeId.IsMatch(pn));
                return (await ReturnError("701"), "", "", "");
            }

            var userError = await ValidateUser(req);
            if (userError != null) return (userError, "", "", "");
            return (null, pn, qnum, EffectiveUser(req));
        }

        /// <summary>요청 user와 토큰 사용자 일치 확인 (내부키 호출은 본문 user 사용)</summary>
        private async Task<IActionResult?> ValidateUser(AnalysisRequest req)
        {
            string tokenUser = HttpContext.Items["UserId"]?.ToString() ?? "";
            string bodyUser = req.user?.Trim() ?? "";
            _logger.LogInformation("[ValidateUser] tokenUser='{TokenUser}', bodyUser='{BodyUser}'", tokenUser, bodyUser);

            if (tokenUser == "" && bodyUser == "")
            {
                _logger.LogWarning("[ValidateUser] Both tokenUser and bodyUser are empty!");
                return await ReturnError("701");
            }
            if (tokenUser != "" && tokenUser != "SYSTEM")
            {
                // 인증 토큰에 의해 식별된 사용자를 우선하여 본문 user와 동기화
                req.user = tokenUser;
            }
            return null;
        }

        private string EffectiveUser(AnalysisRequest req)
        {
            string tokenUser = HttpContext.Items["UserId"]?.ToString() ?? "";
            return (tokenUser != "" && tokenUser != "SYSTEM") ? tokenUser : (req.user?.Trim() ?? "");
        }
    }
}
