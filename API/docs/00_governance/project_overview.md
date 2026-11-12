# Project Overview = 해당 프로젝트를 정의하는 문서

## 목적
이 시스템은 ASP.NET Core 기반으로 설문온(사내에서 설문제작,데이터관리,시각화,실사관리를 통합적으로 지원하는 통합플랫폼)의 API 서버이다. AI 기법들을 주요 기능으로 추가하고 있다.

## 핵심 원칙
- 현재 운영 기능의 안정성이 최우선이다.
- 기능 개선보다 장애 방지가 우선이다.
- 즉흥 코드 생성 금지, 문서 기반 변경만 허용한다.
- 기존 API 유지

## 현재 운영 기능 (확정)
- 전체 controller에서 성공, 에러를 반환하는 API (DB에 errorcode가 없을 경우 기본값 반환) : BaseController.cs
- 로그인체크(check), 고객로그인체크(check/cs), 로그인 토큰검증(token/validate), 로그아웃(logout) : LoginController.cs
- 설문온의 전반적인 프로젝트목록조회(gb=qmlist), 새로운 프로젝트등록(gb=NewProject), 기존 프로젝트 자동 등록(gb=qmlistselect), 2026 프로젝트 자동등록(gb=list) : ProjectController.cs
- 프로젝트의 토큰 사용 목록 조회 (list) : TokenCostController.cs
- 토큰 전체 사용 조회(DB 충전금액 조회=chargecostselect) 등 : TokenUseController.cs
- 크로스테이블 완료데이터 기준으로 문항별 AI 요약 분석 : AiSummaryController.cs
- 크로스테이블 & 전체 rawdata(완료데이터 & 맵) 기준으로 AI챗봇 구현 : AiChatController.cs
- rawdata(완료데이터 & 맵) 기준으로 군집화=segmentation 구현 : AiSegmentController.cs

## 향후 확장 방향 (참고용, 현재 미구현)
- 크로스테이블 완료데이터 뿐만 아니라 전체 rawdata(완료데이터 & 맵) 기준으로 AI챗봇 구현 : AiChatController.cs
※ 현재 코드에는 적용되어 있지 않으며, 기존 구조를 변경하는 작업은 별도 승인 후 진행하며 구현 시 반드시 별도 change policy를 따른다.

## 절대 변경 금지 항목
- 운영 DB 접속 문자열 구조 임의 변경 금지
- 인증 로직 임의 교체 금지
- 기존 API 응답 스키마 임의 변경 금지