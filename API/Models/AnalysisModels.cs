namespace SurveyonApi.Models
{
    // ══════════════════════════════════════════════════════════════════════
    // AI오픈분석 › 분석 페이지 (/analysis/*) 요청/응답 모델
    //  - 레거시 option_edit_api / option_status_api 요청 필드명을 그대로 유지 (프런트 호환)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>분석 페이지 공통 요청 (user는 토큰의 사용자로 강제 대체됨)</summary>
    public class AnalysisRequest
    {
        public string? user { get; set; }
        /// <summary>머지 조사번호 (merge_pn)</summary>
        public string? projectnum { get; set; }
        /// <summary>머지 문항번호 (merge_qnum)</summary>
        public string? qnum { get; set; }
        /// <summary>project_list.id (status 조회용)</summary>
        public string? qid { get; set; }
        /// <summary>popup_check: "1"이면 팝업 확인 처리</summary>
        public string? checkyn { get; set; }
    }

    /// <summary>project_list 분석 대표행 (레거시 select_project_list gb=111 컬럼)</summary>
    public class AnalysisQuestionMeta
    {
        public int id { get; set; }
        public string? option_json { get; set; }
        public string? servername { get; set; }
        public string? projectname { get; set; }
        public string? projectpof { get; set; }
        public string? question_fin { get; set; }
        public int? listapikey { get; set; }
        public string? lvcode { get; set; }
        public string? duplicate_remove_date { get; set; }
        public string? load_fin { get; set; }
        public string? merge_pn { get; set; }
        public string? merge_qnum { get; set; }
        public string? qnum { get; set; }
        public string? qnum_text { get; set; }
    }

    /// <summary>pn_in 조회 결과 (answer_origin은 해당 문항 배열만 서버에서 추출)</summary>
    public class AnalysisPnInRow
    {
        public string? input_date { get; set; }
        public string? update_date { get; set; }
        /// <summary>answer_origin -> qnum (JSON 배열 문자열)</summary>
        public string? origin_items { get; set; }
        /// <summary>answer_fin (JSON 배열 문자열)</summary>
        public string? answer_fin { get; set; }
    }

    /// <summary>pn_lb 조회 결과</summary>
    public class AnalysisPnLbRow
    {
        public string? result_json { get; set; }
        public string? insert_date { get; set; }
        public string? update_date { get; set; }
    }

    /// <summary>머지 원본 문항행 (레거시 select_project_list gb=11, 필요한 컬럼만)</summary>
    public class AnalysisSourceRow
    {
        public string? projectnum { get; set; }
        public string? qnum { get; set; }
        public string? servername { get; set; }
        public string? qnum_text { get; set; }
        /// <summary>answer_origin -> qnum (JSON 배열 문자열)</summary>
        public string? origin_items { get; set; }
    }

    /// <summary>사용자 API 키 설정 (pro_login)</summary>
    public class AnalysisUserKeyInfo
    {
        public string? username { get; set; }
        public int? useapikey { get; set; }
        public string? groupposition { get; set; }
    }

    /// <summary>API 키 목록 (키 원문은 조회하지 않음 — 보안 S3)</summary>
    public class AnalysisApiKeyRow
    {
        public int rid { get; set; }
        public string? keycontent { get; set; }
        public string? keyhint { get; set; }
    }

    /// <summary>분석 저장 요청 모델</summary>
    public class AnalysisSaveRequest : AnalysisRequest
    {
        public string? duplicate_answer { get; set; }
        public string? lvcode { get; set; }
        public Newtonsoft.Json.Linq.JToken? data { get; set; }
    }

    /// <summary>워커 실행/중지 요청 모델</summary>
    public class WorkerStartRequest : AnalysisRequest
    {
        public string? jobId { get; set; }
        public string? action { get; set; } // analyze | translate | respondent | empty_etc | stop
        public string? classified { get; set; }
        public string? response { get; set; }
        public string? opencodeCategory { get; set; }
        public string? opencodeResponse { get; set; }
        public string? recallResponse { get; set; }
        public string? translateResponse { get; set; }
        public Newtonsoft.Json.Linq.JObject? settings { get; set; }
    }

    /// <summary>엑셀 업로드 미리보기 항목</summary>
    public class ExcelCodePreviewItem
    {
        public int rowNum { get; set; }
        public string lv1 { get; set; } = "";
        public string lv2 { get; set; } = "";
        public string lv3 { get; set; } = "";
        public string lv1code { get; set; } = "";
        public string lv2code { get; set; } = "";
        public string lv123code { get; set; } = "";
        public string ex_gubun { get; set; } = "자동분석";
        public bool is_lock { get; set; } = false;
        public string status { get; set; } = "신규"; // 신규, 유지, 수정, 오류
        public string message { get; set; } = "";
    }

    /// <summary>엑셀 업로드 미리보기 결과</summary>
    public class ExcelCodePreviewResult
    {
        public bool ok { get; set; }
        public int totalCount { get; set; }
        public int newCount { get; set; }
        public int updateCount { get; set; }
        public int keepCount { get; set; }
        public int errorCount { get; set; }
        public List<ExcelCodePreviewItem> items { get; set; } = new();
    }

    /// <summary>타 프로젝트 보기 불러오기 프로젝트/문항 항목</summary>
    public class AnalysisImportProjectItem
    {
        public string projectnum { get; set; } = "";
        public string qnum { get; set; } = "";
        public string projectname { get; set; } = "";
        public string projectpof { get; set; } = "";
        public string servername { get; set; } = "";
        public string insert_date { get; set; } = "";
        public int codeCount { get; set; }
    }

    /// <summary>문항 원본 데이터 (project_list 레거시 gb=11, 213 매핑용)</summary>
    public class AnalysisProjectOriginRow
    {
        public int id { get; set; }
        public string projectnum { get; set; } = "";
        public string servername { get; set; } = "";
        public string qnum { get; set; } = "";
        public string qnum_text { get; set; } = "";
        public string merge_pn { get; set; } = "";
        public string merge_qnum { get; set; } = "";
        public string answer_origin { get; set; } = "";
        public string? mapid { get; set; }
    }
}
