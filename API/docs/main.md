# 설문온 APICORE - Controller & API 개발 메인 지침 문서

본 문서는 **SurveyonApi (APICORE)** 프로젝트에서 신규 Controller를 개발하거나 기존 API를 수정/확장할 때 **반드시 무조건 기본으로 참조하고 준수해야 하는 개발 표준 지침**입니다.

---

## 1. 성공 / 에러 응답 처리 표준 (Response Protocol)

### 1.1 성공 응답 (Success Code: `777`)
- 모든 정상 처리 결과는 **`777`** 성공 코드를 사용하여 반환합니다.
- `BaseController`에 정의된 `ReturnResponse("777", data)` 메서드를 일관되게 사용합니다.
- 예시:
  ```csharp
  return await ReturnResponse("777", new { message = "성공", result = data });
  ```

### 1.2 에러 응답 (Error Codes: `400`대 및 `700`대)
- 에러 발생 시 외부 사용자/공격자에게 시스템 내부의 세부 구현 원인이나 조건(예: "계정이 존재하지 않습니다" vs "비밀번호가 틀렸습니다")을 상세 메시지로 노출하지 않습니다.
- PostgreSQL **`public.pro_error_code`** 테이블에 정의된 범용 코드(400번대, 700번대)를 활용하여 표준 처리합니다.
- `BaseController`의 `ReturnError(code)` 또는 `ReturnError(code, customMessage)`를 사용합니다.
- **주요 에러 코드 규격**:
  - `701`: 필수 파라미터 누락 / 유효하지 않은 요청
  - `702`: 로그인 인증 실패 (계정 미존재 또는 비밀번호 불일치 통합)
  - `703`: 파라미터/데이터를 찾을 수 없음
  - `704`: 토큰 검증 실패 / 권한 없음
  - `761`: 내부 서버 오류 / API 실행 예외
  - `774`: 계정/서비스 이용 기간 만료

---

## 2. API 인증 및 보안 가이드라인 (Authentication & Security)

### 2.1 토큰 인증 (Token Authentication)
- `[AllowAnonymous]`가 명시되지 않은 모든 API 엔드포인트는 Header의 `X-Auth-Token` (또는 `Authorization`) 및 `TokenStore` 검증을 필수로 수행해야 합니다.

### 2.2 보안 토큰(Loginkey) 난수화
- 사용자 식별용 토큰(`loginkey`, `Token`)은 사용자의 ID, 이메일, 개인정보를 그대로 토큰값으로 사용하지 않습니다.
- 항상 `Guid.NewGuid().ToString("N")` 등 추측이 불가능한 무작위 난수 기반 토큰을 발급하여 DB 및 `TokenStore`에 관리해야 합니다.

### 2.3 암호화 키 관리
- AES256 키 및 IV, JWT Secret 등 모든 암호화 키는 C# 클래스 소스코드 내에 상수로 하드코딩하지 않으며, 반드시 `appsettings.json` 설정(`IConfiguration`)에서 동적으로 로드해야 합니다.

---

## 3. 데이터베이스 및 Repository 표준 지침 (Database & Repository Standard)

### 3.1 DB 접근 로직의 중앙 집중화 (Repositories Layer Centralization)
- Controller나 Service 레이어 내에 SQL 쿼리문을 직접 작성(Inline SQL)하는 것을 엄격히 금지합니다.
- 모든 데이터베이스 테이블 정의, 조회, CUD(Create, Update, Delete) 쿼리 및 접근 로직은 **`Repositories` (`IMainRepository`, `MainRepository`)** 계층에 정의하여 활용해야 합니다.

### 3.2 범용성 및 재사용성 (Reusability & Genericity)
- 특정 컨트롤러나 기능 전용으로 쿼리를 작성하더라도, 해당 DB 작업 메서드는 **다른 Controller 또는 서비스에서도 범용적으로 재활용할 수 있도록 설계**해야 합니다.
- 메서드 인터페이스(`IMainRepository`)는 특정 HTTP 컨트롤러 의존성을 배제하고 모듈화된 매개변수와 비동기 패턴(`Task<T>`, `CancellationToken ct = default`)을 적용합니다.

### 3.3 SQL Injection 차단 및 보안 쿼리
- 문자열 연결(`+` 또는 `$"..."`)을 통한 쿼리 작성을 금지하며, 반드시 파라미터 바인딩(`NpgsqlParameter`, `SqlParameter` 등)을 사용하여 SQL Injection을 완벽하게 차단합니다.

---

## 4. 컨트롤러 구현 체크리스트 (Developer Checklist)

모든 API 개발자는 Pull Request 제출 또는 코드 반영 전 아래 항목을 확인해야 합니다:
- [ ] `docs/main.md` 지침을 확인하고 준수하였는가?
- [ ] 성공 시 `777` 코드로 반환하고 있는가?
- [ ] 에러 발생 시 `pro_error_code` (400/700번대) 범용 에러 코드를 사용하여 내부 세부 정보 노출을 차단하였는가?
- [ ] DB 쿼리가 Controller에 직접 작성되지 않고 `Repositories` 계층에 범용 메서드로 모듈화 정의되었는가?
- [ ] 파라미터화된 쿼리(NpgsqlParameter / SqlParameter)를 사용하여 SQL Injection을 완벽 차단하였는가?
- [ ] 암호화 키나 비밀키가 하드코딩되지 않고 `IConfiguration`을 통해 전달되는가?
