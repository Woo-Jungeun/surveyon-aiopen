using System.Data;
using System.IO;
using System.Drawing;
using System.Text.RegularExpressions;
using Aspose.Cells;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SurveyonApi.Models;
using SurveyonApi.Repositories;

namespace SurveyonApi.Services
{
    /// <summary>
    /// AI오픈분석 › 분석 페이지 조회 로직 (레거시 JsonHong.selectPnInfo / selectPnIn / SelectPnList / selectPnLb 이관)
    ///  - 응답 JSON 필드·순서·값 규칙은 레거시와 동일하게 유지 (프런트 호환)
    ///  - 개선: 필요한 컬럼만 조회, 독립 쿼리 병렬 실행, QM 조회는 조사번호당 1회,
    ///          보기(codes) 집계 시 rawdata를 JSON 문자열 왕복 없이 1회 계산
    ///  - 보안: API 키 원문은 조회·응답하지 않음 (keyid = 마스킹 힌트)
    /// </summary>
    public interface IAnalysisService
    {
        Task<int?> ResolveQidAsync(string pn, string qnum, CancellationToken ct = default);
        Task<(string lvcode, string duplicateRemoveDate)> GetHeaderInfoAsync(string pn, string qnum, CancellationToken ct = default);
        Task<JArray> BuildInfoAsync(string pn, string qnum, string user, CancellationToken ct = default);
        Task<JArray> BuildResponsesAsync(string pn, string qnum, CancellationToken ct = default);
        Task<JArray> BuildRawdataAsync(string pn, string qnum, CancellationToken ct = default);
        Task<JArray> BuildCodesAsync(string pn, string qnum, CancellationToken ct = default);
        Task<bool> EnsureOptionJsonSavedAsync(string pn, string qnum, string user, JObject? inputSettings, string? actionType, CancellationToken ct = default);
        Task<bool> SaveInfoAsync(string pn, string qnum, string user, JObject infoObj, CancellationToken ct = default);
        Task<bool> SaveInfoAsync(string pn, string qnum, JObject infoObj, CancellationToken ct = default);
        Task<(bool success, string code, string message, int savedCount)> SaveCodesAsync(string pn, string qnum, JArray codesArray, CancellationToken ct = default);
        Task<(bool success, string code, string message, int savedCount)> SaveResponsesAsync(string pn, string qnum, string duplicateAnswer, JArray responsesArray, CancellationToken ct = default);
        Task<object> CompareResponsesAsync(string pn, string qnum, JArray currentResponses, CancellationToken ct = default);
        Task<byte[]> ExportExcelAsync(string pn, string qnum, CancellationToken ct = default);
        Task<ExcelCodePreviewResult> PreviewUploadedExcelCodesAsync(IFormFile file, string pn, string qnum, CancellationToken ct = default);
        Task<List<AnalysisImportProjectItem>> SearchImportProjectsAsync(string search, CancellationToken ct = default);
        Task<JArray> GetImportCodesAsync(string targetPn, string targetQnum, CancellationToken ct = default);
        Task<(bool ok, int addedCount, int duplicateCount)> ApplyImportCodesAsync(string pn, string qnum, JArray selectedCodes, CancellationToken ct = default);
    }

    public class AnalysisService : IAnalysisService
    {
        /// <summary>필터문항(qnum_join) 설정 시 프롬프트 앞에 붙는 안내 문구 (레거시 문구 그대로)</summary>
        public const string AddMergeText = "응답데이터의  \"[문자열]_ \"의 규칙을 가지고 있는 내용은 다른 문항의 응답을 머지한 값이므로 참고만 해서 카테고리 분류해줘.";

        private readonly IAnalysisRepository _repo;
        private readonly IQueueSendingService _queueService;
        private readonly ILogger<AnalysisService> _logger;

        public AnalysisService(IAnalysisRepository repo, IQueueSendingService queueService, ILogger<AnalysisService> logger)
        {
            _repo = repo;
            _queueService = queueService;
            _logger = logger;
        }

        // 레거시 Displayinfodb.Getqid(projectnum, qnum) — 분석 대표행 id (info.qid와 동일 기준)
        public async Task<int?> ResolveQidAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            return meta?.id;
        }

        // 레거시 option_edit_api 응답 상단의 lvcode / duplicateRemoveDate
        public async Task<(string lvcode, string duplicateRemoveDate)> GetHeaderInfoAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var lvTask = _repo.GetLvcodeAsync(pn, qnum, ct);
            var metaTask = _repo.GetQuestionMetaAsync(pn, qnum, ct);
            await Task.WhenAll(lvTask, metaTask);
            return (lvTask.Result, metaTask.Result?.duplicate_remove_date?.TrimEnd() ?? "");
        }

        // ══════════════════════════════════════════════════════════════════
        // info (레거시 selectPnInfo)
        // ══════════════════════════════════════════════════════════════════
        public async Task<JArray> BuildInfoAsync(string pn, string qnum, string user, CancellationToken ct = default)
        {
            var result = new JArray();
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            if (meta == null) return result;

            JObject opt = new JObject();
            bool hasOpt = false;
            if (!string.IsNullOrWhiteSpace(meta.option_json))
            {
                try
                {
                    var arr = JToken.Parse(meta.option_json) as JArray;
                    if (arr != null && arr.Count > 0 && arr[0] is JObject o) { opt = o; hasOpt = true; }
                }
                catch { /* 잘못된 option_json은 빈 설정으로 처리 */ }
            }

            // 레거시: option_json이 비어 있으면 각 값은 ""(초기값), 있으면 키가 없을 때 null
            string? Opt(string key) => hasOpt ? opt[key]?.ToString().TrimEnd() : "";

            string qid = meta.id.ToString();
            string listapikey = meta.listapikey?.ToString() ?? "";
            string? apikeyidOpt = hasOpt ? opt["apikeyid"]?.ToString().TrimEnd() : null;
            if (!string.IsNullOrEmpty(apikeyidOpt)) listapikey = apikeyidOpt;

            string? selapiModel = Opt("model_select");
            string promptText = Opt("prompt_string") ?? "";
            string? promptExBackup = Opt("prompt_string_ex_backup");
            string? promptResBackup = Opt("prompt_string_res_backup");
            string? keywordString = Opt("keyword_string");
            string? resultLang = Opt("result_lang");
            string? openItemLv1 = Opt("open_item_lv1");
            string? openItemLv2 = Opt("open_item_lv2");
            string? openItemLv3 = Opt("open_item_lv3");
            string? temperature = Opt("temperature");
            string? dataType = Opt("data_type");

            // 독립 조회 병렬 실행
            var joinTask = _repo.HasQnumJoinAsync(meta.id, ct);
            var userTask = _repo.GetUserKeyInfoAsync(user, ct);
            var langTask = _repo.GetCountryCodesAsync(ct);
            var modelTask = _repo.GetModelCodesAsync(ct);
            await Task.WhenAll(joinTask, userTask, langTask, modelTask);

            // 필터문항 설정이 있을 경우 프롬프트 지침 추가 / 없으면 제거
            if (joinTask.Result)
            {
                if (!promptText.Contains(AddMergeText)) promptText = AddMergeText + Environment.NewLine + promptText;
            }
            else if (promptText.Contains(AddMergeText))
            {
                promptText = promptText.Replace(AddMergeText, "").Trim();
            }

            // API 키 선택값 (레거시: 문항 설정 → 사용자 범용 설정 순으로 덮어씀)
            var userInfo = userTask.Result;
            string useapikey = userInfo?.useapikey?.ToString() ?? "";
            string keygubun = "";
            if (listapikey != "") keygubun = listapikey;
            if (useapikey != "") keygubun = useapikey;

            string sellistapikey;
            if (keygubun != "")
            {
                // 레거시 GetTokenkey(user, rid).Item2 = rid (없으면 빈 값)
                sellistapikey = int.TryParse(keygubun, out int rid) && await _repo.ApiKeyExistsAsync(rid, ct) ? keygubun : "";
            }
            else
            {
                sellistapikey = "0";
            }

            var apiKeys = await _repo.GetApiKeyListAsync(user, userInfo?.groupposition?.Trim() ?? "", ct);
            var apikeyArr = new JArray();
            foreach (var k in apiKeys)
            {
                apikeyArr.Add(new JObject
                {
                    // 보안 S3: 레거시는 키 원문을 내려보냈음 → 마스킹 힌트로 대체 (선택은 keyvalue=rid 기준이라 영향 없음)
                    ["keyid"] = MaskKeyHint(k.keyhint),
                    ["keyvalue"] = k.rid.ToString(),
                    ["keytext"] = k.keycontent?.TrimEnd() ?? "",
                    ["keyselected"] = sellistapikey,
                });
            }

            string langid = resultLang ?? "1";
            var langArr = new JArray();
            foreach (DataRow r in langTask.Result.Rows)
            {
                langArr.Add(new JObject
                {
                    ["keyid"] = r["id"]?.ToString()?.TrimEnd(),
                    ["keyvalue"] = r["lang"]?.ToString()?.TrimEnd(),
                    ["keytext"] = r["name"]?.ToString()?.TrimEnd(),
                    ["keyselected"] = langid,
                });
            }

            string modelval = selapiModel ?? "GPT_4o";
            var modelArr = new JArray();
            foreach (DataRow r in modelTask.Result.Rows)
            {
                modelArr.Add(new JObject
                {
                    ["keyid"] = r["id"]?.ToString()?.TrimEnd(),
                    ["keyvalue"] = r["modelvalue"]?.ToString()?.TrimEnd(),
                    ["keytext"] = r["modelname"]?.ToString()?.TrimEnd(),
                    ["keyselected"] = modelval,
                });
            }

            string questionFin = meta.question_fin?.TrimEnd() ?? "";
            if (string.IsNullOrWhiteSpace(keywordString)) keywordString = questionFin;

            result.Add(new JObject
            {
                ["qid"] = qid,
                ["qnum_text"] = qnum,
                ["servername"] = meta.servername?.TrimEnd() ?? "",
                ["projectname"] = meta.projectname?.TrimEnd() ?? "",
                ["projectpof"] = meta.projectpof?.TrimEnd() ?? "",
                ["question_fin"] = questionFin,
                ["keyword_string"] = keywordString,
                ["apikeyid"] = keygubun,
                ["apikey"] = apikeyArr,
                ["model_select"] = modelArr,
                ["prompt_string"] = promptText,
                ["prompt_string_ex_backup"] = promptExBackup,
                ["prompt_string_res_backup"] = promptResBackup,
                ["result_lang"] = langArr,
                ["open_item_lv1"] = openItemLv1,
                ["open_item_lv2"] = openItemLv2,
                ["open_item_lv3"] = openItemLv3,
                ["data_type"] = dataType,
                ["temperature"] = temperature,
                ["lvcode"] = meta.lvcode?.TrimEnd() ?? "3"
            });
            return result;
        }

        /// <summary>키 힌트는 이미 마스킹된 값만 허용. sk- 원문 형태가 들어있으면 강제 마스킹.</summary>
        private static string MaskKeyHint(string? hint)
        {
            string h = hint?.Trim() ?? "";
            if (h == "") return "****";
            if (h.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && h.Length > 12) return "sk-****";
            return h;
        }

        // ══════════════════════════════════════════════════════════════════
        // responses (레거시 selectPnIn) — NewOldCheck 선행은 컨트롤러에서 수행
        // ══════════════════════════════════════════════════════════════════
        public async Task<JArray> BuildResponsesAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var result = new JArray();
            var pnInTask = _repo.GetPnInAsync(pn, qnum, ct);
            var pnLbTask = _repo.GetPnLbAsync(pn, qnum, ct);
            await Task.WhenAll(pnInTask, pnLbTask);

            var pnIn = pnInTask.Result;
            if (pnIn == null) return result;

            string updateDate = pnIn.update_date?.TrimEnd() ?? "";
            if (updateDate == "") updateDate = pnIn.input_date?.TrimEnd() ?? "";

            // 보기 사전: lv123code → [lv1, lv2, lv3, lv1code, lv2code, lv123code]
            var diclb = new Dictionary<string, string?[]>();
            foreach (var c in ParseArray(pnLbTask.Result?.result_json).OfType<JObject>())
            {
                string? lv123code = Str(c, "lv123code");
                diclb[lv123code ?? ""] = new[] { Str(c, "lv1"), Str(c, "lv2"), Str(c, "lv3"), Str(c, "lv1code"), Str(c, "lv2code"), lv123code };
            }

            // 응답자 코딩 결과: fixed_key → cid → 값
            var pidlv3 = new Dictionary<string, Dictionary<string, string?>>();
            var pidlv123code = new Dictionary<string, Dictionary<string, string?>>();
            var pidsentiment = new Dictionary<string, Dictionary<string, string?>>();
            var pidrecheckyn = new Dictionary<string, Dictionary<string, string?>>();
            var pidexcerpts = new Dictionary<string, Dictionary<string, string?>>();
            foreach (var o in ParseArray(pnIn.answer_fin).OfType<JObject>())
            {
                string fk = Str(o, "fixed_key") ?? "";
                string cid = Str(o, "cid") ?? "";
                Set(pidlv3, fk, cid, Str(o, "lv3"));
                Set(pidsentiment, fk, cid, Str(o, "sentiment"));
                Set(pidlv123code, fk, cid, Str(o, "lv123code"));
                Set(pidrecheckyn, fk, cid, Str(o, "recheckyn"));
                Set(pidexcerpts, fk, cid, Str(o, "excerpts"));
            }

            foreach (var row in ParseArray(pnIn.origin_items).OfType<JObject>())
            {
                string? answerOrigin = Str(row, "answer_origin");
                string? answer = Str(row, "answer_fin");
                string? fixedKey = Str(row, "fixed_key");
                string fk = fixedKey ?? "";

                int rowcnt = pidlv123code.TryGetValue(fk, out var codesByCid) ? codesByCid.Count : 1;
                for (int cid = 1; cid <= rowcnt; cid++)
                {
                    string sid = cid.ToString();
                    string? lv1 = "", lv2 = "", lv3 = "", lv1code = "", lv2code = "", lv123code = "";
                    string? sentiment = "", recheckyn = "", excerpts = "";

                    if (codesByCid != null && codesByCid.ContainsKey(sid))
                    {
                        lv3 = pidlv3[fk][sid];
                        lv123code = codesByCid[sid];
                        sentiment = pidsentiment[fk][sid];
                        recheckyn = pidrecheckyn[fk][sid];
                        excerpts = pidexcerpts[fk][sid];

                        if (lv123code != null && diclb.TryGetValue(lv123code, out var lb))
                        {
                            if (!string.IsNullOrEmpty(lb[0])) lv1 = lb[0];
                            if (!string.IsNullOrEmpty(lb[1])) lv2 = lb[1];
                            if (!string.IsNullOrEmpty(lb[2])) lv3 = lb[2];
                            if (!string.IsNullOrEmpty(lb[3])) lv1code = lb[3];
                            if (!string.IsNullOrEmpty(lb[4])) lv2code = lb[4];
                            if (!string.IsNullOrEmpty(lb[5])) lv123code = lb[5];
                            result.Add(ResponseRow(fixedKey, cid, lv1, lv2, lv3, lv1code, lv2code, lv123code, sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate));
                        }
                        else if (lv3 != null)
                        {
                            result.Add(ResponseRow(fixedKey, cid, "", "", lv3, "", "", "", sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate));
                        }
                        else if (cid == 1)
                        {
                            result.Add(ResponseRow(fixedKey, cid, lv1, lv2, lv3, "", "", "", sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate));
                        }
                    }
                    else if (cid == 1)
                    {
                        result.Add(ResponseRow(fixedKey, cid, lv1, lv2, lv3, "", "", "", sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate));
                    }
                }
            }
            return result;
        }

        private static JObject ResponseRow(string? fixedKey, int cid, string? lv1, string? lv2, string? lv3, string? lv1code, string? lv2code,
            string? lv123code, string? sentiment, string? recheckyn, string? excerpts, string? answerOrigin, string? answer, string updateDate)
            => new JObject
            {
                ["fixed_key"] = fixedKey,
                ["cid"] = cid,
                ["lv1"] = lv1,
                ["lv2"] = lv2,
                ["lv3"] = lv3,
                ["lv1code"] = lv1code,
                ["lv2code"] = lv2code,
                ["lv123code"] = lv123code,
                ["sentiment"] = sentiment,
                ["recheckyn"] = recheckyn,
                ["excerpts"] = excerpts,
                ["answer_origin"] = answerOrigin,
                ["answer"] = answer,
                ["update_date"] = updateDate
            };

        // ══════════════════════════════════════════════════════════════════
        // rawdata (레거시 SelectPnList)
        // ══════════════════════════════════════════════════════════════════
        public async Task<JArray> BuildRawdataAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var pnInTask = _repo.GetPnInAsync(pn, qnum, ct);
            var pnLbTask = _repo.GetPnLbAsync(pn, qnum, ct);
            var srcTask = _repo.GetSourceRowsAsync(pn, qnum, ct);
            await Task.WhenAll(pnInTask, pnLbTask, srcTask);

            var pnIn = pnInTask.Result;
            var diclb = new Dictionary<string, string?[]>();
            var pidlv123code = new Dictionary<string, Dictionary<string, string?>>();
            var pidsentiment = new Dictionary<string, Dictionary<string, string?>>();
            var pidrecheckyn = new Dictionary<string, Dictionary<string, string?>>();
            var keyToAnswer = new Dictionary<string, string?>();
            var keyToAnswerFin = new Dictionary<string, string?>();

            if (pnIn != null)
            {
                foreach (var c in ParseArray(pnLbTask.Result?.result_json).OfType<JObject>())
                {
                    string? lv123code = Str(c, "lv123code");
                    diclb[lv123code ?? ""] = new[] { Str(c, "lv1"), Str(c, "lv2"), Str(c, "lv3"), lv123code };
                }

                foreach (var o in ParseArray(pnIn.answer_fin).OfType<JObject>())
                {
                    string fk = Str(o, "fixed_key") ?? "";
                    string cid = Str(o, "cid") ?? "";
                    string? recheckyn = Str(o, "recheckyn");
                    if (recheckyn != "완료") recheckyn = "";
                    Set(pidlv123code, fk, cid, Str(o, "lv123code"));
                    Set(pidsentiment, fk, cid, Str(o, "sentiment"));
                    Set(pidrecheckyn, fk, cid, recheckyn);
                }

                foreach (var row in ParseArray(pnIn.origin_items).OfType<JObject>())
                {
                    string origin = Str(row, "answer_origin")?.ToLower() ?? "";
                    keyToAnswer[origin] = Str(row, "fixed_key");
                    keyToAnswerFin[origin] = Str(row, "answer_fin");
                }
            }

            // PID별 최초 응답일: 조사번호당 1회만 QM 조회 (레거시는 원본 문항행마다 반복 조회)
            var sources = srcTask.Result;
            var pnList = sources.Select(s => s.projectnum?.TrimEnd() ?? "").Distinct().ToList();
            var dateTasks = pnList.ToDictionary(p => p, p => _repo.GetFirstWriteDatesAsync(p, ct));
            await Task.WhenAll(dateTasks.Values);

            // 레거시는 원본 행을 순회하며 piddate를 누적(먼저 등록된 값 유지)했으므로 동일 순서로 병합
            var piddate = new Dictionary<string, string>();
            var result = new JArray();
            foreach (var src in sources)
            {
                string srcQnum = src.qnum?.TrimEnd() ?? "";
                foreach (var kv in dateTasks[src.projectnum?.TrimEnd() ?? ""].Result)
                {
                    if (!piddate.ContainsKey(kv.Key)) piddate[kv.Key] = kv.Value;
                }

                foreach (var item in ParseArray(src.origin_items).OfType<JObject>())
                {
                    string answerOrigin = item["answer_origin"]?.ToString().TrimEnd().ToLower() ?? "";
                    string pid = item["pid"]?.ToString() ?? "";
                    string fixedKey = keyToAnswer.TryGetValue(answerOrigin, out var fkv) ? (fkv ?? "") : "";
                    string? answerfin = keyToAnswerFin.TryGetValue(answerOrigin, out var af) ? af : "";

                    // 레거시와 동일: cid "1"이 없으면 사전을 새로 만들어 "1"="" 로 교체 (이후 행에도 영향)
                    if (!pidlv123code.TryGetValue(fixedKey, out var codeMap) || !codeMap.ContainsKey("1"))
                        pidlv123code[fixedKey] = new Dictionary<string, string?> { ["1"] = "" };
                    if (!pidrecheckyn.TryGetValue(fixedKey, out var reMap) || !reMap.ContainsKey("1"))
                        pidrecheckyn[fixedKey] = new Dictionary<string, string?> { ["1"] = "" };

                    string? recheckyn = pidrecheckyn[fixedKey]["1"];
                    string piddateFin = piddate.TryGetValue(pid, out var d) ? d : "";
                    string? lv123code = "";
                    string? sentiment = "";

                    var codes = pidlv123code[fixedKey];
                    int rowcnt = codes.Count;
                    for (int cid = 1; cid <= rowcnt; cid++)
                    {
                        string sid = cid.ToString();
                        if (!codes.ContainsKey(sid)) continue;

                        lv123code = codes[sid];
                        if (pidsentiment.TryGetValue(fixedKey, out var sm) && sm.TryGetValue(sid, out var s)) sentiment = s;
                        recheckyn = pidrecheckyn[fixedKey].TryGetValue(sid, out var rc) ? rc : "";

                        if (lv123code != null && diclb.TryGetValue(lv123code, out var lb))
                        {
                            result.Add(RawRow(pid, srcQnum, cid, answerOrigin, answerfin,
                                lb[0] ?? "", lb[1] ?? "", lb[2] ?? "", lv123code, sentiment, recheckyn, piddateFin));
                        }
                        else
                        {
                            result.Add(RawRow(pid, srcQnum, cid, answerOrigin, answerfin, "", "", "", "", "", "", piddateFin));
                        }
                    }
                }
            }
            return result;
        }

        private static JObject RawRow(string pid, string qnum, int cid, string answerOrigin, string? answerfin, string lv1, string lv2, string lv3,
            string? lv123code, string? sentiment, string? recheckyn, string firstWriteDate)
            => new JObject
            {
                ["pid"] = pid,
                ["qnum"] = qnum,
                ["cid"] = cid,
                ["answer_origin"] = answerOrigin,
                ["answerfin"] = answerfin,
                ["lv1"] = lv1,
                ["lv2"] = lv2,
                ["lv3"] = lv3,
                ["lv123code"] = lv123code,
                ["sentiment"] = sentiment,
                ["recheckyn"] = recheckyn,
                ["firstWriteDate"] = firstWriteDate,
            };

        // ══════════════════════════════════════════════════════════════════
        // codes (레거시 selectPnLb) — ex_sum은 rawdata 1회 계산 결과로 집계
        // ══════════════════════════════════════════════════════════════════
        public async Task<JArray> BuildCodesAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var result = new JArray();
            var pnLb = await _repo.GetPnLbAsync(pn, qnum, ct);
            if (pnLb == null) return result;

            var raw = await BuildRawdataAsync(pn, qnum, ct);
            var exSum = new Dictionary<string, int>();
            foreach (var r in raw)
            {
                string key = r["lv123code"]?.ToString() ?? "";
                exSum[key] = exSum.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            string updateDate = pnLb.update_date?.TrimEnd() ?? "";
            if (updateDate == "") updateDate = pnLb.insert_date?.TrimEnd() ?? "";

            // 정렬: 설문 보기 우선 → lv1code(없는 것 먼저, 숫자 오름차순) → lv2code(동일) → lv123code 숫자 오름차순
            var sorted = ParseArray(pnLb.result_json).OfType<JObject>()
                .OrderBy(o => o["ex_gubun"]?.ToString() != "survey" && o["ex_gubun"]?.ToString() != "설문(수정불가)" && o["gubun_name"]?.ToString() != "설문 보기")
                .ThenBy(o => string.IsNullOrWhiteSpace(o["lv1code"]?.ToString()) ? 0 : 1)
                .ThenBy(o => int.TryParse(o["lv1code"]?.ToString(), out var v1) ? v1 : int.MaxValue)
                .ThenBy(o => string.IsNullOrWhiteSpace(o["lv2code"]?.ToString()) ? 0 : 1)
                .ThenBy(o => int.TryParse(o["lv2code"]?.ToString(), out var v2) ? v2 : int.MaxValue)
                .ThenBy(o => int.TryParse(o["lv123code"]?.ToString(), out var v3) ? v3 : int.MaxValue);

            foreach (var row in sorted)
            {
                string? lv123code = Str(row, "lv123code");
                string? rawExGubun = Str(row, "ex_gubun");
                string? rawGubunName = Str(row, "gubun_name");
                string? rawIsEditable = Str(row, "is_editable");
                bool? rawIsLock = row["is_lock"]?.Value<bool>();

                // 1. 레거시 데이터 폴백: gubun_name 결정
                string gubunName = rawGubunName ?? "";
                if (string.IsNullOrWhiteSpace(gubunName))
                {
                    if (rawExGubun == "설문(수정불가)" || rawExGubun == "survey" || rawExGubun == "설문지")
                        gubunName = "설문 보기";
                    else if (rawExGubun == "트래킹" || rawExGubun == "트래킹보기")
                        gubunName = "트래킹 보기";
                    else
                        gubunName = "자동분석 보기";
                }

                // 2. 레거시 데이터 폴백: is_editable / is_lock 결정
                bool isLock;
                if (!string.IsNullOrWhiteSpace(rawIsEditable))
                {
                    isLock = (rawIsEditable == "불가");
                }
                else if (rawIsLock.HasValue)
                {
                    isLock = rawIsLock.Value;
                }
                else
                {
                    // 신규 키가 없는 레거시 데이터:
                    // 설문 보기나 트래킹 보기는 기본적으로 "불가", 그 외는 "가능"
                    if (gubunName == "설문 보기" || gubunName == "트래킹 보기" || rawExGubun == "설문(수정불가)" || rawExGubun == "survey")
                        isLock = true;
                    else
                        isLock = false;
                }

                string isEditable = isLock ? "불가" : "가능";

                // 3. ex_gubun 이원화: 오직 "설문(수정불가)" or "분석(수정가능)" (Python 워커 호환용)
                string exGubun = isLock ? "설문(수정불가)" : "분석(수정가능)";

                result.Add(new JObject
                {
                    ["qnum"] = qnum,
                    ["lv1"] = Str(row, "lv1"),
                    ["lv2"] = Str(row, "lv2"),
                    ["lv3"] = Str(row, "lv3"),
                    ["lv1code"] = Str(row, "lv1code"),
                    ["lv2code"] = Str(row, "lv2code"),
                    ["lv123code"] = lv123code,
                    ["lv321code"] = Str(row, "lv321code"),
                    ["ex_gubun"] = exGubun,
                    ["gubun_name"] = gubunName,
                    ["is_editable"] = isEditable,
                    ["is_lock"] = isLock,
                    // 버그 B6 수정: lv123code가 null인 보기도 예외 없이 0건 처리
                    ["ex_sum"] = exSum.TryGetValue(lv123code ?? "", out int cnt) ? cnt : 0,
                    ["update_date"] = updateDate,
                    ["summary"] = Str(row, "summary"),
                    ["representative_response"] = Str(row, "representative_response")
                });
            }
            return result;
        }

        // ── 공통 헬퍼 ─────────────────────────────────────────────────────
        private static JArray ParseArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "null") return new JArray();
            try { return JToken.Parse(json) as JArray ?? new JArray(); }
            catch { return new JArray(); }
        }

        /// <summary>레거시 o["key"]?.ToString()?.TrimEnd() 와 동일</summary>
        private static string? Str(JObject o, string key) => o[key]?.ToString()?.TrimEnd();

        private static void Set(Dictionary<string, Dictionary<string, string?>> map, string fk, string cid, string? value)
        {
            if (!map.TryGetValue(fk, out var inner))
            {
                inner = new Dictionary<string, string?>();
                map[fk] = inner;
            }
            inner[cid] = value;
        }

        public Task<bool> SaveInfoAsync(string pn, string qnum, JObject infoObj, CancellationToken ct = default)
        {
            return EnsureOptionJsonSavedAsync(pn, qnum, "", infoObj, "info", ct);
        }

        public Task<bool> SaveInfoAsync(string pn, string qnum, string user, JObject infoObj, CancellationToken ct = default)
        {
            return EnsureOptionJsonSavedAsync(pn, qnum, user, infoObj, "info", ct);
        }

        public async Task<bool> EnsureOptionJsonSavedAsync(
            string pn,
            string qnum,
            string user,
            JObject? inputSettings,
            string? actionType,
            CancellationToken ct = default)
        {
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            if (meta == null)
            {
                return false;
            }

            // 1. 기존 DB의 option_json 파싱 (기존 설정 및 프롬프트 백업 로드)
            JObject existingOpt = new JObject();
            if (!string.IsNullOrWhiteSpace(meta.option_json))
            {
                try
                {
                    var arr = JToken.Parse(meta.option_json) as JArray;
                    if (arr != null && arr.Count > 0 && arr[0] is JObject o)
                    {
                        existingOpt = o;
                    }
                }
                catch { }
            }

            // 2. 사용자 입력 설정 또는 기존 DB 값 병합
            string qFin = inputSettings?["question_fin"]?.ToString()?.Trim()
                          ?? existingOpt["keyword_string"]?.ToString()?.Trim()
                          ?? meta.question_fin?.Trim()
                          ?? "";

            string promptString = inputSettings?["prompt_string"]?.ToString()?.Trim()
                                  ?? existingOpt["prompt_string"]?.ToString()?.Trim()
                                  ?? "";

            string modelSelect = inputSettings?["model_select"]?.ToString()?.Trim()
                                 ?? existingOpt["model_select"]?.ToString()?.Trim()
                                 ?? "GPT-5.2";

            string resultLang = inputSettings?["result_lang"]?.ToString()?.Trim()
                                ?? existingOpt["result_lang"]?.ToString()?.Trim()
                                ?? "Korean";
            if (resultLang == "한국어") resultLang = "Korean";

            object temperatureVal = 0.2;
            if (inputSettings?["temperature"] != null)
            {
                if (double.TryParse(inputSettings["temperature"]?.ToString(), out double parsedTemp))
                    temperatureVal = parsedTemp;
            }
            else if (existingOpt["temperature"] != null)
            {
                if (double.TryParse(existingOpt["temperature"]?.ToString(), out double parsedTemp))
                    temperatureVal = parsedTemp;
            }

            // 분류 개수: 레거시 기준 lv1 = 소분류, lv2 = 중분류, lv3 = 대분류
            string openItemLv1 = inputSettings?["open_item_lv1"]?.ToString()?.Trim()
                                 ?? existingOpt["open_item_lv1"]?.ToString()?.Trim()
                                 ?? "0~50"; // 소분류

            string openItemLv2 = inputSettings?["open_item_lv2"]?.ToString()?.Trim()
                                 ?? existingOpt["open_item_lv2"]?.ToString()?.Trim()
                                 ?? "0"; // 중분류

            string openItemLv3 = inputSettings?["open_item_lv3"]?.ToString()?.Trim()
                                 ?? existingOpt["open_item_lv3"]?.ToString()?.Trim()
                                 ?? "0"; // 대분류

            // 3. API 키 확인 및 시크릿 복호화
            int? reqApiKeyId = null;
            if (inputSettings?["apikeyid"] != null && int.TryParse(inputSettings["apikeyid"]?.ToString(), out int parsedKid) && parsedKid > 0)
            {
                reqApiKeyId = parsedKid;
            }
            else if (existingOpt["apikeyid"] != null && int.TryParse(existingOpt["apikeyid"]?.ToString(), out int parsedOptKid) && parsedOptKid > 0)
            {
                reqApiKeyId = parsedOptKid;
            }
            else if (meta.listapikey.HasValue && meta.listapikey.Value > 0)
            {
                reqApiKeyId = meta.listapikey.Value;
            }

            var (secretKey, resolvedKeyId) = await _repo.GetResolvedApiKeySecretAsync(reqApiKeyId, user, ct);

            // 4. 프롬프트 백업 히스토리 관리
            string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string actionNorm = (actionType ?? "").ToLowerInvariant();

            JObject promptBackupEx = ParsePromptBackup(existingOpt["prompt_string_ex_backup"]);
            JObject promptBackupRes = ParsePromptBackup(existingOpt["prompt_string_res_backup"]);

            if (actionNorm is "analyze" or "보기" || inputSettings?["ev"]?.ToString() == "2")
            {
                if (!string.IsNullOrWhiteSpace(promptString))
                {
                    AddPromptBackup(promptBackupEx, datetime, promptString);
                }
            }
            else if (actionNorm is "respondent" or "응답" || inputSettings?["ev"]?.ToString() == "3")
            {
                if (!string.IsNullOrWhiteSpace(promptString))
                {
                    AddPromptBackup(promptBackupRes, datetime, promptString);
                }
            }

            // 5. Python 워커가 요구하는 표준 option_json 객체 조립
            string selectColumnTitle = !string.IsNullOrWhiteSpace(meta.merge_qnum)
                ? meta.merge_qnum
                : (!string.IsNullOrWhiteSpace(meta.qnum_text) ? meta.qnum_text : qnum);

            var itemnew = new JObject
            {
                ["apikey"] = secretKey,
                ["apikeyid"] = resolvedKeyId > 0 ? resolvedKeyId.ToString() : (reqApiKeyId?.ToString() ?? ""),
                ["data_type"] = !string.IsNullOrWhiteSpace(meta.load_fin) ? meta.load_fin : "DB",
                ["projectnum"] = !string.IsNullOrWhiteSpace(meta.merge_pn) ? meta.merge_pn : pn,
                ["result_lang"] = resultLang,
                ["select_rows"] = "",
                ["temperature"] = JToken.FromObject(temperatureVal),
                ["model_select"] = modelSelect,
                ["open_item_lv1"] = openItemLv1,
                ["open_item_lv2"] = openItemLv2,
                ["open_item_lv3"] = openItemLv3,
                ["prompt_string"] = promptString,
                ["keyword_string"] = qFin,
                ["select_column_id"] = "pid",
                ["select_column_title"] = selectColumnTitle,
                ["prompt_string_ex_backup"] = promptBackupEx,
                ["prompt_string_res_backup"] = promptBackupRes
            };

            string? lvcodeVal = inputSettings?["lvcode"]?.ToString()?.Trim()
                              ?? existingOpt["lvcode"]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(lvcodeVal))
            {
                itemnew["lvcode"] = lvcodeVal;
            }

            var jsonArray = new JArray { itemnew };
            string optionJson = jsonArray.ToString(Newtonsoft.Json.Formatting.None);

            // 6. DB 저장 (사용자/그룹 정보 함께 갱신)
            var (username, usergroup) = await _repo.GetUserAndGroupAsync(user, pn, ct);
            int affected = await _repo.SaveOptionJsonFullAsync(
                pn,
                qnum,
                optionJson,
                qFin,
                resolvedKeyId,
                user,
                username,
                usergroup,
                ct);

            if (!string.IsNullOrEmpty(lvcodeVal))
            {
                await _repo.UpdateLvcodeAsync(pn, qnum, lvcodeVal, ct);
            }

            return affected > 0;
        }

        private static JObject ParsePromptBackup(JToken? token)
        {
            if (token == null) return new JObject();
            if (token is JObject obj) return (JObject)obj.DeepClone();
            if (token.Type == JTokenType.String)
            {
                string s = token.ToString().Trim();
                if (!string.IsNullOrEmpty(s) && s != "{}")
                {
                    try { return JObject.Parse(s); } catch { }
                }
            }
            return new JObject();
        }

        private static void AddPromptBackup(JObject backupObj, string datetime, string promptString)
        {
            bool duplicate = backupObj.Properties().Any(p => p.Value?.ToString() == promptString);
            if (!duplicate)
            {
                backupObj[datetime] = promptString;
            }
            // 최신 10개만 유지
            var sorted = backupObj.Properties()
                .Where(p => DateTime.TryParse(p.Name, out _))
                .OrderByDescending(p => DateTime.Parse(p.Name))
                .ToList();
            foreach (var prop in sorted.Skip(10))
            {
                backupObj.Remove(prop.Name);
            }
        }

        private static readonly string[] PatchFields = new[]
        {
            "lv1", "lv2", "lv3", "lv1code", "lv2code", "lv123code"
        };

        private static string CleanText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Trim().Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        }

        public static (string updatedJson, bool hasChanged) ApplyChangesToAnswer(
            string answerJson,
            string beforeJson,
            string newJson)
        {
            if (string.IsNullOrWhiteSpace(answerJson) || string.IsNullOrWhiteSpace(beforeJson) || string.IsNullOrWhiteSpace(newJson))
                return (answerJson, false);

            JArray answerArr;
            JArray beforeArr;
            JArray newArr;
            try
            {
                answerArr = JArray.Parse(answerJson);
                beforeArr = JArray.Parse(beforeJson);
                newArr = JArray.Parse(newJson);
            }
            catch
            {
                return (answerJson, false);
            }

            // 1) before/new를 lv321code 기준으로 매핑
            var beforeByLv321 = beforeArr.OfType<JObject>()
                .Where(o => !string.IsNullOrWhiteSpace(o.Value<string>("lv321code")))
                .GroupBy(o => o.Value<string>("lv321code")!)
                .ToDictionary(g => g.Key, g => g.First());

            var newByLv321 = newArr.OfType<JObject>()
                .Where(o => !string.IsNullOrWhiteSpace(o.Value<string>("lv321code")))
                .GroupBy(o => o.Value<string>("lv321code")!)
                .ToDictionary(g => g.Key, g => g.First());

            // 2) lv321code로 매칭된 before/new에서 변경된 컬럼만 patch 객체로 생성 (매칭키: before.lv123code)
            var patchByLv123 = new Dictionary<string, JObject>();
            foreach (var kv in beforeByLv321)
            {
                string lv321 = kv.Key;
                var b = kv.Value;
                if (!newByLv321.TryGetValue(lv321, out var n)) continue;

                string lv123 = (b.Value<string>("lv123code") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(lv123)) continue;

                var patch = new JObject();
                foreach (var f in PatchFields)
                {
                    var bTok = b.TryGetValue(f, out var bt) ? bt : JValue.CreateNull();
                    var nTok = n.TryGetValue(f, out var nt) ? nt : JValue.CreateNull();

                    string bs = (bTok.Type == JTokenType.Null) ? "" : (bTok.ToString() ?? "").Trim();
                    string ns = (nTok.Type == JTokenType.Null) ? "" : (nTok.ToString() ?? "").Trim();

                    if (!string.Equals(bs, ns, StringComparison.Ordinal))
                    {
                        patch[f] = nTok.DeepClone();
                    }
                }

                if (patch.Properties().Any())
                {
                    if (!patchByLv123.TryGetValue(lv123, out var existing))
                    {
                        patchByLv123[lv123] = patch;
                    }
                    else
                    {
                        foreach (var p in patch.Properties())
                            existing[p.Name] = p.Value;
                    }
                }
            }

            if (patchByLv123.Count == 0)
            {
                return (answerJson, false);
            }

            // 3) answer 업데이트: answer.lv123code가 patchByLv123에 있으면 변경 필드 반영
            bool anyUpdated = false;
            foreach (var token in answerArr)
            {
                if (token is not JObject a) continue;
                string aLv123 = (a.Value<string>("lv123code") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(aLv123)) continue;

                if (patchByLv123.TryGetValue(aLv123, out var patch))
                {
                    foreach (var p in patch.Properties())
                    {
                        a[p.Name] = p.Value?.DeepClone();
                    }
                    anyUpdated = true;
                }
            }

            return (answerArr.ToString(Newtonsoft.Json.Formatting.None), anyUpdated);
        }

        private static Dictionary<string, (string code, string sentiment)> BuildPidCodeMap(JArray responsesArray)
        {
            var pidMap = new Dictionary<string, (List<string> codes, List<string> sentiments)>();

            foreach (var token in responsesArray)
            {
                if (token is not JObject row) continue;
                string pid = row["pid"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(pid))
                    pid = row["fixed_key"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(pid)) continue;

                string code = row["lv123code"]?.ToString()?.Trim() ?? "";
                string sentiment = row["sentiment"]?.ToString()?.Trim() ?? "";

                if (!pidMap.TryGetValue(pid, out var entry))
                {
                    entry = (new List<string>(), new List<string>());
                    pidMap[pid] = entry;
                }

                if (!string.IsNullOrEmpty(code) && !entry.codes.Contains(code))
                {
                    entry.codes.Add(code);
                    entry.sentiments.Add(sentiment);
                }
            }

            var result = new Dictionary<string, (string code, string sentiment)>();
            foreach (var kvp in pidMap)
            {
                string combinedCodes = string.Join(",", kvp.Value.codes);
                string combinedSentiments = string.Join(",", kvp.Value.sentiments);
                result[kvp.Key] = (combinedCodes, combinedSentiments);
            }

            return result;
        }

        public async Task<(bool success, string code, string message, int savedCount)> SaveCodesAsync(
            string pn, string qnum, JArray codesArray, CancellationToken ct = default)
        {
            if (codesArray == null || codesArray.Count == 0)
            {
                return (false, "701", "저장할 보기 데이터가 없습니다.", 0);
            }

            // 1. 유효성 검사 (코드 중복 및 빈값 검사 -> 오류코드 762)
            var seenCodes = new HashSet<string>();
            var duplicateCodes = new List<string>();
            var emptyEntries = new List<string>();

            foreach (var token in codesArray)
            {
                if (token is not JObject row) continue;
                string code = row["lv123code"]?.ToString()?.Trim() ?? "";
                string lv3 = row["lv3"]?.ToString()?.Trim() ?? "";

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(lv3))
                {
                    emptyEntries.Add(string.IsNullOrEmpty(code) ? "코드빈값" : "보기명빈값");
                }
                else
                {
                    if (!seenCodes.Add(code))
                    {
                        duplicateCodes.Add(code);
                    }
                }
            }

            if (duplicateCodes.Count > 0)
            {
                return (false, "762", $"보기 코드 중복: {string.Join(", ", duplicateCodes.Distinct())}", 0);
            }
            if (emptyEntries.Count > 0)
            {
                return (false, "762", "보기 코드 또는 소분류 보기명이 비어있는 항목이 있습니다.", 0);
            }

            // 2. 텍스트 정제 및 lv321code 유니크 식별자 보장
            var usedLv321 = new HashSet<string>();
            foreach (var token in codesArray)
            {
                if (token is not JObject row) continue;
                string existingKey = row["lv321code"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(existingKey)) usedLv321.Add(existingKey);
            }

            foreach (var token in codesArray)
            {
                if (token is not JObject row) continue;
                row["lv1"] = CleanText(row["lv1"]?.ToString());
                row["lv2"] = CleanText(row["lv2"]?.ToString());
                row["lv3"] = CleanText(row["lv3"]?.ToString());

                string existingKey = row["lv321code"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(existingKey))
                {
                    string newKey;
                    do
                    {
                        newKey = Guid.NewGuid().ToString("N").Substring(0, 10);
                    } while (!usedLv321.Add(newKey));

                    row["lv321code"] = newKey;
                }

                // ── [신규] 보기구분 및 수정여부 분리 관리 & ex_gubun 이원화 동기화 ──
                string gubunName = Str(row, "gubun_name") ?? "";
                string isEditableStr = Str(row, "is_editable") ?? "";
                bool? isLockObj = row["is_lock"]?.Value<bool>();

                // 1) gubun_name 기본값 보정
                if (string.IsNullOrWhiteSpace(gubunName))
                {
                    string oldGubun = Str(row, "ex_gubun") ?? "";
                    gubunName = (oldGubun == "설문(수정불가)" || oldGubun == "survey") ? "설문 보기" : "자동분석 보기";
                }
                row["gubun_name"] = gubunName;

                // 2) is_editable / is_lock 판정: 사용자가 UI에서 명시적으로 변경한 is_editable ("불가" / "가능") 최우선 반영
                bool isLock;
                if (isEditableStr == "불가") isLock = true;
                else if (isEditableStr == "가능") isLock = false;
                else if (isLockObj.HasValue) isLock = isLockObj.Value;
                else isLock = (gubunName == "설문 보기" || gubunName == "트래킹 보기"); // 신규 키 없는 경우 기본값

                row["is_lock"] = isLock;
                row["is_editable"] = isLock ? "불가" : "가능";

                // 3) ex_gubun 이원화 동기화: Python 워커를 위해 오직 "설문(수정불가)" or "분석(수정가능)"
                row["ex_gubun"] = isLock ? "설문(수정불가)" : "분석(수정가능)";
            }

            string newResultJson = codesArray.ToString(Newtonsoft.Json.Formatting.None);

            // 3. 이전 보기와 비교하여 응답자 데이터(pn_in)에 변경 사항 자동 전파 (ApplyChangesToAnswer)
            var lbBefore = await _repo.GetPnLbAsync(pn, qnum, ct);
            string beforeJson = lbBefore?.result_json ?? "";

            if (!string.IsNullOrWhiteSpace(beforeJson) && beforeJson != "[]")
            {
                var inRow = await _repo.GetPnInAsync(pn, qnum, ct);
                string currentAnswerJson = inRow?.answer_fin ?? "";

                if (!string.IsNullOrWhiteSpace(currentAnswerJson) && currentAnswerJson != "[]")
                {
                    var (updatedAnswerJson, hasChanged) = ApplyChangesToAnswer(currentAnswerJson, beforeJson, newResultJson);
                    if (hasChanged)
                    {
                        _logger.LogInformation("[SaveCodesAsync] 보기 변경으로 인한 응답자 데이터 자동 치환 전파 시작 pn={Pn} qnum={Qnum}", pn, qnum);
                        await _repo.SavePnInAsync(pn, qnum, updatedAnswerJson, ct);

                        // 응답자가 변경되었으므로 하위 문항 배분 및 HData/큐 동기화 수행
                        try
                        {
                            var updatedArr = JArray.Parse(updatedAnswerJson);
                            await SaveResponsesAsync(pn, qnum, "n", updatedArr, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[SaveCodesAsync] 응답 전파 후 HData/큐 동기화 실패 pn={Pn} qnum={Qnum}", pn, qnum);
                        }
                    }
                }
            }

            // 4. pn_lb 저장 (이전 보기는 result_json_before에 보존)
            int affected = await _repo.SavePnLbAsync(pn, qnum, newResultJson, ct);

            // 5. HData surveyon_labels 테이블에 대/중/소분류 일괄 동기화 (사용자 추가 컬럼: lv1code, lv1, lv2code, lv2, code, label)
            try
            {
                await _repo.SyncCodesToHDataAsync(pn, qnum, codesArray, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveCodesAsync] HData surveyon_labels 동기화 중 오류 pn={Pn} qnum={Qnum}", pn, qnum);
            }

            return (affected > 0, "777", "정상 저장되었습니다.", codesArray.Count);
        }

        public async Task<(bool success, string code, string message, int savedCount)> SaveResponsesAsync(
            string pn, string qnum, string duplicateAnswer, JArray responsesArray, CancellationToken ct = default)
        {
            // 1. 유효성 검사: codes (lb)에 정의된 유효 코드 목록 수집
            var lbRow = await _repo.GetPnLbAsync(pn, qnum, ct);
            var validCodes = new HashSet<string>();
            if (lbRow != null && !string.IsNullOrWhiteSpace(lbRow.result_json))
            {
                try
                {
                    var lbArr = JArray.Parse(lbRow.result_json);
                    foreach (var c in lbArr)
                    {
                        string code = c["lv123code"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(code)) validCodes.Add(code);
                    }
                }
                catch { }
            }

            // lb에 없는 코드가 지정되었는지 검사 (미분류 빈 값은 허용)
            var invalidCodes = new List<string>();
            foreach (var r in responsesArray)
            {
                string code = r["lv123code"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(code) && validCodes.Count > 0 && !validCodes.Contains(code))
                {
                    invalidCodes.Add(code);
                }
            }
            if (invalidCodes.Count > 0)
            {
                // 레거시 763 오류
                return (false, "763", $"보기 목록에 존재하지 않는 코드({string.Join(", ", invalidCodes.Distinct())})가 지정되어 있습니다.", 0);
            }

            // 2. DK(모름/무응답) 코드 확인 및 특수코드 판별 로직
            string dkCode = await _repo.GetDkCodeAsync(pn, qnum, ct);

            bool IsDkOrNone(string code, string label)
            {
                if (code == "9999" || code == "9998") return true;
                if (!string.IsNullOrEmpty(dkCode) && code == dkCode) return true;
                if (!string.IsNullOrEmpty(label))
                {
                    if (label.Contains("모름") || label.Contains("무응답") || label.Contains("없음") || label.Contains("없다"))
                        return true;
                }
                return false;
            }

            bool IsEtc(string code, string label)
            {
                if (code == "9997") return true;
                if (!string.IsNullOrEmpty(label) && label.Contains("기타")) return true;
                return false;
            }

            // 3. 중복코드 제거 및 결측 정제 규칙 적용
            // [모드 A (체크됨)]: 전체 중복코드 제거 (일반코드 중복 제거 + 특수코드 정제)
            // [모드 B (미체크)]: 기본 결측 정제 (특수코드 9997/9998/9999 중복 시 1개만, 일반코드+모름/없음 공존 시 9999/9998 삭제, 일반코드 중복은 보존)
            bool isDuplicateRemove = string.Equals(duplicateAnswer, "y", StringComparison.OrdinalIgnoreCase);
            JArray finArray = new JArray();

            var pidGroups = new Dictionary<string, List<JObject>>();
            foreach (JObject item in responsesArray)
            {
                string pid = item["pid"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(pid))
                    pid = item["fixed_key"]?.ToString()?.Trim() ?? Guid.NewGuid().ToString("N");

                if (!pidGroups.ContainsKey(pid)) pidGroups[pid] = new List<JObject>();
                pidGroups[pid].Add(item);
            }

            foreach (var (_, items) in pidGroups)
            {
                // 응답자(PID) 내 non-DK, non-None, non-ETC 유효 일반코드가 하나라도 있는지 확인
                bool hasGeneralCode = items.Any(x =>
                {
                    string c = x["lv123code"]?.ToString()?.Trim() ?? "";
                    string l = x["lv3"]?.ToString()?.Trim() ?? "";
                    return !string.IsNullOrEmpty(c) && !IsDkOrNone(c, l) && !IsEtc(c, l);
                });

                var filteredItems = new List<JObject>();
                var seenGeneralCodes = new HashSet<string>();
                var seenSpecialCodes = new HashSet<string>();

                foreach (var item in items)
                {
                    string c = item["lv123code"]?.ToString()?.Trim() ?? "";
                    string l = item["lv3"]?.ToString()?.Trim() ?? "";

                    // 코드가 없는 미분류 행은 보존
                    if (string.IsNullOrEmpty(c))
                    {
                        filteredItems.Add(item);
                        continue;
                    }

                    bool isDkNone = IsDkOrNone(c, l);
                    bool isEtc = IsEtc(c, l);

                    // [기준 2]: 한 명의 응답자 기준 일반코드와 모름/무응답(9999) 또는 없음/없다(9998) 공존 시
                    // 일반코드만 유지하고 9999, 9998 코드는 삭제
                    if (hasGeneralCode && isDkNone)
                    {
                        continue;
                    }

                    // [기준 1]: 특수코드(9997 기타, 9999 모름/무응답, 9998 없음/없다)끼리의 중복은
                    // 일반코드가 없더라도 중복 제거하고 한 개만 유지
                    if (isDkNone || isEtc)
                    {
                        if (seenSpecialCodes.Contains(c)) continue;
                        seenSpecialCodes.Add(c);
                        filteredItems.Add(item);
                        continue;
                    }

                    // 일반 유효코드 처리:
                    if (isDuplicateRemove)
                    {
                        // [모드 A (체크됨)]: 일반코드도 한 명의 응답자 기준 중복 완전 제거
                        if (seenGeneralCodes.Contains(c)) continue;
                        seenGeneralCodes.Add(c);
                        filteredItems.Add(item);
                    }
                    else
                    {
                        // [모드 B (미체크)]: 일반코드는 원본 중복(예: [12, 12]) 그대로 보존
                        filteredItems.Add(item);
                    }
                }

                // [필수]: 행 삭제/정제 후 cid 1부터 순차 재부여 (Re-indexing)
                for (int i = 0; i < filteredItems.Count; i++)
                {
                    filteredItems[i]["cid"] = (i + 1).ToString();
                    finArray.Add(filteredItems[i]);
                }
            }

            // 4. pn_in 저장
            string answerFin = finArray.ToString(Newtonsoft.Json.Formatting.None);
            await _repo.SavePnInAsync(pn, qnum, answerFin, ct);

            // 5. duplicate_remove_date 갱신
            int? qid = await ResolveQidAsync(pn, qnum, ct);
            if (qid.HasValue)
            {
                string? dateStr = isDuplicateRemove ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : null;
                await _repo.UpdateDuplicateRemoveDateAsync(qid.Value, dateStr, ct);
            }

            // 6. MSMQ 큐 전송 (QueueSending / SendingQueus) 및 HData(surveyon_answer) 실제 PID 기준 동기화
            int sentQueueCount = 0;
            int syncedHDataCount = 0;
            try
            {
                // 6-1. pn_in.answer_origin에서 cleanAnswer -> fixed_key 맵 생성
                var keyToFixedKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? pninAnswerOriginStr = await _repo.GetPnInAnswerOriginAsync(pn, qnum, ct);
                if (!string.IsNullOrWhiteSpace(pninAnswerOriginStr))
                {
                    try
                    {
                        var pninObj = JToken.Parse(pninAnswerOriginStr);
                        JArray? originItems = null;
                        if (pninObj is JArray oArr) originItems = oArr;
                        else if (pninObj is JObject oObj)
                        {
                            originItems = (oObj[qnum] as JArray) ?? (oObj.Properties().FirstOrDefault()?.Value as JArray);
                        }

                        if (originItems != null)
                        {
                            foreach (var it in originItems.OfType<JObject>())
                            {
                                string ans = it["answer"]?.ToString() ?? it["answer_origin"]?.ToString() ?? "";
                                string fk = it["fixed_key"]?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(fk))
                                {
                                    string clean = Regex.Replace(ans, @"^\[[^\]]*\]_", "");
                                    keyToFixedKey[clean] = fk;
                                    keyToFixedKey[ans] = fk;
                                }
                            }
                        }
                    }
                    catch (Exception exOrigin)
                    {
                        _logger.LogWarning(exOrigin, "[SaveResponsesAsync] pn_in.answer_origin 파싱 경고 pn={Pn} qnum={Qnum}", pn, qnum);
                    }
                }

                // 6-2. 정제 완료된 finArray에서 fixed_key 및 cid별 코드/감성/재검토/날짜 딕셔너리 구축
                var keylv123code = new Dictionary<string, Dictionary<string, string>>();
                var keysentiment = new Dictionary<string, Dictionary<string, string>>();
                var keyrecheckyn = new Dictionary<string, Dictionary<string, string>>();
                var keyupdatedate = new Dictionary<string, Dictionary<string, string>>();

                foreach (var row in finArray.OfType<JObject>())
                {
                    string fk = row["fixed_key"]?.ToString()?.Trim() ?? "";
                    string cid = row["cid"]?.ToString()?.Trim() ?? "1";
                    string code = row["lv123code"]?.ToString()?.Trim() ?? "";
                    string sentiment = row["sentiment"]?.ToString()?.Trim() ?? "";
                    string recheckyn = row["recheckyn"]?.ToString()?.Trim() ?? "0";
                    string updateDate = row["updateDate"]?.ToString()?.Trim() 
                                        ?? row["update_date"]?.ToString()?.Trim() 
                                        ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    if (string.IsNullOrEmpty(fk)) continue;

                    if (!keylv123code.ContainsKey(fk)) keylv123code[fk] = new Dictionary<string, string>();
                    if (!keysentiment.ContainsKey(fk)) keysentiment[fk] = new Dictionary<string, string>();
                    if (!keyrecheckyn.ContainsKey(fk)) keyrecheckyn[fk] = new Dictionary<string, string>();
                    if (!keyupdatedate.ContainsKey(fk)) keyupdatedate[fk] = new Dictionary<string, string>();

                    keylv123code[fk][cid] = code;
                    keysentiment[fk][cid] = sentiment;
                    keyrecheckyn[fk][cid] = recheckyn;
                    keyupdatedate[fk][cid] = updateDate;
                }

                // 6-3. project_list에서 대상 문항 원본 데이터 조회 (레거시 select_project_list gb=11 / gb=213)
                var originRows = await _repo.GetProjectOriginRowsAsync(pn, qnum, isDuplicateRemove, ct);
                var queueList = new List<QueueDto>();
                var pidSubQnumMap = new Dictionary<string, Dictionary<string, (string code, string sentiment)>>();

                var subQnums = originRows
                    .Select(r => r.qnum?.Trim() ?? "")
                    .Where(q => !string.IsNullOrEmpty(q))
                    .Distinct()
                    .ToList();
                if (subQnums.Count == 0) subQnums.Add(qnum);

                string mergeQnum = (subQnums.Count > 1 || !string.Equals(subQnums[0], qnum, StringComparison.OrdinalIgnoreCase)) ? qnum : "";

                if (!isDuplicateRemove)
                {
                    // ── [CASE A: 일반 저장 (레거시 SendingQueus)] ──
                    foreach (var originRow in originRows)
                    {
                        string rowOriginJson = originRow.answer_origin?.Trim() ?? "";
                        if (string.IsNullOrEmpty(rowOriginJson)) continue;

                        string subQnum = originRow.qnum?.Trim() ?? qnum;
                        string subPn = originRow.projectnum?.Trim() ?? pn;
                        string servername = originRow.servername?.Trim() ?? "";

                        JArray? items = null;
                        try
                        {
                            var token = JToken.Parse(rowOriginJson);
                            if (token is JArray arr) items = arr;
                            else if (token is JObject obj)
                            {
                                items = (obj[subQnum] as JArray) 
                                     ?? (obj[originRow.qnum_text] as JArray) 
                                     ?? (obj.Properties().FirstOrDefault()?.Value as JArray);
                            }
                        }
                        catch { }

                        if (items == null) continue;

                        foreach (var item in items.OfType<JObject>())
                        {
                            string rowAnswerFin = item["answer_fin"]?.ToString() ?? item["answer_origin"]?.ToString() ?? item["answer"]?.ToString() ?? "";
                            string pid = item["pid"]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrEmpty(pid)) continue;

                            string cleanAnswer = Regex.Replace(rowAnswerFin, @"^\[[^\]]*\]_", "");
                            string fixedKey = "";
                            if (keyToFixedKey.TryGetValue(cleanAnswer, out var fk1)) fixedKey = fk1;
                            else if (keyToFixedKey.TryGetValue(rowAnswerFin, out var fk2)) fixedKey = fk2;

                            if (string.IsNullOrEmpty(fixedKey) || !keylv123code.ContainsKey(fixedKey)) continue;

                            var codesByCid = keylv123code[fixedKey];
                            var addedCodes = new HashSet<string>();
                            var codeList = new List<string>();
                            var sentList = new List<string>();
                            string recheck = keyrecheckyn.TryGetValue(fixedKey, out var rcMap) && rcMap.TryGetValue("1", out var r1) ? r1 : "0";
                            string updDate = keyupdatedate.TryGetValue(fixedKey, out var udMap) && udMap.TryGetValue("1", out var u1) ? u1 : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                            for (int cid = 1; cid <= codesByCid.Count; cid++)
                            {
                                string sid = cid.ToString();
                                if (codesByCid.TryGetValue(sid, out var c) && !string.IsNullOrEmpty(c))
                                {
                                    if (!addedCodes.Contains(c))
                                    {
                                        addedCodes.Add(c);
                                        codeList.Add(c);
                                        string s = keysentiment.TryGetValue(fixedKey, out var sMap) && sMap.TryGetValue(sid, out var sVal) ? sVal : "";
                                        sentList.Add(s);
                                    }
                                }
                            }

                            if (codeList.Count > 0)
                            {
                                string combinedCodes = string.Join(",", codeList);
                                string combinedSents = string.Join(",", sentList);

                                queueList.Add(new QueueDto
                                {
                                    servername = servername,
                                    pn = subPn,
                                    qnum = subQnum,
                                    pid = pid,
                                    lv123code = combinedCodes,
                                    recheckyn = recheck,
                                    updateDate = updDate,
                                    deleteyn = "0"
                                });

                                if (!pidSubQnumMap.TryGetValue(pid, out var qMap))
                                {
                                    qMap = new Dictionary<string, (string code, string sentiment)>();
                                    pidSubQnumMap[pid] = qMap;
                                }
                                qMap[subQnum] = (combinedCodes, combinedSents);
                            }
                        }
                    }
                }
                else
                {
                    // ── [CASE B: 중복코드제거 저장 (레거시 SendingQueus_duplicate)] ──
                    var duplicatePnMapFin = new Dictionary<string, Dictionary<string, List<(string lv123code, string sentiment, string recheckyn, string updateDate)>>>();
                    var mergePnQnum = new Dictionary<string, List<string>>();
                    string defaultServername = "";

                    foreach (var originRow in originRows)
                    {
                        string rowOriginJson = originRow.answer_origin?.Trim() ?? "";
                        string subQnum = originRow.qnum?.Trim() ?? qnum;
                        string subPn = originRow.projectnum?.Trim() ?? pn;
                        if (string.IsNullOrEmpty(defaultServername)) defaultServername = originRow.servername?.Trim() ?? "";

                        if (!mergePnQnum.ContainsKey(subPn)) mergePnQnum[subPn] = new List<string>();
                        if (!mergePnQnum[subPn].Contains(subQnum)) mergePnQnum[subPn].Add(subQnum);

                        if (string.IsNullOrEmpty(rowOriginJson)) continue;

                        JArray? items = null;
                        try
                        {
                            var token = JToken.Parse(rowOriginJson);
                            if (token is JArray arr) items = arr;
                            else if (token is JObject obj)
                            {
                                items = (obj[subQnum] as JArray) 
                                     ?? (obj[originRow.qnum_text] as JArray) 
                                     ?? (obj.Properties().FirstOrDefault()?.Value as JArray);
                            }
                        }
                        catch { }

                        if (items == null) continue;

                        foreach (var item in items.OfType<JObject>())
                        {
                            string rowAnswerFin = item["answer_fin"]?.ToString() ?? item["answer_origin"]?.ToString() ?? item["answer"]?.ToString() ?? "";
                            string pid = item["pid"]?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrEmpty(pid)) continue;

                            string cleanAnswer = Regex.Replace(rowAnswerFin, @"^\[[^\]]*\]_", "");
                            string fixedKey = "";
                            if (keyToFixedKey.TryGetValue(cleanAnswer, out var fk1)) fixedKey = fk1;
                            else if (keyToFixedKey.TryGetValue(rowAnswerFin, out var fk2)) fixedKey = fk2;

                            if (string.IsNullOrEmpty(fixedKey) || !keylv123code.ContainsKey(fixedKey)) continue;

                            var codesByCid = keylv123code[fixedKey];
                            string recheck = keyrecheckyn.TryGetValue(fixedKey, out var rcMap) && rcMap.TryGetValue("1", out var r1) ? r1 : "0";
                            string updDate = keyupdatedate.TryGetValue(fixedKey, out var udMap) && udMap.TryGetValue("1", out var u1) ? u1 : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                            if (!duplicatePnMapFin.ContainsKey(subPn))
                                duplicatePnMapFin[subPn] = new Dictionary<string, List<(string, string, string, string)>>();
                            if (!duplicatePnMapFin[subPn].ContainsKey(pid))
                                duplicatePnMapFin[subPn][pid] = new List<(string, string, string, string)>();

                            var pidList = duplicatePnMapFin[subPn][pid];

                            for (int cid = 1; cid <= codesByCid.Count; cid++)
                            {
                                string sid = cid.ToString();
                                if (codesByCid.TryGetValue(sid, out var c) && !string.IsNullOrEmpty(c))
                                {
                                    string s = keysentiment.TryGetValue(fixedKey, out var sMap) && sMap.TryGetValue(sid, out var sVal) ? sVal : "";

                                    // 중복 코드 검사
                                    if (pidList.Any(x => x.lv123code == c)) continue;

                                    // DK(모름) 배제 규칙: 다른 유효 코드가 이미 있는데 현재 코드가 DK이면 제외
                                    bool isCurrentDk = (c == dkCode);
                                    bool hasOtherNonDk = pidList.Any(x => x.lv123code != dkCode);
                                    if (isCurrentDk && hasOtherNonDk) continue;

                                    pidList.Add((c, s, recheck, updDate));
                                }
                            }
                        }
                    }

                    // 하위 문항(qnumList) 순차 배분 및 QueueDto 생성
                    foreach (var (subPn, qnumList) in mergePnQnum)
                    {
                        if (!duplicatePnMapFin.TryGetValue(subPn, out var pidDict)) continue;

                        foreach (var (pid, list) in pidDict)
                        {
                            for (int i = 0; i < qnumList.Count; i++)
                            {
                                string targetQnum = qnumList[i];
                                string codeVal = (i < list.Count && !string.IsNullOrEmpty(list[i].lv123code)) ? list[i].lv123code : "";
                                string sentVal = (i < list.Count && !string.IsNullOrEmpty(list[i].sentiment)) ? list[i].sentiment : "";
                                string rcVal = (i < list.Count && !string.IsNullOrEmpty(list[i].recheckyn)) ? list[i].recheckyn : "0";
                                string udVal = (i < list.Count && !string.IsNullOrEmpty(list[i].updateDate)) ? list[i].updateDate : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                string deleteyn = string.IsNullOrEmpty(codeVal) ? "1" : "0";

                                queueList.Add(new QueueDto
                                {
                                    servername = defaultServername,
                                    pn = subPn,
                                    qnum = targetQnum,
                                    pid = pid,
                                    lv123code = codeVal,
                                    recheckyn = rcVal,
                                    updateDate = udVal,
                                    deleteyn = deleteyn
                                });

                                if (!pidSubQnumMap.TryGetValue(pid, out var qMap))
                                {
                                    qMap = new Dictionary<string, (string code, string sentiment)>();
                                    pidSubQnumMap[pid] = qMap;
                                }
                                qMap[targetQnum] = (codeVal, sentVal);
                            }
                        }
                    }
                }

                // 6-4. MSMQ 배치 전송 실행
                if (queueList.Count > 0)
                {
                    sentQueueCount = _queueService.SendQueueBatch(queueList);
                }

                // 6-5. HData surveyon_answer 실제 PID 기준 업데이트 및 surveyon_variables 플래그 갱신
                if (pidSubQnumMap.Count > 0)
                {
                    syncedHDataCount = await _repo.SyncHDataAnswerAndVariablesAsync(pn, mergeQnum, subQnums, pidSubQnumMap, ct);
                }

                _logger.LogInformation("[SaveResponsesAsync] 저장 완료 pn={Pn} qnum={Qnum} count={Count} queueCount={QueueCount} hdataCount={HdataCount}",
                    pn, qnum, finArray.Count, sentQueueCount, syncedHDataCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveResponsesAsync] MSMQ 큐 전송 또는 HData 동기화 파이프라인 처리 중 오류 pn={Pn} qnum={Qnum}", pn, qnum);
            }

            return (true, "777", $"정상 저장되었습니다. (큐 {sentQueueCount}건, HData {syncedHDataCount}건 동기화)", finArray.Count);
        }

        public async Task<object> CompareResponsesAsync(string pn, string qnum, JArray currentResponses, CancellationToken ct = default)
        {
            var inRow = await _repo.GetPnInAsync(pn, qnum, ct);
            var dbMap = new Dictionary<string, string>();
            if (inRow != null && !string.IsNullOrWhiteSpace(inRow.answer_fin))
            {
                try
                {
                    var arr = JArray.Parse(inRow.answer_fin);
                    foreach (var item in arr)
                    {
                        string key = (item["id"]?.ToString() ?? item["pid"]?.ToString() ?? item["fixed_key"]?.ToString()) ?? "";
                        string cid = item["cid"]?.ToString() ?? "1";
                        string code = item["lv123code"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(key))
                        {
                            dbMap[$"{key}__c{cid}"] = code;
                            if (!dbMap.ContainsKey(key)) dbMap[key] = code;
                        }
                    }
                }
                catch { }
            }

            int total = currentResponses.Count;
            int coded = 0;
            int uncoded = 0;
            int neu = 0;
            int chg = 0;
            int same = 0;

            foreach (var r in currentResponses)
            {
                string key = (r["id"]?.ToString() ?? r["pid"]?.ToString() ?? r["fixed_key"]?.ToString()) ?? "";
                string cid = r["cid"]?.ToString() ?? "1";
                string code = r["lv123code"]?.ToString() ?? "";
                bool hasCode = !string.IsNullOrEmpty(code);
                if (hasCode) coded++; else uncoded++;

                string mapKey = $"{key}__c{cid}";
                if (dbMap.TryGetValue(mapKey, out var dbCode) || dbMap.TryGetValue(key, out dbCode))
                {
                    if (code == dbCode) same++;
                    else chg++;
                }
                else
                {
                    neu++;
                }
            }

            int dbNew = currentResponses.Count(r =>
            {
                string key = (r["id"]?.ToString() ?? r["pid"]?.ToString() ?? r["fixed_key"]?.ToString()) ?? "";
                string cid = r["cid"]?.ToString() ?? "1";
                return !dbMap.ContainsKey($"{key}__c{cid}") && !dbMap.ContainsKey(key);
            });

            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string lastDate = meta?.duplicate_remove_date ?? inRow?.update_date ?? "";
            return new
            {
                total,
                coded,
                unc = uncoded,       // 프론트엔드 호환 필드
                uncoded,             // 원본 필드 유지
                dbCoded = dbMap.Count,
                neu,
                chg,
                same,
                dbNew,
                pending = uncoded + neu + chg,
                lastSavedDate = lastDate, // 프론트엔드 호환 필드
                lastSaveDate = lastDate
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // Phase D: Aspose.Cells 기반 엑셀 다운로드 / 업로드 미리보기 / 보기 불러오기
        // ══════════════════════════════════════════════════════════════════

        private static void EnsureAsposeLicense()
        {
            try
            {
                string licPath = Path.Combine(AppContext.BaseDirectory, "License", "Aspose.Total.lic");
                if (!File.Exists(licPath)) licPath = Path.Combine(Directory.GetCurrentDirectory(), "License", "Aspose.Total.lic");
                if (File.Exists(licPath))
                {
                    new Aspose.Cells.License().SetLicense(licPath);
                }
            }
            catch { }
        }

        public async Task<byte[]> ExportExcelAsync(string pn, string qnum, CancellationToken ct = default)
        {
            EnsureAsposeLicense();

            var codes = await BuildCodesAsync(pn, qnum, ct);
            var responses = await BuildResponsesAsync(pn, qnum, ct);

            var wb = new Workbook();

            // 1. Sheet 1: 보기목록
            var sheetCodes = wb.Worksheets[0];
            sheetCodes.Name = "보기목록";

            string[] codeHeaders = new[] { "대분류코드", "대분류명", "중분류코드", "중분류명", "소분류코드", "보기명(소분류)", "빈도수", "보기유형", "잠금여부" };
            var headerStyle1 = wb.CreateStyle();
            headerStyle1.Pattern = BackgroundType.Solid;
            headerStyle1.ForegroundColor = Color.FromArgb(242, 101, 34); // Brand #F26522
            headerStyle1.Font.IsBold = true;
            headerStyle1.Font.Color = Color.White;
            headerStyle1.HorizontalAlignment = TextAlignmentType.Center;
            headerStyle1.VerticalAlignment = TextAlignmentType.Center;

            for (int c = 0; c < codeHeaders.Length; c++)
            {
                var cell = sheetCodes.Cells[0, c];
                cell.Value = codeHeaders[c];
                cell.SetStyle(headerStyle1);
            }
            sheetCodes.Cells.SetRowHeight(0, 24);

            int rIdx = 1;
            foreach (var item in codes)
            {
                sheetCodes.Cells[rIdx, 0].Value = item["lv1code"]?.ToString() ?? "";
                sheetCodes.Cells[rIdx, 1].Value = item["lv1"]?.ToString() ?? "";
                sheetCodes.Cells[rIdx, 2].Value = item["lv2code"]?.ToString() ?? "";
                sheetCodes.Cells[rIdx, 3].Value = item["lv2"]?.ToString() ?? "";
                sheetCodes.Cells[rIdx, 4].Value = item["lv123code"]?.ToString() ?? "";
                sheetCodes.Cells[rIdx, 5].Value = item["lv3"]?.ToString() ?? "";
                
                int exSum = 0;
                int.TryParse(item["ex_sum"]?.ToString() ?? "0", out exSum);
                sheetCodes.Cells[rIdx, 6].Value = exSum;

                sheetCodes.Cells[rIdx, 7].Value = item["ex_gubun"]?.ToString() ?? "자동분석";
                sheetCodes.Cells[rIdx, 8].Value = (item["is_lock"]?.ToString() == "True" || item["is_lock"]?.ToString() == "1") ? "수정불가" : "수정가능";
                rIdx++;
            }
            sheetCodes.AutoFitColumns();
            sheetCodes.FreezePanes(1, 0, 1, 0);

            // 2. Sheet 2: 응답데이터
            var sheetResp = wb.Worksheets.Add("응답데이터");
            string[] respHeaders = new[] { "NO", "CID", "원본응답", "클리닝응답", "소분류코드", "보기명", "대분류", "중분류", "확인여부", "감성" };
            var headerStyle2 = wb.CreateStyle();
            headerStyle2.Pattern = BackgroundType.Solid;
            headerStyle2.ForegroundColor = Color.FromArgb(47, 53, 66); // Dark #2F3542
            headerStyle2.Font.IsBold = true;
            headerStyle2.Font.Color = Color.White;
            headerStyle2.HorizontalAlignment = TextAlignmentType.Center;
            headerStyle2.VerticalAlignment = TextAlignmentType.Center;

            for (int c = 0; c < respHeaders.Length; c++)
            {
                var cell = sheetResp.Cells[0, c];
                cell.Value = respHeaders[c];
                cell.SetStyle(headerStyle2);
            }
            sheetResp.Cells.SetRowHeight(0, 24);

            rIdx = 1;
            foreach (var item in responses)
            {
                sheetResp.Cells[rIdx, 0].Value = item["id"]?.ToString() ?? item["rownum"]?.ToString() ?? rIdx.ToString();
                sheetResp.Cells[rIdx, 1].Value = item["cid"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 2].Value = item["answer_origin"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 3].Value = item["answer_fin"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 4].Value = item["lv123code"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 5].Value = item["lv3"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 6].Value = item["lv1"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 7].Value = item["lv2"]?.ToString() ?? "";
                sheetResp.Cells[rIdx, 8].Value = item["recheckyn"]?.ToString() == "1" ? "확인" : "미확인";
                sheetResp.Cells[rIdx, 9].Value = item["sentiment"]?.ToString() ?? "";
                rIdx++;
            }
            sheetResp.AutoFitColumns();
            sheetResp.FreezePanes(1, 0, 1, 0);

            using var ms = new MemoryStream();
            wb.Save(ms, SaveFormat.Xlsx);
            return ms.ToArray();
        }

        public async Task<ExcelCodePreviewResult> PreviewUploadedExcelCodesAsync(IFormFile file, string pn, string qnum, CancellationToken ct = default)
        {
            EnsureAsposeLicense();

            var result = new ExcelCodePreviewResult { ok = true };
            if (file == null || file.Length == 0)
            {
                result.ok = false;
                return result;
            }

            // 기존 DB 보기 목록 로드
            var existingCodes = await BuildCodesAsync(pn, qnum, ct);
            var existMap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var ec in existingCodes.OfType<JObject>())
            {
                string code = ec["lv123code"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(code) && !existMap.ContainsKey(code))
                {
                    existMap[code] = ec;
                }
            }

            using var stream = file.OpenReadStream();
            var wb = new Workbook(stream);
            var sheet = wb.Worksheets["보기목록"] ?? wb.Worksheets[0];

            int maxCol = sheet.Cells.MaxDataColumn;
            int maxRow = sheet.Cells.MaxDataRow;
            if (maxRow < 1)
            {
                result.ok = false;
                return result;
            }

            // 헤더 행 탐색 (Row 0 또는 Row 1)
            int headerRow = 0;
            int lv3Col = -1, codeCol = -1, lv1Col = -1, lv1codeCol = -1, lv2Col = -1, lv2codeCol = -1, typeCol = -1, lockCol = -1;

            for (int r = 0; r <= Math.Min(2, maxRow); r++)
            {
                for (int c = 0; c <= maxCol; c++)
                {
                    string h = sheet.Cells[r, c].StringValue?.Trim()?.ToLower() ?? "";
                    if (h.Contains("소분류명") || h.Contains("보기명") || h == "lv3" || h == "라벨" || h == "label") lv3Col = c;
                    else if (h.Contains("소분류코드") || h == "lv123code" || h == "코드" || h == "code") codeCol = c;
                    else if (h.Contains("대분류명") || h == "lv1" || (h.Contains("대분류") && !h.Contains("코드"))) lv1Col = c;
                    else if (h.Contains("대분류코드") || h == "lv1code") lv1codeCol = c;
                    else if (h.Contains("중분류명") || h == "lv2" || (h.Contains("중분류") && !h.Contains("코드"))) lv2Col = c;
                    else if (h.Contains("중분류코드") || h == "lv2code") lv2codeCol = c;
                    else if (h.Contains("유형") || h.Contains("보기유형") || h == "ex_gubun") typeCol = c;
                    else if (h.Contains("잠금") || h.Contains("수정여부") || h == "is_lock") lockCol = c;
                }
                if (lv3Col >= 0 || codeCol >= 0)
                {
                    headerRow = r;
                    break;
                }
            }

            // 만약 헤더를 못 찾은 경우 기본 위치
            if (codeCol < 0) codeCol = 4;
            if (lv3Col < 0) lv3Col = 5;
            if (lv1Col < 0) lv1Col = 1;
            if (lv2Col < 0) lv2Col = 3;

            for (int r = headerRow + 1; r <= maxRow; r++)
            {
                string code = (codeCol >= 0 ? sheet.Cells[r, codeCol].StringValue : "")?.Trim() ?? "";
                string lv3 = (lv3Col >= 0 ? sheet.Cells[r, lv3Col].StringValue : "")?.Trim() ?? "";
                string lv1 = (lv1Col >= 0 ? sheet.Cells[r, lv1Col].StringValue : "")?.Trim() ?? "";
                string lv1code = (lv1codeCol >= 0 ? sheet.Cells[r, lv1codeCol].StringValue : "")?.Trim() ?? "";
                string lv2 = (lv2Col >= 0 ? sheet.Cells[r, lv2Col].StringValue : "")?.Trim() ?? "";
                string lv2code = (lv2codeCol >= 0 ? sheet.Cells[r, lv2codeCol].StringValue : "")?.Trim() ?? "";
                string ex_gubun = (typeCol >= 0 ? sheet.Cells[r, typeCol].StringValue : "")?.Trim() ?? "자동분석";
                string lockStr = (lockCol >= 0 ? sheet.Cells[r, lockCol].StringValue : "")?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(lv3))
                    continue; // 빈 행 스킵

                bool is_lock = lockStr.Contains("불가") || lockStr == "1" || lockStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                var item = new ExcelCodePreviewItem
                {
                    rowNum = r,
                    lv1 = lv1,
                    lv1code = lv1code,
                    lv2 = lv2,
                    lv2code = lv2code,
                    lv3 = lv3,
                    lv123code = code,
                    ex_gubun = string.IsNullOrEmpty(ex_gubun) ? "자동분석" : ex_gubun,
                    is_lock = is_lock
                };

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(lv3))
                {
                    item.status = "오류";
                    item.message = string.IsNullOrEmpty(code) ? "코드 누락" : "소분류명 누락";
                    result.errorCount++;
                }
                else if (existMap.TryGetValue(code, out var existing))
                {
                    string oldLv3 = existing["lv3"]?.ToString()?.Trim() ?? "";
                    string oldLv1 = existing["lv1"]?.ToString()?.Trim() ?? "";
                    string oldLv2 = existing["lv2"]?.ToString()?.Trim() ?? "";

                    if (oldLv3 == lv3 && oldLv1 == lv1 && oldLv2 == lv2)
                    {
                        item.status = "유지";
                        item.message = "기존 항목과 일치";
                        result.keepCount++;
                    }
                    else
                    {
                        item.status = "수정";
                        item.message = $"기존: [{oldLv1}>{oldLv2}>{oldLv3}]";
                        result.updateCount++;
                    }
                }
                else
                {
                    item.status = "신규";
                    item.message = "새로운 코드";
                    result.newCount++;
                }

                result.items.Add(item);
                result.totalCount++;
            }

            return result;
        }

        public Task<List<AnalysisImportProjectItem>> SearchImportProjectsAsync(string search, CancellationToken ct = default)
        {
            return _repo.SearchProjectsForImportAsync(search, ct);
        }

        public async Task<JArray> GetImportCodesAsync(string targetPn, string targetQnum, CancellationToken ct = default)
        {
            var lb = await _repo.GetPnLbAsync(targetPn, targetQnum, ct);
            if (lb == null || string.IsNullOrWhiteSpace(lb.result_json))
                return new JArray();

            try
            {
                return JArray.Parse(lb.result_json);
            }
            catch
            {
                return new JArray();
            }
        }

        public async Task<(bool ok, int addedCount, int duplicateCount)> ApplyImportCodesAsync(string pn, string qnum, JArray selectedCodes, CancellationToken ct = default)
        {
            if (selectedCodes == null || selectedCodes.Count == 0)
                return (false, 0, 0);

            var currentCodes = await BuildCodesAsync(pn, qnum, ct);
            var existCodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in currentCodes.OfType<JObject>())
            {
                string code = c["lv123code"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(code)) existCodeSet.Add(code);
            }

            int addedCount = 0;
            int duplicateCount = 0;
            var finalArray = new JArray(currentCodes);

            foreach (var item in selectedCodes.OfType<JObject>())
            {
                string code = item["lv123code"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(code)) continue;

                if (existCodeSet.Contains(code))
                {
                    duplicateCount++;
                }
                else
                {
                    existCodeSet.Add(code);
                    var newItem = new JObject(item);
                    if (!newItem.ContainsKey("ex_gubun")) newItem["ex_gubun"] = "자동분석";
                    if (!newItem.ContainsKey("is_lock")) newItem["is_lock"] = false;
                    finalArray.Add(newItem);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                await SaveCodesAsync(pn, qnum, finalArray, ct);
            }

            return (true, addedCount, duplicateCount);
        }
    }
}
