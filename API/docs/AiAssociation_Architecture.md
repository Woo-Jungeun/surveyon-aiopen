# AiAssociation Controller — Association Rules Pipeline Architecture

## 개요

설문 응답 데이터 전체에서 문항 간 숨겨진 연관성을 자동 탐색하는 파이프라인.  
특정 목표 문항을 사전에 지정하지 않고, Association Rules 기반으로 유의미한 관계를 추출한다.  
결과는 네트워크 그래프용 nodes / edges JSON 형태로 반환되며, DB에 캐싱된다.

---

## 1. 엔드포인트

| Method | URL | 설명 |
|--------|-----|------|
| POST | `/AiAssociation/analyze` | 연관성 분석 실행 또는 캐시 결과 반환 |

---

## 2. 전체 처리 흐름

```
Client → POST /AiAssociation/analyze
         { "user", "pn", "parquet_file_guid", "min_support", "min_confidence", "min_lift", "max_variables" }
              │
              ▼
┌──────────────────────────────────────────────────────────┐
│  AiAssociationController                                 │
│                                                          │
│  Step 0: DB 캐시 조회 ──────────────────────────────    │
│    PostgresHAI (HAi DB)                                  │
│    public.association_results                            │
│    WHERE pn=@pn AND parquet_guid=@guid                   │
│    → 결과 있음: result_json 즉시 반환 (cached=true)      │
│    → 결과 없음: 아래 분석 진행                            │
│                                                          │
│  Step 1: map_data 조회 ─────────────────────────────    │
│    PostgresHData (HData DB)                              │
│    GetSurveyMapDataAsync(pn, guid)                       │
│    → MapDataContainer { variables, labels }              │
│                                                          │
│  Step 2: Parquet 로드 ──────────────────────────────    │
│    {BasePath}/{parquet_file_guid}.parquet                 │
│    → List<Dictionary<string, double>> rawRows            │
│    (SegmentationService.LoadParquetAsync 동일 패턴)       │
│                                                          │
│  Step 3: 범주형 컬럼 선택 ──────────────────────────    │
│    ColumnMeta.IsCategorical == true 인 변수만 선택       │
│    (single / multi / rank / custom → 포함)               │
│    (scale / open(문자) / open(숫자) → 제외)              │
│    cQuestionVariable → sQuestionVariable 순 매핑          │
│    max_variables 초과 시 상위 변수 선택                   │
│                                                          │
│  Step 4: 트랜잭션 변환 ─────────────────────────────    │
│    응답자(row) → Item 집합으로 변환                       │
│    Item 형식: "{colName}_{(int)code}"  예: "d1_1"        │
│    결측(0, NaN) → 제외                                   │
│                                                          │
│  Step 5: Support 계산 ──────────────────────────────    │
│    단일 Item 등장 횟수 / totalRows                        │
│    min_support 미달 Item 사전 제거                        │
│                                                          │
│  Step 6: 2-Itemset 공출현 계산 ─────────────────────    │
│    유효 Item 쌍 모두 순회 (2-itemset only)               │
│    동일 컬럼(문항) 내 쌍 제외                             │
│    co_count = 두 Item 동시 포함 응답자 수                │
│    support_AB = co_count / totalRows                     │
│    min_support 미달 쌍 제거                               │
│                                                          │
│  Step 7: 연관 규칙 생성 + 필터링 ──────────────────    │
│    A→B: confidence = support_AB / support_A              │
│         lift = confidence / support_B                    │
│    B→A: 동일 계산 (양방향 모두 평가)                     │
│    confidence >= min_confidence (기본 0.6)               │
│    lift >= min_lift (기본 1.2)                           │
│                                                          │
│  Step 8: JSON 직렬화 ──────────────────────────────     │
│    nodes: 규칙에 등장한 Item 목록                        │
│    edges: 필터 통과한 규칙 목록                           │
│                                                          │
│  Step 9: DB 저장 ───────────────────────────────────    │
│    PostgresHAI (HAi DB)                                  │
│    public.association_results UPSERT                     │
│    ON CONFLICT (pn, parquet_guid) DO UPDATE              │
└──────────────────────────────────────────────────────────┘
              │
              ▼
Response: { "success": "777", "cached": false, "resultjson": {
  "meta": { "pn": "...", "respondent_count": 1200, "rule_count": 42, ... },
  "nodes": [
    { "id": "d1_1", "label": "성별=남성", "support": 0.52, "size": 52 }
  ],
  "edges": [
    { "source": "d1_1", "target": "q3_2",
      "confidence": 0.71, "lift": 1.85, "weight": 1.85 }
  ]
}}
```

---

## 3. 입력 데이터 포맷

### POST /AiAssociation/analyze Request

```json
{
  "user":              "syhong",
  "pn":                "n0101_001",
  "parquet_file_guid": "xxxx-yyyy-zzzz",
  "min_support":       0.05,
  "min_confidence":    0.6,
  "min_lift":          1.2,
  "max_variables":     60
}
```

> `map_data_json` 필드는 Controller 내부에서 DB 조회 후 자동 주입. 요청에 포함 불필요.

---

## 4. 출력 데이터 포맷

### nodes 항목

```json
{
  "id":         "d1_1",
  "label":      "성별=남성",
  "variable":   "d1",
  "code":       "1",
  "code_label": "남성",
  "support":    0.52,
  "size":       52
}
```

| 필드 | 설명 |
|---|---|
| `id` | `{colName}_{code}` 형식 |
| `label` | `{variable_label}={code_label}` |
| `support` | 전체 응답자 대비 해당 항목 등장 비율 |
| `size` | `support × 100` (시각화 노드 크기 기준) |

### edges 항목

```json
{
  "source":     "d1_1",
  "target":     "q3_2",
  "support":    0.38,
  "confidence": 0.71,
  "lift":       1.85,
  "weight":     1.85,
  "direction":  "d1_1→q3_2"
}
```

| 필드 | 설명 |
|---|---|
| `confidence` | A가 응답된 경우 B도 응답될 확률 |
| `lift` | 우연 대비 연관 강도 (1.0 = 무관계) |
| `weight` | `lift` 값 = 간선 굵기 기준 |

---

## 5. 파일 구조

```
Controllers/
  AiAssociationController.cs    ← POST /AiAssociation/analyze

Services/
  AssociationRuleService.cs     ← Step 2~8 (Parquet + 규칙 계산 + JSON 직렬화)

Models/
  SegmentationModels.cs         ← AssociationRequest 모델 포함 (기존 파일 확장)

Repositories/
  IMainRepository.cs            ← AssociationResultGetAsync / AssociationResultUpsertAsync
  MainRepository.cs             ← 위 2개 메서드 구현
```

---

## 6. appsettings.json 설정

```json
"ParquetSettings": {
  "BasePath": "D:\\python\\Antigravity\\(260113)설문온_APICORE\\DataStorage"
}
```

- **BasePath**: Parquet 파일이 저장된 루트 경로 (SegmentationService와 공유)
- 추가 전용 설정 없음 (min_support 등 파라미터는 요청 바디로 전달)

---

## 7. DB 테이블 (수동 생성 필요)

### HAi DB에 실행 — association_results

```sql
CREATE TABLE public.association_results (
    id              SERIAL          PRIMARY KEY,
    pn              TEXT            NOT NULL,
    parquet_guid    TEXT            NOT NULL,
    min_support     DOUBLE PRECISION NOT NULL DEFAULT 0.05,
    min_confidence  DOUBLE PRECISION NOT NULL DEFAULT 0.6,
    min_lift        DOUBLE PRECISION NOT NULL DEFAULT 1.2,
    result_json     JSONB           NOT NULL,
    created_by      TEXT,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    UNIQUE (pn, parquet_guid)
);

CREATE INDEX ON public.association_results (pn);
CREATE INDEX ON public.association_results (parquet_guid);
```

> **⚠️ 주의**: 배포 전 반드시 테이블 생성 완료 필요.  
> `result_json` 컬럼은 JSONB 타입으로 nodes/edges/meta 전체를 하나의 JSON으로 저장.

---

## 8. 캐싱 및 재분석 판단 로직

```
요청 수신 (pn + parquet_file_guid)
  │
  ├─ DB 조회 → 결과 있음 → cached=true 즉시 반환
  │
  └─ DB 조회 → 결과 없음
        └─ 분석 실행
        └─ UPSERT (ON CONFLICT DO UPDATE)
        └─ cached=false 반환

Parquet 변경 감지:
  - GUID가 바뀌면 새로운 (pn, guid) 조합 → DB 미히트 → 자동 재분석
  - GUID 동일 → 기존 결과 재사용 (Parquet 내용 변경 없음으로 간주)
```

---

## 9. 문항 유형별 처리

| 유형 | 처리 | 근거 |
|---|---|---|
| `single` | ✅ 포함 | 범주형. 코드 숫자 그대로 Item화 |
| `multi` | ✅ 포함 | 다중선택. 각 선택지 별도 Item |
| `rank` / `maxrank` / `minrank` | ✅ 포함 | 순위형 범주 |
| `custom` | ✅ 포함 | 사용자 정의 범주형 |
| `scale` | ❌ 제외 | 연속형. 구간화 없이 Item화 부적절 |
| `open(숫자)` | ❌ 제외 | 연속형 |
| `open(문자)` | ❌ 제외 | 텍스트형. NLP 없이 불가 |

---

## 10. 성능 제약 및 안전 기준

| 항목 | 값 | 설명 |
|---|---|---|
| `max_variables` | 60 (기본) | 변수 수 상한. 조합 폭발 방지 |
| `min_support` 최솟값 | 0.03 (강제) | 요청값 무시하고 하한 적용 |
| Itemset 크기 | 2-itemset 전용 | 3개 이상 조합 계산 제외 (MVP) |
| CancellationToken | 30초 | 타임아웃 시 ErrorCode=500 반환 |
| DB 쓰기 | SELECT / INSERT / UPDATE만 | DELETE / DDL 금지 |

---

## 11. Rollback

| 대상 | 방법 |
|---|---|
| `AiAssociationController.cs` | 파일 삭제 |
| `AssociationRuleService.cs` | 파일 삭제 |
| `IMainRepository.cs` / `MainRepository.cs` | 추가 메서드 2개 삭제 또는 `git checkout` |
| `SegmentationModels.cs` | `AssociationRequest` 클래스 삭제 |
| `Program.cs` | DI 등록 1줄 삭제 |
| DB 테이블 | 사용자 수동 `DROP TABLE public.association_results` |


{
  "user": "syhong",
  "pn": "q251708",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "min_support":       0.05,
  "min_confidence":    0.6,
  "min_lift":          1.2,
  "max_variables":     60
  "map_data_json": "string"
}