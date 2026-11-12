# Query Inventory = 코드에서 사용 중인 쿼리 목록
> **분석 파일**: `Repositories/MainRepository.cs`  
> **분석 일시**: 2026-04-16  
> **원칙**: 코드에서 명확히 식별된 쿼리만 기록. 추측 없음. 미확인 항목은 `Unverified` 표기.

---

## 범례

| 기호 | 의미 |
|---|---|
| 🔴 High | 위험도 높음 |
| 🟡 Medium | 위험도 중간 |
| 🟢 Low | 위험도 낮음 |
| ⚡ Dynamic SQL | 런타임에 SQL이 동적으로 조합됨 |

---

## MainRepository

---

### 1. `GetTestItemsAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | (없음, 리터럴 상수) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | `SELECT 1 as Id, 'Test' as Name` — 테이블 미참조, 테스트용 |

---

### 2. `AddTestItemAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `TestTable` |
| **Operation** | INSERT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 주석에 "시뮬레이션" 명시. 실제 DB 실행 없음, `Task.FromResult(1)` 반환 |

---

### 3. `GetErrorCodeAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_error_code` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. 에러코드 단건 조회 |

**SQL 요약**:
```sql
SELECT errorcode, CASE WHEN @lang='en' THEN error_en ELSE errortext END AS message,
       http_status, errorcontent
FROM pro_error_code
WHERE errorcode = @code LIMIT 1
```

---

### 4. `select_project_list_async` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `public.project_list` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🔴 High |
| **Risk 이유** | `gb` 파라미터로 15개 분기. 외부 입력(`search`)이 문자열 연결 조건으로 추가됨. 동적 SQL |

**분기 구조**: `gb` 값 0·1·2·3·4·5·10·11·12·13·14·100·110·111·112·113·114·115에 따라 쿼리 변경. 추가로 `search` 파라미터가 있으면 LIKE 조건 동적 추가.

---

### 5. `GetManualAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_manual` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 단순 단건 읽기 |

```sql
SELECT jsdata FROM pro_manual LIMIT 1
```

---

### 6. `UpdateManualAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_manual` |
| **Operation** | MIXED (SELECT COUNT → INSERT or UPDATE) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | COUNT 후 분기로 INSERT or UPDATE 실행. 트랜잭션 없는 READ-THEN-WRITE 패턴. TOCTOU 위험 |

**흐름**:
1. `SELECT COUNT(*) FROM pro_manual`
2. count==0 → `INSERT INTO pro_manual (jsdata) VALUES (@jsdata::jsonb)`
3. count>0 → `UPDATE pro_manual SET jsdata = @jsdata::jsonb`

---

### 7. `SelectProjectListSurveyToolAsync` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `[SURVEYTOOL].[dbo].[T_Questionnaire]` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | `gubun`, `year`, `search` 파라미터로 동적 SQL 조합. MSSQL `with(nolock)` 힌트 사용 |

---

### 8. `select_user_hpro` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `[SURVEYTOOL].[dbo].[T_EmployeeInfo]` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | `gubun` 파라미터로 분기. MSSQL `with(nolock)` 힌트. 임직원 개인정보 포함 |

---

### 9. `select_user_hpro_api`

| 항목 | 내용 |
|---|---|
| **Query Type** | HTTP API (DB 쿼리 없음) |
| **Target Table** | (없음) |
| **Operation** | 외부 HTTP GET |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | API Key 하드코딩 (`+SwkfFTDYdSzSP8wBE...`). 임직원 개인정보 반환 |
| **Notes** | `select_user_hpro`의 외부 API 대안 경로 |

---

### 10. `GetUserNameAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_login` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 단건 읽기, 인증 무관 |

```sql
SELECT username FROM public.pro_login WHERE userid = @userId LIMIT 1
```

---

### 11. `select_surveyon_projects_async` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `public.surveyon_projects` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🔴 High |
| **Risk 이유** | `gubun==1`일 때 `pn` 값이 파라미터 없이 문자열 보간(`$"... IN ({pn})"`)으로 직접 삽입됨. SQL Injection 잠재 위험 |

---

### 12. `select_project_enter_async` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `public.project_enter` |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🔴 High |
| **Risk 이유** | `gubun==1`일 때 `pn`이 문자열 보간으로 `IN ({pn})`에 직접 삽입됨. SQL Injection 잠재 위험 |

---

### 13. `GetTAnswerCountAsync` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `"T_Answer"` (Postgresqm) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🔴 High |
| **Risk 이유** | `gb` 파라미터로 5개 분기(기본·1·2·10·11). 동적 SQL. 응답 원본 데이터(`originalData`) 포함 |

---

### 14. `OpenEnterAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | Unknown |
| **Target Table** | (없음) |
| **Operation** | 플레이스홀더만 존재 |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Notes** | `gb="openenter"` 처리 로직이 placeholder 상태. 실제 쿼리 없음 |

---

### 15. `GetChargeCostAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.api_admin` (Postgres) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | AI API 비용 단가 조회. 비용 계산 로직에 영향 |

```sql
SELECT chargecost FROM api_admin WHERE apigubun=@apigubun LIMIT 1
```

---

### 16. `UpdateChargeCostAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.api_admin` (Postgres) |
| **Operation** | UPDATE |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | WHERE 절 없이 전체 갱신 위험 없음(apigubun 조건 있음)이나, API 과금 단가 직접 변경. 이력 없음 |

```sql
UPDATE api_admin SET chargecost = @chargeCost WHERE apigubun = @apiGubun
```

---

### 17. `RpsQmSelectDataMapAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `T_DataMap` (SqlServer / SqlServerqm) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | `servername` 파라미터로 connectionKey가 분기됨. 오픈형 문항 식별에 중요 |

```sql
SELECT * FROM T_DataMap
WHERE questionnaireNumber=@pn
  AND (openDefinition IS NOT NULL OR questionType='Open' OR cQuestionVariable LIKE '%_op%')
  AND exportType=0
ORDER BY ranking/1, opOrder/1
```

---

### 18. `HdataSelectDataMapAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.surveyon_variables` (Postgreshdata) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 세그멘테이션/요약 파이프라인의 핵심 입력 데이터. 오픈형 변수 필터 |

```sql
SELECT * FROM surveyon_variables
WHERE pn=@pn AND (open_rule!='' OR type='Open' OR c_question_variable LIKE '%_op%' OR type='string')
ORDER BY ranking/1
```

---

### 19. `select_surveyon_variables_async`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.surveyon_variables` (Postgreshdata) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 단순 전체 조회 |

```sql
SELECT * FROM surveyon_variables WHERE pn=@pn
```

---

### 20. `SelectProLoginAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_login` (Postgres) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 다수 메서드에서 호출되는 공용 내부 메서드. 인증 흐름에 간접 영향 |

```sql
SELECT username, groupposition FROM public.pro_login WHERE userid = @userid LIMIT 1
```

---

### 21. `InsertProjectEnterAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.project_enter` (Postgres) |
| **Operation** | INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | 프로젝트 등록 핵심 흐름. `ON CONFLICT DO NOTHING`으로 중복 등록 무시 |

```sql
INSERT INTO public.project_enter
  (projectpof, projectnum, projectname, register_userid, register_date, servername, merge_pn)
VALUES (@pof, @projectnum, @proname, @userid, @date, @server, @projectnum)
ON CONFLICT (projectnum, projectpof) DO NOTHING
```

---

### 22. `Insert_surveyon_projectsAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.surveyon_projects` (Postgreshdata) |
| **Operation** | INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | HData 프로젝트 등록 핵심 흐름. `ON CONFLICT DO NOTHING` |

```sql
INSERT INTO public.surveyon_projects
  (pof, project_name, pn, method, creator, created_at, updated_at, data_source)
VALUES (@pof, @proname, @projectnum, @method, @userid, @date, @date, @server)
ON CONFLICT (pn) DO NOTHING
```

---

### 23. `Insert_surveyon_variablesAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL + 내부 메서드 호출 |
| **Target Table** | `public.surveyon_variables` (Postgreshdata) |
| **Operation** | SELECT (조건 확인) → INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | `select_surveyon_projects` + `select_surveyon_variables` 조회 후 조건부 INSERT. READ-THEN-WRITE 패턴. 트랜잭션 없음 |

```sql
INSERT INTO public.surveyon_variables
  (project_id, pn, s_question_variable, c_question_variable, label, type, ...)
VALUES (@project_id, @projectnum, 'pid', 'pid', 'pid', 'open(문자)', 20, 1, 20, 0, 1, @date, @date, true)
```

---

### 24. `InsertProjectPermissionAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL + 내부 메서드 호출 |
| **Target Table** | `public.pro_permission` (Postgres) |
| **Operation** | INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | 사용자 권한 부여. 내부에서 `SelectProLoginAsync` 별도 호출. `ON CONFLICT DO NOTHING` |

```sql
INSERT INTO public.pro_permission
  (projectpof, projectnum, projectname, userid, username, usergroup, position, register_date, expired_date)
VALUES (@pof, @projectnum, @proname, @userid, @username, @usergroup, @position, @date, @date2)
ON CONFLICT (projectnum, userid) DO NOTHING
```

---

### 25. `UpdateTAnswerAsync` ⚡ Dynamic SQL

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL (Dynamic) |
| **Target Table** | `"T_Answer"` (Postgresqm) |
| **Operation** | UPDATE |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | QM 서버 응답 데이터 상태 갱신. `gb` 분기로 영향 범위 변동. 트랜잭션 없음 |

**분기**:
- 기본: `UPDATE "T_Answer" SET openyn=@openyn WHERE pid IN (SELECT pid FROM "T_Answer" WHERE questionnaireNumber=@pn AND answerStateCode=@statecode)`
- `gb="1"`: `UPDATE "T_Answer" SET openyn=@openyn WHERE questionnaireNumber=@pn AND ... (openyn IS NULL OR openyn='')`

---

### 26. `HdataSelectExAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.surveyon_labels` (Postgreshdata) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. 레이블 코드 조회 |

```sql
SELECT * FROM surveyon_labels WHERE variable_id=@id ORDER BY code::int
```

---

### 27. `RpsQmSelectExAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `T_DataMapExample` (SqlServer / SqlServerqm) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. `servername` 파라미터로 connectionKey 분기 |

```sql
SELECT * FROM T_DataMapExample WHERE dataMapID=@id ORDER BY exampleValue/1
```

---

### 28. `InsertUpdatePnLbAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pn_lb` (Postgres) |
| **Operation** | INSERT + UPDATE (UPSERT) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | AI LB(레이블링) 결과 저장. `result_json_before`도 현재 값으로 덮어씀 (이전 값 보존 없음) |

```sql
INSERT INTO public.pn_lb (projectnum, qnum, insert_date, result_json, result_json_before)
VALUES (@projectnum, @qnum, @input_date, @result_json::jsonb, @result_json::jsonb)
ON CONFLICT (projectnum, qnum) DO UPDATE SET
  result_json = @result_json::jsonb,
  result_json_before = @result_json::jsonb,
  update_date = @input_date
```

> ⚠️ `result_json_before`가 항상 현재값으로 갱신됨. 이전 결과 롤백 불가.

---

### 29. `ProjectListInsertDbAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.project_list` (Postgres) |
| **Operation** | INSERT + UPDATE (UPSERT) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | 프로젝트 핵심 데이터 입력. `ON CONFLICT`시 `answer_origin`, `register_date`만 갱신 |

```sql
INSERT INTO public.project_list
  (projectnum, projectname, projectpof, model, qnum, qnum_text, question_fin,
   question_origin, register_userID, use_group, use_id, use_name, useyn,
   project_lock, load_fin, register_date, merge_pn, merge_qnum, merge_qnum_check,
   answer_origin, servername, mapid)
VALUES (...)
ON CONFLICT (projectnum, qnum) DO UPDATE SET
  answer_origin = @answer::jsonb,
  register_date = @register_date
```

---

### 30. `SelectProListInfoAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL + 내부 메서드 호출 |
| **Target Table** | `public.project_list`, `public.pro_permission` (Postgres) |
| **Operation** | SELECT (서브쿼리 포함) |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | `SelectProLoginAsync` 결과로 쿼리 분기. `groupposition` 값에 따라 접근 허용 범위 변동 |

**분기**:
- 일반/고객: `userid = @userid OR userid = 'all'`
- 그 외: `userid = @userid OR userid = 'all' OR userid = 'hrc'`

---

### 31. `GetUserGroupAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_permission` (Postgres) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 사용자-프로젝트 권한 그룹/직책 조회. 인가 로직에 사용 |

```sql
SELECT usergroup, position FROM public.pro_permission
WHERE userid = @userid AND projectnum = @projectnum LIMIT 1
```

---

### 32. `SelectUserCheckAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_login` (Postgres) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | loginkey 기반 세션 검증. 인증 흐름 직접 관여 |

```sql
SELECT * FROM public.pro_login WHERE userid=@USER AND loginkey=@KEY
```

---

### 33. `GetCustomerLoginInfoAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.pro_login` LEFT JOIN `public.pro_permission` (Postgres) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🔴 High |
| **Risk 이유** | 고객(SRT) 계정 인증 로직. `groupcode LIKE '99999999%'` 조건 기반. 인가 정보 반환 |

```sql
SELECT p.projectnum, p.projectname, p.showmenu, p.page_id, p.page_title,
       l.groupcode, l.loginkey
FROM public.pro_login l
LEFT JOIN public.pro_permission p ON l.userid = p.userid
WHERE l.userid = @cs AND l.groupcode LIKE '99999999%'
LIMIT 1
```

---

### 34. `AiSummaryGetExistingAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.ai_summary` (PostgresHSRT) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. AI 요약 캐시 조회 |

```sql
SELECT context_hash, result_data FROM public.ai_summary
WHERE project_num=@pn AND page_id=@pg AND table_id=@tid LIMIT 1
```

---

### 35. `AiSummaryUpsertAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.ai_summary` (PostgresHSRT) |
| **Operation** | INSERT + UPDATE (UPSERT) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | AI 요약 결과 영구 저장. 충돌 시 기존 결과 덮어씀 |

```sql
INSERT INTO public.ai_summary
  (project_num, page_id, table_id, model, context_hash, result_data, created_at, updated_at)
VALUES (...)
ON CONFLICT (project_num, page_id, table_id) DO UPDATE SET
  model=EXCLUDED.model, context_hash=EXCLUDED.context_hash,
  result_data=EXCLUDED.result_data, updated_at=NOW()
```

---

### 36. `ChatSessionCreateAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_sessions` (PostgresHAI) |
| **Operation** | INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | 챗봇 세션 생성. `RETURNING session_id` 반환. 세션 ID 누출 시 보안 위험 |

```sql
INSERT INTO public.chat_sessions
  (project_num, page_id, model, context_text, user_id, created_at, updated_at)
VALUES (...) RETURNING session_id::TEXT
```

---

### 37. `ChatSessionGetContextAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_sessions` (PostgresHAI) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 세션 컨텍스트 전체 반환. context_text가 대용량일 수 있음 |

```sql
SELECT context_text, model FROM public.chat_sessions
WHERE session_id = @sid::UUID LIMIT 1
```

---

### 38. `ChatMessageInsertAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_messages` (PostgresHAI) |
| **Operation** | INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | 대화 이력 영구 저장. `RETURNING message_id`. chart_data JSONB 포함 |

```sql
INSERT INTO public.chat_messages
  (session_id, role, content, user_id, chart_data, created_at)
VALUES (@sid::UUID, @role, @content, @uid, @chart::JSONB, NOW())
RETURNING message_id::TEXT
```

---

### 39. `ChatMessageGetHistoryAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_messages` (PostgresHAI) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 대화 이력 전체 조회. 데이터 규모에 따라 성능 영향 |

```sql
SELECT message_id, role, content, chart_data, created_at, user_id
FROM public.chat_messages
WHERE session_id = @sid::UUID AND user_id = @uid
ORDER BY created_at ASC
```

---

### 40. `ChatMessagePurgeExpiredAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_messages` (PostgresHAI) |
| **Operation** | DELETE |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | 만료 메시지 일괄 삭제. `retentionDays` 파라미터가 잘못되면 전체 삭제 가능. 복구 불가 |

```sql
DELETE FROM public.chat_messages WHERE created_at < NOW() - INTERVAL '{retentionDays} days'
```

> ⚠️ `retentionDays` 값 검증 로직이 코드상 없음.

---

### 41. `FeedbackUpsertAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.feedback_logs` (PostgresHAI) |
| **Operation** | INSERT + UPDATE (UPSERT) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 피드백 저장. 충돌 시 rating/reason 갱신 |

```sql
INSERT INTO public.feedback_logs (message_id, rating, reason, created_at)
VALUES (@mid::UUID, @rating, @reason, NOW())
ON CONFLICT (message_id) DO UPDATE SET rating=EXCLUDED.rating, reason=EXCLUDED.reason
```

---

### 42. `FewShotGetCandidatesAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.few_shot_candidates` (PostgresHAI) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. LLM 프롬프트 구성용 예시 데이터 |

```sql
SELECT question_text, answer_text, keywords
FROM public.few_shot_candidates ORDER BY created_at DESC LIMIT @n
```

---

### 43. `FewShotInsertAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.few_shot_candidates` (PostgresHAI) |
| **Operation** | INSERT |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | LLM 학습 데이터 직접 삽입. `keywords`가 `TEXT[]` 배열 타입. 데이터 품질이 AI 품질에 직접 영향 |

```sql
INSERT INTO public.few_shot_candidates
  (project_num, question_text, answer_text, keywords, created_at)
VALUES (@pn, @q, @a, @kw, NOW())
```

---

### 44. `RecommendGetCachedAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_recommendations` (PostgresHAI) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. 추천 질문 캐시 조회 |

```sql
SELECT context_hash, questions FROM public.chat_recommendations
WHERE session_id = @sid::UUID LIMIT 1
```

---

### 45. `RecommendUpsertAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.chat_recommendations` (PostgresHAI) |
| **Operation** | INSERT + UPDATE (UPSERT) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 추천 질문 캐시 갱신. 충돌 시 덮어씀. context_hash 기반 자동 갱신 |

```sql
INSERT INTO public.chat_recommendations
  (session_id, context_hash, questions, model, created_at, updated_at)
VALUES (@sid::UUID, @hash, @questions::JSONB, @model, NOW(), NOW())
ON CONFLICT (session_id) DO UPDATE SET
  context_hash=EXCLUDED.context_hash, questions=EXCLUDED.questions,
  model=EXCLUDED.model, updated_at=NOW()
```

---

### 46. `GetSurveyMapDataAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.surveyon_file_storage` (Postgreshdata) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟡 Medium |
| **Risk 이유** | 세그멘테이션 전처리 핵심 입력. 조회 실패 시 모든 컬럼을 연속형으로 처리 (fallback 있음) |

```sql
SELECT map_data FROM surveyon_file_storage
WHERE pn = @pn AND parquet_file_guid = @guid LIMIT 1
```

---

### 47. `SegmentResultUpsertAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.segmentation_results` (PostgresHAI) |
| **Operation** | INSERT + UPDATE (UPSERT) |
| **Transaction-sensitive** | Unverified |
| **Risk** | 🔴 High |
| **Risk 이유** | K-Means 세그멘테이션 결과 저장. 충돌 시 이전 결과 덮어씀. 이력 없음 |

```sql
INSERT INTO public.segmentation_results
  (pn, parquet_guid, k, model, created_by, result_json, created_at, updated_at)
VALUES (...)
ON CONFLICT (pn, parquet_guid) DO UPDATE SET
  k=EXCLUDED.k, model=EXCLUDED.model,
  result_json=EXCLUDED.result_json, updated_at=NOW()
```

---

### 48. `SegmentResultGetAsync`

| 항목 | 내용 |
|---|---|
| **Query Type** | SQL |
| **Target Table** | `public.segmentation_results` (PostgresHAI) |
| **Operation** | SELECT |
| **Transaction-sensitive** | No |
| **Risk** | 🟢 Low |
| **Risk 이유** | 읽기 전용. 기존 세그멘테이션 결과 조회 |

```sql
SELECT result_json FROM public.segmentation_results
WHERE pn = @pn AND parquet_guid = @guid LIMIT 1
```

---

## 요약 통계

| 위험도 | 메서드 수 |
|---|---|
| 🔴 High | 22 |
| 🟡 Medium | 13 |
| 🟢 Low | 13 |
| **합계** | **48** |

| Operation | 메서드 수 |
|---|---|
| SELECT | 22 |
| INSERT | 6 |
| UPDATE | 2 |
| DELETE | 1 |
| UPSERT (INSERT+UPDATE) | 7 |
| MIXED (분기 포함) | 4 |
| HTTP / 없음 | 3 |

### 🔴 특별 주의 메서드

| 메서드 | 이유 |
|---|---|
| `ChatMessagePurgeExpiredAsync` | `retentionDays` 검증 없는 일괄 DELETE |
| `UpdateManualAsync` | 트랜잭션 없는 READ-THEN-WRITE |
| `Insert_surveyon_variablesAsync` | 트랜잭션 없는 READ-THEN-WRITE |
| `InsertUpdatePnLbAsync` | `result_json_before`가 현재값으로 덮어씌워짐 |
