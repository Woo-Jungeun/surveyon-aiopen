using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SurveyonApi.Services
{
    public class ProjectProgressService : IProjectProgressService
    {
        private readonly ConcurrentDictionary<string, ProjectProgressState> _jobsByJobId = new();
        private readonly ConcurrentDictionary<string, string> _activeJobIdByUserId = new();

        public ProjectProgressState CreateOrGetJob(string userId, out string jobId, bool forceNew = false)
        {
            CleanUpOldJobs();

            string normalizedUserId = (userId ?? string.Empty).Trim().ToLowerInvariant();

            if (!forceNew && !string.IsNullOrEmpty(normalizedUserId) && _activeJobIdByUserId.TryGetValue(normalizedUserId, out var existingJobId))
            {
                if (_jobsByJobId.TryGetValue(existingJobId, out var existingState))
                {
                    if (existingState.Status == "processing" && (DateTime.Now - existingState.UpdatedAt).TotalMinutes < 15)
                    {
                        jobId = existingJobId;
                        return existingState;
                    }
                }
            }

            jobId = Guid.NewGuid().ToString("N");
            var newState = new ProjectProgressState
            {
                JobId = jobId,
                UserId = userId ?? string.Empty,
                Status = "processing",
                Percent = 0,
                Message = "동기화 작업을 준비 중입니다...",
                UpdatedAt = DateTime.Now
            };

            _jobsByJobId[jobId] = newState;

            if (!string.IsNullOrEmpty(normalizedUserId))
            {
                _activeJobIdByUserId[normalizedUserId] = jobId;
            }

            return newState;
        }

        public ProjectProgressState? GetProgress(string? jobId, string? userId)
        {
            CleanUpOldJobs();

            if (!string.IsNullOrWhiteSpace(jobId) && _jobsByJobId.TryGetValue(jobId.Trim(), out var stateByJobId))
            {
                return stateByJobId;
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                string normalizedUserId = userId.Trim().ToLowerInvariant();
                if (_activeJobIdByUserId.TryGetValue(normalizedUserId, out var activeJobId))
                {
                    if (_jobsByJobId.TryGetValue(activeJobId, out var stateByUserId))
                    {
                        return stateByUserId;
                    }
                }
            }

            return null;
        }

        public void UpdateProgress(string jobId, int percent, string message, int currentCount = 0, int totalCount = 0)
        {
            if (_jobsByJobId.TryGetValue(jobId, out var state))
            {
                lock (state)
                {
                    state.Percent = Math.Clamp(percent, 0, 99);
                    state.Message = message;
                    if (currentCount > 0) state.CurrentCount = currentCount;
                    if (totalCount > 0) state.TotalCount = totalCount;
                    state.UpdatedAt = DateTime.Now;
                }
            }
        }

        public void CompleteJob(string jobId, object resultJson)
        {
            if (_jobsByJobId.TryGetValue(jobId, out var state))
            {
                lock (state)
                {
                    state.Status = "completed";
                    state.Percent = 100;
                    state.Message = "프로젝트 목록 동기화가 완료되었습니다.";
                    state.ResultJson = resultJson;
                    state.UpdatedAt = DateTime.Now;
                }
            }
        }

        public void FailJob(string jobId, string errorMessage)
        {
            if (_jobsByJobId.TryGetValue(jobId, out var state))
            {
                lock (state)
                {
                    state.Status = "failed";
                    state.ErrorMessage = errorMessage;
                    state.Message = $"오류가 발생했습니다: {errorMessage}";
                    state.UpdatedAt = DateTime.Now;
                }
            }
        }

        public void ClearJob(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            string normalizedUserId = userId.Trim().ToLowerInvariant();
            if (_activeJobIdByUserId.TryRemove(normalizedUserId, out var jobId))
            {
                _jobsByJobId.TryRemove(jobId, out _);
            }
        }

        private void CleanUpOldJobs()
        {
            var cutoff = DateTime.Now.AddMinutes(-30);
            var expiredJobs = _jobsByJobId.Where(kvp => kvp.Value.UpdatedAt < cutoff).Select(kvp => kvp.Key).ToList();

            foreach (var oldJobId in expiredJobs)
            {
                if (_jobsByJobId.TryRemove(oldJobId, out var state))
                {
                    string normalizedUserId = (state.UserId ?? string.Empty).Trim().ToLowerInvariant();
                    if (_activeJobIdByUserId.TryGetValue(normalizedUserId, out var activeId) && activeId == oldJobId)
                    {
                        _activeJobIdByUserId.TryRemove(normalizedUserId, out _);
                    }
                }
            }
        }
    }
}
