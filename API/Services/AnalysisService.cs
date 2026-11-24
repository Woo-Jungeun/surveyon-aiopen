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
        Task<(bool success, string code, string message, int savedCount)> SaveCodesAsync(string pn, string qnum, JArray codesArray, CancellationToken ct = default, string? lvcode = null);
        Task<(bool success, string code, string message, int savedCount)> SaveResponsesAsync(string pn, string qnum, string duplicateAnswer, JArray responsesArray, CancellationToken ct = default);
        Task<object> CompareResponsesAsync(string pn, string qnum, JArray currentResponses, CancellationToken ct = default);
        Task<byte[]> ExportExcelAsync(string pn, string qnum, CancellationToken ct = default);
        Task<(byte[] bytes, string fileName)> ExportExcelWithFileNameAsync(string pn, string qnum, CancellationToken ct = default);
        Task<ExcelFullImportResult> ImportExcelFileAsync(IFormFile file, string pn, string qnum, CancellationToken ct = default);
        Task<ExcelFullImportResult> ImportExcelJsonAsync(string pn, string qnum, JArray? dataIn, JArray? dataLb, CancellationToken ct = default);
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

            // pn_in / pn_lb는 머지 대표키(merge_pn, merge_qnum)로 저장되므로 대표 메타에서 실제 키를 해석
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string targetPn = !string.IsNullOrWhiteSpace(meta?.merge_pn) ? meta.merge_pn.Trim() : pn;
            string targetQnum = !string.IsNullOrWhiteSpace(meta?.merge_qnum) ? meta.merge_qnum.Trim() : qnum;

            var pnInTask = _repo.GetPnInAsync(targetPn, targetQnum, ct);
            var pnLbTask = _repo.GetPnLbAsync(targetPn, targetQnum, ct);
            await Task.WhenAll(pnInTask, pnLbTask);

            var pnIn = pnInTask.Result ?? ((targetPn != pn || targetQnum != qnum) ? await _repo.GetPnInAsync(pn, qnum, ct) : null);
            var pnLb = pnLbTask.Result ?? ((targetPn != pn || targetQnum != qnum) ? await _repo.GetPnLbAsync(pn, qnum, ct) : null);
            if (pnIn == null) return result;

            string updateDate = pnIn.update_date?.TrimEnd() ?? "";
            if (updateDate == "") updateDate = pnIn.input_date?.TrimEnd() ?? "";

            // 보기 사전: lv123code → [lv1, lv2, lv3, lv1code, lv2code, lv123code]
            var diclb = new Dictionary<string, string?[]>();
            foreach (var c in ParseArray(pnLb?.result_json).OfType<JObject>())
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
                string? answer = Str(row, "answer_fin") ?? Str(row, "answer") ?? answerOrigin;
                string? transDate = Str(row, "trans_date") ?? "";
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
                            result.Add(ResponseRow(fixedKey, cid, lv1, lv2, lv3, lv1code, lv2code, lv123code, sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate, transDate));
                        }
                        else if (lv3 != null)
                        {
                            result.Add(ResponseRow(fixedKey, cid, "", "", lv3, "", "", "", sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate, transDate));
                        }
                        else if (cid == 1)
                        {
                            result.Add(ResponseRow(fixedKey, cid, lv1, lv2, lv3, "", "", "", sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate, transDate));
                        }
                    }
                    else if (cid == 1)
                    {
                        result.Add(ResponseRow(fixedKey, cid, lv1, lv2, lv3, "", "", "", sentiment, recheckyn, excerpts, answerOrigin, answer, updateDate, transDate));
                    }
                }
            }
            return result;
        }

        private static JObject ResponseRow(string? fixedKey, int cid, string? lv1, string? lv2, string? lv3, string? lv1code, string? lv2code,
            string? lv123code, string? sentiment, string? recheckyn, string? excerpts, string? answerOrigin, string? answer, string updateDate, string? transDate = "")
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
                ["answer_fin"] = answer,
                ["trans_date"] = transDate ?? "",
                ["update_date"] = updateDate
            };

        // ══════════════════════════════════════════════════════════════════
        // rawdata (레거시 SelectPnList)
        // ══════════════════════════════════════════════════════════════════
        public async Task<JArray> BuildRawdataAsync(string pn, string qnum, CancellationToken ct = default)
        {
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string targetPn = !string.IsNullOrWhiteSpace(meta?.merge_pn) ? meta.merge_pn.Trim() : pn;
            string targetQnum = !string.IsNullOrWhiteSpace(meta?.merge_qnum) ? meta.merge_qnum.Trim() : qnum;

            var pnInTask = _repo.GetPnInAsync(targetPn, targetQnum, ct);
            var pnLbTask = _repo.GetPnLbAsync(targetPn, targetQnum, ct);
            var srcTask = _repo.GetSourceRowsAsync(targetPn, targetQnum, ct);
            await Task.WhenAll(pnInTask, pnLbTask, srcTask);

            var pnIn = pnInTask.Result ?? ((targetPn != pn || targetQnum != qnum) ? await _repo.GetPnInAsync(pn, qnum, ct) : null);
            var pnLb = pnLbTask.Result ?? ((targetPn != pn || targetQnum != qnum) ? await _repo.GetPnLbAsync(pn, qnum, ct) : null);
            var sources = srcTask.Result.Count > 0 ? srcTask.Result : ((targetPn != pn || targetQnum != qnum) ? await _repo.GetSourceRowsAsync(pn, qnum, ct) : srcTask.Result);

            var diclb = new Dictionary<string, string?[]>();
            var pidlv123code = new Dictionary<string, Dictionary<string, string?>>();
            var pidsentiment = new Dictionary<string, Dictionary<string, string?>>();
            var pidrecheckyn = new Dictionary<string, Dictionary<string, string?>>();
            var keyToAnswer = new Dictionary<string, string?>();
            var keyToAnswerFin = new Dictionary<string, string?>();

            if (pnIn != null)
            {
                foreach (var c in ParseArray(pnLb?.result_json).OfType<JObject>())
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
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string targetPn = !string.IsNullOrWhiteSpace(meta?.merge_pn) ? meta.merge_pn.Trim() : pn;
            string targetQnum = !string.IsNullOrWhiteSpace(meta?.merge_qnum) ? meta.merge_qnum.Trim() : qnum;

            var pnLb = await _repo.GetPnLbAsync(targetPn, targetQnum, ct)
                    ?? ((targetPn != pn || targetQnum != qnum) ? await _repo.GetPnLbAsync(pn, qnum, ct) : null);
            if (pnLb == null) return result;

            var raw = await BuildRawdataAsync(targetPn, targetQnum, ct);
            var exSum = new Dictionary<string, int>();
            foreach (var r in raw)
            {
                string key = r["lv123code"]?.ToString() ?? "";
                exSum[key] = exSum.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            string updateDate = pnLb.update_date?.TrimEnd() ?? "";
            if (updateDate == "") updateDate = pnLb.insert_date?.TrimEnd() ?? "";

            static string CleanCodeStr(JToken? tok)
            {
                string s = tok?.ToString()?.Trim() ?? "";
                return (s.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("nan", StringComparison.OrdinalIgnoreCase))
                    ? ""
                    : s;
            }

            // 레거시 Jsonload.selectPnLb 정렬 규칙 100% 동일:
            // 1순위: ex_gubun == "survey" || "설문(수정불가)" 먼저
            // 2순위: lv1code 없는 것 먼저 → 있는 것 숫자 오름차순
            // 3순위: lv2code 없는 것 먼저 → 있는 것 숫자 오름차순
            // 4순위: lv123code 숫자 오름차순 (없으면 맨 뒤)
            var sorted = ParseArray(pnLb.result_json).OfType<JObject>()
                .OrderBy(o =>
                {
                    string eg = o["ex_gubun"]?.ToString()?.Trim() ?? "";
                    return eg != "survey" && eg != "설문(수정불가)";
                })
                .ThenBy(o => string.IsNullOrWhiteSpace(CleanCodeStr(o["lv1code"])) ? 0 : 1)
                .ThenBy(o => int.TryParse(CleanCodeStr(o["lv1code"]), out var v1) ? v1 : int.MaxValue)
                .ThenBy(o => string.IsNullOrWhiteSpace(CleanCodeStr(o["lv2code"])) ? 0 : 1)
                .ThenBy(o => int.TryParse(CleanCodeStr(o["lv2code"]), out var v2) ? v2 : int.MaxValue)
                .ThenBy(o => int.TryParse(CleanCodeStr(o["lv123code"]), out var v3) ? v3 : int.MaxValue);

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
            static string? NonEmpty(params string?[] vals) =>
                vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

            string qFin = NonEmpty(
                              inputSettings?["keyword_string"]?.ToString(),
                              inputSettings?["question_fin"]?.ToString(),
                              existingOpt["keyword_string"]?.ToString(),
                              meta.question_fin)
                          ?? "";

            string promptString = inputSettings?["prompt_string"] != null
                                  ? (inputSettings["prompt_string"]?.ToString()?.Trim() ?? "")
                                  : (existingOpt["prompt_string"]?.ToString()?.Trim() ?? "");

            string modelSelect = NonEmpty(
                                     inputSettings?["model_select"]?.ToString(),
                                     existingOpt["model_select"]?.ToString())
                                 ?? "GPT-5.2";

            string resultLang = NonEmpty(
                                    inputSettings?["result_lang"]?.ToString(),
                                    existingOpt["result_lang"]?.ToString())
                                ?? "Korean";
            resultLang = resultLang switch
            {
                "한국어" or "1" => "Korean",
                "영어" or "2" => "English",
                "일본어" or "3" => "Japanese",
                "중국어" or "4" => "Chinese",
                _ => resultLang
            };

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

            // 6. DB 저장 (사용자/그룹 정보 함께 갱신 — 대표 qid 및 merge_pn/merge_qnum 동시 반영)
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
                ct,
                meta.id);

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
            string pn, string qnum, JArray codesArray, CancellationToken ct = default, string? lvcode = null)
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

            // 5. HData surveyon_labels 테이블에 대/중/소분류 일괄 동기화 (is_open=true 및 lvcode 단계별 반영)
            try
            {
                await _repo.SyncCodesToHDataAsync(pn, qnum, codesArray, ct, lvcode);
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
            var (bytes, _) = await ExportExcelWithFileNameAsync(pn, qnum, ct);
            return bytes;
        }

        private static string ResolveExcelTemplatePath(string templateFileName)
        {
            string[] candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Excel", templateFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Excel", templateFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "API", "Excel", templateFileName),
                Path.Combine(AppContext.BaseDirectory, "docs", "legacy_src", "Excel", templateFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "docs", "legacy_src", "Excel", templateFileName)
            };
            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        private static string SanitizeExcelSheetName(string rawName, string fallback = "pn")
        {
            string s = (rawName ?? "").Trim();
            if (string.IsNullOrEmpty(s)) s = fallback;
            s = Regex.Replace(s, @"[:\\/?*\[\]]", "_");
            if (s.Length > 31) s = s.Substring(0, 31);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        /// <summary>
        /// 레거시 option_edit_api.aspx.cs ExportExcel(merge_pn, merge_qnum) 100% 호환 이관 (Excel/HR_Sample3.xlsx 기반 5개 시트 생성)
        ///  - 시트 1: {merge_pn} (응답자 코딩 시트, 1~3행 메타정보 + 4행 헤더 + 5행~ 데이터)
        ///  - 시트 2: 보기항목 (레거시 정렬 코드북 + J열 COUNTIF 수식)
        ///  - 시트 3: rawdata_세로_복수 (PID별 세로 복수 + 코드 중복 표시)
        ///  - 시트 4: rawdata_가로_복수 (PID별 가로 멀티1~10 + 콤마 머지)
        ///  - 시트 5: rawdata_PID_머지문항_중복제거 (중복제거 설정 시 PID별 병합 결과)
        /// </summary>
        public async Task<(byte[] bytes, string fileName)> ExportExcelWithFileNameAsync(string pn, string qnum, CancellationToken ct = default)
        {
            EnsureAsposeLicense();

            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string mergePn = !string.IsNullOrWhiteSpace(meta?.merge_pn) ? meta.merge_pn.Trim() : pn.Trim();
            string mergeQnum = !string.IsNullOrWhiteSpace(meta?.merge_qnum) ? meta.merge_qnum.Trim() : qnum.Trim();
            string qnumText = !string.IsNullOrWhiteSpace(meta?.qnum_text) ? meta.qnum_text.TrimEnd() : mergeQnum;
            string questionFin = meta?.question_fin?.TrimEnd() ?? "";
            string projectName = meta?.projectname?.TrimEnd() ?? "";
            string duplicateRemoveDate = meta?.duplicate_remove_date?.TrimEnd() ?? "";

            string lvcode = (await _repo.GetLvcodeAsync(mergePn, mergeQnum, ct)).Trim();
            if (string.IsNullOrEmpty(lvcode)) lvcode = meta?.lvcode?.Trim() ?? "";
            if (lvcode != "1" && lvcode != "2" && lvcode != "3") lvcode = "3";

            string templatePath = ResolveExcelTemplatePath("HR_Sample3.xlsx");
            Workbook wb = File.Exists(templatePath) ? new Workbook(templatePath) : new Workbook();

            Worksheet worksheet = wb.Worksheets["pn"] ?? wb.Worksheets[0];
            Worksheet worksheet2 = wb.Worksheets["보기항목"] ?? wb.Worksheets.Add("보기항목");
            Worksheet worksheet3 = wb.Worksheets["rawdata_세로_복수"] ?? wb.Worksheets.Add("rawdata_세로_복수");
            Worksheet worksheet4 = wb.Worksheets["rawdata_가로_복수"] ?? wb.Worksheets.Add("rawdata_가로_복수");

            bool hasDuplicateRemove = !string.IsNullOrWhiteSpace(duplicateRemoveDate);
            const string worksheet5Name = "rawdata_PID_머지문항_중복제거";
            Worksheet? worksheet5 = null;
            if (hasDuplicateRemove)
            {
                worksheet5 = wb.Worksheets[worksheet5Name] ?? wb.Worksheets[wb.Worksheets.Add()];
                worksheet5.Name = worksheet5Name;
            }
            else if (wb.Worksheets[worksheet5Name] != null)
            {
                wb.Worksheets.RemoveAt(worksheet5Name);
            }

            string mainSheetName = SanitizeExcelSheetName(mergePn, "pn");
            worksheet.Name = mainSheetName;
            string formulaSheetRef = mainSheetName.Replace("'", "''");

            var pnInTask = _repo.GetPnInAsync(mergePn, mergeQnum, ct);
            var pnLbTask = _repo.GetPnLbAsync(mergePn, mergeQnum, ct);
            var originRowsTask = _repo.GetProjectOriginRowsAsync(mergePn, mergeQnum, isDuplicateRemove: true, ct);
            await Task.WhenAll(pnInTask, pnLbTask, originRowsTask);

            var pnIn = pnInTask.Result ?? ((mergePn != pn || mergeQnum != qnum) ? await _repo.GetPnInAsync(pn, qnum, ct) : null);
            var pnLb = pnLbTask.Result ?? ((mergePn != pn || mergeQnum != qnum) ? await _repo.GetPnLbAsync(pn, qnum, ct) : null);
            var originRows = originRowsTask.Result;

            string updateDate = pnIn?.update_date?.TrimEnd() ?? "";
            if (string.IsNullOrEmpty(updateDate)) updateDate = pnIn?.input_date?.TrimEnd() ?? "";

            // 시트 1 상단 메타 셀 세팅 (레거시 L583-L586 동일)
            worksheet.Cells[0, 1].Value = updateDate;
            worksheet.Cells[1, 0].Value = mergeQnum;
            worksheet.Cells[1, 1].Value = qnumText;
            worksheet.Cells[2, 1].Value = $"{questionFin} ";

            var pidlv3 = new Dictionary<string, Dictionary<string, string>>();
            var pidlv123code = new Dictionary<string, Dictionary<string, string>>();
            var pidsentiment = new Dictionary<string, Dictionary<string, string>>();
            var pidrecheckyn = new Dictionary<string, Dictionary<string, string>>();
            var diclb = new Dictionary<string, string[]>();

            // 원본 응답 매칭용 사전 (번역 전/후 answer, answer_fin, answer_origin 모두 인덱싱하여 누락 방지)
            var keyToFixedKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keyToAnswerFin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var lbItems = ParseArray(pnLb?.result_json).OfType<JObject>().ToList();
            foreach (var c in lbItems)
            {
                string cLv1 = lvcode == "3" ? (Str(c, "lv1") ?? "") : "";
                string cLv1Code = lvcode == "3" ? (Str(c, "lv1code") ?? "") : "";
                string cLv2 = (lvcode == "2" || lvcode == "3") ? (Str(c, "lv2") ?? "") : "";
                string cLv2Code = (lvcode == "2" || lvcode == "3") ? (Str(c, "lv2code") ?? "") : "";
                string cLv3 = Str(c, "lv3") ?? "";
                string cCode = Str(c, "lv123code") ?? "";
                if (!string.IsNullOrEmpty(cCode))
                {
                    diclb[cCode] = new[] { cLv1, cLv2, cLv3, cLv1Code, cLv2Code };
                }
            }

            if (pnIn != null)
            {
                // 응답자 코딩 결과 파싱 (동일 fixed_key 내 중복 lv123code 제거 및 cid 1부터 재부여 — 레거시 L640-L696 동일)
                var lv123codeSet = new Dictionary<string, HashSet<string>>();
                var nextCidByKey = new Dictionary<string, int>();

                foreach (var o in ParseArray(pnIn.answer_fin).OfType<JObject>())
                {
                    string fixedKey = Str(o, "fixed_key") ?? "";
                    if (string.IsNullOrEmpty(fixedKey)) continue;

                    string lv3 = Str(o, "lv3") ?? "";
                    string lv123code = Str(o, "lv123code") ?? "";
                    string sentiment = Str(o, "sentiment") ?? "";
                    string rawRecheck = Str(o, "recheckyn") ?? "";
                    string recheckyn = (rawRecheck == "완료" || rawRecheck.Equals("y", StringComparison.OrdinalIgnoreCase) || rawRecheck == "1") ? "y" : "";

                    if (!pidlv3.ContainsKey(fixedKey)) pidlv3[fixedKey] = new Dictionary<string, string>();
                    if (!pidsentiment.ContainsKey(fixedKey)) pidsentiment[fixedKey] = new Dictionary<string, string>();
                    if (!pidlv123code.ContainsKey(fixedKey)) pidlv123code[fixedKey] = new Dictionary<string, string>();
                    if (!pidrecheckyn.ContainsKey(fixedKey)) pidrecheckyn[fixedKey] = new Dictionary<string, string>();
                    if (!lv123codeSet.ContainsKey(fixedKey)) lv123codeSet[fixedKey] = new HashSet<string>();

                    if (!string.IsNullOrEmpty(lv123code) && lv123codeSet[fixedKey].Contains(lv123code))
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(lv123code))
                    {
                        lv123codeSet[fixedKey].Add(lv123code);
                    }

                    int newCid = nextCidByKey.TryGetValue(fixedKey, out int curCid) ? curCid + 1 : 1;
                    nextCidByKey[fixedKey] = newCid;
                    string newCidS = newCid.ToString();

                    pidlv3[fixedKey][newCidS] = lv3;
                    pidsentiment[fixedKey][newCidS] = sentiment;
                    pidlv123code[fixedKey][newCidS] = lv123code;
                    pidrecheckyn[fixedKey][newCidS] = recheckyn;
                }

                // 시트 1 ({mergePn}) 데이터 행 기록 (0-based 4행 = Excel 5행부터 시작)
                int rowcount = 3;
                foreach (var row in ParseArray(pnIn.origin_items).OfType<JObject>())
                {
                    string answerOrigin = Str(row, "answer_origin") ?? "";
                    string answerRaw = Str(row, "answer") ?? "";
                    string answerFin = Str(row, "answer_fin") ?? answerRaw;
                    string fixedKey = Str(row, "fixed_key") ?? "";

                    foreach (var cand in new[] { answerRaw, answerFin, answerOrigin })
                    {
                        if (!string.IsNullOrEmpty(cand))
                        {
                            keyToFixedKey[cand] = fixedKey;
                            keyToAnswerFin[cand] = answerFin;
                        }
                    }

                    int rowcnt = pidlv123code.TryGetValue(fixedKey, out var codeDict) && codeDict.Count > 0 ? codeDict.Count : 1;
                    for (int cid = 1; cid <= rowcnt; cid++)
                    {
                        rowcount++;
                        worksheet.Cells[rowcount, 0].Value = fixedKey;
                        worksheet.Cells[rowcount, 1].Value = cid;
                        worksheet.Cells[rowcount, 2].Value = answerOrigin;
                        worksheet.Cells[rowcount, 3].Value = answerFin;

                        string sid = cid.ToString();
                        if (pidlv123code.TryGetValue(fixedKey, out var pCodes) && pCodes.ContainsKey(sid))
                        {
                            string lv3 = pidlv3[fixedKey].GetValueOrDefault(sid, "");
                            string lv123code = pCodes.GetValueOrDefault(sid, "");
                            string sentiment = pidsentiment[fixedKey].GetValueOrDefault(sid, "");
                            string recheckyn = pidrecheckyn[fixedKey].GetValueOrDefault(sid, "");
                            string lv1 = "";
                            string lv2 = "";

                            if (!string.IsNullOrEmpty(lv123code) && diclb.TryGetValue(lv123code, out var lb))
                            {
                                if (!string.IsNullOrEmpty(lb[0])) lv1 = lb[0];
                                if (!string.IsNullOrEmpty(lb[1])) lv2 = lb[1];
                                if (!string.IsNullOrEmpty(lb[2])) lv3 = lb[2];

                                worksheet.Cells[rowcount, 4].Value = lv1;
                                worksheet.Cells[rowcount, 5].Value = lv2;
                                worksheet.Cells[rowcount, 6].Value = lv3;
                                worksheet.Cells[rowcount, 7].Value = lv123code;
                                worksheet.Cells[rowcount, 8].Value = sentiment;
                                worksheet.Cells[rowcount, 9].Value = recheckyn;
                            }
                            else if (!string.IsNullOrEmpty(lv3))
                            {
                                worksheet.Cells[rowcount, 4].Value = lv1;
                                worksheet.Cells[rowcount, 5].Value = lv2;
                                worksheet.Cells[rowcount, 6].Value = lv3;
                                worksheet.Cells[rowcount, 7].Value = lv123code;
                                worksheet.Cells[rowcount, 8].Value = sentiment;
                                worksheet.Cells[rowcount, 9].Value = recheckyn;
                            }
                            else if (cid == 1)
                            {
                                for (int col = 4; col <= 9; col++) worksheet.Cells[rowcount, col].Value = "";
                            }
                        }
                        else if (cid == 1)
                        {
                            for (int col = 4; col <= 9; col++) worksheet.Cells[rowcount, col].Value = "";
                        }
                    }
                }
            }

            // ── 시트 2: 보기항목 (레거시 L793-L848 동일) ──
            string dkCode = "";
            if (lbItems.Count > 0)
            {
                static string CleanCodeToken(JToken? tok)
                {
                    string s = tok?.ToString()?.Trim() ?? "";
                    return (s.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                            s.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                            s.Equals("nan", StringComparison.OrdinalIgnoreCase)) ? "" : s;
                }

                var sortedLb = lbItems
                    .OrderBy(o =>
                    {
                        string eg = o["ex_gubun"]?.ToString()?.Trim() ?? "";
                        return eg != "survey" && eg != "설문(수정불가)";
                    })
                    .ThenBy(o => string.IsNullOrWhiteSpace(CleanCodeToken(o["lv1code"])) ? 0 : 1)
                    .ThenBy(o => int.TryParse(CleanCodeToken(o["lv1code"]), out var v1) ? v1 : int.MaxValue)
                    .ThenBy(o => string.IsNullOrWhiteSpace(CleanCodeToken(o["lv2code"])) ? 0 : 1)
                    .ThenBy(o => int.TryParse(CleanCodeToken(o["lv2code"]), out var v2) ? v2 : int.MaxValue)
                    .ThenBy(o => int.TryParse(CleanCodeToken(o["lv123code"]), out var v3) ? v3 : int.MaxValue)
                    .ToList();

                int rowcount = 0;
                foreach (var row in sortedLb)
                {
                    rowcount++;
                    string lv1 = lvcode == "3" ? CleanCodeToken(row["lv1"]) : "";
                    string lv2 = (lvcode == "2" || lvcode == "3") ? CleanCodeToken(row["lv2"]) : "";
                    string lv3 = CleanCodeToken(row["lv3"]);
                    string lv1code = lvcode == "3" ? CleanCodeToken(row["lv1code"]) : "";
                    string lv2code = (lvcode == "2" || lvcode == "3") ? CleanCodeToken(row["lv2code"]) : "";
                    string lv123code = CleanCodeToken(row["lv123code"]);
                    string rawExGubun = CleanCodeToken(row["ex_gubun"]);
                    string exGubun = (rawExGubun == "survey" || rawExGubun == "설문(수정불가)") ? "설문(수정불가)" : "분석(수정가능)";

                    if (lv3.Contains("모름"))
                    {
                        dkCode = lv123code;
                    }

                    worksheet2.Cells[rowcount, 0].Value = rowcount;
                    worksheet2.Cells[rowcount, 1].Value = qnumText;
                    worksheet2.Cells[rowcount, 2].Value = lv1code;
                    worksheet2.Cells[rowcount, 3].Value = lv1;
                    worksheet2.Cells[rowcount, 4].Value = lv2code;
                    worksheet2.Cells[rowcount, 5].Value = lv2;
                    worksheet2.Cells[rowcount, 6].Value = lv123code;
                    worksheet2.Cells[rowcount, 7].Value = lv3;
                    worksheet2.Cells[rowcount, 8].Value = exGubun;
                    worksheet2.Cells[rowcount, 9].Formula = $"=COUNTIF('{formulaSheetRef}'!$H:$H, G{rowcount + 1})";
                }
            }

            // ── 시트 3: rawdata_세로_복수 (레거시 L852-L1008 동일) ──
            var duplicatePnMap = new Dictionary<string, Dictionary<string, List<(string lv123code, string recheckyn)>>>();
            var duplicatePnMapFin = new Dictionary<string, Dictionary<string, List<(string lv123code, string recheckyn)>>>();

            if (originRows.Count > 0)
            {
                int rowcount = 0;
                foreach (var rowlist in originRows)
                {
                    string jsonOrigin = rowlist.answer_origin?.TrimEnd() ?? "";
                    string srcQnum = rowlist.qnum?.TrimEnd() ?? "";
                    string srcPn = rowlist.projectnum?.TrimEnd() ?? "";
                    if (string.IsNullOrEmpty(jsonOrigin)) continue;

                    JArray? items = null;
                    try
                    {
                        var objOrigin = JObject.Parse(jsonOrigin);
                        items = (objOrigin[srcQnum] ?? objOrigin.Properties().FirstOrDefault()?.Value) as JArray;
                    }
                    catch { }
                    if (items == null) continue;

                    foreach (var item in items.OfType<JObject>())
                    {
                        try
                        {
                            string itemAnswerFin = item["answer_fin"]?.ToString().TrimEnd() ?? "";
                            string itemAnswerOrigin = item["answer_origin"]?.ToString().TrimEnd() ?? "";
                            string pid = item["pid"]?.ToString() ?? "";

                            if (!keyToFixedKey.TryGetValue(itemAnswerFin, out var fixedKey) &&
                                !keyToFixedKey.TryGetValue(itemAnswerOrigin, out fixedKey))
                            {
                                fixedKey = "";
                            }

                            string answerfin = keyToAnswerFin.TryGetValue(itemAnswerFin, out var af)
                                ? af
                                : (keyToAnswerFin.TryGetValue(itemAnswerOrigin, out af) ? af : itemAnswerFin);

                            if (!pidlv123code.ContainsKey(fixedKey))
                            {
                                pidlv123code[fixedKey] = new Dictionary<string, string> { ["1"] = "" };
                            }
                            if (!pidrecheckyn.ContainsKey(fixedKey))
                            {
                                pidrecheckyn[fixedKey] = new Dictionary<string, string> { ["1"] = "" };
                            }

                            string recheckyn = pidrecheckyn[fixedKey].GetValueOrDefault("1", "");
                            int rowcnt = pidlv123code[fixedKey].Count(x => !string.IsNullOrEmpty(x.Value));

                            for (int cid = 1; cid <= rowcnt; cid++)
                            {
                                string sid = cid.ToString();
                                if (!pidlv123code[fixedKey].ContainsKey(sid)) continue;

                                rowcount++;
                                worksheet3.Cells[rowcount, 0].Value = pid;
                                worksheet3.Cells[rowcount, 1].Value = srcQnum;
                                worksheet3.Cells[rowcount, 2].Value = cid;
                                worksheet3.Cells[rowcount, 3].Value = itemAnswerOrigin;
                                worksheet3.Cells[rowcount, 4].Value = answerfin;

                                string lv3 = pidlv3.TryGetValue(fixedKey, out var l3Map) ? l3Map.GetValueOrDefault(sid, "") : "";
                                string lv123code = pidlv123code[fixedKey].GetValueOrDefault(sid, "");
                                string sentiment = pidsentiment.TryGetValue(fixedKey, out var sMap) ? sMap.GetValueOrDefault(sid, "") : "";
                                if (pidrecheckyn.TryGetValue(fixedKey, out var rMap) && rMap.ContainsKey(sid))
                                    recheckyn = rMap[sid];

                                if (!string.IsNullOrEmpty(lv123code) && diclb.TryGetValue(lv123code, out var lb))
                                {
                                    string lv1 = lb[0] ?? "";
                                    string lv2 = lb[1] ?? "";
                                    if (!string.IsNullOrEmpty(lb[2])) lv3 = lb[2];
                                    string lv1code = lb[3] ?? "";
                                    string lv2code = lb[4] ?? "";

                                    if (!duplicatePnMap.ContainsKey(srcPn))
                                    {
                                        duplicatePnMap[srcPn] = new Dictionary<string, List<(string, string)>>();
                                        duplicatePnMapFin[srcPn] = new Dictionary<string, List<(string, string)>>();
                                    }
                                    if (!duplicatePnMap[srcPn].ContainsKey(pid))
                                    {
                                        duplicatePnMap[srcPn][pid] = new List<(string, string)>();
                                        duplicatePnMapFin[srcPn][pid] = new List<(string, string)>();
                                    }

                                    worksheet3.Cells[rowcount, 5].Value = lv1code;
                                    worksheet3.Cells[rowcount, 6].Value = lv1;
                                    worksheet3.Cells[rowcount, 7].Value = lv2code;
                                    worksheet3.Cells[rowcount, 8].Value = lv2;
                                    worksheet3.Cells[rowcount, 9].Value = lv123code;
                                    worksheet3.Cells[rowcount, 10].Value = lv3;
                                    worksheet3.Cells[rowcount, 11].Value = sentiment;
                                    worksheet3.Cells[rowcount, 12].Value = recheckyn;
                                    worksheet3.Cells[rowcount, 13].Value = fixedKey;

                                    if (duplicatePnMap[srcPn][pid].Any(x => x.lv123code == lv123code))
                                    {
                                        worksheet3.Cells[rowcount, 14].Value = "중복";
                                    }
                                    duplicatePnMap[srcPn][pid].Add((lv123code, recheckyn));

                                    var listcheck = duplicatePnMap[srcPn][pid];
                                    bool isCurrentDk = !string.IsNullOrEmpty(dkCode) && lv123code == dkCode;
                                    bool hasOtherNonDk = !string.IsNullOrEmpty(dkCode) && listcheck.Any(x => x.lv123code != dkCode);

                                    if (!(isCurrentDk && hasOtherNonDk))
                                    {
                                        duplicatePnMapFin[srcPn][pid].Add((lv123code, recheckyn));
                                    }
                                    else
                                    {
                                        worksheet3.Cells[rowcount, 14].Value = "중복";
                                    }
                                }
                                else
                                {
                                    worksheet3.Cells[rowcount, 10].Value = lv3;
                                    worksheet3.Cells[rowcount, 11].Value = sentiment;
                                    worksheet3.Cells[rowcount, 12].Value = recheckyn;
                                    worksheet3.Cells[rowcount, 13].Value = fixedKey;
                                    worksheet3.Cells[rowcount, 14].Value = "";
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // ── 시트 4: rawdata_가로_복수 & 시트 5: rawdata_PID_머지문항_중복제거 (레거시 L1010-L1227 동일) ──
            var duplicatePnMap2 = new Dictionary<string, Dictionary<string, List<(string lv123code, string recheckyn)>>>();
            var duplicatePnMapFin2 = new Dictionary<string, Dictionary<string, List<(string lv123code, string recheckyn)>>>();
            var mergePnQnum = new Dictionary<string, List<string>>();

            if (originRows.Count > 0)
            {
                int rowcount = 0;
                foreach (var rowlist in originRows)
                {
                    string jsonOrigin = rowlist.answer_origin?.TrimEnd() ?? "";
                    string srcQnum = rowlist.qnum?.TrimEnd() ?? "";
                    string srcPn = rowlist.projectnum?.TrimEnd() ?? "";

                    if (!mergePnQnum.ContainsKey(srcPn)) mergePnQnum[srcPn] = new List<string>();
                    mergePnQnum[srcPn].Add(srcQnum);

                    if (string.IsNullOrEmpty(jsonOrigin)) continue;

                    JArray? items = null;
                    try
                    {
                        var objOrigin = JObject.Parse(jsonOrigin);
                        items = (objOrigin[srcQnum] ?? objOrigin.Properties().FirstOrDefault()?.Value) as JArray;
                    }
                    catch { }
                    if (items == null) continue;

                    foreach (var item in items.OfType<JObject>())
                    {
                        try
                        {
                            string itemAnswerFin = item["answer_fin"]?.ToString().TrimEnd() ?? "";
                            string itemAnswerOrigin = item["answer_origin"]?.ToString().TrimEnd() ?? "";
                            string pid = item["pid"]?.ToString() ?? "";

                            if (!keyToFixedKey.TryGetValue(itemAnswerFin, out var fixedKey) &&
                                !keyToFixedKey.TryGetValue(itemAnswerOrigin, out fixedKey))
                            {
                                fixedKey = "";
                            }

                            string answerfin = keyToAnswerFin.TryGetValue(itemAnswerFin, out var af)
                                ? af
                                : (keyToAnswerFin.TryGetValue(itemAnswerOrigin, out af) ? af : itemAnswerFin);

                            if (!pidlv123code.ContainsKey(fixedKey))
                            {
                                pidlv123code[fixedKey] = new Dictionary<string, string> { ["1"] = "" };
                            }
                            if (!pidrecheckyn.ContainsKey(fixedKey))
                            {
                                pidrecheckyn[fixedKey] = new Dictionary<string, string> { ["1"] = "" };
                            }

                            string recheckyn = pidrecheckyn[fixedKey].GetValueOrDefault("1", "");
                            rowcount++;

                            worksheet4.Cells[rowcount, 0].Value = "'" + pid;
                            worksheet4.Cells[rowcount, 1].Value = srcQnum;
                            worksheet4.Cells[rowcount, 2].Value = itemAnswerOrigin;
                            worksheet4.Cells[rowcount, 3].Value = answerfin;

                            string tempmergecode = "";
                            int outIndex = 1;
                            for (int cid = 1; cid <= 10; cid++)
                            {
                                string sid = cid.ToString();
                                int cidcell = cid + 3;

                                if (pidlv123code[fixedKey].TryGetValue(sid, out var lv123code) && !string.IsNullOrEmpty(lv123code))
                                {
                                    string lv3 = pidlv3.TryGetValue(fixedKey, out var l3Map) ? l3Map.GetValueOrDefault(sid, "") : "";
                                    if (diclb.ContainsKey(lv123code))
                                    {
                                        if (string.IsNullOrEmpty(duplicateRemoveDate))
                                        {
                                            worksheet4.Cells[rowcount, cidcell].Value = lv123code;
                                            tempmergecode += lv123code + ",";
                                        }
                                        else
                                        {
                                            if (!duplicatePnMap2.ContainsKey(srcPn))
                                            {
                                                duplicatePnMap2[srcPn] = new Dictionary<string, List<(string, string)>>();
                                                duplicatePnMapFin2[srcPn] = new Dictionary<string, List<(string, string)>>();
                                            }
                                            if (!duplicatePnMap2[srcPn].ContainsKey(pid))
                                            {
                                                duplicatePnMap2[srcPn][pid] = new List<(string, string)>();
                                                duplicatePnMapFin2[srcPn][pid] = new List<(string, string)>();
                                            }

                                            bool alreadyHasCode = duplicatePnMap2[srcPn][pid].Any(x => x.lv123code == lv123code);
                                            if (!alreadyHasCode)
                                            {
                                                duplicatePnMap2[srcPn][pid].Add((lv123code, recheckyn));
                                            }
                                            else
                                            {
                                                worksheet4.Cells[rowcount, cidcell].Value = lv123code + "(중복)";
                                            }

                                            var list = duplicatePnMap2[srcPn][pid];
                                            bool isCurrentDk = !string.IsNullOrEmpty(dkCode) && lv123code == dkCode;
                                            bool hasOtherNonDk = !string.IsNullOrEmpty(dkCode) && list.Any(x => x.lv123code != dkCode);

                                            if (!alreadyHasCode && !(isCurrentDk && hasOtherNonDk))
                                            {
                                                int outCol = outIndex + 3;
                                                worksheet4.Cells[rowcount, outCol].Value = lv123code;
                                                tempmergecode += lv123code + ",";
                                                outIndex++;
                                                if (!duplicatePnMapFin2[srcPn][pid].Any(x => x.lv123code == lv123code))
                                                {
                                                    duplicatePnMapFin2[srcPn][pid].Add((lv123code, recheckyn));
                                                }
                                            }
                                            else
                                            {
                                                worksheet4.Cells[rowcount, cidcell].Value = lv123code + "(중복)";
                                            }
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(lv3))
                                    {
                                        worksheet4.Cells[rowcount, cidcell].Value = "";
                                    }
                                }
                                else
                                {
                                    worksheet4.Cells[rowcount, cidcell].Value = "";
                                }
                            }

                            if (tempmergecode.EndsWith(","))
                            {
                                tempmergecode = tempmergecode[..^1];
                            }
                            worksheet4.Cells[rowcount, 14].Value = tempmergecode;
                        }
                        catch { }
                    }
                }

                // 시트 5 헤더 및 중복제거 병합 PID 행 기록 (duplicate_remove_date 있을 때만 생성)
                if (hasDuplicateRemove && worksheet5 != null)
                {
                    worksheet5.Cells[0, 0].PutValue("PID");
                    worksheet5.Cells[0, 1].PutValue("머지문항");
                    for (int i = 1; i <= 10; i++)
                    {
                        worksheet5.Cells[0, i + 1].PutValue("소분류 멀티" + i);
                    }
                    worksheet5.Cells[0, 12].PutValue("1~10 콤마로 머지");

                    int rowcount2 = 1;
                    foreach (var pnItem in mergePnQnum)
                    {
                        if (!duplicatePnMapFin2.TryGetValue(pnItem.Key, out var pidDict)) continue;
                        foreach (var pidItem in pidDict)
                        {
                            string pid = pidItem.Key;
                            var list = pidItem.Value;
                            string tempmergecode2 = "";

                            worksheet5.Cells[rowcount2, 0].PutValue("'" + pid);
                            worksheet5.Cells[rowcount2, 1].PutValue(mergeQnum);

                            int col = 2;
                            for (int i = 0; i < list.Count; i++)
                            {
                                string cVal = list[i].lv123code ?? "";
                                worksheet5.Cells[rowcount2, col].PutValue(cVal);
                                tempmergecode2 += cVal + ",";
                                col++;
                            }
                            if (tempmergecode2.EndsWith(","))
                            {
                                tempmergecode2 = tempmergecode2[..^1];
                            }
                            worksheet5.Cells[rowcount2, 12].PutValue(tempmergecode2);
                            rowcount2++;
                        }
                    }
                }
            }

            try { wb.CalculateFormula(); } catch { }

            string safeProjectName = Regex.Replace(projectName, @"[/\\:*?""<>|]", "_");
            string safeMergeQnum = Regex.Replace(mergeQnum, @"[/\\:*?""<>|]", "_");
            string fileName = string.IsNullOrWhiteSpace(safeProjectName)
                ? $"hrcopen_({mergePn})_{safeMergeQnum}.xlsx"
                : $"hrcopen_{safeProjectName}_({mergePn})_{safeMergeQnum}.xlsx";

            using var ms = new MemoryStream();
            wb.Save(ms, SaveFormat.Xlsx);
            return (ms.ToArray(), fileName);
        }

        private static string NormalizeExcelHeader(string? raw)
            => Regex.Replace(raw ?? "", @"[\s\u00A0\u202F]+", "").ToLowerInvariant().Trim();

        /// <summary>
        /// 레거시 HR_Sample3.xlsx 기반 응답자+보기 통합 엑셀 파일 파싱 및 DB 반영 (option_edit_api.aspx.cs gb="import_excel" 이관)
        /// </summary>
        public async Task<ExcelFullImportResult> ImportExcelFileAsync(IFormFile file, string pn, string qnum, CancellationToken ct = default)
        {
            EnsureAsposeLicense();

            if (file == null || file.Length == 0)
            {
                return new ExcelFullImportResult { ok = false, success = "701", message = "업로드할 엑셀 파일이 없습니다." };
            }

            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string mergePn = !string.IsNullOrWhiteSpace(meta?.merge_pn) ? meta.merge_pn.Trim() : pn.Trim();

            using var stream = file.OpenReadStream();
            var wb = new Workbook(stream);

            // 1) 응답자 시트 탐색 ({mergePn} → {pn} → 첫 번째 시트) 및 보기항목 시트 탐색
            Worksheet? respSheet = wb.Worksheets[SanitizeExcelSheetName(mergePn)]
                                ?? wb.Worksheets[SanitizeExcelSheetName(pn)]
                                ?? wb.Worksheets["응답데이터"]
                                ?? (wb.Worksheets.Count > 0 ? wb.Worksheets[0] : null);

            Worksheet? viewSheet = wb.Worksheets["보기항목"]
                                ?? wb.Worksheets["보기목록"]
                                ?? (wb.Worksheets.Count > 1 ? wb.Worksheets[1] : null);

            if (respSheet == null || viewSheet == null)
            {
                return new ExcelFullImportResult
                {
                    ok = false,
                    success = "701",
                    message = "엑셀 파일에서 응답자 시트 또는 [보기항목] 시트를 찾을 수 없습니다."
                };
            }

            // 2) 보기항목 시트 파싱 (헤더 1행 기준, HR_Sample3.xlsx: no | 문번호 | lv1 | 대분류 | lv2 | 중분류 | lv3(코드) | 소분류(보기명) | 보기유형 | 집계현황)
            var dataLb = new JArray();
            int vMaxRow = viewSheet.Cells.MaxDataRow;
            int vMaxCol = viewSheet.Cells.MaxDataColumn;
            if (vMaxRow >= 0 && vMaxCol >= 0)
            {
                int vHeaderRow = 0;
                int colQnum = -1, colLv1Code = -1, colLv1 = -1, colLv2Code = -1, colLv2 = -1, colLv3Code = -1, colLv3 = -1, colGubun = -1;

                for (int r = 0; r <= Math.Min(3, vMaxRow); r++)
                {
                    for (int c = 0; c <= vMaxCol; c++)
                    {
                        string h = NormalizeExcelHeader(viewSheet.Cells[r, c].StringValue);
                        if (h == "문번호" || h == "qnum") colQnum = c;
                        else if (h == "lv1" || h == "대분류코드" || h == "대코드" || h == "lv1code") colLv1Code = c;
                        else if (h == "대분류" || h == "대분류명") colLv1 = c;
                        else if (h == "lv2" || h == "중분류코드" || h == "중코드" || h == "lv2code") colLv2Code = c;
                        else if (h == "중분류" || h == "중분류명") colLv2 = c;
                        else if (h == "lv3" || h == "소분류코드" || h == "소코드" || h == "lv123code") colLv3Code = c;
                        else if (h == "소분류" || h == "소분류명" || h == "보기명" || h == "보기명(소분류)") colLv3 = c;
                        else if (h.Contains("보기유형") || h == "보기구분" || h == "유형" || h == "ex_gubun") colGubun = c;
                    }
                    if (colLv3Code >= 0 || colLv3 >= 0)
                    {
                        vHeaderRow = r;
                        break;
                    }
                }

                if (colQnum < 0) colQnum = 1;
                if (colLv1Code < 0) colLv1Code = 2;
                if (colLv1 < 0) colLv1 = 3;
                if (colLv2Code < 0) colLv2Code = 4;
                if (colLv2 < 0) colLv2 = 5;
                if (colLv3Code < 0) colLv3Code = 6;
                if (colLv3 < 0) colLv3 = 7;
                if (colGubun < 0) colGubun = 8;

                for (int r = vHeaderRow + 1; r <= vMaxRow; r++)
                {
                    string CellStr(int col) => col >= 0 && col <= vMaxCol ? (viewSheet.Cells[r, col].StringValue?.Trim() ?? "") : "";
                    string lv123code = CellStr(colLv3Code);
                    string lv3 = CellStr(colLv3);
                    string lv1code = CellStr(colLv1Code);
                    string lv1 = CellStr(colLv1);
                    string lv2code = CellStr(colLv2Code);
                    string lv2 = CellStr(colLv2);
                    string rowQnum = CellStr(colQnum);
                    string exGubun = CellStr(colGubun);

                    if (string.IsNullOrEmpty(lv123code) && string.IsNullOrEmpty(lv3) && string.IsNullOrEmpty(lv1) && string.IsNullOrEmpty(lv2))
                        continue;

                    dataLb.Add(new JObject
                    {
                        ["qnum"] = string.IsNullOrEmpty(rowQnum) ? qnum : rowQnum,
                        ["lv1code"] = lv1code,
                        ["lv1"] = lv1,
                        ["lv2code"] = lv2code,
                        ["lv2"] = lv2,
                        ["lv123code"] = lv123code,
                        ["lv3"] = lv3,
                        ["ex_gubun"] = exGubun
                    });
                }
            }

            // 3) 응답자 시트 파싱 (HR_Sample3.xlsx 4행 헤더 기준: key | 복수 | 원본내용 | 클리닝내용(번역) | 대분류 | 중분류 | 소분류(텍스트) | lv3(코드) | sentiment | 확인/미확인)
            var dataIn = new JArray();
            int rMaxRow = respSheet.Cells.MaxDataRow;
            int rMaxCol = respSheet.Cells.MaxDataColumn;
            if (rMaxRow >= 0 && rMaxCol >= 0)
            {
                int rHeaderRow = 3;
                int colKey = -1, colCid = -1, colOrigin = -1, colFin = -1, colRespLv3 = -1, colRespCode = -1, colSent = -1, colRecheck = -1;

                for (int r = 0; r <= Math.Min(6, rMaxRow); r++)
                {
                    int foundKey = -1, foundOrigin = -1;
                    for (int c = 0; c <= rMaxCol; c++)
                    {
                        string h = NormalizeExcelHeader(respSheet.Cells[r, c].StringValue);
                        if (h.Contains("key")) foundKey = c;
                        if (h.Contains("원본내용") || h.Contains("원본응답")) foundOrigin = c;
                    }
                    if (foundKey >= 0 || foundOrigin >= 0)
                    {
                        rHeaderRow = r;
                        for (int c = 0; c <= rMaxCol; c++)
                        {
                            string h = NormalizeExcelHeader(respSheet.Cells[r, c].StringValue);
                            if (colKey < 0 && (h.Contains("key") || h == "no")) colKey = c;
                            else if (h == "복수" || h == "cid" || h == "멀티") colCid = c;
                            else if (h.Contains("원본내용") || h.Contains("원본응답") || h == "answer_origin") colOrigin = c;
                            else if (h.Contains("클리닝") || h == "answer_fin") colFin = c;
                            else if (h == "소분류" || h == "보기명" || h == "소분류명") colRespLv3 = c;
                            else if (h == "lv3" || h == "소분류코드" || h == "lv123code") colRespCode = c;
                            else if (h == "sentiment" || h == "감성") colSent = c;
                            else if (h.Contains("확인") || h == "검증" || h == "recheckyn") colRecheck = c;
                        }
                        break;
                    }
                }

                if (colKey < 0) colKey = 0;
                if (colCid < 0) colCid = 1;
                if (colOrigin < 0) colOrigin = 2;
                if (colFin < 0) colFin = 3;
                if (colRespLv3 < 0) colRespLv3 = 6;
                if (colRespCode < 0) colRespCode = 7;
                if (colSent < 0) colSent = 8;
                if (colRecheck < 0) colRecheck = 9;

                for (int r = rHeaderRow + 1; r <= rMaxRow; r++)
                {
                    string CellStr(int col) => col >= 0 && col <= rMaxCol ? (respSheet.Cells[r, col].StringValue?.Trim() ?? "") : "";
                    string key = CellStr(colKey);
                    string cid = CellStr(colCid);
                    string answerOrigin = CellStr(colOrigin);
                    string answerFin = CellStr(colFin);
                    string lv3 = CellStr(colRespLv3);
                    string lv123code = CellStr(colRespCode);
                    string sentiment = CellStr(colSent);
                    string recheckyn = CellStr(colRecheck);

                    if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(answerOrigin) && string.IsNullOrEmpty(lv123code) && string.IsNullOrEmpty(lv3))
                        continue;

                    dataIn.Add(new JObject
                    {
                        ["key"] = key,
                        ["cid"] = cid,
                        ["answer_origin"] = answerOrigin,
                        ["answer_fin"] = answerFin,
                        ["lv3"] = lv3,
                        ["lv123code"] = lv123code,
                        ["sentiment"] = sentiment,
                        ["recheckyn"] = recheckyn
                    });
                }
            }

            return await ImportExcelJsonAsync(pn, qnum, dataIn, dataLb, ct);
        }

        /// <summary>
        /// 레거시 option_edit_api.aspx.cs (gb == "import_excel") 검증 규칙 및 DB/HData/Queue 일괄 반영
        /// </summary>
        public async Task<ExcelFullImportResult> ImportExcelJsonAsync(string pn, string qnum, JArray? dataIn, JArray? dataLb, CancellationToken ct = default)
        {
            var meta = await _repo.GetQuestionMetaAsync(pn, qnum, ct);
            string mergePn = !string.IsNullOrWhiteSpace(meta?.merge_pn) ? meta.merge_pn.Trim() : pn.Trim();
            string mergeQnum = !string.IsNullOrWhiteSpace(meta?.merge_qnum) ? meta.merge_qnum.Trim() : qnum.Trim();

            var errorList = new JArray();
            var resultLbArray = new JArray();
            var seenLbCodes = new HashSet<string>(StringComparer.Ordinal);
            string datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 기존 pn_lb의 lv321code 매핑 유지
            var existingLb = await _repo.GetPnLbAsync(mergePn, mergeQnum, ct);
            var existingLv321ByCode = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedLv321 = new HashSet<string>(StringComparer.Ordinal);
            foreach (var oldObj in ParseArray(existingLb?.result_json).OfType<JObject>())
            {
                string oc = Str(oldObj, "lv123code") ?? "";
                string ok321 = Str(oldObj, "lv321code") ?? "";
                if (!string.IsNullOrEmpty(ok321)) usedLv321.Add(ok321);
                if (!string.IsNullOrEmpty(oc) && !string.IsNullOrEmpty(ok321) && !existingLv321ByCode.ContainsKey(oc))
                    existingLv321ByCode[oc] = ok321;
            }

            // 1) 보기항목(data_lb) 검증 및 정규화 (레거시 L220-L274 + 코드 중복/빈값 체크)
            if (dataLb != null)
            {
                foreach (var item in dataLb.OfType<JObject>())
                {
                    string lv1 = CleanText(item["lv1"]?.ToString());
                    string lv1code = (item["lv1code"]?.ToString() ?? "").Trim();
                    string lv2 = CleanText(item["lv2"]?.ToString());
                    string lv2code = (item["lv2code"]?.ToString() ?? "").Trim();
                    string lv3 = CleanText(item["lv3"]?.ToString());
                    string lv3code = (item["lv123code"]?.ToString() ?? "").Trim();
                    string rawGubun = (item["ex_gubun"]?.ToString() ?? "").Trim();

                    if (string.IsNullOrEmpty(lv3code) && string.IsNullOrEmpty(lv3) && string.IsNullOrEmpty(lv1) && string.IsNullOrEmpty(lv2))
                        continue;

                    if (string.IsNullOrEmpty(lv3code) || string.IsNullOrEmpty(lv3))
                    {
                        errorList.Add(new JObject
                        {
                            ["error_gb"] = "보기",
                            ["lv3"] = lv3,
                            ["lv123code"] = lv3code,
                            ["error_msg"] = string.IsNullOrEmpty(lv3code) ? "보기코드 빈값" : "소분류 빈값"
                        });
                        continue;
                    }

                    if (!seenLbCodes.Add(lv3code))
                    {
                        errorList.Add(new JObject
                        {
                            ["error_gb"] = "보기",
                            ["lv3"] = lv3,
                            ["lv123code"] = lv3code,
                            ["error_msg"] = "보기코드중복"
                        });
                        continue;
                    }

                    bool isLock = rawGubun == "survey" || rawGubun.Contains("수정불가");
                    string gubunName = (rawGubun == "survey" || rawGubun.StartsWith("설문"))
                        ? "설문 보기"
                        : (rawGubun.StartsWith("트래킹") ? "트래킹 보기" : "자동분석 보기");
                    string exGubun = isLock ? "설문(수정불가)" : "분석(수정가능)";

                    if (!existingLv321ByCode.TryGetValue(lv3code, out var lv321) || string.IsNullOrEmpty(lv321))
                    {
                        do
                        {
                            lv321 = Guid.NewGuid().ToString("N")[..10];
                        } while (!usedLv321.Add(lv321));
                    }

                    resultLbArray.Add(new JObject
                    {
                        ["qnum"] = mergeQnum,
                        ["lv1code"] = lv1code,
                        ["lv1"] = lv1,
                        ["lv2code"] = lv2code,
                        ["lv2"] = lv2,
                        ["lv123code"] = lv3code,
                        ["lv321code"] = lv321,
                        ["lv3"] = lv3,
                        ["ex_gubun"] = exGubun,
                        ["gubun_name"] = gubunName,
                        ["is_lock"] = isLock,
                        ["is_editable"] = isLock ? "불가" : "가능"
                    });
                }
            }

            // 2) 응답자 시트(data_in) 검증 및 조립 (레거시 L276-L397 동일)
            var resultInArray = new JArray();
            var seenKeyCodeSet = new HashSet<string>(StringComparer.Ordinal);
            var cidCountByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            if (dataIn != null)
            {
                foreach (var item in dataIn.OfType<JObject>())
                {
                    string tagKey = (item["key"]?.ToString() ?? item["fixed_key"]?.ToString() ?? "").Trim();
                    string tagOrigin = (item["answer_origin"]?.ToString() ?? "").Trim();
                    string tagFin = (item["answer_fin"]?.ToString() ?? "").Trim();
                    string tagLv3 = CleanText(item["lv3"]?.ToString());
                    string tagCode = (item["lv123code"]?.ToString() ?? "").Trim();
                    string tagSentiment = (item["sentiment"]?.ToString() ?? "").Trim();
                    string rawRecheck = (item["recheckyn"]?.ToString() ?? "").Trim();
                    string tagRecheck = (rawRecheck == "완료" || rawRecheck.Equals("y", StringComparison.OrdinalIgnoreCase) || rawRecheck == "1" || rawRecheck == "확인") ? "y" : "";

                    if (string.IsNullOrEmpty(tagKey) && string.IsNullOrEmpty(tagOrigin) && string.IsNullOrEmpty(tagCode) && string.IsNullOrEmpty(tagLv3))
                        continue;

                    int nextCid = cidCountByKey.TryGetValue(tagKey, out int curCnt) ? curCnt + 1 : 1;
                    string keyCodePair = $"{tagKey}__{tagCode}";
                    bool isDupKeyCode = !string.IsNullOrEmpty(tagKey) && !string.IsNullOrEmpty(tagCode) && !seenKeyCodeSet.Add(keyCodePair);

                    if (!isDupKeyCode)
                    {
                        cidCountByKey[tagKey] = nextCid;
                        resultInArray.Add(new JObject
                        {
                            ["fixed_key"] = tagKey,
                            ["cid"] = nextCid,
                            ["lv3"] = tagLv3,
                            ["lv123code"] = tagCode,
                            ["sentiment"] = tagSentiment,
                            ["recheckyn"] = tagRecheck,
                            ["answer_fin"] = tagFin,
                            ["upload_date"] = datetime
                        });
                    }
                    else
                    {
                        errorList.Add(new JObject
                        {
                            ["error_gb"] = "응답자",
                            ["fixed_key"] = tagKey,
                            ["cid"] = nextCid.ToString(),
                            ["answer_origin"] = tagOrigin,
                            ["answer_fin"] = tagFin,
                            ["lv3"] = tagLv3,
                            ["lv123code"] = tagCode,
                            ["sentiment"] = tagSentiment,
                            ["error_msg"] = "fixed_key 중복코드 & lv3code"
                        });
                    }

                    if (string.IsNullOrEmpty(tagCode))
                    {
                        errorList.Add(new JObject
                        {
                            ["error_gb"] = "응답자",
                            ["fixed_key"] = tagKey,
                            ["cid"] = nextCid.ToString(),
                            ["answer_origin"] = tagOrigin,
                            ["answer_fin"] = tagFin,
                            ["lv3"] = tagLv3,
                            ["lv123code"] = tagCode,
                            ["sentiment"] = tagSentiment,
                            ["error_msg"] = "미코딩"
                        });
                    }
                    else if (!seenLbCodes.Contains(tagCode))
                    {
                        errorList.Add(new JObject
                        {
                            ["error_gb"] = "응답자",
                            ["fixed_key"] = tagKey,
                            ["cid"] = nextCid.ToString(),
                            ["answer_origin"] = tagOrigin,
                            ["answer_fin"] = tagFin,
                            ["lv3"] = tagLv3,
                            ["lv123code"] = tagCode,
                            ["sentiment"] = tagSentiment,
                            ["error_msg"] = "보기항목 미포함 코딩"
                        });
                    }
                }
            }

            if (errorList.Count > 0)
            {
                return new ExcelFullImportResult
                {
                    ok = false,
                    success = "761",
                    message = "엑셀 업로드 검증 오류가 발견되었습니다.",
                    errors = errorList
                };
            }

            // 3) 오류가 없을 때만 pn_lb / pn_in / HData / Queue 일괄 반영 (레거시 L401-L423 동일)
            if (resultLbArray.Count > 0)
            {
                await _repo.SavePnLbAsync(mergePn, mergeQnum, resultLbArray.ToString(Newtonsoft.Json.Formatting.None), ct);
                try
                {
                    await _repo.SyncCodesToHDataAsync(mergePn, mergeQnum, resultLbArray, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ImportExcelJsonAsync] HData surveyon_labels 동기화 중 오류 pn={Pn} qnum={Qnum}", mergePn, mergeQnum);
                }
            }

            if (resultInArray.Count > 0)
            {
                string dupFlag = !string.IsNullOrWhiteSpace(meta?.duplicate_remove_date) ? "y" : "n";
                await SaveResponsesAsync(mergePn, mergeQnum, dupFlag, resultInArray, ct);
            }

            return new ExcelFullImportResult
            {
                ok = true,
                success = "777",
                message = "엑셀 업로드가 완료되었습니다.",
                codeCount = resultLbArray.Count,
                responseCount = resultInArray.Count
            };
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
            var sheet = wb.Worksheets["보기항목"] ?? wb.Worksheets["보기목록"] ?? wb.Worksheets[0];

            int maxCol = sheet.Cells.MaxDataColumn;
            int maxRow = sheet.Cells.MaxDataRow;
            if (maxRow < 1)
            {
                result.ok = false;
                return result;
            }

            // 헤더 행 탐색 (HR_Sample3.xlsx [보기항목] 시트: no | 문번호 | lv1 | 대분류 | lv2 | 중분류 | lv3(코드) | 소분류(보기명) | 보기유형 | 집계현황 완벽 호환)
            int headerRow = 0;
            int lv3Col = -1, codeCol = -1, lv1Col = -1, lv1codeCol = -1, lv2Col = -1, lv2codeCol = -1, typeCol = -1, lockCol = -1;

            for (int r = 0; r <= Math.Min(3, maxRow); r++)
            {
                var headersAtRow = new List<(int c, string h)>();
                for (int c = 0; c <= maxCol; c++)
                {
                    headersAtRow.Add((c, NormalizeExcelHeader(sheet.Cells[r, c].StringValue)));
                }

                bool isLegacySampleFormat = headersAtRow.Any(x => x.h == "소분류") && headersAtRow.Any(x => x.h == "lv3");
                foreach (var (c, h) in headersAtRow)
                {
                    if (isLegacySampleFormat)
                    {
                        if (h == "lv1") lv1codeCol = c;
                        else if (h == "대분류") lv1Col = c;
                        else if (h == "lv2") lv2codeCol = c;
                        else if (h == "중분류") lv2Col = c;
                        else if (h == "lv3") codeCol = c;
                        else if (h == "소분류") lv3Col = c;
                        else if (h.Contains("보기유형")) typeCol = c;
                    }
                    else
                    {
                        if (h == "소분류" || h.Contains("소분류명") || h.Contains("보기명") || h == "라벨" || h == "label") lv3Col = c;
                        else if (h.Contains("소분류코드") || h == "lv123code" || h == "코드" || h == "code" || (h == "lv3" && lv3Col >= 0)) codeCol = c;
                        else if (h.Contains("대분류코드") || h == "lv1code" || h == "lv1") lv1codeCol = c;
                        else if (h.Contains("대분류명") || (h.Contains("대분류") && !h.Contains("코드"))) lv1Col = c;
                        else if (h.Contains("중분류코드") || h == "lv2code" || h == "lv2") lv2codeCol = c;
                        else if (h.Contains("중분류명") || (h.Contains("중분류") && !h.Contains("코드"))) lv2Col = c;
                        else if (h.Contains("유형") || h.Contains("보기유형") || h == "ex_gubun") typeCol = c;
                        else if (h.Contains("잠금") || h.Contains("수정여부") || h == "is_lock") lockCol = c;
                    }
                }

                if (lv3Col >= 0 || codeCol >= 0)
                {
                    headerRow = r;
                    break;
                }
            }

            if (codeCol < 0) codeCol = 6;
            if (lv3Col < 0) lv3Col = 7;
            if (lv1codeCol < 0) lv1codeCol = 2;
            if (lv1Col < 0) lv1Col = 3;
            if (lv2codeCol < 0) lv2codeCol = 4;
            if (lv2Col < 0) lv2Col = 5;

            for (int r = headerRow + 1; r <= maxRow; r++)
            {
                string code = (codeCol >= 0 && codeCol <= maxCol ? sheet.Cells[r, codeCol].StringValue : "")?.Trim() ?? "";
                string lv3 = (lv3Col >= 0 && lv3Col <= maxCol ? sheet.Cells[r, lv3Col].StringValue : "")?.Trim() ?? "";
                string lv1 = (lv1Col >= 0 && lv1Col <= maxCol ? sheet.Cells[r, lv1Col].StringValue : "")?.Trim() ?? "";
                string lv1code = (lv1codeCol >= 0 && lv1codeCol <= maxCol ? sheet.Cells[r, lv1codeCol].StringValue : "")?.Trim() ?? "";
                string lv2 = (lv2Col >= 0 && lv2Col <= maxCol ? sheet.Cells[r, lv2Col].StringValue : "")?.Trim() ?? "";
                string lv2code = (lv2codeCol >= 0 && lv2codeCol <= maxCol ? sheet.Cells[r, lv2codeCol].StringValue : "")?.Trim() ?? "";
                string ex_gubun = (typeCol >= 0 && typeCol <= maxCol ? sheet.Cells[r, typeCol].StringValue : "")?.Trim() ?? "분석(수정가능)";
                string lockStr = (lockCol >= 0 && lockCol <= maxCol ? sheet.Cells[r, lockCol].StringValue : "")?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(lv3))
                    continue; // 빈 행 스킵

                bool is_lock = ex_gubun == "survey"
                            || ex_gubun.Contains("수정불가")
                            || lockStr.Contains("불가")
                            || lockStr == "1"
                            || lockStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                var item = new ExcelCodePreviewItem
                {
                    rowNum = r,
                    lv1 = lv1,
                    lv1code = lv1code,
                    lv2 = lv2,
                    lv2code = lv2code,
                    lv3 = lv3,
                    lv123code = code,
                    ex_gubun = string.IsNullOrEmpty(ex_gubun) ? "분석(수정가능)" : ex_gubun,
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
