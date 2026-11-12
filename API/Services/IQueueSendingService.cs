using SurveyonApi.Models;

namespace SurveyonApi.Services
{
    public interface IQueueSendingService
    {
        /// <summary>
        /// 단건 MSMQ 전송
        /// </summary>
        bool SendQueue(QueueDto dto);

        /// <summary>
        /// 다건(배치) MSMQ 전송
        /// </summary>
        /// <param name="dtoList">전송할 QueueDto 목록</param>
        /// <returns>성공적으로 인큐된 메시지 건수</returns>
        int SendQueueBatch(IEnumerable<QueueDto> dtoList);
    }
}
