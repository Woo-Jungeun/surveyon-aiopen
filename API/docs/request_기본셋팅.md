# 테스트 Request 기본 설정

> 이 파일에 정의된 값이 각 API 호출 시 누락된 필드에 자동으로 입력됩니다.
> 실제 값 변경 시 `appsettings.json` → `TestDefaults` 섹션을 수정하세요.

---

## 공통 기본값

| 키 | 값 | 설명 |
|----|----|------|
| `user` | `syhong` | 사용자 ID |
| `pn` | `q251708` | 프로젝트 번호 |
| `parquet_file_guid` | `33c0cebd-15af-4034-be02-f4be161d3f8f` | Parquet 파일 GUID |
| `page_id` | `04397998-3035-4532-a1e6-9acb9a21ba84` | 페이지 ID (AiSummary/AiChatbot) |
| `model` | `llm-gpt-oss-120b` | LLM 모델 |
| `k` | `3` | K-Means 클러스터 수 (AiAnalysis) |

---

## 컨트롤러별 Request 예시

### POST /AiAnalysis/segment

```json
{
  "user":              "syhong",
  "pn":                "q251708",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "model":             "llm-gpt-oss-120b",
  "k":                 3
}
```

> **자동 채움**: 빈 요청 `{}` 으로도 동작

---

### POST /AiAssociation/analyze

```json
{
  "user":              "syhong",
  "pn":                "q251708",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "min_support":       0.05,
  "min_confidence":    0.5,
  "min_lift":          1.0
}
```

> **자동 채움**: `user`, `pn`, `parquet_file_guid` 누락 시 자동 입력

---

### POST /AiChatbot/session

```json
{
  "user":              "syhong",
  "project_num":       "q251708",
  "page_id":           "04397998-3035-4532-a1e6-9acb9a21ba84",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "model":             "llm-gpt-oss-120b"
}
```

---

### POST /AiChatbot/chat

```json
{
  "session_id": "(세션 생성 후 반환된 값)",
  "message":    "성별과 통신사 교차분석 해줘"
}
```

---

### POST /AiChatbot/recommend

```json
{
  "session_id":        "(세션 생성 후 반환된 값)",
  "force_regenerate":  false
}
```

---

### POST /AiSummary

```json
{
  "user":        "syhong",
  "project_num": "q251708",
  "page_id":     "04397998-3035-4532-a1e6-9acb9a21ba84",
  "model":       "llm-gpt-oss-120b",
  "result_json": { ... }
}
```

> **자동 채움**: `user`, `project_num`, `page_id`, `model` 누락 시 자동 입력

---

## 값 변경 방법

`appsettings.json`의 `TestDefaults` 섹션을 수정하면 모든 컨트롤러에 일괄 반영됩니다:

```json
"TestDefaults": {
  "user":              "syhong",
  "pn":                "q251708",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "page_id":           "04397998-3035-4532-a1e6-9acb9a21ba84",
  "model":             "llm-gpt-oss-120b",
  "k":                 3
}
```

  "pn":                "q260325",
  "parquet_file_guid": "05cd6472-8633-4e4e-8773-2ea01979159f",
  "page_id":           "1aadc213-0e02-4ca2-bf3c-f5b3d9ff4f06",


> ⚠️ 이 설정은 테스트 편의용입니다. 운영 환경에서는 반드시 실제 값을 Request에 명시해야 합니다.
