using System;
using System.Collections.Generic;
using Experimental.System.Messaging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SurveyonApi.Models;

namespace SurveyonApi.Services
{
    public class QueueSendingService : IQueueSendingService
    {
        private const string QueuePath = @".\Private$\SeoulmunOnOpenCodeUpdate";
        private const string LabelHongEvent = "HongEvent";
        private readonly ILogger<QueueSendingService> _logger;

        public QueueSendingService(ILogger<QueueSendingService> logger)
        {
            _logger = logger;
            EnsureQueueExists();
        }

        private void EnsureQueueExists()
        {
            try
            {
                if (!MessageQueue.Exists(QueuePath))
                {
                    _logger.LogInformation("[QueueSendingService] MSMQ 큐가 없어 신규 생성합니다. Path: {QueuePath}", QueuePath);
                    MessageQueue.Create(QueuePath, transactional: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QueueSendingService] MSMQ 큐 존재 확인/생성 중 경고 (권한 또는 서비스 미구동 여부 확인 필요) Path: {QueuePath}", QueuePath);
            }
        }

        public bool SendQueue(QueueDto dto)
        {
            if (dto == null) return false;
            try
            {
                string json = JsonConvert.SerializeObject(dto);
                using var queue = new MessageQueue(QueuePath);
                queue.Formatter = new XmlMessageFormatter(new[] { typeof(string) });

                var msg = new Message
                {
                    Body = json,
                    Label = LabelHongEvent,
                    Priority = MessagePriority.Normal
                };

                queue.Send(msg);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QueueSendingService] MSMQ 단건 전송 실패 pn={Pn} qnum={Qnum} pid={Pid}", dto.pn, dto.qnum, dto.pid);
                return false;
            }
        }

        public int SendQueueBatch(IEnumerable<QueueDto> dtoList)
        {
            if (dtoList == null) return 0;

            int sentCount = 0;
            try
            {
                using var queue = new MessageQueue(QueuePath);
                queue.Formatter = new XmlMessageFormatter(new[] { typeof(string) });

                foreach (var dto in dtoList)
                {
                    if (dto == null) continue;
                    try
                    {
                        string json = JsonConvert.SerializeObject(dto);
                        var msg = new Message
                        {
                            Body = json,
                            Label = LabelHongEvent,
                            Priority = MessagePriority.Normal
                        };

                        queue.Send(msg);
                        sentCount++;
                    }
                    catch (Exception exItem)
                    {
                        _logger.LogError(exItem, "[QueueSendingService] MSMQ 개별 아이템 인큐 실패 pn={Pn} qnum={Qnum} pid={Pid}", dto.pn, dto.qnum, dto.pid);
                    }
                }

                _logger.LogInformation("[QueueSendingService] MSMQ 배치 전송 완료. 총 성공 건수: {SentCount}", sentCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QueueSendingService] MSMQ 큐 열기/배치 전송 중 오류 발생. Path: {QueuePath}", QueuePath);
            }

            return sentCount;
        }
    }
}
