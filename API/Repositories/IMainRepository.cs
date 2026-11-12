using System.Data;
using Microsoft.AspNetCore.Components.Web;
using SurveyonApi.Models;

namespace SurveyonApi.Repositories
{
    public interface IMainRepository
    {
        Task<List<TestItemDto>> GetTestItemsAsync(CancellationToken ct = default);
        Task<int> AddTestItemAsync(TestItemDto item, CancellationToken ct = default);
        Task<ErrorCodeDto?> GetErrorCodeAsync(string code, string lang = "ko", CancellationToken ct = default);
        Task<DataTable> select_project_list_async(string user, int gb, string oid, string pn, string qnum = "", string search = "", CancellationToken ct = default);
        Task<DataTable> HdataSelectDataMapAsync(string pn , CancellationToken ct = default);
        Task<DataTable> select_surveyon_variables_async(string pn, CancellationToken ct = default);


        Task<DataTable> SelectProjectListSurveyToolAsync(string usertext, string search, string gubun, string connectionKey = "SqlServer",string year = "", CancellationToken ct = default);
        Task<DataTable> select_user_hpro(string usertext,string gubun, string connectionKey = "SqlServer",  CancellationToken ct = default);
        Task<DataTable> select_user_hpro_api(string usertext, string gubun, string connectionKey = "SqlServer", CancellationToken ct = default);
        Task<object> OpenEnterAsync(string gb, dynamic param, CancellationToken ct = default);
        Task<string> GetUserNameAsync(string userId, CancellationToken ct = default);
        Task<string> GetUserLoginKeyAsync(string userId, CancellationToken ct = default);
        Task<DataTable> select_project_enter_async(string pn, int gubun = 1, string search = "", CancellationToken ct = default);
        Task<DataTable> select_surveyon_projects_async(string pn, int gubun = 1, CancellationToken ct = default);
        Task<DataTable> HSRT_pages_SelectExAsync(string pn, string user, CancellationToken ct = default);
        Task<DataTable> HdataSelectExAsync(string id,  CancellationToken ct = default);
        Task<DataTable> HdataSelectExBatchAsync(IEnumerable<string> ids, CancellationToken ct = default);
        Task<DataTable> GetTAnswerCountAsync(string projectnum, string type, string gubun, string pid=null, CancellationToken ct = default);
        Task<DataTable> GetTAnswerCountHdataAsync(string projectnum, string type, string gubun, string pid=null, CancellationToken ct = default);
        Task<decimal> GetChargeCostAsync(string apigubun = "openai", CancellationToken ct = default);
        Task<bool> UpdateChargeCostAsync(decimal chargeCost, string apiGubun, CancellationToken ct = default);
        Task<(bool isValid, string showmenu, string projectname, string projectnum, string page_id, string page_title, string groupcode, string loginkey)> GetCustomerLoginInfoAsync(string cs,   CancellationToken ct = default);
        Task<DataTable> RpsQmSelectDataMapAsync(string pn, string servername, CancellationToken ct = default);
        Task<int> InsertProjectEnterAsync(string projectnum, string pof, string proname, string userid, string server, CancellationToken ct = default);
        Task<int> Insert_surveyon_projectsAsync(string projectnum, string pof, string proname, string userid, string server, string method, CancellationToken ct = default);
        Task<int> Insert_surveyon_variablesAsync(string projectnum,  CancellationToken ct = default);
        Task<int> InsertProjectPermissionAsync(string projectnum, string pof, string proname, string userid,string username, string groupposition, string usergroup= "������(����,�б�,����)", CancellationToken ct = default);
        Task<UserInfo> SelectProLoginAsync(string userid, CancellationToken ct = default);
        Task<int> UpdateTAnswerAsync(string projectnum, string type, string value, CancellationToken ct = default);
        Task<DataTable> RpsQmSelectExAsync(string qnumid, string servername, CancellationToken ct = default);
        Task<bool> InsertUpdatePnLbAsync(string projectnum, string qnumText, string jsonData, CancellationToken ct = default);
        Task<bool> InsertUpdatePnLbBatchAsync(List<(string projectnum, string qnum, string result_json)> items, CancellationToken ct = default);
        Task<bool> ProjectListInsertDbAsync(string projectnum, string projectname, string projectpof, string model, string qnum, string qnum_text, string question_fin, string question_origin, string user, string use_name, string load_fin, string answer, string servername, int mapid = 0, string merge_qnum = "", string merge_pn = "", CancellationToken ct = default);
        Task<bool> ProjectListInsertBatchAsync(List<ProjectListInsertItem> items, CancellationToken ct = default);
        Task<bool> DeleteProjectListQuestionsAsync(string projectnum, List<string>? qnums, List<int>? ids, CancellationToken ct = default);
        Task<(bool success, int addedQCount, int updatedQCount, int addedPidCount)> InsertOpenDataAsync(string projectnum, string server, string pof, string proname, string user, bool isIncremental = false, CancellationToken ct = default);
        Task<bool> SyncProjectListMapIdFromSurveyonVariablesAsync(string projectnum, CancellationToken ct = default);
        Task<DataTable> SelectProListInfoAsync(string userid, CancellationToken ct = default);
        Task<(string, string)> GetUserGroupAsync(string userid, string projectnum, CancellationToken ct = default);
        Task<bool> SelectUserCheckAsync(string user, string key, CancellationToken ct = default);
        Task<string?> SelectUserByLoginKeyAsync(string key, CancellationToken ct = default);
        Task<Dictionary<string, string>> GetBulkAnswerCountsAsync(IEnumerable<string> projectNums, string type = "4", CancellationToken ct = default);
        Task<Dictionary<string, (string usergroup, string position, string? topyn)>> GetBulkUserGroupsAsync(string userId, IEnumerable<string> projectNums, CancellationToken ct = default);
        Task<(bool success, string? newTopYn)> UpdateProjectTopYnAsync(string projectnum, string userId, string? topyn = null, CancellationToken ct = default);
        Task<Dictionary<string, ProjectStatusInfo>> GetBulkProjectStatusListAsync(IEnumerable<string> projectNums, CancellationToken ct = default);
        Task<Dictionary<string, string>> GetBulkUserNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default);
        Task<HashSet<string>> GetBulkSurveyonProjectPnsAsync(IEnumerable<string> projectNums, CancellationToken ct = default);
        Task<HashSet<string>> GetBulkHsrtPagePnsAsync(string userId, IEnumerable<string> projectNums, CancellationToken ct = default);
        Task<HashSet<string>> GetBulkExistingPermissionsAsync(IEnumerable<string> projectNums, CancellationToken ct = default);

        Task<DataTable> SelectProLoginDetailAsync(string userid, CancellationToken ct = default);
        Task<bool> CheckUserPasswordAsync(string userid, string encryptedPassword, CancellationToken ct = default);
        Task<int> InsertUserAsync(string id, string passwordAes, string position, string username, int groupcode, string? expiration = null, CancellationToken ct = default);
        Task<int> UpdateUserLoginKeyAsync(string userid, string loginkey, CancellationToken ct = default);



    // ── ProList (문항 목록) DB ──
    Task<DataTable> SelectProQuestionListAsync(string projectnum, string search = "", CancellationToken ct = default);
    Task<bool> UpdateQuestionAnalysisYnAsync(string projectnum, string qnum = "", string analysisYn = "0", bool isAll = false, CancellationToken ct = default);
    Task<bool> UpdateQuestionEditYnAsync(string projectnum, string qnum = "", string editYn = "0", bool isAll = false, CancellationToken ct = default);
    Task<bool> SaveQuestionMergeAsync(string projectnum, string jsonData, CancellationToken ct = default);
    Task<DataTable> SelectQuestionLabelsAsync(string projectnum, string mode = "excel", CancellationToken ct = default);
    Task<bool> ImportQuestionLabelsAllAsync(string projectnum, string user, string jsonLabels, CancellationToken ct = default);
    Task<DataTable> SelectProjectAnswersAllAsync(string projectnum, CancellationToken ct = default);
    Task<bool> SaveFilterSettingsAsync(string projectnum, string qnum, string filterDataJson, bool isAll = false, CancellationToken ct = default);
    Task<Newtonsoft.Json.Linq.JArray> GetFilterSelectQnumListAsync(string projectnum, string qnum, CancellationToken ct = default);
    Task<bool> UpdateFilterSingleAsync(string projectnum, string qnum, List<string> selectedVars, CancellationToken ct = default);
    Task<bool> UpdateFilterAllAsync(string projectnum, List<string> selectedVars, CancellationToken ct = default);
    Task<DataTable> SelectFilterQuestionListAsync(string projectnum, string qnum, CancellationToken ct = default);
    Task<int> GetSamplingCompleteCountAsync(string projectNums, string type = "4", string servername = "", CancellationToken ct = default);
    
    // ── ProList 배치 카운트/업데이트 (SelectQnumList 최적화) ──
    /// <summary>응답자수 배치 조회: project_list 테이블에서 merge_pn 기준으로 각 (projectnum, qnum)별 answer_origin 길이 반환</summary>
    Task<Dictionary<string, string>> SelectExceldataCountBatchAsync(string mergePn, CancellationToken ct = default);

    /// <summary>분석대상수 + 분석완료수 배치 조회: pn_in 테이블에서 merge_pn 기준 일괄 조회</summary>
    Task<Dictionary<string, (string originCount, string finCount)>> SelectPnInCountBatchAsync(string mergePn, CancellationToken ct = default);

    /// <summary>status 변경분 배치 업데이트: (id, status) 리스트를 한 번에 UPDATE</summary>
    Task UpdateListInfoBatchAsync(List<(string id, string status)> updates, CancellationToken ct = default);

    /// <summary>project_list 특정 id의 컬럼값 업데이트 (analysis/rowEdit/merge용)</summary>
    Task<bool> UpdateListInfoByIdAsync(string columnName, string columnVal, string qid, CancellationToken ct = default);

    /// <summary>project_list merge_pn 기준 일괄 컬럼 업데이트 (allAnalysis/allEdit용)</summary>
    Task<bool> UpdateListInfoByMergePnAsync(string columnName, string columnVal, string mergePn, CancellationToken ct = default);

    /// <summary>project_list merge_qnum 기준 묶음 그룹 일괄 컬럼 업데이트 (analysis/rowEdit용)</summary>
    Task<bool> UpdateListInfoByMergeQnumAsync(string columnName, string columnVal, string mergePn, string mergeQnum, CancellationToken ct = default);

    /// <summary>선택된 ids 및 merge_qnums 범위만 일괄 컬럼 업데이트 (하단 툴바 전용)</summary>
    Task<bool> BulkUpdateQuestionColumnAsync(string columnName, string columnVal, string mergePn, List<int> ids, List<string> mergeQnums, CancellationToken ct = default);

    /// <summary>선택된 문항들의 question_fin 일괄/개별 편집 업데이트</summary>
    Task<bool> BatchUpdateQuestionFinAsync(string mergePn, Dictionary<string, string> finMap, CancellationToken ct = default);

    /// <summary>조사DB(SqlServer_answer/SqlServerqm_answer) vs 설문온DB(Postgresqm) vs 오픈등록수 3원 비교 및 차이 PID 분석</summary>
    Task<(bool isManual, int surveyCount, int surveyonCount, int openRegisteredCount, int aiWaitCount, List<string> onlyInSurvey, List<string> onlyInSurveyon, List<string> unregisteredOpen, List<string> onlyInOpen)> GetCompletedPidDiffAsync(string projectnum, string servername = "", CancellationToken ct = default);

    /// <summary>project_list.answer_origin 컬럼에 등록된 고유(Distinct) PID 목록 및 개수 고속 조회</summary>
    Task<HashSet<string>> GetOpenRegisteredPidSetAsync(string projectnum, CancellationToken ct = default);
    Task<int> GetOpenRegisteredPidCountAsync(string projectnum, CancellationToken ct = default);

    /// <summary>보기추출(개발자용) JSON/데이터셋 조회</summary>
    Task<Newtonsoft.Json.Linq.JArray> GetExportLbExcelJsonAsync(string projectnum, CancellationToken ct = default);

    /// <summary>보기추출(DP용) STP 스크립트 텍스트 생성</summary>
    Task<string> GetExportLbDpScriptAsync(string projectnum, CancellationToken ct = default);

    /// <summary>보기추출(DP용) STP 스크립트 텍스트 및 프로젝트명 생성</summary>
    Task<(string scriptText, string projectName)> GetExportLbDpScriptWithProjectNameAsync(string projectnum, CancellationToken ct = default);

    /// <summary>응답추출(ALL) 데이터셋 조회</summary>
    Task<Newtonsoft.Json.Linq.JArray> GetExportDataAllJsonAsync(string projectnum, CancellationToken ct = default);

    /// <summary>응답추출(ALL) 엑셀 매트릭스 DataTable 조회</summary>
    Task<DataTable> GetExportDataAllTableAsync(string projectnum, CancellationToken ct = default);

    /// <summary>AI오픈분석 문항등록 DB탭 오픈변수 및 기존 등록상태/PID 현황 조회</summary>
    Task<Newtonsoft.Json.Linq.JArray> GetProRegisterDbSelectInfoAsync(string projectnum, string server, string user, CancellationToken ct = default);
    Task<DataTable> HdataSelectDataMapQnumAllAsync(string pn, CancellationToken ct = default);
    Task<Newtonsoft.Json.Linq.JArray> GetRpsAnswerDataAsync(string projectnum, string qnumvariable, CancellationToken ct = default);
    Task<Newtonsoft.Json.Linq.JArray> GetRpsAnswerDataHdataAsync(string projectnum, string qnumvariable, CancellationToken ct = default);

    /// <summary>T_Answer 테이블에서 오픈데이터 파싱에 필요한 pid, originalData 컬럼만 정렬 없이 고속 스트리밍 조회</summary>
    Task<List<(string pid, string jsonData)>> GetTAnswerOpenDataFastAsync(string projectnum, string servername = "rps", CancellationToken ct = default);

    /// <summary>HData surveyon_answer 테이블에서 오픈데이터 파싱에 필요한 pid, response_data 컬럼만 고속 스트리밍 조회 (SPSS 지원)</summary>
    Task<List<(string pid, string jsonData)>> GetSurveyonAnswerOpenDataFastAsync(string projectnum, HashSet<string> openVariableSet, CancellationToken ct = default);

    /// <summary>한큐분석 전용: 해당 프로젝트의 범용 옵션(option_json_all) 조회</summary>
    Task<string?> GetOptionJsonAllAsync(string projectnum, CancellationToken ct = default);

    /// <summary>한큐분석 전용: 해당 프로젝트의 모든 문항 행에 범용 옵션(option_json_all) 일괄 저장</summary>
    Task<bool> SaveOptionJsonAllAsync(string projectnum, string optionJsonAll, string user, CancellationToken ct = default);

    /// <summary>T_DataMapExample 테이블에서 여러 dataMapID의 보기 예시를 단 1회 배치 쿼리로 조회</summary>
    Task<DataTable> RpsQmSelectExBatchAsync(IEnumerable<string> ids, string servername = "qm", CancellationToken ct = default);
    }
}

