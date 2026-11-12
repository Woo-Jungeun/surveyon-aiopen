# AiAnalysis Controller — Segmentation Pipeline Architecture

## 개요

응답자 군집화(K-Means Segmentation) + LLM 페르소나 부여 파이프라인.  
설문 응답 Parquet 데이터를 읽어 ML.NET으로 군집화하고, LLM(GPT-4o 등)이 각 군집의 특성을 분석하여 마케팅 페르소나를 도출합니다.

---

## 1. 엔드포인트

| Method | URL | 설명 |
|--------|-----|------|
| POST | `/AiAnalysis/segment` | 군집화 실행 + 페르소나 생성 + 결과 저장 |
| POST | `/AiAnalysis/result` | 저장된 결과 조회 (재실행 없이) |

---

## 2. 전체 처리 흐름

```
Client → POST /AiAnalysis/segment
         { "user", "pn", "parquet_file_guid", "k", "model" }
              │
              ▼
┌─────────────────────────────────────────────────────┐
│  AiAnalysisController                               │
│                                                     │
│  Step 0: DB 조회 ─────────────────────────────────  │
│    PostgreSQL (HData DB)                            │
│    survey_file_storage WHERE pn=@pn AND guid=@guid  │
│    → map_data (컬럼 메타정보 JSON)                  │
│                                                     │
│  Step 1: Parquet 로드 ────────────────────────────  │
│    {BasePath}/data/{guid}.parquet                   │
│    → List<Dictionary<string, double>> rawRows       │
│                                                     │
│  Step 2: 전처리 ──────────────────────────────────  │
│    map_data 파싱 → ColumnMeta 목록                  │
│    연속형/순서형 → NormalizeMinMax                  │
│    명목형/범주형 → 숫자 그대로 (float 벡터화)       │
│    → ML.NET IDataView (동적 float[] Features 벡터) │
│                                                     │
│  Step 3: K-Means 군집화 ──────────────────────────  │
│    mlContext.Clustering.Trainers.KMeans(k=K)        │
│    → 클러스터 할당 (1-based PredictedLabel)         │
│    → 군집별 Centroid 계산 (응답자 인덱스 기반)      │
│    → 전체 평균 vs 군집 평균 Delta 계산              │
│    → 상위 5개 Delta 속성 추출                        │
│                                                     │
│  Step 4: LLM 페르소나 (K개 병렬 처리) ────────────  │
│    PersonaGeneratorService.GeneratePersonaAsync()   │
│    → IAiLlmService.CallLlmAsync (GPT-4o 등)        │
│    → ClusterPersonaDto JSON 역직렬화                │
│                                                     │
│  Step 5: DB 저장 ─────────────────────────────────  │
│    PostgreSQL (HAi DB)                              │
│    segmentation_results UPSERT                      │
└─────────────────────────────────────────────────────┘
              │
              ▼
Response: { "success": "777", "resultjson": {
  "pn": "...", "parquet_guid": "...", "k": 3,
  "total_rows": 1200,
  "clusters": [
    {
      "cluster_index": 0,
      "member_count": 420,
      "persona_name": "트렌드 민감형 가치소비자",
      "persona_summary": "...",
      "key_traits": ["특성1", "특성2"],
      "business_action": "...",
      "top_signals": [
        { "feature": "q10", "label": "...", "delta": 0.32, "direction": "higher" }
      ]
    },
    ...
  ]
}}
```

---

## 3. 입력 데이터 포맷

### POST /AiAnalysis/segment Request
```json
{
  "user":              "syhong",
  "pn":                "n0101_001",
  "parquet_file_guid": "xxxx-yyyy-zzzz",
  "k":                 3,
  "model":             "GPT-4o"
}
```

### map_data JSON 구조 (survey_file_storage.map_data)
```json
[
  { "name": "q1",  "type": "continuous", "label": "브랜드 인지도" },
  { "name": "q2",  "type": "ordinal",    "label": "구매 빈도" },
  { "name": "sex", "type": "nominal",    "label": "성별" },
  { "name": "age", "type": "ordinal",    "label": "연령대" }
]
```

**type 값:** `continuous`, `ordinal`, `nominal`, `categorical`

---

## 4. 출력 데이터 포맷 (ClusterPersonaDto)

```json
{
  "cluster_index": 0,
  "persona_name": "트렌드 민감형 가치소비자",
  "persona_summary": "2~3문장 페르소나 설명",
  "key_traits": ["가격 민감도 낮음", "신제품 추구", "온라인 구매 선호"],
  "business_action": "프리미엄 신제품 얼리어답터 마케팅 캠페인 실행",
  "top_signals": [
    { "feature": "q10", "label": "신제품 관심도", "delta": 0.42, "direction": "higher" },
    { "feature": "q5",  "label": "가격 민감도",   "delta": -0.31, "direction": "lower" }
  ]
}
```

---

## 5. 파일 구조

```
Controllers/
  AiAnalysisController.cs    ← 엔드포인트 2개
Services/
  SegmentationService.cs     ← Step 1~3 (Parquet + ML.NET)
  PersonaGeneratorService.cs ← Step 4 (LLM 페르소나)
Models/
  SegmentationModels.cs      ← DTO/모델 전체
Repositories/
  IMainRepository.cs         ← 3개 메서드 인터페이스
  MainRepository.cs          ← GetSurveyMapDataAsync, SegmentResult{Upsert,Get}Async
```

---

## 6. appsettings.json 설정

```json
"ParquetSettings": {
  "BasePath": "D:/data/parquet"
},
"Segmentation": {
  "DefaultK": 3,
  "TopDeltaFeatures": 5
}
```

- **BasePath**: Parquet 파일이 저장된 루트 경로
- **DefaultK**: k 파라미터 미입력 시 기본 클러스터 수
- **TopDeltaFeatures**: LLM에 주입할 상위 Delta 속성 수

---

## 7. DB 테이블 (수동 생성 필요)

### HAi DB에 실행 — segmentation_results

```sql
CREATE TABLE public.segmentation_results (
    id           SERIAL      PRIMARY KEY,
    pn           TEXT        NOT NULL,
    parquet_guid TEXT        NOT NULL,
    k            INT         NOT NULL,
    result_json  JSONB       NOT NULL,
    model        TEXT,
    created_by   TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (pn, parquet_guid)
);

CREATE INDEX ON public.segmentation_results (pn);
CREATE INDEX ON public.segmentation_results (parquet_guid);
```

> **⚠️ 주의**: `result_json` 컬럼은 JSONB 타입으로, 모든 클러스터 페르소나 결과를 하나의 JSON으로 저장합니다.  
> 프론트엔드에서 개별 클러스터 조회 시 `result_json->'clusters'->0` 형식으로 접근 가능합니다.

---

## 8. ML.NET 동적 스키마 핵심 메커니즘

설문 문항이 파일마다 달라 **컴파일 타임에 컬럼을 알 수 없는** 상황을 해결하는 방법:

```csharp
// 1. Parquet에서 컬럼을 읽어 float[] 벡터로 변환
var featureMatrix = rawRows
    .Select(row => usedCols
        .Select(c => row.TryGetValue(c, out var v) ? (float)v : 0f)
        .ToArray())
    .ToList();

// 2. 런타임 SchemaDefinition으로 벡터 차원 지정
var schema = SchemaDefinition.Create(typeof(DynamicRow));
schema["Features"].ColumnType = new VectorDataViewType(
    NumberDataViewType.Single, featureCount);  // ← 런타임에 차원 결정

// 3. K-Means는 항상 단일 float[] "Features" 벡터만 사용
var pipeline = mlContext.Clustering.Trainers.KMeans("Features", numberOfClusters: k);
```

**제약사항**: 명목형 변수 OneHotEncoding은 동적 스키마에서 직접 구현이 복잡하므로,  
현재는 숫자 변환 후 NormalizeMinMax를 전체 적용합니다.  
향후 개선 시 컬럼별 변환 파이프라인을 분리하여 적용 가능합니다.

---

## 9. LLM 페르소나 프롬프트 지침

### 핵심: 전체 평균 vs 군집 평균 차이(Delta) 기반 분석

단순 군집 평균이 아닌, **전체 대비 상대적 차이**를 LLM에 주입합니다:

```
[군집 특징 수치 (전체 평균 vs 군집 평균, 상위 유의미 속성)]
[
  { "feature": "q10", "label": "신제품 관심도",
    "global_mean": 3.2, "cluster_mean": 4.8,
    "delta": 1.6, "delta_ratio": 50.0, "direction": "higher" },
  ...
]
```

이를 통해 LLM이 "이 군집은 평균보다 신제품 관심도가 50% 높다"는 상대적 특성을 파악하여  
**행동경제학/마케팅 관점의 입체적 페르소나**를 도출합니다.


{
  "user": "syhong",
  "pn": "q251708",
  "parquet_file_guid": "33c0cebd-15af-4034-be02-f4be161d3f8f",
  "k": 3,
  "model": "GPT-4o"
}