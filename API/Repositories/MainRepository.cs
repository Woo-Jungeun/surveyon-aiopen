using System.Data;
using System.Text;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Npgsql;
using SurveyonApi.Models;
using SurveyonApi.Services;
using Microsoft.Extensions.Primitives;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;

namespace SurveyonApi.Repositories
{
    public class ProjectListInsertItem
    {
        public string Projectnum { get; set; } = "";
        public string Projectname { get; set; } = "";
        public string Projectpof { get; set; } = "";
        public string Model { get; set; } = "";
        public string Qnum { get; set; } = "";
        public string QnumText { get; set; } = "";
        public string QuestionFin { get; set; } = "";
        public string QuestionOrigin { get; set; } = "";
        public string User { get; set; } = "";
        public string UseName { get; set; } = "";
        public string LoadFin { get; set; } = "";
        public string Answer { get; set; } = "";
        public string Servername { get; set; } = "";
        public int Mapid { get; set; }
        public string MergeQnum { get; set; } = "";
        public string MergePn { get; set; } = "";
    }

    public class MainRepository : IMainRepository
    {
        private readonly IPostgreLib _postgreLib;
        private readonly IMsSqlLib _msSqlLib;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly INewOldJsonService _newOldJsonService;

        private static readonly Regex CleanAnswerRegex = new Regex(
            @"[^\p{L}\p{N}\s.,!?@#$%^&*()\[\]{}\-_=+~`|:;""'<>\\\/]",
            RegexOptions.Compiled);

        private static readonly Regex QuestionPatternRegex = new Regex(
            @"^(.*?)\.\s*(.*)",
            RegexOptions.Compiled);

        private static readonly Regex QuestionFinCleanRegex = new Regex(
            @"[^\p{L}\p{N}\s]",
            RegexOptions.Compiled);

        private class TAnswerFastDto
        {
            public string? pid { get; set; }
            public string? originalData { get; set; }
        }

        public MainRepository(IPostgreLib postgreLib, IMsSqlLib msSqlLib, IHttpClientFactory httpClientFactory, IConfiguration configuration, INewOldJsonService newOldJsonService)
        {
            _postgreLib = postgreLib;
            _msSqlLib = msSqlLib;
            _httpClient = httpClientFactory.CreateClient();
            _configuration = configuration;
            _newOldJsonService = newOldJsonService;
        }

        public async Task<List<TestItemDto>> GetTestItemsAsync(CancellationToken ct = default)
        {
            var sql = "SELECT 1 as Id, 'Test' as Name";
            DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, ct: ct);
            var list = new List<TestItemDto>();
            foreach (DataRow row in dt.Rows)
            {
                list.Add(new TestItemDto
                {
                    Id = Convert.ToInt32(row["Id"]),
                    Name = row["Name"].ToString() ?? string.Empty
                });
            }
            return list;
        }

        public async Task<int> AddTestItemAsync(TestItemDto item, CancellationToken ct = default)
        {
            var sql = "INSERT INTO TestTable (Name) VALUES (@Name)";
            // 실제 구현 시에는 _msSqlLib.ExecuteNonQueryAsync 등을 호출
            return await Task.FromResult(1); // 시뮬레이션
        }

        public async Task<ErrorCodeDto?> GetErrorCodeAsync(string code, string lang = "ko", CancellationToken ct = default)
        {
            var sql = @"
                SELECT errorcode,
                       CASE WHEN @lang = 'en' THEN error_en ELSE errortext END AS message,
                       http_status, errorcontent
                FROM pro_error_code
                WHERE errorcode = @code
                LIMIT 1";

            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@code", code),
                new NpgsqlParameter("@lang", lang)
            };

            try 
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, ct: ct);

                if (dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];
                    return new ErrorCodeDto
                    {
                        success = row["errorcode"].ToString() ?? string.Empty,
                        Message = row["message"].ToString() ?? string.Empty,
                        Content = row["errorcontent"].ToString() ?? string.Empty,
                        HttpStatus = Convert.ToInt32(row["http_status"])
                    };
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<DataTable> select_project_list_async(string user, int gb, string oid, string pn, string qnum = "", string search = "", CancellationToken ct = default)
        {
            var sql = "select * from public.project_list where projectnum=@pn ";
            var parameters = new List<NpgsqlParameter>();

            parameters.Add(new NpgsqlParameter("@pn", pn));
            parameters.Add(new NpgsqlParameter("@use_ID", user));

            switch (gb)
            {
                case 1:
                    sql = "select * from public.project_list where (register_userid=@use_ID or use_ID like '%' || @use_ID || '%') and id=@oid and projectnum=@pn ";
                    parameters.Add(new NpgsqlParameter("@oid", int.Parse(oid)));
                    break;
                case 2:
                    sql = "select * from public.project_list where (register_userid=@use_ID or use_ID like '%' || @use_ID || '%') and qnum=@qnum and projectnum=@pn ";
                    parameters.Add(new NpgsqlParameter("@qnum", qnum));
                    break;
                case 3:
                    sql = "select * from public.project_list where (register_userid=@use_ID or use_ID like '%' || @use_ID || '%') and projectnum=@pn ";
                    break;
                case 4:
                    sql = "select * from public.project_list where merge_qnum=@qnum and merge_pn=@pn ";
                    parameters.Add(new NpgsqlParameter("@qnum", qnum));
                    break;
                case 5:
                    sql = "select * from public.project_list where merge_qnum=@qnum and merge_pn=@pn and useyn='분석' and project_lock='수정' ";
                    parameters.Add(new NpgsqlParameter("@qnum", qnum));
                    break;
                case 10:
                    sql = "select * from public.project_list where merge_pn=@pn order by update_date ";
                    break;
                case 11:
                    sql = "select * from public.project_list where merge_qnum=@qnum and merge_pn=@pn and useyn='분석' ";
                    parameters.Add(new NpgsqlParameter("@qnum", qnum));
                    break;
                case 12:
                    sql = "select * from public.project_list where id=@oid and projectnum=@pn ";
                    parameters.Add(new NpgsqlParameter("@oid", int.Parse(oid)));
                    break;
                case 13:
                    sql = "select * from public.project_list where merge_pn=@pn order by mapid/1 ";
                    break;
                case 14:
                    break;
                case 100:
                    sql = "select id, projectname, merge_pn, qnum, qnum_text, question_fin, question_origin, answer_origin, merge_qnum, merge_qnum_check from public.project_list where projectnum=@pn order by qnum_text, id/1 DESC ";
                    break;
                case 110:
                    sql = "select id, projectnum, projectname, qnum, qnum_text, register_date, update_date, status, merge_qnum, python_status, use_name, useYN, merge_pn, merge_pn_check, merge_pn_text from public.project_list where merge_pn=@pn order by qnum_text, id/1 DESC ";
                    break;
                case 111:
                    sql = "select id, option_json, servername, projectname, projectpof, question_fin, listapikey, lvcode from public.project_list where merge_qnum=@qnum and merge_pn=@pn and useyn='분석' order by qnum_text, id/1 DESC ";
                    parameters.Add(new NpgsqlParameter("@qnum", qnum));
                    break;
                case 112:
                    sql = "select id, option_json, servername, projectname, projectpof, question_fin, listapikey, lvcode from public.project_list where merge_qnum=@qnum and merge_pn=@pn and useyn='분석' and option_json is not null order by lvcode DESC ";
                    parameters.Add(new NpgsqlParameter("@qnum", qnum));
                    break;
                case 113:
                    sql = "select id, qnum, merge_qnum, projectnum, servername, useyn, question_origin, qnum_text, question_fin, projectName, lvcode from public.project_list where merge_pn=@pn order by mapid/1 ";
                    break;
                case 114:
                    sql = "select id, projectnum, projectname, projectpof, model, qnum, qnum_text, question_fin, question_origin, status, status_cnt, status_cnt_fin, register_date, register_userID, use_group, use_ID, use_name, update_date, merge_pn, merge_qnum, merge_qnum_check, useYN, project_lock, lb_info, res_info from public.project_list where merge_pn=@pn order by merge_qnum, id/1 ASC ";
                    break;
                case 115:
                    sql = "select id, projectname, projectpof, projectnum, merge_pn, qnum, qnum_text, lb_info, res_info, register_userid, register_date, servername, use_name, update_date, status, use_group from public.project_list order by id/1 DESC ";
                    break;
                case 314:
                    sql = "select id, mapid, qnum, merge_qnum, projectnum, servername, useyn, question_origin, qnum_text, question_fin, projectName, lvcode from public.project_list where merge_pn=@pn order by mapid/1 ";
                    break;
            }

            if (!string.IsNullOrEmpty(search))
            {
                if (!sql.Contains("where", StringComparison.OrdinalIgnoreCase))
                    sql += " where ";
                else
                    sql += " and ";
                
                sql += " (question_fin like @search or question_origin like @search) ";
                parameters.Add(new NpgsqlParameter("@search", $"%{search}%"));
            }

            if (gb != 10 && gb != 13 && gb != 14 && gb < 100)
            {
                sql += " order by qnum_text, id/1 DESC ";
            }
            else if (gb == 14)
            {
                sql += " order by merge_qnum, id/1 ASC ";
            }

            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, ct: ct);
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }


        
        public async Task<DataTable> SelectProjectListSurveyToolAsync(string usertext, string search, string gubun, string connectionKey = "SqlServer", string year ="",  CancellationToken ct = default)
        {
            string sql = "SELECT * FROM [SURVEYTOOL].[dbo].[T_Questionnaire] with(nolock) where ([questionnairePersonName] like '%' + @usertext + '%' or [researcherName] like '%' + @usertext + '%' or [fieldWorkerName] like '%' + @usertext + '%') ";
            if (gubun == "2"  )
            {
                sql = "SELECT * FROM [SURVEYTOOL].[dbo].[T_Questionnaire] with(nolock) where questionnaireNumber = @usertext ";
            }
            if( year == "2026")
            {
                sql += " and createDateTime like '%2026%' ";
            }

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@usertext", usertext)
            };

            if (!string.IsNullOrEmpty(search))
            {
                sql += " AND (questionnaireName like '%' + @search + '%' OR pof like '%' + @search + '%' OR questionnaireNumber like '%' + @search + '%') ";
                parameters.Add(new SqlParameter("@search", search));
            }

            sql += " ORDER BY createDateTime DESC ";

            try
            {
                return await _msSqlLib.ExecuteQueryAsync(sql, parameters, connectionKey, ct: ct);
            }
            catch (Exception)
            {
                return null;
            }
        }
        public async Task<DataTable> select_user_hpro(string usertext, string gubun, string connectionKey = "SqlServer",  CancellationToken ct = default)
        {
            string hprosql = $"SELECT * FROM [SURVEYTOOL].[dbo].[T_EmployeeInfo]  with(nolock)  where [이름] like '%' + @usertext + '%' and email is not null  ";
            if (gubun == "2")
            {
                hprosql = $"SELECT * FROM [SURVEYTOOL].[dbo].[T_EmployeeInfo]  with(nolock)  where [email] like  + @usertext + '%' and email is not null  ";
            }

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@usertext", usertext)
            };
             
            try
            {
                return await _msSqlLib.ExecuteQueryAsync(hprosql, parameters, connectionKey, ct: ct);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<DataTable?> select_user_hpro_api(string usertext, string gubun, string dbType= "SqlServer", CancellationToken ct = default)
        {
            string url;
            var query = new Dictionary<string, string>();

            if (gubun == "2")
            {
                url = _configuration["ExternalApi:HproUseremailUrl"] ?? "";
                query["email"] = usertext;
            }
            else
            {
                url = _configuration["ExternalApi:HproUsernameUrl"] ?? "";
                query["name"] = usertext;
            }

            if (string.IsNullOrWhiteSpace(url))
                return new DataTable();

            url = QueryHelpers.AddQueryString(url, query);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // 필요 시 헤더 추가
            request.Headers.Add("apikey", "+SwkfFTDYdSzSP8wBEuERMNzqUzQqbXLO569HrQK5/wK4udZzRNeArIOZdSPamAm");

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return new DataTable();

            string json = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(json))
                return new DataTable();

            return ConvertJsonToDataTable(json);
        }

        private DataTable ConvertJsonToDataTable(string json)
        {
            var dt = new DataTable();

            if (string.IsNullOrWhiteSpace(json))
                return dt;

            try
            {
                JToken token = JToken.Parse(json);
                JArray? arr = null;

                if (token.Type == JTokenType.Array)
                {
                    var arrayToken = (JArray)token;
                    // 동명이인으로 2개 이상 체크될 경우 아예 등록하지 않도록 구성함
                    if (arrayToken.Count > 1)
                    {
                        return dt;
                    }
                    arr = arrayToken;
                }
                else if (token.Type == JTokenType.Object)
                {
                    var objToken = (JObject)token;
                    // JObject 내부에 JArray 프로퍼티가 있는지 검색 (예: { "data": [...] } 또는 { "": [...] })
                    var innerArrayProp = objToken.Properties().FirstOrDefault(p => p.Value.Type == JTokenType.Array);
                    if (innerArrayProp != null && innerArrayProp.Value is JArray innerArr)
                    {
                        if (innerArr.Count > 1)
                        {
                            return dt;
                        }
                        arr = innerArr;
                    }
                    else
                    {
                        arr = new JArray(objToken);
                    }
                }

                if (arr == null || arr.Count == 0)
                    return dt;

                // JArray 내부의 JObject 요소들을 순회하며 DataTable 구성
                foreach (JToken item in arr)
                {
                    if (item is JObject rowObj)
                    {
                        // 필요한 모든 컬럼을 DataTable에 생성
                        foreach (JProperty prop in rowObj.Properties())
                        {
                            if (!dt.Columns.Contains(prop.Name))
                            {
                                dt.Columns.Add(prop.Name, typeof(string));
                            }
                        }

                        DataRow row = dt.NewRow();
                        foreach (JProperty prop in rowObj.Properties())
                        {
                            row[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                        }
                        dt.Rows.Add(row);
                    }
                }

                // 컬럼명 매핑 (userName -> 이름, team -> 부서, employeeNumber -> 사원번호, email -> email)
                if (dt.Columns.Contains("userName")) dt.Columns["userName"].ColumnName = "이름";
                if (dt.Columns.Contains("team")) dt.Columns["team"].ColumnName = "부서";
                if (dt.Columns.Contains("employeeNumber")) dt.Columns["employeeNumber"].ColumnName = "사원번호";
                if (dt.Columns.Contains("email")) dt.Columns["email"].ColumnName = "email";
            }
            catch
            {
                return new DataTable();
            }

            return dt;
        }

        public async Task<string> GetUserNameAsync(string userId, CancellationToken ct = default)
        {
            var sql = "SELECT username FROM public.pro_login WHERE userid = @userId LIMIT 1";
            var name = await _postgreLib.QueryFirstOrDefaultAsync<string>(sql, new { userId }, ct: ct);
            name = name?.ToString().Trim() ?? "";
            try
            {
                return name ?? userId;
            }
            catch (Exception)
            {
                return userId;
            }
        }

        public async Task<string> GetUserLoginKeyAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId)) return "";
            var sql = "SELECT loginkey FROM public.pro_login WHERE userid = @userId LIMIT 1";
            try
            {
                var key = await _postgreLib.QueryFirstOrDefaultAsync<string>(sql, new { userId }, ct: ct);
                return key?.ToString().Trim() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }
        public async Task<DataTable> select_surveyon_projects_async(string pn, int gubun = 2,   CancellationToken ct = default)
        {
            // 사용자 요청의 로직을 반영하여 쿼리 생성
            string sql = $"SELECT * FROM public.surveyon_projects WHERE pn = @pn ";

            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@pn", pn));
            sql += " ORDER BY id/1 DESC";

            try
            {
                // PostgreLib.ExecuteQueryAsync를 사용하여 진정한 비동기 처리
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }


        public async Task<DataTable> select_project_enter_async(string pn, int gubun = 2, string search = "", CancellationToken ct = default)
        {
            // 사용자 요청의 로직을 반영하여 쿼리 생성
            string sql = $"SELECT * FROM public.project_enter WHERE projectnum IN ({pn}) ";
            
            if (gubun == 2)
            {
                sql = "SELECT * FROM public.project_enter WHERE projectnum = @pn ";
            }
            else if (gubun == 3)
            {
                sql = "SELECT * FROM public.project_enter WHERE projectpof = @pn AND servername = 'NEW' ";
            }

            var parameters = new List<NpgsqlParameter>();
            if (gubun == 2 || gubun == 3)
            {
                parameters.Add(new NpgsqlParameter("@pn", pn));
            }

            if (!string.IsNullOrEmpty(search))
            {
                sql += " AND (projectname LIKE @search OR projectpof LIKE @search OR projectnum LIKE @search) ";
                parameters.Add(new NpgsqlParameter("@search", $"%{search}%"));
            }

            sql += " ORDER BY register_date DESC NULLS LAST, id/1 DESC";

            try
            {   
                // PostgreLib.ExecuteQueryAsync를 사용하여 진정한 비동기 처리
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, ct: ct);
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        public async Task<DataTable> GetTAnswerCountAsync(string projectnum, string type, string gb, string pid=null, CancellationToken ct = default)
        {
            string sql = "select * from \"T_Answer\" where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode order by \"lastWriteDateTime\" DESC";
            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@pn", projectnum));
            parameters.Add(new NpgsqlParameter("@statecode", int.Parse(type)));
        
            //설문온업로드 안된 새로운 데이터만 셀렉
            if (gb == "1")
            {
                sql = "select * from \"T_Answer\" where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode and (openyn is null or openyn='') order by \"lastWriteDateTime\" DESC";
            }
            if (gb == "2")
            {
                sql = "select count(\"questionnaireNumber\") as countpn from \"T_Answer\" where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode group by \"questionnaireNumber\"";
            }
            if (gb == "10")
            {
                sql = "select pid  from \"T_Answer\" where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode order by \"lastWriteDateTime\" DESC";
            }
            if (gb == "11")
            {
                sql = "select pid, \"originalData\" from \"T_Answer\" where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode and pid=@pid order by \"lastWriteDateTime\" DESC";
                parameters.Add(new NpgsqlParameter("@pid", pid));
            }
            try
            {
                var count = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgresqm", ct: ct);
                return count;
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<DataTable> GetTAnswerCountHdataAsync(string projectnum, string type, string gb, string pid = null, CancellationToken ct = default)
        {
            string sql = "select * from surveyon_answer where pn=@pn and answer_state_code=@statecode order by updated_at DESC";
            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@pn", projectnum));
            int stateCode = 4;
            int.TryParse(type, out stateCode);
            parameters.Add(new NpgsqlParameter("@statecode", stateCode));

            if (gb == "10")
            {
                sql = "select pid  from surveyon_answer where pn=@pn and answer_state_code=@statecode order by updated_at DESC";
            }
            if (gb == "11")
            {
                sql = "select pid, response_data, response_data as \"originalData\" from surveyon_answer where pn=@pn and answer_state_code=@statecode and pid=@pid order by updated_at DESC";
                parameters.Add(new NpgsqlParameter("@pid", pid));
            }
            try
            {
                var count = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);
                return count;
            }
            catch
            {
                return new DataTable();
            }
        }

        /// <summary>
        /// T_Answer 오픈문항 응답데이터 초고속 인라인 추출 (PostgreSQL Lateral JSONB 집계로 페이로드 99% 절감)
        /// </summary>
        public async Task<List<(string pid, string jsonData)>> GetTAnswerOpenDataFastAsync(string projectnum, string servername = "rps", CancellationToken ct = default)
        {
            string sql = @"
                SELECT 
                    item.pid, 
                    jsonb_object_agg(item.q_key, item.open_val) AS open_data
                FROM (
                    -- 1) Level 1: ROOT 바로 아래 위치한 직속 오픈 변수 (model, adviserName_op1, repairDate_op1 등)
                    SELECT 
                        t.pid, 
                        sec.key AS q_key, 
                        sec.value->>'@OPEN' AS open_val
                    FROM ""T_Answer"" t,
                    LATERAL jsonb_each(t.""originalData""->'ROOT') sec
                    WHERE t.""questionnaireNumber"" = @pn
                      AND t.""answerStateCode"" = @statecode
                      AND t.""originalData"" IS NOT NULL
                      AND jsonb_typeof(t.""originalData""->'ROOT') = 'object'
                      AND jsonb_typeof(sec.value) = 'object'
                      AND (sec.key LIKE '%_op%' OR LOWER(sec.key) LIKE '%model%')
                      AND sec.value ? '@OPEN'
                      AND sec.value->>'@OPEN' != ''
                      AND NOT (sec.value->>'@OPEN' LIKE '%data:image/png;base64%')

                    UNION ALL

                    -- 2) Level 2: ROOT -> section -> question 형태의 하위 오픈 변수 (q100_r1_op1, q400_op1 등)
                    SELECT 
                        t.pid, 
                        sub.key AS q_key, 
                        sub.value->>'@OPEN' AS open_val
                    FROM ""T_Answer"" t,
                    LATERAL jsonb_each(t.""originalData""->'ROOT') sec,
                    LATERAL jsonb_each(sec.value) sub
                    WHERE t.""questionnaireNumber"" = @pn
                      AND t.""answerStateCode"" = @statecode
                      AND t.""originalData"" IS NOT NULL
                      AND jsonb_typeof(t.""originalData""->'ROOT') = 'object'
                      AND jsonb_typeof(sec.value) = 'object'
                      AND jsonb_typeof(sub.value) = 'object'
                      AND (sub.key LIKE '%_op%' OR LOWER(sub.key) LIKE '%model%')
                      AND sub.value ? '@OPEN'
                      AND sub.value->>'@OPEN' != ''
                      AND NOT (sub.value->>'@OPEN' LIKE '%data:image/png;base64%')
                ) item
                GROUP BY item.pid";

            var list = new List<(string pid, string jsonData)>();

            try
            {
                string connStr = _configuration.GetConnectionString("Postgresqm") ?? "";
                if (string.IsNullOrEmpty(connStr))
                {
                    connStr = "server=10.0.12.95; Port=15300;User Id=syhong; password=dv2402033$; database=QMaster; ";
                }

                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@pn", projectnum);
                cmd.Parameters.AddWithValue("@statecode", 4);
                cmd.CommandTimeout = 300;

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                while (await reader.ReadAsync(ct))
                {
                    string pid = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                    string jsonData = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    if (!string.IsNullOrEmpty(jsonData))
                    {
                        list.Add((pid, jsonData));
                    }
                }
                return list;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[GetTAnswerOpenDataFastAsync] Info: Query cancelled by client request (project: {projectnum}).");
                return list;
            }
            catch (Exception ex) when (ex.Message.Contains("Query was cancelled") || ct.IsCancellationRequested)
            {
                Console.WriteLine($"[GetTAnswerOpenDataFastAsync] Info: Query cancelled by client request (project: {projectnum}).");
                return list;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetTAnswerOpenDataFastAsync] Error: {ex.Message}");
                return list;
            }
        }

        /// <summary>
        /// HData surveyon_answer 오픈문항 응답데이터 초고속 추출 (SPSS data_source 지원)
        /// </summary>
        public async Task<List<(string pid, string jsonData)>> GetSurveyonAnswerOpenDataFastAsync(string projectnum, HashSet<string> openVariableSet, CancellationToken ct = default)
        {
            string sql = @"
                SELECT pid, response_data 
                FROM public.surveyon_answer 
                WHERE pn = @pn 
                  AND answer_state_code = 4 
                  AND response_data IS NOT NULL";

            var list = new List<(string pid, string jsonData)>();

            try
            {
                string connStr = _configuration.GetConnectionString("Postgreshdata") ?? "";
                if (string.IsNullOrEmpty(connStr))
                {
                    connStr = "server=10.0.12.95; Port=15300;User Id=syhong; password=dv2402033$; database=HData; ";
                }

                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@pn", projectnum);
                cmd.CommandTimeout = 300;

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                while (await reader.ReadAsync(ct))
                {
                    string pid = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                    string responseData = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(responseData) || string.IsNullOrEmpty(pid)) continue;

                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(responseData);
                        if (!doc.RootElement.TryGetProperty("ROOT", out var root) || root.ValueKind != System.Text.Json.JsonValueKind.Object)
                        {
                            continue;
                        }

                        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var prop in root.EnumerateObject())
                        {
                            string propName = prop.Name;
                            if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                var propObj = prop.Value;
                                // 1) @OPEN 확인
                                if (propObj.TryGetProperty("@OPEN", out var openElem) && openElem.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    string openVal = openElem.GetString()?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(openVal) && !openVal.Contains("data:image/png;base64"))
                                    {
                                        extracted[propName] = openVal;
                                    }
                                }
                                // 2) @CODE 확인 (SPSS의 경우 오픈변수에 텍스트로 저장)
                                else if (openVariableSet.Contains(propName) && propObj.TryGetProperty("@CODE", out var codeElem))
                                {
                                    string codeVal = codeElem.ToString().Trim();
                                    if (!string.IsNullOrEmpty(codeVal) && codeVal != "__explain__")
                                    {
                                        extracted[propName] = codeVal;
                                    }
                                }

                                // 3) 자식 프로퍼티 재귀 확인 (sub-objects)
                                foreach (var child in propObj.EnumerateObject())
                                {
                                    if (child.Name.StartsWith("@")) continue;
                                    if (child.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                                    {
                                        var childObj = child.Value;
                                        if (childObj.TryGetProperty("@OPEN", out var cOpen) && cOpen.ValueKind == System.Text.Json.JsonValueKind.String)
                                        {
                                            string openVal = cOpen.GetString()?.Trim() ?? "";
                                            if (!string.IsNullOrEmpty(openVal) && !openVal.Contains("data:image/png;base64"))
                                            {
                                                extracted[child.Name] = openVal;
                                            }
                                        }
                                        else if (openVariableSet.Contains(child.Name) && childObj.TryGetProperty("@CODE", out var cCode))
                                        {
                                            string codeVal = cCode.ToString().Trim();
                                            if (!string.IsNullOrEmpty(codeVal) && codeVal != "__explain__")
                                            {
                                                extracted[child.Name] = codeVal;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (extracted.Count > 0)
                        {
                            string jsonString = JsonConvert.SerializeObject(extracted);
                            list.Add((pid, jsonString));
                        }
                    }
                    catch
                    {
                        // 개별 레코드 JSON 파싱 실패 시 무시
                    }
                }
                return list;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[GetSurveyonAnswerOpenDataFastAsync] Info: Query cancelled by client request (project: {projectnum}).");
                return list;
            }
            catch (Exception ex) when (ex.Message.Contains("Query was cancelled") || ct.IsCancellationRequested)
            {
                Console.WriteLine($"[GetSurveyonAnswerOpenDataFastAsync] Info: Query cancelled by client request (project: {projectnum}).");
                return list;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetSurveyonAnswerOpenDataFastAsync] Error: {ex.Message}");
                return list;
            }
        }

        public async Task<object> OpenEnterAsync(string gb, dynamic param, CancellationToken ct = default)
        {
            if (gb == "openenter")
            {
                return await Task.FromResult(new { Message = "OpenEnter logic placeholder", Parameters = param });
            }

            return await Task.FromResult(new { Message = "Unknown gb parameter" });
        }

        public async Task<decimal> GetChargeCostAsync(string apigubun = "openai", CancellationToken ct = default)
        {
            string sql = "SELECT chargecost FROM api_admin where apigubun=@apigubun LIMIT 1";
            var result = await _postgreLib.ExecuteScalarAsync<decimal?>(sql, new { apigubun }, "Postgres", ct);
            
            try
            {
                return result ?? 0m;
            }
            catch
            {
                return 0m;
            }
        }

        public async Task<bool> UpdateChargeCostAsync(decimal chargeCost, string apiGubun, CancellationToken ct = default)
        {
            string sql = "UPDATE api_admin SET chargecost = @chargeCost WHERE apigubun = @apiGubun";
            int affected = await _postgreLib.ExecuteAsync(sql, new { chargeCost, apiGubun }, "Postgres", ct);
            try
            {
                return affected > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<DataTable> RpsQmSelectDataMapAsync(string pn, string servername, CancellationToken ct = default)
        {
            string connectionKey = "SqlServer";
            if (servername == "qm")
            {
                connectionKey = "SqlServerqm";
            }
            else if (servername == "rps")
            {
                connectionKey = "SqlServer";
            }

            string sql = $"select * from T_DataMap where questionnaireNumber=@pn and (openDefinition is not null or questionType='Open' or cQuestionVariable like '%_op%' ) and exportType=0 order by ranking/1, opOrder/1 ";
            
            try
            {
                var result = await _msSqlLib.QueryAsync<dynamic>(sql, new { pn }, connectionKey, ct: ct);
                
                DataTable dt = new DataTable();
                if (result != null && result.Any())
                {
                    // Convert dynamic to DataTable
                    var first = result.First() as IDictionary<string, object>;
                    if (first != null)
                    {
                        foreach (var kvp in first) dt.Columns.Add(kvp.Key);
                        foreach (var item in result)
                        {
                            var row = dt.NewRow();
                            var dict = item as IDictionary<string, object>;
                            foreach (var kvp in dict) row[kvp.Key] = kvp.Value ?? DBNull.Value;
                            dt.Rows.Add(row);
                        }
                    }
                }
                return dt;
            }
            catch
            {
                return new DataTable();
            }
        }
        public async Task<DataTable> HdataSelectDataMapAsync(string pn,   CancellationToken ct = default)
        {
            var sql = $"select * from surveyon_variables where pn=@pn and (open_rule !='' or type='Open' or c_question_variable like '%_op%' or type='string' )  order by ranking/1  ";
            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@pn", pn));

            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);
                 
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }
        public async Task<DataTable> select_surveyon_variables_async(string pn, CancellationToken ct = default)
        {
            var sql = $"select * from surveyon_variables where pn=@pn  ";
            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@pn", pn));

            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);

            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        public async Task<UserInfo> SelectProLoginAsync(string userid, CancellationToken ct = default)
        {
            // Assuming public.project_list contains user info as inferred
            string sql = "SELECT username, groupposition FROM public.pro_login WHERE userid = @userid LIMIT 1";
            var result = await _postgreLib.QueryFirstOrDefaultAsync<dynamic>(sql, new { userid }, "Postgres", ct);
            
            if (result != null)
            {
                // Dapper returns dynamic as DapperRow which implements IDictionary
                var dict = result as IDictionary<string, object>;
                string username = dict["username"]?.ToString().Trim() ?? "";
                string groupposition = dict["groupposition"]?.ToString().Trim() ?? "";
                return new UserInfo { Username = username, GroupPosition = groupposition };
            }
            return new UserInfo();
        }

        public async Task<int> InsertProjectEnterAsync(string projectnum, string pof, string proname, string userid, string server, CancellationToken ct = default)
        {
            string sql = @"INSERT INTO public.project_enter
                   (projectpof, projectnum, projectname, register_userid, register_date, servername, merge_pn)
                   VALUES
                   (@pof, @projectnum, @proname, @userid, @date, @server, @projectnum)
                   ON CONFLICT (projectnum,projectpof ) DO NOTHING";

            try
            {
                string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@pof", pof),
                    new NpgsqlParameter("@projectnum", projectnum),
                    new NpgsqlParameter("@proname", proname),
                    new NpgsqlParameter("@userid", userid),
                    new NpgsqlParameter("@date", datetime),
                    new NpgsqlParameter("@server", server)
                };

                return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
            }
            catch
            {
                return 0;
            }
        }
        public async Task<int> Insert_surveyon_projectsAsync(string projectnum, string pof, string proname, string userid, string server,string method, CancellationToken ct = default)
        {
            string sql = @"INSERT INTO public.surveyon_projects
                   (pof,   project_name,  pn,      method, creator,created_at,updated_at,data_source)
                   VALUES
                   (@pof, @proname,   @projectnum, @method, @userid, @date,     @date,    @server )
                   ON CONFLICT (pn ) DO NOTHING";

            try
            {
                //string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                DateTime datetime = DateTime.Now;
                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@pof", pof),
                    new NpgsqlParameter("@proname", proname),
                    new NpgsqlParameter("@projectnum", projectnum),
                    new NpgsqlParameter("@method", method),
                    new NpgsqlParameter("@userid", userid),
                    new NpgsqlParameter("@date", datetime),
                    new NpgsqlParameter("@server", server)
                };

                return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgreshdata", ct);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<int> Insert_surveyon_variablesAsync(string projectnum, CancellationToken ct = default)
        {
            
            string sql = @"INSERT INTO public.surveyon_variables
                   (project_id,       pn,    s_question_variable, c_question_variable, label, type, code_len, opt_count, total_len,min_answer,ranking, created_at,updated_at,is_baked)
                   VALUES
                   (@project_id , @projectnum, 'pid',    'pid', 'pid', 'open(문자)',     20, 1, 20, 0 , 1,@date, @date, true )
                   ";

            try
            {
                DataTable dtEnter = await select_surveyon_projects_async(projectnum, 2, ct: ct); //HData 테이블
                DataTable dtvariable = await select_surveyon_variables_async(projectnum, ct: ct); //HData 테이블

                if (dtEnter.Rows.Count > 0 && dtvariable.Rows.Count ==0)
                {
                    //string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    DateTime datetime = DateTime.Now;
                    DataRow firstrow = dtEnter.Rows[0];
                    int project_id = 0;

                    int.TryParse(firstrow["id"]?.ToString(), out project_id);

                    var parameters = new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@project_id", project_id),
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@date", datetime),
                    };

                    return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgreshdata", ct);
                }
                else
                {
                    return 0;
                }

            }
            catch
            {
                return 0;
            }
        }
        public async Task<int> InsertProjectPermissionAsync(string projectnum, string pof, string proname, string userid, string username, string groupposition, string usergroup = "관리자(관리,읽기,쓰기)",  CancellationToken ct = default)
        {
            var userInfo = await SelectProLoginAsync(userid, ct);
            //string username = userInfo.Username;
            //string groupposition = userInfo.GroupPosition;

            string sql = @"Insert Into public.pro_permission 
                         (  projectpof, projectnum, projectname,  userid,  username, usergroup,    position, register_date, expired_date) values 
                         (     @pof ,   @projectnum , @proname,   @userid, @username, @usergroup, @position,   @date , @date2 ) 
                            ON CONFLICT (projectnum, userid) DO NOTHING
                        ";
            
            string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string datetime2 = DateTime.Now.AddYears(100)
                    .Date
                    .AddHours(23)
                    .AddMinutes(59)
                    .AddSeconds(59)
                    .ToString("yyyy-MM-dd HH:mm:ss");
            var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@pof", pof),
                    new NpgsqlParameter("@projectnum", projectnum),
                    new NpgsqlParameter("@proname", proname),
                    new NpgsqlParameter("@userid", userid),
                    new NpgsqlParameter("@username", username),
                    new NpgsqlParameter("@usergroup", usergroup),
                    new NpgsqlParameter("@position", groupposition),
                    new NpgsqlParameter("@date", datetime),
                    new NpgsqlParameter("@date2", datetime2),
                };
            try
            {
                return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
            }
            catch
            {
                return 0;
            }
        }

         

        public async Task<int> UpdateTAnswerAsync(string pn, string statecode = "4", string gb = "", CancellationToken ct = default)
        {
            string sql = "update \"T_Answer\" set openyn=@openyn where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode";
            //설문온업로드 안된 새로운 데이터만 셀렉
            if (gb == "1")
            {
                sql = "update \"T_Answer\" set openyn=@openyn where \"questionnaireNumber\"=@pn and \"answerStateCode\"=@statecode and (openyn is null or openyn='')";
            }
            try
            {
                string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var parameters = new List<NpgsqlParameter>();
                parameters.Add(new NpgsqlParameter("@openyn", datetime));
                parameters.Add(new NpgsqlParameter("@pn", pn));
                parameters.Add(new NpgsqlParameter("@statecode", int.Parse(statecode)));
                return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgresqm", ct);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<DataTable> HdataSelectExAsync(string id, CancellationToken ct = default)
        {
            var sql = $"select * from surveyon_labels where variable_id=@id order by code::int";
            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@id", int.Parse(id))) ;

            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);

            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        /// <summary>
        /// 보기 예시(surveyon_labels) 복수 variable_id 일괄 배치 조회 (HData 전용)
        /// </summary>
        public async Task<DataTable> HdataSelectExBatchAsync(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var idList = ids?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
            if (idList == null || idList.Count == 0) return new DataTable();

            var intIds = idList.Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
            if (intIds.Count == 0) return new DataTable();

            DataTable dt = new DataTable();
            foreach (var chunk in intIds.Chunk(1000))
            {
                var chunkList = chunk.ToArray();
                string sql = @"
                    SELECT * FROM public.surveyon_labels 
                    WHERE variable_id = ANY(@ids) 
                    ORDER BY variable_id, (CASE WHEN code ~ '^[0-9]+$' THEN code::bigint ELSE 999999999 END), ranking, id";

                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@ids", chunkList)
                };

                try
                {
                    var chunkDt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);
                    if (chunkDt != null && chunkDt.Rows.Count > 0)
                    {
                        if (dt.Columns.Count == 0)
                        {
                            dt = chunkDt.Clone();
                        }
                        foreach (DataRow row in chunkDt.Rows)
                        {
                            dt.ImportRow(row);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HdataSelectExBatchAsync ERROR] {ex.Message}");
                }
            }

            return dt;
        }

        public async Task<DataTable> HSRT_pages_SelectExAsync(string pn, string user, CancellationToken ct = default)
        {
            var sql = $"select * from pages where pn=@pn and created_by=@user LIMIT 1";
            var parameters = new List<NpgsqlParameter>();
            parameters.Add(new NpgsqlParameter("@pn", pn));
            parameters.Add(new NpgsqlParameter("@user", user));

            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "PostgresHSRT", ct: ct);

            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        public async Task<DataTable> RpsQmSelectExAsync(string id, string servername = "qm", CancellationToken ct = default)
        {
            string connectionKey = "SqlServer";
            if (servername == "qm") connectionKey = "SqlServerqm";

            string sql = $"select * from T_DataMapExample where dataMapID=@id order by exampleValue/1 ";
            
            try
            {
                var result = await _msSqlLib.QueryAsync<dynamic>(sql, new { id }, connectionKey, ct: ct);
                DataTable dt = new DataTable();
                if (result != null && result.Any())
                {
                    var first = result.First() as IDictionary<string, object>;
                    if (first != null)
                    {
                        foreach (var kvp in first) dt.Columns.Add(kvp.Key);
                        foreach (var item in result)
                        {
                            var row = dt.NewRow();
                            var dict = item as IDictionary<string, object>;
                            foreach (var kvp in dict) row[kvp.Key] = kvp.Value ?? DBNull.Value;
                            dt.Rows.Add(row);
                        }
                    }
                }
                return dt;
            }
            catch
            {
                return new DataTable();
            }
        }

        /// <summary>
        /// 보기 예시(T_DataMapExample) 복수 dataMapID 일괄 배치 조회
        /// </summary>
        public async Task<DataTable> RpsQmSelectExBatchAsync(IEnumerable<string> ids, string servername = "qm", CancellationToken ct = default)
        {
            var idList = ids?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
            if (idList == null || idList.Count == 0) return new DataTable();

            string connectionKey = (servername == "qm") ? "SqlServerqm" : "SqlServer";

            DataTable dt = new DataTable();
            bool columnsInitialized = false;

            foreach (var chunk in idList.Chunk(1000))
            {
                var chunkList = chunk.ToList();
                bool allInts = chunkList.All(s => int.TryParse(s, out _));
                object param = allInts ? (object)new { ids = chunkList.Select(int.Parse).ToList() } : new { ids = chunkList };
                string sql = "SELECT * FROM T_DataMapExample WHERE dataMapID IN @ids ORDER BY dataMapID, exampleValue/1";

                try
                {
                    var result = await _msSqlLib.QueryAsync<dynamic>(sql, param, connectionKey, ct: ct);
                    if (result != null && result.Any())
                    {
                        if (!columnsInitialized)
                        {
                            var first = result.First() as IDictionary<string, object>;
                            if (first != null)
                            {
                                foreach (var kvp in first) dt.Columns.Add(kvp.Key);
                                columnsInitialized = true;
                            }
                        }

                        foreach (var item in result)
                        {
                            var row = dt.NewRow();
                            if (item is IDictionary<string, object> dict)
                            {
                                foreach (var kvp in dict) row[kvp.Key] = kvp.Value ?? DBNull.Value;
                                dt.Rows.Add(row);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RpsQmSelectExBatchAsync] Error: {ex.Message}");
                }
            }

            return dt;
        }

        public async Task<bool> InsertUpdatePnLbAsync(string projectnum, string qnum, string result_json, CancellationToken ct = default)
        {
             string sql = "Insert Into public.pn_lb (  projectnum,  qnum,   insert_date,   result_json, result_json_before  ) values " +
                                      "( @projectnum, @qnum, @input_date,   @result_json::jsonb,   @result_json::jsonb  ) " +
        "ON conflict(projectnum, qnum) DO UPDATE SET " +
                                       "result_json = @result_json::jsonb,result_json_before = @result_json::jsonb, update_date = @input_date";
            try 
            {
                var parameters = new List<NpgsqlParameter>();
                parameters.Add(new NpgsqlParameter("@projectnum", projectnum));
                parameters.Add(new NpgsqlParameter("@qnum", qnum));
                parameters.Add(new NpgsqlParameter("@result_json", result_json));
                parameters.Add(new NpgsqlParameter("@input_date", DateTime.Now));
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ProjectListInsertDbAsync(string projectnum, string projectname, string projectpof, string model, string qnum, string qnum_text, string question_fin, string question_origin, string user, string use_name, string load_fin, string answer, string servername, int mapid = 0, string merge_qnum = "", string merge_pn = "", CancellationToken ct = default)
        {
            string finalMergeQnum = string.IsNullOrEmpty(merge_qnum) ? qnum_text : merge_qnum;
            string finalMergePn = string.IsNullOrEmpty(merge_pn) ? projectnum : merge_pn;

            string sql = "Insert Into public.project_list ( projectnum, projectname, projectpof,  model, qnum,  qnum_text, question_fin, question_origin, register_userID, use_group, use_id, use_name,  useyn,project_lock, load_fin, register_date,merge_pn, merge_qnum,merge_qnum_check, answer_origin, servername, mapid ) values " +
                                                        "( @projectnum,@projectname,@projectpof,  @model,@qnum,@qnum_text,@question_fin,@question_origin,    @user ,   'admin'   ,@user, @use_name, '분석', '수정', @load_fin, @register_date,@merge_pn,@merge_qnum,@merge_qnum, @answer::jsonb, @servername, @mapid) " +
                                                        "ON conflict(projectnum, qnum) DO UPDATE SET " +
                                                          "mapid = CASE WHEN EXCLUDED.mapid > 0 THEN EXCLUDED.mapid ELSE public.project_list.mapid END, " +
                                                          "answer_origin = EXCLUDED.answer_origin, " +
                                                          "register_date = COALESCE(public.project_list.register_date, EXCLUDED.register_date), " +
                                                          "update_date = EXCLUDED.register_date, " +
                                                          "qnum_text = COALESCE(NULLIF(public.project_list.qnum_text, ''), EXCLUDED.qnum_text), " +
                                                          "question_fin = COALESCE(NULLIF(public.project_list.question_fin, ''), EXCLUDED.question_fin), " +
                                                          "question_origin = COALESCE(NULLIF(public.project_list.question_origin, ''), EXCLUDED.question_origin), " +
                                                          "load_fin = EXCLUDED.load_fin, " +
                                                          "use_name = EXCLUDED.use_name, " +
                                                          "merge_qnum = COALESCE(NULLIF(public.project_list.merge_qnum, ''), EXCLUDED.merge_qnum), " +
                                                          "merge_qnum_check = COALESCE(NULLIF(public.project_list.merge_qnum_check, ''), EXCLUDED.merge_qnum_check), " +
                                                          "merge_pn = COALESCE(NULLIF(public.project_list.merge_pn, ''), EXCLUDED.merge_pn) ";
            try
            {
                string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var parameters = new List<NpgsqlParameter>();
                parameters.Add(new NpgsqlParameter("@projectnum", projectnum));
                parameters.Add(new NpgsqlParameter("@projectname", projectname));
                parameters.Add(new NpgsqlParameter("@projectpof", projectpof));
                parameters.Add(new NpgsqlParameter("@model", model));
                parameters.Add(new NpgsqlParameter("@qnum", qnum));
                parameters.Add(new NpgsqlParameter("@qnum_text", qnum_text));
                parameters.Add(new NpgsqlParameter("@question_fin", question_fin));
                parameters.Add(new NpgsqlParameter("@question_origin", question_origin));
                parameters.Add(new NpgsqlParameter("@user", user));
                parameters.Add(new NpgsqlParameter("@use_name", use_name));
                parameters.Add(new NpgsqlParameter("@load_fin", load_fin));
                parameters.Add(new NpgsqlParameter("@register_date", datetime));
                parameters.Add(new NpgsqlParameter("@answer", answer));
                parameters.Add(new NpgsqlParameter("@servername", servername));
                parameters.Add(new NpgsqlParameter("@mapid", mapid));
                parameters.Add(new NpgsqlParameter("@merge_pn", finalMergePn));
                parameters.Add(new NpgsqlParameter("@merge_qnum", finalMergeQnum));
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProjectListInsertDbAsync ERROR] projectnum={projectnum}, qnum={qnum}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> InsertUpdatePnLbBatchAsync(List<(string projectnum, string qnum, string result_json)> items, CancellationToken ct = default)
        {
            if (items == null || items.Count == 0) return true;

            string connStr = _configuration.GetConnectionString("Postgres") ?? "";
            if (string.IsNullOrEmpty(connStr)) return false;

            string sql = "INSERT INTO public.pn_lb ( projectnum, qnum, insert_date, result_json, result_json_before ) VALUES " +
                         "( @projectnum, @qnum, @input_date, @result_json::jsonb, @result_json::jsonb ) " +
                         "ON CONFLICT(projectnum, qnum) DO UPDATE SET " +
                         "result_json = @result_json::jsonb, result_json_before = @result_json::jsonb, update_date = @input_date";

            try
            {
                DateTime now = DateTime.Now;
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);

                await using var batch = new NpgsqlBatch(conn);
                foreach (var item in items)
                {
                    var bcmd = new NpgsqlBatchCommand(sql);
                    bcmd.Parameters.AddWithValue("@projectnum", item.projectnum);
                    bcmd.Parameters.AddWithValue("@qnum", item.qnum);
                    bcmd.Parameters.AddWithValue("@result_json", item.result_json);
                    bcmd.Parameters.AddWithValue("@input_date", now);
                    batch.BatchCommands.Add(bcmd);
                }

                await batch.ExecuteNonQueryAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InsertUpdatePnLbBatchAsync ERROR] {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> ProjectListInsertBatchAsync(List<ProjectListInsertItem> items, CancellationToken ct = default)
        {
            if (items == null || items.Count == 0) return true;

            string connStr = _configuration.GetConnectionString("Postgres") ?? "";
            if (string.IsNullOrEmpty(connStr)) return false;

            string sql = "Insert Into public.project_list ( projectnum, projectname, projectpof,  model, qnum,  qnum_text, question_fin, question_origin, register_userID, use_group, use_id, use_name,  useyn,project_lock, load_fin, register_date,merge_pn, merge_qnum,merge_qnum_check, answer_origin, servername, mapid ) values " +
                         "( @projectnum,@projectname,@projectpof,  @model,@qnum,@qnum_text,@question_fin,@question_origin,    @user ,   'admin'   ,@user, @use_name, '분석', '수정', @load_fin, @register_date,@merge_pn,@merge_qnum,@merge_qnum, @answer::jsonb, @servername, @mapid) " +
                         "ON conflict(projectnum, qnum) DO UPDATE SET " +
                         "answer_origin = EXCLUDED.answer_origin, " +
                         "register_date = COALESCE(public.project_list.register_date, EXCLUDED.register_date), " +
                         "update_date = EXCLUDED.register_date, " +
                         "qnum_text = COALESCE(NULLIF(public.project_list.qnum_text, ''), EXCLUDED.qnum_text), " +
                         "question_fin = COALESCE(NULLIF(public.project_list.question_fin, ''), EXCLUDED.question_fin), " +
                         "question_origin = COALESCE(NULLIF(public.project_list.question_origin, ''), EXCLUDED.question_origin), " +
                         "load_fin = EXCLUDED.load_fin, " +
                         "use_name = EXCLUDED.use_name, " +
                         "merge_qnum = COALESCE(NULLIF(public.project_list.merge_qnum, ''), EXCLUDED.merge_qnum), " +
                         "merge_qnum_check = COALESCE(NULLIF(public.project_list.merge_qnum_check, ''), EXCLUDED.merge_qnum_check), " +
                         "merge_pn = COALESCE(NULLIF(public.project_list.merge_pn, ''), EXCLUDED.merge_pn) ";

            try
            {
                string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);

                foreach (var chunk in items.Chunk(10))
                {
                    await using var batch = new NpgsqlBatch(conn);
                    batch.Timeout = 300;
                    foreach (var item in chunk)
                    {
                        string finalMergePn = string.IsNullOrEmpty(item.MergePn) ? item.Projectnum : item.MergePn;
                        string finalMergeQnum = string.IsNullOrEmpty(item.MergeQnum) ? item.QnumText : item.MergeQnum;
                        var bcmd = new NpgsqlBatchCommand(sql);
                        bcmd.Parameters.AddWithValue("@projectnum", item.Projectnum);
                        bcmd.Parameters.AddWithValue("@projectname", item.Projectname);
                        bcmd.Parameters.AddWithValue("@projectpof", item.Projectpof);
                        bcmd.Parameters.AddWithValue("@model", item.Model);
                        bcmd.Parameters.AddWithValue("@qnum", item.Qnum);
                        bcmd.Parameters.AddWithValue("@qnum_text", item.QnumText);
                        bcmd.Parameters.AddWithValue("@question_fin", item.QuestionFin);
                        bcmd.Parameters.AddWithValue("@question_origin", item.QuestionOrigin);
                        bcmd.Parameters.AddWithValue("@user", item.User);
                        bcmd.Parameters.AddWithValue("@use_name", item.UseName);
                        bcmd.Parameters.AddWithValue("@load_fin", item.LoadFin);
                        bcmd.Parameters.AddWithValue("@register_date", datetime);
                        bcmd.Parameters.AddWithValue("@answer", item.Answer);
                        bcmd.Parameters.AddWithValue("@servername", item.Servername);
                        bcmd.Parameters.AddWithValue("@mapid", item.Mapid);
                        bcmd.Parameters.AddWithValue("@merge_pn", finalMergePn);
                        bcmd.Parameters.AddWithValue("@merge_qnum", finalMergeQnum);
                        batch.BatchCommands.Add(bcmd);
                    }

                    await batch.ExecuteNonQueryAsync(ct);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProjectListInsertBatchAsync ERROR] {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> DeleteProjectListQuestionsAsync(string projectnum, List<string>? qnums, List<int>? ids, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum)) return false;
            var targetQnums = qnums?.Where(q => !string.IsNullOrWhiteSpace(q)).Distinct().ToList() ?? new List<string>();
            var targetIds = ids?.Where(i => i > 0).Distinct().ToList() ?? new List<int>();

            if (targetQnums.Count == 0 && targetIds.Count == 0) return false;

            try
            {
                var selectParams = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@projectnum", projectnum),
                    new NpgsqlParameter("@qnums", targetQnums.ToArray()),
                    new NpgsqlParameter("@ids", targetIds.ToArray())
                };

                string selectSql = @"SELECT DISTINCT qnum, merge_qnum 
                                     FROM public.project_list 
                                     WHERE projectnum = @projectnum 
                                       AND ( (cardinality(@qnums::text[]) > 0 AND qnum = ANY(@qnums)) 
                                          OR (cardinality(@ids::int[]) > 0 AND id = ANY(@ids)) );";

                DataTable targetDt = await _postgreLib.ExecuteQueryAsync(selectSql, selectParams, "Postgres", ct);
                
                var pnInQnumsToDelete = new HashSet<string>(targetQnums, StringComparer.OrdinalIgnoreCase);
                foreach (DataRow row in targetDt.Rows)
                {
                    var q = row["qnum"]?.ToString();
                    var mq = row["merge_qnum"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(q)) pnInQnumsToDelete.Add(q);
                    if (!string.IsNullOrWhiteSpace(mq)) pnInQnumsToDelete.Add(mq);
                }

                var deletePlParams = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@projectnum", projectnum),
                    new NpgsqlParameter("@qnums", targetQnums.ToArray()),
                    new NpgsqlParameter("@ids", targetIds.ToArray())
                };

                string deletePlSql = @"DELETE FROM public.project_list 
                                       WHERE projectnum = @projectnum 
                                         AND ( (cardinality(@qnums::text[]) > 0 AND qnum = ANY(@qnums)) 
                                            OR (cardinality(@ids::int[]) > 0 AND id = ANY(@ids)) );";

                await _postgreLib.ExecuteNonQueryAsync(deletePlSql, deletePlParams, "Postgres", ct);

                if (pnInQnumsToDelete.Count > 0)
                {
                    var deletePnInParams = new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@pnInQnums", pnInQnumsToDelete.ToArray())
                    };

                    string deletePnInSql = @"DELETE FROM public.pn_in 
                                             WHERE projectnum = @projectnum 
                                               AND qnum = ANY(@pnInQnums);";

                    await _postgreLib.ExecuteNonQueryAsync(deletePnInSql, deletePnInParams, "Postgres", ct);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeleteProjectListQuestionsAsync ERROR] projectnum={projectnum}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public static int GetPidCountFromJson(string? jsonStr, string qnum = "")
        {
            if (string.IsNullOrWhiteSpace(jsonStr)) return 0;
            try
            {
                JToken token = JToken.Parse(jsonStr);
                JArray? arr = null;
                if (token is JArray a)
                {
                    arr = a;
                }
                else if (token is JObject obj)
                {
                    if (!string.IsNullOrEmpty(qnum) && obj[qnum] is JArray qArr)
                    {
                        arr = qArr;
                    }
                    else
                    {
                        var firstProp = obj.Properties().FirstOrDefault();
                        if (firstProp?.Value is JArray subArr)
                        {
                            arr = subArr;
                        }
                    }
                }
                if (arr == null) return 0;
                return arr.OfType<JObject>()
                    .Select(o => o["pid"]?.ToString())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .Count();
            }
            catch
            {
                return 0;
            }
        }

        public static string MergeJsonbByPid(string? existingJsonStr, string? newJsonStr)
        {
            if (string.IsNullOrWhiteSpace(existingJsonStr)) return newJsonStr ?? "[]";
            if (string.IsNullOrWhiteSpace(newJsonStr)) return existingJsonStr ?? "[]";

            try
            {
                JToken existingToken = JToken.Parse(existingJsonStr);
                JToken newToken = JToken.Parse(newJsonStr);

                string? rootKey = null;
                JArray existingArr = new JArray();
                if (existingToken is JArray a1)
                {
                    existingArr = a1;
                }
                else if (existingToken is JObject o1)
                {
                    foreach (var prop in o1.Properties())
                    {
                        rootKey ??= prop.Name;
                        if (prop.Value is JArray subArr)
                        {
                            foreach (var item in subArr) existingArr.Add(item);
                        }
                    }
                }

                JArray newArr = new JArray();
                if (newToken is JArray a2)
                {
                    newArr = a2;
                }
                else if (newToken is JObject o2)
                {
                    foreach (var prop in o2.Properties())
                    {
                        rootKey ??= prop.Name;
                        if (prop.Value is JArray subArr)
                        {
                            foreach (var item in subArr) newArr.Add(item);
                        }
                    }
                }

                var existingDict = existingArr.OfType<JObject>()
                    .Where(x => x["pid"] != null)
                    .GroupBy(x => x["pid"]!.ToString())
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (JObject newItem in newArr.OfType<JObject>())
                {
                    string pid = newItem["pid"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(pid)) continue;

                    if (existingDict.TryGetValue(pid, out var existingItem))
                    {
                        // 기존에 이미 등록된 PID: 기존 insert_date를 영구 보존
                        var existingDateProp = existingItem.Properties()
                            .FirstOrDefault(p => string.Equals(p.Name, "insert_date", StringComparison.OrdinalIgnoreCase));
                        string? existingInsertDate = existingDateProp?.Value?.ToString()?.Trim();

                        foreach (var prop in newItem.Properties())
                        {
                            // insert_date 속성인 경우: 기존 값이 존재하면 덮어쓰지 않고 최초 등록일시 유지
                            if (string.Equals(prop.Name, "insert_date", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(existingInsertDate))
                                {
                                    continue;
                                }
                            }

                            existingItem[prop.Name] = prop.Value;
                        }
                    }
                    else
                    {
                        // 새롭게 추가된 PID: 신규 insert_date(현재 등록 시점) 부여
                        existingArr.Add(newItem);
                        existingDict[pid] = newItem;
                    }
                }

                if (!string.IsNullOrEmpty(rootKey))
                {
                    JObject resObj = new JObject { [rootKey] = existingArr };
                    return resObj.ToString(Formatting.None);
                }

                return existingArr.ToString(Formatting.None);
            }
            catch
            {
                return newJsonStr ?? existingJsonStr ?? "[]";
            }
        }

        /// <summary>
        /// pn_lb 보기레이블 스마트 병합:
        /// 1) 기존 목록의 순서 및 메타데이터(ex_gubun 등) 유지
        /// 2) 동일 코드(lv123code)인데 레이블(lv3)이 다를 경우 새로운 레이블로 업데이트
        /// 3) 새로운 코드는 기존 목록 뒤에 추가 (기존 + 추가된 보기 레이블)
        /// 4) 기존에만 있는 코드는 그대로 유지
        /// </summary>
        public static JArray MergePnLbJson(JArray existingArr, JArray newArr)
        {
            if (existingArr == null || existingArr.Count == 0) return (JArray)(newArr?.DeepClone() ?? new JArray());
            if (newArr == null || newArr.Count == 0) return (JArray)existingArr.DeepClone();

            // 1. 기존 항목을 기본 리스트로 복사 (순서 및 기존 속성 유지)
            var mergedArr = new JArray();
            var existingCodeMap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in existingArr.OfType<JObject>())
            {
                var cloneItem = (JObject)item.DeepClone();
                mergedArr.Add(cloneItem);

                string code = cloneItem["lv123code"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(code) && !existingCodeMap.ContainsKey(code))
                {
                    existingCodeMap[code] = cloneItem;
                }
            }

            // 2. 신규 항목 순회하며 코드 비교
            foreach (var newItem in newArr.OfType<JObject>())
            {
                string newCode = newItem["lv123code"]?.ToString()?.Trim() ?? "";
                string newLv3 = newItem["lv3"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(newCode)) continue;

                if (existingCodeMap.TryGetValue(newCode, out var existingItem))
                {
                    // 규칙 1: 동일 코드이면서 레이블이 다를 경우 새로운 레이블로 업데이트
                    string existLv3 = existingItem["lv3"]?.ToString()?.Trim() ?? "";
                    if (!string.Equals(existLv3, newLv3, StringComparison.Ordinal))
                    {
                        existingItem["lv3"] = newLv3;
                    }
                }
                else
                {
                    // 규칙 2: 새롭게 추가된 보기레이블은 기존 목록 뒤에 추가
                    var cloneNew = (JObject)newItem.DeepClone();
                    mergedArr.Add(cloneNew);
                    existingCodeMap[newCode] = cloneNew;
                }
                // 규칙 3: 새 항목에 없는 기존 코드는 mergedArr에 그대로 유지됨
            }

            return mergedArr;
        }

        /// <summary>
        /// DB (T_Answer) 기반 오픈데이터 및 응답자 PID 정보 자동 등록/병합
        /// </summary>
        public async Task<(bool success, int addedQCount, int updatedQCount, int addedPidCount)> InsertOpenDataAsync(string projectnum, string server, string pof, string proname, string user, bool isIncremental = false, CancellationToken ct = default)
        {
            int addedQCount = 0;
            int updatedQCount = 0;
            int addedPidCount = 0;
            server = server.ToLower();

            // server 및 pof/proname 자동 판별: project_enter 또는 project_list 등록 정보 기준 보정
            if (string.IsNullOrEmpty(server) || server == "rps" || string.IsNullOrEmpty(pof) || string.IsNullOrEmpty(proname))
            {
                var dtEnter = await select_project_enter_async(projectnum, 2, "", ct);
                if (dtEnter.Rows.Count > 0)
                {
                    if (dtEnter.Columns.Contains("servername"))
                    {
                        string s = dtEnter.Rows[0]["servername"]?.ToString()?.Trim().ToLower() ?? "";
                        if (!string.IsNullOrEmpty(s) && (string.IsNullOrEmpty(server) || server == "rps")) server = s;
                    }
                    if (string.IsNullOrEmpty(pof) || pof == "rps" || pof == "qm")
                    {
                        pof = dtEnter.Rows[0]["projectpof"]?.ToString()?.Trim() ?? "";
                    }
                    if (string.IsNullOrEmpty(proname))
                    {
                        proname = dtEnter.Rows[0]["projectname"]?.ToString()?.Trim() ?? "";
                    }
                }
            }

            // 2-1. HData surveyon_projects에서 data_source 확인 (SPSS 여부 판별)
            DataTable dtProject = await select_surveyon_projects_async(projectnum, 2, ct);
            string dataSource = dtProject.Rows.Count > 0 && dtProject.Columns.Contains("data_source")
                ? (dtProject.Rows[0]["data_source"]?.ToString()?.Trim() ?? "")
                : "";
            bool isSpss = string.Equals(dataSource, "SPSS", StringComparison.OrdinalIgnoreCase);

            // [1단계] T_Answer 완료 데이터 (stateCode = 4) openyn = 1 상태 업데이트 (SPSS가 아닌 경우에만)
            if (!isSpss)
            {
                await UpdateTAnswerAsync(projectnum, "4", "1", ct);
            }

            // [2단계] 현재 설문서버(RPS/QM) DataMap 맵 정보 및 응답데이터 조회 (고속 파이프라인)
            DataTable select_map = await HdataSelectDataMapQnumAllAsync(projectnum, ct); //hdata전용
            if (select_map.Rows.Count == 0)
                return (false, 0, 0, 0);

            // [오픈변수 사전 추출 (openQnumSet)]
            var openQnumSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allQnumIds = new List<string>();

            foreach (DataRow row in select_map.Rows)
            {
                string cVar = (row.Table.Columns.Contains("c_question_variable") ? row["c_question_variable"] : row["cQuestionVariable"])?.ToString()?.TrimEnd() ?? "";
                string openDef = (row.Table.Columns.Contains("open_rule") ? row["open_rule"] : row["openDefinition"])?.ToString()?.Trim() ?? "";
                string qid = row["id"]?.ToString()?.Trim() ?? "";
                string type = row["type"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(qid)) allQnumIds.Add(qid);

                if (isSpss)
                {
                    if (string.Equals(type, "open(문자)", StringComparison.OrdinalIgnoreCase) && !string.Equals(cVar, "pid", StringComparison.OrdinalIgnoreCase))
                    {
                        openQnumSet.Add(cVar);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(openDef))
                    {
                        var tokens = openDef.Split(',').Select(t => t.Trim()).Where(t => t.Contains("op", StringComparison.OrdinalIgnoreCase));
                        foreach (var tok in tokens) openQnumSet.Add(tok);
                    }
                    else if (cVar.Contains("op", StringComparison.OrdinalIgnoreCase) || string.Equals(cVar, "model", StringComparison.OrdinalIgnoreCase) || (string.Equals(type, "open(문자)", StringComparison.OrdinalIgnoreCase) && !string.Equals(cVar, "pid", StringComparison.OrdinalIgnoreCase)))
                    {
                        openQnumSet.Add(cVar);
                    }
                }
            }

            if (openQnumSet.Count == 0)
                return (false, 0, 0, 0);

            var rawAnswerData = new List<(string pid, string jsonData)>();
            if (isSpss)
            {
                rawAnswerData = await GetSurveyonAnswerOpenDataFastAsync(projectnum, openQnumSet, ct);
            }
            else if (server == "rps" || server == "qm")
            {
                rawAnswerData = await GetTAnswerOpenDataFastAsync(projectnum, server, ct);
            }

            // 외부 API / DB 수집 완료 후 응답데이터 준비 대기 로직 (최대 3회 재시도, 회당 500ms)
            int readinessRetries = 3;
            while (rawAnswerData.Count == 0 && readinessRetries > 0)
            {
                await Task.Delay(500, ct);
                if (isSpss)
                {
                    rawAnswerData = await GetSurveyonAnswerOpenDataFastAsync(projectnum, openQnumSet, ct);
                }
                else if (server == "rps" || server == "qm")
                {
                    rawAnswerData = await GetTAnswerOpenDataFastAsync(projectnum, server, ct);
                }
                readinessRetries--;
            }

            // [3단계] T_Answer 응답데이터 파싱 및 오픈문항 성격 분류 (병렬 처리 + 메모리 경량화)
            var threadLocals = new System.Collections.Concurrent.ConcurrentBag<(
                Dictionary<string, int> textCounts,
                Dictionary<string, int> numCounts,
                List<(string pid, string qnum, string openval)> answers
            )>();

            if (rawAnswerData.Count > 0)
            {
                Parallel.ForEach(
                    rawAnswerData,
                    () => (
                        textCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                        numCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                        answers: new List<(string pid, string qnum, string openval)>()
                    ),
                    (item, loopState, local) =>
                    {
                        string pid = item.pid;
                        string jsonData = item.jsonData;
                        if (string.IsNullOrEmpty(jsonData)) return local;

                        var (resultDict, resultint, resultall) = RpsOpenProcessor.ParseFlatOpen(jsonData);

                        if (resultDict != null)
                        {
                            foreach (var kv in resultDict)
                            {
                                string key = kv.Key;
                                string val = kv.Value?.ToString() ?? "";
                                if (!RpsOpenProcessor.IsRefusalOrNonSubstantive(val) && openQnumSet.Contains(key))
                                {
                                    local.textCounts[key] = local.textCounts.GetValueOrDefault(key, 0) + 1;
                                }
                            }
                        }

                        if (resultint != null)
                        {
                            foreach (var key in resultint.Keys)
                            {
                                if (openQnumSet.Contains(key))
                                {
                                    local.numCounts[key] = local.numCounts.GetValueOrDefault(key, 0) + 1;
                                }
                            }
                        }

                        if (resultall != null)
                        {
                            foreach (var kv in resultall)
                            {
                                string key = kv.Key;
                                if (openQnumSet.Contains(key))
                                {
                                    string val = kv.Value?.ToString() ?? "";
                                    local.answers.Add((pid, key, val));
                                }
                            }
                        }

                        return local;
                    },
                    local =>
                    {
                        threadLocals.Add(local);
                    }
                );
            }

            var textOpenQnumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var numericOpenQnumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var allOpenAnswers = new List<(string pid, string qnum, string openval)>();

            foreach (var local in threadLocals)
            {
                foreach (var kv in local.textCounts)
                {
                    textOpenQnumCounts[kv.Key] = textOpenQnumCounts.GetValueOrDefault(kv.Key, 0) + kv.Value;
                }
                foreach (var kv in local.numCounts)
                {
                    numericOpenQnumCounts[kv.Key] = numericOpenQnumCounts.GetValueOrDefault(kv.Key, 0) + kv.Value;
                }
                allOpenAnswers.AddRange(local.answers);
            }

            var totalOpenQnumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ans in allOpenAnswers)
            {
                totalOpenQnumCounts[ans.qnum] = totalOpenQnumCounts.GetValueOrDefault(ans.qnum, 0) + 1;
            }

            var textOpenQnumSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in openQnumSet)
            {
                int txtC = textOpenQnumCounts.GetValueOrDefault(key, 0);
                int numC = numericOpenQnumCounts.GetValueOrDefault(key, 0);
                int totalC = totalOpenQnumCounts.GetValueOrDefault(key, 0);

                // 전체 수집된 오픈 응답 데이터가 0건인 경우 등록 대상에서 제외
                if (totalC == 0) continue;

                // 콤마, 점을 포함한 순수 숫자 오픈 문항으로 판단이 되면 리스트에서 제외 (GetProRegisterDbSelectInfoAsync와 100% 동일한 판정 기준)
                bool isPureNumeric = (numC > 0 && txtC == 0) || (numC > 0 && (numC + txtC > 0) && ((double)numC / (numC + txtC) >= 0.95));
                if (!isPureNumeric)
                {
                    textOpenQnumSet.Add(key);
                }
            }

            if (textOpenQnumSet.Count == 0)
                return (false, 0, 0, 0);

            // 유효 텍스트 오픈 응답만 문항별 그룹화
            var answersByQnum = allOpenAnswers
                .Where(a => textOpenQnumSet.Contains(a.qnum))
                .GroupBy(a => a.qnum, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int mapid = 0;

            var userInfo = await SelectProLoginAsync(user, ct);
            string username = userInfo?.Username ?? "";

            // [N+1 쿼리 제거 1] 기존 등록된 문항 전체 1회 일괄 조회 및 해시맵 인덱싱
            DataTable existingRowDt = await select_project_list_async(user, 100, "", projectnum, "", "", ct);
            var existingRowsByQnum = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in existingRowDt.Rows)
            {
                string q = r["qnum"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(q) && !existingRowsByQnum.ContainsKey(q))
                {
                    existingRowsByQnum[q] = r;
                }
            }

            // [N+1 쿼리 제거 2] 보기 예시(T_DataMapExample) 전체 문항 1회 일괄 배치 조회
            //DataTable select_map_ex_all = await RpsQmSelectExBatchAsync(allQnumIds, server, ct); //큐마전용
            // var exRowsByDataMapId = select_map_ex_all.Rows
            //     .Cast<DataRow>()
            //     .ToLookup(r => r["dataMapID"]?.ToString()?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

            DataTable select_map_ex_all = await HdataSelectExBatchAsync(allQnumIds, ct); //hdata전용
            var exRowsByDataMapId = select_map_ex_all.Rows
                .Cast<DataRow>()
                .ToLookup(r => r["variable_id"]?.ToString()?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

            // [N+1 쿼리 제거 3] 기존 등록된 pn_lb 보기레이블 1회 일괄 조회 및 캐싱
            DataTable existingPnLbDt = await SelectQuestionLabelsAsync(projectnum, "db", ct);
            var existingPnLbByQnum = new Dictionary<string, JArray>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in existingPnLbDt.Rows)
            {
                string qKey = r["qnum"]?.ToString()?.Trim() ?? "";
                string jsonStr = r["result_json"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(qKey) && !string.IsNullOrEmpty(jsonStr) && jsonStr.StartsWith("["))
                {
                    try
                    {
                        existingPnLbByQnum[qKey] = JArray.Parse(jsonStr);
                    }
                    catch { }
                }
            }
            
            var updatedPnLbKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedDummyTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 기존에 존재하는 dummy qnum_text 수집
            int maxDummyNum = 0;
            foreach (DataRow rowlist in existingRowDt.Rows)
            {
                string qnum_text_check = rowlist["qnum_text"]?.ToString()?.Trim() ?? "";
                if (qnum_text_check.StartsWith("dummy_"))
                {
                    usedDummyTexts.Add(qnum_text_check);
                    int underscoreIdx = qnum_text_check.IndexOf('_');
                    if (underscoreIdx >= 0 && int.TryParse(qnum_text_check.Substring(underscoreIdx + 1), out int sInt))
                    {
                        if (sInt > maxDummyNum) maxDummyNum = sInt;
                    }
                }
            }

            // [4단계 & 5단계: select_map 맵 정보 기준 project_list 개별 레코드 구축]
            var pnLbBatchList = new List<(string projectnum, string qnum, string result_json)>();
            var projectListBatchList = new List<ProjectListInsertItem>();

            foreach (DataRow row in select_map.Rows)
            {
                string qnum_temp = (row.Table.Columns.Contains("c_question_variable") ? row["c_question_variable"] : row["cQuestionVariable"])?.ToString()?.Trim() ?? "";
                string qnumid = row["id"]?.ToString()?.Trim() ?? "";
                string openDefinition_qnum_temp = (row.Table.Columns.Contains("open_rule") ? row["open_rule"] : row["openDefinition"])?.ToString()?.Trim() ?? "";
                string question_origin = (row.Table.Columns.Contains("label")
                    ? (!string.IsNullOrEmpty(row["re_label"]?.ToString()) ? row["re_label"] : row["label"])
                    : row["question"])?.ToString()?.Trim() ?? "";
                string qnum_text = "";
                string question_fin = "";

                var qnum_arr = new List<string>();
                if (isSpss)
                {
                    if (textOpenQnumSet.Contains(qnum_temp))
                    {
                        qnum_arr.Add(qnum_temp);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(openDefinition_qnum_temp))
                    {
                        var tokens = openDefinition_qnum_temp.Split(',').Select(t => t.Trim()).Where(t => t.Contains("op", StringComparison.OrdinalIgnoreCase));
                        qnum_arr.AddRange(tokens);
                    }
                    else
                    {
                        if (qnum_temp.Contains("op", StringComparison.OrdinalIgnoreCase) || string.Equals(qnum_temp, "model", StringComparison.OrdinalIgnoreCase) || textOpenQnumSet.Contains(qnum_temp))
                        {
                            qnum_arr.Add(qnum_temp);
                        }
                    }
                }

                string cleanQuestionOrigin = Regex.Replace(question_origin, @"^\s*[\(\[]TEXT[\)\]]\s*", "", RegexOptions.IgnoreCase).Trim();
                Match match = QuestionPatternRegex.Match(cleanQuestionOrigin);
                if (match.Success)
                {
                    qnum_text = match.Groups[1].Value.Trim();
                    question_fin = match.Groups[2].Value.Trim();
                }
                else
                {
                    // 점(.) 패턴이 없는 경우 (예: "종료시간", "특이사항")
                    // SPSS 변수명 또는 정제된 라벨을 문항번호로 활용하여 무의미한 dummy 생성 방지
                    qnum_text = !string.IsNullOrEmpty(cleanQuestionOrigin) ? cleanQuestionOrigin : qnum_temp;
                    question_fin = cleanQuestionOrigin;
                }

                if (string.IsNullOrEmpty(qnum_text))
                {
                    maxDummyNum++;
                    qnum_text = "dummy_" + maxDummyNum.ToString("D3");
                    while (usedDummyTexts.Contains(qnum_text))
                    {
                        maxDummyNum++;
                        qnum_text = "dummy_" + maxDummyNum.ToString("D3");
                    }
                    usedDummyTexts.Add(qnum_text);
                }

                question_fin = QuestionFinCleanRegex.Replace(question_fin, "");

                foreach (string qnum in qnum_arr)
                {
                    if (textOpenQnumSet.Contains(qnum))
                    {
                        answersByQnum.TryGetValue(qnum, out var answerItems);
                        if (answerItems == null || answerItems.Count == 0)
                        {
                            continue;
                        }

                        JArray resultArray = new JArray();
                        foreach (var item in answerItems)
                        {
                            string openval = item.openval;
                            string answer_temp = SecureReplace.SanitizePrompt(openval);
                            string answer_fin = CleanAnswerRegex.Replace(answer_temp, "")
                                                                .Replace("CanDK", "").ToLower();

                            JObject newItem = new JObject
                            {
                                ["pid"] = item.pid,
                                ["answer_origin"] = openval,
                                ["answer_fin"] = answer_fin,
                                ["insert_date"] = datetime
                            };
                            resultArray.Add(newItem);
                        }

                        // {"q340_op6": [...]} 형태로 JSON 객체 생성
                        JObject newAnswerObj = new JObject
                        {
                            [qnum] = resultArray
                        };
                        string newAnswerJson = newAnswerObj.ToString(Formatting.None);
                        string finalAnswerJson = newAnswerJson;

                        // [6단계] DB project_list 테이블에서 현재 오픈문항 변수명(qnum)과 1:1로 일치하는 기존 DataRow 선별 (메모리 캐시 O(1))
                        bool isExisting = existingRowsByQnum.TryGetValue(qnum, out var targetRow);

                        string itemQnumText = qnum_text;
                        string itemQuestionFin = question_fin;
                        string itemQuestionOrigin = question_origin;
                        string itemMergeQnum = qnum_text;
                        string itemMergePn = projectnum;

                        if (isExisting && targetRow != null)
                        {
                            // [보존 규칙] qnum 기준으로 기존에 등록되어 있다면 qnum_text, question_fin, question_origin 변경 금지
                            string existQnumText = targetRow["qnum_text"]?.ToString()?.Trim() ?? "";
                            string existQuestionFin = targetRow["question_fin"]?.ToString()?.Trim() ?? "";
                            string existQuestionOrigin = targetRow["question_origin"]?.ToString()?.Trim() ?? "";

                            if (!string.IsNullOrEmpty(existQnumText)) itemQnumText = existQnumText;
                            if (!string.IsNullOrEmpty(existQuestionFin)) itemQuestionFin = existQuestionFin;
                            if (!string.IsNullOrEmpty(existQuestionOrigin)) itemQuestionOrigin = existQuestionOrigin;

                            string existMq = targetRow["merge_qnum"]?.ToString()?.Trim() ?? "";
                            string existMpn = targetRow["merge_pn"]?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(existMq)) itemMergeQnum = existMq;
                            if (!string.IsNullOrEmpty(existMpn)) itemMergePn = existMpn;

                            if (isIncremental)
                            {
                                updatedQCount++;
                                string existingAnswerJson = targetRow["answer_origin"]?.ToString() ?? "";
                                int initialPidCount = GetPidCountFromJson(existingAnswerJson, qnum);

                                // PID 기준 JSON 응답 병합 (MergeJsonbByPid)
                                finalAnswerJson = MergeJsonbByPid(existingAnswerJson, newAnswerJson);
                                int finalPidCount = GetPidCountFromJson(finalAnswerJson, qnum);

                                addedPidCount += Math.Max(0, finalPidCount - initialPidCount);
                            }
                        }
                        else
                        {
                            addedQCount++;
                            int newPidCount = GetPidCountFromJson(newAnswerJson, qnum);
                            addedPidCount += newPidCount;
                        }

                        // 보기 예시 옵션 데이터(select_map_ex) 배치 캐시에서 조회 (O(1)) 및 pn_lb 스마트 병합
                        var exRows = exRowsByDataMapId[qnumid];
                        if (exRows.Any() && !updatedPnLbKeys.Contains(itemQnumText))
                        {
                            JArray newArrayLB = new JArray();
                            var seenLv3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            foreach (DataRow row2 in exRows)
                            {
                                string weightStr = (row2.Table.Columns.Contains("example_weight") ? row2["example_weight"] : row2["exampleWeight"])?.ToString();
                                if (decimal.TryParse(weightStr, out var w) && w == 1m)
                                    continue;

                                string lv3 = (row2.Table.Columns.Contains("label") ? row2["label"] : row2["exampleText"])?.ToString()?.TrimEnd() ?? "";
                                string lv123code = (row2.Table.Columns.Contains("code") ? row2["code"] : row2["exampleValue"])?.ToString()?.TrimEnd() ?? "";

                                if (seenLv3.Add(lv3))
                                {
                                    JObject newItem2 = new JObject
                                    {
                                        ["lv3"] = lv3,
                                        ["lv123code"] = lv123code,
                                        ["ex_gubun"] = "설문(수정불가)",
                                    };
                                    newArrayLB.Add(newItem2);
                                }
                            }

                            // [pn_lb 스마트 병합] 기존에 등록된 보기레이블이 있을 경우 병합
                            JArray finalArrayLB = newArrayLB;
                            if (existingPnLbByQnum.TryGetValue(itemQnumText, out var existLbArr) || (!string.IsNullOrEmpty(qnum_text) && existingPnLbByQnum.TryGetValue(qnum_text, out existLbArr)))
                            {
                                finalArrayLB = MergePnLbJson(existLbArr, newArrayLB);
                            }

                            if (finalArrayLB.Count > 0)
                            {
                                pnLbBatchList.Add((projectnum, itemQnumText, finalArrayLB.ToString(Formatting.None)));
                                updatedPnLbKeys.Add(itemQnumText);
                                existingPnLbByQnum[itemQnumText] = finalArrayLB;
                            }
                        }

                        if (string.IsNullOrEmpty(pof) || pof == "rps" || pof == "qm")
                        {
                            if (targetRow != null && targetRow.Table.Columns.Contains("projectpof"))
                            {
                                string p = targetRow["projectpof"]?.ToString()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(p)) pof = p;
                            }
                            if (string.IsNullOrEmpty(pof) && existingRowDt.Rows.Count > 0 && existingRowDt.Columns.Contains("projectpof"))
                            {
                                string p = existingRowDt.Rows[0]["projectpof"]?.ToString()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(p)) pof = p;
                            }
                            if (string.IsNullOrEmpty(pof)) pof = projectnum;
                        }
                        if (string.IsNullOrEmpty(proname))
                        {
                            if (targetRow != null && targetRow.Table.Columns.Contains("projectname"))
                            {
                                proname = targetRow["projectname"]?.ToString()?.Trim() ?? "";
                            }
                            if (string.IsNullOrEmpty(proname) && existingRowDt.Rows.Count > 0 && existingRowDt.Columns.Contains("projectname"))
                            {
                                proname = existingRowDt.Rows[0]["projectname"]?.ToString()?.Trim() ?? "";
                            }
                        }

                        // [7단계] PostgreSQL project_list 테이블 일괄 삽입/업데이트 배치 리스트 적재 (ON CONFLICT (projectnum, qnum))
                        mapid++;
                        projectListBatchList.Add(new ProjectListInsertItem
                        {
                            Projectnum = projectnum,
                            Projectname = proname,
                            Projectpof = pof,
                            Model = "설문온",
                            Qnum = qnum,
                            QnumText = itemQnumText,
                            QuestionFin = itemQuestionFin,
                            QuestionOrigin = itemQuestionOrigin,
                            User = user,
                            UseName = username,
                            LoadFin = "DB",
                            Answer = finalAnswerJson,
                            Servername = server,
                            Mapid = mapid,
                            MergeQnum = itemMergeQnum,
                            MergePn = itemMergePn
                        });
                    }
                }
            }

            // [7-1단계] NpgsqlBatch를 통한 pn_lb 및 project_list 단일 네트워크 왕복 일괄 저장
            if (pnLbBatchList.Count > 0)
            {
                await InsertUpdatePnLbBatchAsync(pnLbBatchList, ct);
            }
            if (projectListBatchList.Count > 0)
            {
                await ProjectListInsertBatchAsync(projectListBatchList, ct);
            }

            // [7-2단계] surveyon_variables 원본 맵 순서 기반 project_list.mapid 일괄 재색인 및 동기화 (1..N)
            try
            {
                await SyncProjectListMapIdFromSurveyonVariablesAsync(projectnum, ct);
            }
            catch (Exception exSync)
            {
                Console.WriteLine($"[InsertOpenDataAsync] SyncProjectListMapId warning: {exSync.Message}");
            }

            // [8단계] 레거시 .NET NewOldCheck 이관: project_list 수집 완료 후 pn_in 집계 테이블 동기화
            try
            {
                await _newOldJsonService.NewOldCheckAsync(projectnum, "", "", ct);
            }
            catch (Exception exNewOld)
            {
                Console.WriteLine($"[InsertOpenDataAsync] NewOldCheck warning: {exNewOld.Message}");
            }

            return (true, addedQCount, updatedQCount, addedPidCount);
        }

        /// <summary>
        /// 데이터관리 맵 (HData.surveyon_variables) 순서 기준으로 project_list.mapid를 일괄 동기화 (재색인 1..N)
        /// </summary>
        public async Task<bool> SyncProjectListMapIdFromSurveyonVariablesAsync(string projectnum, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum)) return false;
            projectnum = projectnum.Trim();

            try
            {
                // 1. HData.surveyon_variables에서 해당 프로젝트의 맵 순서 목록 조회
                DataTable dtMap = await HdataSelectDataMapQnumAllAsync(projectnum, ct);
                if (dtMap == null || dtMap.Rows.Count == 0)
                {
                    return false;
                }

                // 2. qnum -> 맵 순위 딕셔너리 구축 (ranking ASC, id ASC)
                var mapRankDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int rankSeq = 0;

                foreach (DataRow row in dtMap.Rows)
                {
                    rankSeq++;
                    string cVar = (row.Table.Columns.Contains("c_question_variable") ? row["c_question_variable"] : row["cQuestionVariable"])?.ToString()?.TrimEnd() ?? "";
                    string openRule = (row.Table.Columns.Contains("open_rule") ? row["open_rule"] : row["openDefinition"])?.ToString()?.Trim() ?? "";

                    if (!string.IsNullOrEmpty(cVar))
                    {
                        if (!mapRankDict.ContainsKey(cVar))
                        {
                            mapRankDict[cVar] = rankSeq;
                        }
                    }

                    if (!string.IsNullOrEmpty(openRule))
                    {
                        var tokens = openRule.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t));
                        foreach (var tok in tokens)
                        {
                            if (tok.Contains("op", StringComparison.OrdinalIgnoreCase) || tok.StartsWith("q", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!mapRankDict.ContainsKey(tok))
                                {
                                    mapRankDict[tok] = rankSeq;
                                }
                            }
                        }
                    }
                }

                if (mapRankDict.Count == 0) return false;

                // 3. Postgres.project_list에서 해당 프로젝트 문항 목록 조회
                string sqlSelect = "SELECT id, qnum, mapid FROM public.project_list WHERE merge_pn = @projectnum OR projectnum = @projectnum ORDER BY id ASC";
                var selectParams = new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) };
                DataTable dtPl = await _postgreLib.ExecuteQueryAsync(sqlSelect, selectParams, "Postgres", ct);

                if (dtPl == null || dtPl.Rows.Count == 0) return false;

                // 4. project_list 문항들을 surveyon_variables 맵 순서대로 정렬
                var items = new List<(int Id, string Qnum, int CurMapid, int MapRank)>();
                foreach (DataRow row in dtPl.Rows)
                {
                    int id = Convert.ToInt32(row["id"]);
                    string qnum = row["qnum"]?.ToString()?.Trim() ?? "";
                    int curMapid = row["mapid"] != DBNull.Value && int.TryParse(row["mapid"]?.ToString(), out int mVal) ? mVal : 0;
                    int rank = mapRankDict.TryGetValue(qnum, out int rVal) ? rVal : 999999;

                    items.Add((id, qnum, curMapid, rank));
                }

                var sorted = items.OrderBy(x => x.MapRank).ThenBy(x => x.Qnum).ThenBy(x => x.Id).ToList();

                // 5. 1부터 N까지 순차 mapid 부여 및 변경 필요 항목 추출
                var updates = new List<(int Id, int NewMapid)>();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int newMapid = i + 1;
                    if (sorted[i].CurMapid != newMapid)
                    {
                        updates.Add((sorted[i].Id, newMapid));
                    }
                }

                if (updates.Count == 0)
                {
                    return true;
                }

                // 6. 단일 일괄 UPDATE 쿼리로 초고속 반영
                var sbSql = new StringBuilder();
                sbSql.Append("UPDATE public.project_list SET mapid = CASE id ");
                var updateParams = new List<NpgsqlParameter>();

                for (int i = 0; i < updates.Count; i++)
                {
                    string idParam = $"@id{i}";
                    string mapParam = $"@map{i}";
                    sbSql.Append($"WHEN {idParam} THEN {mapParam} ");
                    updateParams.Add(new NpgsqlParameter(idParam, updates[i].Id));
                    updateParams.Add(new NpgsqlParameter(mapParam, updates[i].NewMapid));
                }
                sbSql.Append("END WHERE id IN (");
                sbSql.Append(string.Join(", ", Enumerable.Range(0, updates.Count).Select(i => $"@id{i}")));
                sbSql.Append(");");

                await _postgreLib.ExecuteNonQueryAsync(sbSql.ToString(), updateParams, "Postgres", ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncProjectListMapId Error] {ex.Message}");
                return false;
            }
        }

        public async Task<DataTable> SelectProListInfoAsync(string userid, CancellationToken ct = default)
        {
            var userInfo = await SelectProLoginAsync(userid, ct);
            string groupposition = userInfo.GroupPosition;

            string sql = @"
                SELECT * FROM public.project_list 
                WHERE merge_pn IN (
                    SELECT projectnum FROM public.pro_permission 
                    WHERE userid = @userid OR userid = 'all'
                )";

            if (groupposition != "일반" && groupposition != "고객")
            {
                sql = @"
                    SELECT * FROM public.project_list 
                    WHERE merge_pn IN (
                        SELECT projectnum FROM public.pro_permission 
                        WHERE userid = @userid OR userid = 'all' OR userid = 'hrc'
                    )";
            }

            sql += " ORDER BY id/1 DESC";

            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userid", userid)
            };

            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
            }
            catch (Exception)
            {
                return new DataTable();
            }
        }

        public async Task<(string, string)> GetUserGroupAsync(string userid, string projectnum, CancellationToken ct = default)
        {
            string sql = "SELECT usergroup, position FROM public.pro_permission WHERE userid = @userid AND projectnum = @projectnum LIMIT 1";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userid", userid),
                new NpgsqlParameter("@projectnum", projectnum)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
                if (dt.Rows.Count > 0)
                {
                    return (dt.Rows[0]["usergroup"]?.ToString() ?? "", dt.Rows[0]["position"]?.ToString() ?? "");
                }
            }
            catch { }
            return ("", "");
        }
        public async Task<bool> SelectUserCheckAsync(string user, string key, CancellationToken ct = default)
        {
            try
            {
                string sql = "select * from public.pro_login where userid=@USER and loginkey=@KEY";
                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@USER", user),
                    new NpgsqlParameter("@KEY", key)
                };

                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct: ct);
                return dt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PostgreSQL 연결 실패: " + ex.Message);
                return false;
            }
        }

        public async Task<string?> SelectUserByLoginKeyAsync(string key, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key)) return null;
                string sql = "select userid from public.pro_login where loginkey=@KEY limit 1";
                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@KEY", key)
                };

                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct: ct);
                if (dt != null && dt.Rows.Count > 0)
                {
                    return dt.Rows[0]["userid"]?.ToString() ?? "";
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PostgreSQL 연결 실패 (SelectUserByLoginKeyAsync): " + ex.Message);
                return null;
            }
        }

        public async Task<Dictionary<string, string>> GetBulkAnswerCountsAsync(IEnumerable<string> projectNums, string type = "4", CancellationToken ct = default)
        {
            var pnsArray = projectNums.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (pnsArray.Length == 0) return result;

            string sql = "select \"questionnaireNumber\", count(\"questionnaireNumber\") as countpn from \"T_Answer\" where \"questionnaireNumber\" = ANY(@pns) and \"answerStateCode\" = @statecode group by \"questionnaireNumber\"";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pns", pnsArray),
                new NpgsqlParameter("@statecode", int.Parse(type))
            };

            try
            {
                var dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgresqm", ct: ct);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string pn = row["questionnaireNumber"]?.ToString()?.Trim() ?? "";
                        string cnt = row["countpn"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(pn))
                        {
                            result[pn] = cnt;
                        }
                    }
                }
            }
            catch { }

            return result;
        }


        public async Task<int> GetSamplingCompleteCountAsync(string projectNums, string type = "4", string servername = "", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectNums)) return 0;
            try
            {
                string connectionKey = servername.Equals("QM", StringComparison.OrdinalIgnoreCase) ? "SqlServerqm_answer" : "SqlServer_answer";
                string sql = "select count(*) from T_SamplingList_" + projectNums + " where answerStateCode = @statecode";
                var parameters = new List<SqlParameter>
                {
                    new SqlParameter("@statecode", int.Parse(type))
                };
                var result = await _msSqlLib.ExecuteScalarAsync(sql, parameters, connectionKey, ct: ct);
                return Convert.ToInt32(result ?? 0);
            }
            catch
            {
                return 0;
            }
        }


        public async Task<Dictionary<string, (string usergroup, string position, string? topyn)>> GetBulkUserGroupsAsync(string userId, IEnumerable<string> projectNums, CancellationToken ct = default)
        {
            var pnsArray = projectNums.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            var result = new Dictionary<string, (string usergroup, string position, string? topyn)>(StringComparer.OrdinalIgnoreCase);
            if (pnsArray.Length == 0) return result;

            string sql = "SELECT projectnum, usergroup, position, topyn FROM public.pro_permission WHERE (userid = @userId OR userid = 'all' OR userid = 'hrc') AND projectnum = ANY(@pns) ORDER BY (CASE WHEN userid = @userId THEN 1 ELSE 2 END)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userId", userId),
                new NpgsqlParameter("@pns", pnsArray)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string pn = row["projectnum"]?.ToString()?.Trim() ?? "";
                        string ug = row["usergroup"]?.ToString()?.Trim() ?? "";
                        string pos = row["position"]?.ToString()?.Trim() ?? "";
                        string? top = string.Equals(row["topyn"]?.ToString()?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ? "y" : null;

                        if (!string.IsNullOrEmpty(pn) && !result.ContainsKey(pn))
                        {
                            result[pn] = (ug, pos, top);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        public async Task<(bool success, string? newTopYn)> UpdateProjectTopYnAsync(string projectnum, string userId, string? topyn = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum) || string.IsNullOrWhiteSpace(userId))
            {
                return (false, null);
            }

            string cleanPn = projectnum.Trim();
            string cleanUser = userId.Trim();

            // 1. 현재 사용자 pro_permission 상태 조회
            string selectSql = "SELECT id, topyn FROM public.pro_permission WHERE projectnum = @pn AND userid = @user LIMIT 1";
            var checkParams = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pn", cleanPn),
                new NpgsqlParameter("@user", cleanUser)
            };

            DataTable dt = await _postgreLib.ExecuteQueryAsync(selectSql, checkParams, "Postgres", ct);
            string? currentTop = null;
            bool rowExists = false;
            if (dt != null && dt.Rows.Count > 0)
            {
                rowExists = true;
                currentTop = dt.Rows[0]["topyn"]?.ToString()?.Trim().ToLowerInvariant();
            }

            // 2. targetTopYn 결정 (topyn 인자가 주어지면 그 값 반영, null이면 토글)
            string? targetTopYn;
            if (topyn != null)
            {
                targetTopYn = string.Equals(topyn.Trim(), "y", StringComparison.OrdinalIgnoreCase) ? "y" : null;
            }
            else
            {
                targetTopYn = (currentTop == "y") ? null : "y";
            }

            // 3. DB 업데이트 또는 인서트
            if (rowExists)
            {
                string updateSql = "UPDATE public.pro_permission SET topyn = @topyn WHERE projectnum = @pn AND userid = @user";
                var updateParams = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@topyn", (object?)targetTopYn ?? DBNull.Value),
                    new NpgsqlParameter("@pn", cleanPn),
                    new NpgsqlParameter("@user", cleanUser)
                };
                await _postgreLib.ExecuteNonQueryAsync(updateSql, updateParams, "Postgres", ct);
            }
            else
            {
                // 사용자 레코드가 아직 없는 경우 project_enter 정보 기반 생성
                string insertSql = @"
                    INSERT INTO public.pro_permission (projectpof, projectnum, projectname, userid, username, usergroup, position, register_date, topyn)
                    SELECT projectpof, projectnum, projectname, @user, @user, '일반', '일반', TO_CHAR(NOW(), 'YYYY-MM-DD HH24:MI:SS'), @topyn
                    FROM public.project_enter
                    WHERE projectnum = @pn
                    LIMIT 1";
                var insertParams = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@user", cleanUser),
                    new NpgsqlParameter("@topyn", (object?)targetTopYn ?? DBNull.Value),
                    new NpgsqlParameter("@pn", cleanPn)
                };
                await _postgreLib.ExecuteNonQueryAsync(insertSql, insertParams, "Postgres", ct);
            }

            return (true, targetTopYn);
        }

        public async Task<Dictionary<string, ProjectStatusInfo>> GetBulkProjectStatusListAsync(IEnumerable<string> projectNums, CancellationToken ct = default)
        {
            var pnsArray = projectNums.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            var result = new Dictionary<string, ProjectStatusInfo>(StringComparer.OrdinalIgnoreCase);
            if (pnsArray.Length == 0) return result;

            string sql = "SELECT id, projectnum, projectname, qnum, qnum_text, register_date, update_date, status, merge_qnum, python_status, use_name, useYN, merge_pn, merge_pn_check, merge_pn_text FROM public.project_list WHERE merge_pn = ANY(@pns) ORDER BY merge_pn, qnum_text, id/1 DESC";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pns", pnsArray)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
                if (dt != null && dt.Rows.Count > 0)
                {
                    var grouped = dt.AsEnumerable().GroupBy(r => r["merge_pn"]?.ToString()?.Trim() ?? "");

                    foreach (var group in grouped)
                    {
                        string mergePn = group.Key;
                        if (string.IsNullOrEmpty(mergePn)) continue;

                        var info = new ProjectStatusInfo { MergePn = mergePn };
                        int cnt2 = 0;
                        int status_check_cnt = 0;
                        int status_real_cnt = 0;
                        int status_analsys_cnt = 0;
                        List<string> checkmergeqnum = new List<string>();

                        foreach (DataRow row2 in group)
                        {
                            cnt2++;
                            if (cnt2 == 1)
                            {
                                info.ProjectRegisterDate = row2["register_date"]?.ToString()?.Trim() ?? "";
                                info.ProjectUpdateDate = row2["update_date"]?.ToString()?.Trim() ?? "";
                            }
                            info.MergePnCheck = row2["merge_pn_check"]?.ToString()?.Trim() ?? "";
                            info.MergePnText = row2["merge_pn_text"]?.ToString()?.Trim() ?? "";
                            info.UseName = row2["use_name"]?.ToString()?.Trim() ?? "";

                            string current_status = row2["status"]?.ToString()?.Trim() ?? "";
                            string useYN = row2["useYN"]?.ToString()?.Trim() ?? "";
                            string merge_qnum = row2["merge_qnum"]?.ToString()?.Trim() ?? "";
                            string python_status = row2["python_status"]?.ToString()?.Trim() ?? "";

                            if (python_status == "분석완료" || python_status == "분석중")
                            {
                                status_analsys_cnt++;
                            }

                            if (!checkmergeqnum.Contains(merge_qnum))
                            {
                                if (useYN == "분석")
                                {
                                    checkmergeqnum.Add(merge_qnum);
                                    status_check_cnt++;
                                    if (current_status == "완료")
                                    {
                                        status_real_cnt++;
                                    }
                                }
                            }
                        }

                        if (status_check_cnt == status_real_cnt && status_real_cnt > 0)
                        {
                            info.ProjectStatus = "완료";
                        }
                        else if (status_real_cnt > 0 || status_analsys_cnt > 0)
                        {
                            info.ProjectStatus = "진행중";
                        }
                        else
                        {
                            info.ProjectStatus = "";
                        }

                        result[mergePn] = info;
                    }
                }
            }
            catch { }

            return result;
        }

        public async Task<Dictionary<string, string>> GetBulkUserNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
        {
            var idsArray = userIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToArray();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (idsArray.Length == 0) return result;

            string sql = "SELECT userid, username FROM public.pro_login WHERE userid = ANY(@userIds)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userIds", idsArray)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string uid = row["userid"]?.ToString()?.Trim() ?? "";
                        string uname = row["username"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(uid))
                        {
                            result[uid] = uname;
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        public async Task<HashSet<string>> GetBulkSurveyonProjectPnsAsync(IEnumerable<string> projectNums, CancellationToken ct = default)
        {
            var pnsArray = projectNums.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pnsArray.Length == 0) return set;

            string sql = "SELECT pn FROM public.surveyon_projects WHERE pn = ANY(@pns)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pns", pnsArray)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string pn = row["pn"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(pn)) set.Add(pn);
                    }
                }
            }
            catch { }

            return set;
        }

        public async Task<HashSet<string>> GetBulkHsrtPagePnsAsync(string userId, IEnumerable<string> projectNums, CancellationToken ct = default)
        {
            var pnsArray = projectNums.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pnsArray.Length == 0) return set;

            string sql = "SELECT pn FROM pages WHERE created_by = @userId AND pn = ANY(@pns)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userId", userId),
                new NpgsqlParameter("@pns", pnsArray)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "PostgresHSRT", ct: ct);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string pn = row["pn"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(pn)) set.Add(pn);
                    }
                }
            }
            catch { }

            return set;
        }

        public async Task<HashSet<string>> GetBulkExistingPermissionsAsync(IEnumerable<string> projectNums, CancellationToken ct = default)
        {
            var pnsArray = projectNums.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pnsArray.Length == 0) return set;

            string sql = "SELECT projectnum, userid, usergroup FROM public.pro_permission WHERE projectnum = ANY(@pns)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pns", pnsArray)
            };

            try
            {
                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct: ct);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string pn = row["projectnum"]?.ToString()?.Trim() ?? "";
                        string uid = row["userid"]?.ToString()?.Trim() ?? "";
                        string ug = row["usergroup"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(pn) && !string.IsNullOrEmpty(uid))
                        {
                            set.Add($"{pn}_{uid}_{ug}");
                        }
                    }
                }
            }
            catch { }

            return set;
        }
        public async Task<(bool isValid, string showmenu,string projectname,  string projectnum, string page_id, string page_title, string groupcode, string loginkey)> GetCustomerLoginInfoAsync(string cs,  CancellationToken ct = default)
        {
            try
            {
                // groupcode='999999991' 인 SRT 고객 계정이면서 비밀번호 일치 및 만료기한 확인
                // string sql = "select  * from public.pro_permission where userid='j9FjlvGpcJ1'";
                string sql2 = @"SELECT  p.projectnum as projectnum,p.projectname as projectname, p.showmenu as showmenu, p.page_id as page_id, p.page_title as page_title, l.groupcode as groupcode , l.loginkey as loginkey
                                FROM public.pro_login l
                                LEFT JOIN public.pro_permission p ON l.userid = p.userid
                                WHERE l.userid = @cs
                                  AND l.groupcode like '99999999%' 
                                LIMIT 1 "; 
                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@cs", cs)
                };

                DataTable dt = await _postgreLib.ExecuteQueryAsync(sql2, parameters, "Postgres", ct: ct);

                if (dt.Rows.Count > 0)
                {
                    string showmenu = dt.Rows[0]["showmenu"]?.ToString() ?? "";
                    string projectnum = dt.Rows[0]["projectnum"]?.ToString() ?? "";
                    string projectname = dt.Rows[0]["projectname"]?.ToString() ?? "";
                    string page_id = dt.Rows[0]["page_id"]?.ToString() ?? "";
                    string page_title = dt.Rows[0]["page_title"]?.ToString() ?? "";
                    string groupcode = dt.Rows[0]["groupcode"]?.ToString() ?? "";
                    string loginkey = dt.Rows[0]["loginkey"]?.ToString() ?? "";
                    return (true, showmenu, projectname,  projectnum, page_id, page_title, groupcode, loginkey);
                }

                return (false, "", "", "", "", "", "", "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository] GetCustomerLoginInfoAsync Error: {ex.Message}");
                return (false, "", "", "", "", "", "", "");
            }
        }











        public async Task<DataTable> SelectProLoginDetailAsync(string userid, CancellationToken ct = default)
        {
            string sql = "SELECT * FROM public.pro_login WHERE userid = @userid LIMIT 1";
            var parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@userid", userid) };
            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct: ct);
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<bool> CheckUserPasswordAsync(string userid, string encryptedPassword, CancellationToken ct = default)
        {
            string sql = "SELECT 1 FROM public.pro_login WHERE userid = @userid AND password = @password LIMIT 1";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userid", userid),
                new NpgsqlParameter("@password", encryptedPassword)
            };
            try
            {
                var dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct: ct);
                return dt != null && dt.Rows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<int> InsertUserAsync(string id, string passwordAes, string position, string username, int groupcode, string? expiration = null, CancellationToken ct = default)
        {
            string sql = @"INSERT INTO public.pro_login (userid, password, groupposition, username, groupcode, expiration, loginkey)
                           VALUES (@ID, @PASSWORD, @POSITION, @USERNAME, @GROUPCODE, @expiration, @ID)";
            if (string.IsNullOrEmpty(expiration))
            {
                if (groupcode < 999999999)
                {
                    expiration = "2125-12-31 23:59:59";
                }
            }
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@ID", id),
                new NpgsqlParameter("@PASSWORD", passwordAes),
                new NpgsqlParameter("@POSITION", position ?? ""),
                new NpgsqlParameter("@USERNAME", username ?? ""),
                new NpgsqlParameter("@GROUPCODE", groupcode.ToString()),
                new NpgsqlParameter("@expiration", (object?)expiration ?? DBNull.Value)
            };
            try
            {
                return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct: ct);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<int> UpdateUserLoginKeyAsync(string userid, string loginkey, CancellationToken ct = default)
        {
            string sql = "UPDATE public.pro_login SET loginkey = @loginkey WHERE userid = @userid";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@userid", userid),
                new NpgsqlParameter("@loginkey", loginkey)
            };
            try
            {
                return await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct: ct);
            }
            catch
            {
                return 0;
            }
        }

        // ── ProList (문항 목록) DB 구현체 ──
        // 원본: Jsonload.cs SelectQnumList → PostgresqlLib.select_project_list(user, 214, "", projectnum)
        // surveyon_variables 맵 순서 동기화 + SQL Window Function으로 merge_qnum 그룹별 mapid 최솟값 기반 정렬
        public async Task<DataTable> SelectProQuestionListAsync(string projectnum, string search = "", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum)) return new DataTable();
            projectnum = projectnum.Trim();

            // 1. 데이터관리 맵(surveyon_variables) 순서 기준으로 project_list.mapid 자동 동기화
            try
            {
                await SyncProjectListMapIdFromSurveyonVariablesAsync(projectnum, ct);
            }
            catch (Exception exSync)
            {
                Console.WriteLine($"[SelectProQuestionListAsync] SyncProjectListMapId warning: {exSync.Message}");
            }

            // 2. 문항통합(merge_qnum) 그룹별 첫 번째 순서 문항(group_min_mapid) 및 그룹 내 원본 순서(mapid ASC) 정렬
            string sql = @"SELECT id, mapid, projectnum, projectname, projectpof, servername, model, qnum, qnum_text,
                                  question_fin, question_origin, status, status_cnt, status_cnt_fin,
                                  register_date, register_userID, use_group, use_ID, use_name, update_date,
                                  merge_pn, merge_qnum, merge_qnum_check, useYN, project_lock,
                                  lb_info, res_info, lvcode,
                                  qnum_join1, qnum_join2, qnum_join3, qnum_join4, qnum_join5,
                                  MIN(mapid) OVER (PARTITION BY COALESCE(NULLIF(merge_qnum, ''), qnum)) AS group_min_mapid
                           FROM   public.project_list
                           WHERE  merge_pn = @projectnum";
            var parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) };

            if (!string.IsNullOrWhiteSpace(search))
            {
                sql += " AND (question_fin LIKE @search OR question_origin LIKE @search)";
                parameters.Add(new NpgsqlParameter("@search", $"%{search}%"));
            }

            sql += " ORDER BY group_min_mapid ASC, mapid ASC, id ASC";
            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<bool> UpdateQuestionAnalysisYnAsync(string projectnum, string qnum = "", string analysisYn = "0", bool isAll = false, CancellationToken ct = default)
        {
            if (!isAll && string.IsNullOrWhiteSpace(qnum)) return false;

            string val = (analysisYn == "1" || analysisYn == "분석") ? "분석" : "제외";
            string sql = "UPDATE public.project_list SET useYN = @val WHERE (merge_pn = @projectnum OR projectnum = @projectnum)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@val", val),
                new NpgsqlParameter("@projectnum", projectnum)
            };

            if (!isAll && !string.IsNullOrWhiteSpace(qnum))
            {
                sql += " AND (qnum = @qnum OR qnum_text = @qnum)";
                parameters.Add(new NpgsqlParameter("@qnum", qnum));
            }

            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateQuestionEditYnAsync(string projectnum, string qnum = "", string editYn = "0", bool isAll = false, CancellationToken ct = default)
        {
            if (!isAll && string.IsNullOrWhiteSpace(qnum)) return false;

            string val = (editYn == "1" || editYn == "수정가능" || editYn == "가능") ? "가능" : "수정불가";
            string sql = "UPDATE public.project_list SET project_lock = @val WHERE (merge_pn = @projectnum OR projectnum = @projectnum)";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@val", val),
                new NpgsqlParameter("@projectnum", projectnum)
            };

            if (!isAll && !string.IsNullOrWhiteSpace(qnum))
            {
                sql += " AND (qnum = @qnum OR qnum_text = @qnum)";
                parameters.Add(new NpgsqlParameter("@qnum", qnum));
            }

            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SaveQuestionMergeAsync(string projectnum, string jsonData, CancellationToken ct = default)
        {
            try
            {
                var arr = Newtonsoft.Json.Linq.JArray.Parse(jsonData);
                foreach (Newtonsoft.Json.Linq.JObject obj in arr)
                {
                    string qnum = obj["qnum"]?.ToString() ?? "";
                    string mergeQnum = obj["merge_qnum"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(qnum))
                    {
                        string sql = "UPDATE public.project_list SET merge_qnum = @mergeQnum WHERE projectnum = @projectnum AND (qnum = @qnum OR qnum_text = @qnum)";
                        var parameters = new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@mergeQnum", mergeQnum),
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@qnum", qnum)
                        };
                        await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<DataTable> SelectQuestionLabelsAsync(string projectnum, string mode = "excel", CancellationToken ct = default)
        {
            string sql = "SELECT * FROM public.pn_lb WHERE projectnum = @projectnum ORDER BY insert_date DESC";
            var parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) };
            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<bool> ImportQuestionLabelsAllAsync(string projectnum, string user, string jsonLabels, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum) || string.IsNullOrWhiteSpace(jsonLabels)) return false;

            try
            {
                JArray arr;
                if (jsonLabels.TrimStart().StartsWith("["))
                {
                    arr = JArray.Parse(jsonLabels);
                }
                else
                {
                    JObject obj = JObject.Parse(jsonLabels);
                    arr = obj["data_lb"] as JArray ?? new JArray();
                }

                if (arr.Count == 0) return false;

                var varDict = new Dictionary<string, List<LvCodeItem>>();
                foreach (JObject itemObj in arr.OfType<JObject>())
                {
                    foreach (var prop in itemObj.Properties())
                    {
                        string varName = prop.Name;
                        var items = prop.Value.ToObject<List<LvCodeItem>>() ?? new List<LvCodeItem>();
                        if (!varDict.ContainsKey(varName))
                        {
                            varDict[varName] = new List<LvCodeItem>();
                        }
                        varDict[varName].AddRange(items);
                    }
                }

                if (varDict.Count == 0) return false;

                bool anySuccess = false;
                foreach (var entry in varDict)
                {
                    string variable = entry.Key;
                    var itemList = entry.Value;
                    if (itemList.Count == 0) continue;

                    string selectSql = @"SELECT merge_qnum, qnum 
                                         FROM public.project_list 
                                         WHERE projectnum = @projectnum 
                                           AND (qnum = @variable OR merge_qnum = @variable OR qnum_text = @variable) 
                                         LIMIT 1";
                    var selParams = new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@variable", variable)
                    };

                    DataTable dtPl = await _postgreLib.ExecuteQueryAsync(selectSql, selParams, "Postgres", ct);
                    string targetQnum = variable;
                    if (dtPl.Rows.Count > 0)
                    {
                        string mq = dtPl.Rows[0]["merge_qnum"]?.ToString()?.TrimEnd() ?? "";
                        string q = dtPl.Rows[0]["qnum"]?.ToString()?.TrimEnd() ?? "";
                        if (!string.IsNullOrEmpty(mq)) targetQnum = mq;
                        else if (!string.IsNullOrEmpty(q)) targetQnum = q;
                    }

                    foreach (var row in itemList)
                    {
                        row.qnum = targetQnum;
                        row.ex_gubun = "설문(수정불가)";
                    }

                    string resultJson = JsonConvert.SerializeObject(itemList);

                    bool ok = await InsertUpdatePnLbAsync(projectnum, targetQnum, resultJson, ct);
                    if (ok) anySuccess = true;
                }

                return anySuccess;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImportQuestionLabelsAllAsync ERROR] projectnum={projectnum}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public async Task<DataTable> SelectProjectAnswersAllAsync(string projectnum, CancellationToken ct = default)
        {
            string sql = "SELECT * FROM public.surveyon_answer WHERE pn = @projectnum LIMIT 1000";
            var parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) };
            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct);
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<bool> SaveFilterSettingsAsync(string projectnum, string qnum, string filterDataJson, bool isAll = false, CancellationToken ct = default)
        {
            string sql = @"INSERT INTO public.pn_lb (projectnum, qnum, insert_date, result_json)
                           VALUES (@projectnum, @qnum, NOW(), @filterDataJson::jsonb)
                           ON CONFLICT (projectnum, qnum) 
                           DO UPDATE SET result_json = @filterDataJson::jsonb, update_date = NOW()";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@projectnum", projectnum),
                new NpgsqlParameter("@qnum", isAll ? "FILTER_ALL" : qnum),
                new NpgsqlParameter("@filterDataJson", filterDataJson)
            };
            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<DataTable> SelectFilterQuestionListAsync(string projectnum, string qnum, CancellationToken ct = default)
        {
            string sql = "SELECT * FROM public.project_list WHERE projectnum = @projectnum";
            var parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) };
            if (!string.IsNullOrWhiteSpace(qnum))
            {
                sql += " AND (qnum = @qnum OR qnum_text = @qnum)";
                parameters.Add(new NpgsqlParameter("@qnum", qnum));
            }
            try
            {
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct);
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<DataTable> HdataSelectDataMapQnumAllAsync(string pn, CancellationToken ct = default)
        {
            string sql = "SELECT * FROM public.surveyon_variables WHERE pn=@pn AND (type NOT IN ('multi', 'rank', 'maxrank', 'minrank') OR (open_rule IS NOT NULL AND open_rule != '')) ORDER BY ranking/1, id ASC";
            var parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pn", pn)
            };

            try
            {
                var dt = await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgreshdata", ct: ct);
                if (dt != null && dt.Rows.Count > 0) return dt;

                // Fallback to Postgres if Postgreshdata returns empty
                return await _postgreLib.ExecuteQueryAsync(sql, parameters, "Postgres", ct: ct);
            }
            catch
            {
                return new DataTable();
            }
        }

        public async Task<Newtonsoft.Json.Linq.JArray> GetRpsAnswerDataAsync(string projectnum, string qnumvariable, CancellationToken ct = default)
        {
            var listinfo = new Newtonsoft.Json.Linq.JArray();
            if (string.IsNullOrWhiteSpace(projectnum) || string.IsNullOrWhiteSpace(qnumvariable)) return listinfo;

            try
            {
                DataTable select_answer = await GetTAnswerCountAsync(projectnum, "4", "10", "", ct);
                if (select_answer == null || select_answer.Rows.Count == 0) return listinfo;

                foreach (DataRow row in select_answer.Rows)
                {
                    string pid = row["pid"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(pid)) continue;

                    DataTable select_answer_pid = await GetTAnswerCountAsync(projectnum, "4", "11", pid, ct);
                    if (select_answer_pid == null || select_answer_pid.Rows.Count == 0) continue;

                    string jsonData = select_answer_pid.Rows[0]["originalData"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(jsonData)) continue;

                    try
                    {
                        var root = Newtonsoft.Json.Linq.JObject.Parse(jsonData);
                        var rootObj = root["ROOT"] as Newtonsoft.Json.Linq.JObject;
                        if (rootObj == null) continue;

                        string result = "";
                        if (qnumvariable.Contains("_op"))
                        {
                            string parentKey = qnumvariable.Split('_')[0];
                            string openValue = rootObj[parentKey]?[qnumvariable]?["@OPEN"]?.ToString() ?? "";
                            if (string.IsNullOrEmpty(openValue) || openValue.Contains("data:image/png;base64"))
                            {
                                continue;
                            }
                            result = openValue.Replace("∬CanDK", "").Replace("CanDK", "");
                        }
                        else
                        {
                            Newtonsoft.Json.Linq.JObject? sectionObj = null;
                            if (rootObj[qnumvariable] is Newtonsoft.Json.Linq.JObject directObj)
                            {
                                sectionObj = directObj;
                            }
                            else if (qnumvariable.Contains("_"))
                            {
                                string parentKey = qnumvariable.Split('_')[0];
                                if (rootObj[parentKey] is Newtonsoft.Json.Linq.JObject pObj)
                                {
                                    sectionObj = pObj[qnumvariable] as Newtonsoft.Json.Linq.JObject;
                                }
                            }

                            string code = sectionObj?["@CODE"]?.ToString() ?? "";
                            string label = sectionObj?["@LABEL"]?.ToString() ?? "";

                            if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(label))
                            {
                                result = $"({code}){label}";
                            }
                            else
                            {
                                result = sectionObj?.ToString() ?? "";
                            }
                        }

                        if (!string.IsNullOrEmpty(result))
                        {
                            listinfo.Add(new Newtonsoft.Json.Linq.JObject
                            {
                                ["pid"] = pid,
                                ["qnumjoin_val"] = result
                            });
                        }
                    }
                    catch
                    {
                        // Skip malformed JSON row
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[GetRpsAnswerDataAsync Error] {ex.Message}");
            }

            return listinfo;
        }

        public async Task<Newtonsoft.Json.Linq.JArray> GetRpsAnswerDataHdataAsync(string projectnum, string qnumvariable, CancellationToken ct = default)
        {
            var listinfo = new Newtonsoft.Json.Linq.JArray();
            if (string.IsNullOrWhiteSpace(projectnum) || string.IsNullOrWhiteSpace(qnumvariable)) return listinfo;

            try
            {
                // Postgreshdata의 surveyon_variables + surveyon_labels에서 보기 라벨(code, label) 사전 조회
                var labelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!qnumvariable.Contains("_op"))
                {
                    string sqlLabel = @"
                        SELECT l.code, l.label 
                        FROM public.surveyon_variables v
                        JOIN public.surveyon_labels l ON l.variable_id = v.id
                        WHERE v.pn = @pn AND (v.c_question_variable = @qnumvariable OR v.s_question_variable = @qnumvariable)";
                    
                    var dtLabels = await _postgreLib.ExecuteQueryAsync(sqlLabel, new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@pn", projectnum),
                        new NpgsqlParameter("@qnumvariable", qnumvariable)
                    }, "Postgreshdata", ct);

                    if (dtLabels != null && dtLabels.Rows.Count > 0)
                    {
                        foreach (DataRow lRow in dtLabels.Rows)
                        {
                            string c = lRow["code"]?.ToString()?.Trim() ?? "";
                            string lbl = lRow["label"]?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(c))
                            {
                                labelMap[c] = lbl;
                            }
                        }
                    }
                }

                DataTable select_answer = await GetTAnswerCountHdataAsync(projectnum, "4", "10", "", ct);
                if (select_answer == null || select_answer.Rows.Count == 0) return listinfo;

                foreach (DataRow row in select_answer.Rows)
                {
                    string pid = row["pid"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(pid)) continue;

                    DataTable select_answer_pid = await GetTAnswerCountHdataAsync(projectnum, "4", "11", pid, ct);
                    if (select_answer_pid == null || select_answer_pid.Rows.Count == 0) continue;

                    string jsonData = select_answer_pid.Rows[0]["response_data"]?.ToString()?.Trim() 
                                   ?? select_answer_pid.Rows[0]["originalData"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(jsonData)) continue;

                    try
                    {
                        var root = Newtonsoft.Json.Linq.JObject.Parse(jsonData);
                        var rootObj = root["ROOT"] as Newtonsoft.Json.Linq.JObject;
                        if (rootObj == null) continue;

                        string result = "";
                        if (qnumvariable.Contains("_op"))
                        {
                            string parentKey = qnumvariable.Split('_')[0];
                            string openValue = rootObj[parentKey]?[qnumvariable]?["@OPEN"]?.ToString() ?? "";
                            if (string.IsNullOrEmpty(openValue) || openValue.Contains("data:image/png;base64"))
                            {
                                continue;
                            }
                            result = openValue.Replace("∬CanDK", "").Replace("CanDK", "");
                        }
                        else
                        {
                            Newtonsoft.Json.Linq.JObject? sectionObj = null;
                            if (rootObj[qnumvariable] is Newtonsoft.Json.Linq.JObject directObj)
                            {
                                sectionObj = directObj;
                            }
                            else if (qnumvariable.Contains("_"))
                            {
                                string parentKey = qnumvariable.Split('_')[0];
                                if (rootObj[parentKey] is Newtonsoft.Json.Linq.JObject pObj)
                                {
                                    sectionObj = pObj[qnumvariable] as Newtonsoft.Json.Linq.JObject;
                                }
                            }

                            string code = sectionObj?["@CODE"]?.ToString()?.Trim() ?? "";
                            string label = "";
                            if (!string.IsNullOrEmpty(code) && labelMap.TryGetValue(code, out var dbLabel))
                            {
                                label = dbLabel;
                            }
                            else
                            {
                                label = sectionObj?["@LABEL"]?.ToString()?.Trim() ?? "";
                            }

                            if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(label))
                            {
                                result = $"({code}){label}";
                            }
                            else
                            {
                                result = sectionObj?.ToString() ?? "";
                            }
                        }

                        if (!string.IsNullOrEmpty(result))
                        {
                            listinfo.Add(new Newtonsoft.Json.Linq.JObject
                            {
                                ["pid"] = pid,
                                ["qnumjoin_val"] = result
                            });
                        }
                    }
                    catch
                    {
                        // Skip malformed JSON row
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[GetRpsAnswerDataHdataAsync Error] {ex.Message}");
            }

            return listinfo;
        }

        // ── ASP.NET 레거시 기준 필터문항 선택 목록 조회 ──
        public async Task<Newtonsoft.Json.Linq.JArray> GetFilterSelectQnumListAsync(string projectnum, string qnum, CancellationToken ct = default)
        {
            var listinfo = new Newtonsoft.Json.Linq.JArray();
            try
            {
                // 1. 대상 문항의 기존 qnum_join1~5 파싱 (dbQnumSet)
                var dbQnumSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(qnum))
                {
                    string sqlTarget;
                    List<NpgsqlParameter> targetParams;
                    if (int.TryParse(qnum, out int targetId))
                    {
                        sqlTarget = "SELECT qnum_join1, qnum_join2, qnum_join3, qnum_join4, qnum_join5 FROM public.project_list WHERE merge_pn = @projectnum AND id = @id LIMIT 1";
                        targetParams = new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@id", targetId)
                        };
                    }
                    else
                    {
                        sqlTarget = "SELECT qnum_join1, qnum_join2, qnum_join3, qnum_join4, qnum_join5 FROM public.project_list WHERE merge_pn = @projectnum AND (qnum = @qnum OR id::text = @qnum) LIMIT 1";
                        targetParams = new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@qnum", qnum)
                        };
                    }

                    var dtTarget = await _postgreLib.ExecuteQueryAsync(sqlTarget, targetParams, "Postgres", ct);

                    if (dtTarget.Rows.Count > 0)
                    {
                        DataRow targetRow = dtTarget.Rows[0];
                        for (int j = 1; j <= 5; j++)
                        {
                            string rawVal = targetRow[$"qnum_join{j}"]?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(rawVal))
                            {
                                try
                                {
                                    var jo = Newtonsoft.Json.Linq.JObject.Parse(rawVal);
                                    foreach (var prop in jo.Properties())
                                    {
                                        if (!string.IsNullOrEmpty(prop.Name)) dbQnumSet.Add(prop.Name);
                                    }
                                }
                                catch
                                {
                                    dbQnumSet.Add(rawVal);
                                }
                            }
                        }
                    }
                }

                // 2. surveyon_variables 맵 정보 조회 (hdataselect_datamap_qnumall)
                DataTable select_map = await HdataSelectDataMapQnumAllAsync(projectnum, ct);

                if (select_map.Rows.Count > 0)
                {
                    foreach (DataRow row in select_map.Rows)
                    {
                        string qnum_id = row["id"]?.ToString()?.TrimEnd() ?? "";
                        string label = row["label"]?.ToString()?.TrimEnd() ?? "";
                        string re_label = row["re_label"]?.ToString()?.TrimEnd() ?? "";
                        string qnum_question = !string.IsNullOrEmpty(re_label) ? re_label : label;

                        string qnum_variable = row["c_question_variable"]?.ToString()?.TrimEnd() ?? "";
                        string qnum_type = row["type"]?.ToString()?.TrimEnd() ?? "single";

                        if (string.Equals(qnum_variable, "pid", StringComparison.OrdinalIgnoreCase)) continue;

                        bool isExistInDb = dbQnumSet.Contains(qnum_variable);

                        listinfo.Add(new Newtonsoft.Json.Linq.JObject
                        {
                            ["qnum_id"] = qnum_id,
                            ["qnum_question"] = qnum_question,
                            ["qnum_variable"] = qnum_variable,
                            ["qnum_type"] = qnum_type,
                            ["qnum_db"] = isExistInDb ? "true" : "false"
                        });
                    }
                }
                else
                {
                    // Fallback to project_list if surveyon_variables is empty
                    string sqlMap = "SELECT id, mapid, qnum, qnum_text, question_fin, question_origin, model FROM public.project_list WHERE merge_pn = @projectnum ORDER BY mapid ASC";
                    var dtMap = await _postgreLib.ExecuteQueryAsync(sqlMap, new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) }, "Postgres", ct);

                    foreach (DataRow row in dtMap.Rows)
                    {
                        string qnum_id = row["id"]?.ToString()?.TrimEnd() ?? "";
                        string qnum_question = row["question_fin"]?.ToString()?.TrimEnd() ?? "";
                        if (string.IsNullOrEmpty(qnum_question)) qnum_question = row["question_origin"]?.ToString()?.TrimEnd() ?? "";

                        string qnum_code = row["qnum"]?.ToString()?.TrimEnd() ?? "";
                        string qnum_text = row["qnum_text"]?.ToString()?.TrimEnd() ?? "";
                        string qnum_variable = !string.IsNullOrEmpty(qnum_code) ? qnum_code : qnum_text;
                        string qnum_type = row["model"]?.ToString()?.TrimEnd() ?? "single";

                        if (string.Equals(qnum_variable, "pid", StringComparison.OrdinalIgnoreCase)) continue;

                        bool isExistInDb = dbQnumSet.Contains(qnum_variable) || dbQnumSet.Contains(qnum_text) || dbQnumSet.Contains(qnum_code);

                        listinfo.Add(new Newtonsoft.Json.Linq.JObject
                        {
                            ["qnum_id"] = qnum_id,
                            ["qnum_question"] = qnum_question,
                            ["qnum_variable"] = qnum_variable,
                            ["qnum"] = qnum_code,
                            ["qnum_text"] = qnum_text,
                            ["qnum_type"] = qnum_type,
                            ["qnum_db"] = isExistInDb ? "true" : "false"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[GetFilterSelectQnumListAsync Error] {ex.Message}");
            }
            return listinfo;
        }

        /// <summary>
        /// answer_origin JSON(배열 또는 객체)에서 해당 문항에 실제로 응답한 고유 PID 집합 추출
        /// </summary>
        private HashSet<string> ExtractPidsFromAnswerOrigin(string? jsonAnswerOrigin, string qnum, string qnumText, string mergeQnum)
        {
            var pids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(jsonAnswerOrigin)) return pids;

            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(jsonAnswerOrigin);
                Newtonsoft.Json.Linq.JArray? items = null;

                if (token is Newtonsoft.Json.Linq.JArray arr)
                {
                    items = arr;
                }
                else if (token is Newtonsoft.Json.Linq.JObject obj)
                {
                    if (!string.IsNullOrEmpty(qnum) && obj[qnum] is Newtonsoft.Json.Linq.JArray a1) items = a1;
                    else if (!string.IsNullOrEmpty(qnumText) && obj[qnumText] is Newtonsoft.Json.Linq.JArray a2) items = a2;
                    else if (!string.IsNullOrEmpty(mergeQnum) && obj[mergeQnum] is Newtonsoft.Json.Linq.JArray a3) items = a3;
                    else if (obj.Properties().FirstOrDefault()?.Value is Newtonsoft.Json.Linq.JArray a4) items = a4;
                }

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (item is Newtonsoft.Json.Linq.JObject jo && jo["pid"] != null)
                        {
                            string pid = jo["pid"]!.ToString().Trim();
                            if (!string.IsNullOrEmpty(pid)) pids.Add(pid);
                        }
                    }
                }
            }
            catch { }

            return pids;
        }

        // ── ASP.NET 레거시 기준 단일/통합 문항 필터 설정 저장 (해당 문항에 응답한 PID 기준 필터링) ──
        public async Task<bool> UpdateFilterSingleAsync(string projectnum, string qnum, List<string> selectedVars, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectnum) || string.IsNullOrWhiteSpace(qnum)) return false;

                // 1. 대상 문항 식별 (id 숫자 우선 매칭, 아니면 qnum 변수명 매칭)
                DataTable dtTarget;
                if (int.TryParse(qnum, out int targetId))
                {
                    string sqlFindById = "SELECT id, qnum, qnum_text, merge_qnum, answer_origin FROM public.project_list WHERE merge_pn = @projectnum AND id = @id LIMIT 1";
                    dtTarget = await _postgreLib.ExecuteQueryAsync(sqlFindById, new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@id", targetId)
                    }, "Postgres", ct);
                }
                else
                {
                    string sqlFindByQnum = "SELECT id, qnum, qnum_text, merge_qnum, answer_origin FROM public.project_list WHERE merge_pn = @projectnum AND (qnum = @qnum OR id::text = @qnum) LIMIT 1";
                    dtTarget = await _postgreLib.ExecuteQueryAsync(sqlFindByQnum, new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@qnum", qnum)
                    }, "Postgres", ct);
                }

                if (dtTarget == null || dtTarget.Rows.Count == 0)
                {
                    System.Console.WriteLine($"[UpdateFilterSingleAsync] 대상 문항을 찾을 수 없습니다: pn={projectnum}, qnum={qnum}");
                    return false;
                }

                DataRow targetRow = dtTarget.Rows[0];
                string mergeQnum = targetRow["merge_qnum"]?.ToString()?.Trim() ?? "";

                // 2. 통합 문항(merge_qnum 존재) 여부 판별 및 타겟 행들 수집
                var targetRows = new List<DataRow>();
                if (!string.IsNullOrEmpty(mergeQnum))
                {
                    // [통합 문항]: 동일 merge_qnum을 가진 모든 문항 조회
                    string sqlGroup = "SELECT id, qnum, qnum_text, merge_qnum, answer_origin FROM public.project_list WHERE merge_pn = @projectnum AND merge_qnum = @merge_qnum";
                    var dtGroup = await _postgreLib.ExecuteQueryAsync(sqlGroup, new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@merge_qnum", mergeQnum)
                    }, "Postgres", ct);

                    foreach (DataRow r in dtGroup.Rows)
                    {
                        targetRows.Add(r);
                    }
                }
                else
                {
                    // [단독 문항]: 오직 해당 문항 행만 수집 (격리 저장)
                    targetRows.Add(targetRow);
                }

                if (targetRows.Count == 0) targetRows.Add(targetRow);

                // 3. selectedVars에 대해 HData 응답데이터 사전 조회 및 캐싱 (중복 조회 방지)
                var varDataMap = new Dictionary<int, (string varName, List<(string pid, string val)> data)>();
                int vIdx = 0;
                foreach (var v in selectedVars)
                {
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    vIdx++;
                    if (vIdx > 5) break;

                    var answerDataResult = await GetRpsAnswerDataHdataAsync(projectnum, v, ct);
                    var pidValList = new List<(string pid, string val)>();
                    if (answerDataResult != null)
                    {
                        foreach (Newtonsoft.Json.Linq.JObject item in answerDataResult.OfType<Newtonsoft.Json.Linq.JObject>())
                        {
                            string pid = item["pid"]?.ToString()?.Trim() ?? "";
                            string val = item["qnumjoin_val"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(pid))
                            {
                                pidValList.Add((pid, val));
                            }
                        }
                    }
                    varDataMap[vIdx] = (v, pidValList);
                }

                // 4. 대상 각 문항(행)별로 실제 응답한 PID(answer_origin)를 추출하여 매칭되는 필터값만 qnum_join1~5에 저장
                foreach (DataRow row in targetRows)
                {
                    if (row["id"] == null || !int.TryParse(row["id"]?.ToString(), out int tid)) continue;

                    string rowQnum = row["qnum"]?.ToString()?.Trim() ?? "";
                    string rowQnumText = row["qnum_text"]?.ToString()?.Trim() ?? "";
                    string rowMergeQnum = row["merge_qnum"]?.ToString()?.Trim() ?? "";
                    string rowAnswerOrigin = row["answer_origin"]?.ToString() ?? "";

                    // 해당 문항에 실제로 응답한 PID 집합 추출
                    var validPids = ExtractPidsFromAnswerOrigin(rowAnswerOrigin, rowQnum, rowQnumText, rowMergeQnum);

                    // 4-1. 기존 qnum_join1~5 NULL 초기화
                    for (int j = 1; j <= 5; j++)
                    {
                        string sqlClear = $"UPDATE public.project_list SET qnum_join{j} = NULL WHERE merge_pn = @projectnum AND id = @id";
                        await _postgreLib.ExecuteNonQueryAsync(sqlClear, new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@id", tid)
                        }, "Postgres", ct);
                    }

                    // 4-2. validPids에 속한 응답자 PID만 선별하여 qnum_join{joinIdx} 업데이트
                    foreach (var kvp in varDataMap)
                    {
                        int joinIdx = kvp.Key;
                        string varName = kvp.Value.varName;
                        var allPidValList = kvp.Value.data;

                        var filteredJArray = new Newtonsoft.Json.Linq.JArray();
                        foreach (var (pid, val) in allPidValList)
                        {
                            // 해당 문항에 실제로 응답한 PID인 경우에만 추가!
                            if (validPids.Contains(pid))
                            {
                                filteredJArray.Add(new Newtonsoft.Json.Linq.JObject
                                {
                                    ["pid"] = pid,
                                    ["qnumjoin_val"] = val
                                });
                            }
                        }

                        var answerDataAll = new Newtonsoft.Json.Linq.JObject
                        {
                            [varName] = filteredJArray
                        };

                        string sqlUpdate = $"UPDATE public.project_list SET qnum_join{joinIdx} = @val::jsonb WHERE merge_pn = @projectnum AND id = @id";
                        await _postgreLib.ExecuteNonQueryAsync(sqlUpdate, new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@val", answerDataAll.ToString(Formatting.None)),
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@id", tid)
                        }, "Postgres", ct);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[UpdateFilterSingleAsync Error] {ex.Message}");
                return false;
            }
        }

        // ── ASP.NET 레거시 기준 전체 분석 문항 일괄 필터 설정 저장 (해당 문항에 응답한 PID 기준 필터링) ──
        public async Task<bool> UpdateFilterAllAsync(string projectnum, List<string> selectedVars, CancellationToken ct = default)
        {
            try
            {
                // 1. useYN = '분석'인 전체 문항 조회 (answer_origin 포함)
                string sqlTargetList = "SELECT id, qnum_text, qnum, merge_qnum, answer_origin FROM public.project_list WHERE merge_pn = @projectnum AND useYN = '분석'";
                var dtTargets = await _postgreLib.ExecuteQueryAsync(sqlTargetList, new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) }, "Postgres", ct);

                // 2. selectedVars에 대해 HData 응답데이터 사전 조회 및 캐싱
                var varDataMap = new Dictionary<int, (string varName, List<(string pid, string val)> data)>();
                int vIdx = 0;
                foreach (var v in selectedVars)
                {
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    vIdx++;
                    if (vIdx > 5) break;

                    var answerDataResult = await GetRpsAnswerDataHdataAsync(projectnum, v, ct);
                    var pidValList = new List<(string pid, string val)>();
                    if (answerDataResult != null)
                    {
                        foreach (Newtonsoft.Json.Linq.JObject item in answerDataResult.OfType<Newtonsoft.Json.Linq.JObject>())
                        {
                            string pid = item["pid"]?.ToString()?.Trim() ?? "";
                            string val = item["qnumjoin_val"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(pid))
                            {
                                pidValList.Add((pid, val));
                            }
                        }
                    }
                    varDataMap[vIdx] = (v, pidValList);
                }

                foreach (DataRow row in dtTargets.Rows)
                {
                    string targetId = row["id"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(targetId) || !int.TryParse(targetId, out int idVal)) continue;

                    string rowQnum = row["qnum"]?.ToString()?.Trim() ?? "";
                    string rowQnumText = row["qnum_text"]?.ToString()?.Trim() ?? "";
                    string rowMergeQnum = row["merge_qnum"]?.ToString()?.Trim() ?? "";
                    string rowAnswerOrigin = row["answer_origin"]?.ToString() ?? "";

                    // 해당 문항에 실제로 응답한 PID 집합 추출
                    var validPids = ExtractPidsFromAnswerOrigin(rowAnswerOrigin, rowQnum, rowQnumText, rowMergeQnum);

                    // qnum_join1 ~ qnum_join5 컬럼 초기화
                    for (int j = 1; j <= 5; j++)
                    {
                        string sqlClear = $"UPDATE public.project_list SET qnum_join{j} = NULL WHERE merge_pn = @projectnum AND id = @id";
                        await _postgreLib.ExecuteNonQueryAsync(sqlClear, new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@id", idVal)
                        }, "Postgres", ct);
                    }

                    // validPids에 속한 응답자 PID만 선별하여 qnum_join{joinIdx} 업데이트
                    foreach (var kvp in varDataMap)
                    {
                        int joinIdx = kvp.Key;
                        string varName = kvp.Value.varName;
                        var allPidValList = kvp.Value.data;

                        var filteredJArray = new Newtonsoft.Json.Linq.JArray();
                        foreach (var (pid, val) in allPidValList)
                        {
                            if (validPids.Contains(pid))
                            {
                                filteredJArray.Add(new Newtonsoft.Json.Linq.JObject
                                {
                                    ["pid"] = pid,
                                    ["qnumjoin_val"] = val
                                });
                            }
                        }

                        var answerDataAll = new Newtonsoft.Json.Linq.JObject
                        {
                            [varName] = filteredJArray
                        };

                        string sqlUpdate = $"UPDATE public.project_list SET qnum_join{joinIdx} = @val::jsonb WHERE merge_pn = @projectnum AND id = @id";
                        await _postgreLib.ExecuteNonQueryAsync(sqlUpdate, new List<NpgsqlParameter>
                        {
                            new NpgsqlParameter("@val", answerDataAll.ToString(Formatting.None)),
                            new NpgsqlParameter("@projectnum", projectnum),
                            new NpgsqlParameter("@id", idVal)
                        }, "Postgres", ct);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[UpdateFilterAllAsync Error] {ex.Message}");
                return false;
            }
        }

        // ── ProList 배치 카운트/업데이트 (SelectQnumList N+1 최적화) ──

        /// <summary>
        /// 응답자수 배치 조회 (원본: select_exceldata_count를 행마다 호출 → 배치 1회로 최적화)
        /// project_list 테이블에서 merge_pn에 속하는 모든 행의 answer_origin jsonb 길이를 한 번에 반환
        /// 키: "projectnum|qnum", 값: pid_count
        /// </summary>
        public async Task<Dictionary<string, string>> SelectExceldataCountBatchAsync(string mergePn, CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>();
            // answer_origin이 JSONB Array([{"pid": "..."}, ...]) 구조 또는 JSONB Object({"qnum": [...]}) 구조인 경우 모두 대응
            const string sql = @"SELECT projectnum, qnum, answer_origin,
                                        CASE 
                                            WHEN answer_origin IS NOT NULL AND jsonb_typeof(answer_origin) = 'array'
                                                THEN jsonb_array_length(answer_origin)
                                            WHEN answer_origin IS NOT NULL AND jsonb_typeof(answer_origin) = 'object' AND jsonb_typeof(answer_origin->qnum) = 'array'
                                                THEN jsonb_array_length(answer_origin->qnum)
                                            ELSE 0
                                        END AS pid_count
                                 FROM   public.project_list
                                 WHERE  merge_pn = @mergePn OR projectnum = @mergePn";
            try
            {
                var dt = await _postgreLib.ExecuteQueryAsync(sql,
                    new List<NpgsqlParameter> { new("@mergePn", mergePn) }, "Postgres", ct);
                foreach (DataRow row in dt.Rows)
                {
                    string pnVal = row["projectnum"]?.ToString()?.TrimEnd() ?? "";
                    string qnumVal = row["qnum"]?.ToString()?.TrimEnd() ?? "";
                    string key = $"{pnVal}|{qnumVal}";

                    int pidCount = 0;
                    if (row.Table.Columns.Contains("pid_count") && row["pid_count"] != DBNull.Value)
                    {
                        int.TryParse(row["pid_count"]?.ToString(), out pidCount);
                    }

                    // SQL 계산 결과가 0이고 answer_origin 문자열이 존재하는 경우 C# JToken 다형성 파싱으로 2차 보정
                    if (pidCount == 0 && row.Table.Columns.Contains("answer_origin") && row["answer_origin"] != DBNull.Value)
                    {
                        string answerStr = row["answer_origin"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(answerStr))
                        {
                            try
                            {
                                JToken token = JToken.Parse(answerStr);
                                if (token is JArray arr)
                                {
                                    pidCount = arr.Count;
                                }
                                else if (token is JObject obj)
                                {
                                    if (obj[qnumVal] is JArray arr2)
                                    {
                                        pidCount = arr2.Count;
                                    }
                                    else
                                    {
                                        foreach (var prop in obj.Properties())
                                        {
                                            if (prop.Value is JArray pArr) pidCount += pArr.Count;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    result[key] = pidCount.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.SelectExceldataCountBatchAsync] Error: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 분석대상수 + 분석완료수 배치 조회 (원본: select_pn_in_count_origin + select_pn_in_count_fin 행마다 2회 → 배치 1회)
        /// pn_in 테이블에서 projectnum(=merge_pn) 기준으로 각 qnum별 카운트를 한 번에 반환
        /// 키: "merge_qnum", 값: (originCount, finCount)
        /// </summary>
        public async Task<Dictionary<string, (string originCount, string finCount)>> SelectPnInCountBatchAsync(string mergePn, CancellationToken ct = default)
        {
            var result = new Dictionary<string, (string originCount, string finCount)>();
            // 분석대상수: answer_origin 배열/객체 길이 (pn_in.qnum 기준)
            // 분석완료수: answer_fin 배열 중 lv123code가 있는 고유 fixed_key 수
            const string sql = @"SELECT 
                                    p.qnum,
                                    p.answer_origin,
                                    CASE 
                                        WHEN p.answer_origin IS NOT NULL AND jsonb_typeof(p.answer_origin) = 'array'
                                            THEN jsonb_array_length(p.answer_origin)
                                        WHEN p.answer_origin IS NOT NULL AND jsonb_typeof(p.answer_origin) = 'object' AND jsonb_typeof(p.answer_origin->p.qnum) = 'array'
                                            THEN jsonb_array_length(p.answer_origin->p.qnum)
                                        ELSE 0
                                    END AS origin_count,
                                    COALESCE(f.fin_count, 0) AS fin_count
                                 FROM public.pn_in p
                                 LEFT JOIN LATERAL (
                                     SELECT COUNT(DISTINCT elem->>'fixed_key') AS fin_count
                                     FROM jsonb_array_elements(
                                         CASE 
                                             WHEN p.answer_fin IS NOT NULL AND jsonb_typeof(p.answer_fin) = 'array' THEN p.answer_fin
                                             ELSE '[]'::jsonb
                                         END
                                     ) AS elem
                                     WHERE elem->>'lv123code' IS NOT NULL
                                       AND TRIM(elem->>'lv123code') != ''
                                 ) f ON TRUE
                                 WHERE p.projectnum = @mergePn ";
            try
            {
                var dt = await _postgreLib.ExecuteQueryAsync(sql,
                    new List<NpgsqlParameter> { new("@mergePn", mergePn) }, "Postgres", ct);
                foreach (DataRow row in dt.Rows)
                {
                    string qnum = row["qnum"]?.ToString()?.TrimEnd() ?? "";
                    int originCnt = 0;
                    if (row.Table.Columns.Contains("origin_count") && row["origin_count"] != DBNull.Value)
                    {
                        int.TryParse(row["origin_count"]?.ToString(), out originCnt);
                    }

                    // SQL 계산 결과가 0이고 answer_origin 문자열이 존재하는 경우 C# JToken 다형성 파싱으로 2차 보정
                    if (originCnt == 0 && row.Table.Columns.Contains("answer_origin") && row["answer_origin"] != DBNull.Value)
                    {
                        string answerStr = row["answer_origin"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(answerStr))
                        {
                            try
                            {
                                JToken token = JToken.Parse(answerStr);
                                if (token is JArray arr)
                                {
                                    originCnt = arr.Count;
                                }
                                else if (token is JObject obj)
                                {
                                    if (obj[qnum] is JArray arr2)
                                    {
                                        originCnt = arr2.Count;
                                    }
                                    else
                                    {
                                        foreach (var prop in obj.Properties())
                                        {
                                            if (prop.Value is JArray pArr) originCnt += pArr.Count;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    string finCnt = row["fin_count"]?.ToString() ?? "0";
                    result[qnum] = (originCnt.ToString(), finCnt);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.SelectPnInCountBatchAsync] Error: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// status 변경분 배치 업데이트 (원본: 행마다 update_list_info("status", status_text, id) → 배치 1회)
        /// </summary>
        public async Task UpdateListInfoBatchAsync(List<(string id, string status)> updates, CancellationToken ct = default)
        {
            if (updates == null || updates.Count == 0) return;
            try
            {
                // 개별 UPDATE 실행 (PostgreSQL은 VALUES 기반 배치도 가능하지만, 안정성을 위해 개별 실행)
                foreach (var (id, status) in updates)
                {
                    const string sql = "UPDATE public.project_list SET status = @status WHERE id = @id";
                    await _postgreLib.ExecuteNonQueryAsync(sql,
                        new List<NpgsqlParameter>
                        {
                            new("@status", status),
                            new("@id", int.Parse(id))
                        }, "Postgres", ct);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.UpdateListInfoBatchAsync] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// project_list 특정 id의 컬럼값 업데이트 (원본: update_list_info(columninfo, columnval, qid))
        /// analysis/rowEdit/merge gb 핸들러에서 사용
        /// </summary>
        public async Task<bool> UpdateListInfoByIdAsync(string columnName, string columnVal, string qid, CancellationToken ct = default)
        {
            // 허용된 컬럼만 동적 SQL에 사용 (SQL Injection 방지)
            var allowedColumns = new HashSet<string> { "status", "useYN", "project_lock", "merge_qnum", "merge_qnum_check", "model", "question_fin", "qnum_text", "qnum_join1", "qnum_join2", "qnum_join3", "qnum_join4", "qnum_join5" };
            if (!allowedColumns.Contains(columnName)) return false;

            string sql = $"UPDATE public.project_list SET {columnName} = @val WHERE id = @qid";
            var parameters = new List<NpgsqlParameter>
            {
                new("@val", string.IsNullOrEmpty(columnVal) ? (object)DBNull.Value : columnVal),
                new("@qid", int.Parse(qid))
            };
            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.UpdateListInfoByIdAsync] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// project_list merge_pn 기준 일괄 컬럼 업데이트 (원본: update_list_info(columninfo, columnval, null, merge_pn))
        /// allAnalysis/allEdit gb 핸들러에서 사용
        /// </summary>
        public async Task<bool> UpdateListInfoByMergePnAsync(string columnName, string columnVal, string mergePn, CancellationToken ct = default)
        {
            var allowedColumns = new HashSet<string> { "status", "useYN", "project_lock", "merge_qnum", "merge_qnum_check", "model", "question_fin", "qnum_text" };
            if (!allowedColumns.Contains(columnName)) return false;

            string sql = $"UPDATE public.project_list SET {columnName} = @val WHERE merge_pn = @mergePn";
            var parameters = new List<NpgsqlParameter>
            {
                new("@val", string.IsNullOrEmpty(columnVal) ? (object)DBNull.Value : columnVal),
                new("@mergePn", mergePn)
            };
            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.UpdateListInfoByMergePnAsync] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// project_list merge_qnum 기준 묶음 그룹 일괄 컬럼 업데이트 (analysis/rowEdit용)
        /// </summary>
        public async Task<bool> UpdateListInfoByMergeQnumAsync(string columnName, string columnVal, string mergePn, string mergeQnum, CancellationToken ct = default)
        {
            var allowedColumns = new HashSet<string> { "status", "useYN", "project_lock", "merge_qnum", "merge_qnum_check", "model", "question_fin", "qnum_text" };
            if (!allowedColumns.Contains(columnName)) return false;

            string sql = $"UPDATE public.project_list SET {columnName} = @val WHERE (merge_pn = @mergePn OR projectnum = @mergePn) AND merge_qnum = @mergeQnum";
            var parameters = new List<NpgsqlParameter>
            {
                new("@val", string.IsNullOrEmpty(columnVal) ? (object)DBNull.Value : columnVal),
                new("@mergePn", mergePn),
                new("@mergeQnum", mergeQnum)
            };
            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.UpdateListInfoByMergeQnumAsync] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 선택된 ids 및 merge_qnums 범위만 일괄 컬럼 업데이트 (하단 툴바 전용)
        /// </summary>
        public async Task<bool> BulkUpdateQuestionColumnAsync(string columnName, string columnVal, string mergePn, List<int> ids, List<string> mergeQnums, CancellationToken ct = default)
        {
            var allowedColumns = new HashSet<string> { "status", "useYN", "project_lock", "merge_qnum", "merge_qnum_check", "model", "question_fin", "qnum_text" };
            if (!allowedColumns.Contains(columnName)) return false;

            var conditions = new List<string>();
            var parameters = new List<NpgsqlParameter>
            {
                new("@val", string.IsNullOrEmpty(columnVal) ? (object)DBNull.Value : columnVal),
                new("@mergePn", mergePn)
            };

            if (ids != null && ids.Count > 0)
            {
                conditions.Add("id = ANY(@ids)");
                parameters.Add(new NpgsqlParameter("@ids", ids.ToArray()));
            }

            if (mergeQnums != null && mergeQnums.Count > 0)
            {
                conditions.Add("merge_qnum = ANY(@mergeQnums)");
                parameters.Add(new NpgsqlParameter("@mergeQnums", mergeQnums.ToArray()));
            }

            if (conditions.Count == 0) return true;

            string whereClause = string.Join(" OR ", conditions);
            string sql = $"UPDATE public.project_list SET {columnName} = @val WHERE (merge_pn = @mergePn OR projectnum = @mergePn) AND ({whereClause})";

            try
            {
                await _postgreLib.ExecuteNonQueryAsync(sql, parameters, "Postgres", ct);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainRepository.BulkUpdateQuestionColumnAsync] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 선택된 문항들의 question_fin 일괄/개별 편집 업데이트
        /// </summary>
        public async Task<bool> BatchUpdateQuestionFinAsync(string mergePn, Dictionary<string, string> finMap, CancellationToken ct = default)
        {
            if (finMap == null || finMap.Count == 0) return true;

            foreach (var kvp in finMap)
            {
                string qid = kvp.Key;
                string newFin = kvp.Value ?? "";
                await UpdateListInfoByIdAsync("question_fin", newFin, qid, ct);
            }
            return true;
        }

        /// <summary>
        /// 오픈서버정보(servername: NEW / RPS / QM)에 따라 조사DB 및 설문온DB 완료자(answerStateCode=4) PID 대조
        /// NEW = 수동등록이므로 조사DB 대조 제외 (isManual = true)
        /// <summary>
        /// project_list.answer_origin 컬럼에 등록된 고유(Distinct) PID Set 및 개수 조회 (C# Regex + HashSet 기반 유연 집계)
        /// </summary>
        public async Task<HashSet<string>> GetOpenRegisteredPidSetAsync(string projectnum, CancellationToken ct = default)
        {
            var pidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(projectnum)) return pidSet;
            const string sql = @"
                SELECT answer_origin
                FROM public.project_list
                WHERE (projectnum = @pn OR merge_pn = @pn)
                  AND answer_origin IS NOT NULL
                  AND answer_origin::text <> ''
                  AND answer_origin::text <> 'null';";
            try
            {
                var dt = await _postgreLib.ExecuteQueryAsync(sql,
                    new List<NpgsqlParameter> { new("@pn", projectnum) }, "Postgres", ct);

                if (dt != null && dt.Rows.Count > 0)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string jsonStr = row["answer_origin"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(jsonStr)) continue;

                        var matches = System.Text.RegularExpressions.Regex.Matches(
                            jsonStr,
                            @"""pid""\s*:\s*""?([^"",}\s\]]+)""?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        );

                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (match.Success && match.Groups.Count > 1)
                            {
                                string pid = match.Groups[1].Value.Trim();
                                if (!string.IsNullOrEmpty(pid) && !pid.Equals("null", StringComparison.OrdinalIgnoreCase))
                                {
                                    pidSet.Add(pid);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetOpenRegisteredPidSetAsync] Error: {ex.Message}");
            }
            return pidSet;
        }

        public async Task<int> GetOpenRegisteredPidCountAsync(string projectnum, CancellationToken ct = default)
        {
            var set = await GetOpenRegisteredPidSetAsync(projectnum, ct);
            return set.Count;
        }

        /// <summary>
        /// RPS = SqlServer_answer DB (database=SURVEY)
        /// QM = SqlServerqm_answer DB (database=SURVEY)
        /// </summary>
        public async Task<(bool isManual, int surveyCount, int surveyonCount, int openRegisteredCount, int aiWaitCount, List<string> onlyInSurvey, List<string> onlyInSurveyon, List<string> unregisteredOpen, List<string> onlyInOpen)> GetCompletedPidDiffAsync(string projectnum, string servername = "", CancellationToken ct = default)
        {
            var surveyPids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var surveyonPids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string sName = (servername ?? "").Trim();
            bool isManual = sName.Equals("NEW", StringComparison.OrdinalIgnoreCase);

            // 0. 오픈등록(추가) 고유 PID Set 조회
            var openPids = await GetOpenRegisteredPidSetAsync(projectnum, ct);
            int openRegisteredCount = openPids.Count;

            // 1. 설문온DB (Postgresqm / T_Answer) 조회 (기본 수행)
            try
            {
                const string sqlSurveyon = "SELECT pid FROM \"T_Answer\" WHERE \"questionnaireNumber\" = @pn AND \"answerStateCode\" = 4";
                var dtSurveyon = await _postgreLib.ExecuteQueryAsync(sqlSurveyon,
                    new List<NpgsqlParameter> { new("@pn", projectnum) }, "Postgresqm", ct);

                foreach (DataRow row in dtSurveyon.Rows)
                {
                    string pidStr = row["pid"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(pidStr)) surveyonPids.Add(pidStr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetCompletedPidDiffAsync] Postgresqm Query Error: {ex.Message}");
            }

            // AI오픈 대기수 및 실제 대기 PID 목록 계산 (데이터등록 -> 문항등록 DB탭과 100% 동일한 기준)
            var pendingOpenPidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int aiWaitCount = 0;
            try
            {
                var dbSelectItems = await GetProRegisterDbSelectInfoAsync(projectnum, sName, "", ct);
                if (dbSelectItems != null && dbSelectItems.Count > 0)
                {
                    foreach (JObject item in dbSelectItems.OfType<JObject>())
                    {
                        if (item["new_pids"] is JArray arr)
                        {
                            foreach (var p in arr)
                            {
                                string pStr = p?.ToString()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(pStr)) pendingOpenPidSet.Add(pStr);
                            }
                        }
                    }
                }
                aiWaitCount = pendingOpenPidSet.Count;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetCompletedPidDiffAsync] aiWaitCount calculation error: {ex.Message}");
                aiWaitCount = 0;
            }

            // 오픈등록수 연관 차이 계산: 실제 오픈 대기 PID 목록 및 설문 미매칭 오픈 PID
            var unregisteredOpen = pendingOpenPidSet.ToList();
            var onlyInOpen = openPids.Except(surveyonPids).ToList();

            // 2. 수동등록(NEW)인 경우 조사DB 조회 스킵
            if (isManual)
            {
                return (true, 0, surveyonPids.Count, openRegisteredCount, aiWaitCount, new List<string>(), new List<string>(), unregisteredOpen, onlyInOpen);
            }

            // 3. 조사DB (SqlServer_answer 또는 SqlServerqm_answer) 조회
            try
            {
                string connectionKey = sName.Equals("QM", StringComparison.OrdinalIgnoreCase) ? "SqlServerqm_answer" : "SqlServer_answer";
                string sqlSurvey = $"SELECT PID FROM T_SamplingList_{projectnum} WHERE answerStateCode = 4";
                var dtSurvey = await _msSqlLib.QueryAsync<dynamic>(sqlSurvey, null, connectionKey, ct: ct);
                if (dtSurvey != null)
                {
                    foreach (var item in dtSurvey)
                    {
                        var dict = item as IDictionary<string, object>;
                        if (dict != null && dict.TryGetValue("PID", out var val) && val != null)
                        {
                            string pidStr = val.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(pidStr)) surveyPids.Add(pidStr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetCompletedPidDiffAsync] SqlServer Query Error ({sName}): {ex.Message}");
            }

            // 4. 차이 계산
            var onlyInSurvey = surveyPids.Except(surveyonPids).ToList();
            var onlyInSurveyon = surveyonPids.Except(surveyPids).ToList();

            return (false, surveyPids.Count, surveyonPids.Count, openRegisteredCount, aiWaitCount, onlyInSurvey, onlyInSurveyon, unregisteredOpen, onlyInOpen);
        }

        /// <summary>
        /// 보기추출(개발자용) - 문항별 1~3단계 보기 레벨 정렬 데이터셋 생성
        private static string CleanVal(object? val)
        {
            if (val == null) return "";
            string s = val.ToString()?.Trim() ?? "";
            if (s.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            return s;
        }

        /// <summary>
        /// 보기추출(개발자용) - 1~3단계 보기 레이블 계층 구조 JSON 생성
        /// </summary>
        public async Task<JArray> GetExportLbExcelJsonAsync(string projectnum, CancellationToken ct = default)
        {
            DataTable dtList = await SelectProQuestionListAsync(projectnum, "", ct);
            var resultArr = new JArray();
            if (dtList.Rows.Count == 0) return resultArr;

            DataTable dtLb = await SelectQuestionLabelsAsync(projectnum, "excel", ct);
            var lbDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in dtLb.Rows)
            {
                string qn = r["qnum"]?.ToString()?.Trim() ?? "";
                string json = r["result_json"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(qn) && !string.IsNullOrEmpty(json))
                {
                    lbDict[qn] = json;
                }
            }

            foreach (DataRow row in dtList.Rows)
            {
                string useyn = row["useyn"]?.ToString()?.Trim() ?? "";
                if (useyn == "0" || useyn == "제외") continue;

                string qnum = row["qnum"]?.ToString()?.Trim() ?? "";
                string mergeQnum = row["merge_qnum"]?.ToString()?.Trim() ?? "";
                string questionOrigin = row["question_origin"]?.ToString()?.Trim() ?? row["question_fin"]?.ToString()?.Trim() ?? "";

                string targetQn = !string.IsNullOrEmpty(mergeQnum) ? mergeQnum : qnum;
                string jsonLb = "";
                if (!lbDict.TryGetValue(targetQn, out jsonLb) || string.IsNullOrEmpty(jsonLb))
                {
                    lbDict.TryGetValue(qnum, out jsonLb);
                }

                if (string.IsNullOrEmpty(jsonLb)) continue;

                try
                {
                    JArray lbArr = JArray.Parse(jsonLb);
                    var sortedList = lbArr.OfType<JObject>()
                        .OrderBy(o => int.TryParse(o["lv1code"]?.ToString(), out var v1) ? v1 : int.MaxValue)
                        .ThenBy(o => int.TryParse(o["lv2code"]?.ToString(), out var v2) ? v2 : int.MaxValue)
                        .ThenBy(o => int.TryParse(o["lv123code"]?.ToString(), out var v3) ? v3 : int.MaxValue);

                    foreach (JObject item in sortedList)
                    {
                        var rowObj = new JObject
                        {
                            ["qnum"] = qnum,
                            ["merge_qnum"] = mergeQnum,
                            ["question_origin"] = questionOrigin,
                            ["lv123code"] = CleanVal(item["lv123code"]),
                            ["lv3"] = CleanVal(item["lv3"]),
                            ["lv2code"] = CleanVal(item["lv2code"]),
                            ["lv2"] = CleanVal(item["lv2"]),
                            ["lv1code"] = CleanVal(item["lv1code"]),
                            ["lv1"] = CleanVal(item["lv1"])
                        };
                        resultArr.Add(rowObj);
                    }
                }
                catch { }
            }

            return resultArr;
        }

        private List<BrandItemWithPos> ComputeBrandItemPositions(List<BrandItem> items)
        {
            var specialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "기타", "없음", "없다", "모름", "무응답", "없음/없다", "모름/무응답", "없음/모름/무응답", "모름/없음/무응답"
            };

            bool IsSpecialText(string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                return specialNames.Contains(name.Trim());
            }

            bool HasCategoryGroup(BrandItem item) => !string.IsNullOrEmpty(item.Lv1) || !string.IsNullOrEmpty(item.Lv2);

            var list = items
                .OrderBy(i => HasCategoryGroup(i) ? 0 : 1)
                .ThenBy(i =>
                {
                    if (!HasCategoryGroup(i)) return 0;
                    string groupName = !string.IsNullOrEmpty(i.Lv1) ? i.Lv1 : i.Lv2;
                    return IsSpecialText(groupName) ? 1 : 0;
                })
                .ThenBy(i => HasCategoryGroup(i) ? i.Lv1lbCode : 0)
                .ThenBy(i => HasCategoryGroup(i) ? i.Lv2lbCode : 0)
                .ThenBy(i => HasCategoryGroup(i) ? (IsSpecialText(i.Lv3) ? 1 : 0) : 0)
                .ThenBy(i => i.Code)
                .Select(i => new BrandItemWithPos
                {
                    Code = i.Code,
                    Text = i.Text,
                    Lv1 = i.Lv1,
                    Lv2 = i.Lv2,
                    Lv3 = i.Lv3
                })
                .ToList();

            // -------------------------------
            // Lv1 그룹 포지션 계산
            // -------------------------------
            var g1 = list.Where(i => !string.IsNullOrEmpty(i.Lv1))
                         .GroupBy(i => i.Lv1);
            int g1count = 0;
            int g1last = g1.Count();
            foreach (var g in g1)
            {
                g1count++;
                var arr = g.ToList();
                string result = BrandItemHelper.BuildCodeRangeString(arr);
                if (g1count == 1)
                {
                    for (int k = 0; k < arr.Count; k++)
                    {
                        arr[k].Lv1GroupPos = GroupPosition.Start;
                        arr[k].Lv123GroupItemsLv1 = result;
                    }
                }
                else if (g1count == g1last)
                {
                    for (int k = 0; k < arr.Count; k++)
                    {
                        arr[k].Lv1GroupPos = GroupPosition.End;
                        arr[k].Lv123GroupItemsLv1 = result;
                    }
                }
                else
                {
                    for (int k = 0; k < arr.Count; k++)
                    {
                        arr[k].Lv1GroupPos = GroupPosition.Middle;
                        arr[k].Lv123GroupItemsLv1 = result;
                    }
                }

                if (arr.Count == 1)
                {
                    arr[0].Lv1Pos = GroupPosition.Start;
                }
                else
                {
                    arr[0].Lv1Pos = GroupPosition.Start;
                    for (int k = 1; k < arr.Count - 1; k++)
                        arr[k].Lv1Pos = GroupPosition.Middle;
                    arr[arr.Count - 1].Lv1Pos = GroupPosition.End;
                }
            }

            // -------------------------------
            // Lv2 그룹 포지션 계산
            // -------------------------------
            var g2g = list.Where(i => !string.IsNullOrEmpty(i.Lv2))
                          .GroupBy(i => i.Lv1);

            foreach (var g in g2g)
            {
                var garr = g.GroupBy(i => i.Lv2).ToList();

                if (garr.Count == 1)
                {
                    var arr = g.ToList();
                    string result = BrandItemHelper.BuildCodeRangeString(arr);
                    for (int k = 0; k < arr.Count; k++)
                    {
                        arr[k].Lv2GroupPos = GroupPosition.Start;
                        arr[k].Lv123GroupItems = result;
                    }
                }
                else
                {
                    int g2count = 0;
                    foreach (var g12 in garr)
                    {
                        g2count++;
                        var arr = g12.ToList();
                        string result = BrandItemHelper.BuildCodeRangeString(arr);
                        if (g2count == 1)
                        {
                            for (int k = 0; k < arr.Count; k++)
                            {
                                arr[k].Lv2GroupPos = GroupPosition.Start;
                                arr[k].Lv123GroupItems = result;
                            }
                        }
                        else if (g2count == garr.Count)
                        {
                            for (int k = 0; k < arr.Count; k++)
                            {
                                arr[k].Lv2GroupPos = GroupPosition.End;
                                arr[k].Lv123GroupItems = result;
                            }
                        }
                        else
                        {
                            for (int k = 0; k < arr.Count; k++)
                            {
                                arr[k].Lv2GroupPos = GroupPosition.Middle;
                                arr[k].Lv123GroupItems = result;
                            }
                        }
                    }
                }
            }

            var g2 = list.Where(i => !string.IsNullOrEmpty(i.Lv2))
                         .GroupBy(i => new { i.Lv1, i.Lv2 });

            foreach (var g in g2)
            {
                var arr = g.ToList();
                if (arr.Count == 1)
                {
                    arr[0].Lv2Pos = GroupPosition.Start;
                }
                else
                {
                    arr[0].Lv2Pos = GroupPosition.Start;
                    for (int k = 1; k < arr.Count - 1; k++)
                        arr[k].Lv2Pos = GroupPosition.Middle;
                    arr[arr.Count - 1].Lv2Pos = GroupPosition.End;
                }
            }

            // -------------------------------
            // Lv3 그룹(Lv2 묶음) 포지션 계산
            // -------------------------------
            var g3 = list.GroupBy(i => new { i.Lv1, i.Lv2 });

            foreach (var g in g3)
            {
                var arr = g.ToList();
                if (arr.Count == 1)
                {
                    if (string.IsNullOrEmpty(arr[0].Lv1) && string.IsNullOrEmpty(arr[0].Lv2))
                    {
                        arr[0].Lv3Pos = GroupPosition.Single;
                    }
                }
                else
                {
                    arr[0].Lv3Pos = GroupPosition.Start;
                    for (int k = 1; k < arr.Count - 1; k++)
                        arr[k].Lv3Pos = GroupPosition.Middle;
                    arr[arr.Count - 1].Lv3Pos = GroupPosition.End;
                }
            }

            return list;
        }

        private string BuildBrandItemScript(List<BrandItemWithPos> itemsWithPos)
        {
            var sb = new StringBuilder();

            foreach (var item in itemsWithPos)
            {
                // lv1 (g3*) – lv1이 있을 때만
                if (!string.IsNullOrEmpty(item.Lv1) && (item.Lv1Pos == GroupPosition.Start))
                {
                    sb.Append(item.Lv123GroupItemsLv1);
                    sb.Append(";");
                    sb.Append("▣");
                    sb.Append(item.Lv1);
                    sb.Append("▣");
                    if (item.Lv1GroupPos == GroupPosition.Start || item.Lv1GroupPos == GroupPosition.End)
                    {
                        var g3Tag = GTagHelper.ToTag(item.Lv1Code, item.Lv1GroupPos);
                        sb.Append(g3Tag);
                    }
                    sb.Append(";\n");
                }

                // lv2 (g2*) – lv2가 있을 때만 (Lv1이 있을 때: ▶...◀)
                if (!string.IsNullOrEmpty(item.Lv1) && !string.IsNullOrEmpty(item.Lv2) && item.Lv2Pos == GroupPosition.Start)
                {
                    sb.Append(item.Lv123GroupItems);
                    sb.Append(";");
                    sb.Append("▶");
                    sb.Append(item.Lv2);
                    sb.Append("◀");
                    if (item.Lv2GroupPos == GroupPosition.Start || item.Lv2GroupPos == GroupPosition.End)
                    {
                        var g2Tag = GTagHelper.ToTag(item.Lv2Code, item.Lv2GroupPos);
                        sb.Append(g2Tag);
                    }
                    if (!string.IsNullOrEmpty(item.Lv1))
                    {
                        sb.Append("<g3p>");
                    }
                    sb.Append(";\n");
                }

                // lv2 (g2*) – lv2가 있을 때만 (Lv1이 없을 때: ▣...▣)
                if (string.IsNullOrEmpty(item.Lv1) && !string.IsNullOrEmpty(item.Lv2) && item.Lv2Pos == GroupPosition.Start)
                {
                    sb.Append(item.Lv123GroupItems);
                    sb.Append(";");
                    sb.Append("▣");
                    sb.Append(item.Lv2);
                    sb.Append("▣");
                    if (item.Lv2GroupPos == GroupPosition.Start || item.Lv2GroupPos == GroupPosition.End)
                    {
                        var g2Tag = GTagHelper.ToTag(item.Lv2Code, item.Lv2GroupPos);
                        sb.Append(g2Tag);
                    }
                    if (!string.IsNullOrEmpty(item.Lv1))
                    {
                        sb.Append("<g3p>");
                    }
                    sb.Append(";\n");
                }

                ///////////// 상세 보기 //////////////
                sb.Append(item.Code).Append(";  ");
                sb.Append(item.Text);

                // 3) g1/g2/g3 태그 조합
                if (string.IsNullOrEmpty(item.Lv2) && string.IsNullOrEmpty(item.Lv1))
                {
                }
                else
                {
                    if (item.Lv3Pos == GroupPosition.Start || item.Lv3Pos == GroupPosition.End)
                    {
                        var g1Tag = GTagHelper.ToTag(item.Lv3Code, item.Lv3Pos);
                        sb.Append(g1Tag);
                    }
                }
                // lv2 (g2*) – lv2가 있을 때만
                if (!string.IsNullOrEmpty(item.Lv2))
                {
                    var g2Tag = GTagHelper.ToTag(item.Lv2Code, item.Lv2Pos);
                    sb.Append("<g2p>");
                }
                // lv1 (g3*) – lv1이 있을 때만
                if (!string.IsNullOrEmpty(item.Lv1))
                {
                    var g3Tag = GTagHelper.ToTag(item.Lv1Code, item.Lv1Pos);
                    sb.Append("<g3p>");
                }
                sb.AppendLine(";");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 보기추출(DP용) - STP 집계 스크립트 텍스트 및 조사명 생성 (ASP.NET 레거시 Export_LB_DP 이관)
        /// </summary>
        public async Task<(string scriptText, string projectName)> GetExportLbDpScriptWithProjectNameAsync(string projectnum, CancellationToken ct = default)
        {
            DataTable dtList = await SelectProQuestionListAsync(projectnum, "", ct);
            if (dtList.Rows.Count == 0) return ("", "");

            string projectName = dtList.Rows[0]["projectname"]?.ToString()?.Trim() ?? "";

            // useyn == "분석" 필터링
            var analyzedRows = dtList.AsEnumerable()
                .Where(r => string.Equals(r["useyn"]?.ToString()?.Trim(), "분석", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (analyzedRows.Count == 0) return ("", projectName);

            // merge_qnum 그룹별 min(mapid) 기준 2단계 정렬 (레거시 동일)
            var minMapIdByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in analyzedRows)
            {
                string mq = row["merge_qnum"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(mq)) mq = row["qnum"]?.ToString()?.Trim() ?? "";
                int mid = Convert.ToInt32(row["mapid"] != DBNull.Value ? row["mapid"] : 999999);
                if (!minMapIdByGroup.TryGetValue(mq, out int curMin) || mid < curMin)
                {
                    minMapIdByGroup[mq] = mid;
                }
            }

            var sortedRows = analyzedRows
                .OrderBy(r => {
                    string mq = r["merge_qnum"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(mq)) mq = r["qnum"]?.ToString()?.Trim() ?? "";
                    return minMapIdByGroup[mq];
                })
                .ThenBy(r => Convert.ToInt32(r["mapid"] != DBNull.Value ? r["mapid"] : 999999))
                .ToList();

            // 변수 맵 조회 (qnumque 매핑)
            DataTable qmselect_tdatamap = await HdataSelectDataMapQnumAllAsync(projectnum, ct);
            var varMap = new List<(string cVar, string openRule)>();
            if (qmselect_tdatamap != null && qmselect_tdatamap.Rows.Count > 0)
            {
                foreach (DataRow vRow in qmselect_tdatamap.Rows)
                {
                    string cVar = (vRow.Table.Columns.Contains("c_question_variable") ? vRow["c_question_variable"] : vRow["cQuestionVariable"])?.ToString()?.TrimEnd() ?? "";
                    string openRule = (vRow.Table.Columns.Contains("open_rule") ? vRow["open_rule"] : vRow["openDefinition"])?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(cVar))
                    {
                        varMap.Add((cVar, openRule));
                    }
                }
            }

            string ResolveQnumQue(string qnum, string servername)
            {
                if (!string.Equals(servername, "qm", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(servername, "rps", StringComparison.OrdinalIgnoreCase))
                {
                    return qnum;
                }

                foreach (var (cVar, openRule) in varMap)
                {
                    if (!string.IsNullOrEmpty(openRule) && openRule.IndexOf(qnum, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return cVar;
                    }
                }
                foreach (var (cVar, _) in varMap)
                {
                    if (string.Equals(cVar, qnum, StringComparison.OrdinalIgnoreCase))
                    {
                        return cVar;
                    }
                }
                return qnum;
            }

            // 보기 라벨(pn_lb) 조회
            DataTable dtLb = await SelectQuestionLabelsAsync(projectnum, "dp", ct);
            var lbDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in dtLb.Rows)
            {
                string qn = r["qnum"]?.ToString()?.Trim() ?? "";
                string json = r["result_json"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(qn) && !string.IsNullOrEmpty(json) && !lbDict.ContainsKey(qn))
                {
                    lbDict[qn] = json;
                }
            }

            var sb = new StringBuilder();
            string checkqnum = "";

            foreach (DataRow rowlist in sortedRows)
            {
                string qnum = rowlist["qnum"]?.ToString()?.TrimEnd() ?? "";
                string merge_qnum = rowlist["merge_qnum"]?.ToString()?.TrimEnd() ?? "";
                string servername = rowlist["servername"]?.ToString()?.TrimEnd() ?? "";
                string question_origin = rowlist["question_origin"]?.ToString()?.TrimEnd() ?? "";
                if (string.IsNullOrEmpty(question_origin))
                {
                    question_origin = rowlist["question_fin"]?.ToString()?.TrimEnd() ?? "";
                }

                string groupKey = !string.IsNullOrEmpty(merge_qnum) ? merge_qnum : qnum;
                string qnumque = ResolveQnumQue(qnum, servername);

                int lvcode = 0;
                string rawLvcode = rowlist.Table.Columns.Contains("lvcode") ? (rowlist["lvcode"]?.ToString()?.Trim() ?? "") : "";
                if (!string.IsNullOrEmpty(rawLvcode) && int.TryParse(rawLvcode, out int parsedLv))
                {
                    lvcode = parsedLv;
                }

                if (checkqnum != groupKey)
                {
                    checkqnum = groupKey;
                    sb.Append($"\n\n[* data {qnumque} (;) = \n");

                    string jsonData = "";
                    if (!string.IsNullOrEmpty(merge_qnum) && lbDict.TryGetValue(merge_qnum, out var jm))
                    {
                        jsonData = jm;
                    }
                    else if (!string.IsNullOrEmpty(qnum) && lbDict.TryGetValue(qnum, out var jq))
                    {
                        jsonData = jq;
                    }

                    if (!string.IsNullOrEmpty(jsonData))
                    {
                        try
                        {
                            JArray jsonArray_lb = JArray.Parse(jsonData);
                            var items = new List<BrandItem>();

                            // DB lvcode가 설정되지 않았으나 JSON에 lv1/lv2가 있는 경우 자동 보정
                            if (lvcode == 0)
                            {
                                bool hasLv1 = jsonArray_lb.Any(c => !string.IsNullOrWhiteSpace(c["lv1"]?.ToString()));
                                bool hasLv2 = jsonArray_lb.Any(c => !string.IsNullOrWhiteSpace(c["lv2"]?.ToString()));
                                if (hasLv1) lvcode = 3;
                                else if (hasLv2) lvcode = 2;
                                else lvcode = 1;
                            }

                            foreach (JObject c in jsonArray_lb)
                            {
                                string lv1 = (lvcode == 3 ? c["lv1"]?.ToString()?.TrimEnd() ?? "" : "");
                                string lv1code = (lvcode == 3 ? c["lv1code"]?.ToString()?.TrimEnd() ?? "" : "");
                                string lv2 = (lvcode >= 2 ? c["lv2"]?.ToString()?.TrimEnd() ?? "" : "");
                                string lv2code = (lvcode >= 2 ? c["lv2code"]?.ToString()?.TrimEnd() ?? "" : "");
                                string lv3 = c["lv3"]?.ToString()?.TrimEnd() ?? "";
                                string lv123code = c["lv123code"]?.ToString()?.TrimEnd() ?? "";

                                if (!int.TryParse(lv123code, out int parsedCode)) continue;

                                int ParseGroupCode(string? codeStr)
                                {
                                    if (string.IsNullOrWhiteSpace(codeStr)) return int.MaxValue;
                                    string s = codeStr.Trim();
                                    if (string.Equals(s, "nan", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
                                    return int.TryParse(s, out var v) ? v : int.MaxValue;
                                }

                                items.Add(new BrandItem
                                {
                                    Code = parsedCode,
                                    Text = lv3,
                                    Lv1 = lv1,
                                    Lv2 = lv2,
                                    Lv3 = lv3,
                                    Lv1lbCode = ParseGroupCode(lv1code),
                                    Lv2lbCode = ParseGroupCode(lv2code)
                                });
                            }

                            var specialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            {
                                "기타", "없음", "없다", "모름", "무응답", "없음/없다", "모름/무응답", "없음/모름/무응답", "모름/없음/무응답"
                            };

                            bool IsSpecialText(string? name)
                            {
                                if (string.IsNullOrWhiteSpace(name)) return false;
                                return specialNames.Contains(name.Trim());
                            }

                            bool HasCategoryGroup(BrandItem item) => !string.IsNullOrEmpty(item.Lv1) || !string.IsNullOrEmpty(item.Lv2);

                            var items2 = items
                                .OrderBy(i => HasCategoryGroup(i) ? 0 : 1)
                                .ThenBy(i =>
                                {
                                    if (!HasCategoryGroup(i)) return 0;
                                    string groupName = !string.IsNullOrEmpty(i.Lv1) ? i.Lv1 : i.Lv2;
                                    return IsSpecialText(groupName) ? 1 : 0;
                                })
                                .ThenBy(i => HasCategoryGroup(i) ? i.Lv1lbCode : 0)
                                .ThenBy(i => HasCategoryGroup(i) ? i.Lv2lbCode : 0)
                                .ThenBy(i => HasCategoryGroup(i) ? (IsSpecialText(i.Lv3) ? 1 : 0) : 0)
                                .ThenBy(i => i.Code)
                                .ToList();

                            var itemsPos = ComputeBrandItemPositions(items2);
                            string script = BuildBrandItemScript(itemsPos);
                            script = script.TrimEnd();

                            if (script.EndsWith(";"))
                            {
                                script = script.Substring(0, script.Length - 1);
                            }
                            script += "]\n";
                            sb.Append(script);
                        }
                        catch
                        {
                            sb.AppendLine("]");
                        }
                    }
                    else
                    {
                        sb.AppendLine("]");
                    }

                    sb.Append($"\n");
                    sb.Append($"[* set z = {qnumque}.#]\n");
                    sb.Append($"    xt = '{question_origin}', \n");
                    sb.Append($"    x = '[* do i=1:z:2][{qnumque}.i+1];[* end i]',\n\n");
                }
                else
                {
                    sb.Append($"\n");
                    sb.Append($"[* set z = {qnumque}.#]\n");
                    sb.Append($"    xt = '{question_origin}', \n");
                    sb.Append($"    x = '[* do i=1:z:2][{qnumque}.i+1];[* end i]',\n\n");
                }
            }

            return (sb.ToString(), projectName);
        }

        /// <summary>
        /// 보기추출(DP용) - STP 집계 스크립트 텍스트 생성
        /// </summary>
        public async Task<string> GetExportLbDpScriptAsync(string projectnum, CancellationToken ct = default)
        {
            var res = await GetExportLbDpScriptWithProjectNameAsync(projectnum, ct);
            return res.scriptText;
        }

        /// <summary>
        /// 응답추출(ALL) - 전체 문항 응답자 매트릭스 DataTable 생성 (ASP.NET 레거시 Export_Data_All 로직 이관)
        /// </summary>
        public async Task<DataTable> GetExportDataAllTableAsync(string projectnum, CancellationToken ct = default)
        {
            var merge_pn_qnum = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
            var merge_pn_pid = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
            var duplicate_pn_qnum_map = new Dictionary<string, Dictionary<string, Dictionary<string, List<(string lv123code, string sentiment, string recheckyn)>>>>(StringComparer.OrdinalIgnoreCase);
            var duplicate_pn_qnum_map_fin = new Dictionary<string, Dictionary<string, Dictionary<string, List<(string lv123code, string sentiment, string recheckyn)>>>>(StringComparer.OrdinalIgnoreCase);
            var qnumQuestionOriginMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. pn_in 데이터 조회
            string sqlIn = "SELECT * FROM public.pn_in WHERE projectnum = @projectnum";
            DataTable select_pn_in_data = await _postgreLib.ExecuteQueryAsync(sqlIn, new List<NpgsqlParameter> { new NpgsqlParameter("@projectnum", projectnum) }, "Postgres", ct);

            if (select_pn_in_data.Rows.Count > 0)
            {
                foreach (DataRow rowlist in select_pn_in_data.Rows)
                {
                    string merge_qnum = rowlist["qnum"]?.ToString()?.TrimEnd() ?? "";
                    string pnin_json_data = rowlist["answer_fin"]?.ToString()?.TrimEnd() ?? "";
                    string pnin_json_data_origin = rowlist["answer_origin"]?.ToString()?.TrimEnd() ?? "";

                    if (string.IsNullOrEmpty(pnin_json_data) || pnin_json_data == "[]")
                        continue;

                    // 2. pn_lb 카테고리 데이터에서 DK(모름) 코드 추출
                    string sqlLb = "SELECT * FROM public.pn_lb WHERE projectnum = @projectnum AND qnum = @qnum";
                    DataTable select_pn_lb_data = await _postgreLib.ExecuteQueryAsync(sqlLb, new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@qnum", merge_qnum)
                    }, "Postgres", ct);

                    string DKcode = "";
                    if (select_pn_lb_data.Rows.Count > 0)
                    {
                        string json_data = select_pn_lb_data.Rows[0]["result_json"]?.ToString()?.TrimEnd() ?? "";
                        if (!string.IsNullOrWhiteSpace(json_data))
                        {
                            try
                            {
                                JArray jsonArray_lb = JArray.Parse(json_data);
                                foreach (JObject c in jsonArray_lb)
                                {
                                    string lv3 = c["lv3"]?.ToString()?.TrimEnd() ?? "";
                                    string lv123code = c["lv123code"]?.ToString()?.TrimEnd() ?? "";
                                    if (lv3.Contains("모름")) DKcode = lv123code;
                                }
                            }
                            catch { }
                        }
                    }

                    // 3. 중복제거일자 설정 확인
                    string duplicateRemoveDate = "";
                    string sqlOpt = "SELECT duplicate_remove_date FROM public.project_list WHERE merge_pn = @projectnum AND merge_qnum = @qnum AND useyn = '분석' ORDER BY id DESC LIMIT 1";
                    try
                    {
                        var optObj = await _postgreLib.QueryFirstOrDefaultAsync<string>(sqlOpt, new { projectnum, qnum = merge_qnum }, "Postgres", ct);
                        duplicateRemoveDate = optObj?.Trim() ?? "";
                    }
                    catch { }

                    // 4. 원본 answer_origin 기준 fixed_key 매핑 (다중 키 인덱싱)
                    var keyToudpateanswer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(pnin_json_data_origin))
                    {
                        try
                        {
                            JObject pnin_obj_origin = JObject.Parse(pnin_json_data_origin);
                            if (pnin_obj_origin[merge_qnum] is JArray items)
                            {
                                foreach (JObject item in items)
                                {
                                    string fixed_key = item["fixed_key"]?.ToString() ?? "";
                                    if (string.IsNullOrWhiteSpace(fixed_key)) continue;

                                    string ansOrig = item["answer_origin"]?.ToString().Trim() ?? "";
                                    string ansFin = item["answer_fin"]?.ToString().Trim() ?? "";
                                    string ans = item["answer"]?.ToString().Trim() ?? "";

                                    if (!string.IsNullOrWhiteSpace(ansOrig) && !keyToudpateanswer.ContainsKey(ansOrig)) keyToudpateanswer[ansOrig] = fixed_key;
                                    if (!string.IsNullOrWhiteSpace(ansFin) && !keyToudpateanswer.ContainsKey(ansFin)) keyToudpateanswer[ansFin] = fixed_key;
                                    if (!string.IsNullOrWhiteSpace(ans) && !keyToudpateanswer.ContainsKey(ans)) keyToudpateanswer[ans] = fixed_key;
                                }
                            }
                        }
                        catch { }
                    }

                    // 5. answer_fin 딕셔너리 매핑
                    var keylv123code = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    var keysentiment = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    var keyTorecheckyn = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    var keyToudpatedate = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        JArray jsonArray = JArray.Parse(pnin_json_data);
                        foreach (JObject item in jsonArray)
                        {
                            string fixed_key = item["fixed_key"]?.ToString() ?? "";
                            string update_date = item["update_date"]?.ToString() ?? "";
                            string cid = item["cid"]?.ToString() ?? "";
                            string lv123code = item["lv123code"]?.ToString() ?? "";
                            string sentiment = item["sentiment"]?.ToString() ?? "";
                            string recheckyn = item["recheckyn"]?.ToString() ?? "";

                            if (string.IsNullOrWhiteSpace(fixed_key)) continue;
                            if (string.IsNullOrWhiteSpace(cid)) cid = "1";

                            GetOrCreateDict(keylv123code, fixed_key)[cid] = lv123code;
                            GetOrCreateDict(keyTorecheckyn, fixed_key)[cid] = recheckyn;
                            GetOrCreateDict(keyToudpatedate, fixed_key)[cid] = update_date;
                            GetOrCreateDict(keysentiment, fixed_key)[cid] = sentiment;
                        }
                    }
                    catch { }

                    // 6. project_list 문항 정보 조회
                    string sqlPl = "SELECT * FROM public.project_list WHERE merge_pn = @projectnum AND merge_qnum = @qnum ORDER BY mapid ASC";
                    DataTable select_project_list = await _postgreLib.ExecuteQueryAsync(sqlPl, new List<NpgsqlParameter>
                    {
                        new NpgsqlParameter("@projectnum", projectnum),
                        new NpgsqlParameter("@qnum", merge_qnum)
                    }, "Postgres", ct);

                    if (select_project_list.Rows.Count == 0) continue;

                    foreach (DataRow rowlist2 in select_project_list.Rows)
                    {
                        string json_data_origin = rowlist2["answer_origin"]?.ToString()?.TrimEnd() ?? "";
                        string qnum = rowlist2["qnum"]?.ToString()?.TrimEnd() ?? "";
                        string pn = rowlist2["projectnum"]?.ToString()?.TrimEnd() ?? "";
                        string questionOrigin = rowlist2["question_origin"]?.ToString()?.TrimEnd() ?? "";

                        if (!string.IsNullOrEmpty(qnum) && !qnumQuestionOriginMap.ContainsKey(qnum))
                        {
                            qnumQuestionOriginMap[qnum] = questionOrigin;
                        }

                        List<string> qnumList = GetOrCreateDict(GetOrCreateDict(merge_pn_qnum, pn), merge_qnum);
                        if (!qnumList.Contains(qnum)) qnumList.Add(qnum);

                        if (string.IsNullOrEmpty(json_data_origin)) continue;

                        try
                        {
                            JObject obj_origin = JObject.Parse(json_data_origin);
                            if (obj_origin[qnum] is JArray answerItems)
                            {
                                foreach (JObject item in answerItems)
                                {
                                    string ans_orig = item["answer_origin"]?.ToString().Trim() ?? "";
                                    string ans_fin = item["answer_fin"]?.ToString().Trim() ?? "";
                                    string ans = item["answer"]?.ToString().Trim() ?? "";
                                    string pid = item["pid"]?.ToString() ?? "";
                                    if (string.IsNullOrWhiteSpace(pid)) continue;

                                    List<string> pidList = GetOrCreateDict(GetOrCreateDict(merge_pn_pid, pn), merge_qnum);
                                    if (!pidList.Contains(pid)) pidList.Add(pid);

                                    // 다중 Fallback 매칭 (answer_origin -> answer_fin -> answer)
                                    string? fixed_key = null;
                                    if (!string.IsNullOrWhiteSpace(ans_orig) && keyToudpateanswer.TryGetValue(ans_orig, out var fk1))
                                        fixed_key = fk1;
                                    else if (!string.IsNullOrWhiteSpace(ans_fin) && keyToudpateanswer.TryGetValue(ans_fin, out var fk2))
                                        fixed_key = fk2;
                                    else if (!string.IsNullOrWhiteSpace(ans) && keyToudpateanswer.TryGetValue(ans, out var fk3))
                                        fixed_key = fk3;

                                    if (string.IsNullOrWhiteSpace(fixed_key)) continue;

                                    var lvCodeDict = GetOrCreateDict(keylv123code, fixed_key);
                                    var sentimentDict = GetOrCreateDict(keysentiment, fixed_key);
                                    var recheckDict = GetOrCreateDict(keyTorecheckyn, fixed_key);
                                    var updateDateDict = GetOrCreateDict(keyToudpatedate, fixed_key);

                                    if (!lvCodeDict.ContainsKey("1")) lvCodeDict["1"] = "";
                                    if (!sentimentDict.ContainsKey("1")) sentimentDict["1"] = "";
                                    if (!recheckDict.ContainsKey("1")) recheckDict["1"] = "";
                                    if (!updateDateDict.ContainsKey("1")) updateDateDict["1"] = "";

                                    string recheckyn = recheckDict["1"];
                                    int rowcnt = lvCodeDict.Count;

                                    for (int cid = 1; cid <= rowcnt; cid++)
                                    {
                                        string sid = cid.ToString();
                                        if (!lvCodeDict.ContainsKey(sid)) continue;

                                        string lv123code = lvCodeDict[sid];
                                        string sentiment = sentimentDict.ContainsKey(sid) ? sentimentDict[sid] : "";

                                        List<(string lv123code, string sentiment, string recheckyn)> originList =
                                            GetOrCreateDict(GetOrCreateDict(GetOrCreateDict(duplicate_pn_qnum_map, pn), pid), qnum);
                                        List<(string lv123code, string sentiment, string recheckyn)> finList =
                                            GetOrCreateDict(GetOrCreateDict(GetOrCreateDict(duplicate_pn_qnum_map_fin, pn), pid), qnum);

                                        if (duplicateRemoveDate == "")
                                        {
                                            originList.Add((lv123code, sentiment, recheckyn));
                                            finList.Add((lv123code, sentiment, recheckyn));
                                        }
                                        else
                                        {
                                            if (!originList.Any(x => x.lv123code == lv123code)) originList.Add((lv123code, sentiment, recheckyn));
                                            bool isCurrentDK = lv123code == DKcode;
                                            bool hasOtherNonDK = originList.Any(x => x.lv123code != DKcode);

                                            if (!(isCurrentDK && hasOtherNonDK))
                                            {
                                                if (!finList.Any(x => x.lv123code == lv123code)) finList.Add((lv123code, sentiment, recheckyn));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // 7. 각 변수(qnum)별 최대 cid 응답개수(max_count) 파악
            var orderedQnums = new List<string>();
            foreach (var pnItem in merge_pn_qnum)
            {
                foreach (var mergeItem in pnItem.Value)
                {
                    foreach (string qnum in mergeItem.Value)
                    {
                        if (!orderedQnums.Contains(qnum)) orderedQnums.Add(qnum);
                    }
                }
            }

            var qnumMaxCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string qnum in orderedQnums)
            {
                int maxCount = 0;
                foreach (var pnItem in duplicate_pn_qnum_map_fin)
                {
                    foreach (var pidItem in pnItem.Value)
                    {
                        if (pidItem.Value.TryGetValue(qnum, out var ansList))
                        {
                            int validCount = ansList.Count(x => !string.IsNullOrWhiteSpace(x.lv123code));
                            if (validCount > maxCount) maxCount = validCount;
                        }
                    }
                }
                qnumMaxCounts[qnum] = Math.Max(1, maxCount);
            }

            // 8. DataTable 구축: 표제목 메타데이터(DisplayHeader, QuestionOrigin) 설정
            DataTable dtExport = new DataTable();
            var pidCol = dtExport.Columns.Add("pid");
            pidCol.ExtendedProperties["DisplayHeader"] = "pid";
            pidCol.ExtendedProperties["QuestionOrigin"] = "";

            foreach (string qnum in orderedQnums)
            {
                int maxCount = qnumMaxCounts[qnum];
                for (int k = 0; k < maxCount; k++)
                {
                    string colName = (k == 0) ? qnum : $"{qnum}__cid_{k + 1}";
                    if (!dtExport.Columns.Contains(colName))
                    {
                        var col = dtExport.Columns.Add(colName);
                        // 표제목: 첫 번째 열에는 변수명(qnum), 이후 확장 열에는 빈칸("") 설정
                        col.ExtendedProperties["DisplayHeader"] = (k == 0) ? qnum : "";
                        // 문항내용: 첫 번째 열에는 question_origin, 이후 확장 열에는 빈칸("") 설정
                        col.ExtendedProperties["QuestionOrigin"] = (k == 0)
                            ? (qnumQuestionOriginMap.TryGetValue(qnum, out var qo) ? qo : "")
                            : "";
                    }
                }
            }

            // 9. 전체 고유 PID 목록 수집 및 정렬
            var allPids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 1) 전체 응답자 PID 추가
            foreach (var pnItem in merge_pn_pid)
            {
                foreach (var mergeItem in pnItem.Value)
                {
                    foreach (string pid in mergeItem.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(pid)) allPids.Add(pid);
                    }
                }
            }
            // 2) 매칭 완료된 PID 추가 (합집합)
            foreach (var pnItem in duplicate_pn_qnum_map_fin)
            {
                foreach (string pid in pnItem.Value.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(pid)) allPids.Add(pid);
                }
            }

            // PID 정렬 (숫자 가능 시 숫자 순, 아니면 문자열 순)
            var sortedPids = allPids.OrderBy(p => long.TryParse(p, out long n) ? n : long.MaxValue)
                                    .ThenBy(p => p)
                                    .ToList();

            // 10. PID별 데이터 행 기입: 셀 한 개당 하나의 코드값 배치
            foreach (string pid in sortedPids)
            {
                DataRow row = dtExport.NewRow();
                row["pid"] = pid;

                foreach (string qnum in orderedQnums)
                {
                    int maxCount = qnumMaxCounts[qnum];

                    // 해당 PID의 qnum 코드 목록 수집
                    List<string> lv123codeList = new List<string>();
                    foreach (var pnItem in duplicate_pn_qnum_map_fin)
                    {
                        if (pnItem.Value.TryGetValue(pid, out var qnumMap) && qnumMap.TryGetValue(qnum, out var ansList))
                        {
                            var codes = ansList
                                .Select(x => x.lv123code)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .ToList();
                            if (codes.Count > lv123codeList.Count)
                            {
                                lv123codeList = codes;
                            }
                        }
                    }

                    // cid 키값 순서대로 셀 한 개당 코드값 1개씩 기입 (남는 칸은 빈칸)
                    for (int k = 0; k < maxCount; k++)
                    {
                        string colName = (k == 0) ? qnum : $"{qnum}__cid_{k + 1}";
                        row[colName] = (k < lv123codeList.Count) ? lv123codeList[k] : "";
                    }
                }

                dtExport.Rows.Add(row);
            }

            return dtExport;
        }

        private static TValue GetOrCreateDict<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key) where TValue : new()
        {
            if (!dict.TryGetValue(key, out TValue? value))
            {
                value = new TValue();
                dict[key] = value;
            }
            return value;
        }

        /// <summary>
        /// 응답추출(ALL) - 전체 문항 응답자 데이터셋 생성 (JArray 반환)
        /// </summary>
        public async Task<JArray> GetExportDataAllJsonAsync(string projectnum, CancellationToken ct = default)
        {
            DataTable dtExport = await GetExportDataAllTableAsync(projectnum, ct);
            var resultArr = new JArray();
            foreach (DataRow row in dtExport.Rows)
            {
                var rowObj = new JObject();
                foreach (DataColumn col in dtExport.Columns)
                {
                    rowObj[col.ColumnName] = row[col]?.ToString() ?? "";
                }
                resultArr.Add(rowObj);
            }
            return resultArr;
        }

        /// <summary>
        /// AI오픈분석 문항등록 DB탭 오픈변수 및 기존 등록상태/PID 현황 조회 (StateCode="4" 전체완료 기준 정밀 비교)
        /// </summary>
        public async Task<JArray> GetProRegisterDbSelectInfoAsync(string projectnum, string server, string user, CancellationToken ct = default)
        {
            var jsonArray = new JArray();
            if (string.IsNullOrWhiteSpace(projectnum)) return jsonArray;

            string servername = string.IsNullOrEmpty(server) ? "rps" : server.Trim().ToLower();

            // 1-1. HData surveyon_projects에서 data_source 확인 (SPSS 여부 판별)
            DataTable dtProject = await select_surveyon_projects_async(projectnum, 2, ct);
            string dataSource = dtProject.Rows.Count > 0 && dtProject.Columns.Contains("data_source")
                ? (dtProject.Rows[0]["data_source"]?.ToString()?.Trim() ?? "")
                : "";
            bool isSpss = string.Equals(dataSource, "SPSS", StringComparison.OrdinalIgnoreCase);

            // 1-2. Target DB 오픈변수 맵 정보 조회 (hdata전용)
            DataTable qmselect_tdatamap = await HdataSelectDataMapQnumAllAsync(projectnum, ct);
            var opendic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var validOpenQnumSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (qmselect_tdatamap.Rows.Count > 0)
            {
                foreach (DataRow row in qmselect_tdatamap.Rows)
                {
                    string qnum = (row.Table.Columns.Contains("c_question_variable") ? row["c_question_variable"] : row["cQuestionVariable"])?.ToString()?.TrimEnd() ?? "";
                    string question = (row.Table.Columns.Contains("label")
                        ? (!string.IsNullOrEmpty(row["re_label"]?.ToString()) ? row["re_label"] : row["label"])
                        : row["question"])?.ToString()?.Trim() ?? "";
                    string exopen = (row.Table.Columns.Contains("open_rule") ? row["open_rule"] : row["openDefinition"])?.ToString()?.Trim() ?? "";
                    string type = row["type"]?.ToString()?.TrimEnd() ?? "";

                    if (isSpss)
                    {
                        // SPSS: type == 'open(문자)' (단, pid 제외)
                        if (string.Equals(type, "open(문자)", StringComparison.OrdinalIgnoreCase) && !string.Equals(qnum, "pid", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(qnum))
                            {
                                opendic[qnum] = question;
                                validOpenQnumSet.Add(qnum);
                            }
                        }
                    }
                    else
                    {
                        // 일반 (QMaster / RPS): op, model, open(문자), open_rule
                        if (qnum.Contains("op", StringComparison.OrdinalIgnoreCase) || qnum.Contains("model", StringComparison.OrdinalIgnoreCase) || (string.Equals(type, "open(문자)", StringComparison.OrdinalIgnoreCase) && !string.Equals(qnum, "pid", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!string.IsNullOrEmpty(qnum))
                            {
                                opendic[qnum] = question;
                                validOpenQnumSet.Add(qnum);
                            }
                        }

                        if (!string.IsNullOrEmpty(exopen))
                        {
                            string[] exopenlist = exopen.Split(',');
                            var opList = exopenlist.Where(item => item.Contains("op", StringComparison.OrdinalIgnoreCase)).ToList();
                            foreach (var item in opList)
                            {
                                string trimmedItem = item.Trim();
                                if (!string.IsNullOrEmpty(trimmedItem))
                                {
                                    opendic[trimmedItem] = question;
                                    validOpenQnumSet.Add(trimmedItem);
                                }
                            }
                        }
                    }
                }
            }

            // 1-3. 기존 등록된 문항 목록 및 문항별 PID HashSet 추출
            DataTable dtRegisteredList = await select_project_list_async(user, 100, "", projectnum, "", "", ct);
            var registeredDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var registeredPidSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var registeredQuestionDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // server 자동 판별: project_enter 또는 dtRegisteredList 등록 서버 확인
            if (servername == "rps")
            {
                var dtEnter = await select_project_enter_async(projectnum, 2, "", ct);
                if (dtEnter.Rows.Count > 0 && dtEnter.Columns.Contains("servername"))
                {
                    string s = dtEnter.Rows[0]["servername"]?.ToString()?.Trim().ToLower() ?? "";
                    if (!string.IsNullOrEmpty(s)) servername = s;
                }
                else if (dtRegisteredList.Rows.Count > 0 && dtRegisteredList.Columns.Contains("servername"))
                {
                    string s = dtRegisteredList.Rows[0]["servername"]?.ToString()?.Trim().ToLower() ?? "";
                    if (!string.IsNullOrEmpty(s)) servername = s;
                }
            }

            foreach (DataRow row in dtRegisteredList.Rows)
            {
                string qnum = row.Table.Columns.Contains("qnum") ? (row["qnum"]?.ToString()?.TrimEnd() ?? "") : "";
                string qnumText = row.Table.Columns.Contains("qnum_text") ? (row["qnum_text"]?.ToString()?.TrimEnd() ?? "") : "";
                string questionFin = row.Table.Columns.Contains("question_fin") ? (row["question_fin"]?.ToString()?.Trim() ?? "") : "";
                string questionOrigin = row.Table.Columns.Contains("question_origin") ? (row["question_origin"]?.ToString()?.Trim() ?? "") : "";
                string answerOrigin = row.Table.Columns.Contains("answer_origin") ? (row["answer_origin"]?.ToString() ?? "") : "";

                string regQText = !string.IsNullOrEmpty(questionFin) ? questionFin : questionOrigin;
                if (!string.IsNullOrEmpty(qnum) && !string.IsNullOrEmpty(regQText))
                {
                    registeredQuestionDict[qnum] = regQText;
                }
                if (!string.IsNullOrEmpty(qnumText) && !string.IsNullOrEmpty(regQText) && !registeredQuestionDict.ContainsKey(qnumText))
                {
                    registeredQuestionDict[qnumText] = regQText;
                }

                var pidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int pidCount = 0;

                if (!string.IsNullOrWhiteSpace(answerOrigin))
                {
                    try
                    {
                        JToken token = JToken.Parse(answerOrigin);
                        if (token is JArray arr)
                        {
                            foreach (JObject item in arr.OfType<JObject>())
                            {
                                string pid = item["pid"]?.ToString()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(pid)) pidSet.Add(pid);
                            }
                            pidCount = pidSet.Count > 0 ? pidSet.Count : arr.Count;
                        }
                        else if (token is JObject obj)
                        {
                            foreach (var prop in obj.Properties())
                            {
                                string subQnum = prop.Name.Trim();
                                if (prop.Value is JArray subArr)
                                {
                                    var subPidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    foreach (JObject item in subArr.OfType<JObject>())
                                    {
                                        string pid = item["pid"]?.ToString()?.Trim() ?? "";
                                        if (!string.IsNullOrEmpty(pid))
                                        {
                                            pidSet.Add(pid);
                                            subPidSet.Add(pid);
                                        }
                                    }
                                    int subCount = subPidSet.Count > 0 ? subPidSet.Count : subArr.Count;
                                    if (!string.IsNullOrEmpty(subQnum))
                                    {
                                        registeredDict[subQnum] = subCount;
                                        registeredPidSets[subQnum] = subPidSet;
                                    }
                                }
                            }
                            if (pidSet.Count > 0) pidCount = pidSet.Count;
                        }
                    }
                    catch { }
                }

                // [방안 A] 실제 데이터 변수명인 qnum만 등록 (qnum_text는 라벨이므로 등록에서 제외)
                if (!string.IsNullOrEmpty(qnum))
                {
                    if (!registeredDict.ContainsKey(qnum) || registeredDict[qnum] == 0) registeredDict[qnum] = pidCount;
                    if (!registeredPidSets.ContainsKey(qnum)) registeredPidSets[qnum] = pidSet;
                }
                else if (!string.IsNullOrEmpty(qnumText) && validOpenQnumSet.Contains(qnumText))
                {
                    // qnum이 비어있고 qnumText에 변수명이 들어간 특수 케이스의 경우, 유효 오픈 변수일 때만 허용
                    if (!registeredDict.ContainsKey(qnumText) || registeredDict[qnumText] == 0) registeredDict[qnumText] = pidCount;
                    if (!registeredPidSets.ContainsKey(qnumText)) registeredPidSets[qnumText] = pidSet;
                }
            }

            // 1-4. Target DB 전체완료 (answerStateCode="4") 응답 데이터 고속 조회 (SPSS vs QMASTER 분기)
            var answerData = new List<(string pid, string jsonData)>();
            if (isSpss)
            {
                answerData = await GetSurveyonAnswerOpenDataFastAsync(projectnum, validOpenQnumSet, ct);
            }
            else if (servername == "rps" || servername == "qm")
            {
                answerData = await GetTAnswerOpenDataFastAsync(projectnum, servername, ct);
            }

            var targetDbPidSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var textDbPidSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var numericDbPidSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (answerData.Count > 0)
            {
                var threadLocals = new System.Collections.Concurrent.ConcurrentBag<(
                    Dictionary<string, HashSet<string>> targetLocal,
                    Dictionary<string, HashSet<string>> textLocal,
                    Dictionary<string, HashSet<string>> numericLocal
                )>();

                Parallel.ForEach(
                    answerData,
                    () => (
                        targetLocal: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
                        textLocal: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
                        numericLocal: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                    ),
                    (item, loopState, local) =>
                    {
                        string pid = item.pid;
                        string jsonData = item.jsonData;
                        if (string.IsNullOrEmpty(jsonData)) return local;

                        // RpsOpenProcessor.ParseFlatOpen 인라인 JSON 고속 파싱 (Zero-Allocation)
                        var (resultDict, resultint, resultall) = RpsOpenProcessor.ParseFlatOpen(jsonData);

                        // 1) 전체 응답자 집합 (resultall)
                        if (resultall != null)
                        {
                            foreach (var kv in resultall)
                            {
                                string qkey = kv.Key;
                                if (!local.targetLocal.TryGetValue(qkey, out var set))
                                {
                                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    local.targetLocal[qkey] = set;
                                }
                                if (!string.IsNullOrEmpty(pid)) set.Add(pid);
                            }
                        }

                        // 2) 텍스트 응답자 집합 (resultDict) - 단순 무응답/거절 단어("없음", "모름" 등) 제외
                        if (resultDict != null)
                        {
                            foreach (var kv in resultDict)
                            {
                                string qkey = kv.Key;
                                string valStr = kv.Value?.ToString()?.Trim() ?? "";
                                if (RpsOpenProcessor.IsRefusalOrNonSubstantive(valStr))
                                {
                                    continue;
                                }

                                if (!local.textLocal.TryGetValue(qkey, out var set))
                                {
                                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    local.textLocal[qkey] = set;
                                }
                                if (!string.IsNullOrEmpty(pid)) set.Add(pid);
                            }
                        }

                        // 3) 숫자형 응답자 집합 (resultint) - 콤마, 점 포함
                        if (resultint != null)
                        {
                            foreach (var kv in resultint)
                            {
                                string qkey = kv.Key;
                                if (!local.numericLocal.TryGetValue(qkey, out var set))
                                {
                                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    local.numericLocal[qkey] = set;
                                }
                                if (!string.IsNullOrEmpty(pid)) set.Add(pid);
                            }
                        }

                        return local;
                    },
                    local =>
                    {
                        threadLocals.Add(local);
                    }
                );

                // 병렬 스레드 로컬 집계 데이터 메인 Dictionary로 병합
                foreach (var local in threadLocals)
                {
                    foreach (var kv in local.targetLocal)
                    {
                        if (!targetDbPidSets.TryGetValue(kv.Key, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            targetDbPidSets[kv.Key] = set;
                        }
                        set.UnionWith(kv.Value);
                    }

                    foreach (var kv in local.textLocal)
                    {
                        if (!textDbPidSets.TryGetValue(kv.Key, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            textDbPidSets[kv.Key] = set;
                        }
                        set.UnionWith(kv.Value);
                    }

                    foreach (var kv in local.numericLocal)
                    {
                        if (!numericDbPidSets.TryGetValue(kv.Key, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            numericDbPidSets[kv.Key] = set;
                        }
                        set.UnionWith(kv.Value);
                    }
                }
            }

            // 4. project_list에 이미 등록되어 있으나 opendic에 누락된 변수 보충 (유효 오픈 변수 기준 충족 시에만)
            foreach (var regKey in registeredDict.Keys)
            {
                if (!opendic.ContainsKey(regKey))
                {
                    string fallbackQuestion = registeredQuestionDict.TryGetValue(regKey, out var fq) && !string.IsNullOrWhiteSpace(fq)
                        ? fq
                        : regKey;

                    if (isSpss)
                    {
                        if (validOpenQnumSet.Contains(regKey))
                        {
                            opendic[regKey] = fallbackQuestion;
                        }
                    }
                    else
                    {
                        if (regKey.Contains("op", StringComparison.OrdinalIgnoreCase) || regKey.Contains("model", StringComparison.OrdinalIgnoreCase) || validOpenQnumSet.Contains(regKey))
                        {
                            opendic[regKey] = fallbackQuestion;
                        }
                    }
                }
            }

            // 순수 숫자 오픈 문항 판정 로직 (콤마, 점 포함 순수 숫자는 리스트에서 제외)
            bool IsPureNumericOpenQuestion(string qnumKey)
            {
                numericDbPidSets.TryGetValue(qnumKey, out var numSet);
                textDbPidSets.TryGetValue(qnumKey, out var txtSet);

                int numCount = numSet?.Count ?? 0;
                int txtCount = txtSet?.Count ?? 0;

                if (numCount > 0 || txtCount > 0)
                {
                    // 1) 실질 텍스트 응답이 0건이고 숫자 응답이 1건 이상인 경우 -> 100% 순수 숫자 오픈 문항
                    if (txtCount == 0 && numCount > 0) return true;

                    // 2) 숫자 응답 비율이 95% 이상으로 대다수이며 실질 텍스트 응답이 극소수인 경우 -> 순수 숫자 문항으로 판단
                    if (numCount > 0 && ((double)numCount / (numCount + txtCount) >= 0.95)) return true;
                }

                return false;
            }

            if (opendic.Count > 0)
            {
                int cnt = 0;
                foreach (var open in opendic)
                {
                    string qnum = open.Key;
                    string question = open.Value;

                    // 콤마, 점을 포함한 순수 숫자 오픈 문항으로 판단이 되면 리스트에서 제외
                    if (IsPureNumericOpenQuestion(qnum))
                    {
                        continue;
                    }

                    cnt++;
                    registeredPidSets.TryGetValue(qnum, out var regSet);
                    targetDbPidSets.TryGetValue(qnum, out var targetSet);

                    regSet ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    targetSet ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    bool isRegistered = registeredDict.ContainsKey(qnum) || regSet.Count > 0;
                    int registeredPidCount = regSet.Count > 0 ? regSet.Count : (isRegistered ? (registeredDict.TryGetValue(qnum, out int rc) ? rc : 0) : 0);
                    
                    // 실질 텍스트 응답자 수 확인 (순수 0건 응답 문항 제외)
                    int txtCount = textDbPidSets.TryGetValue(qnum, out var tSet) ? tSet.Count : 0;
                    if (txtCount == 0 && targetSet.Count == 0 && registeredPidCount == 0)
                    {
                        continue;
                    }
                    
                    // 신규 대기 PID 목록 및 카운팅
                    int newPidCount = 0;
                    var newPids = new List<string>();
                    foreach (var p in targetSet)
                    {
                        if (!regSet.Contains(p))
                        {
                            newPidCount++;
                            newPids.Add(p);
                        }
                    }
                    int totalPidCount = regSet.Count + newPidCount;
                    if (totalPidCount == 0 && targetSet.Count > 0) totalPidCount = targetSet.Count;

                    var newObj = new JObject
                    {
                        ["cnt"] = cnt,
                        ["qnum"] = qnum,
                        ["question"] = question,
                        ["is_registered"] = isRegistered,
                        ["registered_pid_count"] = registeredPidCount,
                        ["total_pid_count"] = totalPidCount,
                        ["new_pid_count"] = newPidCount,
                        ["new_pids"] = JArray.FromObject(newPids)
                    };

                    jsonArray.Add(newObj);
                }
            }

            return jsonArray;
        }

        public async Task<string?> GetOptionJsonAllAsync(string projectnum, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum)) return null;
            const string sql = @"SELECT option_json_all::text 
                                 FROM public.project_list 
                                 WHERE (merge_pn = @pn OR projectnum = @pn) 
                                   AND option_json_all IS NOT NULL 
                                   AND option_json_all::text != '' 
                                   AND option_json_all::text != '[]' 
                                 ORDER BY id/1 DESC 
                                 LIMIT 1";
            var result = await _postgreLib.QueryFirstOrDefaultAsync<string>(sql, new { pn = projectnum.Trim() }, "Postgres", ct);
            return result;
        }

        public async Task<bool> SaveOptionJsonAllAsync(string projectnum, string optionJsonAll, string user, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum)) return false;
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            const string sql = @"UPDATE public.project_list
                                 SET option_json_all = @optionJsonAll::jsonb,
                                     update_date = @now,
                                     use_id = @user
                                 WHERE merge_pn = @pn OR projectnum = @pn";
            var affected = await _postgreLib.ExecuteAsync(sql, new
            {
                pn = projectnum.Trim(),
                optionJsonAll,
                now,
                user = user ?? ""
            }, "Postgres", ct);

            return affected > 0;
        }
    }
}



