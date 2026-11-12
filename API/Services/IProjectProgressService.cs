using System;

namespace SurveyonApi.Services
{
    public class ProjectProgressState
    {
        public string JobId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Status { get; set; } = "processing"; // "processing", "completed", "failed"
        public int TotalCount { get; set; } = 0;
        public int CurrentCount { get; set; } = 0;
        public int Percent { get; set; } = 0;
        public string Message { get; set; } = string.Empty;
        public object? ResultJson { get; set; } = null;
        public string? ErrorMessage { get; set; } = null;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public interface IProjectProgressService
    {
        ProjectProgressState CreateOrGetJob(string userId, out string jobId, bool forceNew = false);
        ProjectProgressState? GetProgress(string? jobId, string? userId);
        void UpdateProgress(string jobId, int percent, string message, int currentCount = 0, int totalCount = 0);
        void CompleteJob(string jobId, object resultJson);
        void FailJob(string jobId, string errorMessage);
        void ClearJob(string userId);
    }
}
