namespace SurveyonApi.Models
{
    /// <summary>
    /// MSMQ (.\Private$\SeoulmunOnOpenCodeUpdate) 전송용 DTO
    /// 레거시 MyDto 구조와 100% 동일하게 호환
    /// </summary>
    public class QueueDto
    {
        public string servername { get; set; } = "";
        public string pn { get; set; } = "";
        public string qnum { get; set; } = "";
        public string pid { get; set; } = "";
        public string? cid { get; set; }
        public string lv123code { get; set; } = "";
        public string recheckyn { get; set; } = "0";
        public string deleteyn { get; set; } = "0";
        public string updateDate { get; set; } = "";
    }
}
