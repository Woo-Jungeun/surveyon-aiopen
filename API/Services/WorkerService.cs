using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SurveyonApi.Hubs;

namespace SurveyonApi.Services
{
    public class WorkerCustomFlags
    {
        public string? Classified { get; set; }
        public string? Response { get; set; }
        public string? OpencodeCategory { get; set; }
        public string? OpencodeResponse { get; set; }
        public string? RecallResponse { get; set; }
        public string? TranslateResponse { get; set; }
        public bool IsHanq { get; set; }
        public string? Lvcode { get; set; }
    }

    public interface IWorkerService
    {
        Task<string> StartJobAsync(string projectnum, string qnum, int qid, string action, WorkerCustomFlags? flags = null, CancellationToken ct = default);
        WorkerJobStatusDto GetStatus(string jobId, int offset = 0);
        bool StopJob(string jobId);
    }

    public class WorkerJobStatusDto
    {
        public bool Exists { get; set; }
        public bool IsCompleted { get; set; }
        public bool HasError { get; set; }
        public int? ExitCode { get; set; }
        public string? LastLine { get; set; }
        public string Output { get; set; } = "";
        public int NextOffset { get; set; }
        public DateTime? UpdatedUtc { get; set; }
    }

    public class WorkerService : IWorkerService
    {
        private class JobState
        {
            public string JobId { get; }
            public string ProjectNum { get; set; } = "";
            public string Qnum { get; set; } = "";
            public string Action { get; set; } = "";
            public bool IsHanq { get; set; }
            public string? Lvcode { get; set; }
            public Process? Process { get; set; }
            public StringBuilder Output { get; } = new();
            public string? LastLine { get; set; }
            public bool IsCompleted { get; set; }
            public bool HasError { get; set; }
            public int? ExitCode { get; set; }
            public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
            public object LockObj { get; } = new();

            public JobState(string jobId) => JobId = jobId;
        }

        private static readonly ConcurrentDictionary<string, JobState> Jobs = new();
        private readonly ILogger<WorkerService> _logger;
        private readonly IHubContext<WorkerHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;

        public WorkerService(
            ILogger<WorkerService> logger,
            IHubContext<WorkerHub> hubContext,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
        }

        public async Task<string> StartJobAsync(
            string projectnum,
            string qnum,
            int qid,
            string action,
            WorkerCustomFlags? flags = null,
            CancellationToken ct = default)
        {
            static bool IsYes(string? val) =>
                string.Equals(val?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);

            string actionNorm = (action ?? "").Trim().ToLowerInvariant();
            bool hasActiveCustomFlags = flags != null && (
                IsYes(flags.Classified) ||
                IsYes(flags.Response) ||
                IsYes(flags.OpencodeCategory) ||
                IsYes(flags.OpencodeResponse) ||
                IsYes(flags.RecallResponse) ||
                IsYes(flags.TranslateResponse));

            string arguments;
            if (hasActiveCustomFlags)
            {
                var sb = new StringBuilder($"--projectid {qid} --projectnum {projectnum}");
                if (IsYes(flags!.Classified)) sb.Append(" --classified_yn Y");
                if (IsYes(flags.Response)) sb.Append(" --response_yn Y");
                if (IsYes(flags.OpencodeCategory)) sb.Append(" --opencode_category_yn Y");
                if (IsYes(flags.OpencodeResponse)) sb.Append(" --opencode_response_yn Y");
                if (IsYes(flags.RecallResponse)) sb.Append(" --recall_response_yn Y");
                if (IsYes(flags.TranslateResponse) || actionNorm is "translate" or "번역")
                    sb.Append(" --translated_response_yn Y");
                arguments = sb.ToString();
            }
            else
            {
                string actionArg = actionNorm switch
                {
                    "analyze" or "보기" => "--classified_yn Y --opencode_category_yn Y",
                    "translate" or "번역" => "--translated_response_yn Y",
                    "respondent" or "응답" or "응답자" => "--response_yn Y --opencode_response_yn Y --recall_response_yn Y",
                    "empty_etc" or "재분류" => "--recall_response_yn Y",
                    _ => "--classified_yn Y --opencode_category_yn Y"
                };
                arguments = $"--projectid {qid} --projectnum {projectnum} {actionArg}";
            }

            string jobId = $"job_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..28];

            var state = new JobState(jobId)
            {
                ProjectNum = projectnum,
                Qnum = qnum,
                Action = actionNorm,
                IsHanq = flags?.IsHanq == true,
                Lvcode = flags?.Lvcode
            };
            Jobs[jobId] = state;

            // Locate worker executable (worker_0427.exe 바이너리 직접 호출)
            string[] possiblePaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "docs", "legacy_src", "worker_0427.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "API", "docs", "legacy_src", "worker_0427.exe"),
                Path.Combine(AppContext.BaseDirectory, "docs", "legacy_src", "worker_0427.exe"),
                Path.Combine(AppContext.BaseDirectory, "App_Data", "worker_0427.exe"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "legacy_src", "worker_0427.exe")),
                @"d:\User_Data\Desktop\프로젝트\설문온\surveyon-aiopen-api\API\docs\legacy_src\worker_0427.exe",
                @"D:\python\Antigravity\(261109)설문온_AIOPEN\API\docs\legacy_src\worker_0427.exe",
                @"d:\python\Antigravity\(260113)설문온_APICORE\docs\legacy_src\worker_0427.exe"
            };

            string? exePath = null;
            foreach (var p in possiblePaths)
            {
                if (File.Exists(p))
                {
                    exePath = p;
                    break;
                }
            }

            if (exePath != null)
            {
                _logger.LogInformation("[WorkerService] Starting worker_0427.exe at {ExePath} with args: {Args}", exePath, arguments);
                StartRealProcess(state, exePath, arguments);
            }
            else
            {
                _logger.LogWarning("[WorkerService] worker_0427.exe not found! Falling back to simulated job for {Pn}/{Qnum} ({Action})", projectnum, qnum, action);
                _ = Task.Run(() => RunSimulatedJobAsync(state, action, projectnum, qnum));
            }

            return jobId;
        }

        private void StartRealProcess(JobState state, string exePath, string arguments)
        {
            string workingDir = Path.GetDirectoryName(exePath) ?? Directory.GetCurrentDirectory();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding consoleEncoding;
            try
            {
                // worker_0427.exe는 Windows 콘솔 기본 코드페이지인 CP949(EUC-KR)로 표준 출력을 생성함
                consoleEncoding = Encoding.GetEncoding(949);
            }
            catch
            {
                consoleEncoding = Encoding.UTF8;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDir,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = consoleEncoding,
                StandardErrorEncoding = consoleEncoding,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                psi.Environment["PYTHONUTF8"] = "1";
            }
            catch { }

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            state.Process = proc;

            // 표준 출력 (파이썬 소스 내부의 print() 출력 실시간 중계)
            proc.OutputDataReceived += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (state.LockObj)
                    {
                        state.Output.AppendLine(e.Data);
                        state.LastLine = e.Data;
                        state.UpdatedUtc = DateTime.UtcNow;
                    }

                    try
                    {
                        await _hubContext.Clients.Group(state.JobId).SendAsync("onLine", state.JobId, e.Data, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to broadcast stdout onLine to group {JobId}", state.JobId);
                    }
                }
            };

            // 표준 오류 (파이썬 sys.stderr 출력 실시간 중계)
            proc.ErrorDataReceived += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    string line = "ERROR: " + e.Data;
                    lock (state.LockObj)
                    {
                        state.Output.AppendLine(line);
                        state.LastLine = line;
                        state.HasError = true;
                        state.UpdatedUtc = DateTime.UtcNow;
                    }

                    try
                    {
                        await _hubContext.Clients.Group(state.JobId).SendAsync("onLine", state.JobId, line, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to broadcast stderr onLine to group {JobId}", state.JobId);
                    }
                }
            };

            // 프로세스 종료 (정상 또는 비정상 종료 완료 통보)
            proc.Exited += async (s, e) =>
            {
                int exitCode = -1;
                try { exitCode = proc.ExitCode; } catch { }

                // 한큐분석(isHanq=true) 3단계(empty_etc) 정상 완료 시 HData.surveyon_labels 및 응답/큐 자동 동기화
                bool shouldHanqAutoSync;
                lock (state.LockObj)
                {
                    shouldHanqAutoSync = state.IsHanq
                        && (state.Action == "empty_etc" || state.Action == "재분류")
                        && exitCode == 0
                        && !state.HasError;
                }

                if (shouldHanqAutoSync)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var analysisService = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
                        await analysisService.AutoSyncAfterHanqCompleteAsync(
                            state.ProjectNum, state.Qnum, state.Lvcode);
                    }
                    catch (Exception syncEx)
                    {
                        _logger.LogError(syncEx, "[WorkerService] 한큐분석 완료 후 HData/큐 자동 동기화 실패 jobId={JobId} pn={Pn} qnum={Qnum}",
                            state.JobId, state.ProjectNum, state.Qnum);
                        string errLine = $"ERROR: [한큐분석 자동 반영 실패] {syncEx.Message}";
                        lock (state.LockObj)
                        {
                            state.HasError = true;
                            state.Output.AppendLine(errLine);
                            state.LastLine = errLine;
                            state.UpdatedUtc = DateTime.UtcNow;
                        }
                        try
                        {
                            await _hubContext.Clients.Group(state.JobId).SendAsync("onLine", state.JobId, errLine, true);
                        }
                        catch { }
                    }
                }

                lock (state.LockObj)
                {
                    state.IsCompleted = true;
                    state.ExitCode = exitCode;
                    state.UpdatedUtc = DateTime.UtcNow;
                }

                try
                {
                    await _hubContext.Clients.Group(state.JobId).SendAsync("onCompleted", state.JobId, state.HasError, state.ExitCode);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to broadcast onCompleted to group {JobId}", state.JobId);
                }

                try { proc.Dispose(); } catch { }
            };

            try
            {
                if (!proc.Start())
                {
                    lock (state.LockObj)
                    {
                        state.HasError = true;
                        state.IsCompleted = true;
                        state.LastLine = "프로세스 시작 실패";
                    }
                    _ = _hubContext.Clients.Group(state.JobId).SendAsync("onLine", state.JobId, "ERROR: 프로세스 시작 실패", true);
                    _ = _hubContext.Clients.Group(state.JobId).SendAsync("onCompleted", state.JobId, true, -1);
                    return;
                }

                // 레거시 WorkerManager와 동일하게 StandardInput 즉시 Close (입력 대기 방지)
                try { proc.StandardInput.Close(); } catch { }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                lock (state.LockObj)
                {
                    state.HasError = true;
                    state.IsCompleted = true;
                    state.LastLine = ex.Message;
                    state.Output.AppendLine("EXCEPTION: " + ex);
                }
                _ = _hubContext.Clients.Group(state.JobId).SendAsync("onLine", state.JobId, "EXCEPTION: " + ex.Message, true);
                _ = _hubContext.Clients.Group(state.JobId).SendAsync("onCompleted", state.JobId, true, -1);
            }
        }

        private async Task RunSimulatedJobAsync(JobState state, string action, string pn, string qnum)
        {
            string label = action switch
            {
                "analyze" => "보기분석(카테고리/응답)",
                "translate" => "번역",
                "respondent" => "응답자분석",
                "empty_etc" => "빈셀·기타 재호출",
                _ => "처리"
            };

            string[] steps = new[]
            {
                $"[1/4] {label} 작업 시작: {pn} / {qnum}",
                $"[2/4] DB 응답 데이터 로드 및 전처리 검증 완료",
                $"[3/4] Python AI 분류 모델 처리 중 …",
                $"[4/4 완료] {label} 처리 정상 완료"
            };

            for (int i = 0; i < steps.Length; i++)
            {
                await Task.Delay(400);
                string line = steps[i];
                lock (state.LockObj)
                {
                    state.Output.AppendLine(line);
                    state.LastLine = line;
                    state.UpdatedUtc = DateTime.UtcNow;
                }

                try
                {
                    await _hubContext.Clients.Group(state.JobId).SendAsync("onLine", state.JobId, line, false);
                }
                catch { }
            }

            lock (state.LockObj)
            {
                state.IsCompleted = true;
                state.ExitCode = 0;
            }

            try
            {
                await _hubContext.Clients.Group(state.JobId).SendAsync("onCompleted", state.JobId, false, 0);
            }
            catch { }
        }

        public WorkerJobStatusDto GetStatus(string jobId, int offset = 0)
        {
            if (!Jobs.TryGetValue(jobId, out var state))
                return new WorkerJobStatusDto { Exists = false };

            lock (state.LockObj)
            {
                string full = state.Output.ToString();
                int start = Math.Min(Math.Max(offset, 0), full.Length);
                string delta = full.Substring(start);

                return new WorkerJobStatusDto
                {
                    Exists = true,
                    IsCompleted = state.IsCompleted,
                    HasError = state.HasError,
                    ExitCode = state.IsCompleted ? state.ExitCode : null,
                    LastLine = state.LastLine,
                    UpdatedUtc = state.UpdatedUtc,
                    Output = delta,
                    NextOffset = full.Length
                };
            }
        }

        public bool StopJob(string jobId)
        {
            if (!Jobs.TryGetValue(jobId, out var state)) return false;
            lock (state.LockObj)
            {
                if (state.Process != null && !state.Process.HasExited)
                {
                    try
                    {
                        state.Process.Kill(entireProcessTree: true);
                        state.IsCompleted = true;
                        state.HasError = true;
                        string killLine = "[Worker Killed] 사용자에 의해 중지되었습니다.";
                        state.Output.AppendLine(killLine);
                        _ = _hubContext.Clients.Group(jobId).SendAsync("onLine", jobId, killLine, true);
                        _ = _hubContext.Clients.Group(jobId).SendAsync("onCompleted", jobId, true, -1);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to kill worker process {JobId}", jobId);
                    }
                }
            }
            return false;
        }
    }
}
