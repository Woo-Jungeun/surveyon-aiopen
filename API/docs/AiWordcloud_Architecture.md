# AiWordcloud Architecture

## 개요
초거대 AI 워드클라우드 마이닝 모듈은 설문 주관식 응답 텍스트를 NLP 분석하여
빈도 기반 워드클라우드, 연관성 네트워크, 단어 클러스터링 결과를 시각화합니다.

---

## 시스템 구성도

```
[프론트엔드: React + Vite]          [백엔드: .NET Core C#]
AiWordcloudPage.jsx                 AiWordcloudController.cs
  ├─ Step 0: 데이터 설정              └─ /AiWordcloud/variables
  ├─ Step 1: 시각화 + 사전 관리           /AiWordcloud/config/load
  │    ├─ WordcloudCanvas.jsx             /AiWordcloud/result
  │    ├─ 단어 리스트 테이블              /AiWordcloud/analyze
  │    └─ 클러스터링 탭                   /AiWordcloud/cluster (LLM 클러스터링)
  │         ├─ LLM OSS API 호출       AiWordcloudService.cs
  │         └─ JS 폴백(Jaccard)           ├─ ClusterWordsAsync() — LLM 의미 클러스터링
  ├─ Step 2: 연관성 분석                  ├─ 프롬프트 파일 로딩 (docs/wordcloude/prompts/)
  │    └─ AssociationGraph.jsx            ├─ Parquet 파일 처리
  └─ Step 3: 리포트 추출                  └─ PostgreSQL 저장
                                      AiLlmService.cs (재사용)
                                          └─ llm-gpt-oss-120b (사내 LLM OSS)
[DB: PostgreSQL]                      MainRepository.cs
  └─ hai_ai_wordcloud                     └─ hai_ai_wordcloud 테이블
       ├─ pn (프로젝트 번호)          [프롬프트 파일]
       ├─ parquet_file_guid           docs/wordcloude/prompts/
       ├─ configjson                      ├─ wordcloud_system.md
       └─ resultjson                      └─ wordcloud_user.md
```

---

## 프론트엔드 상태 구조

```js
// AiWordcloudPage.jsx 주요 상태
params: {
  dataSource, target_vars, banner_vars,
  min_words, max_words, pos_tags,
  enable_translation, use_ngram,
  viz_config: { font, colorTheme, theme, shape },
  custom_dictionaries: { stop_words[], synonyms{}, custom_words[] }
}
resultData: { words: [{ text, value, sentiment }], summaries }
wordSettings: { [word]: { excluded, synonym, compound } }
clusters: [{ representative, members[], totalValue, applied }]
```

---

## 클러스터링 엔진

### v2.0 — LLM OSS 의미적 클러스터링 (메인)
```
POST /AiWordcloud/cluster
  → ClusterWordsAsync(words[], model)
     1. 프롬프트 파일 로딩
        - AppContext.BaseDirectory/docs/wordcloude/prompts/wordcloud_system.md
        - AppContext.BaseDirectory/docs/wordcloude/prompts/wordcloud_user.md
        - 파일 없으면 내장 기본 프롬프트 사용
     2. 단어 목록을 "단어: 빈도" 형식으로 직렬화
     3. IAiLlmService.CallLlmAsync("llm-gpt-oss-120b", sys, user, temp=0.1, timeout=60s)
     4. JSON 파싱: { clusters: [{ representative, members[], reason }] }
     5. LLM 실패 시 → { success=false, fallback=true } 반환
        → 프론트엔드가 JS clusterWordsFallback() 으로 대체
```

### v1.0 (폴백) — 프론트엔드 음절 자카드
```
clusterWordsFallback(words, threshold=0.65):
  Union-Find 방식, 음절 자카드 유사도 + 부분 문자열 포함 관계
  → LLM 실패 시에만 사용
```

### 적용 방식 (옵션 B — 확인 후 적용)
1. "클러스터 분석 실행" → LLM 호출 → 클러스터 탭에 결과 표시
2. 사용자가 개별 "적용" 버튼 → `wordSettings[word].synonym = 대표어`
3. "미적용" 버튼 → `wordSettings[word].synonym` 삭제
4. `displayWords` 필터링 시 synonym 기반 자동 통합

---

## 색상 엔진 (WordcloudCanvas.jsx)

| 테마 | 다크 모드 | 라이트 모드 |
|---|---|---|
| 고빈도 단어 | 밝은 색 | 진한 색 |
| 저빈도 단어 | 어두운 색 | 연한 색 |
| 색상 계열 | 10종 (빨강/파랑/초록/노랑/보라/주황/회색/분홍/랜덤/감정) |

---

## 폰트 구성

| 표시명 | 파일명 | 위치 |
|---|---|---|
| 노토산스 (기본) | NotoSansKR-Regular.ttf | public/fonts/ |
| 나눔고딕 | NanumGothic.ttf | public/fonts/ |
| 나눔스퀘어네오 | NanumSquareNeo-bRg.ttf | public/fonts/ |
| G마켓산스 | GmarketSansTTFMedium.ttf | public/fonts/ |
| LG스마트체 | LGEITextTTF-Regular.ttf | public/fonts/ |
| 카이겐고딕 | KaiGenGothicKR-Medium.ttf | public/fonts/ |
| Kopub돋움체 | KoPubDotumMedium.ttf | public/fonts/ |
| 애플고딕 | AppleGothic.ttf | public/fonts/ |

CSS: `src/index.css` 상단 `@font-face` 8개 선언

---

## API 엔드포인트

| 메서드 | 경로 | 설명 |
|---|---|---|
| POST | /AiWordcloud/variables | Parquet 파일의 변수 목록 조회 |
| POST | /AiWordcloud/config/load | 저장된 시각화 설정 로드 |
| POST | /AiWordcloud/result | 기존 분석 결과 로드 |
| POST | /AiWordcloud/analyze | NLP 분석 실행 |
| POST | /AiWordcloud/cluster | **LLM 의미적 클러스터링 (신규)** |

---

## DB 테이블: hai_ai_wordcloud

| 컬럼 | 타입 | 설명 |
|---|---|---|
| pn | varchar | 프로젝트 번호 |
| parquet_file_guid | varchar | Parquet 파일 식별자 |
| configjson | jsonb | 시각화 설정 (viz_config) |
| resultjson | jsonb | NLP 분석 결과 (words, summaries) |

> **Non-Negotiable**: DB 스키마 변경 금지. configjson/resultjson은 확장 가능 구조.

---

## 변경 이력

| 날짜 | 버전 | 내용 |
|---|---|---|
| 2026-04-27 | v1.0 | 워드클라우드 기본 구현 (5-step stepper, NLP, 시각화) |
| 2026-04-30 | v1.1 | 10가지 색상 계열 팔레트 엔진 구현 |
| 2026-05-06 | v1.2 | 다크/라이트 이중 팔레트 + 커스텀 폰트 8종 |
| 2026-05-07 | v1.3 | 단어 리스트 테이블 + 음절 자카드 클러스터링 엔진 + Step 2 UI 3단 개편 |
| 2026-05-07 | v1.4 | **LLM OSS 의미적 클러스터링 엔진 + 프롬프트 파일 분리 + 미적용/적용 토글** |

---

## 운영 배포 요건 (Windows Server)

| 항목 | 요건 |
|---|---|
| 백엔드 | .NET Core Runtime |
| 프론트엔드 | `npm run build` → dist/ → IIS 정적 사이트 |
| Python | **불필요** (Antigravity 작업 도구용만 사용, 운영 무관) |
| DB | PostgreSQL 접속 설정 (appsettings.json) |
