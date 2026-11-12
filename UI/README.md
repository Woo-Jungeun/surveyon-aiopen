# Surveyon AI Open - Frontend UI

## 개요
- **기술 스택**: React 18, Vite 5, Tailwind CSS, Lucide Icons
- **포트**: `5180` (API 프록시: `http://127.0.0.1:5130`)
- **주요 화면**:
  - 문항 목록 화면 (`QuestionList.jsx`): 한큐분석 트리거 및 진행상황 표시, 범용 옵션 설정
  - 분석 뷰어 (`AnalysisView.jsx`): 보기 데이터, 응답 데이터, PID별 데이터

## 실행 방법
```powershell
# 패키지 설치
npm install

# 개발 서버 실행 (포트 5180)
npm run dev

# 프로덕션 빌드
npm run build
```

상세 내용은 상위 폴더의 `HANDOVER.md` 문서를 참고하십시오.
