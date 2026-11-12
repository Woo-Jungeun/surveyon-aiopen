using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SurveyonApi.Repositories;

namespace SurveyonApi.Services
{
    public class NewOldJsonService : INewOldJsonService
    {
        private readonly IPostgreLib _postgreLib;
        private readonly IConfiguration _configuration;

        public NewOldJsonService(IPostgreLib postgreLib, IConfiguration configuration)
        {
            _postgreLib = postgreLib;
            _configuration = configuration;
        }

        public class PnInUpsertItem
        {
            public string MergePn { get; set; } = "";
            public string MergeQnum { get; set; } = "";
            public string AnswerOrigin { get; set; } = "";
            public string AnswerFixedKey { get; set; } = "";
        }

        /// <summary>
        /// 레거시 .NET NewOldCeck 이관: project_list 데이터를 기반으로 pn_in 테이블 레코드를 자동 생성/병합 동기화
        /// </summary>
        public async Task<bool> NewOldCheckAsync(string projectnum, string qnum = "", string qid = "", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectnum)) return false;
            string cleanPn = projectnum.Trim();

            try
            {
                // 1. 처리 대상 (merge_pn, merge_qnum) 쌍 도출
                string sqlTargets = "SELECT DISTINCT merge_pn, merge_qnum, qnum, qnum_text, projectnum FROM public.project_list WHERE (merge_pn = @pn OR projectnum = @pn)";
                var targetParams = new List<NpgsqlParameter> { new NpgsqlParameter("@pn", cleanPn) };

                if (!string.IsNullOrWhiteSpace(qid))
                {
                    sqlTargets += " AND id = @qid";
                    targetParams.Add(new NpgsqlParameter("@qid", int.Parse(qid.Trim())));
                }
                else if (!string.IsNullOrWhiteSpace(qnum))
                {
                    sqlTargets += " AND (merge_qnum = @qnum OR qnum = @qnum OR qnum_text = @qnum)";
                    targetParams.Add(new NpgsqlParameter("@qnum", qnum.Trim()));
                }

                DataTable dtTargets = await _postgreLib.ExecuteQueryAsync(sqlTargets, targetParams, "Postgres", ct);
                if (dtTargets.Rows.Count == 0) return true;

                var targetPairs = new HashSet<(string mergePn, string mergeQnum)>();
                foreach (DataRow row in dtTargets.Rows)
                {
                    string mPn = row["merge_pn"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(mPn)) mPn = row["projectnum"]?.ToString()?.Trim() ?? cleanPn;

                    string mQnum = row["merge_qnum"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(mQnum)) mQnum = row["qnum_text"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(mQnum)) mQnum = row["qnum"]?.ToString()?.Trim() ?? "";

                    if (!string.IsNullOrEmpty(mPn) && !string.IsNullOrEmpty(mQnum))
                    {
                        targetPairs.Add((mPn, mQnum));
                    }
                }

                // 2. 전체 일괄 동기화 (qid, qnum 없을 때: N+1 쿼리 제거 및 NpgsqlBatch 일괄 적용)
                if (string.IsNullOrWhiteSpace(qid) && string.IsNullOrWhiteSpace(qnum))
                {
                    string sqlPnInBulk = "SELECT qnum, answer_origin FROM public.pn_in WHERE projectnum = @pn";
                    DataTable dtPnInBulk = await _postgreLib.ExecuteQueryAsync(sqlPnInBulk, new List<NpgsqlParameter> { new NpgsqlParameter("@pn", cleanPn) }, "Postgres", ct);
                    var pnInDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DataRow r in dtPnInBulk.Rows)
                    {
                        string qn = r["qnum"]?.ToString()?.Trim() ?? "";
                        string ao = r["answer_origin"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(qn)) pnInDict[qn] = ao;
                    }

                    string sqlListBulk = @"SELECT id, projectnum, servername, qnum, qnum_text, merge_pn, merge_qnum, answer_origin,
                                                  qnum_join1, qnum_join2, qnum_join3, qnum_join4, qnum_join5
                                           FROM public.project_list
                                           WHERE (merge_pn = @pn OR projectnum = @pn)
                                           ORDER BY mapid/1 ASC";
                    DataTable dtListBulk = await _postgreLib.ExecuteQueryAsync(sqlListBulk, new List<NpgsqlParameter> { new NpgsqlParameter("@pn", cleanPn) }, "Postgres", ct);

                    var batchUpserts = new List<PnInUpsertItem>();
                    foreach (var (mergePn, mergeQnum) in targetPairs)
                    {
                        bool isExistingInPnIn = pnInDict.TryGetValue(mergeQnum, out var oldJsonStr);
                        oldJsonStr ??= "";

                        var matchingRows = dtListBulk.Rows.Cast<DataRow>().Where(row =>
                        {
                            string mPn = row["merge_pn"]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrEmpty(mPn)) mPn = row["projectnum"]?.ToString()?.Trim() ?? "";
                            string mQnum = row["merge_qnum"]?.ToString()?.Trim() ?? "";
                            string qnText = row["qnum_text"]?.ToString()?.Trim() ?? "";
                            string qn = row["qnum"]?.ToString()?.Trim() ?? "";

                            return string.Equals(mPn, mergePn, StringComparison.OrdinalIgnoreCase) &&
                                   (string.Equals(mQnum, mergeQnum, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(qnText, mergeQnum, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(qn, mergeQnum, StringComparison.OrdinalIgnoreCase));
                        });

                        var upsertItem = ComputeNewOldData(mergePn, mergeQnum, oldJsonStr, matchingRows, isPnInRowExisting: isExistingInPnIn);
                        if (upsertItem != null)
                        {
                            batchUpserts.Add(upsertItem);
                        }
                    }

                    if (batchUpserts.Count > 0)
                    {
                        await ExecutePnInBatchUpsertAsync(batchUpserts, ct);
                    }
                }
                else
                {
                    // 단일 문항 / 단일 qid 처리
                    foreach (var (mergePn, mergeQnum) in targetPairs)
                    {
                        await ProcessSingleNewOldCheckAsync(mergePn, mergeQnum, qid, ct);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NewOldJsonService.NewOldCheckAsync ERROR] {ex.Message}");
                return false;
            }
        }

        private async Task ProcessSingleNewOldCheckAsync(string mergePn, string mergeQnum, string qid, CancellationToken ct)
        {
            // 1. 기존 pn_in 데이터 조회
            string sqlPnIn = "SELECT answer_origin FROM public.pn_in WHERE projectnum = @pn AND qnum = @qnum LIMIT 1";
            var pnInParams = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pn", mergePn),
                new NpgsqlParameter("@qnum", mergeQnum)
            };

            DataTable dtPnIn = await _postgreLib.ExecuteQueryAsync(sqlPnIn, pnInParams, "Postgres", ct);
            string oldJsonStr = dtPnIn.Rows.Count > 0 ? (dtPnIn.Rows[0]["answer_origin"]?.ToString()?.Trim() ?? "") : "";

            // 2. project_list 레코드 조회
            string sqlList = @"SELECT id, projectnum, servername, qnum, qnum_text, merge_pn, merge_qnum, answer_origin,
                                      qnum_join1, qnum_join2, qnum_join3, qnum_join4, qnum_join5
                               FROM public.project_list
                               WHERE (merge_pn = @pn OR projectnum = @pn)
                                 AND (merge_qnum = @qnum OR qnum_text = @qnum OR qnum = @qnum)
                               ORDER BY mapid/1 ASC";
            var listParams = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("@pn", mergePn),
                new NpgsqlParameter("@qnum", mergeQnum)
            };

            DataTable dtList = await _postgreLib.ExecuteQueryAsync(sqlList, listParams, "Postgres", ct);

            var upsertItem = ComputeNewOldData(mergePn, mergeQnum, oldJsonStr, dtList.Rows.Cast<DataRow>(), isPnInRowExisting: dtPnIn.Rows.Count > 0);
            if (upsertItem != null)
            {
                string sqlUpsertPnIn = @"
                    INSERT INTO public.pn_in (projectnum, qnum, answer_origin, update_date, answerfixedkeypnqnum)
                    VALUES (@pn, @qnum, @answerOrigin::jsonb, NOW(), @answerFixedKey::jsonb)
                    ON CONFLICT (projectnum, qnum) DO UPDATE SET
                        answer_origin = EXCLUDED.answer_origin,
                        answerfixedkeypnqnum = EXCLUDED.answerfixedkeypnqnum,
                        update_date = NOW();";

                var upsertParams = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@pn", upsertItem.MergePn),
                    new NpgsqlParameter("@qnum", upsertItem.MergeQnum),
                    new NpgsqlParameter("@answerOrigin", upsertItem.AnswerOrigin),
                    new NpgsqlParameter("@answerFixedKey", upsertItem.AnswerFixedKey)
                };

                await _postgreLib.ExecuteNonQueryAsync(sqlUpsertPnIn, upsertParams, "Postgres", ct);
            }

            // 3. qid가 있는 경우 project_list.merge_qnum_check 업데이트
            if (!string.IsNullOrWhiteSpace(qid) && int.TryParse(qid.Trim(), out int parsedQid))
            {
                string sqlUpdateListInfo = "UPDATE public.project_list SET merge_qnum_check = @mergeQnum WHERE id = @qid";
                var listInfoParams = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@mergeQnum", mergeQnum),
                    new NpgsqlParameter("@qid", parsedQid)
                };
                await _postgreLib.ExecuteNonQueryAsync(sqlUpdateListInfo, listInfoParams, "Postgres", ct);
            }
        }

        private PnInUpsertItem? ComputeNewOldData(string mergePn, string mergeQnum, string oldJsonStr, IEnumerable<DataRow> matchingRows, bool isPnInRowExisting = true)
        {
            JObject oldData = string.IsNullOrEmpty(oldJsonStr) ? new JObject() : JObject.Parse(oldJsonStr);
            JArray oldArray = oldData[mergeQnum] as JArray ?? new JArray();

            // 기존 answer 집합 및 answer -> fixed_key 맵 생성
            var oldAnswerFinSet = new HashSet<string>(
                oldArray.OfType<JObject>()
                        .Where(o => o["answer_origin"] != null && !string.IsNullOrWhiteSpace(o["answer"]?.ToString()))
                        .Select(o => o["answer"]!.ToString()),
                StringComparer.Ordinal
            );

            var answerToFixedKey = oldArray.OfType<JObject>()
                .Where(o => o["answer_origin"] != null && !string.IsNullOrWhiteSpace(o["answer"]?.ToString()))
                .Select(o => new
                {
                    answer = o["answer"]!.ToString(),
                    fixedKey = o["fixed_key"]?.ToString()
                })
                .Where(x => !string.IsNullOrEmpty(x.fixedKey))
                .GroupBy(x => x.answer, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().fixedKey!, StringComparer.Ordinal);

            var projectlistAnswerfinFixedkeyList = new List<JObject>();
            var projectlistAnswerfinJArray = new JArray();
            string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (DataRow row in matchingRows)
            {
                string jsonDataOrigin = row["answer_origin"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(jsonDataOrigin)) continue;

                string qnum = row["qnum"]?.ToString()?.Trim() ?? "";
                string qnumText = row["qnum_text"]?.ToString()?.Trim() ?? qnum;
                string pn = row["projectnum"]?.ToString()?.Trim() ?? "";
                string servername = row["servername"]?.ToString()?.Trim() ?? "";

                JArray? items = null;
                try
                {
                    JToken token = JToken.Parse(jsonDataOrigin);
                    if (token is JArray arr)
                    {
                        items = arr;
                    }
                    else if (token is JObject obj)
                    {
                        items = (obj[qnum] as JArray)
                             ?? (obj[qnumText] as JArray)
                             ?? (obj[mergeQnum] as JArray)
                             ?? (obj.Properties().FirstOrDefault()?.Value as JArray);
                    }
                }
                catch (Exception exParse)
                {
                    Console.WriteLine($"[NewOldJsonService] Parse answer_origin failed: {exParse.Message}");
                    continue;
                }

                if (items == null || items.Count == 0) continue;

                // qnum_join1 ~ qnum_join5 맵 파싱
                var qnumJoinMaps = new Dictionary<string, string>[5];
                for (int i = 1; i <= 5; i++)
                {
                    string colName = $"qnum_join{i}";
                    string json = row.Table.Columns.Contains(colName) ? (row[colName]?.ToString() ?? "") : "";
                    qnumJoinMaps[i - 1] = ParseQnumJoinMap(json);
                }

                foreach (JObject item in items.OfType<JObject>())
                {
                    string answerFin = item["answer_fin"]?.ToString() ?? item["answer_origin"]?.ToString() ?? item["answer"]?.ToString() ?? "";
                    string answerOrigin = item["answer_origin"]?.ToString() ?? item["answer_fin"]?.ToString() ?? item["answer"]?.ToString() ?? "";
                    string pid = item["pid"]?.ToString() ?? "";

                    List<string>? results = null;
                    if (!string.IsNullOrEmpty(pid))
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            var map = qnumJoinMaps[i];
                            if (map != null && map.TryGetValue(pid, out string? value) && !string.IsNullOrEmpty(value))
                            {
                                results ??= new List<string>(5);
                                results.Add(value);
                            }
                        }
                    }

                    if (results != null && results.Count > 0)
                    {
                        string qnumJoinAll = "[" + string.Join("|", results) + "]";
                        answerFin = qnumJoinAll + "_" + answerFin;
                    }

                    var answerkeytemp = new JObject
                    {
                        ["servername"] = servername,
                        ["merge_pn"] = mergePn,
                        ["projectnum"] = pn,
                        ["merge_qnum"] = mergeQnum,
                        ["qnum"] = qnum,
                        ["pid"] = pid,
                        ["answer_origin"] = answerOrigin,
                        ["answer"] = answerFin
                    };
                    projectlistAnswerfinFixedkeyList.Add(answerkeytemp);

                    var answerData = new JObject
                    {
                        ["qnum"] = mergeQnum,
                        ["answer"] = answerFin,
                        ["answer_fin"] = answerFin,
                        ["insert_date"] = nowStr,
                        ["update_date"] = nowStr,
                        ["answer_origin"] = answerOrigin
                    };
                    projectlistAnswerfinJArray.Add(answerData);
                }
            }

            // 3. answer 변경 여부 비교 (SHA256 해시)
            var newAnswerFinSet = new HashSet<string>(
                projectlistAnswerfinJArray.Select(o => ((JObject)o)["answer"]?.ToString() ?? ""),
                StringComparer.Ordinal
            );

            string oldHash = GetAnswerFinHash(oldAnswerFinSet);
            string newHash = GetAnswerFinHash(newAnswerFinSet);

            if (oldHash != newHash || oldArray.Count == 0 || !isPnInRowExisting)
            {
                // oldArray 중 신규에 존재하는 answer만 필터링
                var filteredOldArray = oldArray
                    .OfType<JObject>()
                    .Where(obj => newAnswerFinSet.Contains(obj["answer"]?.ToString() ?? ""))
                    .ToList();

                foreach (JObject obj in projectlistAnswerfinJArray.OfType<JObject>())
                {
                    string answerFin = obj["answer"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(answerFin)) continue;

                    if (!oldAnswerFinSet.Contains(answerFin))
                    {
                        // 신규 answer: fixed_key 발급
                        string fixedKey = GenerateFixedKey(answerFin);
                        obj["fixed_key"] = fixedKey;

                        filteredOldArray.Add(obj);
                        oldAnswerFinSet.Add(answerFin);
                        answerToFixedKey[answerFin] = fixedKey;
                    }
                    else if (answerToFixedKey.TryGetValue(answerFin, out var existingFixedKey))
                    {
                        obj["fixed_key"] = existingFixedKey;
                    }
                    else
                    {
                        string fixedKey = GenerateFixedKey(answerFin);
                        obj["fixed_key"] = fixedKey;
                        answerToFixedKey[answerFin] = fixedKey;
                    }
                }

                // PID 상세목록에도 fixed_key 매핑
                foreach (JObject item in projectlistAnswerfinFixedkeyList)
                {
                    string? ans = item["answer"]?.ToString();
                    if (ans != null && answerToFixedKey.TryGetValue(ans, out var fx))
                    {
                        item["fixed_key"] = fx;
                    }
                }

                // 결과 배열 중복 제거 및 정렬
                JArray sortedArray = new JArray(
                    filteredOldArray
                        .Where(obj => !string.IsNullOrWhiteSpace(obj["fixed_key"]?.ToString()))
                        .GroupBy(obj => obj["fixed_key"]!.ToString(), StringComparer.Ordinal)
                        .Select(g => g.First())
                        .OrderBy(obj => obj["qnum"]?.ToString() ?? "", StringComparer.Ordinal)
                        .ThenBy(obj => obj["answer"]?.ToString() ?? "", StringComparer.Ordinal)
                );

                JObject answerDataAll = new JObject();
                answerDataAll[mergeQnum] = sortedArray;

                return new PnInUpsertItem
                {
                    MergePn = mergePn,
                    MergeQnum = mergeQnum,
                    AnswerOrigin = answerDataAll.ToString(Formatting.None),
                    AnswerFixedKey = new JArray(projectlistAnswerfinFixedkeyList).ToString(Formatting.None)
                };
            }

            return null;
        }

        private async Task ExecutePnInBatchUpsertAsync(List<PnInUpsertItem> items, CancellationToken ct)
        {
            if (items == null || items.Count == 0) return;

            string connStr = _configuration.GetConnectionString("Postgres") ?? "";
            if (string.IsNullOrEmpty(connStr)) return;

            string sqlUpsertPnIn = @"
                INSERT INTO public.pn_in (projectnum, qnum, answer_origin, update_date, answerfixedkeypnqnum)
                VALUES (@pn, @qnum, @answerOrigin::jsonb, NOW(), @answerFixedKey::jsonb)
                ON CONFLICT (projectnum, qnum) DO UPDATE SET
                    answer_origin = EXCLUDED.answer_origin,
                    answerfixedkeypnqnum = EXCLUDED.answerfixedkeypnqnum,
                    update_date = NOW();";

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);

                await using var batch = new NpgsqlBatch(conn);
                foreach (var item in items)
                {
                    var bcmd = new NpgsqlBatchCommand(sqlUpsertPnIn);
                    bcmd.Parameters.AddWithValue("@pn", item.MergePn);
                    bcmd.Parameters.AddWithValue("@qnum", item.MergeQnum);
                    bcmd.Parameters.AddWithValue("@answerOrigin", item.AnswerOrigin);
                    bcmd.Parameters.AddWithValue("@answerFixedKey", item.AnswerFixedKey);
                    batch.BatchCommands.Add(bcmd);
                }

                await batch.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NewOldJsonService.ExecutePnInBatchUpsertAsync ERROR] {ex.Message}");
            }
        }

        private static Dictionary<string, string> ParseQnumJoinMap(string? json)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return map;

            try
            {
                JToken token = JToken.Parse(json);
                JArray? arr = token as JArray;
                if (token is JObject obj)
                {
                    arr = obj.Properties().FirstOrDefault()?.Value as JArray;
                }

                if (arr == null || arr.Count == 0) return map;

                foreach (JObject item in arr.OfType<JObject>())
                {
                    string? pid = item["pid"]?.ToString();
                    if (string.IsNullOrEmpty(pid)) continue;

                    string val = item["qnumjoin_val"]?.ToString() ?? "";
                    map[pid] = val;
                }
            }
            catch
            {
                // ignore parse errors
            }

            return map;
        }

        private static string GenerateFixedKey(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
            return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 12);
        }

        private static string GetAnswerFinHash(IEnumerable<string> set)
        {
            string concat = string.Join("|", set.Where(s => !string.IsNullOrEmpty(s)).OrderBy(x => x, StringComparer.Ordinal));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(concat));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
