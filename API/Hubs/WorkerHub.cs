using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace SurveyonApi.Hubs
{
    /// <summary>
    /// AI 오픈 분석 worker_0427.exe 실행 로그 및 상태값을 실시간으로 스트리밍하기 위한 SignalR Hub
    /// 레거시 worker_api_web.aspx의 hub.server.join(job) / leave(job)과 완벽 호환
    /// </summary>
    public class WorkerHub : Hub
    {
        private readonly ILogger<WorkerHub> _logger;

        public WorkerHub(ILogger<WorkerHub> logger)
        {
            _logger = logger;
        }

        public async Task Join(string job)
        {
            if (!string.IsNullOrWhiteSpace(job))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, job);
                _logger.LogInformation("[WorkerHub] Client {ConnectionId} joined job group {Job}", Context.ConnectionId, job);
            }
        }

        public async Task Leave(string job)
        {
            if (!string.IsNullOrWhiteSpace(job))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, job);
                _logger.LogInformation("[WorkerHub] Client {ConnectionId} left job group {Job}", Context.ConnectionId, job);
            }
        }
    }
}
