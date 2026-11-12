# Surveyon AI Open - Backend API

## 개요
- **프레임워크**: ASP.NET Core 8 Web API (.NET 8.0)
- **포트**: `5130`
- **주요 기능**:
  - 주관식 문항 목록 및 분석 메타 관리
  - AI 분석 워커(`worker_0427.exe`) 비동기 실행 및 실시간 진행률 추적
  - SignalR Hub (`/hub/analysis`) 웹소켓 푸시
  - 중앙 인증 서버(`AuthCore`) 연동 원격 토큰 검증 (`AuthCheckFilter`)

## 실행 방법
```powershell
# 빌드
dotnet build

# 실행 (포트 5130)
dotnet run --launch-profile http
```

## 주요 설정 (`appsettings.json`)
```json
"Auth": {
  "AuthCoreBaseUrl": "https://stest.hrc.kr/APIs/o/",
  "ValidatePath": "login/token/validate",
  "HeaderName": "X-Auth-Token",
  "CacheSeconds": 30,
  "DisableAuth": false,
  "InternalKey": "cGZeWyxlli5N4FRSwMAsbbRoNzJEhFkocore",
  "InternalKeyHeaderName": "X-Internal-Key"
}
```

상세 내용은 상위 폴더의 `HANDOVER.md` 문서를 참고하십시오.
