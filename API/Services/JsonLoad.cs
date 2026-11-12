using System.Data;
using Newtonsoft.Json.Linq;
using SurveyonApi.Repositories;
using SurveyonApi.Services;
using SurveyonApi.Models;
using Npgsql;

namespace SurveyonApi.Services
{
    public class JsonHong
    {
        private readonly IMainRepository _mainRepository;
        private readonly IPostgreLib _postgreLib;

        public JsonHong(IMainRepository mainRepository, IPostgreLib postgreLib)
        {
            _mainRepository = mainRepository;
            _postgreLib = postgreLib;
        }

        public async Task<object> SelectProListInfoAsync(string user, string sqltext, CancellationToken ct = default)
        {
            DataTable select_pro_enter_page_data = await _mainRepository.select_project_enter_async(sqltext, 1, ct: ct);
            
            JArray listinfo = new JArray();
            if (select_pro_enter_page_data.Rows.Count > 0)
            {
                // Collect project numbers and register_userids for bulk lookups
                var projectNums = select_pro_enter_page_data.AsEnumerable()
                    .Select(r => r["projectnum"]?.ToString()?.Trim() ?? "")
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .ToList();

                var registerUserIds = select_pro_enter_page_data.AsEnumerable()
                    .Select(r => r["register_userid"]?.ToString()?.Trim() ?? "")
                    .Where(u => !string.IsNullOrEmpty(u))
                    .Distinct()
                    .ToList();

                // Run bulk queries concurrently
                var userNamesTask = _mainRepository.GetBulkUserNamesAsync(registerUserIds, ct);
                var answerCountsTask = _mainRepository.GetBulkAnswerCountsAsync(projectNums, "4", ct);
                var userGroupsTask = _mainRepository.GetBulkUserGroupsAsync(user, projectNums, ct);
                var statusListTask = _mainRepository.GetBulkProjectStatusListAsync(projectNums, ct);

                await Task.WhenAll(userNamesTask, answerCountsTask, userGroupsTask, statusListTask);

                var userNamesMap = await userNamesTask;
                var answerCountsMap = await answerCountsTask;
                var userGroupsMap = await userGroupsTask;
                var statusListMap = await statusListTask;

                foreach (DataRow row in select_pro_enter_page_data.Rows)
                {
                    string id = row["id"].ToString()?.Trim() ?? "";
                    string projectpof = row["projectpof"].ToString()?.Trim() ?? "";
                    string projectnum = row["projectnum"].ToString()?.Trim() ?? "";
                    string projectname = row["projectname"].ToString()?.Trim() ?? "";
                    string register_userid = row["register_userid"].ToString()?.Trim() ?? "";
                    string register_date = row["register_date"].ToString()?.Trim() ?? "";
                    string servername = row["servername"].ToString()?.Trim() ?? "";
                    string merge_pn = row["merge_pn"].ToString()?.Trim() ?? "";

                    // 사용자 이름
                    userNamesMap.TryGetValue(register_userid, out string? register_username);
                    if (string.IsNullOrEmpty(register_username))
                    {
                        register_username = register_userid;
                    }

                    // 응답 수
                    answerCountsMap.TryGetValue(projectnum, out string? postcount);
                    postcount ??= "";

                    // 그룹 정보 및 상단 고정
                    string usergroup = "";
                    string groupposition = "";
                    string? topyn = null;
                    if (userGroupsMap.TryGetValue(projectnum, out var ugInfo))
                    {
                        usergroup = ugInfo.usergroup;
                        groupposition = ugInfo.position;
                        topyn = string.Equals(ugInfo.topyn, "y", StringComparison.OrdinalIgnoreCase) ? "y" : null;
                    }

                    // 프로젝트 상세 상태 및 정보 (Bulk 상태 Map)
                    string project_register_date = "";
                    string project_use_name = "";
                    string project_update_date = "";
                    string project_status = "";
                    string merge_pn_check = "";
                    string merge_pn_text = "";

                    if (statusListMap.TryGetValue(projectnum, out var statusInfo))
                    {
                        project_register_date = statusInfo.ProjectRegisterDate;
                        project_update_date = statusInfo.ProjectUpdateDate;
                        project_use_name = statusInfo.UseName;
                        project_status = statusInfo.ProjectStatus;
                        merge_pn_check = statusInfo.MergePnCheck;
                        merge_pn_text = statusInfo.MergePnText;
                    }

                    var projectInfo = new JObject
                    {
                        ["id"] = id,
                        ["projectpof"] = projectpof,
                        ["projectnum"] = projectnum,
                        ["projectname"] = projectname,
                        ["register_userid"] = register_username,
                        ["register_date"] = register_date,
                        ["servername"] = servername,
                        ["project_use_name"] = project_use_name,
                        ["project_update_date"] = project_update_date,
                        ["project_status"] = project_status,
                        ["groupposition"] = groupposition,
                        ["usergroup"] = usergroup,
                        ["topyn"] = topyn,
                        ["postgrecompletecount"] = postcount,
                        ["merge_pn"] = merge_pn,
                        ["merge_pn_check"] = merge_pn_check,
                        ["merge_pn_text"] = merge_pn_text,
                    };
                    listinfo.Add(projectInfo);
                }

                if (listinfo.Count > 0)
                {
                    var sortedList = listinfo.OfType<JObject>()
                        .OrderBy(item => string.Equals(item["topyn"]?.ToString(), "y", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenByDescending(item => item["register_date"]?.ToString() ?? "")
                        .ThenByDescending(item =>
                        {
                            long.TryParse(item["id"]?.ToString(), out var idVal);
                            return idVal;
                        })
                        .ToList();
                    return new JArray(sortedList);
                }
            }
            return listinfo;
        }
    }
}
