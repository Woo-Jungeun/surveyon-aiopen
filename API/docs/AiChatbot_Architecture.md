# AiChatbot 아키텍처 문서

> [!IMPORTANT]
> **이 문서는 변경사항 발생 시 반드시 먼저 업데이트하고, 사용자 검토 후 코드를 구현합니다.**

---

## 서비스 개요

의사결정권자가 복잡한 교차분석표를 직접 보지 않고 자연어로 질문하면 데이터 기반 답변을 즉각 제공하는 **대화형 데이터 분석 비서**.

---

## 엔드포인트

| Method | Route | 설명 |
|--------|-------|------|
| POST | `/AiChatbot/session` | 세션 생성 (result_json 등록) |
| POST | `/AiChatbot/chat` | 자연어 질문 → LLM 답변 |
| POST | `/AiChatbot/feedback` | 👍/👎 피드백 등록 |
| GET  | `/AiChatbot/history/{session_id}` | 대화 이력 조회 |

---

## 처리 흐름

```
① 세션 생성 (POST /session)
   result_json → BuildCrosstabContext() → context_text
   → chat_sessions INSERT → session_id 반환

② 질문 (POST /chat)
   → 만료 메시지 자동 삭제 (비동기, MessageRetentionDays 기준)
   → chat_sessions에서 context_text, model 조회
   → chat_messages에서 대화 히스토리 조회 (멀티턴)
   → few_shot_candidates에서 우수 사례 FewShotMaxCount건 조회
   → 프롬프트 조립:
       [System + Context + Few-Shot]
       + 이전 대화 히스토리
       + 현재 질문
   → LLM 호출 (CallLlmWithHistoryAsync)
   → 응답 파싱: answer_text + chart_data
   → chat_messages INSERT (user / assistant 쌍)
   → 반환: message_id, answer_text, chart_data

③ 피드백 (POST /feedback)
   → feedback_logs UPSERT
   → rating == "good":
       → ExtractKeywords(question_text)
       → few_shot_candidates 전체 조회
       → JaccardSimilarity 비교
       → 유사도 < SimilarityThreshold → INSERT (영구 보존)
       → 유사도 >= SimilarityThreshold → 중복 스킵

④ 이력 조회 (GET /history/{session_id})
   → chat_messages 시간순 반환
```

---

## DB 테이블 (HAi DB)

| 테이블 | 보존 기간 | 설명 |
|--------|-----------|------|
| `chat_sessions` | 영구 | 세션 단위, context_text 포함 |
| `chat_messages` | 설정값 (기본 90일) | 사용자 질문 + AI 답변 |
| `feedback_logs` | 영구 | 👍👎 피드백 (cascade: message 삭제 시 같이 삭제) |
| `few_shot_candidates` | 영구 | 좋아요 우수 사례 (Dynamic Few-Shot 학습용) |

### chat_sessions
```sql
session_id   UUID PK   project_num  page_id  model  context_text  created_at  updated_at
```

### chat_messages
```sql
message_id   UUID PK   session_id(FK)  role('user'|'assistant')  content  chart_data(JSONB)  created_at
인덱스: (session_id, created_at), (created_at) -- 만료 삭제 성능
```

### feedback_logs
```sql
feedback_id  SERIAL PK   message_id(FK, UNIQUE)  rating('good'|'bad')  reason  created_at
```

### few_shot_candidates
```sql
candidate_id  SERIAL PK   project_num  question_text  answer_text  keywords(TEXT[])  created_at
```

---

## appsettings.json 설정

```json
"AiChatbot": {
  "MessageRetentionDays": 90,      // chat_messages 보존 기간
  "FewShotMaxCount": 2,            // 프롬프트 주입 우수 사례 수
  "SimilarityThreshold": 0.85      // Jaccard 유사도 중복 기준
}
```

---

## MainRepository 메서드

| 메서드 | 테이블 | 설명 |
|--------|--------|------|
| `ChatSessionCreateAsync` | chat_sessions | INSERT, UUID 반환 |
| `ChatSessionGetContextAsync` | chat_sessions | session_id → (context, model) |
| `ChatMessageInsertAsync` | chat_messages | INSERT, UUID 반환 |
| `ChatMessageGetHistoryAsync` | chat_messages | session 메시지 목록 |
| `ChatMessagePurgeExpiredAsync` | chat_messages | 만료 메시지 DELETE |
| `FeedbackUpsertAsync` | feedback_logs | INSERT/UPDATE |
| `FewShotGetCandidatesAsync` | few_shot_candidates | 최근 N건 조회 |
| `FewShotInsertAsync` | few_shot_candidates | 우수 사례 영구 INSERT |

---

## 유사도 중복 방지 (Jaccard Similarity)

```
신규 질문 키워드 추출 (ExtractKeywords)
  → few_shot_candidates 전체 keywords 비교
  → Jaccard = |교집합| / |합집합|
  → Jaccard >= SimilarityThreshold (0.85) → 중복, 미저장
  → Jaccard <  SimilarityThreshold        → 새 사례 INSERT
```

---

## LLM 응답 형식

```json
{
  "answer_text": "20대 여성의 긍정 응답률은 70.3%로 전체 평균(45.1%)보다 25.2%p 높습니다.",
  "chart_data": {
    "type": "bar",
    "labels": ["20대", "30대", "40대"],
    "data":   [70.3, 45.2, 38.1]
  }
}
```
`chart_data`는 LLM이 시각화 데이터를 제공할 수 없을 때 `null` 반환.

---

## 변경 이력

| 날짜 | 버전 | 내용 |
|------|------|------|
| 2026-04-14 | v1.0 | 최초 구현 (result_json 기반 세션, LLM 전용 응답) |
| 2026-04-22 | v1.1 | 보기 레이블 매핑 개선 (`BuildTextFromParquetRows` - `ResolveLabel` 헬퍼 추가) |
| 2026-04-22 | v2.0 | 동적 분석 엔진 추가 (`ChatAnalyticsService`, DB mapdata 연동, 5가지 분석 유형) |
| 2026-04-23 | v2.1 | 교차분석 테이블·그래프 시각화 (`ChartMeta`, `CrossTabChart`, `GroupedBar`) |
| 2026-04-23 | v2.2 | 세션 재사용 (`ChatSessionFindAsync` — pn+page_id+guid 기준 기존 세션 재사용) |
| 2026-04-23 | v2.3 | 자연어 문항 매핑 (`enrichedVars` — "q10(성별)" 형식으로 LLM 힌트 확장) |
| 2026-04-23 | v2.4 | 교차분석 진단 로그 + `GetIntVal` 대소문자 무시 fallback 추가 |
| 2026-04-27 | v2.5 | 이력 chart_data JSONB 파싱 버그 수정 (`raw is string` → `raw?.ToString()`); 세션 생성 시 이전 이력 최신 20개 채팅 탭 자동 로드 |
| 2026-04-27 | v3.0 | 1:N 다변수 교차분석 지원 (최대 4개 변수, CartesianProduct 알고리즘); 결과창 width 92%; 교차표 sticky 첫행/첫열; 헤더 자동개행 |
| 2026-04-27 | v3.1 | 3모드 교차분석 체계 (`IntentSystemPrompt` mode 필드; `DispatchCrosstab`; `RunBannerCrosstab`; `RunCartesianCrosstab`; `BannerTable` 컴포넌트 신규) |
| 2026-04-27 | v3.2 | CrossTabChart 계층형 다행 헤더 (상위 변수 colspan 병합, 최하위 보기 개별 셀) |
| 2026-04-27 | v3.3 | 4타입 교차분석(`stub_cart` 모드 신규, `RunStubCartBanner`; 배너 그룹별 GroupedBar 시각화; `BannerTable` bars 다중 그래프) |
| 2026-04-27 | v3.4 | `IntentSystemPrompt` 스터브/배너 유연화 — row_var 첫변수 고정 제거, 키워드 감지 규칙 + 5종 케이스 예시 추가 |
| 2026-04-27 | v3.5 | 문항 매핑 다양성 지원 (`AiChatbotController` enrichedVars를 `q10(SQ2. 성별)` 구조로 보강하여 문항 번호와 레이블 혼합 인식 지원) |
| 2026-04-27 | v3.6 | 교차분석 다중응답(복수응답) 지원 — `GetIntVal` 대신 `GetIntVals`/`Contains`/`SelectMany`를 활용하여 `JArray` 배열 데이터의 중복 가산 교차표 생성 완비 |

---

## v1.1 변경: 보기 레이블 매핑 (2026-04-22)

### 문제
`BuildTextFromParquetRows`에서 보기 코드(숫자)만 출력되던 문제.
```
[통신사 (q10)]
  값=1: 968명 (49.0%)   ← 보기 레이블 없음
```

### 해결
`AiChatbotController.cs`에 `ResolveLabel()` 헬퍼 추가.
- `MapDataContainer.ColIndex[colName]` → `ColumnMeta` 조회
- `MapDataContainer.LabelIndex[meta.Id]` → 코드→레이블 변환

### 결과
```
[통신사 (q10)]
  (1)SKT: 968명 (49.0%)   ← 보기 레이블 정상 표시
  (2)KT: 512명 (25.9%)
```

---

## v2.0 변경: 동적 분석 엔진 (2026-04-22)

### 배경 및 목적

기존 방식(정적 컨텍스트)의 한계:
- 세션 생성 시 사전 집계된 텍스트만 LLM에 주입
- `"q50=1 응답자 중 q20=2인 비율"` 같은 교차/복합 질의 불가

신규 방식(동적 분석):
- **Rawdata**: Parquet 파일 (응답자별 row, pid 기준)
- **Mapdata**: HData DB (`surveyon_variables` + `surveyon_labels`)
- **C# 집계 엔진**: 실시간 계산 → LLM 해석 요청

---

### 아키텍처 다이어그램

```
POST /AiChatbot/session
  ├── parquet_file_guid 있음
  │     └── BuildContextFromParquetAsync()
  │           ├── HData DB: map_data 조회 (GetSurveyMapDataAsync)
  │           ├── Parquet 로드 (LoadParquetAsync)
  │           └── META 헤더 + 분포 요약 → context_text 저장
  │                "[META] pn=q251708 guid=33c0cebd-...\n[DATA]\n..."
  └── result_json 있음 → 기존 방식 (하위호환 유지)

POST /AiChatbot/chat
  ├── context_text에서 META 헤더 파싱 (pn, guid 추출)
  │
  ├── [동적 분석 경로] ← parquet_file_guid 있을 때
  │     ├── GetVariablesWithLabelsAsync(pn)
  │     │     surveyon_variables + surveyon_labels JOIN
  │     ├── LoadParquetAsync(guid)  → Parquet rawdata
  │     ├── DetectIntentAsync(question)  → LLM 의도 파악
  │     │     → { type, vars, pid, filter }
  │     ├── ExecuteAnalysis(intent, rows, varMap)  → C# 집계
  │     └── LLM 해석 요청 → answer_text + chart_data
  │
  └── [LLM fallback 경로] ← 동적 분석 실패 or result_json 방식
        BuildSystemPrompt(contextText)
        → CallLlmWithHistoryAsync → ParseLlmResponse
```

---

### 지원 분석 유형 (ChatAnalyticsService)

| 타입 | 설명 | 예시 질문 |
|------|------|-----------|
| `frequency` | 단일 문항 빈도/비율 | "q50 응답 분포 보여줘" |
| `mean_std` | 평균/표준편차/최솟값/최댓값 | "q30 점수 평균은?" |
| `crosstab` | 두 문항 교차 집계 | "q10별 q20 분포" |
| `pid_lookup` | 특정 응답자 전체 답변 조회 | "pid=106654 응답 내역" |
| `multi_freq` | 복수 문항 동시 빈도 | "q10, q20, q50 모두 보여줘" |

**filter 지원**: `"q10=1인 응답자 중 q20 분포"` → `{ type: crosstab, vars: [q10, q20], filter: { var: q10, code: 1 } }`

---

### 신규 파일 목록

| 파일 | 역할 |
|------|------|
| `Services/ChatAnalyticsService.cs` | 동적 분석 엔진 + `IChatAnalyticsService` 인터페이스 |

### 수정 파일 목록

| 파일 | 변경 내용 |
|------|-----------|
| `Controllers/AiChatbotController.cs` | `IChatAnalyticsService` DI, META 헤더 저장, Chat() 동적분석 분기 |
| `Repositories/IMainRepository.cs` | `GetVariablesWithLabelsAsync` 선언 추가 |
| `Repositories/MainRepository.cs` | surveyon_variables + labels JOIN 조회 구현 (HData DB) |
| `Program.cs` | `IChatAnalyticsService` 스코프 등록 |

---

### DB 접근 확장

| DB | 테이블 | 목적 | 유형 |
|----|--------|------|------|
| HData (`Postgreshdata`) | `surveyon_variables` | 변수명·레이블·유형 조회 | SELECT 전용 |
| HData (`Postgreshdata`) | `surveyon_labels` | 보기코드→레이블 매핑 | SELECT 전용 |

```sql
-- GetVariablesWithLabelsAsync 쿼리
SELECT v.id, v.s_question_variable, v.c_question_variable, v.label, v.type,
       l.code, l.label AS code_label
FROM   public.surveyon_variables v
LEFT JOIN public.surveyon_labels l ON l.variable_id = v.id
WHERE  v.pn = @pn
ORDER BY v.id, l.code;
```

---

### context_text META 헤더 포맷

```
[META] pn=q251708 guid=33c0cebd-15af-4034-be02-f4be161d3f8f
[DATA]
[통신사 (q10)]
  (1)SKT: 968명 (49.0%)
  ...
```

`Chat()` 엔드포인트는 `context_text.StartsWith("[META]")` 여부로 동적 분석 가능 여부를 판단.

---

### LLM 의도 파악 프롬프트 (DetectIntentAsync)

```json
{
  "type": "frequency|mean_std|crosstab|pid_lookup|multi_freq|unknown",
  "vars": ["q10", "q20"],
  "pid": "pid값(pid_lookup일 때만)",
  "filter": { "var": "q10", "code": 1 }
}
```

- `unknown` 반환 시 → 기존 LLM 경로로 자동 fallback
- LLM 호출 실패 시 → `LogWarning` 후 자동 fallback

---

### 테스트 가이드 (v2.0)

```
테스트 데이터:
  pn: q251708
  parquet_file_guid: 33c0cebd-15af-4034-be02-f4be161d3f8f
  page_id: 04397998-3035-4532-a1e6-9acb9a21ba84

1. 세션 생성
   POST /AiChatbot/session
   { "project_num":"q251708", "page_id":"04397998-...", "parquet_file_guid":"33c0cebd-..." }

2. 빈도 분석
   POST /AiChatbot/chat { "session_id":"...", "message":"q50 응답 분포 보여줘" }
   → frequency 분기 → (1)SKT: 968명 형태 반환 기대

3. 교차 분석
   POST /AiChatbot/chat { "message":"q10=1인 응답자 중 q20 분포" }
   → crosstab + filter 분기 기대

4. 특정 응답자 조회
   POST /AiChatbot/chat { "message":"pid=106654 응답 내역 알려줘" }
   → pid_lookup 분기 기대

5. 기존 방식 호환 확인 (result_json 방식 세션)
   → LLM fallback 경로 동작 확인
```

---

## Rollback 방법 (v2.0)

1. `Services/ChatAnalyticsService.cs` 삭제
2. `Program.cs` — `IChatAnalyticsService` 등록 1줄 제거
3. `IMainRepository.cs` — `GetVariablesWithLabelsAsync` 선언 1줄 제거
4. `MainRepository.cs` — 메서드 제거
5. `AiChatbotController.cs` — DI 필드/생성자/Chat() 분기 원복, `BuildContextFromParquetAsync` META 헤더 제거

> DB 변경 없음 → DB Rollback 불필요

---

## 참고: 테스트 데이터

| 항목 | 값 |
|------|-----|
| pn | `q251708` |
| page_id | `04397998-3035-4532-a1e6-9acb9a21ba84` |
| parquet_file_guid | `33c0cebd-15af-4034-be02-f4be161d3f8f` |

---

## v2.1 변경: 교차분석 테이블·그래프 시각화 (2026-04-23)

### 변경 배경
`crosstab` 분석 결과가 텍스트로만 반환되어 UI에서 표 형태로 확인 불가.

### 변경 내용

| 파일 | 변경 |
|------|------|
| `Services/ChatAnalyticsService.cs` | `AnalysisResult`에 `JToken? ChartMeta` 필드 추가. `RunCrosstab()`에서 headers/rows/pcts/colTotals/bar 구조 생성 |
| `Controllers/AiChatbotController.cs` | `ar.ChartMeta != null`이면 직접 `chartData`로 사용 (3줄) |
| `src/components/chat/ChatChart.jsx` | `type === "table"` 분기 → `CrossTabChart`(테이블) + `GroupedBar`(그룹드 바차트) |
| `src/index.css` | `.chat-table`, `.chat-crosstab`, `.chat-grouped-bar` 스타일 추가 |

### chart_data 확장 스키마 (`type: "table"`)

```json
{
  "type": "table",
  "rowLabel": "통신사",
  "colLabel": "성별",
  "headers": ["구분", "(1)남성", "(2)여성", "계"],
  "rows": [
    { "label": "(1)SKT", "cells": [512, 456, 968], "pcts": [52.9, 47.1, 100.0] }
  ],
  "colTotals": [955, 1024, 1979],
  "bar": {
    "groups": ["(1)SKT", "(2)KT", "(3)LG U+"],
    "series": [
      { "name": "(1)남성", "data": [52.9, 38.7, 49.1] },
      { "name": "(2)여성", "data": [47.1, 61.3, 50.9] }
    ]
  }
}
```

### UI 결과
챗봇 버블 내에 **행×열 퍼센트 테이블 + 그룹드 바차트** 인라인 표시.

---

## v2.2 변경: 세션 재사용 (2026-04-23)

### 변경 배경
`POST /AiChatbot/session`이 항상 INSERT → 동일 설문에서 매번 새 세션 생성, Parquet 반복 로드, 이력 단절 문제.

### 변경 내용

| 파일 | 변경 |
|------|------|
| `Repositories/IMainRepository.cs` | `ChatSessionFindAsync(pn, pageId, guid)` 선언 추가 |
| `Repositories/MainRepository.cs` | `chat_sessions` LIKE SELECT 구현 (HAi DB, READ-ONLY) |
| `Controllers/AiChatbotController.cs` | `CreateSession()`에 기존 세션 조회 분기 11줄 추가 |

### 변경된 흐름

```
POST /AiChatbot/session (parquet 방식)
  ↓
① DB 조회: pn + page_id + guid 일치하는 기존 세션 확인
  ↓
② 기존 세션 있음 → session_id 즉시 반환 (Parquet 로드 스킵, 이력 유지)
   기존 세션 없음 → 기존 흐름 (Parquet 로드 → INSERT → 새 session_id)
```

### 신규 쿼리 (`ChatSessionFindAsync`)

```sql
SELECT session_id::TEXT
FROM   public.chat_sessions
WHERE  project_num = @pn
  AND  page_id     = @pg
  AND  context_text LIKE '[META] pn=%guid={guid}%'
ORDER BY created_at DESC LIMIT 1;
```

### 주의 사항
- `context_text LIKE` 풀스캔 가능 → 현재 세션 수 적어 무관. 향후 `parquet_guid` 컬럼 분리 고려.
- `result_json` 방식 세션은 이 분기에 진입하지 않음 (하위 호환 유지).

---

## Rollback 방법 (v2.1 ~ v2.2)

### v2.1 (교차분석 시각화) Rollback
1. `ChatAnalyticsService.cs` — `ChartMeta` 필드 제거, `RunCrosstab()` 원복
2. `AiChatbotController.cs` — `ChartMeta` 분기 3줄 제거
3. `ChatChart.jsx` — `type==="table"` 분기 제거
4. `index.css` — `.chat-table`, `.chat-grouped-bar` 블록 제거

### v2.2 (세션 재사용) Rollback
1. `IMainRepository.cs` — `ChatSessionFindAsync` 선언 제거
2. `MainRepository.cs` — 메서드 제거
3. `AiChatbotController.cs` — 기존 세션 조회 분기 11줄 제거

> DB 변경 없음 → DB Rollback 불필요

---

## v2.3 변경: 자연어 문항 매핑 개선 (2026-04-23)

### 변경 배경
`"성별 & 주이용통신사 현황을 알려줘"` 입력 시 의도 파악 실패.
`DetectIntentAsync`에 변수코드(`q10, q20...`)만 전달하여 LLM이 자연어·레이블 기반 매핑 불가.

### 변경 내용

| 파일 | 변경 |
|------|------|
| `Services/ChatAnalyticsService.cs` | `IntentSystemPrompt`에 varHint 형식 설명 2문장 추가 |
| `Controllers/AiChatbotController.cs` | `varMap.Keys.ToList()` → `enrichedVars`("q10(성별)" 형식) 교체 (9줄) |

### Before / After

```
Before: "사용 가능한 변수: [q10, q20, q50]"
After:  "사용 가능한 변수: [q10(성별), q20(연령대), q50(주이용통신사)]"
```

### enrichedVars 구성 로직

```csharp
var enrichedVars = varMap.Values
    .Select(v => {
        string code = v.CQuestionVariable ?? v.SQuestionVariable ?? "";
        string lbl  = string.IsNullOrEmpty(v.Label) ? "" : $"({v.Label})";
        return $"{code}{lbl}";
    })
    .Where(x => !string.IsNullOrEmpty(x)).ToList();
```

### 기대 효과

| 입력 | Before | After |
|------|--------|-------|
| `"q10별 q20 분포"` | ✅ 정상 | ✅ 정상 유지 |
| `"성별 & 주이용통신사 현황"` | ❌ 의도 파악 실패 | ✅ `vars:["q10","q50"]` 매핑 |
| `"통신사별 연령대 교차분석"` | ❌ 실패 | ✅ `vars:["q50","q20"]` 매핑 |

### 위험도: 🟢 Low

### v2.3 Rollback
1. `ChatAnalyticsService.cs` — `IntentSystemPrompt` 추가 2줄 제거
2. `AiChatbotController.cs` — `enrichedVars` 빌드 블록 제거, `varMap.Keys.ToList()` 원복

---

## v2.4 변경: 교차분석 진단 로그 + GetIntVal 대소문자 무시 fallback (2026-04-23)

### 변경 배경

교차분석 질문 시 LLM fallback 응답("교차분석 데이터 없음")이 나오는 원인 파악을 위해
진단 로그 추가 + Parquet 컬럼명 대소문자 불일치 대응.

### 변경 내용

| 파일 | 변경 |
|------|------|
| `Services/ChatAnalyticsService.cs` | `GetIntVal` 2차 대소문자 무시 fallback 추가. `RunCrosstab` col1/col2/rowKeys/colKeys 진단 로그 |
| `Controllers/AiChatbotController.cs` | varMap keys vs objRows keys 비교 `Console.WriteLine` 2줄 추가 |

### 핵심: GetIntVal 대소문자 무시 fallback

```csharp
// 1차: 정확 일치
if (row.TryGetValue(col, out var val)) { ... }
// 2차: 대소문자 무시 (Parquet 컬럼명 vs varMap 코드 불일치 대응)
var fallbackKey = row.Keys.FirstOrDefault(k =>
    string.Equals(k, col, StringComparison.OrdinalIgnoreCase));
if (fallbackKey != null && row.TryGetValue(fallbackKey, out val)) { ... }
return -1;
```

### 진단 로그 확인 방법

`dotnet run` 재시작 후 교차분석 요청 시 콘솔에서 확인:
```
[Chatbot/Debug] varMap keys: q10, q20, q30_op1, q50...
[Chatbot/Debug] objRows[0] keys: q10, q20, q30_op1, q50...
[Analytics/Crosstab] col1=q50, col2=q20, rowKeys=5, colKeys=3
```

### 위험도: 🟡 Medium (Console.WriteLine 한시적 사용)

> Console.WriteLine은 진단 목적. 원인 확인 후 제거 예정.

### v2.4 Rollback
1. `ChatAnalyticsService.cs` — `GetIntVal` 원복, `Console.WriteLine` 2줄 제거
2. `AiChatbotController.cs` — `Console.WriteLine` 진단 로그 3줄 제거

---

## v2.5 변경 이력 (2026-04-23 오후)

### 변경 1: 교차분석 엔진 실패 원인 수정

| 항목 | 내용 |
|------|------|
| 파일 | `AiChatbotController.cs`, `ChatAnalyticsService.cs` |
| 원인 | varMap 키(SQ2) vs Parquet 컬럼명(q10) 불일치, `JValue(null)` filter 접근 예외 |
| 수정 | `effectiveVarMap` Parquet 컬럼 재색인, `JTokenType.Null` 체크 추가 |

**effectiveVarMap 재색인 흐름:**
```
objRows 컬럼명 → varMap 직접 매칭 or SQuestionVariable 매칭
→ effectiveVarMap 구성 → enrichedVars = "q10(성별), q20(통신사)"
→ LLM이 Parquet 실제 키로 vars 반환
→ RunCrosstab에서 col1/col2 정상 매핑
```

### 변경 2: 추천질문 강제 재생성 (force_regenerate)

| 항목 | 내용 |
|------|------|
| 파일 | `AiChatbotController.cs`, `AiChatPage.jsx` |
| 추가 파라미터 | `RecommendRequest.force_regenerate: bool = false` |
| 동작 | `false` → DB 캐시 반환, `true` → 캐시 무시 + LLM 재생성 |

**흐름:**
```
세션 생성 → callRecommend(force=false) 자동 호출 → DB 캐시 있으면 즉시 표시
🔄 추천질문 재생성 버튼 → force=true → LLM 재생성 → DB UPSERT
```

### 변경 3: answerStateCode=4 Parquet 필터

| 파일 | 위치 |
|------|------|
| `Services/SegmentationService.cs` | `RunSegmentationAsync()` rawRows 로드 직후 |
| `Services/AssociationRuleService.cs` | `RunAsync()` rawRows 로드 직후 |
| `Controllers/AiChatbotController.cs` | `Chat()` doubleRows, `BuildContextFromParquetAsync()` rows |

**필터 패턴:**
```csharp
rawRows = rawRows
    .Where(r => r.TryGetValue("answerStateCode", out var v) && (int)v == 4)
    .ToList();
```

> AiSummary는 Parquet 미사용이므로 변경 없음.

### v2.5 Rollback
1. `SegmentationService.cs` → rawRows 필터 4줄 제거
2. `AssociationRuleService.cs` → rawRows 필터 5줄 제거
3. `AiChatbotController.cs` → doubleRows/rows 필터 각 2~3줄 제거, force_regenerate 분기 제거, effectiveVarMap 재색인 블록 제거
4. `AiChatPage.jsx` → handleForceRecommend 제거, 버튼 원복

---

## v2.6 변경 이력 (2026-04-23 20:30)

### 변경: 테스트 Request 기본값 자동 입력 (TestDefaults)

| 파일 | 변경 |
|------|------|
| `appsettings.json` | `TestDefaults` 섹션 추가 |
| `docs/request_기본셋팅.md` | 전체 컨트롤러 Request 기본값 정의 문서 [NEW] |
| `Controllers/BaseController.cs` | `FillTestDefaults(IConfiguration, object)` 헬퍼 추가 |
| `Controllers/AiAnalysis/Association/Chatbot/SummaryController.cs` | 각 endpoint 수신 직후 `FillTestDefaults()` 1줄 호출 |

**동작 원리:**
```
빈 body {} 로 요청
→ FillTestDefaults() 실행
→ appsettings.json TestDefaults 섹션 읽기
→ DTO 필드가 비어있으면 기본값으로 채움
→ 기존 필드 값 있으면 유지 (덮어쓰지 않음)
```

**기본값:**
```json
"TestDefaults": {
  "user": "syhong", "pn": "q251708", "project_num": "q251708",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "page_id": "04397998-3035-4532-a1e6-9acb9a21ba84",
  "model": "llm-gpt-oss-120b", "k": 3
}
```

> ⚠️ **운영 배포 전 TestDefaults 섹션 제거 필수**

### v2.6 Rollback
1. `appsettings.json` → `TestDefaults` 섹션 전체 제거
2. `BaseController.cs` → `FillTestDefaults()` 메서드 제거
3. 각 컨트롤러 → `req ??= new(); FillTestDefaults();` 2줄 제거, `if (req == null || ...)` 원복
