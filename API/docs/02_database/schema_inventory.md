# Schema Inventory = 코드에서 사용 중인 테이블과 객체 목록
> **생성 기준**: 코드에서 실제 참조된 테이블만 수록. 추측·추론 없음.  
> **분석 파일**: `Repositories/MainRepository.cs` (1298줄)  
> **분석 일시**: 2026-04-16

---

## ⚠️ 범례

| 기호 | 의미 |
|---|---|
| 🔴 High Risk | INSERT / UPDATE / DELETE 발생 |
| 🟡 Medium | 주로 SELECT, 간헐적 쓰기 |
| 🟢 Low | SELECT 전용 (Read-Only) |
| ❓ Unverified | 테이블명 식별됐으나 컬럼 등 세부사항 미검증 |

---

## PostgreSQL 테이블 목록

### 🗄️ DB: Postgres (HSRT 운영 메인)

---

#### 1. `public.project_list` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `MainRepository.select_project_list_async`, `ProjectListInsertDbAsync`, `SelectProListInfoAsync` |
| **Usage Type** | SELECT, INSERT, UPDATE (ON CONFLICT DO UPDATE) |
| **Importance** | **High** |
| **비고** | 프로젝트 핵심 메타 테이블. 전 시스템의 중심. gb 파라미터로 15가지 분기 쿼리. `merge_pn`, `qnum`, `useyn`, `project_lock` 등 다수 컬럼 참조 |

**참조 컬럼**: `id`, `projectnum`, `projectname`, `projectpof`, `model`, `qnum`, `qnum_text`, `question_fin`, `question_origin`, `register_userid`, `register_date`, `update_date`, `status`, `status_cnt`, `status_cnt_fin`, `merge_pn`, `merge_qnum`, `merge_qnum_check`, `useYN`, `project_lock`, `lb_info`, `res_info`, `use_group`, `use_ID`, `use_name`, `answer_origin`, `servername`, `mapid`, `listapikey`, `lvcode`, `option_json`, `python_status`, `merge_pn_check`, `merge_pn_text`, `load_fin`

---

#### 2. `public.project_enter` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `select_project_enter_async`, `InsertProjectEnterAsync` |
| **Usage Type** | SELECT, INSERT (ON CONFLICT DO NOTHING) |
| **Importance** | **High** |
| **비고** | 프로젝트 등록 이력 테이블. `(projectnum, projectpof)` UNIQUE 제약 존재 |

**참조 컬럼**: `id`, `projectpof`, `projectnum`, `projectname`, `register_userid`, `register_date`, `servername`, `merge_pn`

---

#### 3. `public.pro_permission` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `InsertProjectPermissionAsync`, `GetUserGroupAsync`, `SelectProListInfoAsync` (서브쿼리) |
| **Usage Type** | SELECT, INSERT (ON CONFLICT DO NOTHING) |
| **Importance** | **High** |
| **비고** | 사용자별 프로젝트 접근 권한 관리. `(projectnum, userid)` UNIQUE 제약 존재 |

**참조 컬럼**: `projectpof`, `projectnum`, `projectname`, `userid`, `username`, `usergroup`, `position`, `register_date`, `expired_date`, `showmenu`, `page_id`, `page_title`

---

#### 4. `public.pro_login` 🟡 Medium

| 항목 | 내용 |
|---|---|
| **사용 위치** | `GetUserNameAsync`, `SelectProLoginAsync`, `SelectUserCheckAsync`, `GetCustomerLoginInfoAsync` (LEFT JOIN) |
| **Usage Type** | SELECT |
| **Importance** | **High** |
| **비고** | 사용자 인증 / 계정 정보 테이블. 로그인 흐름 핵심. 쓰기 쿼리 없음 (코드상 Read-Only) |

**참조 컬럼**: `userid`, `username`, `groupposition`, `loginkey`, `groupcode`

---

#### 5. `public.pro_error_code` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `GetErrorCodeAsync` |
| **Usage Type** | SELECT |
| **Importance** | **Medium** |
| **비고** | 에러 코드 다국어 메시지 조회. 수정 쿼리 없음 |

**참조 컬럼**: `errorcode`, `errortext`, `error_en`, `http_status`, `errorcontent`

---

#### 6. `public.pro_manual` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `GetManualAsync`, `UpdateManualAsync` |
| **Usage Type** | SELECT, INSERT, UPDATE |
| **Importance** | **Medium** |
| **비고** | 매뉴얼 JSON 데이터 저장. 단일 행 관리 패턴 (COUNT 후 INSERT or UPDATE) |

**참조 컬럼**: `jsdata` 

---

#### 7. `public.pn_lb` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `InsertUpdatePnLbAsync` |
| **Usage Type** | INSERT, UPDATE (ON CONFLICT DO UPDATE) |
| **Importance** | **Medium** |
| **비고** | 프로젝트별 레이블링 결과 JSON 저장. `(projectnum, qnum)` UNIQUE 제약 |

**참조 컬럼**: `projectnum`, `qnum`, `insert_date`, `result_json`, `result_json_before`, `update_date`

---

#### 8. `public.api_admin` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `GetChargeCostAsync`, `UpdateChargeCostAsync` |
| **Usage Type** | SELECT, UPDATE |
| **Importance** | **Medium** |
| **비고** | AI API 비용 설정 관리. `apigubun` 기준으로 LLM 공급자별 단가 관리 |

**참조 컬럼**: `chargecost`, `apigubun`

---

#### 9. `public.ai_summary` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `AiSummaryGetExistingAsync`, `AiSummaryUpsertAsync` |
| **Usage Type** | SELECT, INSERT, UPDATE (ON CONFLICT DO UPDATE) |
| **Importance** | **High** |
| **비고** | AI 요약 결과 캐시. context_hash 기반 중복 LLM 호출 방지. DB: PostgresHSRT |

**참조 컬럼**: `project_num`, `page_id`, `table_id`, `model`, `context_hash`, `result_data`, `created_at`, `updated_at`

---

### 🗄️ DB: PostgresHAI (AI 챗봇 전용)

---

#### 10. `public.chat_sessions` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `ChatSessionCreateAsync`, `ChatSessionGetContextAsync` |
| **Usage Type** | SELECT, INSERT |
| **Importance** | **High** |
| **비고** | 챗봇 세션 관리. `session_id` UUID Primary Key |

**참조 컬럼**: `session_id`, `project_num`, `page_id`, `model`, `context_text`, `user_id`, `created_at`, `updated_at`

---

#### 11. `public.chat_messages` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `ChatMessageInsertAsync`, `ChatMessageGetHistoryAsync`, `ChatMessagePurgeExpiredAsync` |
| **Usage Type** | SELECT, INSERT, DELETE |
| **Importance** | **High** |
| **비고** | 챗봇 대화 이력. 보존 기간 초과 시 DELETE 실행 (`retentionDays` 파라미터) |

**참조 컬럼**: `message_id`, `session_id`, `role`, `content`, `user_id`, `chart_data`, `created_at`

---

#### 12. `public.feedback_logs` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `FeedbackUpsertAsync` |
| **Usage Type** | INSERT, UPDATE (ON CONFLICT DO UPDATE) |
| **Importance** | **Medium** |
| **비고** | 챗봇 응답 평가 피드백. `(message_id)` UNIQUE 제약 |

**참조 컬럼**: `message_id`, `rating`, `reason`, `created_at`

---

#### 13. `public.few_shot_candidates` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `FewShotGetCandidatesAsync`, `FewShotInsertAsync` |
| **Usage Type** | SELECT, INSERT |
| **Importance** | **Medium** |
| **비고** | 챗봇 Few-shot 예시 학습 데이터. `keywords` 컬럼은 TEXT[] 배열 타입 |

**참조 컬럼**: `project_num`, `question_text`, `answer_text`, `keywords`, `created_at`

---

#### 14. `public.chat_recommendations` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `RecommendGetCachedAsync`, `RecommendUpsertAsync` |
| **Usage Type** | SELECT, INSERT, UPDATE (ON CONFLICT DO UPDATE) |
| **Importance** | **Medium** |
| **비고** | 추천 질문 캐시. `session_id` UNIQUE 제약. context_hash 변경 시 자동 갱신 |

**참조 컬럼**: `session_id`, `context_hash`, `questions`, `model`, `created_at`, `updated_at`

---

#### 15. `public.segmentation_results` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `SegmentResultUpsertAsync`, `SegmentResultGetAsync` |
| **Usage Type** | SELECT, INSERT, UPDATE (ON CONFLICT DO UPDATE) |
| **Importance** | **High** |
| **비고** | K-Means 세그멘테이션 결과 저장. `(pn, parquet_guid)` UNIQUE 제약. DB: PostgresHAI |

**참조 컬럼**: `pn`, `parquet_guid`, `k`, `model`, `created_by`, `result_json`, `created_at`, `updated_at`

---

### 🗄️ DB: Postgreshdata (설문 데이터 HData)

---

#### 16. `public.surveyon_projects` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `select_surveyon_projects_async`, `Insert_surveyon_projectsAsync` |
| **Usage Type** | SELECT, INSERT (ON CONFLICT DO NOTHING) |
| **Importance** | **High** |
| **비고** | 설문 프로젝트 메타 (HData). `(pn)` UNIQUE 제약 |

**참조 컬럼**: `id`, `pof`, `project_name`, `pn`, `method`, `creator`, `created_at`, `updated_at`, `data_source`

---

#### 17. `public.surveyon_variables` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `HdataSelectDataMapAsync`, `select_surveyon_variables_async`, `Insert_surveyon_variablesAsync` |
| **Usage Type** | SELECT, INSERT |
| **Importance** | **High** |
| **비고** | 설문 변수 정의 테이블 (HData). 세그멘테이션/요약 파이프라인 입력 데이터 |

**참조 컬럼**: `project_id`, `pn`, `s_question_variable`, `c_question_variable`, `label`, `type`, `code_len`, `opt_count`, `total_len`, `min_answer`, `ranking`, `created_at`, `updated_at`, `is_baked`, `open_rule`

---

#### 18. `public.surveyon_labels` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `HdataSelectExAsync` |
| **Usage Type** | SELECT |
| **Importance** | **Medium** |
| **비고** | 변수별 코드-레이블 매핑. `variable_id` FK 추정 (Unverified) |

**참조 컬럼**: `variable_id`, `code`

---

#### 19. `public.surveyon_file_storage` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `GetSurveyMapDataAsync` |
| **Usage Type** | SELECT |
| **Importance** | **High** |
| **비고** | Parquet 파일 메타 + 설문 맵 JSON 저장. 세그멘테이션 전처리 핵심 입력. 코드 주석에 `survey_files_storage`로도 표기되어 있음 ❓ |

**참조 컬럼**: `pn`, `parquet_file_guid`, `map_data`

---

### 🗄️ DB: Postgresqm (QM 서버)

---

#### 20. `"T_Answer"` 🔴 High Risk

| 항목 | 내용 |
|---|---|
| **사용 위치** | `GetTAnswerCountAsync`, `UpdateTAnswerAsync` |
| **Usage Type** | SELECT, UPDATE |
| **Importance** | **High** |
| **비고** | QM 서버 응답 데이터. 테이블명 대소문자 민감 (따옴표 처리). `openyn` 컬럼을 통해 설문온 업로드 여부 추적 |

**참조 컬럼**: `pid`, `questionnaireNumber`, `answerStateCode`, `lastWriteDateTime`, `openyn`, `originalData`

---

## MSSQL 테이블 목록

> **DB**: `[SURVEYTOOL].[dbo]`

### 🗄️ DB: SqlServer / SqlServerqm

---

#### 21. `[SURVEYTOOL].[dbo].[T_Questionnaire]` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `SelectProjectListSurveyToolAsync` |
| **Usage Type** | SELECT |
| **Importance** | **Medium** |
| **비고** | MSSQL SurveyTool DB의 설문지 목록. `with(nolock)` 힌트 사용 |

**참조 컬럼**: `questionnaireNumber`, `questionnaireName`, `questionnairePersonName`, `researcherName`, `fieldWorkerName`, `pof`, `createDateTime`

---

#### 22. `[SURVEYTOOL].[dbo].[T_EmployeeInfo]` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `select_user_hpro` |
| **Usage Type** | SELECT |
| **Importance** | **Low** |
| **비고** | MSSQL 임직원 정보. `이름`, `email` 컬럼 한글명 사용. `with(nolock)` 힌트 |

**참조 컬럼**: `이름`, `email`

---

#### 23. `T_DataMap` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `RpsQmSelectDataMapAsync` |
| **Usage Type** | SELECT |
| **Importance** | **High** |
| **비고** | RPS/QM 서버 데이터 맵. 오픈형 문항 식별에 사용. connectionKey 분기(`SqlServer`/`SqlServerqm`) |

**참조 컬럼**: `questionnaireNumber`, `openDefinition`, `questionType`, `cQuestionVariable`, `exportType`, `ranking`, `opOrder`

---

#### 24. `T_DataMapExample` 🟢 Low (Read-Only)

| 항목 | 내용 |
|---|---|
| **사용 위치** | `RpsQmSelectExAsync` |
| **Usage Type** | SELECT |
| **Importance** | **Medium** |
| **비고** | 데이터 맵 예시값 조회. `dataMapID` FK 추정 (Unverified) ❓ |

**참조 컬럼**: `dataMapID`, `exampleValue`

---

## 테이블 미포함 항목 (코드 내 플레이스홀더)

| 테이블 | 위치 | 이유 |
|---|---|---|
| `TestTable` | `AddTestItemAsync` L52 | "시뮬레이션" 주석 처리, 실제 실행 없음 |

---

## 요약: 위험도별 분류

### 🔴 High Risk (쓰기 발생 테이블) — 15개

`project_list`, `project_enter`, `pro_permission`, `pro_manual`, `pn_lb`, `api_admin`, `ai_summary`, `chat_sessions`, `chat_messages`, `feedback_logs`, `few_shot_candidates`, `chat_recommendations`, `segmentation_results`, `surveyon_projects`, `surveyon_variables`, `T_Answer`

### 🟢 Read-Only 테이블 — 8개

`pro_login`, `pro_error_code`, `surveyon_labels`, `surveyon_file_storage`, `T_Questionnaire`, `T_EmployeeInfo`, `T_DataMap`, `T_DataMapExample`

---

## ⚠️ 주의 필요 항목

1. **"T_Answer"**
   - PostgreSQL 테이블명은 대소문자 구분됨
   - 반드시 따옴표("T_Answer") 사용 필요
   - 소문자 변환 또는 이름 변경 시 쿼리 오류 발생 가능

2. **select_user_hpro_api()**
   - DB 테이블이 아님
   - 외부 HTTP API 호출을 통해 직원 정보 조회
   - Repository 또는 DB 쿼리와 혼용 금지
   - DB 테이블로 간주하거나 JOIN 대상으로 사용하면 안 됨