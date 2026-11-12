using Microsoft.AspNetCore.Mvc;
using SurveyonApi.Repositories;
using SurveyonApi.Models;
using Newtonsoft.Json.Linq;
using System.Data;
using SurveyonApi.Services;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Hosting.Server;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Threading;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace SurveyonApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Route("o/[controller]")]
    [Route("o/project")]
    public class ProjectController : BaseController
    {
        private readonly JsonHong _jsonHong;
        private readonly ILogger<ProjectController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly IProjectProgressService _progressService;
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly SemaphoreSlim _newProjectLock = new SemaphoreSlim(1, 1);

        public ProjectController(
            IMainRepository mainRepository,
            JsonHong jsonHong,
            ILogger<ProjectController> logger,
            IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IProjectProgressService progressService,
            IServiceScopeFactory scopeFactory) : base(mainRepository)
        {
            _jsonHong = jsonHong;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _progressService = progressService;
            _scopeFactory = scopeFactory;
        }

        [HttpPost]
        public async Task<IActionResult> ProjectAction(
            [FromBody] jobject_arr req,
            [FromHeader(Name = "X-Auth-Token")] string? TokenKey
            )
        //[FromBody] JObject requestBody)
        {
            //string expectedKey = _configuration["InternalAuth:InternalKey"];
            //if (!string.IsNullOrEmpty(expectedKey) && "" != expectedKey)
            //{
            //    _logger.LogWarning("Invalid Internal Key attempt.");
            //    return await ReturnError("701");
            //}

            string gb = req.gb ?? "";

            if (string.IsNullOrEmpty(gb))
            {
                return await ReturnError("703");
            }

            try
            {
                // 큐마DB 목록 불러오기
                if (gb == "qmlist")
                {
                    string user = req.user ?? "";
                    string search = req.search ?? "";
                    string gubun = req.gubun ?? "1";

                    // 1. 사용자 이름 가져오기
                    string username = await _mainRepository.GetUserNameAsync(user, HttpContext.RequestAborted);

                    //mssql -> 프로젝트 목록 불러오기
                    JArray projectList = await check_ms_project(username, search,  "3");
                    
                    string json = projectList.ToString(Formatting.None);
                    return await ReturnResponse("777", json);
                }
                else if (gb == "qmlistselect")
                {
                    string projectnum = req.projectnum ?? "";
                    string pof = req.pof ?? "";
                    string proname = req.questionnaireName ?? "";
                    string server = req.servername ?? "";
                    string user = req.user ?? "";

                    DataTable select_map = await _mainRepository.RpsQmSelectDataMapAsync(projectnum, server, HttpContext.RequestAborted);
                    // DataTable select_map = new DataTable(); // Placeholder
                    DataTable select_answer = new DataTable();

                    // 설문온업로드 새로운 데이터만 셀렉 및 큐마openyn = 날짜 업데이트
                    if (server == "rps" || server == "qm")
                    {
                        select_answer = await _mainRepository.GetTAnswerCountAsync(projectnum, "4", "0","", HttpContext.RequestAborted);

                        // 응답데이터와 맵정보가 있을 경우 자동 불러오기
                        if (select_answer.Rows.Count > 0 )
                        {
                            await Auto_project_enter(projectnum, server, pof, proname, user, "관리자(관리,읽기,쓰기)", "2"); 
                            //HData 데이터관리 최초등록
                            try
                            {
                                string effectiveToken = ExtractEffectiveToken(TokenKey);
                                string result = await CallExternalApi(projectnum, user, effectiveToken);
                                await CallExternalApi_srt(projectnum, user, effectiveToken);
                                if (result == "OK")
                                {
                                    //AI오픈 데이터 insert :  answer데이터 insert, list목록 insert
                                    await InsertOpenData(projectnum, server, pof, proname, user);
                                }

                            }
                            catch (Exception ex)
                            {
                                //api 실행 오류
                                return await ReturnResponse("761");
                            }
                            // 성공 응답
                            return await ReturnResponse("777", new { message = "정상 접속", contents = "성공" });
                        }
                        else
                        {
                            await Auto_project_enter(projectnum, server, pof, proname, user, "관리자(관리,읽기,쓰기)", "2"); 
                            //HData 데이터관리 최초등록
                            try
                            {
                                string effectiveToken = ExtractEffectiveToken(TokenKey);
                                string result = await CallExternalApi(projectnum, user, effectiveToken);
                                return await ReturnResponse("777", new { message = "정상 접속", contents = "성공" });
                            }
                            catch (Exception ex)
                            {
                                //api 실행 오류
                                return await ReturnResponse("761");
                            }  
                            // return await ReturnResponse("767", new { message = "맵 정보가 등록되지 않았습니다. (담당웹제작자에게 문의해주세요)", contents = "성공" });
                        }
                    }
                    return await ReturnError("761", "Invalid server or no data");
                }
                else if (gb == "NewProject")
                {
                    string pof = req.pof ?? "";
                    string proname = req.projectname ?? "";
                    string user = req.user ?? "";
                    string mid = "";
                    string last = "";

                    if (string.IsNullOrEmpty(pof) || string.IsNullOrEmpty(proname))
                    {
                        return await ReturnError("701");
                    }
                    else
                    {
                        if (Regex.IsMatch(pof, @"^\d{4}-\d{2}-\d{4}$"))
                        {
                            // 포맷이 "YYYY-MM-NNNN"일 경우
                            string[] parts = pof.Split('-');
                            mid = parts[1];
                            last = parts[2];
                        }
                        else
                        {
                            // 다른 포맷일 경우 문자열의 끝 4자리 사용
                            mid = "";
                            last = pof.Length >= 4 ? pof.Substring(pof.Length - 4) : pof;
                        }

                        string prefix = "n" + mid + last;
                        string projectnum = "";

                        await _newProjectLock.WaitAsync(HttpContext.RequestAborted);
                        try
                        {
                            int next = 1;
                            while (true)
                            {
                                projectnum = prefix + "_" + next.ToString("D3");

                                DataTable dt = await _mainRepository.select_project_enter_async(projectnum, 2, ct: HttpContext.RequestAborted);

                                // 없으면 이 번호 사용
                                if (dt == null || dt.Rows.Count == 0)
                                {
                                    // projectnum 확정
                                    break;
                                }

                                // 있으면 다음 번호로
                                next++;
                            }
                            var userInfo = await _mainRepository.SelectProLoginAsync(user, ct: HttpContext.RequestAborted);
                            string username = userInfo.Username;
                            string groupposition = userInfo.GroupPosition;

                            await _mainRepository.InsertProjectEnterAsync(projectnum, pof, proname, user, "NEW", HttpContext.RequestAborted);
                            await _mainRepository.InsertProjectPermissionAsync(projectnum, pof, proname, user, username, groupposition,  "관리자(관리,읽기,쓰기)", HttpContext.RequestAborted);
                            // HData에 insert 기본 셋팅 프로젝트생성, 기본맵 pid만 insert되도록
                            await _mainRepository.Insert_surveyon_projectsAsync(projectnum, pof, proname, user, "SPSS", "SAV입력", HttpContext.RequestAborted);
                            await _mainRepository.Insert_surveyon_variablesAsync(projectnum, HttpContext.RequestAborted);

                        }
                        finally
                        {
                            _newProjectLock.Release();
                        }
                        
                        // 성공 응답
                        return await ReturnResponse("777", new { projectnum = projectnum });
                    }
                }
                else if (gb == "list")
                {
                    string user = req.user ?? "";
                    string search = req.search ?? "";
                    string gubun = "1";

                    var userInfo = await _mainRepository.SelectProLoginAsync(user, HttpContext.RequestAborted);
                    string groupposition = userInfo.GroupPosition;

                    string sqltext = $"select projectnum from public.pro_permission where (userid='{user}' or userid='all') ";
                    if (groupposition != "일반" && groupposition != "고객")
                    {
                        sqltext = $"select projectnum from public.pro_permission where (userid='{user}' or userid='all' or userid='hrc') ";
                    }

                    // 1. PostgreSQL 프로젝트 목록 0.03초(30ms) 즉시 반환
                    object listinfo = await _jsonHong.SelectProListInfoAsync(user, sqltext, HttpContext.RequestAborted);

                    // 2. 원격 MSSQL 2026 수집 및 자동 등록은 비동기 백그라운드 Task로 분리 (HTTP 응답 블로킹 제로)
                    string tokenKeyCopy = ExtractEffectiveToken(TokenKey);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var mainRepo = scope.ServiceProvider.GetRequiredService<IMainRepository>();
                            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                            var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProjectController>>();

                            string username = await mainRepo.GetUserNameAsync(user, CancellationToken.None);
                            JArray projectList = await check_ms_project(username, search, gubun, "2026", repoOverride: mainRepo, ct: CancellationToken.None);
                            if (projectList.Count > 0)
                            {
                                var allPns = projectList.Select(item => item["projectnum"]?.ToString() ?? "")
                                                        .Where(p => !string.IsNullOrEmpty(p))
                                                        .Distinct()
                                                        .ToList();

                                var surveyonProjectsTask = mainRepo.GetBulkSurveyonProjectPnsAsync(allPns, CancellationToken.None);
                                var hsrtPagesTask = mainRepo.GetBulkHsrtPagePnsAsync(user, allPns, CancellationToken.None);
                                var existingPermsTask = mainRepo.GetBulkExistingPermissionsAsync(allPns, CancellationToken.None);

                                await Task.WhenAll(surveyonProjectsTask, hsrtPagesTask, existingPermsTask);

                                var existingSurveyonProjects = await surveyonProjectsTask;
                                var existingHsrtPages = await hsrtPagesTask;
                                var existingPerms = await existingPermsTask;

                                var hproCache = new System.Collections.Concurrent.ConcurrentDictionary<(string user, string gubun), DataTable>();

                                foreach (JObject item in projectList)
                                {
                                    string projectnum = item["projectnum"]?.ToString() ?? "";
                                    string server = item["servername"]?.ToString() ?? "";
                                    string pof = item["pof"]?.ToString() ?? "";
                                    string adminuser = item["user아이디"]?.ToString() ?? "";
                                    string proname = item["questionnaireName"]?.ToString() ?? "";
                                    string questionnairePersonName = item["questionnairePersonName"]?.ToString() ?? "";
                                    string researcherName = item["researcherName"]?.ToString() ?? "";
                                    string fieldWorkerName = item["fieldWorkerName"]?.ToString() ?? "";

                                    if (!string.IsNullOrWhiteSpace(adminuser))
                                    {
                                        foreach (string single in adminuser.Split(',', StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "관리자(관리,읽기,쓰기)", "2", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                        }
                                    }
                                    if (!string.IsNullOrWhiteSpace(questionnairePersonName))
                                    {
                                        foreach (string single in questionnairePersonName.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "관리자(관리,읽기,쓰기)", "1", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                        }
                                    }
                                    if (!string.IsNullOrWhiteSpace(researcherName))
                                    {
                                        foreach (string single in researcherName.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "연구원(읽기,쓰기)", "1", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                        }
                                    }
                                    if (!string.IsNullOrWhiteSpace(fieldWorkerName))
                                    {
                                        foreach (string single in fieldWorkerName.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "실사(관리,읽기,쓰기)", "1", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                        }
                                    }

                                    try
                                    {
                                        if (!existingSurveyonProjects.Contains(projectnum))
                                        {
                                            string result = await CallExternalApi(projectnum, user, tokenKeyCopy, httpOverride: httpClientFactory, configOverride: config, repoOverride: mainRepo);
                                            if (result == "OK")
                                            {
                                                await InsertOpenData(projectnum, server, pof, proname, user, repoOverride: mainRepo, ct: CancellationToken.None);
                                            }
                                        }
                                        if (!existingHsrtPages.Contains(projectnum))
                                        {
                                            await CallExternalApi_srt(projectnum, user, tokenKeyCopy, httpOverride: httpClientFactory, configOverride: config, repoOverride: mainRepo);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogError(ex, $"[ProjectController] Background sync error for project {projectnum}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[ProjectController] Background 2026 sync error");
                        }
                    });

                    return await ReturnResponse("777", listinfo);
                }
                else if (gb == "list_start")
                {
                    string user = req.user ?? "";
                    string search = req.search ?? "";
                    bool forceNew = req.forceNew ?? false;

                    if (string.IsNullOrWhiteSpace(user))
                    {
                        return await ReturnError("701", "user 파라미터가 필요합니다.");
                    }

                    var progressState = _progressService.CreateOrGetJob(user, out string jobId, forceNew);

                    var userInfo = await _mainRepository.SelectProLoginAsync(user, HttpContext.RequestAborted);
                    string groupposition = userInfo.GroupPosition;

                    string sqltext = $"select projectnum from public.pro_permission where (userid='{user}' or userid='all') ";
                    if (groupposition != "일반" && groupposition != "고객")
                    {
                        sqltext = $"select projectnum from public.pro_permission where (userid='{user}' or userid='all' or userid='hrc') ";
                    }

                    if (progressState.Status == "completed" && !forceNew)
                    {
                        object? resultJson = progressState.ResultJson;
                        if (resultJson == null)
                        {
                            resultJson = await _jsonHong.SelectProListInfoAsync(user, sqltext, HttpContext.RequestAborted);
                            _progressService.CompleteJob(jobId, resultJson);
                        }

                        return await ReturnResponse("777", new
                        {
                            jobId = jobId,
                            userId = user,
                            status = "completed",
                            check_progress_yn = 0,
                            percent = 100,
                            message = "프로젝트 목록 조회가 완료되었습니다.",
                            resultjson = resultJson
                        });
                    }

                    if (progressState.Status == "processing" && progressState.Percent > 0 && !forceNew)
                    {
                        return await ReturnResponse("777", new
                        {
                            jobId = jobId,
                            userId = user,
                            status = "processing",
                            check_progress_yn = 1,
                            percent = progressState.Percent,
                            message = progressState.Message
                        });
                    }

                    // MSSQL 2026 프로젝트 수집 및 PostgreSQL 사전 검사 (check_progress_yn 사전 판별)
                    string username = await _mainRepository.GetUserNameAsync(user, HttpContext.RequestAborted);
                    JArray projectList = await check_ms_project(username, search, "1", "2026", repoOverride: null, ct: HttpContext.RequestAborted);
                    int totalCount = projectList.Count;

                    int check_progress_yn = 0;
                    List<string> allPns = new List<string>();
                    HashSet<string> existingSurveyonProjects = new HashSet<string>();
                    HashSet<string> existingHsrtPages = new HashSet<string>();
                    HashSet<string> existingPerms = new HashSet<string>();

                    if (totalCount > 0)
                    {
                        allPns = projectList.Select(item => item["projectnum"]?.ToString() ?? "")
                                            .Where(p => !string.IsNullOrEmpty(p))
                                            .Distinct()
                                            .ToList();

                        var surveyonProjectsTask = _mainRepository.GetBulkSurveyonProjectPnsAsync(allPns, HttpContext.RequestAborted);
                        var hsrtPagesTask = _mainRepository.GetBulkHsrtPagePnsAsync(user, allPns, HttpContext.RequestAborted);
                        var existingPermsTask = _mainRepository.GetBulkExistingPermissionsAsync(allPns, HttpContext.RequestAborted);

                        await Task.WhenAll(surveyonProjectsTask, hsrtPagesTask, existingPermsTask);

                        existingSurveyonProjects = await surveyonProjectsTask;
                        existingHsrtPages = await hsrtPagesTask;
                        existingPerms = await existingPermsTask;

                        // 미등록 HData 또는 H-SRT 대시보드 존재 여부 확인
                        bool needsExternalSync = allPns.Any(pn => !existingSurveyonProjects.Contains(pn) || !existingHsrtPages.Contains(pn));
                        if (needsExternalSync)
                        {
                            check_progress_yn = 1;
                        }
                    }

                    // check_progress_yn = 0 인 경우: 동기화 이벤트 대상이 없으므로 즉시 결과 반환 (프로그래스바 미구동)
                    if (check_progress_yn == 0)
                    {
                        object listinfo = await _jsonHong.SelectProListInfoAsync(user, sqltext, HttpContext.RequestAborted);
                        _progressService.CompleteJob(jobId, listinfo);

                        return await ReturnResponse("777", new
                        {
                            jobId = jobId,
                            userId = user,
                            status = "completed",
                            check_progress_yn = 0,
                            percent = 100,
                            message = "프로젝트 목록 조회가 즉시 완료되었습니다.",
                            resultjson = listinfo
                        });
                    }

                    // check_progress_yn = 1 인 경우: 백그라운드 Task 구동하여 프로그래스바 수집 진행
                    string tokenKeyCopy = ExtractEffectiveToken(TokenKey);
                    _ = Task.Run(async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var mainRepo = scope.ServiceProvider.GetRequiredService<IMainRepository>();
                        var jsonHong = scope.ServiceProvider.GetRequiredService<JsonHong>();
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProjectController>>();
                        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                        var progressSvc = scope.ServiceProvider.GetRequiredService<IProjectProgressService>();

                        try
                        {
                            var hproCache = new System.Collections.Concurrent.ConcurrentDictionary<(string user, string gubun), DataTable>();
                            for (int i = 0; i < totalCount; i++)
                            {
                                JObject item = (JObject)projectList[i];
                                string projectnum = item["projectnum"]?.ToString() ?? "";
                                string server = item["servername"]?.ToString() ?? "";
                                string pof = item["pof"]?.ToString() ?? "";
                                string adminuser = item["user아이디"]?.ToString() ?? "";
                                string proname = item["questionnaireName"]?.ToString() ?? "";
                                string questionnairePersonName = item["questionnairePersonName"]?.ToString() ?? "";
                                string researcherName = item["researcherName"]?.ToString() ?? "";
                                string fieldWorkerName = item["fieldWorkerName"]?.ToString() ?? "";

                                int currentPercent = 10 + (int)((i + 1) / (double)totalCount * 80);
                                progressSvc.UpdateProgress(jobId, currentPercent, $"[{i + 1}/{totalCount}] {proname}({projectnum}) 동기화 처리 중...", i + 1, totalCount);

                                // 메인담당자
                                if (!string.IsNullOrWhiteSpace(adminuser))
                                {
                                    string[] single_arr = adminuser.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                    foreach (string single in single_arr)
                                    {
                                        await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "관리자(관리,읽기,쓰기)", "2", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                    }
                                }
                                // 제작자
                                if (!string.IsNullOrWhiteSpace(questionnairePersonName))
                                {
                                    string[] single_arr = questionnairePersonName.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (string single in single_arr)
                                    {
                                        await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "관리자(관리,읽기,쓰기)", "1", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                    }
                                }
                                // 연구원
                                if (!string.IsNullOrWhiteSpace(researcherName))
                                {
                                    string[] single_arr = researcherName.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (string single in single_arr)
                                    {
                                        await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "연구원(읽기,쓰기)", "1", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                    }
                                }
                                // 실사원
                                if (!string.IsNullOrWhiteSpace(fieldWorkerName))
                                {
                                    string[] single_arr = fieldWorkerName.Split(new char[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (string single in single_arr)
                                    {
                                        await Auto_project_enter(projectnum, server, pof, proname, single.Trim(), "실사(관리,읽기,쓰기)", "1", repoOverride: mainRepo, ct: CancellationToken.None, hproCache: hproCache, existingPerms: existingPerms);
                                    }
                                }

                                // HData 데이터관리 최초등록
                                try
                                {
                                    if (!existingSurveyonProjects.Contains(projectnum))
                                    {
                                        string result = await CallExternalApi(projectnum, user, tokenKeyCopy, httpOverride: httpClientFactory, configOverride: config, repoOverride: mainRepo);
                                        progressSvc.UpdateProgress(jobId, 92, "맵구성", totalCount, totalCount);
                                        if (result == "OK")
                                        {
                                            progressSvc.UpdateProgress(jobId, 92, "AI오픈 자동 등록", totalCount, totalCount);
                                            await InsertOpenData(projectnum, server, pof, proname, user, repoOverride: mainRepo, ct: CancellationToken.None);
                                        }
                                        else
                                        {
                                            progressSvc.UpdateProgress(jobId, 92, "Data 맵 등록실패:"+ result, totalCount, totalCount);
                                        }
                                    }
                                    else
                                    {
                                        // H-SRT 대시보드 최초 등록
                                        if (!existingHsrtPages.Contains(projectnum))
                                        {
                                            await CallExternalApi_srt(projectnum, user, tokenKeyCopy, httpOverride: httpClientFactory, configOverride: config, repoOverride: mainRepo);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, $"[ProjectController] Background sync error for project {projectnum}");
                                }
                            }

                            progressSvc.UpdateProgress(jobId, 92, "최종 프로젝트 목록 데이터 구성 중...", totalCount, totalCount);

                            object listinfo = await jsonHong.SelectProListInfoAsync(user, sqltext, CancellationToken.None);
                            progressSvc.CompleteJob(jobId, listinfo);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[ProjectController] Background sync job failed");
                            progressSvc.FailJob(jobId, ex.Message);
                        }
                    });

                    return await ReturnResponse("777", new
                    {
                        jobId = jobId,
                        userId = user,
                        status = "processing",
                        check_progress_yn = 1,
                        percent = 0,
                        message = "프로젝트 목록 동기화 작업을 시작했습니다."
                    });
                }
                else if (gb == "list_progress")
                {
                    string jobid = req.jobid ?? "";
                    string user = req.user ?? "";

                    var progressState = _progressService.GetProgress(jobid, user);
                    if (progressState == null)
                    {
                        return await ReturnError("703", "진행 중인 동기화 작업을 찾을 수 없습니다.");
                    }

                    return await ReturnResponse("777", new
                    {
                        jobId = progressState.JobId,
                        userId = progressState.UserId,
                        status = progressState.Status,
                        check_progress_yn = progressState.Status == "completed" ? 0 : 1,
                        percent = progressState.Percent,
                        currentCount = progressState.CurrentCount,
                        totalCount = progressState.TotalCount,
                        message = progressState.Message,
                        resultjson = progressState.ResultJson,
                        errorMessage = progressState.ErrorMessage
                    });
                }
                else if (gb == "top_toggle" || gb == "set_top")
                {
                    string user = req.user ?? "";
                    string projectnum = req.projectnum ?? "";
                    string? targetTopYn = req.topyn;

                    if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(projectnum))
                    {
                        return await ReturnError("701", "user 및 projectnum 파라미터가 필요합니다.");
                    }

                    var (success, newTopYn) = await _mainRepository.UpdateProjectTopYnAsync(projectnum, user, targetTopYn, HttpContext.RequestAborted);
                    if (!success)
                    {
                        return await ReturnError("761", "상단 고정 상태 변경에 실패했습니다.");
                    }

                    // 캐시 무효화 -> 다음 list_start 조회 시 즉시 재정렬된 최신 목록 반영
                    _progressService.ClearJob(user);

                    return await ReturnResponse("777", new
                    {
                        projectnum = projectnum,
                        userId = user,
                        topyn = newTopYn,
                        message = newTopYn == "y" ? "프로젝트가 상단에 고정되었습니다." : "프로젝트 상단 고정이 해제되었습니다."
                    });
                }
                else
                {
                    return await ReturnError("703", "지원하지 않는 gb 파라미터입니다.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API Error");
                return await ReturnError("761");
            }
        }
        private async Task<JArray> check_ms_project(string username, string search, string gubun, string year = "", IMainRepository? repoOverride = null, CancellationToken ct = default)
        {
            var mainRepo = repoOverride ?? _mainRepository;
            var token = repoOverride != null ? ct : (HttpContext?.RequestAborted ?? CancellationToken.None);

            // RPS (SqlServer) 및 QM (SqlServerqm) 리스트 동시 가져오기
            var rpsTask = mainRepo.SelectProjectListSurveyToolAsync(username, search, gubun, "SqlServer", year, token);
            var qmTask = mainRepo.SelectProjectListSurveyToolAsync(username, search, gubun, "SqlServerqm", year, token);

            await Task.WhenAll(rpsTask, qmTask);

            DataTable? dtRps = await rpsTask;
            DataTable? dtQm = await qmTask;

            // null이면 빈 테이블로 대체
            dtRps ??= new DataTable();
            dtQm ??= new DataTable();

            // rps 리스트에 "server" 컬럼 추가
            if (!dtRps.Columns.Contains("server"))
                dtRps.Columns.Add("server", typeof(string));

            foreach (DataRow row in dtRps.Rows)
            {
                row["server"] = "rps";
            }

            // qm 리스트에 "server" 컬럼 추가
            if (!dtQm.Columns.Contains("server"))
                dtQm.Columns.Add("server", typeof(string));

            foreach (DataRow row in dtQm.Rows)
            {
                row["server"] = "qm";
            }

            // 3. 머지 및 정렬
            var mergedList = dtRps.AsEnumerable()
                .Concat(dtQm.AsEnumerable())
                .OrderByDescending(row => row["createDateTime"])
                .ToList();
            var resultList = new JArray();

            if (mergedList.Count == 0)
            {
                return resultList;
            }
            else
            {
                // 4. 상세 정보 보강 (PostgreSQL 연동) - Bulk 집계
                var allPns = mergedList.Select(item => item["questionnaireNumber"]?.ToString() ?? "")
                                       .Where(p => !string.IsNullOrEmpty(p))
                                       .Distinct()
                                       .ToList();

                var registeredPnsTask = mainRepo.GetBulkSurveyonProjectPnsAsync(allPns, token);
                var postgreynMapTask = mainRepo.GetBulkAnswerCountsAsync(allPns, "4", token);

                await Task.WhenAll(registeredPnsTask, postgreynMapTask);

                var registeredPns = await registeredPnsTask;
                var postgreynMap = await postgreynMapTask;

                // 5. MS Sampling 완료수 병렬(Parallel) 조회 및 1 이상인 항목 필터링
                var countTasks = mergedList.Select(async item =>
                {
                    string projectnum = item["questionnaireNumber"]?.ToString() ?? "";
                    string server = item["server"]?.ToString() ?? "";
                    int count = await mainRepo.GetSamplingCompleteCountAsync(projectnum, "4", server, token);
                    return (item, projectnum, count);
                });

                var countResults = await Task.WhenAll(countTasks);

                foreach (var (item, projectnum, count) in countResults)
                {
                    if (count <= 0 && gubun== "1") continue;

                    string gubunYN = registeredPns.Contains(projectnum) ? "등록" : "";

                    postgreynMap.TryGetValue(projectnum, out string? postgreyn);
                    postgreyn ??= "";

                    string fieldWorkYN = item["fieldWorkYN"]?.ToString() == "1" ? "마감" : "진행중";

                    string questionnaireName = item["questionnaireName"]?.ToString() ?? "";
                    questionnaireName = questionnaireName.Replace("\"", "").Replace("'", "");

                    var row = new JObject
                    {
                        ["pof"] = item["pof"]?.ToString(),
                        ["projectnum"] = projectnum,
                        ["questionnaireName"] = questionnaireName,
                        ["user아이디"] = item["user아이디"]?.ToString(),
                        ["researcherName"] = item["researcherName"]?.ToString(),
                        ["questionnairePersonName"] = item["questionnairePersonName"]?.ToString(),
                        ["fieldWorkerName"] = item["fieldWorkerName"]?.ToString(),
                        ["project_register_date"] = item["createDateTime"]?.ToString(),
                        ["created_at"] = item["createDateTime"]?.ToString(),
                        ["createdAt"] = item["createDateTime"]?.ToString(),
                        ["servername"] = item["server"]?.ToString(),
                        ["postgreyn"] = postgreyn,
                        ["fieldWorkYN"] = fieldWorkYN,
                        ["gubunYN"] = gubunYN
                    };
                    resultList.Add(row);
                }
                return resultList;
            }
        }

        private async Task<bool> Auto_project_enter(
            string projectnum,
            string server,
            string pof,
            string proname,
            string user,
            string usergroup = "관리자(관리,읽기,쓰기)",
            string gubun = "1",
            IMainRepository? repoOverride = null,
            CancellationToken ct = default,
            System.Collections.Concurrent.ConcurrentDictionary<(string user, string gubun), DataTable>? hproCache = null,
            HashSet<string>? existingPerms = null)
        {
            var mainRepo = repoOverride ?? _mainRepository;
            var token = repoOverride != null ? ct : (HttpContext?.RequestAborted ?? CancellationToken.None);
            
            DataTable? dtRps_userid = null;
            if (hproCache != null && hproCache.TryGetValue((user, gubun), out var cachedDt))
            {
                dtRps_userid = cachedDt;
            }
            else
            {
                dtRps_userid = await mainRepo.select_user_hpro_api(user, gubun, "", token);
                if (hproCache != null && dtRps_userid != null)
                {
                    hproCache[(user, gubun)] = dtRps_userid;
                }
            }

            dtRps_userid ??= new DataTable();
            string userid_single = "";
            string username = "";
            string groupposition = "";
            if (dtRps_userid.Rows.Count > 0)
            {
                foreach (DataRow row in dtRps_userid.Rows)
                {
                    string email = row["email"].ToString().TrimEnd();
                    string[] parts = email.Split('@');
                    userid_single = parts[0];
                    username = row["이름"].ToString().TrimEnd();
                    groupposition = row["부서"].ToString().TrimEnd();
                }
            }
            if (userid_single != "")
            {
                string key = $"{projectnum}_{userid_single}_{usergroup}";
                if (existingPerms != null && existingPerms.Contains(key))
                {
                    return false;
                }

                await mainRepo.InsertProjectEnterAsync(projectnum, pof, proname, userid_single, server, token);
                await mainRepo.InsertProjectPermissionAsync(projectnum, pof, proname, userid_single, username, groupposition, usergroup, token);

                if (existingPerms != null)
                {
                    existingPerms.Add(key);
                }
                return true;
            }
            return false;
        }

        private async Task InsertOpenData(string projectnum, string server, string pof, string proname, string user, IMainRepository? repoOverride = null, CancellationToken ct = default)
        {
            var mainRepo = repoOverride ?? _mainRepository;
            var token = repoOverride != null ? ct : (HttpContext?.RequestAborted ?? CancellationToken.None);
            await mainRepo.InsertOpenDataAsync(projectnum, server, pof, proname, user, isIncremental: false, token);
        }

        private string ExtractEffectiveToken(string? tokenFromHeader)
        {
            string effective = tokenFromHeader ?? "";
            if (string.IsNullOrWhiteSpace(effective) && HttpContext != null)
            {
                effective = HttpContext.Request.Headers["X-Auth-Token"].FirstOrDefault() ?? "";
                if (string.IsNullOrWhiteSpace(effective))
                {
                    var authHeader = HttpContext.Request.Headers["Authorization"].FirstOrDefault() ?? "";
                    if (!string.IsNullOrWhiteSpace(authHeader))
                    {
                        effective = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? authHeader.Substring("Bearer ".Length).Trim()
                            : authHeader.Trim();
                    }
                }
            }
            return effective;
        }

        private async Task<string> CallExternalApi(string pn, string user, string TokenKey, IHttpClientFactory? httpOverride = null, Microsoft.Extensions.Configuration.IConfiguration? configOverride = null, IMainRepository? repoOverride = null)
        {
            var configuration = configOverride ?? _configuration;
            var httpClientFactory = httpOverride ?? _httpClientFactory;
            var mainRepo = repoOverride ?? _mainRepository;
            string url = configuration["ExternalApi:DataMapUrl"] ?? "";

            if (string.IsNullOrWhiteSpace(url))
            {
                _logger?.LogError($"[CallExternalApi] ExternalApi:DataMapUrl configuration is missing for project {pn}");
                return "ConfigError";
            }

            var body = new
            {
                pn = pn,
                user = user,
                Creator = user
            };

            string json = JsonConvert.SerializeObject(body);

            int maxRetries = 5;
            int delayMs = 500;
            string lastResult = "UnknownError";

            string currentToken = TokenKey ?? "";

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // 2차 시도 이상이거나 1차 시도 토큰이 빈 값인 경우: Token Auto-Healing
                if (attempt > 1 || string.IsNullOrWhiteSpace(currentToken))
                {
                    if (attempt == 2 || string.IsNullOrWhiteSpace(currentToken))
                    {
                        // DB pro_login 테이블에서 user의 loginkey 조회
                        string dbKey = await mainRepo.GetUserLoginKeyAsync(user, CancellationToken.None);
                        if (!string.IsNullOrWhiteSpace(dbKey))
                        {
                            currentToken = dbKey;
                        }
                    }
                    if ((attempt >= 3 || string.IsNullOrWhiteSpace(currentToken)) && string.IsNullOrWhiteSpace(currentToken))
                    {
                        // System InternalKey 대조
                        currentToken = configuration["InternalAuth:InternalKey"] ?? "";
                    }
                }

                try
                {
                    var client = httpClientFactory.CreateClient();
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    client.DefaultRequestHeaders.Remove("X-Auth-Token");
                    client.DefaultRequestHeaders.Add("X-Auth-Token", currentToken);
                    client.DefaultRequestHeaders.Remove("Authorization");
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + currentToken);

                    var response = await client.PostAsync(url, content);
                    var responseText = await response.Content.ReadAsStringAsync();
                    lastResult = response.StatusCode.ToString();

                    if (response.IsSuccessStatusCode && lastResult == "OK")
                    {
                        _logger?.LogInformation($"[CallExternalApi] Success on attempt {attempt}/{maxRetries} for project {pn}. Status: {lastResult}");
                        return "OK";
                    }
                    else
                    {
                        lastResult = lastResult; 
                    }

                    _logger?.LogWarning($"[CallExternalApi] Attempt {attempt}/{maxRetries} failed for project {pn}. StatusCode: {response.StatusCode}, Response: {responseText}");

                    // 401 Unauthorized 또는 403 Forbidden 발생 시 다음 attempt에서 DB loginkey / InternalKey 로 교체 시도
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        currentToken = "";
                    }
                }
                catch (Exception ex)
                {
                    lastResult = ex.Message;
                    _logger?.LogWarning(ex, $"[CallExternalApi] Attempt {attempt}/{maxRetries} threw exception for project {pn}");
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs);
                    delayMs *= 2; // Exponential backoff: 500ms, 1000ms, 2000ms, 4000ms...
                }
            }

            _logger?.LogError($"[CallExternalApi] All {maxRetries} attempts failed for project {pn}. Final Status: {lastResult}");
            return lastResult;
        }

        private async Task<string> CallExternalApi_srt(string pn, string user, string TokenKey, IHttpClientFactory? httpOverride = null, Microsoft.Extensions.Configuration.IConfiguration? configOverride = null, IMainRepository? repoOverride = null)
        {
            var configuration = configOverride ?? _configuration;
            var httpClientFactory = httpOverride ?? _httpClientFactory;
            var mainRepo = repoOverride ?? _mainRepository;
            string url = configuration["ExternalApi:SRTdashbordUrl"] ?? "";

            if (string.IsNullOrWhiteSpace(url))
            {
                _logger?.LogError($"[CallExternalApi_srt] ExternalApi:SRTdashbordUrl configuration is missing for project {pn}");
                return "ConfigError";
            }

            var body = new
            {
                pn = pn,
                user = user,
                title = "NEW_DASHBOARD"
            };

            string json = JsonConvert.SerializeObject(body);

            int maxRetries = 5;
            int delayMs = 500;
            string lastResult = "UnknownError";

            string currentToken = TokenKey ?? "";

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (attempt > 1 || string.IsNullOrWhiteSpace(currentToken))
                {
                    if (attempt == 2 || string.IsNullOrWhiteSpace(currentToken))
                    {
                        string dbKey = await mainRepo.GetUserLoginKeyAsync(user, CancellationToken.None);
                        if (!string.IsNullOrWhiteSpace(dbKey))
                        {
                            currentToken = dbKey;
                        }
                    }
                    if ((attempt >= 3 || string.IsNullOrWhiteSpace(currentToken)) && string.IsNullOrWhiteSpace(currentToken))
                    {
                        currentToken = configuration["InternalAuth:InternalKey"] ?? "";
                    }
                }

                try
                {
                    var client = httpClientFactory.CreateClient();
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    client.DefaultRequestHeaders.Remove("X-Auth-Token");
                    client.DefaultRequestHeaders.Add("X-Auth-Token", currentToken);
                    client.DefaultRequestHeaders.Remove("Authorization");
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + currentToken);

                    var response = await client.PostAsync(url, content);
                    var responseText = await response.Content.ReadAsStringAsync();
                    lastResult = response.StatusCode.ToString();

                    if (response.IsSuccessStatusCode && lastResult == "OK")
                    {
                        _logger?.LogInformation($"[CallExternalApi_srt] Success on attempt {attempt}/{maxRetries} for project {pn}. Status: {lastResult}");
                        return "OK";
                    }

                    _logger?.LogWarning($"[CallExternalApi_srt] Attempt {attempt}/{maxRetries} failed for project {pn}. StatusCode: {response.StatusCode}, Response: {responseText}");

                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        currentToken = "";
                    }
                }
                catch (Exception ex)
                {
                    lastResult = ex.Message;
                    _logger?.LogWarning(ex, $"[CallExternalApi_srt] Attempt {attempt}/{maxRetries} threw exception for project {pn}");
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
            }

            _logger?.LogError($"[CallExternalApi_srt] All {maxRetries} attempts failed for project {pn}. Final Status: {lastResult}");
            return lastResult;
        }
        

        public class jobject_arr
        {
            //qmlist
            public string? user { get; set; }
            public string? search { get; set; }
            public string? gubun { get; set; }
            public string? gb { get; set; }
            //qmlistselect
            public string? projectnum { get; set; }
            public string? pof { get; set; }
            public string? projectname { get; set; }
            public string? questionnaireName { get; set; }
            public string? servername { get; set; }
            //list_start & list_progress
            public string? jobid { get; set; }
            public bool? forceNew { get; set; }
            //top_toggle
            public string? topyn { get; set; }
        }

        [HttpPost("top_toggle")]
        public async Task<IActionResult> TopToggleAction([FromBody] jobject_arr req)
        {
            req.gb = "top_toggle";
            return await ProjectAction(req, null);
        }
    }
}
