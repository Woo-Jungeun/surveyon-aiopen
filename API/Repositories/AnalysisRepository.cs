using System.Data;
using Npgsql;
using SurveyonApi.Models;
using SurveyonApi.Services;

namespace SurveyonApi.Repositories
{
    /// <summary>
    /// AI오픈분석 › 분석 페이지 전용 HOpen/QM 조회 저장소
    ///  - MainRepository(6,000+ 줄)와 분리
    ///  - 레거시 SELECT * 대신 필요한 컬럼만 조회 (성능 P2)
    ///  - 대용량 jsonb(answer_origin)는 해당 문항 키만 서버에서 추출
    /// </summary>
    public interface IAnalysisRepository
    {
        Task<AnalysisQuestionMeta?> GetQuestionMetaAsync(string pn, string qnum, CancellationToken ct = default);
        Task<string> GetLvcodeAsync(string pn, string qnum, CancellationToken ct = default);
        Task<int> UpdateLvcodeAsync(string pn, string qnum, string lvcode, CancellationToken ct = default);
        Task<(string pythonStatus, string popupCheck)?> GetStatusByQidAsync(int qid, CancellationToken ct = default);
        Task<int> ClearPopupCheckAsync(int qid, CancellationToken ct = default);
        Task<bool> HasQnumJoinAsync(int qid, CancellationToken ct = default);
        Task<AnalysisUserKeyInfo?> GetUserKeyInfoAsync(string user, CancellationToken ct = default);
        Task<List<AnalysisApiKeyRow>> GetApiKeyListAsync(string user, string groupposition, CancellationToken ct = default);
        Task<bool> ApiKeyExistsAsync(int rid, CancellationToken ct = default);
        Task<DataTable> GetCountryCodesAsync(CancellationToken ct = default);
        Task<DataTable> GetModelCodesAsync(CancellationToken ct = default);
        Task<AnalysisPnInRow?> GetPnInAsync(string pn, string qnum, CancellationToken ct = default);
        Task<AnalysisPnLbRow?> GetPnLbAsync(string pn, string qnum, CancellationToken ct = default);
        Task<List<AnalysisSourceRow>> GetSourceRowsAsync(string pn, string qnum, CancellationToken ct = default);
        Task<Dictionary<string, string>> GetFirstWriteDatesAsync(string pn, CancellationToken ct = default);
        Task<int> SaveOptionJsonAsync(string pn, string qnum, string optionJson, string? questionFin, CancellationToken ct = default);
        Task<int> SaveOptionJsonFullAsync(string pn, string qnum, string optionJson, string questionFin, int listapikey, string user, string username, string usergroup, CancellationToken ct = default);
        Task<(string secretKey, int keyId)> GetResolvedApiKeySecretAsync(int? apikeyid, string user, CancellationToken ct = default);
        Task<(string username, string usergroup)> GetUserAndGroupAsync(string user, string projectnum, CancellationToken ct = default);
        Task<int> SavePnLbAsync(string pn, string qnum, string resultJson, CancellationToken ct = default);
        Task<int> SavePnInAsync(string pn, string qnum, string answerFin, CancellationToken ct = default);
        Task<int> UpdateDuplicateRemoveDateAsync(int qid, string? date, CancellationToken ct = default);
        Task<string> GetDkCodeAsync(string pn, string qnum, CancellationToken ct = default);
        Task<List<AnalysisImportProjectItem>> SearchProjectsForImportAsync(string search, CancellationToken ct = default);
        Task<List<string>> GetRelatedQnumsAsync(string pn, string qnum, CancellationToken ct = default);
        Task<int?> GetHDataVariableIdAsync(string pn, string qnum, CancellationToken ct = default);
        Task<int> SyncCodesToHDataAsync(string pn, string qnum, Newtonsoft.Json.Linq.JArray codesArray, CancellationToken ct = default);
        Task<int> SyncHDataAnswerAndVariablesAsync(string pn, string mergeQnum, List<string> subQnums, Dictionary<string, Dictionary<string, (string code, string sentiment)>> pidSubQnumResults, CancellationToken ct = default);
        Task<List<AnalysisProjectOriginRow>> GetProjectOriginRowsAsync(string pn, string qnum, bool isDuplicateRemove, CancellationToken ct = default);
        Task<string?> GetPnInAnswerOriginAsync(string pn, string qnum, CancellationToken ct = default);
    }

    public class AnalysisRepository : IAnalysisRepository
    {
        private const string HOpen = "Postgres";
        private const string QMaster = "Postgresqm";
        private readonly IPostgreLib _db;
        private readonly ILogger<AnalysisRepository> _logger;

        public AnalysisRepository(IPostgreLib db, ILogger<AnalysisRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        // 레거시 select_project_list(gb=111) → 분석 대표행 1건
        public async Task<AnalysisQuestionMeta?> GetQuestionMetaAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sqlStrict = @"SELECT id, option_json::text AS option_json, servername, projectname, projectpof,
                                        question_fin, listapikey, lvcode, duplicate_remove_date, load_fin, merge_pn, merge_qnum, qnum, qnum_text
                                 FROM public.project_list
                                 WHERE (merge_qnum = @qnum OR qnum = @qnum) 
                                   AND (merge_pn = @pn OR projectnum = @pn) 
                                   AND useyn = '분석'
                                 ORDER BY qnum_text, id/1 DESC
                                 LIMIT 1";
            var meta = await _db.QueryFirstOrDefaultAsync<AnalysisQuestionMeta>(sqlStrict, new { pn, qnum }, HOpen, ct);
            if (meta != null)
            {
                _logger.LogInformation("[GetQuestionMetaAsync] Strict match id={Id} pn={Pn} qnum={Qnum}", meta.id, pn, qnum);
                return meta;
            }

            const string sqlFallback = @"SELECT id, option_json::text AS option_json, servername, projectname, projectpof,
                                         question_fin, listapikey, lvcode, duplicate_remove_date, load_fin, merge_pn, merge_qnum, qnum, qnum_text
                                  FROM public.project_list
                                  WHERE (merge_qnum = @qnum OR qnum = @qnum) 
                                    AND (merge_pn = @pn OR projectnum = @pn)
                                  ORDER BY id/1 DESC
                                  LIMIT 1";
            meta = await _db.QueryFirstOrDefaultAsync<AnalysisQuestionMeta>(sqlFallback, new { pn, qnum }, HOpen, ct);
            if (meta != null)
            {
                _logger.LogInformation("[GetQuestionMetaAsync] Fallback match id={Id} pn={Pn} qnum={Qnum}", meta.id, pn, qnum);
                return meta;
            }

            // 3차: qnum_text 매칭 또는 merge_qnum 접두사 매칭 (예: qnum이 'A1'인데 DB에 'A1/A1-1_R1'로 등록된 경우)
            const string sqlTextMatch = @"SELECT id, option_json::text AS option_json, servername, projectname, projectpof,
                                                 question_fin, listapikey, lvcode, duplicate_remove_date, load_fin, merge_pn, merge_qnum, qnum, qnum_text
                                          FROM public.project_list
                                          WHERE (merge_pn = @pn OR projectnum = @pn)
                                            AND (qnum_text = @qnum OR merge_qnum LIKE (@qnum || '/%') OR merge_qnum LIKE (@qnum || '_%'))
                                          ORDER BY id/1 DESC
                                          LIMIT 1";
            meta = await _db.QueryFirstOrDefaultAsync<AnalysisQuestionMeta>(sqlTextMatch, new { pn, qnum }, HOpen, ct);
            if (meta != null)
            {
                _logger.LogInformation("[GetQuestionMetaAsync] TextMatch match id={Id} pn={Pn} qnum={Qnum}", meta.id, pn, qnum);
                return meta;
            }

            // 디버그용: pn에 해당하는 실제 문항 행들을 로그에 기록
            try
            {
                var candidates = await _db.QueryAsync<dynamic>(
                    "SELECT id, projectnum, merge_pn, qnum, merge_qnum, useyn FROM public.project_list WHERE projectnum = @pn OR merge_pn = @pn LIMIT 10",
                    new { pn }, HOpen, ct);
                _logger.LogWarning("[GetQuestionMetaAsync] No match for pn={Pn} qnum={Qnum}. Available candidates in project_list: {@Candidates}",
                    pn, qnum, candidates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetQuestionMetaAsync] Failed to query candidates for pn={Pn}", pn);
            }

            return null;
        }

        // 레거시 selectLvcode (select_project_list gb=112)
        public async Task<string> GetLvcodeAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = @"SELECT lvcode FROM public.project_list
                                 WHERE merge_qnum = @qnum AND merge_pn = @pn AND useyn = '분석' AND lvcode IS NOT NULL
                                 ORDER BY lvcode DESC
                                 LIMIT 1";
            return (await _db.QueryFirstOrDefaultAsync<string>(sql, new { pn, qnum }, HOpen, ct))?.TrimEnd() ?? "";
        }

        // 레거시 update_list_info("lvcode", lvcode, qid)
        public async Task<int> UpdateLvcodeAsync(string pn, string qnum, string lvcode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(lvcode)) return 0;
            const string sql = @"UPDATE public.project_list 
                                 SET lvcode = @lvcode 
                                 WHERE merge_pn = @pn AND merge_qnum = @qnum AND useyn = '분석'";
            return await _db.ExecuteAsync(sql, new { pn, qnum, lvcode = lvcode.Trim() }, HOpen, ct);
        }

        // 레거시 select_project_list_pythonstatus
        public async Task<(string pythonStatus, string popupCheck)?> GetStatusByQidAsync(int qid, CancellationToken ct = default)
        {
            const string sql = "SELECT python_status, popup_check FROM public.project_list WHERE id = @qid";
            var dt = await _db.ExecuteQueryAsync(sql, new List<NpgsqlParameter> { new("@qid", qid) }, HOpen, ct);
            if (dt.Rows.Count == 0) return null;
            var r = dt.Rows[0];
            return (r["python_status"]?.ToString()?.TrimEnd() ?? "", r["popup_check"]?.ToString()?.TrimEnd() ?? "");
        }

        // 레거시 update_list_info("popup_check", "", qid) → 빈 값은 NULL 저장 (동작 동일)
        public Task<int> ClearPopupCheckAsync(int qid, CancellationToken ct = default)
        {
            const string sql = "UPDATE public.project_list SET popup_check = NULL WHERE id = @qid";
            return _db.ExecuteAsync(sql, new { qid }, HOpen, ct);
        }

        // 레거시 Displayinfodb.Getqnumjoin(qid).Count > 0 (필터문항 설정 여부)
        public async Task<bool> HasQnumJoinAsync(int qid, CancellationToken ct = default)
        {
            const string sql = @"SELECT EXISTS (
                                    SELECT 1 FROM public.project_list
                                    WHERE id = @qid AND (
                                        (qnum_join1 IS NOT NULL AND qnum_join1::text NOT IN ('{}','[]','null')) OR
                                        (qnum_join2 IS NOT NULL AND qnum_join2::text NOT IN ('{}','[]','null')) OR
                                        (qnum_join3 IS NOT NULL AND qnum_join3::text NOT IN ('{}','[]','null')) OR
                                        (qnum_join4 IS NOT NULL AND qnum_join4::text NOT IN ('{}','[]','null')) OR
                                        (qnum_join5 IS NOT NULL AND qnum_join5::text NOT IN ('{}','[]','null'))))";
            return await _db.ExecuteScalarAsync<bool>(sql, new { qid }, HOpen, ct);
        }

        // 레거시 Displayinfodb.GetUserName(user).Item2(useapikey) + Getposition(user)
        public Task<AnalysisUserKeyInfo?> GetUserKeyInfoAsync(string user, CancellationToken ct = default)
        {
            const string sql = "SELECT username, useapikey, groupposition FROM public.pro_login WHERE userid = @user LIMIT 1";
            return _db.QueryFirstOrDefaultAsync<AnalysisUserKeyInfo>(sql, new { user }, HOpen, ct);
        }

        // 레거시 select_api(user, "0") — 키 원문(key) 컬럼은 조회하지 않음 (보안 S3)
        public async Task<List<AnalysisApiKeyRow>> GetApiKeyListAsync(string user, string groupposition, CancellationToken ct = default)
        {
            const string sql = @"SELECT rid, keycontent, keyhint FROM public.project_security
                                 WHERE username = @user OR username = 'hrc' OR groupposition = @groupposition
                                 ORDER BY id/1";
            return (await _db.QueryAsync<AnalysisApiKeyRow>(sql, new { user, groupposition }, HOpen, ct)).ToList();
        }

        // 레거시 Displayinfodb.GetTokenkey(user, rid).Item2 → rid 존재 여부만 확인 (키 원문 미조회)
        public async Task<bool> ApiKeyExistsAsync(int rid, CancellationToken ct = default)
        {
            const string sql = "SELECT EXISTS (SELECT 1 FROM public.project_security WHERE rid = @rid)";
            return await _db.ExecuteScalarAsync<bool>(sql, new { rid }, HOpen, ct);
        }

        // 레거시 selectcountryCode
        public Task<DataTable> GetCountryCodesAsync(CancellationToken ct = default)
            => _db.ExecuteQueryAsync("SELECT id, lang, name FROM public.project_countrycode ORDER BY id/1", null, HOpen, ct);

        // 레거시 selectmodelCode
        public Task<DataTable> GetModelCodesAsync(CancellationToken ct = default)
            => _db.ExecuteQueryAsync("SELECT id, modelvalue, modelname FROM public.project_model WHERE modelyn = 'y' ORDER BY id/1", null, HOpen, ct);

        // 레거시 select_pn_in — answer_origin은 해당 문항 키 배열만 추출
        public Task<AnalysisPnInRow?> GetPnInAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = @"SELECT input_date, update_date,
                                        (answer_origin -> @qnum)::text AS origin_items,
                                        answer_fin::text AS answer_fin
                                 FROM public.pn_in
                                 WHERE projectnum = @pn AND qnum = @qnum
                                 LIMIT 1";
            return _db.QueryFirstOrDefaultAsync<AnalysisPnInRow>(sql, new { pn, qnum }, HOpen, ct);
        }

        // 레거시 select_pn_lb
        public Task<AnalysisPnLbRow?> GetPnLbAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = @"SELECT result_json::text AS result_json, insert_date, update_date
                                 FROM public.pn_lb
                                 WHERE projectnum = @pn AND qnum = @qnum
                                 LIMIT 1";
            return _db.QueryFirstOrDefaultAsync<AnalysisPnLbRow>(sql, new { pn, qnum }, HOpen, ct);
        }

        // 레거시 select_project_list(gb=11) — 각 행의 자기 문항 키 배열만 추출
        public async Task<List<AnalysisSourceRow>> GetSourceRowsAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = @"SELECT projectnum, qnum, servername, qnum_text,
                                        (answer_origin -> qnum)::text AS origin_items
                                 FROM public.project_list
                                 WHERE merge_qnum = @qnum AND merge_pn = @pn AND useyn = '분석'
                                 ORDER BY mapid/1";
            return (await _db.QueryAsync<AnalysisSourceRow>(sql, new { pn, qnum }, HOpen, ct)).ToList();
        }

        // 레거시 qmselect_tanswer(pn, "4", "12") — PID별 최초 응답일 (QM)
        public async Task<Dictionary<string, string>> GetFirstWriteDatesAsync(string pn, CancellationToken ct = default)
        {
            const string sql = @"SELECT DISTINCT ON (pid) TRIM(pid) AS pid, CAST(""firstWriteDateTime"" AS text) AS firstwrite
                                 FROM ""T_Answer""
                                 WHERE ""questionnaireNumber"" = @pn AND ""answerStateCode"" = 4
                                 ORDER BY pid, ""firstWriteDateTime"" ASC";
            var dt = await _db.ExecuteQueryAsync(sql, new List<NpgsqlParameter> { new("@pn", pn) }, QMaster, ct);
            var dict = new Dictionary<string, string>();
            foreach (DataRow r in dt.Rows)
            {
                string pid = r["pid"]?.ToString()?.TrimEnd() ?? "";
                if (!dict.ContainsKey(pid)) dict[pid] = r["firstwrite"]?.ToString()?.TrimEnd() ?? "";
            }
            return dict;
        }

        public async Task<int> SaveOptionJsonAsync(string pn, string qnum, string optionJson, string? questionFin, CancellationToken ct = default)
        {
            string sql = string.IsNullOrEmpty(questionFin)
                ? @"UPDATE public.project_list
                    SET option_json = @optionJson::jsonb
                    WHERE (merge_pn = @pn OR projectnum = @pn) AND (merge_qnum = @qnum OR qnum = @qnum) AND useyn = '분석'"
                : @"UPDATE public.project_list
                    SET option_json = @optionJson::jsonb, question_fin = @questionFin
                    WHERE (merge_pn = @pn OR projectnum = @pn) AND (merge_qnum = @qnum OR qnum = @qnum) AND useyn = '분석'";
            return await _db.ExecuteAsync(sql, new { pn, qnum, optionJson, questionFin }, HOpen, ct);
        }

        public async Task<int> SaveOptionJsonFullAsync(
            string pn,
            string qnum,
            string optionJson,
            string questionFin,
            int listapikey,
            string user,
            string username,
            string usergroup,
            CancellationToken ct = default)
        {
            string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            const string sql = @"UPDATE public.project_list
                                 SET option_json = @optionJson::jsonb,
                                     question_fin = @questionFin,
                                     listapikey = @listapikey,
                                     update_date = @datetime,
                                     use_id = @user,
                                     use_name = @username,
                                     use_group = @usergroup
                                 WHERE (merge_pn = @pn OR projectnum = @pn)
                                   AND (merge_qnum = @qnum OR qnum = @qnum)
                                   AND useyn = '분석'";

            int affected = await _db.ExecuteAsync(sql, new
            {
                pn,
                qnum,
                optionJson,
                questionFin,
                listapikey,
                datetime,
                user,
                username,
                usergroup
            }, HOpen, ct);

            if (affected == 0)
            {
                const string sqlFallback = @"UPDATE public.project_list
                                             SET option_json = @optionJson::jsonb,
                                                 question_fin = @questionFin,
                                                 listapikey = @listapikey,
                                                 update_date = @datetime,
                                                 use_id = @user,
                                                 use_name = @username,
                                                 use_group = @usergroup
                                             WHERE (merge_pn = @pn OR projectnum = @pn)
                                               AND (merge_qnum = @qnum OR qnum = @qnum)";
                affected = await _db.ExecuteAsync(sqlFallback, new
                {
                    pn,
                    qnum,
                    optionJson,
                    questionFin,
                    listapikey,
                    datetime,
                    user,
                    username,
                    usergroup
                }, HOpen, ct);
            }

            return affected;
        }

        public async Task<(string secretKey, int keyId)> GetResolvedApiKeySecretAsync(int? apikeyid, string user, CancellationToken ct = default)
        {
            // 1. 전달받은 apikeyid가 유효할 경우
            if (apikeyid.HasValue && apikeyid.Value > 0)
            {
                const string sql = "SELECT rid, key FROM public.project_security WHERE rid = @rid LIMIT 1";
                var dt = await _db.ExecuteQueryAsync(sql, new List<NpgsqlParameter> { new("@rid", apikeyid.Value) }, HOpen, ct);
                if (dt.Rows.Count > 0)
                {
                    string rawKey = dt.Rows[0]["key"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(rawKey))
                    {
                        return (DecryptApiKey(rawKey), apikeyid.Value);
                    }
                }
            }

            // 2. 사용자 기본 키 (pro_login.useapikey)
            const string sqlUser = "SELECT useapikey FROM public.pro_login WHERE userid = @user LIMIT 1";
            var userKeyDt = await _db.ExecuteQueryAsync(sqlUser, new List<NpgsqlParameter> { new("@user", user) }, HOpen, ct);
            if (userKeyDt.Rows.Count > 0 && int.TryParse(userKeyDt.Rows[0]["useapikey"]?.ToString(), out int uKeyId) && uKeyId > 0)
            {
                const string sql = "SELECT rid, key FROM public.project_security WHERE rid = @rid LIMIT 1";
                var dt = await _db.ExecuteQueryAsync(sql, new List<NpgsqlParameter> { new("@rid", uKeyId) }, HOpen, ct);
                if (dt.Rows.Count > 0)
                {
                    string rawKey = dt.Rows[0]["key"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(rawKey))
                    {
                        return (DecryptApiKey(rawKey), uKeyId);
                    }
                }
            }

            // 3. 회사 공용 키 (username = 'hrc')
            const string sqlHrc = "SELECT rid, key FROM public.project_security WHERE username = 'hrc' AND key IS NOT NULL AND key != '' ORDER BY id/1 LIMIT 1";
            var hrcDt = await _db.ExecuteQueryAsync(sqlHrc, null, HOpen, ct);
            if (hrcDt.Rows.Count > 0)
            {
                int rid = int.TryParse(hrcDt.Rows[0]["rid"]?.ToString(), out int parsedRid) ? parsedRid : 28;
                string rawKey = hrcDt.Rows[0]["key"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(rawKey))
                {
                    return (DecryptApiKey(rawKey), rid);
                }
            }

            return ("", 0);
        }

        private static string DecryptApiKey(string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey)) return "";
            if (rawKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)) return rawKey;

            try
            {
                const string aesKey = "cGZeWyxlli5N4FRSwMAsbbRoNzJEhFko";
                const string aesIv = "cGZeWyxlli5N4FRS";
                string decrypted = AES256.Crypto.decryptAES256(rawKey, aesKey, aesIv);
                return string.IsNullOrWhiteSpace(decrypted) ? rawKey : decrypted;
            }
            catch
            {
                return rawKey;
            }
        }

        public async Task<(string username, string usergroup)> GetUserAndGroupAsync(string user, string projectnum, CancellationToken ct = default)
        {
            try
            {
                const string sqlUser = "SELECT username FROM public.pro_login WHERE userid = @user LIMIT 1";
                var dtUser = await _db.ExecuteQueryAsync(sqlUser, new List<NpgsqlParameter> { new("@user", user) }, HOpen, ct);
                string uname = dtUser.Rows.Count > 0 ? (dtUser.Rows[0]["username"]?.ToString()?.Trim() ?? "") : "";

                const string sqlGroup = "SELECT use_group FROM public.project_list WHERE projectnum = @pn OR merge_pn = @pn LIMIT 1";
                var dtGroup = await _db.ExecuteQueryAsync(sqlGroup, new List<NpgsqlParameter> { new("@pn", projectnum) }, HOpen, ct);
                string ugroup = dtGroup.Rows.Count > 0 ? (dtGroup.Rows[0]["use_group"]?.ToString()?.Trim() ?? "") : "";

                return (uname, ugroup);
            }
            catch
            {
                return ("", "");
            }
        }

        public Task<int> SavePnLbAsync(string pn, string qnum, string resultJson, CancellationToken ct = default)
        {
            const string sql = @"INSERT INTO public.pn_lb (projectnum, qnum, insert_date, result_json, result_json_before)
                                 VALUES (@pn, @qnum, @inputDate, @resultJson::jsonb, @resultJson::jsonb)
                                 ON CONFLICT (projectnum, qnum) DO UPDATE SET
                                     result_json_before = COALESCE(pn_lb.result_json, @resultJson::jsonb),
                                     result_json = @resultJson::jsonb,
                                     update_date = @inputDate";
            string inputDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return _db.ExecuteAsync(sql, new { pn, qnum, resultJson, inputDate }, HOpen, ct);
        }

        public Task<int> SavePnInAsync(string pn, string qnum, string answerFin, CancellationToken ct = default)
        {
            const string sql = @"UPDATE public.pn_in
                                 SET answer_fin = @answerFin::jsonb, update_date = @inputDate
                                 WHERE projectnum = @pn AND qnum = @qnum";
            string inputDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return _db.ExecuteAsync(sql, new { pn, qnum, answerFin, inputDate }, HOpen, ct);
        }

        public Task<int> UpdateDuplicateRemoveDateAsync(int qid, string? date, CancellationToken ct = default)
        {
            const string sql = @"UPDATE public.project_list
                                 SET duplicate_remove_date = @date
                                 WHERE id = @qid";
            return _db.ExecuteAsync(sql, new { qid, date }, HOpen, ct);
        }

        public async Task<string> GetDkCodeAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var lbRow = await GetPnLbAsync(pn, qnum, ct);
            if (lbRow == null || string.IsNullOrWhiteSpace(lbRow.result_json)) return "99";
            try
            {
                var arr = Newtonsoft.Json.Linq.JArray.Parse(lbRow.result_json);
                foreach (var item in arr)
                {
                    string lv3 = item["lv3"]?.ToString()?.TrimEnd() ?? "";
                    string code = item["lv123code"]?.ToString()?.TrimEnd() ?? "";
                    if (lv3.Contains("모름") || lv3.Contains("없음") || lv3.Contains("무응답"))
                    {
                        if (!string.IsNullOrEmpty(code)) return code;
                    }
                }
            }
            catch { }
            return "99";
        }

        public async Task<List<AnalysisImportProjectItem>> SearchProjectsForImportAsync(string search, CancellationToken ct = default)
        {
            search = (search ?? "").Trim();
            string sql = string.IsNullOrEmpty(search)
                ? @"SELECT t1.projectnum, t1.qnum,
                           COALESCE(t2.projectname, '') AS projectname,
                           COALESCE(t2.projectpof, '') AS projectpof,
                           COALESCE(t2.servername, '') AS servername,
                           COALESCE(t1.insert_date, '') AS insert_date,
                           jsonb_array_length(CASE WHEN t1.result_json::text ~ '^\s*\[' THEN t1.result_json ELSE '[]'::jsonb END) AS codeCount
                    FROM public.pn_lb t1
                    LEFT JOIN public.project_enter t2 ON t1.projectnum = t2.projectnum
                    WHERE t1.result_json IS NOT NULL AND t1.result_json::text NOT IN ('', '[]', 'null')
                    ORDER BY t1.insert_date DESC, t1.projectnum, t1.qnum
                    LIMIT 80"
                : @"SELECT t1.projectnum, t1.qnum,
                           COALESCE(t2.projectname, '') AS projectname,
                           COALESCE(t2.projectpof, '') AS projectpof,
                           COALESCE(t2.servername, '') AS servername,
                           COALESCE(t1.insert_date, '') AS insert_date,
                           jsonb_array_length(CASE WHEN t1.result_json::text ~ '^\s*\[' THEN t1.result_json ELSE '[]'::jsonb END) AS codeCount
                    FROM public.pn_lb t1
                    LEFT JOIN public.project_enter t2 ON t1.projectnum = t2.projectnum
                    WHERE t1.result_json IS NOT NULL AND t1.result_json::text NOT IN ('', '[]', 'null')
                      AND (t1.projectnum ILIKE '%' || @search || '%'
                           OR t1.qnum ILIKE '%' || @search || '%'
                           OR t2.projectname ILIKE '%' || @search || '%'
                           OR t2.projectpof ILIKE '%' || @search || '%')
                    ORDER BY t1.insert_date DESC, t1.projectnum, t1.qnum
                    LIMIT 80";

            var rows = await _db.QueryAsync<AnalysisImportProjectItem>(sql, new { search }, HOpen, ct);
            return rows.ToList();
        }

        public async Task<List<string>> GetRelatedQnumsAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = @"SELECT DISTINCT qnum 
                                 FROM public.project_list 
                                 WHERE (merge_pn = @pn OR projectnum = @pn)
                                   AND (merge_qnum = @qnum OR qnum = @qnum OR qnum_text = @qnum)
                                 ORDER BY qnum";
            var rows = await _db.QueryAsync<string>(sql, new { pn, qnum }, HOpen, ct);
            var list = rows.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (!list.Contains(qnum)) list.Insert(0, qnum);
            return list;
        }

        public async Task<int?> GetHDataVariableIdAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = @"SELECT id FROM public.surveyon_variables 
                                 WHERE pn = @pn 
                                   AND (c_question_variable = @qnum OR open_rule LIKE '%' || @qnum || '%' OR s_question_variable = @qnum)
                                 ORDER BY (CASE WHEN c_question_variable = @qnum THEN 0 ELSE 1 END), id
                                 LIMIT 1";
            return await _db.QueryFirstOrDefaultAsync<int?>(sql, new { pn, qnum }, "Postgreshdata", ct);
        }

        public async Task<int> SyncCodesToHDataAsync(string pn, string qnum, Newtonsoft.Json.Linq.JArray codesArray, CancellationToken ct = default)
        {
            try
            {
                // 1. 머지/단일 문항에 연결된 모든 하위 qnum 확인
                var relatedQnums = await GetRelatedQnumsAsync(pn, qnum, ct);
                int totalUpdated = 0;

                foreach (var qqnum in relatedQnums)
                {
                    int? varId = await GetHDataVariableIdAsync(pn, qqnum, ct);
                    if (!varId.HasValue) continue;

                    var activeCodes = new List<string>();

                    // 2. 보기 일괄 UPSERT (대, 중, 소분류 컬럼 포함)
                    const string upsertSql = @"
                        WITH up AS (
                            UPDATE public.surveyon_labels
                            SET label = @label,
                                ranking = @ranking,
                                lv1code = @lv1code,
                                lv1 = @lv1,
                                lv2code = @lv2code,
                                lv2 = @lv2,
                                updated_at = NOW()
                            WHERE variable_id = @varId
                              AND pn = @pn
                              AND code = @code
                            RETURNING *
                        )
                        INSERT INTO public.surveyon_labels
                        (variable_id, pn, code, ranking, label, lv1code, lv1, lv2code, lv2, created_at, updated_at)
                        SELECT
                            @varId,
                            @pn,
                            @code,
                            @ranking,
                            @label,
                            @lv1code,
                            @lv1,
                            @lv2code,
                            @lv2,
                            NOW(),
                            NOW()
                        WHERE NOT EXISTS (SELECT 1 FROM up)";

                    foreach (var token in codesArray)
                    {
                        if (token is not Newtonsoft.Json.Linq.JObject item) continue;
                        string code = item["lv123code"]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(code)) continue;

                        string label = item["lv3"]?.ToString()?.Trim() ?? "";
                        string lv1code = item["lv1code"]?.ToString()?.Trim() ?? "";
                        string lv1 = item["lv1"]?.ToString()?.Trim() ?? "";
                        string lv2code = item["lv2code"]?.ToString()?.Trim() ?? "";
                        string lv2 = item["lv2"]?.ToString()?.Trim() ?? "";
                        int ranking = int.TryParse(code, out int r) ? r : 0;

                        activeCodes.Add(code);

                        totalUpdated += await _db.ExecuteAsync(upsertSql, new
                        {
                            varId = varId.Value,
                            pn,
                            code,
                            ranking,
                            label,
                            lv1code,
                            lv1,
                            lv2code,
                            lv2
                        }, "Postgreshdata", ct);
                    }

                    // 3. 사용자가 삭제한 보기 정리: 이번 activeCodes에 없는 코드는 surveyon_labels에서도 삭제
                    if (activeCodes.Count > 0)
                    {
                        const string deleteSql = @"DELETE FROM public.surveyon_labels
                                                  WHERE variable_id = @varId
                                                    AND pn = @pn
                                                    AND code != ALL(@activeCodes)";
                        await _db.ExecuteAsync(deleteSql, new { varId = varId.Value, pn, activeCodes = activeCodes.ToArray() }, "Postgreshdata", ct);
                    }
                }

                _logger.LogInformation("[SyncCodesToHDataAsync] HData surveyon_labels 동기화 완료 pn={Pn} qnum={Qnum} count={Count}", pn, qnum, totalUpdated);
                return totalUpdated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SyncCodesToHDataAsync] HData surveyon_labels 동기화 중 오류 pn={Pn} qnum={Qnum}", pn, qnum);
                return 0;
            }
        }

        public async Task<int> SyncHDataAnswerAndVariablesAsync(
            string pn,
            string mergeQnum,
            List<string> subQnums,
            Dictionary<string, Dictionary<string, (string code, string sentiment)>> pidSubQnumResults,
            CancellationToken ct = default)
        {
            if (pidSubQnumResults == null || pidSubQnumResults.Count == 0) return 0;

            try
            {
                int updatedCount = 0;
                var removeKeyList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(mergeQnum)) removeKeyList.Add(mergeQnum);
                if (subQnums != null)
                {
                    foreach (var sq in subQnums)
                    {
                        if (!string.IsNullOrWhiteSpace(sq)) removeKeyList.Add(sq);
                    }
                }
                string[] keysToRemove = removeKeyList.ToArray();

                // 1. surveyon_answer 업데이트 (과거 잘못 들어간 mergeQnum 및 대상 하위 qnum 키 삭제 후 신규 JSON 병합)
                const string updateAnswerSql = @"
                    UPDATE public.surveyon_answer
                    SET ai_open_data = ((COALESCE(NULLIF(ai_open_data, ''), '{}')::jsonb - CAST(@keysToRemove AS text[])) || CAST(@jsonPayload AS jsonb))::text,
                        ai_open_updated_at = NOW()
                    WHERE pn = @pn AND pid = @pid";

                foreach (var chunk in pidSubQnumResults.Chunk(100))
                {
                    foreach (var kvp in chunk)
                    {
                        string pid = kvp.Key;
                        var subQnumMap = kvp.Value;

                        var data = new Dictionary<string, object>();
                        foreach (var (subQ, (code, sentiment)) in subQnumMap)
                        {
                            data[subQ] = new Dictionary<string, string>
                            {
                                ["@CODE"] = code,
                                ["@sentiment"] = sentiment
                            };
                        }
                        string jsonPayload = System.Text.Json.JsonSerializer.Serialize(data);

                        updatedCount += await _db.ExecuteAsync(updateAnswerSql, new
                        {
                            pn,
                            pid,
                            keysToRemove,
                            jsonPayload
                        }, "Postgreshdata", ct);
                    }
                }

                // 2. surveyon_variables.is_ai_opened 플래그 활성화 (모든 하위 qnum 대상)
                var targets = (subQnums != null && subQnums.Count > 0)
                    ? subQnums
                    : (!string.IsNullOrWhiteSpace(mergeQnum) ? new List<string> { mergeQnum } : new List<string>());

                if (targets.Count > 0)
                {
                    const string updateVarSql = @"
                        UPDATE public.surveyon_variables
                        SET is_ai_opened = true,
                            updated_at = NOW()
                        WHERE pn = @pn 
                          AND (c_question_variable = ANY(@targets) 
                               OR s_question_variable = ANY(@targets) 
                               OR open_rule = ANY(@targets))";
                    await _db.ExecuteAsync(updateVarSql, new { pn, targets = targets.ToArray() }, "Postgreshdata", ct);
                }

                _logger.LogInformation("[SyncHDataAnswerAndVariablesAsync] HData surveyon_answer 동기화 완료 pn={Pn} mergeQnum={MergeQnum} subQnums={SubQnums} count={Count}",
                    pn, mergeQnum, string.Join(",", targets), updatedCount);
                return updatedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SyncHDataAnswerAndVariablesAsync] HData surveyon_answer 동기화 중 오류 pn={Pn} mergeQnum={MergeQnum}", pn, mergeQnum);
                return 0;
            }
        }

        public async Task<List<AnalysisProjectOriginRow>> GetProjectOriginRowsAsync(string pn, string qnum, bool isDuplicateRemove, CancellationToken ct = default)
        {
            // 레거시 gb=11 (일반저장: useyn='분석') vs gb=213 (중복제거저장: 머지문항 전체)
            string sql;
            if (isDuplicateRemove)
            {
                sql = @"SELECT id, projectnum, servername, qnum, qnum_text, merge_pn, merge_qnum, answer_origin, mapid 
                        FROM public.project_list 
                        WHERE (merge_pn = @pn OR projectnum = @pn) 
                          AND (merge_qnum = @qnum OR qnum = @qnum) 
                        ORDER BY mapid ASC, id ASC";
            }
            else
            {
                sql = @"SELECT id, projectnum, servername, qnum, qnum_text, merge_pn, merge_qnum, answer_origin, mapid 
                        FROM public.project_list 
                        WHERE (merge_pn = @pn OR projectnum = @pn) 
                          AND (merge_qnum = @qnum OR qnum = @qnum) 
                          AND useyn = '분석' 
                        ORDER BY mapid ASC, id ASC";
            }

            var rows = await _db.QueryAsync<AnalysisProjectOriginRow>(sql, new { pn, qnum }, HOpen, ct);
            return rows.ToList();
        }

        public async Task<string?> GetPnInAnswerOriginAsync(string pn, string qnum, CancellationToken ct = default)
        {
            const string sql = "SELECT answer_origin FROM public.pn_in WHERE projectnum = @pn AND qnum = @qnum LIMIT 1";
            return await _db.QueryFirstOrDefaultAsync<string?>(sql, new { pn, qnum }, HOpen, ct);
        }
    }
}
