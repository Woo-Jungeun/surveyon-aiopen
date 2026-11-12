import React, { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import '../analysis.css';
import {
  getAnalysisInfo,
  getAnalysisResponses,
  getAnalysisCodes,
  getAnalysisRawdata,
  getAnalysisStatus,
  checkAnalysisPopup,
  saveAnalysisInfo,
  saveAnalysisCodes,
  saveAnalysisResponses,
  compareAnalysisResponses,
  startAnalysisWorker,
  getAnalysisWorkerStatus,
  stopAnalysisWorker,
  createWorkerHubConnection,
  downloadAnalysisExcel,
  previewExcelCodes,
  searchImportProjects,
  getImportCodes,
  applyImportCodes
} from '../api/analysisApi';

const ensureRowUids = (items) => {
  return (items || []).map((r, i) => ({
    ...r,
    _uid: r._uid || `${r.fixed_key || 'row'}_c${r.cid ?? 1}_${i}`
  }));
};

export default function AnalysisPage({
  userInfo,
  projectnum,
  projectName,
  initialTarget, // { qnum, qnum_text, question_title, id }
  navList = [],   // [{ qnum, qnum_text, question_title, status }]
  onBackToList,
  onBackToProjects,
  onLogout
}) {
  const user = (userInfo && userInfo.userid) || (() => {
    try {
      const u = JSON.parse(localStorage.getItem('userInfo') || '{}');
      return u.userid || localStorage.getItem('UserId') || 'syhong';
    } catch {
      return 'syhong';
    }
  })();
  const username = (userInfo && userInfo.username) || (() => {
    try {
      const u = JSON.parse(localStorage.getItem('userInfo') || '{}');
      return u.username || '홍소연';
    } catch {
      return '홍소연';
    }
  })();

  // 현재 선택된 문항 인덱스 및 정보
  const [currentIdx, setCurrentIdx] = useState(() => {
    if (!initialTarget || !navList.length) return 0;
    const found = navList.findIndex(n => n.qnum === initialTarget.qnum);
    return found >= 0 ? found : 0;
  });

  const currentItem = navList[currentIdx] || initialTarget || { qnum: '', qnum_text: '', question_title: '' };
  const currentQnum = currentItem.qnum;

  // UI 상태
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);
  const [showQPop, setShowQPop] = useState(false);
  const [qSearch, setQSearch] = useState('');
  const [setOpen, setSetOpen] = useState(false);
  const [activeTab, setActiveTab] = useState('resp'); // 'resp' | 'code' | 'raw'
  const [codeViewMode, setCodeViewMode] = useState('grid'); // 'grid' (스프레드시트 편집) | 'tree' (3단 열)
  const [cbSearch, setCbSearch] = useState('');
  const [cbSort, setCbSort] = useState('freq'); // 'freq' | 'code' | 'name'

  // 열별 개별 검색 필터 상태 (AND 조건 결합)
  const [colFilters, setColFilters] = useState({
    raw: '',
    clean: '',
    multi: '',
    l3: '',
    l1: '',
    l2: '',
    sent: 'all',
    ok: 'all'
  });

  // 열별 3단계 토글 정렬 상태 ({ key: null, dir: null })
  const [sortConfig, setSortConfig] = useState({ key: null, dir: null });

  // 3단계 토글 정렬 핸들러 (asc -> desc -> null)
  const handleToggleSort = (colKey) => {
    setSortConfig(prev => {
      if (prev.key !== colKey) {
        return { key: colKey, dir: 'asc' };
      }
      if (prev.dir === 'asc') {
        return { key: colKey, dir: 'desc' };
      }
      return { key: null, dir: null };
    });
  };

  // 열별 필터 전체 초기화
  const handleResetColFilters = () => {
    setColFilters({
      raw: '',
      clean: '',
      multi: '',
      l3: '',
      l1: '',
      l2: '',
      sent: 'all',
      ok: 'all'
    });
  };

  // PID별 데이터 (rawdata) 열별 검색 필터 상태 (AND 조건 결합)
  const [rawColFilters, setRawColFilters] = useState({
    pid: '',
    cid: '',
    answer: '',
    l1: '',
    l2: '',
    freq: '',
    code: '',
    l3: '',
    sent: 'all',
    date: ''
  });

  // PID별 데이터 열별 3단계 토글 정렬 상태 ({ key: null, dir: null })
  const [rawSortConfig, setRawSortConfig] = useState({ key: null, dir: null });

  // PID별 데이터 3단계 토글 정렬 핸들러
  const handleToggleRawSort = (colKey) => {
    setRawSortConfig(prev => {
      if (prev.key !== colKey) {
        return { key: colKey, dir: 'asc' };
      }
      if (prev.dir === 'asc') {
        return { key: colKey, dir: 'desc' };
      }
      return { key: null, dir: null };
    });
  };

  // PID별 데이터 열별 필터 전체 초기화
  const handleResetRawColFilters = () => {
    setRawColFilters({
      pid: '',
      cid: '',
      answer: '',
      l1: '',
      l2: '',
      freq: '',
      code: '',
      l3: '',
      sent: 'all',
      date: ''
    });
  };

  // 분류 단계 (3단계: 대·중·소 / 2단계: 중·소 / 1단계: 소분류)
  const [levelMode, setLevelMode] = useState('3');
  const [initialLevelMode, setInitialLevelMode] = useState('3');
  const isLevelDirty = levelMode !== initialLevelMode;

  // 컬럼 표시/숨김 설정
  const [showColPop, setShowColPop] = useState(false);
  const [colVis, setColVis] = useState({
    raw: true,
    clean: true,
    multi: true,
    l1: true,
    l2: true,
    l3: true,
    sent: true,
    ok: true
  });

  // 컬럼 줄바꿈 토글 상태
  const [wrapCols, setWrapCols] = useState({ raw: false, clean: false });
  const [expandedRowIds, setExpandedRowIds] = useState(new Set());

  // 컬럼 폭 상태 (드래그 조절)
  const [colWidths, setColWidths] = useState({
    raw: 176,
    clean: 176,
    multi: 44,
    l1: 92,
    l2: 124,
    l3: 0,
    sent: 64,
    ok: 62
  });
  const resizeStateRef = useRef(null);

  // Phase A: 스플리터 패널 폭 조절 상태 (기본 600px, min 360px, max 950px)
  const [cbWidth, setCbWidth] = useState(600);
  const [isDraggingSplitter, setIsDraggingSplitter] = useState(false);

  // Phase A: 키보드 네비게이션 포커스 및 단축키 상태
  const [focusedRowIdx, setFocusedRowIdx] = useState(null);
  const cbSearchInputRef = useRef(null);

  // Phase A: 소분류 빠른 지정 팝오버 (#pop) 상태
  const [showPop, setShowPop] = useState(false);
  const [popPos, setPopPos] = useState({ x: 0, y: 0 });
  const [popTargetRow, setPopTargetRow] = useState(null);
  const [popSearch, setPopSearch] = useState('');
  const [popHighlightIdx, setPopHighlightIdx] = useState(0);
  const popSearchInputRef = useRef(null);

  // Phase A: 새 소분류 즉시 추가 상태
  const [quickAddL1L2, setQuickAddL1L2] = useState('');

  // Phase C: 콘솔 및 파이썬 워커 모달/팝오버 상태
  const [showConsole, setShowConsole] = useState(false);
  const [consoleLines, setConsoleLines] = useState([]);
  const [workerRunning, setWorkerRunning] = useState(false);
  const [currentJobId, setCurrentJobId] = useState(null);
  const [showWorkerModal, setShowWorkerModal] = useState(false);
  const [workerStep, setWorkerStep] = useState(1); // 1: 데이터검증, 2: 모델호출, 3: 분류/임베딩, 4: DB반영 및 완료
  const [workerElapsed, setWorkerElapsed] = useState(0); // 경과 초
  const [workerActionType, setWorkerActionType] = useState('analyze'); // 'analyze' | 'translate' | 'respondent' | 'empty_etc'
  const [workerStatusState, setWorkerStatusState] = useState('idle'); // 'idle' | 'running' | 'completed' | 'error'
  const [showPyInfoPop, setShowPyInfoPop] = useState(false);
  const consoleBodyRef = useRef(null);
  const wkTermRef = useRef(null);
  const workerPollTimer = useRef(null);
  const workerElapsedTimer = useRef(null);
  const workerHubConnection = useRef(null);

  // Phase D: 엑셀 다운로드 / 업로드 미리보기 / 보기 불러오기 상태
  const [isExportingExcel, setIsExportingExcel] = useState(false);
  const [showExcelModal, setShowExcelModal] = useState(false);
  const [excelPreviewData, setExcelPreviewData] = useState(null);
  const [excelUploadedFileName, setExcelUploadedFileName] = useState('');
  const [excelApplyMode, setExcelApplyMode] = useState('merge'); // 'merge' | 'overwrite'
  const [isUploadingExcel, setIsUploadingExcel] = useState(false);
  const excelFileInputRef = useRef(null);

  const [showImportModal, setShowImportModal] = useState(false);
  const [importSearch, setImportSearch] = useState('');
  const [importProjectList, setImportProjectList] = useState([]);
  const [selectedImportItem, setSelectedImportItem] = useState(null);
  const [importCodes, setImportCodes] = useState([]);
  const [selectedImportCodes, setSelectedImportCodes] = useState(new Set());
  const [isLoadingImportList, setIsLoadingImportList] = useState(false);
  const [isLoadingImportCodes, setIsLoadingImportCodes] = useState(false);
  const [isApplyingImport, setIsApplyingImport] = useState(false);

  // 저장 및 DB 비교 상태
  const [duplicateRemove, setDuplicateRemove] = useState(false);
  const [initialDuplicateRemove, setInitialDuplicateRemove] = useState(false);
  const isDuplicateRemoveDirty = duplicateRemove !== initialDuplicateRemove;
  const [showSaveModal, setShowSaveModal] = useState(false);
  const [saveLoading, setSaveLoading] = useState(false);
  const [saveDiff, setSaveDiff] = useState(null);
  const [saveWithUncoded, setSaveWithUncoded] = useState(false);
  const [saveUncodedFallbackCode, setSaveUncodedFallbackCode] = useState('99');

  // 상태 필터 및 마우스 다중 선택
  const [statusFilter, setStatusFilter] = useState('all'); // all | un | none | ed | multi
  const [selectedRowKeys, setSelectedRowKeys] = useState(new Set());
  const dragAnchorRef = useRef(null);
  const isDraggingRef = useRef(false);
  const [sentPopRowKey, setSentPopRowKey] = useState(null); // 감성 퀵 선택 팝오버 열림 rowKey
  const [sentPopPos, setSentPopPos] = useState({ x: 0, y: 0, openUp: false }); // 감성 팝오버 fixed 화면 좌표
  const [colPopPos, setColPopPos] = useState({ x: 0, y: 0, openUp: false }); // 컬럼 설정 팝오버 fixed 화면 좌표

  // 스마트 콤보 드롭다운 활성 상태 ({ targetKey, field, realIdx }) [요구사항 2, 3]
  const [activeCombo, setActiveCombo] = useState(null);

  // 보기 3단 열 선택 상태
  const [selectedL1, setSelectedL1] = useState(null);
  const [selectedL2, setSelectedL2] = useState(null);

  // Phase B: 보기 데이터 3단 열 범위 및 필터 상태
  const [cScope, setCScope] = useState('b'); // 'b' (선택 중분류) | 'a' (선택 대분류) | 'all' (전체)
  const [cTypeF, setCTypeF] = useState(new Set()); // '설문지' | '자동분석' | '트래킹'
  const [cLockF, setCLockF] = useState(null); // null (전체) | true (수정불가) | false (수정가능)

  // Phase B: 3단 열 항목 다중 선택 상태
  const [cSel, setCSel] = useState({ a: new Set(), b: new Set(), c: new Set() });
  const cAnchorRef = useRef({ a: null, b: null, c: null });

  // Phase B: 보기유형별 정책 (수정불가/수정가능 기본값 토글)
  const [typePolicies, setTypePolicies] = useState({
    '설문지': true,
    '자동분석': false,
    '트래킹': true
  });

  // Phase B: 보기유형/수정여부 변경 팝오버 (#srcpop) 상태
  const [showSrcPop, setShowSrcPop] = useState(false);
  const [srcPopPos, setSrcPopPos] = useState({ x: 0, y: 0 });
  const [srcPopTarget, setSrcPopTarget] = useState(null);

  // 우측 패널 대분류 선택 필터 & 인라인 편집 상태
  const [selectedPickL1, setSelectedPickL1] = useState(''); // '' = 전체
  const [editingL1Name, setEditingL1Name] = useState(false);
  const [inputL1Name, setInputL1Name] = useState('');

  // 새 소분류 인라인 추가 상태
  const [newSubName, setNewSubName] = useState('');
  const [newSubCode, setNewSubCode] = useState('');
  const [newSubL1, setNewSubL1] = useState('');
  const [newSubL2, setNewSubL2] = useState('');

  // 소분류 인라인 수정 상태 (codeId -> newName)
  const [editingSubCodeId, setEditingSubCodeId] = useState(null);
  const [editingSubName, setEditingSubName] = useState('');

  // 스마트 그리드 편집 및 고속 다중 선택 상태
  const [gridSelectedCodes, setGridSelectedCodes] = useState(new Set());
  const [gridSearch, setGridSearch] = useState('');
  const [gridSort, setGridSort] = useState('freq'); // 'freq' | 'l1' | 'code' | 'name'
  const [gridBulkParentCol, setGridBulkParentCol] = useState('lv1');
  const [gridBulkParentVal, setGridBulkParentVal] = useState('');
  const gridAnchorIdxRef = useRef(null);   // Shift 범위 선택 기준 앵커 인덱스
  const isGridDraggingRef = useRef(false);  // 마우스 드래그 중 여부
  const gridDragModeRef = useRef('check');  // 'check' 또는 'uncheck'
  const gridTableWrapRef = useRef(null);    // 스크롤 컨테이너 ref
  const [showQuickSelectPop, setShowQuickSelectPop] = useState(false); // 빠른 선택 도우미 팝오버
  const [rangeStartVal, setRangeStartVal] = useState('1'); // 범위 시작
  const [rangeEndVal, setRangeEndVal] = useState('30'); // 범위 끝

  // API 데이터 상태
  const [loadingInfo, setLoadingInfo] = useState(false);
  const [infoData, setInfoData] = useState(null);
  const [headerMeta, setHeaderMeta] = useState({ lvcode: '', duplicateRemoveDate: '' });

  // 로컬 편집 설정 상태
  const [qName, setQName] = useState('');
  const [promptText, setPromptText] = useState('');
  const [selectedModel, setSelectedModel] = useState('');
  const [selectedLang, setSelectedLang] = useState('');
  const [temperature, setTemperature] = useState(0.2);
  const [openLv1, setOpenLv1] = useState('0~50'); // 레거시 규격 lv1: 소분류
  const [openLv2, setOpenLv2] = useState('0');    // 레거시 규격 lv2: 중분류
  const [openLv3, setOpenLv3] = useState('0');    // 레거시 규격 lv3: 대분류

  const [loadingData, setLoadingData] = useState(false);
  const [responses, setResponses] = useState([]);
  const [codes, setCodes] = useState([]);
  const [codesDirty, setCodesDirty] = useState(false);
  const [rawdata, setRawdata] = useState([]);

  // 토스트 메시지
  const [toastMsg, setToastMsg] = useState('');
  const toastTimer = useRef(null);
  const showToast = (msg) => {
    setToastMsg(msg);
    clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToastMsg(''), 2200);
  };

  // 전역 마우스 업 이벤트로 드래그 종료 처리
  useEffect(() => {
    const handleGlobalMouseUp = () => {
      isDraggingRef.current = false;
    };
    window.addEventListener('mouseup', handleGlobalMouseUp);
    return () => window.removeEventListener('mouseup', handleGlobalMouseUp);
  }, []);

  // 문항 선택 변경
  const handleSelectQnum = (idx) => {
    if (idx >= 0 && idx < navList.length) {
      setCurrentIdx(idx);
      setShowQPop(false);
      setSelectedRowKeys(new Set());
      setStatusFilter('all');
      handleResetColFilters();
      setSortConfig({ key: null, dir: null });
      handleResetRawColFilters();
      setRawSortConfig({ key: null, dir: null });
      setSelectedL1(null);
      setSelectedL2(null);
      setSelectedPickL1('');
      try {
        sessionStorage.setItem('lastAnalysisTarget', JSON.stringify(navList[idx]));
      } catch {}
    }
  };

  // 문항 데이터 호출
  useEffect(() => {
    if (!projectnum || !currentQnum) return;

    const abortController = new AbortController();
    const signal = abortController.signal;

    setLoadingInfo(true);
    setLoadingData(true);
    setSelectedRowKeys(new Set());

    // 1. info 호출
    getAnalysisInfo(projectnum, currentQnum, user, signal)
      .then(res => {
        if (signal.aborted) return;
        setLoadingInfo(false);
        if (res && (res.success === '200' || res.success === true || Array.isArray(res.resultjson))) {
          const items = Array.isArray(res.resultjson) ? res.resultjson : (Array.isArray(res) ? res : []);
          const first = items[0] || {};
          setInfoData(first);
          const dupDate = res.duplicateRemoveDate || first.duplicate_remove_date || '';
          setHeaderMeta({
            lvcode: res.lvcode || '',
            duplicateRemoveDate: dupDate
          });
          const hasDupDate = Boolean(dupDate && dupDate.trim() !== '');
          setDuplicateRemove(hasDupDate);
          setInitialDuplicateRemove(hasDupDate);

          // 기본 단계 설정
          const lv = first.lvcode || res.lvcode || '3';
          if (lv === '1' || lv === '2' || lv === '3') {
            setLevelMode(lv);
            setInitialLevelMode(lv);
          } else {
            setLevelMode('3');
            setInitialLevelMode('3');
          }

          // 설정 폼 초기화
          setQName(first.question_fin || currentItem.question_title || '');
          setPromptText(first.prompt_string || '');
          setTemperature(parseFloat(first.temperature) || 0.2);
          setOpenLv1(first.open_item_lv1 || '0~50'); // lv1: 소분류
          setOpenLv2(first.open_item_lv2 || '0');    // lv2: 중분류
          setOpenLv3(first.open_item_lv3 || '0');    // lv3: 대분류

          // 모델 기본 선택
          if (Array.isArray(first.model_select) && first.model_select.length > 0) {
            const sel = first.model_select.find(m => m.keyselected === m.keyvalue) || first.model_select[0];
            setSelectedModel(sel.keyvalue || '');
          } else {
            setSelectedModel('');
          }

          // 언어 기본 선택
          if (Array.isArray(first.result_lang) && first.result_lang.length > 0) {
            const sel = first.result_lang.find(l => l.keyselected === l.keyvalue) || first.result_lang[0];
            setSelectedLang(sel.keyvalue || '한국어');
          } else {
            setSelectedLang('한국어');
          }

          // 팝업 체크
          if (first.qid) {
            checkAnalysisPopup(projectnum, currentQnum, first.qid, '0', signal).catch(() => {});
          }
        } else {
          showToast(res?.message || '문항 설정을 불러오지 못했습니다.');
        }
      })
      .catch(err => {
        if (!signal.aborted) setLoadingInfo(false);
      });

    // 2. responses, codes, rawdata 병렬 호출
    Promise.all([
      getAnalysisResponses(projectnum, currentQnum, signal),
      getAnalysisCodes(projectnum, currentQnum, signal),
      getAnalysisRawdata(projectnum, currentQnum, signal)
    ])
      .then(([respRes, codeRes, rawRes]) => {
        if (signal.aborted) return;
        setLoadingData(false);

        // responses
        const rawResponses = (respRes && Array.isArray(respRes.resultjson)) 
          ? respRes.resultjson 
          : (Array.isArray(respRes) ? respRes : []);
        setResponses(ensureRowUids(rawResponses));

        // codes
        if (codeRes && Array.isArray(codeRes.resultjson)) {
          setCodes(codeRes.resultjson);
        } else if (Array.isArray(codeRes)) {
          setCodes(codeRes);
        } else {
          setCodes([]);
        }

        // rawdata
        if (rawRes && Array.isArray(rawRes.resultjson)) {
          setRawdata(rawRes.resultjson);
        } else if (Array.isArray(rawRes)) {
          setRawdata(rawRes);
        } else {
          setRawdata([]);
        }
      })
      .catch(err => {
        if (!signal.aborted) setLoadingData(false);
      });

    return () => {
      abortController.abort();
    };
  }, [projectnum, currentQnum, user]);

  // rawdata 전용 재조회 함수
  const fetchRawdata = useCallback(async (showLoading = false) => {
    if (showLoading) setLoadingData(true);
    try {
      const rawRes = await getAnalysisRawdata(projectnum, currentQnum);
      const items = (rawRes && Array.isArray(rawRes.resultjson)) 
        ? rawRes.resultjson 
        : (Array.isArray(rawRes) ? rawRes : []);
      setRawdata(items);
      return items;
    } catch (err) {
      console.warn('rawdata 재조회 실패:', err);
      return [];
    } finally {
      if (showLoading) setLoadingData(false);
    }
  }, [projectnum, currentQnum]);

  // 전체 데이터 일괄 새로고침 (responses, codes, rawdata)
  const handleRefreshAll = useCallback(async (silent = false) => {
    if (!silent) setLoadingData(true);
    try {
      const [respRes, codeRes, rawRes] = await Promise.all([
        getAnalysisResponses(projectnum, currentQnum),
        getAnalysisCodes(projectnum, currentQnum),
        getAnalysisRawdata(projectnum, currentQnum)
      ]);

      const rawResponses = (respRes && Array.isArray(respRes.resultjson)) 
        ? respRes.resultjson 
        : (Array.isArray(respRes) ? respRes : []);
      setResponses(ensureRowUids(rawResponses));

      if (codeRes && Array.isArray(codeRes.resultjson)) {
        setCodes(codeRes.resultjson);
      } else if (Array.isArray(codeRes)) {
        setCodes(codeRes);
      } else {
        setCodes([]);
      }

      if (rawRes && Array.isArray(rawRes.resultjson)) {
        setRawdata(rawRes.resultjson);
      } else if (Array.isArray(rawRes)) {
        setRawdata(rawRes);
      } else {
        setRawdata([]);
      }

      if (!silent) {
        showToast('데이터가 최신 상태로 갱신되었습니다.');
      }
    } catch (err) {
      if (!silent) showToast('데이터 새로고침 중 오류가 발생했습니다.');
    } finally {
      if (!silent) setLoadingData(false);
    }
  }, [projectnum, currentQnum]);

  // 옵션 리스트
  const modelOptions = useMemo(() => Array.isArray(infoData?.model_select) ? infoData.model_select : [], [infoData]);
  const langOptions = useMemo(() => Array.isArray(infoData?.result_lang) ? infoData.result_lang : [], [infoData]);

  // 설정 한 줄 요약
  const summaryLine = useMemo(() => {
    const curModel = modelOptions.find(m => m.keyvalue === selectedModel)?.keytext || selectedModel || '기본 모델';
    const curLang = langOptions.find(l => l.keyvalue === selectedLang)?.keytext || selectedLang || '한국어';
    const cls = [
      openLv1 && openLv1 !== '0' ? `대 ${openLv1}` : null,
      openLv2 && openLv2 !== '0' ? `중 ${openLv2}` : null,
      `소 ${openLv3 || '0'}`
    ].filter(Boolean).join(' / ');
    return `모델: ${curModel} · 언어: ${curLang} · 창의성 ${temperature.toFixed(1)} · 분류 ${cls}`;
  }, [modelOptions, langOptions, selectedModel, selectedLang, temperature, openLv1, openLv2, openLv3]);

  // 고유 대분류 목록
  const uniqueL1List = useMemo(() => {
    const set = new Set();
    codes.forEach(c => { if (c.lv1 && c.lv1.trim()) set.add(c.lv1.trim()); });
    return Array.from(set);
  }, [codes]);

  // 고유 중분류 목록
  const uniqueL2List = useMemo(() => {
    const set = new Set();
    codes.forEach(c => {
      if ((!newSubL1 || c.lv1 === newSubL1) && c.lv2 && c.lv2.trim()) {
        set.add(c.lv2.trim());
      }
    });
    return Array.from(set);
  }, [codes, newSubL1]);

  // 스마트 콤보 드롭다운용 고유 대분류 목록 및 코드 매핑 [요구사항 3]
  const l1Items = useMemo(() => {
    const map = new Map();
    codes.forEach(c => {
      const l1 = (c.lv1 || '').trim();
      if (l1 && !map.has(l1)) {
        map.set(l1, { name: l1, code: c.lv1code || '' });
      }
    });
    return Array.from(map.values());
  }, [codes]);

  // 대분류명에 따른 소속 중분류 목록 및 코드 필터링 반환 (Cascading) [요구사항 2]
  const getL2ItemsForL1 = useCallback((lv1Name) => {
    const map = new Map();
    const trimmedL1 = (lv1Name || '').trim();
    codes.forEach(c => {
      const cL1 = (c.lv1 || '').trim();
      const l2 = (c.lv2 || '').trim();
      if (!l2) return;
      // 대분류가 지정되어 있다면 해당 대분류 소속만, 비어있다면 전체 중분류
      if (!trimmedL1 || cL1 === trimmedL1) {
        if (!map.has(l2)) {
          map.set(l2, { name: l2, code: c.lv2code || '' });
        }
      }
    });
    return Array.from(map.values());
  }, [codes]);

  // Phase A: 중분류 옵션 쌍 (quickAddL1L2)
  const l1l2Options = useMemo(() => {
    const map = new Map();
    codes.forEach(c => {
      const l1 = c.lv1 || '기타';
      const l2 = c.lv2 || '기타';
      const key = `${l1}__sep__${l2}`;
      if (!map.has(key)) {
        map.set(key, { key, l1, l2, label: `${l1} › ${l2}` });
      }
    });
    return Array.from(map.values());
  }, [codes]);

  useEffect(() => {
    if (l1l2Options.length > 0 && !quickAddL1L2) {
      setQuickAddL1L2(l1l2Options[0].key);
    }
  }, [l1l2Options, quickAddL1L2]);

  // Phase A: 검색어와 일치하는 소분류가 없을 때 새 소분류 즉시 추가 배너 표시 여부
  const showQuickAdd = useMemo(() => {
    const q = cbSearch.trim().toLowerCase();
    if (!q) return false;
    return !codes.some(c => (c.lv3 || c.code_name || '').toLowerCase() === q);
  }, [cbSearch, codes]);

  // Phase A: 팝오버 필터링 후보군 (#pop)
  const popCandidates = useMemo(() => {
    const q = popSearch.trim().toLowerCase();
    if (!q) return codes;
    return codes.filter(c => {
      const str = `${c.lv123code || c.code_num || ''} ${c.lv3 || c.code_name || ''} ${c.lv2 || ''} ${c.lv1 || ''}`.toLowerCase();
      return str.includes(q);
    });
  }, [codes, popSearch]);

  // 확인 수 통계
  const okCount = useMemo(() => {
    return responses.filter(r => r.recheckyn === 'Y' || r.recheckyn === '1').length;
  }, [responses]);

  const okPercent = responses.length > 0 ? Math.round((okCount / responses.length) * 100) : 0;

  // 필터 통계 건수
  const filterCounts = useMemo(() => {
    return {
      all: responses.length,
      un: responses.filter(r => r.recheckyn !== 'Y' && r.recheckyn !== '1').length,
      none: responses.filter(r => !r.lv123code && !r.lv3).length,
      ed: responses.filter(r => r._edited).length,
      multi: responses.filter(r => r.cid && r.cid !== '1').length
    };
  }, [responses]);

  // 응답 필터링 및 3단계 정렬 파이프라인
  const filteredResponses = useMemo(() => {
    // 1. 상태 칩 필터 & 열별 개별 검색 (AND 조건 결합)
    let list = responses.filter(r => {
      if (statusFilter === 'un' && (r.recheckyn === 'Y' || r.recheckyn === '1')) return false;
      if (statusFilter === 'none' && (r.lv123code || r.lv3)) return false;

      // 열별 개별 검색 (AND)
      if (colFilters.raw.trim()) {
        const q = colFilters.raw.trim().toLowerCase();
        if (!r.answer_origin?.toLowerCase().includes(q)) return false;
      }
      if (colFilters.clean.trim()) {
        const q = colFilters.clean.trim().toLowerCase();
        const cleanText = (r.answer || r.answer_origin || '').toLowerCase();
        if (!cleanText.includes(q)) return false;
      }
      if (colFilters.multi.trim()) {
        const q = colFilters.multi.trim();
        const cid = String(r.cid || '1');
        if (!cid.includes(q)) return false;
      }
      if (colFilters.l3.trim()) {
        const q = colFilters.l3.trim().toLowerCase();
        const codeStr = `${r.lv123code || ''} ${r.lv3 || ''}`.toLowerCase();
        if (!codeStr.includes(q)) return false;
      }
      if (colFilters.l1.trim()) {
        const q = colFilters.l1.trim().toLowerCase();
        const l1Str = `${r.lv1code || ''} ${r.lv1 || ''}`.toLowerCase();
        if (!l1Str.includes(q)) return false;
      }
      if (colFilters.l2.trim()) {
        const q = colFilters.l2.trim().toLowerCase();
        const l2Str = `${r.lv2code || ''} ${r.lv2 || ''}`.toLowerCase();
        if (!l2Str.includes(q)) return false;
      }
      if (colFilters.sent !== 'all') {
        if ((r.sentiment || '') !== colFilters.sent) return false;
      }
      if (colFilters.ok !== 'all') {
        const isOk = r.recheckyn === 'Y' || r.recheckyn === '1';
        if (colFilters.ok === 'Y' && !isOk) return false;
        if (colFilters.ok === 'N' && isOk) return false;
      }

      return true;
    });

    // 2. 열별 3단계 토글 정렬
    if (sortConfig.key && sortConfig.dir) {
      const { key, dir } = sortConfig;
      const factor = dir === 'asc' ? 1 : -1;

      list = [...list].sort((a, b) => {
        if (key === 'multi') {
          return (Number(a.cid || 1) - Number(b.cid || 1)) * factor;
        }

        let valA = '';
        let valB = '';
        if (key === 'raw') {
          valA = a.answer_origin || '';
          valB = b.answer_origin || '';
        } else if (key === 'clean') {
          valA = a.answer || a.answer_origin || '';
          valB = b.answer || b.answer_origin || '';
        } else if (key === 'l3') {
          valA = a.lv3 || a.lv123code || '';
          valB = b.lv3 || b.lv123code || '';
        } else if (key === 'l1') {
          valA = a.lv1 || '';
          valB = b.lv1 || '';
        } else if (key === 'l2') {
          valA = a.lv2 || '';
          valB = b.lv2 || '';
        } else if (key === 'sent') {
          valA = a.sentiment || '';
          valB = b.sentiment || '';
        } else if (key === 'ok') {
          valA = (a.recheckyn === 'Y' || a.recheckyn === '1') ? '1' : '0';
          valB = (b.recheckyn === 'Y' || b.recheckyn === '1') ? '1' : '0';
        }

        return valA.localeCompare(valB, 'ko-KR', { numeric: true }) * factor;
      });
    }

    return list;
  }, [responses, statusFilter, colFilters, sortConfig]);

  // 테이블 헤더 열별 3단계 토글 정렬 버튼 렌더러 (오름차순 -> 내림차순 -> 해제)
  const renderSortBtn = (colKey) => {
    const isCur = sortConfig.key === colKey;
    const dir = isCur ? sortConfig.dir : null;
    const iconCls = !dir 
      ? 'ti ti-arrows-sort' 
      : (dir === 'asc' ? 'ti ti-sort-ascending on' : 'ti ti-sort-descending on');
    const title = !dir 
      ? '클릭 시 오름차순 정렬' 
      : (dir === 'asc' ? '클릭 시 내림차순 정렬' : '클릭 시 정렬 해제');

    return (
      <button
        type="button"
        className={`an-th-sort-btn${dir ? ' active' : ''}`}
        title={title}
        onClick={(e) => {
          e.stopPropagation();
          handleToggleSort(colKey);
        }}
      >
        <i className={iconCls} />
      </button>
    );
  };

  // PID별 데이터 테이블 헤더 열별 3단계 토글 정렬 버튼 렌더러
  const renderRawSortBtn = (colKey) => {
    const isCur = rawSortConfig.key === colKey;
    const dir = isCur ? rawSortConfig.dir : null;
    const iconCls = !dir 
      ? 'ti ti-arrows-sort' 
      : (dir === 'asc' ? 'ti ti-sort-ascending on' : 'ti ti-sort-descending on');
    const title = !dir 
      ? '클릭 시 오름차순 정렬' 
      : (dir === 'asc' ? '클릭 시 내림차순 정렬' : '클릭 시 정렬 해제');

    return (
      <button
        type="button"
        className={`an-th-sort-btn${dir ? ' active' : ''}`}
        title={title}
        onClick={(e) => {
          e.stopPropagation();
          handleToggleRawSort(colKey);
        }}
      >
        <i className={iconCls} />
      </button>
    );
  };

  // PID별 데이터: 소코드별 출현 빈도수 매핑 (Map<code, count>)
  const rawCodeFreqMap = useMemo(() => {
    const map = new Map();
    if (Array.isArray(rawdata)) {
      rawdata.forEach(r => {
        const code = String(r.lv123code || r.code_num || '').trim();
        if (code) {
          map.set(code, (map.get(code) || 0) + 1);
        }
      });
    }
    return map;
  }, [rawdata]);

  // PID별 데이터: 열별 AND 검색 및 3단계 정렬 파이프라인
  const filteredRawdata = useMemo(() => {
    let list = (rawdata || []).filter(r => {
      if (rawColFilters.pid.trim()) {
        const q = rawColFilters.pid.trim().toLowerCase();
        if (!String(r.pid || '').toLowerCase().includes(q)) return false;
      }
      if (rawColFilters.cid.trim()) {
        const q = rawColFilters.cid.trim();
        const cid = String(r.cid || '1');
        if (!cid.includes(q)) return false;
      }
      if (rawColFilters.answer.trim()) {
        const q = rawColFilters.answer.trim().toLowerCase();
        const ans = String(r.answerfin || r.answer_origin || '').toLowerCase();
        if (!ans.includes(q)) return false;
      }
      if (rawColFilters.l1.trim()) {
        const q = rawColFilters.l1.trim().toLowerCase();
        if (!String(r.lv1 || '').toLowerCase().includes(q)) return false;
      }
      if (rawColFilters.l2.trim()) {
        const q = rawColFilters.l2.trim().toLowerCase();
        if (!String(r.lv2 || '').toLowerCase().includes(q)) return false;
      }
      if (rawColFilters.freq.trim()) {
        const q = rawColFilters.freq.trim();
        const code = String(r.lv123code || r.code_num || '').trim();
        const freq = code ? String(rawCodeFreqMap.get(code) || 0) : '';
        if (!freq.includes(q)) return false;
      }
      if (rawColFilters.code.trim()) {
        const q = rawColFilters.code.trim().toLowerCase();
        const code = String(r.lv123code || r.code_num || '').toLowerCase();
        if (!code.includes(q)) return false;
      }
      if (rawColFilters.l3.trim()) {
        const q = rawColFilters.l3.trim().toLowerCase();
        if (!String(r.lv3 || '').toLowerCase().includes(q)) return false;
      }
      if (rawColFilters.sent !== 'all') {
        if ((r.sentiment || '') !== rawColFilters.sent) return false;
      }
      if (rawColFilters.date.trim()) {
        const q = rawColFilters.date.trim().toLowerCase();
        if (!String(r.firstWriteDate || '').toLowerCase().includes(q)) return false;
      }
      return true;
    });

    if (rawSortConfig.key && rawSortConfig.dir) {
      const { key, dir } = rawSortConfig;
      const factor = dir === 'asc' ? 1 : -1;

      list = [...list].sort((a, b) => {
        if (key === 'cid') {
          return (Number(a.cid || 1) - Number(b.cid || 1)) * factor;
        }
        if (key === 'freq') {
          const codeA = String(a.lv123code || a.code_num || '').trim();
          const codeB = String(b.lv123code || b.code_num || '').trim();
          const freqA = codeA ? (rawCodeFreqMap.get(codeA) || 0) : 0;
          const freqB = codeB ? (rawCodeFreqMap.get(codeB) || 0) : 0;
          return (freqA - freqB) * factor;
        }

        let valA = '';
        let valB = '';
        if (key === 'pid') {
          valA = String(a.pid || '');
          valB = String(b.pid || '');
        } else if (key === 'answer') {
          valA = a.answerfin || a.answer_origin || '';
          valB = b.answerfin || b.answer_origin || '';
        } else if (key === 'l1') {
          valA = a.lv1 || '';
          valB = b.lv1 || '';
        } else if (key === 'l2') {
          valA = a.lv2 || '';
          valB = b.lv2 || '';
        } else if (key === 'code') {
          valA = String(a.lv123code || a.code_num || '');
          valB = String(b.lv123code || b.code_num || '');
        } else if (key === 'l3') {
          valA = a.lv3 || '';
          valB = b.lv3 || '';
        } else if (key === 'sent') {
          valA = a.sentiment || '';
          valB = b.sentiment || '';
        } else if (key === 'date') {
          valA = a.firstWriteDate || '';
          valB = b.firstWriteDate || '';
        }

        return valA.localeCompare(valB, 'ko-KR', { numeric: true }) * factor;
      });
    }

    return list;
  }, [rawdata, rawColFilters, rawSortConfig, rawCodeFreqMap]);

  // -------------------------------------------------------------
  // 마우스 드래그, Shift, Ctrl 다중 선택 로직
  // -------------------------------------------------------------

  const getRowKey = (r, idx) => {
    if (!r) return `r-${idx}`;
    if (r._uid) return r._uid;
    const cid = r.cid != null && r.cid !== '' ? r.cid : '1';
    return r.fixed_key ? `${r.fixed_key}__c${cid}` : `r-${idx}`;
  };

  const rangeSelect = (startIdx, endIdx) => {
    const min = Math.min(startIdx, endIdx);
    const max = Math.max(startIdx, endIdx);
    const newKeys = new Set(selectedRowKeys);
    for (let i = min; i <= max; i++) {
      if (filteredResponses[i]) {
        newKeys.add(getRowKey(filteredResponses[i], i));
      }
    }
    setSelectedRowKeys(newKeys);
  };

  const handleRowMouseDown = (index, e) => {
    // 버튼, 인라인 클릭 시 행 선택 방지
    if (e.target.closest('button') || e.target.closest('.an-sent') || e.target.closest('.an-okbtn') || e.target.closest('.an-cchip') || e.target.closest('.an-none')) {
      return;
    }

    const rowKey = getRowKey(filteredResponses[index], index);

    if (e.shiftKey && dragAnchorRef.current !== null) {
      rangeSelect(dragAnchorRef.current, index);
      return;
    }

    if (e.ctrlKey || e.metaKey) {
      setSelectedRowKeys(prev => {
        const next = new Set(prev);
        if (next.has(rowKey)) next.delete(rowKey);
        else next.add(rowKey);
        return next;
      });
      dragAnchorRef.current = index;
      return;
    }

    // 일반 마우스다운: 단일 선택 및 드래그 시작
    setSelectedRowKeys(new Set([rowKey]));
    dragAnchorRef.current = index;
    isDraggingRef.current = true;
  };

  const handleRowMouseEnter = (index) => {
    if (!isDraggingRef.current || dragAnchorRef.current === null) return;
    rangeSelect(dragAnchorRef.current, index);
  };

  // 선택 해제
  const clearSelection = () => {
    setSelectedRowKeys(new Set());
    dragAnchorRef.current = null;
  };

  // 감성 팝오버 및 컬럼 팝오버 외부 클릭 및 스크롤 시 닫기
  useEffect(() => {
    if (!sentPopRowKey && !showColPop) return;
    const handleClose = (e) => {
      if (e && e.target && e.target.closest) {
        if (e.target.closest('.an-fpop') || e.target.closest('#colpopbtn')) return;
      }
      if (sentPopRowKey) setSentPopRowKey(null);
      if (showColPop) setShowColPop(false);
    };
    window.addEventListener('click', handleClose);
    window.addEventListener('scroll', handleClose, true);
    return () => {
      window.removeEventListener('click', handleClose);
      window.removeEventListener('scroll', handleClose, true);
    };
  }, [sentPopRowKey, showColPop]);

  // [컬럼] 버튼 클릭 핸들러 (Fixed 최상위 플로팅 위치 계산)
  const handleOpenColPop = (e) => {
    e.stopPropagation();
    if (showColPop) {
      setShowColPop(false);
      return;
    }
    const rect = e.currentTarget.getBoundingClientRect();
    const openUp = (window.innerHeight - rect.bottom) < 320;
    setColPopPos({
      x: rect.left,
      y: openUp ? (rect.top - 6) : (rect.bottom + 6),
      openUp
    });
    setShowColPop(true);
  };

  // 감성 팝오버 열기/닫기 토글 (화면 기준 위치 계산)
  const handleOpenSentimentPop = (rowKey, e) => {
    if (e) {
      e.stopPropagation();
      const rect = e.currentTarget.getBoundingClientRect();
      const openUp = (window.innerHeight - rect.bottom) < 140;
      setSentPopPos({
        x: rect.left + rect.width / 2,
        y: openUp ? (rect.top - 6) : (rect.bottom + 6),
        openUp
      });
    }
    setSentPopRowKey(prev => prev === rowKey ? null : rowKey);
  };

  // 감성 선택 적용 (다중 선택 시 일괄 반영)
  const handleApplySentiment = (targetSentiment, triggerRowKey) => {
    const isMulti = selectedRowKeys.size > 1 && selectedRowKeys.has(triggerRowKey);
    const targetKeys = isMulti ? new Set(selectedRowKeys) : new Set([triggerRowKey]);

    setResponses(prev => prev.map((r, i) => {
      const k = getRowKey(r, i);
      if (targetKeys.has(k)) {
        return { ...r, sentiment: targetSentiment, _edited: true };
      }
      return r;
    }));

    const labelMap = { pos: '긍정', neg: '부정', neu: '중립' };
    if (isMulti) {
      showToast(`선택된 ${targetKeys.size}건의 응답 감성을 [${labelMap[targetSentiment]}](으)로 일괄 변경했습니다.`);
    }
    setSentPopRowKey(null);
  };

  // 감성 순환 토글 (다중 선택 시 일괄 순환 변경)
  const handleToggleSentiment = (rowKey, e) => {
    if (e) e.stopPropagation();
    const isMulti = selectedRowKeys.size > 1 && selectedRowKeys.has(rowKey);
    const targetKeys = isMulti ? new Set(selectedRowKeys) : new Set([rowKey]);

    const curRow = responses.find((r, i) => getRowKey(r, i) === rowKey);
    const cur = curRow?.sentiment;
    const next = cur === 'pos' ? 'neg' : cur === 'neg' ? 'neu' : 'pos';

    setResponses(prev => prev.map((r, i) => {
      const k = getRowKey(r, i);
      if (targetKeys.has(k)) {
        return { ...r, sentiment: next, _edited: true };
      }
      return r;
    }));

    const labelMap = { pos: '긍정', neg: '부정', neu: '중립' };
    if (isMulti) {
      showToast(`선택된 ${targetKeys.size}건의 감성을 [${labelMap[next]}](으)로 일괄 변경했습니다.`);
    }
  };

  // 확인 토글 (다중 선택 시 일괄 체크 / 일괄 해제)
  const handleToggleCheck = (rowKey, e) => {
    if (e) e.stopPropagation();
    const isMulti = selectedRowKeys.size > 1 && selectedRowKeys.has(rowKey);
    const targetKeys = isMulti ? new Set(selectedRowKeys) : new Set([rowKey]);

    const curRow = responses.find((r, i) => getRowKey(r, i) === rowKey);
    const curOk = curRow?.recheckyn === 'Y' || curRow?.recheckyn === '1';
    const nextVal = curOk ? 'N' : 'Y';

    setResponses(prev => prev.map((r, i) => {
      const k = getRowKey(r, i);
      if (targetKeys.has(k)) {
        return { ...r, recheckyn: nextVal, _edited: true };
      }
      return r;
    }));

    if (isMulti) {
      showToast(`선택된 ${targetKeys.size}건의 응답을 일괄 ${nextVal === 'Y' ? '확인 처리' : '확인 해제'}했습니다.`);
    }
  };

  // 헤더 일괄 확인 / 일괄 해제
  const handleBulkCheck = (val) => {
    const targetKeys = selectedRowKeys.size > 0 ? selectedRowKeys : new Set(filteredResponses.map((r, i) => getRowKey(r, i)));
    if (targetKeys.size === 0) {
      showToast('대상 행이 없습니다.');
      return;
    }
    const checkVal = val ? 'Y' : 'N';
    setResponses(prev => prev.map((r, i) => {
      const k = getRowKey(r, i);
      if (targetKeys.has(k)) {
        return { ...r, recheckyn: checkVal, _edited: true };
      }
      return r;
    }));
    showToast(`${targetKeys.size}건을 ${val ? '확인' : '확인 해제'} 처리했습니다.`);
  };

  // 멀티 행 추가 (원본 fixed_key 유지 + 신규 cid & _uid 부여)
  const handleAddMultiRow = (row, index, e) => {
    if (e) e.stopPropagation();
    const curCid = Number(row.cid || 1);
    const nextCid = String(curCid + 1);
    const newRow = {
      ...row,
      fixed_key: row.fixed_key,
      cid: nextCid,
      _uid: `${row.fixed_key || 'row'}_c${nextCid}_${Date.now()}`,
      lv1: '',
      lv2: '',
      lv3: '',
      lv1code: '',
      lv2code: '',
      lv123code: '',
      recheckyn: 'N',
      _edited: true
    };
    const nextList = [...responses];
    nextList.splice(index + 1, 0, newRow);
    setResponses(nextList);
    showToast(`멀티 ${nextCid} 행을 추가했습니다.`);
  };

  // 멀티 행 삭제
  const handleDeleteMultiRow = (rowKey, e) => {
    if (e) e.stopPropagation();
    if (!window.confirm('해당 멀티 행을 삭제하시겠습니까?')) return;
    setResponses(prev => prev.filter((r, i) => getRowKey(r, i) !== rowKey));
    showToast('멀티 행을 삭제했습니다.');
  };

  // ── [고속 코드 펀칭 (Quick Code Punching)] ──
  const punchInputRefs = useRef({});

  // 소분류 코드 직접 입력 시 보기 자동 매칭 및 동기화
  const handlePunchCodeChange = (row, index, newCode) => {
    const trimmed = String(newCode || '').trim();
    const rowKey = getRowKey(row, index);

    setResponses(prev => {
      const next = [...prev];
      const targetIdx = next.findIndex((r, i) => getRowKey(r, i) === rowKey);
      const applyIdx = targetIdx >= 0 ? targetIdx : index;
      if (!next[applyIdx]) return prev;

      if (!trimmed) {
        // 코드 지움 -> 미분류로 초기화
        next[applyIdx] = {
          ...next[applyIdx],
          lv1: '',
          lv1code: '',
          lv2: '',
          lv2code: '',
          lv3: '',
          lv3code: '',
          lv123code: '',
          _edited: true
        };
      } else {
        // codes 배열에서 코드 일치 보기 검색
        const matched = codes.find(c => 
          String(c.lv123code || c.code_num || '').trim() === trimmed ||
          String(c.lv3code || '').trim() === trimmed
        );

        if (matched) {
          next[applyIdx] = {
            ...next[applyIdx],
            lv1: matched.lv1 || '',
            lv1code: matched.lv1code || '',
            lv2: matched.lv2 || '',
            lv2code: matched.lv2code || '',
            lv3: matched.lv3 || '',
            lv3code: matched.lv3code || matched.lv123code || matched.code_num || '',
            lv123code: matched.lv123code || matched.code_num || trimmed,
            _edited: true
          };
        } else {
          // 일치하는 보기가 아직 없더라도 타이핑 값은 보존
          next[applyIdx] = {
            ...next[applyIdx],
            lv123code: newCode,
            _edited: true
          };
        }
      }
      return next;
    });
  };

  // 고속 코드 펀칭 키보드 단축키 핸들러 (Enter, Shift+Enter, Delete, 방향키)
  const handlePunchKeyDown = (row, index, e) => {
    // 1. Shift + Enter: 멀티 행 즉시 추가 후 신규 행으로 포커스 이동
    if (e.shiftKey && e.key === 'Enter') {
      e.preventDefault();
      e.stopPropagation();
      handleAddMultiRow(row, index);
      const newIdx = index + 1;
      setFocusedRowIdx(newIdx);
      setTimeout(() => {
        const el = punchInputRefs.current[newIdx];
        if (el) {
          el.focus();
          el.select();
          el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }
      }, 60);
      return;
    }

    // 2. 단독 Enter: 현재 코드 확정 및 다음 행으로 즉시 포커스 이동
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      const nextIdx = index + 1;
      if (nextIdx < filteredResponses.length) {
        setFocusedRowIdx(nextIdx);
        setTimeout(() => {
          const el = punchInputRefs.current[nextIdx];
          if (el) {
            el.focus();
            el.select();
            el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
          }
        }, 20);
      } else {
        showToast('마지막 응답입니다.');
      }
      return;
    }

    // 3. Delete: 행 삭제 경고창 (멀티행 삭제 또는 단일행 소분류 초기화)
    if (e.key === 'Delete') {
      e.preventDefault();
      const isMulti = Number(row.cid || 1) > 1;
      const rowKey = getRowKey(row, index);

      if (isMulti) {
        if (window.confirm(`선택한 멀티 응답 행(cid: ${row.cid})을 삭제하시겠습니까?`)) {
          setResponses(prev => prev.filter((r, i) => getRowKey(r, i) !== rowKey));
          showToast('멀티 행을 삭제했습니다.');
          const targetIdx = Math.max(0, index - 1);
          setFocusedRowIdx(targetIdx);
          setTimeout(() => {
            const el = punchInputRefs.current[targetIdx];
            if (el) {
              el.focus();
              el.select();
            }
          }, 60);
        }
        return;
      } else {
        if (window.confirm('현재 응답의 소분류 분류 코드를 삭제(미분류로 초기화)하시겠습니까?')) {
          handlePunchCodeChange(row, index, '');
          showToast('소분류 분류를 삭제(초기화)했습니다.');
        }
        return;
      }
    }

    // 4. ArrowDown: 다음 행 이동
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      const nextIdx = Math.min(filteredResponses.length - 1, index + 1);
      setFocusedRowIdx(nextIdx);
      const el = punchInputRefs.current[nextIdx];
      if (el) {
        el.focus();
        el.select();
        el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
      return;
    }

    // 5. ArrowUp: 이전 행 이동
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      const prevIdx = Math.max(0, index - 1);
      setFocusedRowIdx(prevIdx);
      const el = punchInputRefs.current[prevIdx];
      if (el) {
        el.focus();
        el.select();
        el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
      return;
    }
  };


  // 개별 행 펼치기 토글
  const handleToggleRowExpand = (id, e) => {
    if (e) e.stopPropagation();
    setExpandedRowIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  // 컬럼 폭 드래그 조절 핸들러
  const handleColResizeMouseDown = (colKey, e) => {
    e.preventDefault();
    e.stopPropagation();
    const startX = e.clientX;
    const startW = colWidths[colKey] || 120;
    resizeStateRef.current = { colKey, startX, startW };
    document.body.classList.add('resizing');

    const handleMouseMove = (ev) => {
      if (!resizeStateRef.current) return;
      const { colKey: k, startX: sx, startW: sw } = resizeStateRef.current;
      const newW = Math.max(40, sw + (ev.clientX - sx));
      setColWidths(prev => ({ ...prev, [k]: newW }));
    };

    const handleMouseUp = () => {
      resizeStateRef.current = null;
      document.body.classList.remove('resizing');
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };

    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
  };

  const handleColResizeDblClick = (colKey) => {
    const defaults = {
      raw: 176,
      clean: 176,
      multi: 44,
      l1: 92,
      l2: 124,
      l3: 0,
      sent: 64,
      ok: 62
    };
    setColWidths(prev => ({ ...prev, [colKey]: defaults[colKey] || 120 }));
    showToast('컬럼 폭을 초기화했습니다.');
  };

  // -------------------------------------------------------------
  // Phase A: 스플리터 드래그 핸들러
  // -------------------------------------------------------------
  const handleSplitterMouseDown = (e) => {
    e.preventDefault();
    const startX = e.clientX;
    const startW = cbWidth;
    setIsDraggingSplitter(true);
    document.body.classList.add('resizing');

    const handleMouseMove = (ev) => {
      // 우측 패널이므로 마우스가 왼쪽(음수 변화)으로 갈수록 패널 너비는 넓어짐
      const newW = Math.max(360, Math.min(950, startW - (ev.clientX - startX)));
      setCbWidth(newW);
    };

    const handleMouseUp = () => {
      setIsDraggingSplitter(false);
      document.body.classList.remove('resizing');
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };

    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
  };

  const handleSplitterDblClick = () => {
    setCbWidth(600);
    showToast('폭을 기본값(600px)으로 초기화했습니다');
  };

  // -------------------------------------------------------------
  // Phase A: 소분류 빠른 지정 팝오버 (#pop) 핸들러
  // -------------------------------------------------------------
  const handleOpenCategoryPop = (row, idx, e) => {
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    const rect = e.currentTarget.getBoundingClientRect();
    const left = Math.min(Math.max(8, rect.left), window.innerWidth - 352);
    const top = (rect.bottom + 6 + 340 > window.innerHeight) ? Math.max(8, rect.top - 340) : rect.bottom + 6;
    setPopPos({ x: left, y: top });
    setPopTargetRow({ row, idx, rowKey: getRowKey(row, idx) });
    setPopSearch('');
    setPopHighlightIdx(0);
    setShowPop(true);
    setTimeout(() => {
      if (popSearchInputRef.current) {
        popSearchInputRef.current.focus();
        popSearchInputRef.current.select();
      }
    }, 50);
  };

  const handlePickCode = (c) => {
    const targetKey = popTargetRow?.rowKey;
    setShowPop(false);
    if (!targetKey) return;

    const codeNum = c.lv123code || c.code_num || '';
    const codeName = c.lv3 || c.code_name || '';

    // 만약 2개 이상의 행이 선택되어 있고, 팝오버를 연 행이 선택 행에 포함되어 있다면 선택된 모든 행에 일괄 지정!
    if (selectedRowKeys.size > 1 && selectedRowKeys.has(targetKey)) {
      setResponses(prev => prev.map((r, i) => {
        const k = getRowKey(r, i);
        if (selectedRowKeys.has(k)) {
          return {
            ...r,
            lv1: c.lv1 || r.lv1,
            lv2: c.lv2 || r.lv2,
            lv3: codeName,
            lv1code: c.lv1code || r.lv1code,
            lv2code: c.lv2code || r.lv2code,
            lv3code: c.lv3code || codeNum || r.lv3code,
            lv123code: codeNum,
            _edited: true
          };
        }
        return r;
      }));
      showToast(`${selectedRowKeys.size}건에 [${codeNum}] ${codeName} 지정 완료`);
      return;
    }

    // 단일 행 지정
    setResponses(prev => prev.map((r, i) => {
      const k = getRowKey(r, i);
      if (k === targetKey) {
        return {
          ...r,
          lv1: c.lv1 || r.lv1,
          lv2: c.lv2 || r.lv2,
          lv3: codeName,
          lv1code: c.lv1code || r.lv1code,
          lv2code: c.lv2code || r.lv2code,
          lv3code: c.lv3code || codeNum || r.lv3code,
          lv123code: codeNum,
          _edited: true
        };
      }
      return r;
    }));
    showToast(`[${codeNum}] ${codeName} 지정 완료`);
  };

  const handlePopKeyDown = (e) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setPopHighlightIdx(prev => Math.min(prev + 1, popCandidates.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setPopHighlightIdx(prev => Math.max(prev - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (popCandidates[popHighlightIdx]) {
        handlePickCode(popCandidates[popHighlightIdx]);
      }
    } else if (e.key === 'Escape') {
      setShowPop(false);
    }
  };

  // 팝오버 외부 클릭 시 닫기
  useEffect(() => {
    const handleDocClick = (e) => {
      if (showPop && !e.target.closest('#pop') && !e.target.closest('.an-cchip') && !e.target.closest('.an-none')) {
        setShowPop(false);
      }
    };
    document.addEventListener('click', handleDocClick);
    return () => document.removeEventListener('click', handleDocClick);
  }, [showPop]);

  // -------------------------------------------------------------
  // Phase A: 우측 패널 새 소분류 즉시 추가 (.an-addnew)
  // -------------------------------------------------------------
  const handleQuickAdd = () => {
    const name = cbSearch.trim();
    if (!name) return;

    let l1 = selectedPickL1 || '';
    let l2 = '';
    if (quickAddL1L2) {
      const parts = quickAddL1L2.split('__sep__');
      l1 = parts[0] || l1;
      l2 = parts[1] || '';
    }

    const existingCodes = codes.map(c => Number(c.lv123code || c.code_num || 0)).filter(n => !isNaN(n) && n < 99);
    const nextCode = String(existingCodes.length ? Math.max(...existingCodes) + 1 : codes.length + 1);

    const newCodeObj = {
      lv1: l1,
      lv2: l2,
      lv3: name,
      lv123code: nextCode,
      ex_sum: 0,
      ex_gubun: '분석(수정가능)',
      gubun_name: '자동분석 보기',
      is_editable: '가능',
      is_lock: false,
      lock: false,
      src: '자동분석'
    };

    setCodes(prev => [newCodeObj, ...prev]);
    setCbSearch('');

    if (selectedRowKeys.size > 0) {
      setResponses(prev => prev.map((r, i) => {
        const k = getRowKey(r, i);
        if (selectedRowKeys.has(k)) {
          return {
            ...r,
            lv1: newCodeObj.lv1 || r.lv1,
            lv2: newCodeObj.lv2 || r.lv2,
            lv3: newCodeObj.lv3,
            lv123code: newCodeObj.lv123code,
            _edited: true
          };
        }
        return r;
      }));
      showToast(`[${nextCode}] ${name} 생성 및 ${selectedRowKeys.size}건 즉시 지정 완료!`);
    } else {
      showToast(`[${nextCode}] ${name} 새 소분류 추가 완료`);
    }
  };

  const handleCbSearchKeyDown = (e) => {
    if (e.key === 'Enter') {
      if (showQuickAdd) {
        e.preventDefault();
        handleQuickAdd();
      }
    } else if (e.key === 'Escape') {
      setCbSearch('');
      e.target.blur();
    }
  };

  // -------------------------------------------------------------
  // Phase A: 전역 키보드 단축키
  // -------------------------------------------------------------
  useEffect(() => {
    const handleKeyDown = (e) => {
      if (showPop || showSaveModal || showColPop) return;
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(e.target.tagName)) return;

      // 1. '/' 키 누르면 우측 분류 검색창 포커스
      if (e.key === '/' && activeTab !== 'code') {
        e.preventDefault();
        if (cbSearchInputRef.current) {
          cbSearchInputRef.current.focus();
          cbSearchInputRef.current.select();
        }
        return;
      }

      if (activeTab !== 'resp') return;
      if (!filteredResponses.length) return;

      const maxIdx = filteredResponses.length - 1;

      // 2. 방향키 이동 (ArrowDown / j)
      if (e.key === 'ArrowDown' || e.key === 'j') {
        e.preventDefault();
        const nextIdx = focusedRowIdx === null ? 0 : Math.min(focusedRowIdx + 1, maxIdx);
        setFocusedRowIdx(nextIdx);
        const targetKey = getRowKey(filteredResponses[nextIdx], nextIdx);
        if (e.shiftKey && dragAnchorRef.current !== null) {
          rangeSelect(dragAnchorRef.current, nextIdx);
        } else {
          dragAnchorRef.current = nextIdx;
          setSelectedRowKeys(new Set([targetKey]));
        }
      }
      // 3. 방향키 이동 (ArrowUp / k)
      else if (e.key === 'ArrowUp' || e.key === 'k') {
        e.preventDefault();
        const prevIdx = focusedRowIdx === null ? 0 : Math.max(focusedRowIdx - 1, 0);
        setFocusedRowIdx(prevIdx);
        const targetKey = getRowKey(filteredResponses[prevIdx], prevIdx);
        if (e.shiftKey && dragAnchorRef.current !== null) {
          rangeSelect(dragAnchorRef.current, prevIdx);
        } else {
          dragAnchorRef.current = prevIdx;
          setSelectedRowKeys(new Set([targetKey]));
        }
      }
      // 4. Space: 선택 토글
      else if (e.key === ' ') {
        e.preventDefault();
        if (focusedRowIdx !== null && filteredResponses[focusedRowIdx]) {
          const k = getRowKey(filteredResponses[focusedRowIdx], focusedRowIdx);
          setSelectedRowKeys(prev => {
            const next = new Set(prev);
            if (next.has(k)) next.delete(k);
            else next.add(k);
            return next;
          });
        }
      }
      // 5. Ctrl+A / Cmd+A: 전체 선택
      else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a') {
        e.preventDefault();
        const allKeys = new Set(filteredResponses.map((r, i) => getRowKey(r, i)));
        setSelectedRowKeys(allKeys);
      }
      // 6. Enter: 확인 토글
      else if (e.key === 'Enter') {
        e.preventDefault();
        if (focusedRowIdx !== null && filteredResponses[focusedRowIdx]) {
          const k = getRowKey(filteredResponses[focusedRowIdx], focusedRowIdx);
          handleToggleCheck(k);
        }
      }
      // 7. Escape: 선택 해제
      else if (e.key === 'Escape') {
        clearSelection();
        setFocusedRowIdx(null);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [showPop, showSaveModal, showColPop, activeTab, filteredResponses, focusedRowIdx, selectedRowKeys]);

  const ACTION_NAMES = {
    analyze: '보기분석 실행',
    translate: '번역 실행',
    respondent: '응답자분석 실행',
    empty_etc: '빈셀·기타 처리'
  };

  // 워커 실행 및 SignalR 실시간 스트리밍 핸들러 (worker_0427.exe & worker_api_web.aspx 호환)
  const handleRunWorker = async (actionType) => {
    if (workerRunning) {
      setShowWorkerModal(true);
      return;
    }
    const actTitle = ACTION_NAMES[actionType] || actionType;
    setWorkerActionType(actionType);
    setWorkerStatusState('running');
    setWorkerStep(1);
    setWorkerElapsed(0);
    setShowWorkerModal(true);
    setShowConsole(true);
    setSetOpen(true);
    setConsoleLines([`[${new Date().toLocaleTimeString()}] 작업 시작 요청 (${actTitle})...`]);
    setWorkerRunning(true);

    if (workerElapsedTimer.current) clearInterval(workerElapsedTimer.current);
    workerElapsedTimer.current = setInterval(() => {
      setWorkerElapsed(s => s + 1);
    }, 1000);

    // 레거시 option_analysis_api.aspx.cs 호환 파라미터 및 option_json 자동 저장을 위한 settings 구성
    const options = {
      qid: currentItem?.id ? String(currentItem.id) : undefined,
      settings: {
        question_fin: qName,
        prompt_string: promptText,
        model_select: selectedModel,
        result_lang: selectedLang,
        temperature: temperature,
        open_item_lv1: openLv1, // 소분류
        open_item_lv2: openLv2, // 중분류
        open_item_lv3: openLv3, // 대분류
        apikeyid: infoData?.apikeyid || undefined
      }
    };
    if (actionType === 'analyze') {
      options.classified = 'Y';        // 1단계: LLM 카테고리/보기 생성 및 pn_lb 저장
      options.opencodeCategory = 'Y';  // 2단계: pn_lb 카테고리 코드화
      options.response = 'N';
      options.opencodeResponse = 'N';
    } else if (actionType === 'translate') {
      options.translateResponse = 'Y';
    } else if (actionType === 'respondent') {
      options.classified = 'N';
      options.opencodeCategory = 'N';
      options.response = 'Y';          // 3단계: 응답자 카테고리 분류
      options.opencodeResponse = 'Y';  // 4단계: 응답자 카테고리 코드화
      options.recallResponse = 'Y';    // 5단계: 빈셀/기타 재분류
    } else if (actionType === 'empty_etc') {
      options.recallResponse = 'Y';
    }

    try {
      const res = await startAnalysisWorker(projectnum, currentQnum, actionType, user, options);

      if (res && (res.ok || res.success === '777')) {
        const jid = res.jobId || res.job_id;
        setCurrentJobId(jid);
        showToast(`${actTitle} 작업이 시작되었습니다.`);

        // 1. 기존 SignalR 연결 정리 후 신규 연결 인스턴스 생성
        if (workerHubConnection.current) {
          try { workerHubConnection.current.stop(); } catch (e) {}
        }
        const hub = createWorkerHubConnection();
        workerHubConnection.current = hub;

        // 2. 소켓 실시간 라인 이벤트 (파이썬 소스 내부 print() 출력 실시간 수신)
        hub.on('onLine', (jobKey, line, isError) => {
          if (jobKey && jid && jobKey !== jid) return;
          const displayLine = isError && !line.startsWith('ERROR:') && !line.startsWith('[오류]') 
            ? `[오류] ${line}` 
            : line;

          setConsoleLines(lines => [...lines, displayLine]);

          // 파이썬 print 문맥 분석하여 UI 상단 단계값(1/4~4/4) 동적 갱신
          if (/시작|starting|init|준비|connect/i.test(line)) {
            setWorkerStep(s => Math.max(s, 1));
          }
          if (/category|카테고리|보기|llm|추출|생성|model/i.test(line)) {
            setWorkerStep(s => Math.max(s, 2));
          }
          if (/classif|분류|매핑|mapping|응답|embed|embedding/i.test(line)) {
            setWorkerStep(s => Math.max(s, 3));
          }
          if (/완료|finished|success|updated|저장|반영|done/i.test(line)) {
            setWorkerStep(s => Math.max(s, 4));
          }

          setTimeout(() => {
            if (wkTermRef.current) {
              wkTermRef.current.scrollTop = wkTermRef.current.scrollHeight;
            }
            if (consoleBodyRef.current) {
              consoleBodyRef.current.scrollTop = consoleBodyRef.current.scrollHeight;
            }
          }, 30);
        });

        // 3. 소켓 완료 이벤트 (정상 종료 또는 에러 종료)
        hub.on('onCompleted', (jobKey, hasError, exitCode) => {
          if (jobKey && jid && jobKey !== jid) return;
          if (workerElapsedTimer.current) {
            clearInterval(workerElapsedTimer.current);
            workerElapsedTimer.current = null;
          }
          if (workerPollTimer.current) {
            clearInterval(workerPollTimer.current);
            workerPollTimer.current = null;
          }
          setWorkerRunning(false);
          setWorkerStep(4);
          setWorkerStatusState(hasError ? 'error' : 'completed');
          showToast(hasError ? '작업 중 오류가 발생했습니다.' : `${actTitle} 완료되었습니다.`);

          // 완료 후 응답/보기/rawdata 데이터 자동 재조회
          getAnalysisResponses(projectnum, currentQnum).then(r => {
            if (r?.resultjson) setResponses(ensureRowUids(r.resultjson));
          }).catch(() => {});
          getAnalysisCodes(projectnum, currentQnum).then(c => {
            if (c?.resultjson) setCodes(c.resultjson);
          }).catch(() => {});
          fetchRawdata();

          try { hub.invoke('Leave', jid); } catch (e) {}
        });

        // 4. SignalR Hub 연결 시작 및 Job 그룹 Join (레거시 worker_api_web.aspx 방식)
        hub.start()
          .then(async () => {
            await hub.invoke('Join', jid);
            setConsoleLines(lines => [...lines, `[SignalR] 실시간 스트리밍 채널에 연결되었습니다. (Job: ${jid})`]);
          })
          .catch(err => {
            console.warn('[SignalR] WebSocket 연결 실패, Fallback HTTP 폴링으로 대체합니다:', err);
          });

        // 5. 보조 Fallback: 소켓 차단망/순단 대비 보조 HTTP 상태 폴링 유지
        let logOffset = 0;
        if (workerPollTimer.current) clearInterval(workerPollTimer.current);

        workerPollTimer.current = setInterval(async () => {
          try {
            const statusRes = await getAnalysisWorkerStatus(jid, logOffset);
            if (statusRes && statusRes.ok) {
              // 소켓이 미동작하여 콘솔 라인이 비어있는 경우 보충
              if (Array.isArray(statusRes.output) && statusRes.output.length > 0) {
                logOffset = statusRes.nextOffset ?? (logOffset + statusRes.output.length);
              }

              if (statusRes.isCompleted) {
                clearInterval(workerPollTimer.current);
                workerPollTimer.current = null;
                if (workerElapsedTimer.current) {
                  clearInterval(workerElapsedTimer.current);
                  workerElapsedTimer.current = null;
                }
                setWorkerRunning(false);
                setWorkerStep(4);
                setWorkerStatusState(statusRes.hasError ? 'error' : 'completed');

                getAnalysisResponses(projectnum, currentQnum).then(r => {
                  if (r?.resultjson) setResponses(ensureRowUids(r.resultjson));
                }).catch(() => {});
                getAnalysisCodes(projectnum, currentQnum).then(c => {
                  if (c?.resultjson) setCodes(c.resultjson);
                }).catch(() => {});
                fetchRawdata();
              }
            }
          } catch (err) {
            console.debug('Worker fallback polling notice', err);
          }
        }, 1500);

      } else {
        setWorkerRunning(false);
        setWorkerStatusState('error');
        if (workerElapsedTimer.current) {
          clearInterval(workerElapsedTimer.current);
          workerElapsedTimer.current = null;
        }
        showToast(res?.message || '작업 시작에 실패했습니다.');
        setConsoleLines(lines => [...lines, `[오류] ${res?.message || '실패'}`]);
      }
    } catch (err) {
      setWorkerRunning(false);
      setWorkerStatusState('error');
      if (workerElapsedTimer.current) {
        clearInterval(workerElapsedTimer.current);
        workerElapsedTimer.current = null;
      }
      showToast(err.message || '작업 호출 중 오류가 발생했습니다.');
      setConsoleLines(lines => [...lines, `[오류] ${err.message}`]);
    }
  };

  const handleStopWorker = async () => {
    if (!currentJobId) return;
    try {
      await stopAnalysisWorker(currentJobId, projectnum, currentQnum, user);
      if (workerPollTimer.current) {
        clearInterval(workerPollTimer.current);
        workerPollTimer.current = null;
      }
      if (workerElapsedTimer.current) {
        clearInterval(workerElapsedTimer.current);
        workerElapsedTimer.current = null;
      }
      if (workerHubConnection.current) {
        try { workerHubConnection.current.stop(); } catch (e) {}
      }
      setWorkerRunning(false);
      setWorkerStatusState('error');
      setConsoleLines(lines => [...lines, `[${new Date().toLocaleTimeString()}] 사용자에 의해 작업이 중지되었습니다.`]);
      showToast('작업을 중지했습니다.');
    } catch (e) {
      showToast('중지 실패');
    }
  };

  // Phase D 핸들러: 엑셀 다운로드
  const handleDownloadExcel = async () => {
    if (isExportingExcel) return;
    setIsExportingExcel(true);
    showToast('엑셀 파일을 생성하고 있습니다...');
    try {
      const res = await downloadAnalysisExcel(projectnum, currentQnum, user);
      showToast(`엑셀 다운로드 완료: ${res.filename}`);
    } catch (err) {
      showToast(err.message || '엑셀 다운로드 실패');
    } finally {
      setIsExportingExcel(false);
    }
  };

  // Phase D 핸들러: 엑셀 업로드 파일 선택창 트리거
  const handleOpenExcelFilePicker = () => {
    if (excelFileInputRef.current) {
      excelFileInputRef.current.value = '';
      excelFileInputRef.current.click();
    }
  };

  // Phase D 핸들러: 엑셀 파일 선택 시 파싱 및 미리보기 오픈
  const handleExcelFileChange = async (e) => {
    const file = e.target.files && e.target.files[0];
    if (!file) return;
    setIsUploadingExcel(true);
    setExcelUploadedFileName(file.name);
    showToast('엑셀 파일을 분석하고 있습니다...');
    try {
      const res = await previewExcelCodes(file, projectnum, currentQnum);
      if (res && res.ok) {
        setExcelPreviewData(res);
        setShowExcelModal(true);
      } else {
        showToast('엑셀 미리보기를 불러오지 못했습니다.');
      }
    } catch (err) {
      showToast(err.message || '엑셀 업로드 파싱 실패');
    } finally {
      setIsUploadingExcel(false);
    }
  };

  // Phase D 핸들러: 미리보기 모달에서 보기에 반영
  const handleApplyExcel = () => {
    if (!excelPreviewData || !excelPreviewData.items) return;
    const validItems = excelPreviewData.items.filter(it => it.status !== '오류' && it.lv123code && it.lv3);
    if (validItems.length === 0) {
      showToast('반영할 수 있는 유효한 보기 항목이 없습니다.');
      return;
    }

    if (excelApplyMode === 'overwrite') {
      const newCodes = validItems.map(it => {
        const isLock = it.is_lock !== undefined ? Boolean(it.is_lock) : (it.ex_gubun === '설문(수정불가)' || it.ex_gubun === 'survey');
        const gubunName = it.gubun_name || (isLock ? '설문 보기' : '자동분석 보기');
        const isEditable = isLock ? '불가' : '가능';
        const exGubun = isLock ? '설문(수정불가)' : '분석(수정가능)';
        return {
          lv1: it.lv1 || '',
          lv1code: it.lv1code || '',
          lv2: it.lv2 || '',
          lv2code: it.lv2code || '',
          lv3: it.lv3 || '',
          lv123code: it.lv123code || '',
          ex_sum: 0,
          ex_gubun: exGubun,
          gubun_name: gubunName,
          is_editable: isEditable,
          is_lock: isLock,
          lock: isLock
        };
      });
      setCodes(newCodes);
      setCodesDirty(true);
      showToast(`기존 보기를 대체하고 ${newCodes.length}개 보기를 반영했습니다.`);
    } else {
      // merge
      const curMap = new Map();
      codes.forEach(c => curMap.set(String(c.lv123code || '').trim(), { ...c }));
      validItems.forEach(it => {
        const cKey = String(it.lv123code || '').trim();
        const existing = curMap.get(cKey);
        const isLock = it.is_lock !== undefined ? Boolean(it.is_lock) : (existing ? getCodeLock(existing) : (it.ex_gubun === '설문(수정불가)' || it.ex_gubun === 'survey'));
        const gubunName = it.gubun_name || (existing ? (existing.gubun_name || '자동분석 보기') : (isLock ? '설문 보기' : '자동분석 보기'));
        const isEditable = isLock ? '불가' : '가능';
        const exGubun = isLock ? '설문(수정불가)' : '분석(수정가능)';

        curMap.set(cKey, {
          lv1: it.lv1 || (existing ? existing.lv1 : ''),
          lv1code: it.lv1code || (existing ? existing.lv1code : ''),
          lv2: it.lv2 || (existing ? existing.lv2 : ''),
          lv2code: it.lv2code || (existing ? existing.lv2code : ''),
          lv3: it.lv3,
          lv123code: it.lv123code,
          ex_sum: existing ? existing.ex_sum : 0,
          ex_gubun: exGubun,
          gubun_name: gubunName,
          is_editable: isEditable,
          is_lock: isLock,
          lock: isLock
        });
      });
      const merged = Array.from(curMap.values());
      setCodes(merged);
      setCodesDirty(true);
      showToast(`보기를 병합 반영했습니다. (총 ${merged.length}개)`);
    }
    setShowExcelModal(false);
  };

  // Phase D 핸들러: 보기 불러오기 모달 오픈
  const handleOpenImportModal = async () => {
    setShowImportModal(true);
    setImportSearch('');
    setSelectedImportItem(null);
    setImportCodes([]);
    setSelectedImportCodes(new Set());
    setIsLoadingImportList(true);
    try {
      const res = await searchImportProjects('');
      if (res && res.ok) {
        setImportProjectList(res.list || []);
      }
    } catch {
      showToast('프로젝트 목록을 불러오지 못했습니다.');
    } finally {
      setIsLoadingImportList(false);
    }
  };

  // Phase D 핸들러: 타 프로젝트 검색
  const handleSearchImportProjects = async (val) => {
    setImportSearch(val);
    setIsLoadingImportList(true);
    try {
      const res = await searchImportProjects(val);
      if (res && res.ok) {
        setImportProjectList(res.list || []);
      }
    } catch {
      showToast('검색 중 오류가 발생했습니다.');
    } finally {
      setIsLoadingImportList(false);
    }
  };

  // Phase D 핸들러: 타 프로젝트/문항 선택 시 보기 로드
  const handleSelectImportItem = async (item) => {
    setSelectedImportItem(item);
    setIsLoadingImportCodes(true);
    try {
      const res = await getImportCodes(item.projectnum, item.qnum);
      if (res && res.ok && Array.isArray(res.codes)) {
        setImportCodes(res.codes);
        const allSet = new Set(res.codes.map(c => String(c.lv123code || '').trim()).filter(Boolean));
        setSelectedImportCodes(allSet);
      } else {
        setImportCodes([]);
        setSelectedImportCodes(new Set());
      }
    } catch {
      showToast('보기 데이터를 불러오지 못했습니다.');
    } finally {
      setIsLoadingImportCodes(false);
    }
  };

  // Phase D 핸들러: 보기 항목 개별 토글
  const handleToggleImportCode = (code) => {
    setSelectedImportCodes(prev => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });
  };

  // Phase D 핸들러: 보기 항목 전체 토글
  const handleToggleAllImportCodes = () => {
    if (selectedImportCodes.size === importCodes.length) {
      setSelectedImportCodes(new Set());
    } else {
      setSelectedImportCodes(new Set(importCodes.map(c => String(c.lv123code || '').trim()).filter(Boolean)));
    }
  };

  // Phase D 핸들러: 선택한 보기 현재 문항에 가져오기
  const handleApplyImport = async () => {
    const selectedList = importCodes.filter(c => selectedImportCodes.has(String(c.lv123code || '').trim()));
    if (selectedList.length === 0) {
      showToast('가져올 보기를 선택해주세요.');
      return;
    }
    setIsApplyingImport(true);
    try {
      const res = await applyImportCodes(projectnum, currentQnum, selectedList, user);
      if (res && res.ok) {
        showToast(`보기 ${res.addedCount || selectedList.length}개를 가져왔습니다.${res.duplicateCount ? ` (중복 제외 ${res.duplicateCount}건)` : ''}`);
        const freshCodes = await getAnalysisCodes(projectnum, currentQnum);
        if (Array.isArray(freshCodes)) {
          setCodes(freshCodes);
          setCodesDirty(false);
        }
        setShowImportModal(false);
      } else {
        showToast(res.message || '보기 가져오기에 실패했습니다.');
      }
    } catch (err) {
      showToast(err.message || '보기 가져오기 오류');
    } finally {
      setIsApplyingImport(false);
    }
  };
  // 스마트 그리드 필터링 및 정렬 목록
  const filteredGridCodes = useMemo(() => {
    let list = [...codes];
    if (gridSearch.trim()) {
      const q = gridSearch.toLowerCase();
      list = list.filter(c =>
        (c.lv3 && c.lv3.toLowerCase().includes(q)) ||
        (c.lv2 && c.lv2.toLowerCase().includes(q)) ||
        (c.lv1 && c.lv1.toLowerCase().includes(q)) ||
        (c.lv123code && String(c.lv123code).toLowerCase().includes(q)) ||
        (c.lv1code && String(c.lv1code).toLowerCase().includes(q)) ||
        (c.lv2code && String(c.lv2code).toLowerCase().includes(q)) ||
        (c.code_num && String(c.code_num).toLowerCase().includes(q)) ||
        (c.src && c.src.toLowerCase().includes(q)) ||
        (c.ex_gubun && c.ex_gubun.toLowerCase().includes(q))
      );
    }
    if (gridSort === 'freq') {
      list.sort((a, b) => Number(b.ex_sum || 0) - Number(a.ex_sum || 0));
    } else if (gridSort === 'l1') {
      list.sort((a, b) => {
        const cmp1 = String(a.lv1code || '').localeCompare(String(b.lv1code || ''), undefined, { numeric: true });
        if (cmp1 !== 0) return cmp1;
        const cmp2 = String(a.lv2code || '').localeCompare(String(b.lv2code || ''), undefined, { numeric: true });
        if (cmp2 !== 0) return cmp2;
        return String(a.lv123code || a.code_num || '').localeCompare(String(b.lv123code || b.code_num || ''), undefined, { numeric: true });
      });
    } else if (gridSort === 'code') {
      list.sort((a, b) => String(a.lv123code || a.code_num || '').localeCompare(String(b.lv123code || b.code_num || ''), undefined, { numeric: true }));
    } else if (gridSort === 'name') {
      list.sort((a, b) => String(a.lv3 || '').localeCompare(String(b.lv3 || '')));
    }
    return list;
  }, [codes, gridSearch, gridSort]);

  // 스마트 그리드 셀 수정 핸들러 (다중 선택 일괄 변경 지원 및 동일 레이블 자동 전파)
  const handleGridCellChangeWithPropagation = useCallback((realIdx, colKey, val) => {
    setCodes(prev => {
      const next = [...prev];
      const target = next[realIdx];
      if (!target) return prev;

      const trimmedVal = String(val || '').trim();
      const targetKey = String(target.lv123code || target.code_num);
      const isMultiSelected = gridSelectedCodes.size > 1 && gridSelectedCodes.has(targetKey);

      // [다중 선택 일괄 변경]: 대분류(lv1, lv1code) 또는 중분류(lv2, lv2code) 수정 시 선택된 전체 행에 동일하게 일괄 반영
      if (isMultiSelected && (colKey === 'lv1' || colKey === 'lv1code' || colKey === 'lv2' || colKey === 'lv2code')) {
        let autoCode = '';
        if (colKey === 'lv1') {
          const matched = next.find(c => c.lv1 && c.lv1.trim().toLowerCase() === trimmedVal.toLowerCase() && c.lv1code);
          autoCode = matched?.lv1code || '';
        } else if (colKey === 'lv2') {
          const matched = next.find(c => c.lv2 && c.lv2.trim().toLowerCase() === trimmedVal.toLowerCase() && c.lv2code);
          autoCode = matched?.lv2code || '';
        }

        const updated = next.map(c => {
          const cKey = String(c.lv123code || c.code_num);
          if (gridSelectedCodes.has(cKey)) {
            const rowCopy = { ...c, [colKey]: val };
            if (colKey === 'lv1' && autoCode) rowCopy.lv1code = autoCode;
            if (colKey === 'lv2' && autoCode) rowCopy.lv2code = autoCode;
            return rowCopy;
          }
          return c;
        });

        // 응답 데이터 동기화
        setResponses(rPrev => rPrev.map(r => {
          if (gridSelectedCodes.has(String(r.lv123code))) {
            const rCopy = { ...r, [colKey]: val, _edited: true };
            if (colKey === 'lv1' && autoCode) rCopy.lv1code = autoCode;
            if (colKey === 'lv2' && autoCode) rCopy.lv2code = autoCode;
            return rCopy;
          }
          return r;
        }));

        showToast(`선택된 ${gridSelectedCodes.size}건의 ${colKey.startsWith('lv1') ? '대분류' : '중분류'}가 일괄 변경되었습니다.`);
        return updated;
      }

      // 1. 단일 대분류 코드(lv1code) 변경 시:
      if (colKey === 'lv1code') {
        const curL1 = target.lv1;
        if (trimmedVal) {
          const conflict = next.find((c, i) => i !== realIdx && c.lv1 && c.lv1 !== curL1 && String(c.lv1code).trim() === trimmedVal);
          if (conflict) {
            showToast(`[대코드 정의 오류] 대코드 '${trimmedVal}'(은)는 이미 다른 대분류 '[${conflict.lv1}]'에 사용 중입니다. 동일 코드를 다른 레이블로 정의할 수 없습니다.`);
            return prev;
          }
        }
        let count = 0;
        const updated = next.map(c => {
          if (c.lv1 && c.lv1 === curL1) {
            count++;
            return { ...c, lv1code: val };
          }
          return c;
        });
        if (count > 1) {
          showToast(`대분류 [${curL1}] 동일 레이블 ${count}개 항목의 대코드가 '${val}'(으)로 일괄 변경되었습니다.`);
        }
        return updated;
      }

      // 2. 단일 대분류명(lv1) 변경 시:
      if (colKey === 'lv1') {
        const oldL1 = target.lv1;
        const matchedExistingL1 = next.find((c, i) => i !== realIdx && c.lv1 && c.lv1.trim().toLowerCase() === trimmedVal.toLowerCase());
        const autoL1Code = matchedExistingL1 && matchedExistingL1.lv1code ? matchedExistingL1.lv1code : target.lv1code;

        const updated = next.map(c => {
          if (c.lv1 === oldL1) {
            return { ...c, lv1: val, lv1code: autoL1Code };
          }
          return c;
        });
        setResponses(rPrev => rPrev.map(r => r.lv1 === oldL1 ? { ...r, lv1: val, lv1code: autoL1Code, _edited: true } : r));
        return updated;
      }

      // 3. 단일 중분류 코드(lv2code) 변경 시:
      if (colKey === 'lv2code') {
        const curL1 = target.lv1;
        const curL2 = target.lv2;
        if (trimmedVal) {
          const conflict = next.find((c, i) => i !== realIdx && c.lv1 === curL1 && c.lv2 && c.lv2 !== curL2 && String(c.lv2code).trim() === trimmedVal);
          if (conflict) {
            showToast(`[중코드 정의 오류] 중코드 '${trimmedVal}'(은)는 이미 다른 중분류 '[${conflict.lv2}]'에 사용 중입니다. 동일 코드를 다른 레이블로 정의할 수 없습니다.`);
            return prev;
          }
        }
        let count = 0;
        const updated = next.map(c => {
          if (c.lv1 === curL1 && c.lv2 === curL2) {
            count++;
            return { ...c, lv2code: val };
          }
          return c;
        });
        if (count > 1) {
          showToast(`중분류 [${curL2}] 동일 레이블 ${count}개 항목의 중코드가 '${val}'(으)로 일괄 변경되었습니다.`);
        }
        return updated;
      }

      // 4. 단일 중분류명(lv2) 변경 시:
      if (colKey === 'lv2') {
        const curL1 = target.lv1;
        const oldL2 = target.lv2;
        const matchedExistingL2 = next.find((c, i) => i !== realIdx && c.lv1 === curL1 && c.lv2 && c.lv2.trim().toLowerCase() === trimmedVal.toLowerCase());
        const autoL2Code = matchedExistingL2 && matchedExistingL2.lv2code ? matchedExistingL2.lv2code : target.lv2code;

        const updated = next.map(c => {
          if (c.lv1 === curL1 && c.lv2 === oldL2) {
            return { ...c, lv2: val, lv2code: autoL2Code };
          }
          return c;
        });
        setResponses(rPrev => rPrev.map(r => (r.lv1 === curL1 && r.lv2 === oldL2) ? { ...r, lv2: val, lv2code: autoL2Code, _edited: true } : r));
        return updated;
      }

      // 5. 소분류 코드(code_num, lv123code) 변경 시:
      if (colKey === 'code_num' || colKey === 'lv123code') {
        if (trimmedVal) {
          const conflict = next.find((c, i) => i !== realIdx && String(c.lv123code || c.code_num || '').trim() === trimmedVal && c.lv3 && c.lv3 !== target.lv3);
          if (conflict) {
            showToast(`[소코드 정의 오류] 소코드 '${trimmedVal}'(은)는 이미 다른 레이블 '[${conflict.lv3}]'에 사용 중입니다. 서로 다른 레이블에 동일 코드를 부여할 수 없습니다.`);
            return prev;
          }
        }
        const rowUpdated = { ...target, [colKey]: val, lv123code: val, lv3code: val, code_num: val };
        next[realIdx] = rowUpdated;
        return next;
      }

      // 6. 소분류명(lv3) 변경 시:
      if (colKey === 'lv3') {
        const curCode = String(target.lv123code || target.code_num || '').trim();
        if (curCode && trimmedVal) {
          const conflict = next.find((c, i) => i !== realIdx && String(c.lv123code || c.code_num || '').trim() === curCode && c.lv3 && c.lv3 !== trimmedVal);
          if (conflict) {
            showToast(`[소분류 정의 오류] 코드 '${curCode}'(은)는 이미 '[${conflict.lv3}]'(으)로 정의되어 있습니다. 서로 다른 레이블에 동일 코드를 지정할 수 없습니다.`);
            return prev;
          }
        }
        const rowUpdated = { ...target, lv3: val };
        next[realIdx] = rowUpdated;
        return next;
      }

      // 기타 필드
      const rowUpdated = { ...target, [colKey]: val };
      next[realIdx] = rowUpdated;
      return next;
    });
    setCodesDirty(true);
  }, [gridSelectedCodes]);

  // 스마트 그리드 수정가능(ON)/수정불가(OFF) 토글 (다중 선택 일괄 토글 지원 및 설문보기 전환 지원)
  const handleToggleGridLock = useCallback((realIdx) => {
    setCodes(prev => {
      const next = [...prev];
      const target = next[realIdx];
      if (!target) return prev;
      const targetKey = String(target.lv123code || target.code_num);
      const isMultiSelected = gridSelectedCodes.size > 1 && gridSelectedCodes.has(targetKey);
      const curLock = getCodeLock(target);
      const newLock = !curLock; // true(불가) <-> false(가능)
      const newEditable = newLock ? '불가' : '가능';
      const newExGubun = newLock ? '설문(수정불가)' : '분석(수정가능)';

      if (isMultiSelected) {
        const updated = next.map(c => {
          const cKey = String(c.lv123code || c.code_num);
          if (gridSelectedCodes.has(cKey)) {
            const gubunName = c.gubun_name || (getCodeSrc(c) === '설문지' ? '설문 보기' : getCodeSrc(c) === '트래킹' ? '트래킹 보기' : '자동분석 보기');
            return {
              ...c,
              gubun_name: gubunName,
              is_editable: newEditable,
              is_lock: newLock,
              lock: newLock,
              ex_gubun: newExGubun
            };
          }
          return c;
        });
        showToast(`선택된 ${gridSelectedCodes.size}건이 일괄 [${newLock ? '수정불가' : '수정가능'}](으)로 변경되었습니다. (ex_gubun: ${newExGubun})`);
        return updated;
      }

      const gubunName = target.gubun_name || (getCodeSrc(target) === '설문지' ? '설문 보기' : getCodeSrc(target) === '트래킹' ? '트래킹 보기' : '자동분석 보기');
      next[realIdx] = {
        ...target,
        gubun_name: gubunName,
        is_editable: newEditable,
        is_lock: newLock,
        lock: newLock,
        ex_gubun: newExGubun
      };

      if (gubunName.includes('설문')) {
        if (!newLock) {
          showToast('설문 보기가 [수정가능]으로 변경되었습니다. (AI 자동분석 시 재분류 대상에 포함될 수 있습니다)');
        } else {
          showToast('설문 보기가 [수정불가]로 잠겼습니다. (AI 자동분석 시 고정 유지됩니다)');
        }
      } else {
        showToast(`수정여부가 [${newLock ? '수정불가' : '수정가능'}](으)로 변경되었습니다.`);
      }
      return next;
    });
    setCodesDirty(true);
  }, [gridSelectedCodes]);

  // 스마트 그리드 보기구분 드롭다운 변경 (다중 선택 일괄 변경 지원)
  const handleChangeGridSrc = useCallback((realIdx, newSrc) => {
    setCodes(prev => {
      const next = [...prev];
      const target = next[realIdx];
      if (!target) return prev;
      const targetKey = String(target.lv123code || target.code_num);
      const isMultiSelected = gridSelectedCodes.size > 1 && gridSelectedCodes.has(targetKey);
      const gubunNameMap = {
        '설문지': '설문 보기',
        '자동분석': '자동분석 보기',
        '트래킹': '트래킹 보기'
      };
      const gubunNameVal = gubunNameMap[newSrc] || '자동분석 보기';
      // 설문 보기와 트래킹 보기는 기본적으로 수정불가(lock), 자동분석은 수정가능(unlock)
      const isLockVal = (newSrc === '설문지' || newSrc === '트래킹');
      const isEditableVal = isLockVal ? '불가' : '가능';
      const exGubunVal = isLockVal ? '설문(수정불가)' : '분석(수정가능)';
      const srcName = gubunNameVal;

      if (isMultiSelected) {
        const updated = next.map(c => {
          const cKey = String(c.lv123code || c.code_num);
          if (gridSelectedCodes.has(cKey)) {
            return {
              ...c,
              src: newSrc,
              gubun_name: gubunNameVal,
              is_editable: isEditableVal,
              is_lock: isLockVal,
              lock: isLockVal,
              ex_gubun: exGubunVal
            };
          }
          return c;
        });
        showToast(`선택된 ${gridSelectedCodes.size}건의 보기구분이 [${srcName}](으)로 일괄 변경되었습니다.`);
        return updated;
      }

      next[realIdx] = {
        ...target,
        src: newSrc,
        gubun_name: gubunNameVal,
        is_editable: isEditableVal,
        is_lock: isLockVal,
        lock: isLockVal,
        ex_gubun: exGubunVal
      };
      showToast(`보기구분이 [${srcName}](으)로 변경되었습니다. (${isEditableVal})`);
      return next;
    });
    setCodesDirty(true);
  }, [gridSelectedCodes]);

  // 스마트 그리드 선택 항목 일괄 보기구분 변경
  const handleGridBulkSrc = useCallback((newSrc) => {
    if (gridSelectedCodes.size === 0) return;
    const gubunNameMap = {
      '설문지': '설문 보기',
      '자동분석': '자동분석 보기',
      '트래킹': '트래킹 보기'
    };
    const gubunNameVal = gubunNameMap[newSrc] || '자동분석 보기';
    const isLockVal = (newSrc === '설문지' || newSrc === '트래킹');
    const isEditableVal = isLockVal ? '불가' : '가능';
    const exGubunVal = isLockVal ? '설문(수정불가)' : '분석(수정가능)';

    setCodes(prev => prev.map(c => {
      const codeKey = String(c.lv123code || c.code_num);
      if (gridSelectedCodes.has(codeKey)) {
        return {
          ...c,
          src: newSrc,
          gubun_name: gubunNameVal,
          is_editable: isEditableVal,
          is_lock: isLockVal,
          lock: isLockVal,
          ex_gubun: exGubunVal
        };
      }
      return c;
    }));
    setCodesDirty(true);
    showToast(`선택한 ${gridSelectedCodes.size}개 항목의 보기구분을 [${gubunNameVal}]로 일괄 변경했습니다.`);
  }, [gridSelectedCodes]);

  // 스마트 그리드 선택 항목 일괄 수정여부(가능/불가) 변경
  const handleGridBulkLock = useCallback((newLock) => {
    if (gridSelectedCodes.size === 0) return;
    const newEditable = newLock ? '불가' : '가능';
    const newExGubun = newLock ? '설문(수정불가)' : '분석(수정가능)';

    setCodes(prev => prev.map(c => {
      const codeKey = String(c.lv123code || c.code_num);
      if (gridSelectedCodes.has(codeKey)) {
        const gubunName = c.gubun_name || (getCodeSrc(c) === '설문지' ? '설문 보기' : getCodeSrc(c) === '트래킹' ? '트래킹 보기' : '자동분석 보기');
        return {
          ...c,
          gubun_name: gubunName,
          is_editable: newEditable,
          is_lock: newLock,
          lock: newLock,
          ex_gubun: newExGubun
        };
      }
      return c;
    }));
    setCodesDirty(true);
    showToast(`선택한 ${gridSelectedCodes.size}개 항목을 [${newLock ? '수정불가' : '수정가능'}](으)로 일괄 변경했습니다.`);
  }, [gridSelectedCodes]);

  // -------------------------------------------------------------
  // 스마트 그리드: Shift / Ctrl / 마우스 드래그 다중 선택 핸들러
  // -------------------------------------------------------------

  // 마우스 다운 이벤트 (Shift, Ctrl, 또는 드래그 시작)
  const handleGridCheckboxMouseDown = (idx, codeKey, e) => {
    e.preventDefault();

    // 1. Shift 키: 앵커부터 현재 행까지 연속 범위 선택
    if (e.shiftKey && gridAnchorIdxRef.current !== null) {
      const start = Math.min(gridAnchorIdxRef.current, idx);
      const end = Math.max(gridAnchorIdxRef.current, idx);
      const rangeCodes = [];
      for (let i = start; i <= end; i++) {
        if (filteredGridCodes[i]) {
          rangeCodes.push(String(filteredGridCodes[i].lv123code || filteredGridCodes[i].code_num));
        }
      }
      setGridSelectedCodes(prev => {
        const next = new Set(prev);
        rangeCodes.forEach(k => next.add(k));
        return next;
      });
      showToast(`${start + 1}번 ~ ${end + 1}번 보기 (${rangeCodes.length}건) 범위 선택`);
      return;
    }

    // 2. Ctrl / Meta 키: 기존 선택 유지 + 개별 토글
    if (e.ctrlKey || e.metaKey) {
      setGridSelectedCodes(prev => {
        const next = new Set(prev);
        if (next.has(codeKey)) next.delete(codeKey);
        else next.add(codeKey);
        return next;
      });
      gridAnchorIdxRef.current = idx;
      return;
    }

    // 3. 일반 클릭: 드래그 모드 판정 및 시작
    const isAlreadyChecked = gridSelectedCodes.has(codeKey);
    const mode = isAlreadyChecked ? 'uncheck' : 'check';
    gridDragModeRef.current = mode;
    isGridDraggingRef.current = true;
    gridAnchorIdxRef.current = idx;

    setGridSelectedCodes(prev => {
      const next = new Set(prev);
      if (mode === 'check') next.add(codeKey);
      else next.delete(codeKey);
      return next;
    });
  };

  // 마우스 엔터 이벤트 (드래그 중 지나가는 행 연속 선택/해제)
  const handleGridCheckboxMouseEnter = (idx, codeKey) => {
    if (!isGridDraggingRef.current) return;
    const mode = gridDragModeRef.current;
    setGridSelectedCodes(prev => {
      const next = new Set(prev);
      if (mode === 'check') next.add(codeKey);
      else next.delete(codeKey);
      return next;
    });
  };

  // 드래그 중 테이블 가장자리 근처일 때 부드러운 자동 스크롤
  const handleGridMouseMove = (e) => {
    if (!isGridDraggingRef.current || !gridTableWrapRef.current) return;
    const rect = gridTableWrapRef.current.getBoundingClientRect();
    const threshold = 35;
    if (e.clientY < rect.top + threshold) {
      gridTableWrapRef.current.scrollTop -= 14;
    } else if (e.clientY > rect.bottom - threshold) {
      gridTableWrapRef.current.scrollTop += 14;
    }
  };

  // 글로벌 마우스 업 리스너
  useEffect(() => {
    const handleGlobalMouseUp = () => {
      if (isGridDraggingRef.current) {
        isGridDraggingRef.current = false;
      }
    };
    window.addEventListener('mouseup', handleGlobalMouseUp);
    return () => {
      window.removeEventListener('mouseup', handleGlobalMouseUp);
    };
  }, []);

  // -------------------------------------------------------------
  // 빠른 조건 선택 도우미 (Quick Selection Assistant)
  // -------------------------------------------------------------
  const handleQuickSelectAll = () => {
    setGridSelectedCodes(new Set(filteredGridCodes.map(c => String(c.lv123code || c.code_num))));
    setShowQuickSelectPop(false);
    showToast(`전체 ${filteredGridCodes.length}개 보기를 선택했습니다.`);
  };

  const handleQuickClearAll = () => {
    setGridSelectedCodes(new Set());
    setShowQuickSelectPop(false);
    showToast('선택을 모두 해제했습니다.');
  };

  const handleQuickInvert = () => {
    setGridSelectedCodes(prev => {
      const next = new Set();
      filteredGridCodes.forEach(c => {
        const k = String(c.lv123code || c.code_num);
        if (!prev.has(k)) next.add(k);
      });
      return next;
    });
    setShowQuickSelectPop(false);
    showToast('선택을 반전했습니다.');
  };

  const handleQuickSelectBySrc = (srcType) => {
    const matched = filteredGridCodes.filter(c => getCodeSrc(c) === srcType);
    setGridSelectedCodes(new Set(matched.map(c => String(c.lv123code || c.code_num))));
    setShowQuickSelectPop(false);
    const label = srcType === '설문지' ? '설문 보기' : srcType === '자동분석' ? '자동분석 보기' : '트래킹 보기';
    showToast(`[${label}] ${matched.length}건을 선택했습니다.`);
  };

  const handleQuickSelectByLock = (isLock) => {
    const matched = filteredGridCodes.filter(c => getCodeLock(c) === isLock);
    setGridSelectedCodes(new Set(matched.map(c => String(c.lv123code || c.code_num))));
    setShowQuickSelectPop(false);
    showToast(`[${isLock ? '수정불가' : '수정가능'}] 보기 ${matched.length}건을 선택했습니다.`);
  };

  const handleQuickSelectByRange = () => {
    const s = parseInt(rangeStartVal, 10);
    const e = parseInt(rangeEndVal, 10);
    if (isNaN(s) || isNaN(e) || s < 1 || e < s) {
      showToast('올바른 범위를 입력해주세요 (예: 1 ~ 30).');
      return;
    }
    const startIdx = s - 1;
    const endIdx = Math.min(e - 1, filteredGridCodes.length - 1);
    const rangeCodes = [];
    for (let i = startIdx; i <= endIdx; i++) {
      if (filteredGridCodes[i]) {
        rangeCodes.push(String(filteredGridCodes[i].lv123code || filteredGridCodes[i].code_num));
      }
    }
    setGridSelectedCodes(prev => {
      const next = new Set(prev);
      rangeCodes.forEach(k => next.add(k));
      return next;
    });
    setShowQuickSelectPop(false);
    showToast(`${s}번 ~ ${endIdx + 1}번 보기 (${rangeCodes.length}건)를 선택했습니다.`);
  };

  // 스마트 그리드 새 행 추가
  const handleGridAddRow = useCallback(() => {
    setCodes(prev => {
      const maxNum = prev.reduce((max, c) => {
        const n = parseInt(c.lv123code || c.code_num, 10);
        return !isNaN(n) && n > max ? n : max;
      }, 0);
      const nextCode = String(maxNum + 1);
      const newRow = {
        code_num: nextCode,
        lv123code: nextCode,
        lv3code: nextCode,
        lv1: uniqueL1List[0] || '대분류 1',
        lv1code: '1',
        lv2: uniqueL2List[0] || '중분류 1',
        lv2code: '1',
        lv3: '새 보기',
        ex_sum: 0,
        ex_gubun: '분석(수정가능)',
        gubun_name: '자동분석 보기',
        is_editable: '가능',
        is_lock: false,
        lock: false,
        src: '자동분석'
      };
      return [...prev, newRow];
    });
    setCodesDirty(true);
    showToast('새 보기 행이 추가되었습니다. (수정가능 상태)');
  }, [uniqueL1List, uniqueL2List]);

  // 스마트 그리드 특정 행 삭제 (다중 선택 시 일괄 삭제 지원 및 수정불가 항목 보호)
  const handleGridDeleteRow = useCallback((realIdx) => {
    const target = codes[realIdx];
    if (!target) return;
    const targetKey = String(target.lv123code || target.code_num);
    const isMultiSelected = gridSelectedCodes.size > 1 && gridSelectedCodes.has(targetKey);

    if (isMultiSelected) {
      // 수정불가(isLocked) 항목 제외 처리
      const lockedKeys = new Set(codes.filter(c => getCodeLock(c)).map(c => String(c.lv123code || c.code_num)));
      const actualDeleteKeys = new Set([...gridSelectedCodes].filter(k => !lockedKeys.has(k)));
      if (actualDeleteKeys.size === 0) {
        showToast('선택된 항목이 모두 수정불가(잠금) 상태이므로 삭제할 수 없습니다. 수정을 원하시면 먼저 수정가능으로 변경하세요.');
        return;
      }

      const lockedCount = gridSelectedCodes.size - actualDeleteKeys.size;
      const promptMsg = lockedCount > 0
        ? `수정불가 항목 ${lockedCount}건을 제외하고, 선택된 ${actualDeleteKeys.size}건의 보기를 삭제하시겠습니까?\n매핑된 응답 데이터도 미분류 처리됩니다.`
        : `선택된 ${actualDeleteKeys.size}건의 보기를 모두 삭제하시겠습니까?\n매핑된 응답 데이터도 미분류 처리됩니다.`;

      if (!window.confirm(promptMsg)) return;

      setCodes(prev => prev.filter(c => !actualDeleteKeys.has(String(c.lv123code || c.code_num))));
      setResponses(prev => prev.map(r => actualDeleteKeys.has(String(r.lv123code)) ? { ...r, lv1: '', lv1code: '', lv2: '', lv2code: '', lv3: '', lv3code: '', lv123code: '', _edited: true } : r));
      setGridSelectedCodes(new Set());
      setCodesDirty(true);
      showToast(`${actualDeleteKeys.size}건의 보기가 삭제되었습니다.${lockedCount > 0 ? ` (수정불가 ${lockedCount}건 보호)` : ''}`);
    } else {
      if (getCodeLock(target)) {
        showToast('수정불가 상태인 보기는 삭제할 수 없습니다. 수정을 원하시면 먼저 수정가능으로 변경하세요.');
        return;
      }
      const name = target.lv3 || targetKey || '해당';
      if (!window.confirm(`[${name}] 보기를 삭제하시겠습니까?\n매핑된 응답 데이터도 미분류 처리됩니다.`)) return;
      setCodes(prev => prev.filter((_, i) => i !== realIdx));
      setResponses(prev => prev.map(r => String(r.lv123code) === targetKey ? { ...r, lv1: '', lv1code: '', lv2: '', lv2code: '', lv3: '', lv3code: '', lv123code: '', _edited: true } : r));
      setCodesDirty(true);
      showToast(`[${name}] 보기가 삭제되었습니다.`);
    }
  }, [codes, gridSelectedCodes]);

  // 스마트 그리드 선택 일괄 삭제 (수정불가 항목 보호)
  const handleGridBulkDelete = useCallback(() => {
    if (gridSelectedCodes.size === 0) return;
    const lockedKeys = new Set(codes.filter(c => getCodeLock(c)).map(c => String(c.lv123code || c.code_num)));
    const actualDeleteKeys = new Set([...gridSelectedCodes].filter(k => !lockedKeys.has(k)));
    if (actualDeleteKeys.size === 0) {
      showToast('선택한 항목이 모두 수정불가(잠금) 상태이므로 삭제할 수 없습니다.');
      return;
    }

    const lockedCount = gridSelectedCodes.size - actualDeleteKeys.size;
    const promptMsg = lockedCount > 0
      ? `수정불가 항목 ${lockedCount}개를 제외하고, ${actualDeleteKeys.size}개 보기를 일괄 삭제하시겠습니까?`
      : `선택한 ${actualDeleteKeys.size}개 보기를 일괄 삭제하시겠습니까?`;

    if (!window.confirm(promptMsg)) return;
    setCodes(prev => prev.filter(c => !actualDeleteKeys.has(String(c.lv123code || c.code_num))));
    setGridSelectedCodes(new Set());
    setCodesDirty(true);
    showToast(`${actualDeleteKeys.size}개 보기가 일괄 삭제되었습니다.${lockedCount > 0 ? ` (수정불가 ${lockedCount}개 보호)` : ''}`);
  }, [codes, gridSelectedCodes]);

  // 스마트 그리드 선택 항목 대/중분류 일괄 변경
  const handleGridBulkAssignParent = useCallback(() => {
    if (gridSelectedCodes.size === 0 || !gridBulkParentVal.trim()) {
      showToast('변경할 분류명을 입력하거나 선택해주세요.');
      return;
    }
    const colName = gridBulkParentCol === 'lv1' ? '대분류' : '중분류';
    setCodes(prev => prev.map(c => {
      const codeKey = String(c.lv123code || c.code_num);
      if (gridSelectedCodes.has(codeKey)) {
        return { ...c, [gridBulkParentCol]: gridBulkParentVal.trim() };
      }
      return c;
    }));
    setCodesDirty(true);
    showToast(`선택한 ${gridSelectedCodes.size}개 항목의 ${colName}를 '${gridBulkParentVal.trim()}'(으)로 일괄 변경했습니다.`);
    setGridSelectedCodes(new Set());
    setGridBulkParentVal('');
  }, [gridSelectedCodes, gridBulkParentCol, gridBulkParentVal]);

  // 스마트 그리드 키보드 엔터 내비게이션 (다음 행 동일 필드로 포커스)
  const handleGridKeyDown = (e, rowIdx, colKey) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      const nextInput = document.querySelector(`input[data-grid-row="${rowIdx + 1}"][data-grid-col="${colKey}"]`);
      if (nextInput) {
        nextInput.focus();
        nextInput.select();
      } else {
        e.target.blur();
      }
    }
  };

  useEffect(() => {
    return () => {
      if (workerPollTimer.current) clearInterval(workerPollTimer.current);
      if (workerElapsedTimer.current) clearInterval(workerElapsedTimer.current);
      if (workerHubConnection.current) {
        try { workerHubConnection.current.stop(); } catch (e) {}
      }
    };
  }, []);

  // 중복코드 제거 프리뷰 통계 (백엔드 AnalysisService와 동일한 규칙: 모드 A 전체제거 vs 모드 B 기본결측정제)
  const duplicateStats = useMemo(() => {
    const emptyStats = {
      dupCount: 0,
      dkRemovedCount: 0,
      specialDupCount: 0,
      totalRemoved: 0,
      finalCount: responses?.length || 0,
      modeA: { generalDupCount: 0, specialDupCount: 0, dkRemovedCount: 0, totalRemoved: 0, finalCount: responses?.length || 0 },
      modeB: { specialDupCount: 0, dkRemovedCount: 0, totalRemoved: 0, finalCount: responses?.length || 0 }
    };

    if (!responses || responses.length === 0) {
      return emptyStats;
    }

    // DK(모름/무응답/없음/없다) 코드 및 레이블 판별
    const dkCodeObj = codes.find(c => {
      const name = c.lv3 || c.code_name || '';
      return name.includes('모름') || name.includes('무응답') || name.includes('없음') || name.includes('없다');
    });
    const dkCode = dkCodeObj ? (dkCodeObj.lv123code || dkCodeObj.lv3code || dkCodeObj.code_num) : null;

    const isDkOrNone = (code, label) => {
      if (code === '9999' || code === '9998') return true;
      if (dkCode && code === dkCode) return true;
      if (label && (label.includes('모름') || label.includes('무응답') || label.includes('없음') || label.includes('없다'))) return true;
      return false;
    };

    const isEtc = (code, label) => {
      if (code === '9997') return true;
      if (label && label.includes('기타')) return true;
      return false;
    };

    const pidGroups = new Map();
    responses.forEach(r => {
      const pid = r.pid || r.fixed_key || 'unknown';
      if (!pidGroups.has(pid)) pidGroups.set(pid, []);
      pidGroups.get(pid).push(r);
    });

    // 모드 A (전체 중복제거): 일반코드 중복제거 + 특수코드 정제
    let generalDupA = 0;
    let specialDupA = 0;
    let dkRemovedA = 0;
    let finalCountA = 0;

    // 모드 B (기본 결측정제): 일반코드 중복은 보존 + 특수코드 정제
    let specialDupB = 0;
    let dkRemovedB = 0;
    let finalCountB = 0;

    pidGroups.forEach(items => {
      const hasGeneralCode = items.some(x => {
        const c = x.lv123code || x.lv3code || x.code || '';
        const l = x.lv3 || x.label || '';
        return Boolean(c) && !isDkOrNone(c, l) && !isEtc(c, l);
      });

      // ── [모드 A 시뮬레이션] ──
      const seenGeneralA = new Set();
      const seenSpecialA = new Set();
      items.forEach(item => {
        const c = item.lv123code || item.lv3code || item.code || '';
        const l = item.lv3 || item.label || '';
        if (!c) { finalCountA++; return; }

        const dkNone = isDkOrNone(c, l);
        const etc = isEtc(c, l);

        if (hasGeneralCode && dkNone) {
          dkRemovedA++;
          return;
        }

        if (dkNone || etc) {
          if (seenSpecialA.has(c)) {
            specialDupA++;
            return;
          }
          seenSpecialA.add(c);
          finalCountA++;
          return;
        }

        if (seenGeneralA.has(c)) {
          generalDupA++;
          return;
        }
        seenGeneralA.add(c);
        finalCountA++;
      });

      // ── [모드 B 시뮬레이션] ──
      const seenSpecialB = new Set();
      items.forEach(item => {
        const c = item.lv123code || item.lv3code || item.code || '';
        const l = item.lv3 || item.label || '';
        if (!c) { finalCountB++; return; }

        const dkNone = isDkOrNone(c, l);
        const etc = isEtc(c, l);

        if (hasGeneralCode && dkNone) {
          dkRemovedB++;
          return;
        }

        if (dkNone || etc) {
          if (seenSpecialB.has(c)) {
            specialDupB++;
            return;
          }
          seenSpecialB.add(c);
          finalCountB++;
          return;
        }

        // 일반코드는 중복이어도 그대로 유지
        finalCountB++;
      });
    });

    const modeAStats = {
      generalDupCount: generalDupA,
      specialDupCount: specialDupA,
      dkRemovedCount: dkRemovedA,
      totalRemoved: generalDupA + specialDupA + dkRemovedA,
      finalCount: finalCountA
    };

    const modeBStats = {
      specialDupCount: specialDupB,
      dkRemovedCount: dkRemovedB,
      totalRemoved: specialDupB + dkRemovedB,
      finalCount: finalCountB
    };

    return {
      dupCount: duplicateRemove ? generalDupA : 0,
      dkRemovedCount: duplicateRemove ? dkRemovedA : dkRemovedB,
      specialDupCount: duplicateRemove ? specialDupA : specialDupB,
      totalRemoved: duplicateRemove ? modeAStats.totalRemoved : modeBStats.totalRemoved,
      finalCount: duplicateRemove ? finalCountA : finalCountB,
      modeA: modeAStats,
      modeB: modeBStats
    };
  }, [responses, codes, duplicateRemove]);

  // 저장 모달 열기 및 DB 비교
  const handleOpenSaveModal = async () => {
    // 1. 코드-레이블 일치성 종합 유효성 검사 (동일 코드 다른 레이블 정의 방지)
    const valResult = validateCodeLabelConsistency(codes);
    if (!valResult.valid) {
      showToast(valResult.message);
      return;
    }

    try {
      setSaveLoading(true);
      setShowSaveModal(true);
      const res = await compareAnalysisResponses(projectnum, currentQnum, responses, user);
      setSaveLoading(false);
      if (res && (res.success === '777' || res.success === '200') && res.diff) {
        const d = res.diff;
        const unc = Number(d.unc ?? d.uncoded ?? 0);
        const coded = Number(d.coded ?? 0);
        const total = Number(d.total ?? (coded + unc));
        const dbCoded = Number(d.dbCoded ?? 0);
        const neu = Number(d.neu ?? 0);
        const chg = Number(d.chg ?? 0);
        const same = Number(d.same ?? 0);
        const dbNew = Number(d.dbNew ?? 0);
        setSaveDiff({
          total,
          coded,
          unc,
          dbCoded,
          neu,
          chg,
          same,
          dbNew,
          pending: unc + neu + chg,
          lastSavedDate: d.lastSavedDate || d.lastSaveDate || headerMeta.duplicateRemoveDate || ''
        });
      } else {
        const coded = responses.filter(r => r.lv123code || r.lv3);
        const unc = responses.filter(r => !r.lv123code && !r.lv3);
        const ed = responses.filter(r => r._edited);
        setSaveDiff({
          total: responses.length,
          coded: coded.length,
          unc: unc.length,
          dbCoded: coded.length - ed.length,
          neu: ed.length,
          chg: 0,
          same: coded.length - ed.length,
          dbNew: 0,
          pending: ed.length + unc.length,
          lastSavedDate: headerMeta.duplicateRemoveDate || ''
        });
      }
    } catch (err) {
      setSaveLoading(false);
      showToast('비교 데이터 로드 실패');
    }
  };

  // 저장 실행 핸들러
  const handleExecuteSave = async () => {
    try {
      setSaveLoading(true);

      // 1. info 저장
      const infoPayload = {
        question_fin: qName,
        prompt_string: promptText,
        model_select: selectedModel,
        result_lang: selectedLang,
        temperature: String(temperature),
        open_item_lv1: openLv1, // 소분류
        open_item_lv2: openLv2, // 중분류
        open_item_lv3: openLv3, // 대분류
        apikeyid: infoData?.apikeyid || undefined
      };
      await saveAnalysisInfo(projectnum, currentQnum, infoPayload, user);

      // 2. codes 저장
      const codeRes = await saveAnalysisCodes(projectnum, currentQnum, codes, user, levelMode);
      if (codeRes && codeRes.success !== '200' && codeRes.success !== '777' && codeRes.success !== true) {
        setSaveLoading(false);
        showToast(codeRes.message || '보기 저장 실패: 코드가 중복되었거나 빈 값이 있습니다.');
        return;
      }

      // 3. responses 저장
      let payloadResponses = responses;
      if (saveWithUncoded && saveDiff && saveDiff.unc > 0) {
        payloadResponses = responses.map(r => {
          if (!r.lv123code && !r.lv3) {
            return {
              ...r,
              lv123code: saveUncodedFallbackCode,
              lv3: '기타/무응답(자동)',
              _edited: true
            };
          }
          return r;
        });
        setResponses(payloadResponses);
      }

      const respRes = await saveAnalysisResponses(
        projectnum,
        currentQnum,
        duplicateRemove ? 'y' : 'n',
        payloadResponses,
        user,
        levelMode
      );

      setSaveLoading(false);
      setShowSaveModal(false);

      if (respRes && (respRes.success === '200' || respRes.success === '777' || respRes.success === true)) {
        const toastMsg = respRes.message ? respRes.message : `저장 완료 — ${respRes.count || respRes.saved_count || 0}건이 반영되었습니다.`;
        showToast(toastMsg);
        setResponses(prev => prev.map(r => ({ ...r, _edited: false })));
        setInitialLevelMode(levelMode);
        setCodesDirty(false);
        setInitialDuplicateRemove(duplicateRemove);
        const nowStr = new Date().toLocaleString();
        setHeaderMeta(prev => ({
          ...prev,
          duplicateRemoveDate: duplicateRemove ? nowStr : ''
        }));
        // 저장 완료 즉시 rawdata 및 보기 집계 재조회
        fetchRawdata();
        getAnalysisCodes(projectnum, currentQnum).then(c => {
          if (c?.resultjson) setCodes(c.resultjson);
        }).catch(() => {});
      } else {
        showToast(respRes?.message || '저장 중 오류가 발생했습니다.');
      }
    } catch (err) {
      setSaveLoading(false);
      showToast(err.message || '저장 중 오류가 발생했습니다.');
    }
  };

  const pendingCount = useMemo(() => {
    return responses.filter(r => r._edited || (!r.lv123code && !r.lv3)).length;
  }, [responses]);

  // Phase B: 코드 보기유형 및 잠금 헬퍼
  const getCodeSrc = (c) => {
    if (c.gubun_name) {
      if (c.gubun_name.includes('설문')) return '설문지';
      if (c.gubun_name.includes('트래킹')) return '트래킹';
      return '자동분석';
    }
    if (c.src) return c.src;
    const g = c.ex_gubun || '';
    if (g === '설문(수정불가)' || g === 'survey' || g === '설문지' || g === '수동') return '설문지';
    if (g === '트래킹' || g === '트래킹보기') return '트래킹';
    return '자동분석';
  };

  const getCodeLock = (c) => {
    // 1. is_editable 최우선 반영 (사용자가 토글을 변경한 경우)
    if (c.is_editable === '불가') return true;
    if (c.is_editable === '가능') return false;

    // 2. is_lock / lock 프로퍼티 반영
    if (c.is_lock !== undefined) return Boolean(c.is_lock);
    if (c.lock !== undefined) return Boolean(c.lock);

    // 3. 레거시 폴백: ex_gubun이 '설문(수정불가)'인 경우 잠금
    if (c.ex_gubun === '설문(수정불가)' || c.ex_gubun === 'survey') return true;

    // 4. 출처 기본 정책 폴백
    const s = getCodeSrc(c);
    if (s === '설문지' || s === '트래킹') return true;
    return typePolicies[s] !== undefined ? typePolicies[s] : false;
  };

  // Phase B: 보기유형별 개수 통계
  const srcCounts = useMemo(() => {
    const counts = { '설문지': 0, '자동분석': 0, '트래킹': 0 };
    codes.forEach(c => {
      const s = getCodeSrc(c);
      if (counts[s] !== undefined) counts[s]++;
      else counts['자동분석']++;
    });
    return counts;
  }, [codes]);

  // Phase B: 유지 및 재생성 통계
  const keepCount = useMemo(() => {
    return codes.filter(c => getCodeLock(c)).length;
  }, [codes, typePolicies]);

  const regenCount = useMemo(() => {
    return codes.length - keepCount;
  }, [codes, keepCount]);

  // 보기 3단 트리 구조 구축
  const categoryTree = useMemo(() => {
    const l1Map = new Map();
    codes.forEach(c => {
      const l1Name = c.lv1 || '(대분류 없음)';
      const l2Name = c.lv2 || '(중분류 없음)';
      if (!l1Map.has(l1Name)) {
        l1Map.set(l1Name, { name: l1Name, code: c.lv1code || '', l2Map: new Map(), count: 0 });
      }
      const l1Obj = l1Map.get(l1Name);
      if (!l1Obj.code && c.lv1code) l1Obj.code = c.lv1code;
      l1Obj.count += Number(c.ex_sum || 0);

      if (!l1Obj.l2Map.has(l2Name)) {
        l1Obj.l2Map.set(l2Name, { name: l2Name, code: c.lv2code || '', items: [], count: 0 });
      }
      const l2Obj = l1Obj.l2Map.get(l2Name);
      if (!l2Obj.code && c.lv2code) l2Obj.code = c.lv2code;
      l2Obj.count += Number(c.ex_sum || 0);
      l2Obj.items.push(c);
    });

    const result = [];
    l1Map.forEach((l1Obj, l1Name) => {
      const l2List = [];
      l1Obj.l2Map.forEach((l2Obj, l2Name) => {
        l2List.push({ name: l2Name, code: l2Obj.code, count: l2Obj.count, items: l2Obj.items });
      });
      result.push({ name: l1Name, code: l1Obj.code, count: l1Obj.count, children: l2List });
    });
    return result;
  }, [codes]);

  const activeL1 = useMemo(() => {
    if (!categoryTree.length) return null;
    if (selectedL1 && categoryTree.some(t => t.name === selectedL1)) return selectedL1;
    return categoryTree[0].name;
  }, [categoryTree, selectedL1]);

  const activeL2List = useMemo(() => {
    const found = categoryTree.find(t => t.name === activeL1);
    return found ? found.children : [];
  }, [categoryTree, activeL1]);

  const activeL2 = useMemo(() => {
    if (!activeL2List.length) return null;
    if (selectedL2 && activeL2List.some(l2 => l2.name === selectedL2)) return selectedL2;
    return activeL2List[0].name;
  }, [activeL2List, selectedL2]);

  const activeL3Items = useMemo(() => {
    const found = activeL2List.find(l2 => l2.name === activeL2);
    return found ? found.items : [];
  }, [activeL2List, activeL2]);

  // Phase B: 범위 및 필터에 따른 소분류 목록
  const displayedL3Items = useMemo(() => {
    let list = [];
    if (cScope === 'all') {
      list = [...codes];
    } else if (cScope === 'a') {
      list = codes.filter(c => (c.lv1 || '(대분류 없음)') === activeL1);
    } else {
      // 'b' (선택 중분류)
      list = codes.filter(c => (c.lv1 || '(대분류 없음)') === activeL1 && (c.lv2 || '(중분류 없음)') === activeL2);
    }

    if (cTypeF.size > 0) {
      list = list.filter(c => cTypeF.has(getCodeSrc(c)));
    }
    if (cLockF !== null) {
      list = list.filter(c => getCodeLock(c) === cLockF);
    }
    if (cbSearch.trim()) {
      const q = cbSearch.toLowerCase();
      list = list.filter(c =>
        (c.lv3 && c.lv3.toLowerCase().includes(q)) ||
        (c.lv123code && String(c.lv123code).toLowerCase().includes(q)) ||
        (c.lv1 && c.lv1.toLowerCase().includes(q)) ||
        (c.lv2 && c.lv2.toLowerCase().includes(q))
      );
    }
    return list;
  }, [codes, cScope, activeL1, activeL2, cTypeF, cLockF, cbSearch, typePolicies]);

  // Phase B: 선택된 항목들로부터 하위 소분류 코드 추출
  const selectedLeafCodes = useMemo(() => {
    const out = new Set();
    // cSel.c: 소분류 코드
    cSel.c.forEach(codeNum => out.add(String(codeNum)));

    // cSel.b: 중분류 식별자 `${l1Name}__sep__${l2Name}`
    cSel.b.forEach(bKey => {
      const [l1, l2] = bKey.split('__sep__');
      codes.forEach(c => {
        if ((c.lv1 || '(대분류 없음)') === l1 && (c.lv2 || '(중분류 없음)') === l2) {
          const codeVal = String(c.lv123code || c.lv3code || '');
          if (codeVal) out.add(codeVal);
        }
      });
    });

    // cSel.a: 대분류 이름
    cSel.a.forEach(l1Name => {
      codes.forEach(c => {
        if ((c.lv1 || '(대분류 없음)') === l1Name) {
          const codeVal = String(c.lv123code || c.lv3code || '');
          if (codeVal) out.add(codeVal);
        }
      });
    });

    return Array.from(out);
  }, [cSel, codes]);

  const totalCSelCount = (cSel.a?.size || 0) + (cSel.b?.size || 0) + (cSel.c?.size || 0);

  // 우측 분류 패널 목록
  const sortedSideCodes = useMemo(() => {
    let list = [...codes];
    if (selectedPickL1) {
      list = list.filter(c => c.lv1 === selectedPickL1);
    }
    if (cbSearch.trim()) {
      const q = cbSearch.toLowerCase();
      list = list.filter(c =>
        (c.lv3 && String(c.lv3).toLowerCase().includes(q)) ||
        (c.lv123code && String(c.lv123code).toLowerCase().includes(q)) ||
        (c.lv2 && String(c.lv2).toLowerCase().includes(q)) ||
        (c.lv2code && String(c.lv2code).toLowerCase().includes(q)) ||
        (c.lv1 && String(c.lv1).toLowerCase().includes(q)) ||
        (c.lv1code && String(c.lv1code).toLowerCase().includes(q)) ||
        (c.code_num && String(c.code_num).toLowerCase().includes(q))
      );
    }
    if (cbSort === 'freq') {
      list.sort((a, b) => Number(b.ex_sum || 0) - Number(a.ex_sum || 0));
    } else if (cbSort === 'l1') {
      list.sort((a, b) => {
        const cmp1 = String(a.lv1code || '').localeCompare(String(b.lv1code || ''), undefined, { numeric: true });
        if (cmp1 !== 0) return cmp1;
        const cmp2 = String(a.lv2code || '').localeCompare(String(b.lv2code || ''), undefined, { numeric: true });
        if (cmp2 !== 0) return cmp2;
        return String(a.lv123code || a.code_num || '').localeCompare(String(b.lv123code || b.code_num || ''), undefined, { numeric: true });
      });
    } else if (cbSort === 'code') {
      list.sort((a, b) => String(a.lv123code || a.code_num || '').localeCompare(String(b.lv123code || b.code_num || ''), undefined, { numeric: true }));
    } else if (cbSort === 'name') {
      list.sort((a, b) => String(a.lv3 || '').localeCompare(String(b.lv3 || '')));
    }
    return list;
  }, [codes, selectedPickL1, cbSearch, cbSort]);

  // -------------------------------------------------------------
  // Phase B: 보기유형 정책 및 팝오버 핸들러
  // -------------------------------------------------------------
  const handleToggleTypePolicy = (srcType, lockVal) => {
    const curVal = typePolicies[srcType];
    if (curVal === lockVal) return;

    const targetCodes = codes.filter(c => getCodeSrc(c) === srcType);
    const chgCount = targetCodes.filter(c => getCodeLock(c) !== lockVal).length;

    if (chgCount > 0) {
      const warnMsg = lockVal
        ? `[${srcType}] 정책을 [수정불가]로 변경합니다.\n적용 대상 소분류 ${chgCount}개.\n\n계속하시겠습니까?`
        : `[${srcType}] 정책을 [수정가능]으로 변경합니다.\n적용 대상 소분류 ${chgCount}개.\n※ 수정가능으로 두면 보기분석 실행 시 해당 보기가 모두 삭제되고 새로 생성됩니다.\n\n계속하시겠습니까?`;
      if (!window.confirm(warnMsg)) return;
    }

    setTypePolicies(prev => ({ ...prev, [srcType]: lockVal }));
    setCodes(prev => prev.map(c => {
      if (getCodeSrc(c) === srcType) {
        return { ...c, src: srcType, lock: lockVal, ex_gubun: srcType };
      }
      return c;
    }));
    showToast(`[${srcType}] → ${lockVal ? '수정불가 (실행 시 유지)' : '수정가능 (실행 시 재생성)'}`);
  };

  const handleOpenSrcPop = (c, e) => {
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    const rect = e.currentTarget.getBoundingClientRect();
    const left = Math.min(Math.max(8, rect.left - 60), window.innerWidth - 300);
    const top = (rect.bottom + 6 + 270 > window.innerHeight) ? Math.max(8, rect.top - 270) : rect.bottom + 6;
    setSrcPopPos({ x: left, y: top });
    setSrcPopTarget(c);
    setShowSrcPop(true);
  };

  const handleApplySrc = (newSrc) => {
    if (!srcPopTarget) return;
    const targetCode = srcPopTarget.lv123code || srcPopTarget.lv3code;
    const defaultLock = typePolicies[newSrc] !== undefined ? typePolicies[newSrc] : (newSrc === '자동분석' ? false : true);

    setCodes(prev => prev.map(c => {
      if ((c.lv123code || c.lv3code) === targetCode) {
        return { ...c, src: newSrc, lock: defaultLock, ex_gubun: newSrc };
      }
      return c;
    }));
    setShowSrcPop(false);
    showToast(`[${targetCode}] 보기유형 → ${newSrc} (${defaultLock ? '수정불가' : '수정가능'})`);
  };

  const handleApplyLock = (newLock) => {
    if (!srcPopTarget) return;
    const targetCode = srcPopTarget.lv123code || srcPopTarget.lv3code;

    setCodes(prev => prev.map(c => {
      if ((c.lv123code || c.lv3code) === targetCode) {
        return { ...c, lock: newLock };
      }
      return c;
    }));
    setShowSrcPop(false);
    showToast(`[${targetCode}] ${newLock ? '수정불가 — 실행 시 유지' : '수정가능 — 실행 시 재생성'}`);
  };

  // 팝오버 외부 클릭 및 ESC 닫기
  useEffect(() => {
    const handleGlobalClick = (e) => {
      if (showSrcPop) {
        if (!e.target.closest('#srcpop') && !e.target.closest('.an-sb') && !e.target.closest('.an-lkb')) {
          setShowSrcPop(false);
        }
      }
      if (showPyInfoPop) {
        if (!e.target.closest('#pyinfopop') && !e.target.closest('.an-pyinfo-btn')) {
          setShowPyInfoPop(false);
        }
      }
      if (showQuickSelectPop) {
        if (!e.target.closest('.an-quick-select-wrap')) {
          setShowQuickSelectPop(false);
        }
      }
      if (activeCombo) {
        if (!e.target.closest('.an-combo-wrap')) {
          setActiveCombo(null);
        }
      }
    };
    const handleKeyDown = (e) => {
      if (e.key === 'Escape') {
        if (showSrcPop) setShowSrcPop(false);
        if (showPyInfoPop) setShowPyInfoPop(false);
        if (showQuickSelectPop) setShowQuickSelectPop(false);
        if (activeCombo) setActiveCombo(null);
        if (showColPop) setShowColPop(false);
      }
    };
    window.addEventListener('click', handleGlobalClick);
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('click', handleGlobalClick);
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [showSrcPop, showPyInfoPop, showQuickSelectPop, activeCombo]);

  // -------------------------------------------------------------
  // Phase B: 3단 열 다중 선택 & 일괄 액션 핸들러
  // -------------------------------------------------------------
  const handleColRowClick = (lv, key, item, e) => {
    if (e.target.closest('button') || e.target.closest('.an-sb') || e.target.closest('.an-lkb')) return;

    const isMod = e.shiftKey || e.ctrlKey || e.metaKey;
    if (!isMod && e.target.closest('input')) return; // 인라인 수정 input 클릭 시 일반 포커스

    e.preventDefault();

    if (e.shiftKey && cAnchorRef.current[lv] !== null) {
      let listKeys = [];
      if (lv === 'a') listKeys = categoryTree.map(t => t.name);
      else if (lv === 'b') listKeys = activeL2List.map(l2 => `${activeL1}__sep__${l2.name}`);
      else if (lv === 'c') listKeys = displayedL3Items.map(c => String(c.lv123code || c.lv3code));

      const i = listKeys.indexOf(cAnchorRef.current[lv]);
      const j = listKeys.indexOf(key);
      if (i >= 0 && j >= 0) {
        const start = Math.min(i, j);
        const end = Math.max(i, j);
        setCSel(prev => {
          const next = new Set(prev[lv]);
          for (let idx = start; idx <= end; idx++) next.add(listKeys[idx]);
          return { ...prev, [lv]: next };
        });
      }
    } else if (e.ctrlKey || e.metaKey) {
      setCSel(prev => {
        const next = new Set(prev[lv]);
        if (next.has(key)) next.delete(key);
        else next.add(key);
        return { ...prev, [lv]: next };
      });
      cAnchorRef.current[lv] = key;
    } else {
      if (lv === 'a') {
        setSelectedL1(item.name);
        setSelectedL2(null);
      } else if (lv === 'b') {
        setSelectedL2(item.name);
      }
      setCSel({
        a: new Set(lv === 'a' ? [key] : []),
        b: new Set(lv === 'b' ? [key] : []),
        c: new Set(lv === 'c' ? [key] : [])
      });
      cAnchorRef.current[lv] = key;
    }
  };

  const handleSelectAllCol = (lv) => {
    let listKeys = [];
    if (lv === 'a') listKeys = categoryTree.map(t => t.name);
    else if (lv === 'b') listKeys = activeL2List.map(l2 => `${activeL1}__sep__${l2.name}`);
    else if (lv === 'c') listKeys = displayedL3Items.map(c => String(c.lv123code || c.lv3code));

    const curSet = cSel[lv] || new Set();
    const isAll = listKeys.length > 0 && listKeys.every(k => curSet.has(k));

    setCSel(prev => {
      const next = new Set(prev[lv]);
      if (isAll) {
        listKeys.forEach(k => next.delete(k));
      } else {
        listKeys.forEach(k => next.add(k));
      }
      return { ...prev, [lv]: next };
    });
  };

  const handleClearCSel = () => {
    setCSel({ a: new Set(), b: new Set(), c: new Set() });
    cAnchorRef.current = { a: null, b: null, c: null };
  };

  const handleBulkSrc = (newSrc) => {
    if (!selectedLeafCodes.length) {
      showToast('선택된 항목이 없습니다.');
      return;
    }
    const targetCodesSet = new Set(selectedLeafCodes);
    const targetList = codes.filter(c => targetCodesSet.has(String(c.lv123code || c.lv3code)));
    const chgCount = targetList.filter(c => getCodeSrc(c) !== newSrc).length;
    const defaultLock = typePolicies[newSrc] !== undefined ? typePolicies[newSrc] : (newSrc === '자동분석' ? false : true);

    if (!window.confirm(`선택한 소분류 ${selectedLeafCodes.length}개를 [${newSrc}] 로 변경합니다.\n· 보기유형 변경: ${chgCount}개\n· 수정여부: ${defaultLock ? '수정불가 (실행 시 유지)' : '수정가능 (삭제 후 재생성)'}\n\n계속하시겠습니까?`)) {
      return;
    }

    setCodes(prev => prev.map(c => {
      if (targetCodesSet.has(String(c.lv123code || c.lv3code))) {
        return { ...c, src: newSrc, lock: defaultLock, ex_gubun: newSrc };
      }
      return c;
    }));
    showToast(`${selectedLeafCodes.length}개 소분류의 보기유형을 [${newSrc}]로 변경했습니다.`);
  };

  const handleBulkLock = (newLock) => {
    if (!selectedLeafCodes.length) {
      showToast('선택된 항목이 없습니다.');
      return;
    }
    const targetCodesSet = new Set(selectedLeafCodes);
    const chgCount = codes.filter(c => targetCodesSet.has(String(c.lv123code || c.lv3code)) && getCodeLock(c) !== newLock).length;
    if (chgCount === 0) {
      showToast(`이미 모두 ${newLock ? '수정불가' : '수정가능'} 상태입니다.`);
      return;
    }

    const warnMsg = newLock ? '' : '\n※ 수정가능 보기는 보기분석 실행 시 삭제 후 새로 생성됩니다.';
    if (!window.confirm(`소분류 ${chgCount}개를 [${newLock ? '수정불가' : '수정가능'}]로 변경합니다.${warnMsg}\n\n계속하시겠습니까?`)) {
      return;
    }

    setCodes(prev => prev.map(c => {
      if (targetCodesSet.has(String(c.lv123code || c.lv3code))) {
        return { ...c, lock: newLock };
      }
      return c;
    }));
    showToast(`${chgCount}개를 [${newLock ? '수정불가' : '수정가능'}]로 변경했습니다.`);
  };

  const handleBulkDeleteNodes = () => {
    const nA = cSel.a.size, nB = cSel.b.size, nC = cSel.c.size;
    if (!nA && !nB && !nC) return;
    if (!window.confirm(`선택한 대분류 ${nA}개 · 중분류 ${nB}개 · 소분류 ${nC}개를 삭제하시겠습니까?\n하위 소분류 ${selectedLeafCodes.length}개와 지정된 응답의 분류도 함께 해제됩니다.`)) {
      return;
    }

    const removeCodesSet = new Set(selectedLeafCodes);

    setCodes(prev => prev.filter(c => {
      const codeStr = String(c.lv123code || c.lv3code);
      if (removeCodesSet.has(codeStr)) return false;
      if (cSel.a.has(c.lv1)) return false;
      const bKey = `${c.lv1}__sep__${c.lv2}`;
      if (cSel.b.has(bKey)) return false;
      return true;
    }));

    setResponses(prev => prev.map(r => {
      const codeStr = String(r.lv123code || r.lv3code);
      if (removeCodesSet.has(codeStr) || cSel.a.has(r.lv1)) {
        return { ...r, lv1: '', lv2: '', lv3: '', lv1code: '', lv2code: '', lv3code: '', lv123code: '', _edited: true };
      }
      return r;
    }));

    handleClearCSel();
    showToast(`선택 항목 및 하위 소분류 ${selectedLeafCodes.length}개를 삭제했습니다.`);
  };

  // -------------------------------------------------------------
  // Phase B: 인라인 이름/코드 수정 및 추가 핸들러
  // -------------------------------------------------------------
  const handleRenL1Name = (oldName, newName) => {
    const val = (newName || '').trim();
    if (!val || val === oldName) return;
    setCodes(prev => prev.map(c => c.lv1 === oldName ? { ...c, lv1: val } : c));
    setResponses(prev => prev.map(r => r.lv1 === oldName ? { ...r, lv1: val, _edited: true } : r));
    if (selectedL1 === oldName) setSelectedL1(val);
    showToast(`대분류명을 [${val}]으로 변경했습니다.`);
  };

  const handleRenL1Code = (l1Name, newCode) => {
    const val = (newCode || '').trim();
    setCodes(prev => prev.map(c => c.lv1 === l1Name ? { ...c, lv1code: val } : c));
    setResponses(prev => prev.map(r => r.lv1 === l1Name ? { ...r, lv1code: val, _edited: true } : r));
    showToast(`대분류 코드를 [${val}]로 변경했습니다.`);
  };

  const handleRenL2Name = (l1Name, oldL2Name, newL2Name) => {
    const val = (newL2Name || '').trim();
    if (!val || val === oldL2Name) return;
    setCodes(prev => prev.map(c => (c.lv1 === l1Name && c.lv2 === oldL2Name) ? { ...c, lv2: val } : c));
    setResponses(prev => prev.map(r => (r.lv1 === l1Name && r.lv2 === oldL2Name) ? { ...r, lv2: val, _edited: true } : r));
    if (selectedL2 === oldL2Name) setSelectedL2(val);
    showToast(`중분류명을 [${val}]으로 변경했습니다.`);
  };

  const handleRenL2Code = (l1Name, l2Name, newCode) => {
    const val = (newCode || '').trim();
    setCodes(prev => prev.map(c => (c.lv1 === l1Name && c.lv2 === l2Name) ? { ...c, lv2code: val } : c));
    setResponses(prev => prev.map(r => (r.lv1 === l1Name && r.lv2 === l2Name) ? { ...r, lv2code: val, _edited: true } : r));
    showToast(`중분류 코드를 [${val}]로 변경했습니다.`);
  };

  const handleRenL3Name = (codeNum, newName) => {
    const val = (newName || '').trim();
    if (!val) return;
    setCodes(prev => prev.map(c => (c.lv123code || c.lv3code) === codeNum ? { ...c, lv3: val } : c));
    setResponses(prev => prev.map(r => (r.lv123code || r.lv3code) === codeNum ? { ...r, lv3: val, _edited: true } : r));
    showToast(`소분류명을 [${val}]으로 변경했습니다.`);
  };

  const handleRenL3Code = (oldCode, newCode) => {
    const val = (newCode || '').trim();
    if (!val || val === oldCode) return;
    setCodes(prev => prev.map(c => (c.lv123code || c.lv3code) === oldCode ? { ...c, lv123code: val, lv3code: val } : c));
    setResponses(prev => prev.map(r => (r.lv123code || r.lv3code) === oldCode ? { ...r, lv123code: val, lv3code: val, _edited: true } : r));
    showToast(`소분류 코드를 [${val}]로 변경했습니다.`);
  };

  const handleAddL1 = () => {
    const l1Count = categoryTree.length + 1;
    const l1Name = `대분류 ${l1Count}`;
    const nextCode = String(l1Count);
    const nextL3Code = String(codes.length + 1);
    const newCode = {
      lv1: l1Name,
      lv1code: nextCode,
      lv2: `중분류 1`,
      lv2code: '1',
      lv3: `소분류 1`,
      lv123code: nextL3Code,
      lv3code: nextL3Code,
      ex_sum: 0,
      ex_gubun: '설문지',
      src: '설문지',
      lock: true
    };
    setCodes(prev => [...prev, newCode]);
    setSelectedL1(l1Name);
    setSelectedL2(`중분류 1`);
    showToast(`대분류 [${l1Name}] 추가 완료 (이름을 클릭하여 바로 수정 가능)`);
  };

  const handleAddL2 = () => {
    const l1Name = activeL1 || (categoryTree[0]?.name) || '대분류 1';
    const curL2Count = activeL2List.length + 1;
    const l2Name = `중분류 ${curL2Count}`;
    const nextL2Code = String(curL2Count);
    const nextL3Code = String(codes.length + 1);
    const newCode = {
      lv1: l1Name,
      lv1code: categoryTree.find(t => t.name === l1Name)?.code || '1',
      lv2: l2Name,
      lv2code: nextL2Code,
      lv3: `소분류 1`,
      lv123code: nextL3Code,
      lv3code: nextL3Code,
      ex_sum: 0,
      ex_gubun: '설문지',
      src: '설문지',
      lock: true
    };
    setCodes(prev => [...prev, newCode]);
    setSelectedL2(l2Name);
    showToast(`중분류 [${l2Name}] 추가 완료`);
  };

  const handleAddL3 = () => {
    const l1Name = activeL1 || (categoryTree[0]?.name) || '대분류 1';
    const l2Name = activeL2 || (activeL2List[0]?.name) || '중분류 1';
    const curL3Count = displayedL3Items.length + 1;
    const l3Name = `새 소분류 ${curL3Count}`;
    const nextCode = String(codes.length + 1);
    const newCode = {
      lv1: l1Name,
      lv1code: categoryTree.find(t => t.name === l1Name)?.code || '1',
      lv2: l2Name,
      lv2code: activeL2List.find(l2 => l2.name === l2Name)?.code || '1',
      lv3: l3Name,
      lv123code: nextCode,
      lv3code: nextCode,
      ex_sum: 0,
      ex_gubun: '자동분석',
      src: '자동분석',
      lock: typePolicies['자동분석'] !== undefined ? typePolicies['자동분석'] : false
    };
    setCodes(prev => [...prev, newCode]);
    showToast(`소분류 [${l3Name}] (코드:${nextCode}) 추가 완료`);
  };

  const handleEditL1 = () => {
    if (!selectedPickL1) {
      showToast('수정할 대분류를 먼저 선택해주세요.');
      return;
    }
    setInputL1Name(selectedPickL1);
    setEditingL1Name(true);
  };

  const handleSaveL1Name = () => {
    if (!inputL1Name.trim()) {
      setEditingL1Name(false);
      return;
    }
    const oldName = selectedPickL1;
    const newName = inputL1Name.trim();
    setCodes(prev => prev.map(c => c.lv1 === oldName ? { ...c, lv1: newName } : c));
    setResponses(prev => prev.map(r => r.lv1 === oldName ? { ...r, lv1: newName, _edited: true } : r));
    setSelectedPickL1(newName);
    if (selectedL1 === oldName) setSelectedL1(newName);
    setEditingL1Name(false);
    showToast(`대분류명이 [${newName}]으로 수정되었습니다.`);
  };

  const handleDeleteL1 = () => {
    if (!selectedPickL1) {
      showToast('삭제할 대분류를 먼저 선택해주세요.');
      return;
    }
    if (!window.confirm(`대분류 [${selectedPickL1}] 및 소속된 모든 보기를 삭제하시겠습니까?`)) return;
    const delTarget = selectedPickL1;
    setCodes(prev => prev.filter(c => c.lv1 !== delTarget));
    setResponses(prev => prev.map(r => r.lv1 === delTarget ? { ...r, lv1: '', lv2: '', lv3: '', lv123code: '', _edited: true } : r));
    setSelectedPickL1('');
    if (selectedL1 === delTarget) setSelectedL1(null);
    showToast(`대분류 [${delTarget}]이(가) 삭제되었습니다.`);
  };

  const handleAddSubcategory = () => {
    if (!newSubName.trim()) {
      showToast('소분류명을 입력해주세요.');
      return;
    }
    const codeVal = newSubCode.trim() || String(codes.length + 1);
    const newCode = {
      lv1: newSubL1 || (selectedPickL1 || (activeL1 !== '(대분류 없음)' ? activeL1 : '')),
      lv2: newSubL2 || (activeL2 !== '(중분류 없음)' ? activeL2 : ''),
      lv3: newSubName.trim(),
      lv123code: codeVal,
      ex_sum: 0,
      ex_gubun: '수동',
      src: '설문지',
      lock: true
    };
    setCodes(prev => [newCode, ...prev]);
    setNewSubName('');
    setNewSubCode('');
    showToast(`소분류 [${newCode.lv3}] (코드:${codeVal})이(가) 추가되었습니다.`);
  };

  const handleStartEditSub = (c, e) => {
    if (e) e.stopPropagation();
    setEditingSubCodeId(c.lv123code);
    setEditingSubName(c.lv3 || '');
  };

  const handleSaveSubName = (codeVal) => {
    if (!editingSubName.trim()) {
      setEditingSubCodeId(null);
      return;
    }
    const newName = editingSubName.trim();
    setCodes(prev => prev.map(c => c.lv123code === codeVal ? { ...c, lv3: newName } : c));
    setResponses(prev => prev.map(r => r.lv123code === codeVal ? { ...r, lv3: newName, _edited: true } : r));
    setEditingSubCodeId(null);
    showToast(`소분류명이 [${newName}]으로 수정되었습니다.`);
  };

  const handleDeleteSub = (c, e) => {
    if (e) e.stopPropagation();
    if (!window.confirm(`소분류 [${c.lv3}] (${c.lv123code})를 삭제하시겠습니까?`)) return;
    setCodes(prev => prev.filter(item => item.lv123code !== c.lv123code));
    setResponses(prev => prev.map(r => r.lv123code === c.lv123code ? { ...r, lv123code: '', lv3: '', _edited: true } : r));
    showToast(`소분류 [${c.lv3}]이(가) 삭제되었습니다.`);
  };

  // [← 매핑] 고속 버튼 핸들러 (구형 UI 계승: 선택된 행 또는 포커스 행에 대·중·소 전체 즉시 매핑)
  const handleAssignCategoryFromRow = (codeObj) => {
    let targetKeys = new Set(selectedRowKeys);
    if (targetKeys.size === 0 && focusedRowIdx !== null && filteredResponses[focusedRowIdx]) {
      targetKeys.add(getRowKey(filteredResponses[focusedRowIdx], focusedRowIdx));
    }
    if (targetKeys.size === 0 && filteredResponses.length > 0) {
      targetKeys.add(getRowKey(filteredResponses[0], 0));
      setFocusedRowIdx(0);
    }
    if (targetKeys.size === 0) {
      showToast('좌측에서 매핑할 응답을 먼저 선택해주세요.');
      return;
    }

    setResponses(prev => prev.map((r, i) => {
      const k = getRowKey(r, i);
      if (targetKeys.has(k)) {
        return {
          ...r,
          lv1: codeObj.lv1 || '',
          lv1code: codeObj.lv1code || '',
          lv2: codeObj.lv2 || '',
          lv2code: codeObj.lv2code || '',
          lv3: codeObj.lv3 || '',
          lv3code: codeObj.lv3code || codeObj.lv123code || codeObj.code_num || '',
          lv123code: codeObj.lv123code || codeObj.code_num || '',
          _edited: true
        };
      }
      return r;
    }));

    showToast(`${targetKeys.size}건에 [${codeObj.lv123code || codeObj.code_num}] ${codeObj.lv3} 매핑 완료!`);
  };

  // [→ 해제] 핸들러 (선택된 응답에서 해당 보기 분류 해제)
  const handleUnassignCategoryFromRow = (codeObj) => {
    const codeVal = String(codeObj.lv123code || codeObj.code_num || '');
    const targetKeys = new Set(selectedRowKeys);

    if (targetKeys.size > 0) {
      let unassignCount = 0;
      setResponses(prev => prev.map((r, i) => {
        const k = getRowKey(r, i);
        if (targetKeys.has(k) && String(r.lv123code) === codeVal) {
          unassignCount++;
          return { ...r, lv1: '', lv1code: '', lv2: '', lv2code: '', lv3: '', lv3code: '', lv123code: '', _edited: true };
        }
        return r;
      }));
      if (unassignCount > 0) {
        showToast(`선택된 응답 중 ${unassignCount}건의 분류를 해제(미분류)했습니다.`);
      } else {
        showToast('선택된 응답에 해당 보기가 지정되어 있지 않습니다.');
      }
    } else {
      showToast('좌측에서 분류를 해제할 응답 행을 먼저 선택해주세요.');
    }
  };

  // [🗑 삭제] 핸들러 (보기선택편집 그리드 마지막 열 삭제 버튼: 보기 자체 삭제 및 매핑 응답 미분류 처리)
  const handleDeleteCompactCodeRow = (codeObj, targetIdx) => {
    const codeVal = String(codeObj.lv123code || codeObj.code_num || '');
    const label = codeObj.lv3 || codeVal;
    if (window.confirm(`[${label}] (코드: ${codeVal}) 보기를 삭제하시겠습니까?\n매핑된 응답 데이터도 미분류 처리됩니다.`)) {
      setCodes(prev => prev.filter((_, idx) => idx !== targetIdx));
      setResponses(prev => prev.map(r => String(r.lv123code) === codeVal ? { ...r, lv1: '', lv1code: '', lv2: '', lv2code: '', lv3: '', lv3code: '', lv123code: '', _edited: true } : r));
      setCodesDirty(true);
      showToast(`[${label}] 보기가 삭제되었습니다.`);
    }
  };

  // 보기 리스트 종합 코드-레이블 일치성 유효성 검사 (저장 전 호출)
  const validateCodeLabelConsistency = (targetCodes = codes) => {
    const codeToLabelMap = new Map();
    const l1CodeToNameMap = new Map();
    const l2CodeToNameMap = new Map();

    for (let i = 0; i < targetCodes.length; i++) {
      const item = targetCodes[i];
      const code = String(item.lv123code || item.code_num || '').trim();
      const label = String(item.lv3 || '').trim();

      // 1. 소코드 고유성 검사
      if (code && label) {
        if (codeToLabelMap.has(code)) {
          const existingLabel = codeToLabelMap.get(code);
          if (existingLabel !== label) {
            return {
              valid: false,
              message: `[소코드 중복 오류] 소코드 '${code}'에 서로 다른 레이블('[${existingLabel}]' vs '[${label}]')이 정의되어 있습니다. 동일 코드로 정의할 수 없습니다.`
            };
          }
        } else {
          codeToLabelMap.set(code, label);
        }
      }

      // 2. 대분류 코드 일치성 검사
      const l1Code = String(item.lv1code || '').trim();
      const l1Name = String(item.lv1 || '').trim();
      if (l1Code && l1Name) {
        if (l1CodeToNameMap.has(l1Code) && l1CodeToNameMap.get(l1Code) !== l1Name) {
          return {
            valid: false,
            message: `[대코드 중복 오류] 대코드 '${l1Code}'에 서로 다른 대분류명('[${l1CodeToNameMap.get(l1Code)}]' vs '[${l1Name}]')이 지정되어 있습니다.`
          };
        }
        l1CodeToNameMap.set(l1Code, l1Name);
      }

      // 3. 중분류 코드 일치성 검사 (동일 대분류 내)
      const l2Code = String(item.lv2code || '').trim();
      const l2Name = String(item.lv2 || '').trim();
      if (l1Name && l2Code && l2Name) {
        const key = `${l1Name}__${l2Code}`;
        if (l2CodeToNameMap.has(key) && l2CodeToNameMap.get(key) !== l2Name) {
          return {
            valid: false,
            message: `[중코드 중복 오류] 대분류 [${l1Name}] 내 중코드 '${l2Code}'에 서로 다른 중분류명('[${l2CodeToNameMap.get(key)}]' vs '[${l2Name}]')이 지정되어 있습니다.`
          };
        }
        l2CodeToNameMap.set(key, l2Name);
      }
    }
    return { valid: true };
  };

  // 우측 패널 최상단에 새 보기 행 추가
  const handleCbAddNewRow = () => {
    setCodes(prev => {
      const maxNum = prev.reduce((max, c) => {
        const n = parseInt(c.lv123code || c.code_num, 10);
        return !isNaN(n) && n > max ? n : max;
      }, 0);
      const nextCode = String(maxNum + 1);

      const newRow = {
        code_num: nextCode,
        lv123code: nextCode,
        lv3code: nextCode,
        lv1: '',
        lv1code: '',
        lv2: '',
        lv2code: '',
        lv3: '',
        ex_sum: 0,
        ex_gubun: '분석(수정가능)',
        src: '자동분석',
        lock: false
      };
      return [newRow, ...prev];
    });
    setCodesDirty(true);
    showToast('새 보기 행이 추가되었습니다. 대/중분류 선택 및 레이블을 입력해주세요.');
  };

  // 기존 호환용 handleAssignCategory
  const handleAssignCategory = (c) => {
    handleAssignCategoryFromRow(c);
  };

  return (
    <div className={`an-app${isSidebarCollapsed ? ' sidebar-mini' : ''}`}>
      {/* ========== 사이드바 ========== */}
      <aside className={`an-sb${isSidebarCollapsed ? ' mini' : ''}`}>
        <div className="an-brand">
          <i className="ti ti-home" onClick={onBackToProjects} title="홈 (프로젝트 목록)" />
          <span className="an-brand-sep" />
          <span className="an-brand-full">
            <i className="ti ti-menu-2" />
            <span className="lg"><em>AI</em>오픈분석</span>
          </span>
        </div>

        <div className="an-prj" title={`${projectName || '프로젝트'} (${projectnum})`}>
          <div className="an-prj-full">
            <span className="nm">{projectName || projectnum}</span>
            <span className="cd">{projectnum}</span>
            <div className="an-sp" />
            <i className="ti ti-settings" />
          </div>
          <div className="an-prj-mini" title={`${projectName || '프로젝트'} (${projectnum})`}>
            <i className="ti ti-folder" />
          </div>
        </div>

        <div className="an-navt">분석 설정 <i className="ti ti-chevron-up" /></div>
        <div className="an-nav">
          <div className="an-nv" title="문항 목록" onClick={onBackToList}>
            <i className="ti ti-file-text" /><span>문항 목록</span>
          </div>
          <div className="an-nv on" title="분석">
            <i className="ti ti-adjustments-alt" /><span>분석</span>
          </div>
          <div className="an-nv" title="토큰 사용 내역" onClick={() => showToast('토큰 사용 내역 페이지는 준비 중입니다')}>
            <i className="ti ti-chart-bar" /><span>토큰 사용 내역</span>
          </div>
        </div>

        <div className="an-sbf">
          <div className="an-fcard" title="누적액 $ 360">
            <div className="an-fcard-full" style={{ display: 'flex', alignItems: 'center', width: '100%', gap: '6px' }}>
              <span className="lb">누적액 $</span>
              <div className="an-sp" />
              <span className="vl">360</span>
              <button className="btn-pill" onClick={() => showToast('사용량을 갱신했습니다')}>업데이트</button>
            </div>
            <div className="an-fcard-mini" title="누적액: $360">
              <i className="ti ti-coin" />
            </div>
          </div>
          <div className="an-fcard bal" title="OPEN AI 잔액: 약 $335.32">
            <div className="an-fcard-full" style={{ display: 'flex', alignItems: 'center', width: '100%', gap: '6px' }}>
              <i className="ti ti-sparkles" style={{ color: 'var(--brand)' }} />
              <span>OPEN AI 잔액</span>
              <div className="an-sp" />
              <span className="vl">약 $335.32</span>
            </div>
            <div className="an-fcard-mini" title="OPEN AI 잔액: 약 $335.32">
              <i className="ti ti-sparkles" />
            </div>
          </div>
        </div>

        <div className="an-usr" title={`${username}님`}>
          <i className="ti ti-user-circle" /><span>{username}님</span>
        </div>
      </aside>

      {/* ========== 메인 본문 ========== */}
      <main className="an-main">
        {/* 상단 헤더 */}
        <header className="an-top">
          <span
            className="an-collapse"
            onClick={() => setIsSidebarCollapsed(prev => !prev)}
            title={isSidebarCollapsed ? "사이드바 펼치기" : "사이드바 간소화"}
          >
            <i className={`ti ti-chevron-${isSidebarCollapsed ? 'right' : 'left'}`} />
          </span>
          <span className="an-head-sep" />
          <h1>분석</h1>
          <i className="ti ti-info-circle hi" style={{ color: 'var(--tx3, #9AA0A7)', fontSize: '15px', marginLeft: '4px', cursor: 'pointer' }} title="분석 도움말" />

          {/* 문항 네비게이션 */}
          <div className="an-qnav" style={{ position: 'relative' }}>
            <button
              className="an-btn an-qbtn"
              disabled={currentIdx <= 0}
              onClick={() => handleSelectQnum(currentIdx - 1)}
              title="이전 문항"
            >
              <i className="ti ti-chevron-left" />
            </button>

            <div
              className="an-qchip"
              onClick={() => setShowQPop(prev => !prev)}
              title="문항 선택 (클릭하여 검색 및 변경)"
            >
              <b>{currentItem.qnum_text || currentItem.qnum || '선택'}</b>
              <span className="sep">·</span>
              <span>{currentItem.question_title || currentItem.qnum || '문항명'}</span>
              <i className="ti ti-selector sel" />
            </div>

            <button
              className="an-btn an-qbtn"
              disabled={currentIdx >= navList.length - 1}
              onClick={() => handleSelectQnum(currentIdx + 1)}
              title="다음 문항"
            >
              <i className="ti ti-chevron-right" />
            </button>

            <span className="an-qpos">
              {navList.length > 0 ? `${currentIdx + 1} / ${navList.length}` : ''}
            </span>

            {/* 문항 선택 팝오버 */}
            {showQPop && (
              <div className="an-qpop" style={{ display: 'block' }}>
                <div className="qs2">
                  <input
                    placeholder="문항번호 · 문항명 검색"
                    value={qSearch}
                    onChange={(e) => setQSearch(e.target.value)}
                    autoFocus
                  />
                </div>
                <div className="ql">
                  {navList
                    .filter(x => !qSearch.trim() || `${x.qnum} ${x.qnum_text} ${x.question_title}`.toLowerCase().includes(qSearch.toLowerCase()))
                    .map((item, idx) => {
                      const realIdx = navList.findIndex(n => n.qnum === item.qnum);
                      const isSelected = realIdx === currentIdx;
                      return (
                        <div
                          key={item.qnum || idx}
                          className={`an-qrow${isSelected ? ' on' : ''}`}
                          onClick={() => handleSelectQnum(realIdx)}
                        >
                          <span className="qn">{item.qnum_text || item.qnum}</span>
                          <span className="qt2">{item.question_title || '-'}</span>
                          <span className={`an-qst ${item.status === '완료' ? 'done' : 'wait'}`}>
                            {item.status || '대기'}
                          </span>
                        </div>
                      );
                    })}
                </div>
                <div className="qf">총 {navList.length}개 문항 (분석 대상)</div>
              </div>
            )}
          </div>

          <div className="an-sp" />

          {/* 최상단 엑셀 다운로드 아이콘 버튼 */}
          <button
            className="an-btn-icon-excel"
            onClick={handleDownloadExcel}
            disabled={isExportingExcel}
            title="응답 데이터와 보기 데이터를 포함한 다중 시트 엑셀(.xlsx) 다운로드"
            style={{ marginRight: '4px' }}
          >
            <i className={`ti ti-${isExportingExcel ? 'loader an-spin' : 'download'}`} />
            <span>엑셀 다운</span>
          </button>

          {/* 최상단 엑셀 업로드 아이콘 버튼 */}
          <button
            className="an-btn-icon-excel up"
            onClick={handleOpenExcelFilePicker}
            disabled={isUploadingExcel}
            title="수정된 보기 목록 엑셀 파일 업로드"
            style={{ marginRight: '6px' }}
          >
            <i className={`ti ti-${isUploadingExcel ? 'loader an-spin' : 'upload'}`} />
            <span>엑셀 업로드</span>
          </button>

          {/* 새로고침 버튼 */}
          <button
            className="an-btn gh xs"
            onClick={() => handleRefreshAll(false)}
            title="서버 최신 데이터 전체 새로고침 (응답·보기·rawdata)"
            disabled={loadingData}
            style={{ marginRight: '6px' }}
          >
            <i className={`ti ti-refresh${loadingData ? ' spin' : ''}`} />새로고침
          </button>

          {/* 저장 상태 표시 */}
          <span className={`an-saved${(pendingCount > 0 || isLevelDirty || codesDirty || isDuplicateRemoveDirty) ? ' dirty' : ''}`}>
            <i className={`ti ti-cloud-${(pendingCount > 0 || isLevelDirty || codesDirty || isDuplicateRemoveDirty) ? 'exclamation' : 'check'}`} />
            {pendingCount > 0
              ? `DB 미반영 ${pendingCount}건`
              : isLevelDirty
                ? `분류 단계 변경 (${initialLevelMode}단계 → ${levelMode}단계)`
                : codesDirty
                  ? '보기 변경 (미저장)'
                  : isDuplicateRemoveDirty
                    ? `중복코드 제거 ${duplicateRemove ? '적용' : '해제'} (미저장)`
                    : (headerMeta.duplicateRemoveDate ? `DB 반영 완료 (${headerMeta.duplicateRemoveDate})` : 'DB 최신 상태')}
          </span>

          <span
            className={`an-tgl${duplicateRemove ? ' on' : ''}`}
            onClick={() => {
              setDuplicateRemove(v => {
                const next = !v;
                showToast(next ? '중복코드 전체 정제 모드 (추천) 적용' : '기본 결측 정제 모드 적용 (일반중복 보존)');
                return next;
              });
            }}
            title={duplicateRemove ? '현재: [모드 A] 전체 중복코드 제거 (클릭 시 기본 결측 정제 전환)' : '현재: [모드 B] 기본 결측 정제 (클릭 시 전체 중복제거 전환)'}
            style={{ fontWeight: 600 }}
          >
            <span className="an-sw"><i className="ti ti-check" /></span>
            {duplicateRemove ? '중복제거 (전체정제 ON)' : '중복제거 (기본정제 OFF)'}
          </span>

          <button
            className="an-btn br"
            onClick={handleOpenSaveModal}
            title="저장 및 DB 반영 비교"
          >
            <i className="ti ti-device-floppy" />저장
            {(pendingCount > 0 || isLevelDirty || codesDirty || isDuplicateRemoveDirty) && (
              <span className="sbadge on">
                {pendingCount > 0 ? pendingCount : (isLevelDirty ? '단계' : (codesDirty ? '보기' : '중복제거'))}
              </span>
            )}
          </button>
        </header>

        {/* 상단 설정 바 */}
        <div className="an-setbar">
          <span
            className={`an-setbtn${setOpen ? ' on' : ''}`}
            onClick={() => setSetOpen(prev => !prev)}
          >
            <i className="ti ti-settings-automation" />분석 설정
            <i className="ti ti-chevron-down cv" />
          </span>

          <div className="an-sumline" title={summaryLine}>
            {summaryLine}
          </div>

          <div className="an-sp" />

          <button
            className="an-btn gh"
            title="콘솔 열기/닫기"
            onClick={() => {
              setShowConsole(v => {
                const next = !v;
                if (next) setSetOpen(true);
                return next;
              });
            }}
          >
            <i className="ti ti-terminal-2" />
          </button>
        </div>

        {/* 접이식 설정 패널 */}
        {setOpen && (
          <div className="an-setpanel">
            <div className="an-sgrid">
              <div className="an-sf">
                <label>문항명</label>
                <input
                  className="tx"
                  value={qName}
                  onChange={(e) => setQName(e.target.value)}
                  placeholder="문항명 입력"
                />
              </div>

              <div className="an-sf">
                <label>프롬프트 지침</label>
                <textarea
                  value={promptText}
                  onChange={(e) => setPromptText(e.target.value)}
                  placeholder="프롬프트 지침 입력"
                  rows={3}
                />
              </div>

              <div className="an-sf">
                <label>옵션</label>
                <div className="an-orow">
                  <span>결과언어</span>
                  <select
                    className="an-sl2"
                    value={selectedLang}
                    onChange={(e) => setSelectedLang(e.target.value)}
                  >
                    {langOptions.map((l, i) => (
                      <option key={l.keyvalue || i} value={l.keyvalue}>
                        {l.keytext || l.keyvalue}
                      </option>
                    ))}
                    {langOptions.length === 0 && <option value="한국어">한국어 (Korean)</option>}
                  </select>
                </div>

                <div className="an-orow">
                  <span>모델선택</span>
                  <select
                    className="an-sl2"
                    value={selectedModel}
                    onChange={(e) => setSelectedModel(e.target.value)}
                  >
                    {modelOptions.map((m, i) => (
                      <option key={m.keyvalue || i} value={m.keyvalue}>
                        {m.keytext || m.keyvalue}
                      </option>
                    ))}
                    {modelOptions.length === 0 && <option value="GPT-4o">GPT-4o</option>}
                  </select>
                </div>

                <div className="an-orow">
                  <span>창의성</span>
                  <input
                    type="range"
                    min="0"
                    max="1"
                    step="0.1"
                    value={temperature}
                    onChange={(e) => setTemperature(parseFloat(e.target.value))}
                  />
                  <b>{temperature.toFixed(1)}</b>
                </div>
              </div>

              <div className="an-sf">
                <label>분류 개수 설정</label>
                <div className="an-nums">
                  <label>소분류
                    <input
                      value={openLv1}
                      onChange={(e) => setOpenLv1(e.target.value)}
                    />
                  </label>
                  <label className="l2c">중분류
                    <input
                      value={openLv2}
                      onChange={(e) => setOpenLv2(e.target.value)}
                    />
                  </label>
                  <label className="l1c">대분류
                    <input
                      value={openLv3}
                      onChange={(e) => setOpenLv3(e.target.value)}
                    />
                  </label>
                </div>
              </div>
            </div>

            {/* 콘솔 창 */}
            <div className={`an-console${showConsole ? ' on' : ''}`}>
              <div className="an-con-head">
                <span>
                  <i className="ti ti-terminal-2" style={{ marginRight: '6px' }} />
                  Python Worker 실행 콘솔
                  {workerRunning && (
                    <span className="an-badge" style={{ marginLeft: '8px', background: '#3b82f6', color: '#fff', fontSize: '11px', padding: '2px 6px', borderRadius: '4px' }}>
                      실행 중
                    </span>
                  )}
                </span>
                <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                  {workerRunning && (
                    <button className="an-btn xs" onClick={handleStopWorker} style={{ background: '#ef4444', color: '#fff', borderColor: '#dc2626' }}>
                      <i className="ti ti-player-stop" />중지
                    </button>
                  )}
                  <button className="an-btn gh xs" onClick={() => setConsoleLines([])} title="콘솔 비우기"><i className="ti ti-trash" /></button>
                  <button className="an-btn gh xs" onClick={() => setShowConsole(false)} title="닫기"><i className="ti ti-x" /></button>
                </div>
              </div>
              <div className="an-con-body" ref={consoleBodyRef}>
                {consoleLines.length === 0 ? (
                  <div style={{ color: '#64748b' }}>콘솔 출력이 없습니다. 실행 버튼을 누르면 로그가 표시됩니다.</div>
                ) : (
                  consoleLines.map((line, idx) => (
                    <div key={idx} className="an-con-line">{line}</div>
                  ))
                )}
              </div>
            </div>
          </div>
        )}

        {/* 바디 작업 영역 */}
        <div className="an-body">
          {/* 좌측 메인 작업 창 */}
          <section className="an-pane an-work">
            {/* 탭 툴바 */}
            <div className="an-wtool">
              <div className="an-segs">
                <div
                  className={`an-seg${activeTab === 'resp' ? ' on' : ''}`}
                  onClick={() => setActiveTab('resp')}
                >
                  응답 데이터<span className="n">{responses.length.toLocaleString()}</span>
                </div>
                <div
                  className={`an-seg${activeTab === 'code' ? ' on' : ''}`}
                  onClick={() => setActiveTab('code')}
                >
                  보기 데이터<span className="n">{codes.length.toLocaleString()}</span>
                </div>
                <div
                  className={`an-seg${activeTab === 'raw' ? ' on' : ''}`}
                  onClick={() => {
                    setActiveTab('raw');
                    if (rawdata.length === 0) {
                      fetchRawdata();
                    }
                  }}
                >
                  PID별데이터<span className="n">{rawdata.length.toLocaleString()}</span>
                </div>
              </div>

              <div className="an-sp" />

              {/* [요구사항] 응답 데이터 탭 전용: 응답자분석(전체) -> 응답자분석(빈셀) -> 번역 순서 및 문구 */}
              {activeTab === 'resp' && (
                <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                  <button
                    className="an-btn xs br"
                    onClick={() => handleRunWorker('respondent')}
                    disabled={workerRunning}
                    title="정의된 보기를 기준으로 전체 응답자 데이터를 자동 분류/코딩합니다"
                  >
                    <i className="ti ti-users" />응답자분석(전체)
                  </button>
                  <button
                    className="an-btn xs"
                    onClick={() => handleRunWorker('empty_etc')}
                    disabled={workerRunning}
                    title="미분류된 빈셀 및 기타 응답을 재수집하여 보완 분류합니다"
                  >
                    <i className="ti ti-filter-check" />응답자분석(빈셀)
                  </button>
                  <button
                    className="an-btn xs"
                    onClick={() => handleRunWorker('translate')}
                    disabled={workerRunning}
                    title="외국어 응답을 한국어로 번역합니다"
                  >
                    <i className="ti ti-language" />번역
                  </button>
                </div>
              )}

              {/* [요구사항 8, 9] 보기 데이터 탭 전용: 보기 불러오기, 보기분석 실행 */}
              {activeTab === 'code' && (
                <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                  <button
                    className="an-btn xs"
                    onClick={handleOpenImportModal}
                    title="타 프로젝트나 다른 문항에서 작성된 보기 목록을 검색하여 복사합니다"
                  >
                    <i className="ti ti-file-import" />보기 불러오기
                  </button>
                  <button
                    className="an-btn xs br"
                    onClick={() => handleRunWorker('analyze')}
                    disabled={workerRunning}
                    title="전체 응답을 분석하여 보기(대/중/소분류)를 새롭게 자동 생성합니다"
                  >
                    <i className="ti ti-sparkles" />보기분석 실행
                  </button>
                </div>
              )}
            </div>

            {/* 필터 / 통계 상태줄 (activeTab !== 'code' 일 때만 표시) */}
            {activeTab !== 'code' && (
              <div className="an-wstat" style={{ position: 'relative' }}>
              {/* 응답 데이터 탭 전용: 컬럼 설정 및 분류 단계 선택 */}
              {activeTab === 'resp' && (
                <>
                  <button
                    id="colpopbtn"
                    className={`an-btn xs${showColPop ? ' br' : ''}`}
                    onClick={handleOpenColPop}
                    title="컬럼 표시 설정"
                  >
                    <i className="ti ti-columns-3" />컬럼
                  </button>

                  <select
                    className="an-sl2 xs an-step-select"
                    value={levelMode}
                    onChange={(e) => setLevelMode(e.target.value)}
                    title="분류 단계 — 3단계: 대·중·소 / 2단계: 중·소 / 1단계: 소분류"
                  >
                    <option value="3">3단계 (대,중,소)</option>
                    <option value="2">2단계 (중,소)</option>
                    <option value="1">1단계 (소)</option>
                  </select>
                </>
              )}

              {/* 응답 탭인 경우 상태 칩 노출 (미분류율 프로그레스바는 삭제됨) */}
              {activeTab === 'resp' ? (
                <>
                  {/* 상태 칩: 전체, 미분류(중요도 높음), 미확인(중요도 낮음) - 수정됨/멀티 삭제 */}
                  <div className="an-chips">
                    <span
                      className={`an-fchip${statusFilter === 'all' ? ' on' : ''}`}
                      onClick={() => setStatusFilter('all')}
                    >
                      전체 <b>{filterCounts.all}</b>
                    </span>
                    <span
                      className={`an-fchip fchip-high-none${statusFilter === 'none' ? ' on' : ''}`}
                      onClick={() => setStatusFilter('none')}
                      title="미분류 응답 (중요도 높음)"
                    >
                      미분류 <b>{filterCounts.none}</b>
                    </span>
                    <span
                      className={`an-fchip fchip-low-un${statusFilter === 'un' ? ' on' : ''}`}
                      onClick={() => setStatusFilter('un')}
                      title="미확인 응답 (중요도 낮음)"
                    >
                      미확인 <b>{filterCounts.un}</b>
                    </span>
                  </div>

                  <div className="an-sp" />

                  {/* ⚡ 고속 펀칭 단축키 안내 바 */}
                  <div className="an-punch-guide-bar" title="키보드만으로 마우스 없이 빠른 연속 코딩 작업이 가능합니다">
                    <span className="guide-title"><i className="ti ti-bolt" />고속 펀칭:</span>
                    <span><kbd>Enter</kbd> 다음 응답</span>
                    <span><kbd>Shift + Enter</kbd> 멀티 추가</span>
                    <span><kbd>Del</kbd> 행 삭제</span>
                    <span><kbd>↑ / ↓</kbd> 이동</span>
                  </div>

                  {/* 시안의 다중 선택 인디케이터 (.selind) */}
                  <span className={`an-selind${selectedRowKeys.size > 0 ? ' on' : ''}`}>
                    {selectedRowKeys.size}건 선택
                    <button onClick={clearSelection} title="선택 해제"><i className="ti ti-x" /></button>
                  </span>
                </>
              ) : (
                <>
                  <span style={{ fontSize: '11.5px', color: 'var(--tx2)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span>조회 건수: <b>{rawdata.length.toLocaleString()}</b>건</span>
                    {rawdata.length > 500 && (
                      <span style={{ color: 'var(--tx3)' }}>(화면에는 상위 500건 표시)</span>
                    )}
                  </span>
                  <div className="an-sp" />
                  <button
                    className="an-btn gh xs"
                    onClick={() => fetchRawdata(true)}
                    title="PID별데이터 최신 상태로 새로고침"
                    disabled={loadingData}
                    style={{ padding: '2px 8px' }}
                  >
                    <i className={`ti ti-refresh${loadingData ? ' spin' : ''}`} /> PID별데이터 새로고침
                  </button>
                </>
              )}
            </div>
            )}

            {/* 본문 컨테이너 (시안대로 체크박스 열 완전 제거, 드래그/클릭으로 행 선택) */}
            <div className={`an-twrap ${activeTab === 'code' ? 'is-code-view' : ''}`}>
              {loadingData ? (
                <div className="an-empty">
                  <i className="ti ti-loader" style={{ fontSize: '24px', animation: 'spin 1s linear infinite' }} />
                  <div style={{ marginTop: '8px' }}>데이터를 불러오는 중입니다...</div>
                </div>
              ) : activeTab === 'resp' ? (
                /* 1. 응답 데이터 탭: 시안의 정확한 컬럼 순서 및 너비 반영 */
                <table className="an-tbl wide">
                  <colgroup>
                    {colVis.raw && <col style={colWidths.raw ? { width: `${colWidths.raw}px` } : undefined} />}
                    {colVis.clean && <col style={colWidths.clean ? { width: `${colWidths.clean}px` } : undefined} />}
                    {colVis.multi && <col style={colWidths.multi ? { width: `${colWidths.multi}px` } : undefined} />}
                    <col style={{ width: '48px' }} /> {/* 행 추가 액션 열 (소분류 바로 앞) */}
                    {colVis.l3 && <col style={colWidths.l3 ? { width: `${colWidths.l3}px` } : undefined} />}
                    {levelMode === '3' && colVis.l1 && <col style={colWidths.l1 ? { width: `${colWidths.l1}px` } : undefined} />}
                    {(levelMode === '3' || levelMode === '2') && colVis.l2 && <col style={colWidths.l2 ? { width: `${colWidths.l2}px` } : undefined} />}
                    {colVis.sent && <col style={colWidths.sent ? { width: `${colWidths.sent}px` } : undefined} />}
                    {colVis.ok && <col style={colWidths.ok ? { width: `${colWidths.ok}px` } : undefined} />}
                  </colgroup>
                  <thead>
                    <tr className="an-th-title-row">
                      {colVis.raw && (
                        <th>
                          <span className="an-th-txt">원본 응답</span>
                          {renderSortBtn('raw')}
                          <i
                            className={`ti ti-text-wrap wi${wrapCols.raw ? ' on' : ''}`}
                            title={wrapCols.raw ? '한 줄 보기로' : '줄바꿈(전체 펼치기)'}
                            onClick={() => setWrapCols(v => ({ ...v, raw: !v.raw }))}
                          />
                          <span
                            className="rz"
                            data-k="raw"
                            title="드래그하여 폭 조정 (더블클릭 초기화)"
                            onMouseDown={(e) => handleColResizeMouseDown('raw', e)}
                            onDoubleClick={() => handleColResizeDblClick('raw')}
                          />
                        </th>
                      )}
                      {colVis.clean && (
                        <th>
                          <span className="an-th-txt">클리닝 응답</span>
                          {renderSortBtn('clean')}
                          <i
                            className={`ti ti-text-wrap wi${wrapCols.clean ? ' on' : ''}`}
                            title={wrapCols.clean ? '한 줄 보기로' : '줄바꿈(전체 펼치기)'}
                            onClick={() => setWrapCols(v => ({ ...v, clean: !v.clean }))}
                          />
                          <span
                            className="rz"
                            data-k="clean"
                            title="드래그하여 폭 조정 (더블클릭 초기화)"
                            onMouseDown={(e) => handleColResizeMouseDown('clean', e)}
                            onDoubleClick={() => handleColResizeDblClick('clean')}
                          />
                        </th>
                      )}
                      {colVis.multi && (
                        <th className="c">
                          <span className="an-th-txt">멀티</span>
                          {renderSortBtn('multi')}
                          <span
                            className="rz"
                            data-k="multi"
                            title="드래그하여 폭 조정"
                            onMouseDown={(e) => handleColResizeMouseDown('multi', e)}
                            onDoubleClick={() => handleColResizeDblClick('multi')}
                          />
                        </th>
                      )}

                      {/* ★ 추가 액션 열 (소분류 바로 앞) */}
                      <th style={{ width: '48px' }} className="c" title="멀티 행 추가 (단축키: Shift+Enter)">
                        추가
                      </th>

                      {/* ★ 소분류 (고속 펀칭 열) */}
                      {colVis.l3 && (
                        <th>
                          <span className="an-th-txt">소분류</span>
                          {renderSortBtn('l3')}
                          <span className="an-punch-th-hint" title="키보드 고속 펀칭 지원: 소코드 입력 후 [Enter] 다음 응답 | [Shift+Enter] 멀티 추가 | [Del] 삭제">
                            <i className="ti ti-keyboard" />
                          </span>
                          <span
                            className="rz"
                            data-k="l3"
                            title="드래그하여 폭 조정"
                            onMouseDown={(e) => handleColResizeMouseDown('l3', e)}
                            onDoubleClick={() => handleColResizeDblClick('l3')}
                          />
                        </th>
                      )}

                      {/* ★ 대분류 (소분류 뒤, 톤다운) */}
                      {levelMode === '3' && colVis.l1 && (
                        <th>
                          <span className="an-th-txt">대분류</span>
                          {renderSortBtn('l1')}
                          <span
                            className="rz"
                            data-k="l1"
                            title="드래그하여 폭 조정"
                            onMouseDown={(e) => handleColResizeMouseDown('l1', e)}
                            onDoubleClick={() => handleColResizeDblClick('l1')}
                          />
                        </th>
                      )}

                      {/* ★ 중분류 (소분류 뒤, 톤다운) */}
                      {(levelMode === '3' || levelMode === '2') && colVis.l2 && (
                        <th>
                          <span className="an-th-txt">중분류</span>
                          {renderSortBtn('l2')}
                          <span
                            className="rz"
                            data-k="l2"
                            title="드래그하여 폭 조정"
                            onMouseDown={(e) => handleColResizeMouseDown('l2', e)}
                            onDoubleClick={() => handleColResizeDblClick('l2')}
                          />
                        </th>
                      )}

                      {colVis.sent && (
                        <th className="c">
                          <span className="an-th-txt">감성</span>
                          {renderSortBtn('sent')}
                          <span
                            className="rz"
                            data-k="sent"
                            title="드래그하여 폭 조정"
                            onMouseDown={(e) => handleColResizeMouseDown('sent', e)}
                            onDoubleClick={() => handleColResizeDblClick('sent')}
                          />
                        </th>
                      )}
                      {colVis.ok && (
                        <th className="c">
                          <span className="an-th-txt">확인</span>
                          {renderSortBtn('ok')}
                          <span className="an-okall">
                            <b className="y" title="일괄 확인 처리" onClick={() => handleBulkCheck(true)}><i className="ti ti-check" /></b>
                            <b className="n" title="일괄 확인 해제" onClick={() => handleBulkCheck(false)}><i className="ti ti-x" /></b>
                          </span>
                          <span
                            className="rz"
                            data-k="ok"
                            title="드래그하여 폭 조정"
                            onMouseDown={(e) => handleColResizeMouseDown('ok', e)}
                            onDoubleClick={() => handleColResizeDblClick('ok')}
                          />
                        </th>
                      )}
                    </tr>

                    {/* ★ 2행: 열별 검색 필터 행 (AND 조건 결합) */}
                    <tr className="an-th-filter-row">
                      {colVis.raw && (
                        <th className="an-th-filter-th">
                          <div className="an-col-filter-box">
                            <input
                              className="an-col-filter-in"
                              placeholder="원본 검색"
                              value={colFilters.raw}
                              onChange={(e) => setColFilters(prev => ({ ...prev, raw: e.target.value }))}
                            />
                            {colFilters.raw && (
                              <button
                                type="button"
                                className="an-col-filter-clear"
                                onClick={() => setColFilters(prev => ({ ...prev, raw: '' }))}
                                title="입력 지우기"
                              >
                                <i className="ti ti-x" />
                              </button>
                            )}
                          </div>
                        </th>
                      )}
                      {colVis.clean && (
                        <th className="an-th-filter-th">
                          <div className="an-col-filter-box">
                            <input
                              className="an-col-filter-in"
                              placeholder="클리닝 검색"
                              value={colFilters.clean}
                              onChange={(e) => setColFilters(prev => ({ ...prev, clean: e.target.value }))}
                            />
                            {colFilters.clean && (
                              <button
                                type="button"
                                className="an-col-filter-clear"
                                onClick={() => setColFilters(prev => ({ ...prev, clean: '' }))}
                                title="입력 지우기"
                              >
                                <i className="ti ti-x" />
                              </button>
                            )}
                          </div>
                        </th>
                      )}
                      {colVis.multi && (
                        <th className="an-th-filter-th c">
                          <div className="an-col-filter-box">
                            <input
                              className="an-col-filter-in c"
                              placeholder="멀티"
                              value={colFilters.multi}
                              onChange={(e) => setColFilters(prev => ({ ...prev, multi: e.target.value }))}
                            />
                            {colFilters.multi && (
                              <button
                                type="button"
                                className="an-col-filter-clear"
                                onClick={() => setColFilters(prev => ({ ...prev, multi: '' }))}
                                title="입력 지우기"
                              >
                                <i className="ti ti-x" />
                              </button>
                            )}
                          </div>
                        </th>
                      )}

                      {/* 추가 열 위치: 필터 전체 초기화 버튼 */}
                      <th className="an-th-filter-th c">
                        <button
                          type="button"
                          className="an-col-filter-reset"
                          title="열별 검색 조건 전체 초기화"
                          onClick={handleResetColFilters}
                        >
                          <i className="ti ti-rotate-2" />
                        </button>
                      </th>

                      {colVis.l3 && (
                        <th className="an-th-filter-th">
                          <div className="an-col-filter-box">
                            <input
                              className="an-col-filter-in"
                              placeholder="소분류/코드"
                              value={colFilters.l3}
                              onChange={(e) => setColFilters(prev => ({ ...prev, l3: e.target.value }))}
                            />
                            {colFilters.l3 && (
                              <button
                                type="button"
                                className="an-col-filter-clear"
                                onClick={() => setColFilters(prev => ({ ...prev, l3: '' }))}
                                title="입력 지우기"
                              >
                                <i className="ti ti-x" />
                              </button>
                            )}
                          </div>
                        </th>
                      )}

                      {levelMode === '3' && colVis.l1 && (
                        <th className="an-th-filter-th">
                          <div className="an-col-filter-box">
                            <input
                              className="an-col-filter-in"
                              placeholder="대분류"
                              value={colFilters.l1}
                              onChange={(e) => setColFilters(prev => ({ ...prev, l1: e.target.value }))}
                            />
                            {colFilters.l1 && (
                              <button
                                type="button"
                                className="an-col-filter-clear"
                                onClick={() => setColFilters(prev => ({ ...prev, l1: '' }))}
                                title="입력 지우기"
                              >
                                <i className="ti ti-x" />
                              </button>
                            )}
                          </div>
                        </th>
                      )}

                      {(levelMode === '3' || levelMode === '2') && colVis.l2 && (
                        <th className="an-th-filter-th">
                          <div className="an-col-filter-box">
                            <input
                              className="an-col-filter-in"
                              placeholder="중분류"
                              value={colFilters.l2}
                              onChange={(e) => setColFilters(prev => ({ ...prev, l2: e.target.value }))}
                            />
                            {colFilters.l2 && (
                              <button
                                type="button"
                                className="an-col-filter-clear"
                                onClick={() => setColFilters(prev => ({ ...prev, l2: '' }))}
                                title="입력 지우기"
                              >
                                <i className="ti ti-x" />
                              </button>
                            )}
                          </div>
                        </th>
                      )}

                      {colVis.sent && (
                        <th className="an-th-filter-th c">
                          <select
                            className="an-col-filter-sl"
                            value={colFilters.sent}
                            onChange={(e) => setColFilters(prev => ({ ...prev, sent: e.target.value }))}
                            title="감성 필터"
                          >
                            <option value="all">전체</option>
                            <option value="긍정">긍정</option>
                            <option value="중립">중립</option>
                            <option value="부정">부정</option>
                          </select>
                        </th>
                      )}

                      {colVis.ok && (
                        <th className="an-th-filter-th c">
                          <select
                            className="an-col-filter-sl"
                            value={colFilters.ok}
                            onChange={(e) => setColFilters(prev => ({ ...prev, ok: e.target.value }))}
                            title="확인 여부 필터"
                          >
                            <option value="all">전체</option>
                            <option value="Y">확인</option>
                            <option value="N">미확인</option>
                          </select>
                        </th>
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {filteredResponses.slice(0, 500).map((row, idx) => {
                      const rowKey = getRowKey(row, idx);
                      const isSelected = selectedRowKeys.has(rowKey);
                      const isOk = row.recheckyn === 'Y' || row.recheckyn === '1';
                      const isMulti = Number(row.cid || 1) > 1;
                      const isWrapRaw = wrapCols.raw || expandedRowIds.has(rowKey);
                      const isWrapClean = wrapCols.clean || expandedRowIds.has(rowKey);

                      return (
                        <tr
                          key={rowKey}
                          className={`${isSelected ? 'sel' : ''} ${focusedRowIdx === idx ? 'cur' : ''} ${row._edited ? 'ed' : ''} ${isMulti ? 'm2' : ''}`}
                          onMouseDown={(e) => { setFocusedRowIdx(idx); handleRowMouseDown(idx, e); }}
                          onMouseEnter={() => handleRowMouseEnter(idx)}
                          onKeyDown={(e) => {
                            if (e.key === 'Delete') {
                              e.preventDefault();
                              if (isMulti) {
                                if (window.confirm(`선택한 멀티 응답 행(cid: ${row.cid})을 삭제하시겠습니까?`)) {
                                  setResponses(prev => prev.filter((r, i) => getRowKey(r, i) !== rowKey));
                                  showToast('멀티 행을 삭제했습니다.');
                                }
                              } else {
                                if (window.confirm('현재 응답의 소분류 분류 코드를 삭제(미분류로 초기화)하시겠습니까?')) {
                                  handlePunchCodeChange(row, idx, '');
                                  showToast('소분류 분류를 삭제(초기화)했습니다.');
                                }
                              }
                            } else if (e.shiftKey && e.key === 'Enter') {
                              e.preventDefault();
                              handleAddMultiRow(row, idx);
                              const newIdx = idx + 1;
                              setFocusedRowIdx(newIdx);
                              setTimeout(() => {
                                const el = punchInputRefs.current[newIdx];
                                if (el) {
                                  el.focus();
                                  el.select();
                                  el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
                                }
                              }, 60);
                            }
                          }}
                          tabIndex={0}
                          style={{ cursor: 'pointer' }}
                        >
                          {/* 1. 원본 응답 */}
                          {colVis.raw && (
                            <td>
                              {isMulti ? (
                                <span className="an-dim" style={{ fontSize: '11px' }}>〃</span>
                              ) : (
                                <div className="an-rawcell">
                                  <span
                                    className={`${isWrapRaw ? 'wrapx' : 'ellip'} an-dim`}
                                    title={isWrapRaw ? '' : (row.answer_origin || '')}
                                  >
                                    {row.answer_origin || '-'}
                                  </span>
                                  {!wrapCols.raw && (
                                    <button
                                      className={`exb${expandedRowIds.has(rowKey) ? ' on' : ''}`}
                                      title="이 행만 펼치기/접기"
                                      onClick={(e) => handleToggleRowExpand(rowKey, e)}
                                    >
                                      <i className={`ti ti-chevron-${expandedRowIds.has(rowKey) ? 'up' : 'down'}`} />
                                    </button>
                                  )}
                                </div>
                              )}
                            </td>
                          )}

                          {/* 2. 클리닝 응답 */}
                          {colVis.clean && (
                            <td>
                              <div className="an-txcell">
                                <span
                                  className={isWrapClean ? 'wrapx' : 'ellip'}
                                  title={isWrapClean ? '' : (row.answer || row.answer_origin || '')}
                                >
                                  {row.answer || row.answer_origin || '-'}
                                </span>
                                {row._edited && <span className="an-edb">수정</span>}
                              </div>
                            </td>
                          )}

                          {/* 3. 멀티 */}
                          {colVis.multi && (
                            <td className="c">
                              {isMulti ? <b>{row.cid}</b> : <span className="an-dim">1</span>}
                            </td>
                          )}

                          {/* ★ 4. 멀티 행 추가/삭제 액션 (소분류 바로 앞) */}
                          <td className="c" onClick={(e) => e.stopPropagation()}>
                            <div className="an-rowact" style={{ justifyContent: 'center' }}>
                              <button
                                className="an-ib add"
                                title="멀티 행 추가 (단축키: Shift+Enter)"
                                onClick={(e) => handleAddMultiRow(row, idx, e)}
                              >
                                <i className="ti ti-plus" />
                              </button>
                              {isMulti && (
                                <button
                                  className="an-ib del"
                                  title="이 멀티 행 삭제 (단축키: Delete)"
                                  onClick={(e) => handleDeleteMultiRow(rowKey, e)}
                                >
                                  <i className="ti ti-minus" />
                                </button>
                              )}
                            </div>
                          </td>

                          {/* ★ 5. 소분류 (고속 코드 펀칭 인풋 + 매핑 라벨) */}
                          {colVis.l3 && (
                            <td onClick={(e) => e.stopPropagation()}>
                              <div className="an-punch-cell">
                                <input
                                  ref={el => { if (el) punchInputRefs.current[idx] = el; }}
                                  className="an-punch-in"
                                  value={row.lv123code || ''}
                                  placeholder="코드"
                                  onChange={(e) => handlePunchCodeChange(row, idx, e.target.value)}
                                  onKeyDown={(e) => handlePunchKeyDown(row, idx, e)}
                                  onFocus={() => setFocusedRowIdx(idx)}
                                  title="소코드 입력 후 [Enter]: 다음 응답 | [Shift+Enter]: 멀티행 추가 | [Del]: 행 삭제"
                                />
                                {row.lv3 ? (
                                  <span
                                    className="an-punch-label"
                                    title={`${row.lv1 || ''} > ${row.lv2 || ''} > ${row.lv3} (클릭하여 팝오버 열기)`}
                                    onClick={(e) => handleOpenCategoryPop(row, idx, e)}
                                  >
                                    {row.lv3}
                                  </span>
                                ) : (
                                  <span
                                    className="an-punch-none"
                                    onClick={(e) => handleOpenCategoryPop(row, idx, e)}
                                    title="클릭하여 목록에서 선택"
                                  >
                                    미분류 · 클릭하여 지정
                                  </span>
                                )}
                              </div>
                            </td>
                          )}

                          {/* ★ 6. 대분류 (소분류 뒤, 톤다운) */}
                          {levelMode === '3' && colVis.l1 && (
                            <td>
                              {row.lv1 ? (
                                <span className="an-lchip a" title={row.lv1}>{row.lv1}</span>
                              ) : (
                                <span className="an-dim">—</span>
                              )}
                            </td>
                          )}

                          {/* ★ 7. 중분류 (소분류 뒤, 톤다운) */}
                          {(levelMode === '3' || levelMode === '2') && colVis.l2 && (
                            <td>
                              {row.lv2 ? (
                                <span className="an-lchip b" title={row.lv2}>{row.lv2}</span>
                              ) : (
                                <span className="an-dim">—</span>
                              )}
                            </td>
                          )}

                          {/* 8. 감성 */}
                          {colVis.sent && (
                            <td className="c" onClick={(e) => e.stopPropagation()}>
                              <span
                                className={`an-sent ${row.sentiment === 'pos' ? 'pos' : row.sentiment === 'neg' ? 'neg' : 'neu'}`}
                                title="클릭하여 감성 선택 (다중 선택 시 일괄 반영)"
                                onClick={(e) => handleOpenSentimentPop(rowKey, e)}
                              >
                                {row.sentiment === 'pos' ? '긍정' : row.sentiment === 'neg' ? '부정' : '중립'}
                              </span>
                            </td>
                          )}

                          {/* 9. 확인 */}
                          {colVis.ok && (
                            <td className="c" onClick={(e) => e.stopPropagation()}>
                              <span
                                className={`an-okbtn${isOk ? ' on' : ''}`}
                                title={selectedRowKeys.size > 1 && selectedRowKeys.has(rowKey) ? `선택된 ${selectedRowKeys.size}건 일괄 ${isOk ? '확인 해제' : '확인 처리'}` : "클릭하여 확인 토글"}
                                onClick={(e) => handleToggleCheck(rowKey, e)}
                              >
                                <i className="ti ti-check" />
                              </span>
                            </td>
                          )}
                        </tr>
                      );
                    })}
                    {filteredResponses.length === 0 && (
                      <tr>
                        <td colSpan="9" className="an-empty">조건에 맞는 응답 데이터가 없습니다.</td>
                      </tr>
                    )}
                  </tbody>
                </table>
              ) : activeTab === 'code' ? (
                /* 2. 보기 데이터 탭: 스마트 스프레드시트 그리드 뷰 단일 모드 [요구사항 6] */
                <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0, height: '100%', overflow: 'hidden' }}>
                    {/* 데이터리스트 자동완성 옵션 */}
                    <datalist id="grid-l1-list">
                      {uniqueL1List.map(name => <option key={name} value={name} />)}
                    </datalist>
                    <datalist id="grid-l2-list">
                      {uniqueL2List.map(name => <option key={name} value={name} />)}
                    </datalist>

                    {/* 스마트 그리드 상단 툴바: [총 n개 보기] -> [검색] -> [빈도순/소코드순/가나다순] -> [+ 새 보기] */}
                    <div className="an-grid-toolbar" style={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 12px', background: 'var(--bg2, #f8fafc)', borderBottom: '1px solid var(--br, #e2e8f0)', flexWrap: 'wrap' }}>
                      {/* 1. [신규 배치] 총 n개 보기 뱃지 (검색란 앞) */}
                      <div className="an-grid-total-badge" title={`전체 ${codes.length}개 보기${gridSearch ? ` (필터링: ${filteredGridCodes.length}개)` : ''}`}>
                        <span>총</span>
                        <span className="an-total-num">{codes.length}</span>
                        <span>개 보기</span>
                        {gridSearch && (
                          <span style={{ color: '#2563eb', fontWeight: 600, marginLeft: '3px' }}>
                            ({filteredGridCodes.length}개)
                          </span>
                        )}
                      </div>

                      {/* 2. 검색 입력란 */}
                      <div className="an-search" style={{ width: '240px', margin: 0 }}>
                        <i className="ti ti-search" />
                        <input
                          placeholder="대·중·소분류명 또는 코드 검색 (단축키: /)"
                          value={gridSearch}
                          onChange={e => setGridSearch(e.target.value)}
                        />
                        {gridSearch && (
                          <button className="an-btn gh xs" onClick={() => setGridSearch('')} style={{ padding: '0 4px' }}>
                            <i className="ti ti-x" />
                          </button>
                        )}
                      </div>

                      {/* 3. 빠른 정렬 세그먼트 탭 버튼 */}
                      <div className="an-cb-sort-tabs" style={{ margin: 0 }}>
                        <button
                          type="button"
                          className={`an-cb-sort-tab ${gridSort === 'freq' ? 'active' : ''}`}
                          onClick={() => setGridSort('freq')}
                          title="응답 빈도수 내림차순 정렬"
                        >
                          ⚡ 빈도순
                        </button>
                        {levelMode === '3' && (
                          <button
                            type="button"
                            className={`an-cb-sort-tab ${gridSort === 'l1' ? 'active' : ''}`}
                            onClick={() => setGridSort('l1')}
                            title="대분류 코드/명칭 오름차순 정렬"
                          >
                            📁 대분류순
                          </button>
                        )}
                        <button
                          type="button"
                          className={`an-cb-sort-tab ${gridSort === 'code' ? 'active' : ''}`}
                          onClick={() => setGridSort('code')}
                          title="소분류 고유코드 오름차순 정렬"
                        >
                          🔢 소코드순
                        </button>
                        <button
                          type="button"
                          className={`an-cb-sort-tab ${gridSort === 'name' ? 'active' : ''}`}
                          onClick={() => setGridSort('name')}
                          title="소분류명 가나다순 정렬"
                        >
                          🔤 가나다순
                        </button>
                      </div>

                      {/* 4. + 새 보기 버튼 */}
                      <button
                        type="button"
                        className="an-cb-add-btn"
                        onClick={handleCbAddNewRow}
                        title="최상단에 새 보기 행 추가"
                      >
                        <i className="ti ti-plus" />새 보기
                      </button>

                      <div className="an-sp" />

                      <span style={{ fontSize: '11px', color: 'var(--tx3, #94a3b8)' }}>
                        드래그 · <b>Shift</b> · <b>Ctrl</b>+클릭 다중 선택
                      </span>
                    </div>

                    {/* 스마트 그리드 테이블 본문 (마우스 드래그 및 자동 스크롤 연동) */}
                    <div
                      ref={gridTableWrapRef}
                      onMouseMove={handleGridMouseMove}
                      style={{ flex: 1, minHeight: 0, overflow: 'auto' }}
                    >
                      <table className="an-tbl wide an-smart-grid">
                        <thead>
                          <tr>
                            <th style={{ width: '48px' }} className="c th-chk">
                              <div className="an-quick-select-wrap" style={{ position: 'relative', display: 'inline-flex', alignItems: 'center' }}>
                                <input
                                  type="checkbox"
                                  checked={filteredGridCodes.length > 0 && filteredGridCodes.every(c => gridSelectedCodes.has(String(c.lv123code || c.code_num)))}
                                  onChange={e => {
                                    if (e.target.checked) {
                                      setGridSelectedCodes(new Set(filteredGridCodes.map(c => String(c.lv123code || c.code_num))));
                                    } else {
                                      setGridSelectedCodes(new Set());
                                    }
                                  }}
                                  title="전체 선택 / 해제"
                                />
                                <button
                                  type="button"
                                  className="an-quick-select-btn"
                                  onClick={(e) => { e.stopPropagation(); setShowQuickSelectPop(v => !v); }}
                                  title="빠른 조건 선택 도우미"
                                >
                                  <i className="ti ti-chevron-down" />
                                </button>

                                {/* 빠른 선택 도우미 팝오버 */}
                                {showQuickSelectPop && (
                                  <div className="an-quick-select-pop" onClick={e => e.stopPropagation()}>
                                    <div className="an-quick-select-header">빠른 일괄 선택 도우미</div>
                                    <div className="an-quick-select-item" onClick={handleQuickSelectAll}>
                                      <i className="ti ti-check-all" /> 전체 선택
                                    </div>
                                    <div className="an-quick-select-item" onClick={handleQuickClearAll}>
                                      <i className="ti ti-x" /> 전체 선택 해제
                                    </div>
                                    <div className="an-quick-select-item" onClick={handleQuickInvert}>
                                      <i className="ti ti-arrows-diff" /> 선택 반전
                                    </div>

                                    <div className="an-quick-select-divider" />
                                    <div className="an-quick-select-header">보기구분별 선택</div>
                                    <div className="an-quick-select-item" onClick={() => handleQuickSelectBySrc('설문지')}>
                                      <span style={{ display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: '#8b5cf6', marginRight: '4px' }} />
                                      '설문 보기'만 선택
                                    </div>
                                    <div className="an-quick-select-item" onClick={() => handleQuickSelectBySrc('자동분석')}>
                                      <span style={{ display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: '#10b981', marginRight: '4px' }} />
                                      '자동분석 보기'만 선택
                                    </div>
                                    <div className="an-quick-select-item" onClick={() => handleQuickSelectBySrc('트래킹')}>
                                      <span style={{ display: 'inline-block', width: '8px', height: '8px', borderRadius: '50%', background: '#f97316', marginRight: '4px' }} />
                                      '트래킹 보기'만 선택
                                    </div>

                                    <div className="an-quick-select-divider" />
                                    <div className="an-quick-select-header">수정여부별 선택</div>
                                    <div className="an-quick-select-item" onClick={() => handleQuickSelectByLock(false)}>
                                      <i className="ti ti-lock-open" style={{ color: '#10b981' }} /> '수정가능'만 선택
                                    </div>
                                    <div className="an-quick-select-item" onClick={() => handleQuickSelectByLock(true)}>
                                      <i className="ti ti-lock" style={{ color: '#64748b' }} /> '수정불가'만 선택
                                    </div>

                                    <div className="an-quick-range-box">
                                      <span style={{ fontSize: '10.5px', color: '#64748b' }}>범위:</span>
                                      <input
                                        value={rangeStartVal}
                                        onChange={e => setRangeStartVal(e.target.value)}
                                        placeholder="시작"
                                      />
                                      <span style={{ fontSize: '10.5px', color: '#64748b' }}>~</span>
                                      <input
                                        value={rangeEndVal}
                                        onChange={e => setRangeEndVal(e.target.value)}
                                        placeholder="끝"
                                      />
                                      <button
                                        type="button"
                                        className="an-btn br xs"
                                        onClick={handleQuickSelectByRange}
                                      >
                                        선택
                                      </button>
                                    </div>
                                  </div>
                                )}
                              </div>
                            </th>
                            <th style={{ width: '42px' }} className="c">#</th>
                            <th style={{ width: '105px' }} className="c">보기구분</th>
                            <th style={{ width: '75px' }} className="c">수정여부</th>

                            {/* 3단계 대분류 */}
                            {levelMode === '3' && (
                              <>
                                <th style={{ width: '65px' }} className="c">대코드</th>
                                <th style={{ width: '140px' }}>대분류명 (자동완성)</th>
                              </>
                            )}

                            {/* 3단계 또는 2단계 중분류 */}
                            {(levelMode === '3' || levelMode === '2') && (
                              <>
                                <th style={{ width: '65px' }} className="c">중코드</th>
                                <th style={{ width: '150px' }}>중분류명 (자동완성)</th>
                              </>
                            )}

                            {/* 빈도 (소코드 앞, 구형 UI 일관 뱃지 적용) */}
                            <th style={{ width: '54px' }} className="c" title="응답 빈도수">빈도</th>

                            {/* 소분류 (공통) */}
                            <th style={{ width: '75px' }} className="c">소코드</th>
                            <th style={{ minWidth: '180px' }}>소분류명 (고속 편집)</th>
                            <th style={{ width: '45px' }} className="c">삭제</th>
                          </tr>
                        </thead>
                        <tbody>
                          {filteredGridCodes.map((code, idx) => {
                            const codeKey = String(code.lv123code || code.code_num || idx);
                            const realIdx = codes.findIndex(c => (c.lv123code || c.code_num) === (code.lv123code || code.code_num));
                            const isSelected = gridSelectedCodes.has(codeKey);
                            const isLocked = getCodeLock(code);
                            const src = getCodeSrc(code);
                            const srcClass = src === '설문지' ? 'src-survey' : src === '자동분석' ? 'src-auto' : 'src-track';

                            return (
                              <tr key={codeKey} className={`${isSelected ? 'msel' : ''} ${isLocked ? 'an-grid-row-locked' : ''}`}>
                                {/* 1. 선택 체크박스 (클릭, 드래그, Shift, Ctrl 지원) */}
                                <td
                                  className="c td-chk"
                                  onMouseDown={(e) => handleGridCheckboxMouseDown(idx, codeKey, e)}
                                  onMouseEnter={() => handleGridCheckboxMouseEnter(idx, codeKey)}
                                  title="클릭: 선택/해제 | Shift+클릭: 범위 선택 | 드래그: 연속 선택"
                                >
                                  <input
                                    type="checkbox"
                                    checked={isSelected}
                                    readOnly
                                    style={{ pointerEvents: 'none' }}
                                  />
                                </td>

                                {/* 2. 순번 */}
                                <td className="c an-dim" style={{ fontSize: '11px' }}>{idx + 1}</td>

                                {/* 3. [신설] 보기구분 셀 */}
                                <td className="c">
                                  <select
                                    className={`an-src-select ${srcClass}`}
                                    value={src}
                                    onChange={e => handleChangeGridSrc(realIdx >= 0 ? realIdx : idx, e.target.value)}
                                    title="보기구분 (설문 보기 / 자동분석 보기 / 트래킹 보기)"
                                  >
                                    <option value="설문지">설문 보기</option>
                                    <option value="자동분석">자동분석 보기</option>
                                    <option value="트래킹">트래킹 보기</option>
                                  </select>
                                </td>

                                {/* 4. [신설] 옆버튼 슬라이드 토글 스위치 (가능/불가) */}
                                <td className="c">
                                  <div
                                    className={`an-sw-toggle ${!isLocked ? 'is-on' : ''}`}
                                    onClick={() => handleToggleGridLock(realIdx >= 0 ? realIdx : idx)}
                                    title={!isLocked ? '현재 수정가능 (클릭하여 수정불가로 잠금)' : '현재 수정불가 (클릭하여 수정가능으로 변경)'}
                                  >
                                    <span className="an-sw-knob" />
                                    <span className="an-sw-label">{!isLocked ? '가능' : '불가'}</span>
                                  </div>
                                </td>

                                {/* 5. 3단계 대분류 코드 & 대분류명 */}
                                {levelMode === '3' && (
                                  <>
                                    <td>
                                      <input
                                        className="an-grid-input an-grid-code-input"
                                        value={code.lv1code || ''}
                                        disabled={isLocked}
                                        onChange={e => handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv1code', e.target.value)}
                                        onKeyDown={e => handleGridKeyDown(e, idx, 'lv1code')}
                                        data-grid-row={idx}
                                        data-grid-col="lv1code"
                                        placeholder="대코드"
                                        title="대분류 코드 (수정 시 동일 대분류 전체 행 자동 동기화)"
                                      />
                                    </td>
                                    <td>
                                      <div className="an-combo-wrap">
                                        <input
                                          className="an-grid-input"
                                          value={code.lv1 || ''}
                                          disabled={isLocked}
                                          onFocus={() => { if (!isLocked) setActiveCombo({ targetKey: `grid_${idx}`, field: 'lv1', realIdx: realIdx >= 0 ? realIdx : idx }); }}
                                          onClick={() => { if (!isLocked) setActiveCombo({ targetKey: `grid_${idx}`, field: 'lv1', realIdx: realIdx >= 0 ? realIdx : idx }); }}
                                          onChange={e => handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv1', e.target.value)}
                                          onKeyDown={e => handleGridKeyDown(e, idx, 'lv1')}
                                          data-grid-row={idx}
                                          data-grid-col="lv1"
                                          placeholder="대분류명 선택/입력"
                                          title="대분류명 (클릭하여 전체 목록 선택 또는 직접 입력)"
                                        />
                                        {!isLocked && (
                                          <span
                                            className="an-combo-arrow"
                                            onClick={(e) => {
                                              e.stopPropagation();
                                              setActiveCombo(prev => (prev?.targetKey === `grid_${idx}` && prev?.field === 'lv1' ? null : { targetKey: `grid_${idx}`, field: 'lv1', realIdx: realIdx >= 0 ? realIdx : idx }));
                                            }}
                                            title="목록 보기"
                                          >
                                            <i className="ti ti-chevron-down" style={{ fontSize: '11px' }} />
                                          </span>
                                        )}

                                        {!isLocked && activeCombo?.targetKey === `grid_${idx}` && activeCombo?.field === 'lv1' && (
                                          <div className="an-combo-dropdown">
                                            {l1Items.map((item) => (
                                              <div
                                                key={item.name}
                                                className={`an-combo-item${code.lv1 === item.name ? ' selected' : ''}`}
                                                onClick={() => {
                                                  handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv1', item.name);
                                                  if (item.code) {
                                                    handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv1code', item.code);
                                                  }
                                                  setActiveCombo(null);
                                                }}
                                              >
                                                <span>{code.lv1 === item.name ? '✓ ' : ''}{item.name}</span>
                                                {item.code && <span className="code-badge">{item.code}</span>}
                                              </div>
                                            ))}
                                            {l1Items.length === 0 && (
                                              <div className="an-combo-empty">등록된 대분류가 없습니다.</div>
                                            )}
                                          </div>
                                        )}
                                      </div>
                                    </td>
                                  </>
                                )}

                                {/* 6. 중분류 코드 & 중분류명 (3단계 또는 2단계) */}
                                {(levelMode === '3' || levelMode === '2') && (
                                  <>
                                    <td>
                                      <input
                                        className="an-grid-input an-grid-code-input"
                                        value={code.lv2code || ''}
                                        disabled={isLocked}
                                        onChange={e => handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv2code', e.target.value)}
                                        onKeyDown={e => handleGridKeyDown(e, idx, 'lv2code')}
                                        data-grid-row={idx}
                                        data-grid-col="lv2code"
                                        placeholder="중코드"
                                        title="중분류 코드 (수정 시 동일 중분류 전체 행 자동 동기화)"
                                      />
                                    </td>
                                    <td>
                                      {(() => {
                                        const curL2Options = getL2ItemsForL1(code.lv1);
                                        return (
                                          <div className="an-combo-wrap">
                                            <input
                                              className="an-grid-input"
                                              value={code.lv2 || ''}
                                              disabled={isLocked}
                                              onFocus={() => { if (!isLocked) setActiveCombo({ targetKey: `grid_${idx}`, field: 'lv2', realIdx: realIdx >= 0 ? realIdx : idx }); }}
                                              onClick={() => { if (!isLocked) setActiveCombo({ targetKey: `grid_${idx}`, field: 'lv2', realIdx: realIdx >= 0 ? realIdx : idx }); }}
                                              onChange={e => handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv2', e.target.value)}
                                              onKeyDown={e => handleGridKeyDown(e, idx, 'lv2')}
                                              data-grid-row={idx}
                                              data-grid-col="lv2"
                                              placeholder="중분류명 선택/입력"
                                              title={code.lv1 ? `[${code.lv1}] 소속 중분류 선택 또는 직접 입력` : '중분류명 선택 또는 직접 입력'}
                                            />
                                            {!isLocked && (
                                              <span
                                                className="an-combo-arrow"
                                                onClick={(e) => {
                                                  e.stopPropagation();
                                                  setActiveCombo(prev => (prev?.targetKey === `grid_${idx}` && prev?.field === 'lv2' ? null : { targetKey: `grid_${idx}`, field: 'lv2', realIdx: realIdx >= 0 ? realIdx : idx }));
                                                }}
                                                title="목록 보기"
                                              >
                                                <i className="ti ti-chevron-down" style={{ fontSize: '11px' }} />
                                              </span>
                                            )}

                                            {!isLocked && activeCombo?.targetKey === `grid_${idx}` && activeCombo?.field === 'lv2' && (
                                              <div className="an-combo-dropdown">
                                                {curL2Options.map((item) => (
                                                  <div
                                                    key={item.name}
                                                    className={`an-combo-item${code.lv2 === item.name ? ' selected' : ''}`}
                                                    onClick={() => {
                                                      handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv2', item.name);
                                                      if (item.code) {
                                                        handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv2code', item.code);
                                                      }
                                                      setActiveCombo(null);
                                                    }}
                                                  >
                                                    <span>{code.lv2 === item.name ? '✓ ' : ''}{item.name}</span>
                                                    {item.code && <span className="code-badge">{item.code}</span>}
                                                  </div>
                                                ))}
                                                {curL2Options.length === 0 && (
                                                  <div className="an-combo-empty">
                                                    {code.lv1 ? `[${code.lv1}]에 등록된 중분류가 없습니다.` : '등록된 중분류가 없습니다.'}
                                                  </div>
                                                )}
                                              </div>
                                            )}
                                          </div>
                                        );
                                      })()}
                                    </td>
                                  </>
                                )}

                                {/* 7. 빈도 뱃지 (응답 데이터 패널과 동일한 스타일) */}
                                <td className="c" title={`응답 빈도: ${code.ex_sum || 0}건`}>
                                  <span className="an-badge-freq">
                                    {Number(code.ex_sum || 0).toLocaleString()}
                                  </span>
                                </td>

                                {/* 8. 소분류 코드 & 소분류명 */}
                                <td>
                                  <input
                                    className="an-grid-input an-grid-code-input"
                                    value={code.lv123code || code.code_num || ''}
                                    disabled={isLocked}
                                    onChange={e => handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv123code', e.target.value)}
                                    onKeyDown={e => handleGridKeyDown(e, idx, 'lv123code')}
                                    data-grid-row={idx}
                                    data-grid-col="lv123code"
                                    placeholder="소코드"
                                    title="소분류 코드"
                                  />
                                </td>
                                <td>
                                  <input
                                    className="an-grid-input strong"
                                    value={code.lv3 || ''}
                                    disabled={isLocked}
                                    onChange={e => handleGridCellChangeWithPropagation(realIdx >= 0 ? realIdx : idx, 'lv3', e.target.value)}
                                    onKeyDown={e => handleGridKeyDown(e, idx, 'lv3')}
                                    data-grid-row={idx}
                                    data-grid-col="lv3"
                                    placeholder="소분류명 (Enter: 다음 행)"
                                  />
                                </td>

                                {/* 9. 삭제 */}
                                <td className="c">
                                  <button
                                    className="an-ib del"
                                    title={isLocked ? "수정불가 상태에서는 삭제할 수 없습니다. 수정을 원하시면 먼저 수정가능으로 전환하세요." : "이 보기 삭제"}
                                    disabled={isLocked}
                                    onClick={() => {
                                      if (isLocked) {
                                        showToast('수정불가 상태인 보기는 삭제할 수 없습니다. 먼저 수정가능으로 변경하세요.');
                                        return;
                                      }
                                      handleGridDeleteRow(realIdx >= 0 ? realIdx : idx);
                                    }}
                                    style={{
                                      padding: '2px 4px',
                                      color: isLocked ? '#94a3b8' : '#ef4444',
                                      cursor: isLocked ? 'not-allowed' : 'pointer',
                                      opacity: isLocked ? 0.45 : 1
                                    }}
                                  >
                                    <i className="ti ti-trash" />
                                  </button>
                                </td>
                              </tr>
                            );
                          })}
                          {filteredGridCodes.length === 0 && (
                            <tr>
                              <td colSpan={levelMode === '3' ? 12 : (levelMode === '2' ? 10 : 8)} className="an-empty" style={{ padding: '32px 0' }}>
                                {gridSearch ? '검색 결과와 일치하는 보기가 없습니다.' : '등록된 보기가 없습니다. [+ 행 추가] 버튼을 눌러 보기를 등록하세요.'}
                              </td>
                            </tr>
                          )}
                        </tbody>
                      </table>
                    </div>
                  </div>
              ) : (
                /* 3. PID별데이터 (rawdata) 탭 */
                <table className="an-tbl wide">
                  <colgroup>
                    <col style={{ width: '45px' }} />
                    <col style={{ width: '95px' }} />
                    <col style={{ width: '50px' }} />
                    <col style={{ minWidth: '180px' }} />
                    <col style={{ width: '110px' }} />
                    <col style={{ width: '120px' }} />
                    <col style={{ width: '65px' }} />
                    <col style={{ width: '75px' }} />
                    <col style={{ minWidth: '150px' }} />
                    <col style={{ width: '75px' }} />
                    <col style={{ width: '115px' }} />
                  </colgroup>
                  <thead>
                    {/* 1행: 컬럼 타이틀 & 3단 정렬 토글 */}
                    <tr className="an-th-title-row">
                      <th className="c">#</th>
                      <th>
                        <span className="an-th-txt">PID</span>
                        {renderRawSortBtn('pid')}
                      </th>
                      <th className="c">
                        <span className="an-th-txt">CID</span>
                        {renderRawSortBtn('cid')}
                      </th>
                      <th>
                        <span className="an-th-txt">최종 응답</span>
                        {renderRawSortBtn('answer')}
                      </th>
                      <th>
                        <span className="an-th-txt">대분류</span>
                        {renderRawSortBtn('l1')}
                      </th>
                      <th>
                        <span className="an-th-txt">중분류</span>
                        {renderRawSortBtn('l2')}
                      </th>
                      <th className="c">
                        <span className="an-th-txt">빈도</span>
                        {renderRawSortBtn('freq')}
                      </th>
                      <th className="c">
                        <span className="an-th-txt">소코드</span>
                        {renderRawSortBtn('code')}
                      </th>
                      <th>
                        <span className="an-th-txt">소분류</span>
                        {renderRawSortBtn('l3')}
                      </th>
                      <th className="c">
                        <span className="an-th-txt">감성</span>
                        {renderRawSortBtn('sent')}
                      </th>
                      <th className="c">
                        <span className="an-th-txt">작성일</span>
                        {renderRawSortBtn('date')}
                      </th>
                    </tr>

                    {/* 2행: 열별 검색 필터 행 (AND 조건 결합) */}
                    <tr className="an-th-filter-row">
                      <th className="an-th-filter-th c">
                        <button
                          type="button"
                          className="an-col-filter-reset"
                          title="열별 검색 조건 전체 초기화"
                          onClick={handleResetRawColFilters}
                        >
                          <i className="ti ti-rotate-2" />
                        </button>
                      </th>
                      <th className="an-th-filter-th">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in"
                            placeholder="PID 검색"
                            value={rawColFilters.pid}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, pid: e.target.value }))}
                          />
                          {rawColFilters.pid && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, pid: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th c">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in c"
                            placeholder="CID"
                            value={rawColFilters.cid}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, cid: e.target.value }))}
                          />
                          {rawColFilters.cid && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, cid: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in"
                            placeholder="응답 검색"
                            value={rawColFilters.answer}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, answer: e.target.value }))}
                          />
                          {rawColFilters.answer && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, answer: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in"
                            placeholder="대분류"
                            value={rawColFilters.l1}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, l1: e.target.value }))}
                          />
                          {rawColFilters.l1 && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, l1: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in"
                            placeholder="중분류"
                            value={rawColFilters.l2}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, l2: e.target.value }))}
                          />
                          {rawColFilters.l2 && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, l2: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th c">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in c"
                            placeholder="빈도"
                            value={rawColFilters.freq}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, freq: e.target.value }))}
                          />
                          {rawColFilters.freq && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, freq: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th c">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in c"
                            placeholder="소코드"
                            value={rawColFilters.code}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, code: e.target.value }))}
                          />
                          {rawColFilters.code && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, code: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in"
                            placeholder="소분류"
                            value={rawColFilters.l3}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, l3: e.target.value }))}
                          />
                          {rawColFilters.l3 && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, l3: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                      <th className="an-th-filter-th c">
                        <select
                          className="an-col-filter-sl"
                          value={rawColFilters.sent}
                          onChange={(e) => setRawColFilters(prev => ({ ...prev, sent: e.target.value }))}
                          title="감성 필터"
                        >
                          <option value="all">전체</option>
                          <option value="긍정">긍정</option>
                          <option value="중립">중립</option>
                          <option value="부정">부정</option>
                        </select>
                      </th>
                      <th className="an-th-filter-th">
                        <div className="an-col-filter-box">
                          <input
                            className="an-col-filter-in"
                            placeholder="작성일"
                            value={rawColFilters.date}
                            onChange={(e) => setRawColFilters(prev => ({ ...prev, date: e.target.value }))}
                          />
                          {rawColFilters.date && (
                            <button
                              type="button"
                              className="an-col-filter-clear"
                              onClick={() => setRawColFilters(prev => ({ ...prev, date: '' }))}
                              title="입력 지우기"
                            >
                              <i className="ti ti-x" />
                            </button>
                          )}
                        </div>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredRawdata.slice(0, 500).map((raw, idx) => {
                      const codeVal = String(raw.lv123code || raw.code_num || '').trim();
                      const freq = codeVal ? (rawCodeFreqMap.get(codeVal) || 0) : null;

                      return (
                        <tr key={`${raw.pid}-${raw.cid}-${idx}`}>
                          <td className="c an-dim">{idx + 1}</td>
                          <td style={{ fontFamily: 'ui-monospace, Consolas, monospace', fontSize: '11.5px' }}>{raw.pid}</td>
                          <td className="c">{raw.cid || '1'}</td>
                          <td>{raw.answerfin || raw.answer_origin || '-'}</td>
                          {/* 대분류 */}
                          <td style={{ color: 'var(--tx2)' }}>{raw.lv1 || '-'}</td>
                          {/* 중분류 */}
                          <td style={{ color: 'var(--tx2)' }}>{raw.lv2 || '-'}</td>
                          {/* 빈도: 보기 데이터 페이지와 동일한 다크 슬레이트 뱃지 */}
                          <td className="c">
                            {freq !== null ? (
                              <span className="an-badge-freq">{freq}</span>
                            ) : (
                              <span className="an-dim">-</span>
                            )}
                          </td>
                          {/* 소코드 */}
                          <td className="c">
                            {codeVal ? (
                              <span className="an-cchip"><span className="path">{codeVal}</span></span>
                            ) : '—'}
                          </td>
                          {/* 소분류 */}
                          <td style={{ fontWeight: 600 }}>{raw.lv3 || '-'}</td>
                          {/* 감성 */}
                          <td className="c">{raw.sentiment || '—'}</td>
                          {/* 작성일 */}
                          <td className="an-dim" style={{ fontSize: '11.5px' }}>{raw.firstWriteDate || '-'}</td>
                        </tr>
                      );
                    })}
                    {filteredRawdata.length === 0 && (
                      <tr>
                        <td colSpan={11} className="an-empty" style={{ padding: '32px 0' }}>
                          검색 조건과 일치하는 데이터가 없습니다.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              )}
            </div>
          </section>

          {/* 스플리터 드래그 핸들 (응답 데이터 탭에서만 노출, 보기 데이터/rawdata에서는 숨김) [요구사항 1] */}
          {activeTab === 'resp' && (
            <div
              className={`an-splitter${isDraggingSplitter ? ' dragging' : ''}`}
              title="드래그하여 패널 너비 조절 (더블클릭 시 기본값 복원)"
              onMouseDown={handleSplitterMouseDown}
              onDoubleClick={handleSplitterDblClick}
            />
          )}

          {/* 우측 분류 선택 · 편집 패널 (응답 데이터 탭에서만 노출, rawdata/보기데이터에서는 숨김) [요구사항 1] */}
          {activeTab === 'resp' && (
            <aside className="an-pane an-cb" style={{ width: `${cbWidth}px` }}>
              <div className="an-phd">
                <span className="t">
                  <i className="ti ti-table" />보기 선택 · 편집
                  <span className="an-cnt">{codes.length}</span>
                </span>
                <span className="an-hint"><span className="k">/</span> 로 검색 · 더블클릭/Enter로 매핑</span>
              </div>

              <div className="an-cbody" style={{ display: 'flex', flexDirection: 'column', height: 'calc(100% - 40px)' }}>
                {/* 1. 검색창 */}
                <div className="an-search an-bigsearch" style={{ margin: '8px 8px 6px' }}>
                  <i className="ti ti-search" />
                  <input
                    ref={cbSearchInputRef}
                    id="cbQ"
                    placeholder="대·중·소분류명 또는 코드 검색 (단축키: /)"
                    value={cbSearch}
                    onChange={(e) => setCbSearch(e.target.value)}
                    onKeyDown={handleCbSearchKeyDown}
                  />
                  {cbSearch && (
                    <button
                      className="an-btn gh xs"
                      style={{ padding: '0 4px', minWidth: 'auto' }}
                      onClick={() => setCbSearch('')}
                    >
                      <i className="ti ti-x" />
                    </button>
                  )}
                </div>

                {/* 2. 빠른 정렬 세그먼트 탭 & 새 보기 추가 바 */}
                <div className="an-cb-top-actions">
                  <div className="an-cb-sort-tabs">
                    <button
                      className={`an-cb-sort-tab ${cbSort === 'freq' ? 'active' : ''}`}
                      onClick={() => setCbSort('freq')}
                      title="응답 빈도수 내림차순 정렬"
                    >
                      ⚡ 빈도순
                    </button>
                    {levelMode === '3' && (
                      <button
                        className={`an-cb-sort-tab ${cbSort === 'l1' ? 'active' : ''}`}
                        onClick={() => setCbSort('l1')}
                        title="대분류 코드/명칭 오름차순 정렬"
                      >
                        📁 대분류순
                      </button>
                    )}
                    <button
                      className={`an-cb-sort-tab ${cbSort === 'code' ? 'active' : ''}`}
                      onClick={() => setCbSort('code')}
                      title="소분류 고유코드 오름차순 정렬"
                    >
                      🔢 소코드순
                    </button>
                    <button
                      className={`an-cb-sort-tab ${cbSort === 'name' ? 'active' : ''}`}
                      onClick={() => setCbSort('name')}
                      title="소분류명 가나다순 정렬"
                    >
                      🔤 가나다순
                    </button>
                  </div>

                  <button
                    className="an-cb-add-btn"
                    onClick={handleCbAddNewRow}
                    title="최상단에 새 보기 행 추가"
                  >
                    <i className="ti ti-plus" />새 보기
                  </button>
                </div>

                {/* datalist 자동완성 지원 */}
                <datalist id="cb-l1-list">
                  {uniqueL1List.map((item, i) => (
                    <option key={item || i} value={item} />
                  ))}
                </datalist>
                <datalist id="cb-l2-list">
                  {uniqueL2List.map((item, i) => (
                    <option key={item || i} value={item} />
                  ))}
                </datalist>

                {/* 3. 구형 UI 계승: 한 행 콤팩트 테이블 그리드 */}
                <div className="an-cb-grid-wrap">
                  <table className="an-compact-code-tbl">
                    <thead>
                      <tr>
                        <th style={{ width: '34px' }} className="c" title="선택된 좌측 응답에 이 보기 매핑">매핑</th>
                        <th style={{ width: '30px' }} className="c" title="선택된 응답의 이 보기 분류 해제">해제</th>
                        {levelMode === '3' && (
                          <>
                            <th style={{ width: '42px' }} className="c">대코드</th>
                            <th style={{ width: '92px' }}>대분류명</th>
                          </>
                        )}
                        {(levelMode === '3' || levelMode === '2') && (
                          <>
                            <th style={{ width: '42px' }} className="c">중코드</th>
                            <th style={{ width: '95px' }}>중분류명</th>
                          </>
                        )}
                        <th style={{ width: '42px' }} className="c" title="응답 빈도수">빈도</th>
                        <th style={{ width: '48px' }} className="c">소코드</th>
                        <th style={{ minWidth: '110px' }}>소분류명 (LABEL)</th>
                        <th style={{ width: '32px' }} className="c" title="보기 삭제">삭제</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sortedSideCodes.map((c, idx) => {
                        const realIdx = codes.findIndex(item => (item.lv123code || item.code_num) === (c.lv123code || c.code_num));
                        const targetIdx = realIdx >= 0 ? realIdx : idx;

                        return (
                          <tr
                            key={c.lv123code || idx}
                            onDoubleClick={() => handleAssignCategoryFromRow(c)}
                            title="더블클릭 또는 [←] 클릭 시 좌측 선택 응답에 즉시 매핑"
                          >
                            {/* [←] 매핑 버튼 */}
                            <td className="c" onClick={(e) => e.stopPropagation()}>
                              <button
                                className="an-btn-map-add"
                                onClick={() => handleAssignCategoryFromRow(c)}
                                title="선택된 좌측 응답에 이 보기 즉시 매핑 (단축: 행 더블클릭)"
                              >
                                ←
                              </button>
                            </td>

                            {/* [→] 해제 버튼 */}
                            <td className="c" onClick={(e) => e.stopPropagation()}>
                              <button
                                className="an-btn-map-del"
                                onClick={() => handleUnassignCategoryFromRow(c)}
                                title="선택된 응답의 이 보기 분류 해제"
                              >
                                →
                              </button>
                            </td>

                            {/* 대코드 & 대분류명 (3단계 모드) */}
                            {levelMode === '3' && (
                              <>
                                <td onClick={(e) => e.stopPropagation()}>
                                  <input
                                    className="an-cb-in code"
                                    value={c.lv1code || ''}
                                    placeholder="대코드"
                                    onChange={(e) => handleGridCellChangeWithPropagation(targetIdx, 'lv1code', e.target.value)}
                                    title="대분류 코드 (수정 시 동일 대분류 전체 자동 동기화)"
                                  />
                                </td>
                                <td onClick={(e) => e.stopPropagation()}>
                                  <div className="an-combo-wrap">
                                    <input
                                      className="an-cb-in"
                                      value={c.lv1 || ''}
                                      placeholder="대분류명"
                                      onFocus={() => setActiveCombo({ targetKey: `cb_${idx}`, field: 'lv1', realIdx: targetIdx })}
                                      onClick={() => setActiveCombo({ targetKey: `cb_${idx}`, field: 'lv1', realIdx: targetIdx })}
                                      onChange={(e) => handleGridCellChangeWithPropagation(targetIdx, 'lv1', e.target.value)}
                                      title="대분류명 (클릭하여 전체 목록 선택 또는 직접 입력)"
                                    />
                                    <span
                                      className="an-combo-arrow"
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        setActiveCombo(prev => (prev?.targetKey === `cb_${idx}` && prev?.field === 'lv1' ? null : { targetKey: `cb_${idx}`, field: 'lv1', realIdx: targetIdx }));
                                      }}
                                      title="목록 보기"
                                    >
                                      <i className="ti ti-chevron-down" style={{ fontSize: '11px' }} />
                                    </span>

                                    {activeCombo?.targetKey === `cb_${idx}` && activeCombo?.field === 'lv1' && (
                                      <div className="an-combo-dropdown">
                                        {l1Items.map((item) => (
                                          <div
                                            key={item.name}
                                            className={`an-combo-item${c.lv1 === item.name ? ' selected' : ''}`}
                                            onClick={() => {
                                              handleGridCellChangeWithPropagation(targetIdx, 'lv1', item.name);
                                              if (item.code) {
                                                handleGridCellChangeWithPropagation(targetIdx, 'lv1code', item.code);
                                              }
                                              setActiveCombo(null);
                                            }}
                                          >
                                            <span>{c.lv1 === item.name ? '✓ ' : ''}{item.name}</span>
                                            {item.code && <span className="code-badge">{item.code}</span>}
                                          </div>
                                        ))}
                                        {l1Items.length === 0 && (
                                          <div className="an-combo-empty">등록된 대분류가 없습니다.</div>
                                        )}
                                      </div>
                                    )}
                                  </div>
                                </td>
                              </>
                            )}

                            {/* 중코드 & 중분류명 (3단계 또는 2단계 모드) */}
                            {(levelMode === '3' || levelMode === '2') && (
                              <>
                                <td onClick={(e) => e.stopPropagation()}>
                                  <input
                                    className="an-cb-in code"
                                    value={c.lv2code || ''}
                                    placeholder="중코드"
                                    onChange={(e) => handleGridCellChangeWithPropagation(targetIdx, 'lv2code', e.target.value)}
                                    title="중분류 코드 (수정 시 동일 중분류 전체 자동 동기화)"
                                  />
                                </td>
                                <td onClick={(e) => e.stopPropagation()}>
                                  {(() => {
                                    const curL2Options = getL2ItemsForL1(c.lv1);
                                    return (
                                      <div className="an-combo-wrap">
                                        <input
                                          className="an-cb-in"
                                          value={c.lv2 || ''}
                                          placeholder="중분류명"
                                          onFocus={() => setActiveCombo({ targetKey: `cb_${idx}`, field: 'lv2', realIdx: targetIdx })}
                                          onClick={() => setActiveCombo({ targetKey: `cb_${idx}`, field: 'lv2', realIdx: targetIdx })}
                                          onChange={(e) => handleGridCellChangeWithPropagation(targetIdx, 'lv2', e.target.value)}
                                          title={c.lv1 ? `[${c.lv1}] 소속 중분류 선택 또는 직접 입력` : '중분류명 선택 또는 직접 입력'}
                                        />
                                        <span
                                          className="an-combo-arrow"
                                          onClick={(e) => {
                                            e.stopPropagation();
                                            setActiveCombo(prev => (prev?.targetKey === `cb_${idx}` && prev?.field === 'lv2' ? null : { targetKey: `cb_${idx}`, field: 'lv2', realIdx: targetIdx }));
                                          }}
                                          title="목록 보기"
                                        >
                                          <i className="ti ti-chevron-down" style={{ fontSize: '11px' }} />
                                        </span>

                                        {activeCombo?.targetKey === `cb_${idx}` && activeCombo?.field === 'lv2' && (
                                          <div className="an-combo-dropdown">
                                            {curL2Options.map((item) => (
                                              <div
                                                key={item.name}
                                                className={`an-combo-item${c.lv2 === item.name ? ' selected' : ''}`}
                                                onClick={() => {
                                                  handleGridCellChangeWithPropagation(targetIdx, 'lv2', item.name);
                                                  if (item.code) {
                                                    handleGridCellChangeWithPropagation(targetIdx, 'lv2code', item.code);
                                                  }
                                                  setActiveCombo(null);
                                                }}
                                              >
                                                <span>{c.lv2 === item.name ? '✓ ' : ''}{item.name}</span>
                                                {item.code && <span className="code-badge">{item.code}</span>}
                                              </div>
                                            ))}
                                            {curL2Options.length === 0 && (
                                              <div className="an-combo-empty">
                                                {c.lv1 ? `[${c.lv1}]에 등록된 중분류가 없습니다.` : '등록된 중분류가 없습니다.'}
                                              </div>
                                            )}
                                          </div>
                                        )}
                                      </div>
                                    );
                                  })()}
                                </td>
                              </>
                            )}

                            {/* 빈도 뱃지 (구형 UI 음영 뱃지) */}
                            <td className="c" title={`응답 빈도: ${c.ex_sum || 0}건`}>
                              <span className="an-badge-freq">
                                {Number(c.ex_sum || 0).toLocaleString()}
                              </span>
                            </td>

                            {/* 소코드 */}
                            <td onClick={(e) => e.stopPropagation()}>
                              <input
                                className="an-cb-in code"
                                value={c.lv123code || c.code_num || ''}
                                placeholder="소코드"
                                onChange={(e) => handleGridCellChangeWithPropagation(targetIdx, 'lv123code', e.target.value)}
                                title="소분류 코드"
                              />
                            </td>

                            {/* 소분류명 (LABEL) */}
                            <td onClick={(e) => e.stopPropagation()}>
                              <input
                                className="an-cb-in strong"
                                value={c.lv3 || ''}
                                placeholder="소분류명"
                                onChange={(e) => handleGridCellChangeWithPropagation(targetIdx, 'lv3', e.target.value)}
                                title="소분류명 (더블클릭 또는 [←]로 매핑)"
                              />
                            </td>

                            {/* [🗑 삭제] 버튼 */}
                            <td className="c" onClick={(e) => e.stopPropagation()}>
                              <button
                                className="an-btn-compact-del"
                                onClick={() => handleDeleteCompactCodeRow(c, targetIdx)}
                                title={`[${c.lv3 || c.lv123code}] 보기 삭제`}
                              >
                                <i className="ti ti-trash" />
                              </button>
                            </td>
                          </tr>
                        );
                      })}

                      {sortedSideCodes.length === 0 && (
                        <tr>
                          <td
                            colSpan={levelMode === '3' ? 10 : (levelMode === '2' ? 8 : 6)}
                            className="an-empty"
                            style={{ padding: '36px 0' }}
                          >
                            {cbSearch ? '검색된 보기가 없습니다.' : '등록된 보기가 없습니다. [+ 새 보기] 버튼으로 추가하세요.'}
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>

                {/* 4. 하단 상태 바 */}
                <div className="an-cbfoot">
                  {selectedRowKeys.size > 0 ? (
                    <b style={{ color: 'var(--brand)' }}>
                      좌측 {selectedRowKeys.size}개 행 선택됨: <b>[←]</b> 버튼 또는 행 더블클릭 시 즉시 매핑됩니다.
                    </b>
                  ) : (
                    <span>좌측 행을 선택(체크) 후 <b>[←]</b> 버튼 또는 더블클릭 시 즉시 매핑됩니다.</span>
                  )}
                </div>
              </div>
            </aside>
          )}
        </div>
      </main>

      {/* 저장 : DB 반영 비교 모달 */}
      <div className={`ov2${showSaveModal ? ' on' : ''}`} onClick={() => setShowSaveModal(false)} />
      <div className={`svm${showSaveModal ? ' on' : ''}`} id="mSave">
        <div className="svhd">
          <i className="ti ti-device-floppy" />
          <div>
            <b>저장 — DB 반영 비교</b>
            <div className="svsub">{currentItem.qnum_text} · {currentItem.question_title}</div>
          </div>
          <div className="an-sp" />
          <button className="an-btn gh xs" onClick={() => setShowSaveModal(false)}><i className="ti ti-x" /></button>
        </div>

        <div className="svbd">
          {isLevelDirty && (
            <div style={{ background: '#f0fdf4', border: '1px solid #86efac', color: '#166534', padding: '10px 14px', borderRadius: '6px', marginBottom: '14px', display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px' }}>
              <i className="ti ti-info-circle" style={{ fontSize: '18px', color: '#15803d' }} />
              <div>
                분류 단계가 <b>[{initialLevelMode}단계]</b>에서 <b>[{levelMode}단계]</b>로 변경되었습니다. (저장 시 project_list.lvcode에 반영)
              </div>
            </div>
          )}
          {/* 중복코드 정제 방식 선택 카드 (모드 A vs 모드 B) */}
          <div className="sv-mode-section">
            <div className="sv-mode-label">
              <span><i className="ti ti-adjustments-horizontal" />중복코드 정제 방식 선택</span>
              <span style={{ fontSize: '11px', color: '#64748B', fontWeight: 500 }}>
                {duplicateRemove ? '모드 A (전체 중복제거) 선택됨' : '모드 B (기본 결측정제) 선택됨'}
              </span>
            </div>
            <div className="sv-mode-cards">
              {/* 모드 A 카드 (전체 중복코드 제거) */}
              <div
                className={`sv-mode-card ${duplicateRemove ? 'active' : ''}`}
                onClick={() => {
                  if (!duplicateRemove) {
                    setDuplicateRemove(true);
                    showToast('전체 중복코드 제거 모드 (추천)로 변경되었습니다.');
                  }
                }}
              >
                <div className="sv-mode-hd">
                  <i className={`ti ${duplicateRemove ? 'ti-circle-dot' : 'ti-circle'}`} />
                  <span>전체 중복코드 제거</span>
                  <span className="sv-mode-rec-tag">⭐ 추천</span>
                </div>
                <div className="sv-mode-desc">
                  • 동일 응답자의 중복코드(12, 12 등) <b>완전 제거</b><br />
                  • 모름/없음(9999, 9998) 결측치 <b>논리적 정제</b>
                </div>
                <div className="sv-mode-stat">
                  <span className="st-target">정리 <b>{duplicateStats.modeA.totalRemoved.toLocaleString()}건</b></span>
                  <span className="st-fin">최종 <b>{duplicateStats.modeA.finalCount.toLocaleString()}건</b> 저장</span>
                </div>
              </div>

              {/* 모드 B 카드 (기본 결측 정제) */}
              <div
                className={`sv-mode-card ${!duplicateRemove ? 'active' : ''}`}
                onClick={() => {
                  if (duplicateRemove) {
                    setDuplicateRemove(false);
                    showToast('기본 결측 정제 모드 (일반중복 보존)로 변경되었습니다.');
                  }
                }}
              >
                <div className="sv-mode-hd">
                  <i className={`ti ${!duplicateRemove ? 'ti-circle-dot' : 'ti-circle'}`} />
                  <span>기본 결측 정제</span>
                  <span className="sv-mode-std-tag">기본</span>
                </div>
                <div className="sv-mode-desc">
                  • 일반코드 중복(12, 12 등)은 <b>원본 그대로 보존</b><br />
                  • 모름/없음 결측 및 특수코드 중복만 <b>정제</b>
                </div>
                <div className="sv-mode-stat">
                  <span className="st-target">정리 <b>{duplicateStats.modeB.totalRemoved.toLocaleString()}건</b></span>
                  <span className="st-fin">최종 <b>{duplicateStats.modeB.finalCount.toLocaleString()}건</b> 저장</span>
                </div>
              </div>
            </div>
          </div>

          {/* 모드별 상세 안내 박스 */}
          {duplicateRemove ? (
            <div style={{ background: '#f0fdf4', border: '1px solid #86efac', color: '#166534', padding: '10px 14px', borderRadius: '8px', marginBottom: '14px', display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '12.5px' }}>
              <i className="ti ti-check-circle" style={{ fontSize: '18px', color: '#15803d', marginTop: '1px' }} />
              <div>
                <div style={{ fontWeight: 700, marginBottom: '2px' }}>
                  [모드 A: 전체 중복코드 제거 적용 중] {isDuplicateRemoveDirty && <span style={{ color: '#b91c1c', fontWeight: 600 }}>(설정 변경됨)</span>}
                </div>
                <div>
                  동일 응답자(PID) 내 중복 일반코드 <b>{duplicateStats.modeA.generalDupCount.toLocaleString()}건</b>
                  {duplicateStats.modeA.dkRemovedCount > 0 && <>, 모름/무응답 결측 <b>{duplicateStats.modeA.dkRemovedCount.toLocaleString()}건</b></>}
                  {duplicateStats.modeA.specialDupCount > 0 && <>, 특수코드 중복 <b>{duplicateStats.modeA.specialDupCount.toLocaleString()}건</b></>}이 정리되어 최종 <b>{duplicateStats.modeA.finalCount.toLocaleString()}건</b>이 DB에 저장됩니다.
                  {duplicateStats.modeA.totalRemoved === 0 && <span style={{ color: '#475569' }}> (제거 대상 코드가 없어 전체 행이 유지됩니다.)</span>}
                </div>
              </div>
            </div>
          ) : (
            <div style={{ background: '#f8fafc', border: '1px solid #cbd5e1', color: '#334155', padding: '10px 14px', borderRadius: '8px', marginBottom: '14px', display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '12.5px' }}>
              <i className="ti ti-info-circle" style={{ fontSize: '18px', color: '#475569', marginTop: '1px' }} />
              <div>
                <div style={{ fontWeight: 700, marginBottom: '2px' }}>
                  [모드 B: 기본 결측 정제 저장] {isDuplicateRemoveDirty && <span style={{ color: '#b91c1c', fontWeight: 600 }}>(설정 변경됨)</span>}
                </div>
                <div>
                  일반코드 중복(예: 12, 12)은 <b>원본 그대로 보존</b>되며,
                  {duplicateStats.modeB.dkRemovedCount > 0 && <> 모름/무응답 결측 <b>{duplicateStats.modeB.dkRemovedCount.toLocaleString()}건</b></>}
                  {duplicateStats.modeB.specialDupCount > 0 && <>, 특수코드 중복 <b>{duplicateStats.modeB.specialDupCount.toLocaleString()}건</b></>}
                  {duplicateStats.modeB.totalRemoved === 0 ? ' (제거 대상 결측치가 없어 전체 행이 보존됩니다.)' : '이 정제되어'} 최종 <b>{duplicateStats.modeB.finalCount.toLocaleString()}건</b>이 DB에 저장됩니다.
                </div>
              </div>
            </div>
          )}
          {saveDiff ? (
            <>
              <div className="cmp" id="svCmp">
                <div className="cmpr h">
                  <span className="lb">구분</span>
                  <span className="v1">분석 결과</span>
                  <span className="v2">최종 DB</span>
                  <span className="dl">이번 반영</span>
                </div>
                <div className="cmpr ok">
                  <span className="lb">코딩 완료</span>
                  <span className="v1">{(saveDiff.coded || 0).toLocaleString()}</span>
                  <span className="v2">{(saveDiff.dbCoded || 0).toLocaleString()}</span>
                  <span className={`dl {((saveDiff.neu || 0) + (saveDiff.chg || 0)) > 0 ? 'plus' : 'zero'}`}>
                    {((saveDiff.neu || 0) + (saveDiff.chg || 0)) > 0 ? `+${((saveDiff.neu || 0) + (saveDiff.chg || 0)).toLocaleString()}` : '—'}
                  </span>
                </div>
                <div className={`cmpr ${(saveDiff.unc || 0) > 0 ? 'warn' : ''}`}>
                  <span className="lb">미코딩</span>
                  <span className="v1">{(saveDiff.unc || 0).toLocaleString()}</span>
                  <span className="v2">{(saveDiff.unc || 0).toLocaleString()}</span>
                  <span className={`dl ${saveWithUncoded && (saveDiff.unc || 0) > 0 ? 'plus' : 'zero'}`}>
                    {saveWithUncoded && (saveDiff.unc || 0) > 0 ? `+${(saveDiff.unc || 0).toLocaleString()}` : '—'}
                  </span>
                </div>
                <div className="cmpr">
                  <span className="lb">전체 PID</span>
                  <span className="v1">{(saveDiff.total || 0).toLocaleString()}</span>
                  <span className="v2">{((saveDiff.dbCoded || 0) + (saveDiff.unc || 0)).toLocaleString()}</span>
                  <span className="dl zero">{(saveDiff.dbNew || 0) > 0 ? `신규 ${(saveDiff.dbNew || 0).toLocaleString()}` : '—'}</span>
                </div>
              </div>

              <div className="brk">
                <span className="bk n">신규 <b>{(saveDiff.neu || 0).toLocaleString()}</b></span>
                <span className="bk c">변경 <b>{(saveDiff.chg || 0).toLocaleString()}</b></span>
                <span className="bk">동일 <b>{(saveDiff.same || 0).toLocaleString()}</b></span>
                {(saveDiff.dbNew || 0) > 0 && <span className="bk">DB 미등록 <b>{(saveDiff.dbNew || 0).toLocaleString()}</b></span>}
              </div>

              {(saveDiff.unc || 0) > 0 ? (
                <div className="note wr">
                  <i className="ti ti-alert-triangle" />
                  <div>
                    <b>미코딩 {(saveDiff.unc || 0).toLocaleString()}건</b>은 코드가 없어 DB에 반영되지 않습니다.
                    아래를 체크하면 지정한 코드로 함께 저장되고, 체크하지 않으면 다음에 <b>저장</b>을 다시 눌러 처리할 수 있습니다.
                  </div>
                </div>
              ) : (
                <div className="note ok">
                  <i className="ti ti-circle-check" />
                  <div>모든 PID에 코드가 지정되어 있습니다. 저장 시 DB와 완전히 일치합니다.</div>
                </div>
              )}

              <div
                className={`uncbox ${(saveDiff.unc || 0) === 0 ? 'dis' : ''} ${saveWithUncoded && (saveDiff.unc || 0) > 0 ? 'on' : ''}`}
                onClick={() => {
                  if ((saveDiff.unc || 0) > 0) setSaveWithUncoded(v => !v);
                }}
              >
                <span className={`sw ${saveWithUncoded && (saveDiff.unc || 0) > 0 ? 'on' : ''}`}><i className="ti ti-check" /></span>
                <span className="ut">미코딩 <b>{(saveDiff.unc || 0).toLocaleString()}</b>건도 함께 저장</span>
                <select
                  value={saveUncodedFallbackCode}
                  onChange={(e) => setSaveUncodedFallbackCode(e.target.value)}
                  onClick={(e) => e.stopPropagation()}
                >
                  {codes.map((c, i) => (
                    <option key={c.code_num || i} value={c.code_num || '99'}>
                      {c.lv123code || c.code_num || '99'} {c.lv3 || c.code_name || '기타/무응답'}
                    </option>
                  ))}
                  {codes.length === 0 && <option value="99">99 모름/무응답</option>}
                </select>
              </div>
            </>
          ) : (
            <div style={{ padding: '24px 0', textAlign: 'center', color: '#64748b' }}>
              비교 데이터를 계산하는 중입니다...
            </div>
          )}
        </div>

        <div className="svft">
          <span className="svlast">
            {headerMeta.duplicateRemoveDate ? `최근 저장: ${headerMeta.duplicateRemoveDate}` : '저장 이력 없음'}
            {saveDiff && ` · 반영 대기 ${(saveDiff.pending ?? ((saveDiff.unc || 0) + (saveDiff.neu || 0) + (saveDiff.chg || 0))).toLocaleString()}건`}
          </span>
          <div className="an-sp" />
          <button className="an-btn" onClick={() => setShowSaveModal(false)}>취소</button>
          <button
            className="an-btn br"
            disabled={saveLoading || (!isDuplicateRemoveDirty && !isLevelDirty && !codesDirty && !saveWithUncoded && (!saveDiff || saveDiff.pending === 0))}
            onClick={handleExecuteSave}
            title={`${duplicateRemove ? '[모드 A: 전체 중복코드 제거]' : '[모드 B: 기본 결측 정제]'} 최종 ${duplicateStats.finalCount.toLocaleString()}건 저장`}
          >
            {saveLoading ? <i className="ti ti-loader" style={{ animation: 'spin 1s linear infinite' }} /> : <i className="ti ti-database-import" />}
            저장 실행 ({duplicateStats.finalCount.toLocaleString()}건)
          </button>
        </div>
      </div>

      {/* 컬럼 설정 팝오버 (Fixed 화면 최상위 플로팅 레이어) */}
      {showColPop && (
        <div
          className="an-fpop"
          style={{
            left: `${colPopPos.x}px`,
            top: `${colPopPos.y}px`,
            transform: colPopPos.openUp ? 'translateY(-100%)' : 'none'
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="an-fph">
            <span>표시할 컬럼 선택</span>
            <button className="an-btn gh xs" onClick={() => setShowColPop(false)}><i className="ti ti-x" /></button>
          </div>
          <div className="an-fpl">
            <label className="an-fpi">
              <input type="checkbox" checked={colVis.raw} onChange={e => setColVis(v => ({ ...v, raw: e.target.checked }))} />
              <span>원본 응답</span>
            </label>
            <label className="an-fpi">
              <input type="checkbox" checked={colVis.clean} onChange={e => setColVis(v => ({ ...v, clean: e.target.checked }))} />
              <span>클리닝 응답</span>
            </label>
            <label className="an-fpi">
              <input type="checkbox" checked={colVis.multi} onChange={e => setColVis(v => ({ ...v, multi: e.target.checked }))} />
              <span>멀티</span>
            </label>
            {levelMode === '3' && (
              <label className="an-fpi">
                <input type="checkbox" checked={colVis.l1} onChange={e => setColVis(v => ({ ...v, l1: e.target.checked }))} />
                <span>대분류</span>
              </label>
            )}
            {(levelMode === '3' || levelMode === '2') && (
              <label className="an-fpi">
                <input type="checkbox" checked={colVis.l2} onChange={e => setColVis(v => ({ ...v, l2: e.target.checked }))} />
                <span>중분류</span>
              </label>
            )}
            <label className="an-fpi">
              <input type="checkbox" checked={colVis.l3} onChange={e => setColVis(v => ({ ...v, l3: e.target.checked }))} />
              <span>소분류</span>
            </label>
            <label className="an-fpi">
              <input type="checkbox" checked={colVis.sent} onChange={e => setColVis(v => ({ ...v, sent: e.target.checked }))} />
              <span>감성</span>
            </label>
            <label className="an-fpi">
              <input type="checkbox" checked={colVis.ok} onChange={e => setColVis(v => ({ ...v, ok: e.target.checked }))} />
              <span>확인</span>
            </label>
          </div>
          <div className="an-fpf">
            <button className="an-btn br xs" onClick={() => setShowColPop(false)}>닫기</button>
          </div>
        </div>
      )}

      {/* 감성 퀵 선택 미니 팝오버 (Fixed 최상위 플로팅) */}
      {sentPopRowKey && (
        <div
          className="an-sent-popover"
          style={{
            left: `${sentPopPos.x}px`,
            top: `${sentPopPos.y}px`,
            transform: `translateX(-50%) ${sentPopPos.openUp ? 'translateY(-100%)' : ''}`
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <button
            type="button"
            className="an-sent-pop-item pos"
            onClick={() => handleApplySentiment('pos', sentPopRowKey)}
          >
            <span className="dot" /> 긍정
          </button>
          <button
            type="button"
            className="an-sent-pop-item neu"
            onClick={() => handleApplySentiment('neu', sentPopRowKey)}
          >
            <span className="dot" /> 중립
          </button>
          <button
            type="button"
            className="an-sent-pop-item neg"
            onClick={() => handleApplySentiment('neg', sentPopRowKey)}
          >
            <span className="dot" /> 부정
          </button>
        </div>
      )}

      {/* Phase A: 소분류 빠른 지정 팝오버 (#pop) */}
      {showPop && (
        <div
          className="an-pop on"
          id="pop"
          style={{ left: `${popPos.x}px`, top: `${popPos.y}px` }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="an-ph" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <span className="t" id="popHd" style={{ fontWeight: 700 }}>
              {popTargetRow && selectedRowKeys.has(popTargetRow.rowKey) && selectedRowKeys.size > 1
                ? `선택한 ${selectedRowKeys.size}건에 분류 지정`
                : '이 응답의 분류 지정'}
            </span>
            <button className="an-btn gh xs" onClick={() => setShowPop(false)} style={{ padding: '0 4px', minWidth: 'auto' }}>
              <i className="ti ti-x" />
            </button>
          </div>
          <div className="an-ps">
            <input
              ref={popSearchInputRef}
              id="popQ"
              placeholder="소분류명 · 코드 검색"
              value={popSearch}
              onChange={(e) => {
                setPopSearch(e.target.value);
                setPopHighlightIdx(0);
              }}
              onKeyDown={handlePopKeyDown}
            />
          </div>
          <div className="an-pl2" id="popList">
            {popCandidates.map((c, i) => (
              <div
                key={c.lv123code || c.code_num || i}
                className={`an-pi${i === popHighlightIdx ? ' k' : ''}`}
                onClick={() => handlePickCode(c)}
              >
                <div className="r1">
                  <span className="cno">{c.lv123code || c.code_num || i + 1}</span>
                  <span className="nm">{c.lv3 || c.code_name || '-'}</span>
                  <span className="dim">
                    {Number(c.ex_sum || 0).toLocaleString()}
                  </span>
                </div>
                <div className="bc">{c.lv1 || '-'} › {c.lv2 || '-'}</div>
              </div>
            ))}
            {popCandidates.length === 0 && (
              <div className="an-empty" style={{ padding: '22px 0' }}>일치하는 분류가 없습니다.</div>
            )}
          </div>
          <div className="an-pf">
            <span className="k">↑↓</span> 이동 · <span className="k">Enter</span> 지정 · <span className="k">Esc</span> 닫기
          </div>
        </div>
      )}

      {/* Phase B: 보기유형 · 수정여부 변경 팝오버 (#srcpop) */}
      {showSrcPop && srcPopTarget && (
        <div
          id="srcpop"
          className="an-srcpop on"
          style={{ left: `${srcPopPos.x}px`, top: `${srcPopPos.y}px` }}
        >
          <div className="an-sph">
            <b>[{srcPopTarget.lv123code || srcPopTarget.lv3code}] {srcPopTarget.lv3}</b>
            <div className="an-spnow" style={{ fontSize: '10.5px', color: 'var(--tx3)', marginTop: '2px' }}>
              현재 <b>{getCodeSrc(srcPopTarget)}</b> · {getCodeLock(srcPopTarget) ? '수정불가' : '수정가능'}
            </div>
          </div>
          <div className="an-spl">
            <div className="an-spsec">보기유형</div>
            {['설문지', '자동분석', '트래킹'].map(s => {
              const cur = getCodeSrc(srcPopTarget) === s;
              const cardCls = s === '설문지' ? 's1' : s === '자동분석' ? 's2' : 's3';
              return (
                <div
                  key={s}
                  className={`an-spi${cur ? ' cur' : ''}`}
                  onClick={() => !cur && handleApplySrc(s)}
                >
                  <span className="ck2">{cur ? <i className="ti ti-check" /> : ''}</span>
                  <span className="tt2">
                    <span className={`an-sb ${cardCls}`} style={{ cursor: 'default' }}>{s}</span> {s}
                    <span className="d">기본 정책 · {typePolicies[s] ? '수정불가' : '수정가능'}</span>
                  </span>
                </div>
              );
            })}
            <div className="an-spsec">수정 여부</div>
            {[
              [true, '수정불가', '보기분석 실행 시 그대로 유지됩니다'],
              [false, '수정가능', '보기분석 실행 시 삭제 후 재생성됩니다']
            ].map(([v, n, d]) => {
              const cur = getCodeLock(srcPopTarget) === v;
              return (
                <div
                  key={String(v)}
                  className={`an-spi${cur ? ' cur' : ''}${!v ? ' warn' : ''}`}
                  onClick={() => !cur && handleApplyLock(v)}
                >
                  <span className="ck2">{cur ? <i className="ti ti-check" /> : ''}</span>
                  <span className="tt2">
                    <i className={`ti ti-lock${v ? '' : '-open'}`} style={{ fontSize: '12px', color: v ? '#2554A8' : 'var(--brand)' }} /> {n}
                    <span className="d">{d}</span>
                  </span>
                </div>
              );
            })}
          </div>
          <div className="an-spf">
            <i className="ti ti-info-circle" />
            <span>유형/잠금 상태는 보기분석 실행에 반영됩니다</span>
          </div>
        </div>
      )}

      {/* Phase C: 파이썬 워커 진행률 모달 (.an-wkmodal, .an-wkov) */}
      {showWorkerModal && (
        <div className="an-wkov" onClick={() => { if (!workerRunning) setShowWorkerModal(false); }}>
          <div className="an-wkmodal" onClick={e => e.stopPropagation()}>
            <div className="an-wkhd">
              <i className="ti ti-brand-python main-icon" />
              <b>{ACTION_NAMES[workerActionType] || '파이썬 작업 실행'}</b>
              <div className="an-sp" />
              <span className={`an-wkbadge ${workerStatusState === 'running' ? 'run' : (workerStatusState === 'completed' ? 'done' : 'err')}`}>
                {workerStatusState === 'running' ? '실행 중' : (workerStatusState === 'completed' ? '처리 완료' : '중지 / 오류')}
              </span>
            </div>

            <div className="an-wkbody">
              {/* 단계별 스텝 인디케이터 (4단계) */}
              <div className="an-wksteps">
                <div className={`an-wkstep ${workerStep === 1 ? 'active' : (workerStep > 1 ? 'completed' : '')}`}>
                  <div className="stitle">
                    <i className={`ti ti-${workerStep > 1 ? 'circle-check' : (workerStep === 1 ? 'loader' : 'circle')}`} />
                    <span>[1/4] 데이터 검증</span>
                  </div>
                  <div className="desc">설정 및 응답 전처리</div>
                </div>
                <div className={`an-wkstep ${workerStep === 2 ? 'active' : (workerStep > 2 ? 'completed' : '')}`}>
                  <div className="stitle">
                    <i className={`ti ti-${workerStep > 2 ? 'circle-check' : (workerStep === 2 ? 'loader' : 'circle')}`} />
                    <span>[2/4] 모델 호출</span>
                  </div>
                  <div className="desc">AI 파라미터 전달</div>
                </div>
                <div className={`an-wkstep ${workerStep === 3 ? 'active' : (workerStep > 3 ? 'completed' : '')}`}>
                  <div className="stitle">
                    <i className={`ti ti-${workerStep > 3 ? 'circle-check' : (workerStep === 3 ? 'loader' : 'circle')}`} />
                    <span>[3/4] 분류/임베딩</span>
                  </div>
                  <div className="desc">문맥 매핑 및 태깅</div>
                </div>
                <div className={`an-wkstep ${workerStep === 4 ? 'active' : (workerStep > 4 ? 'completed' : '')}`}>
                  <div className="stitle">
                    <i className={`ti ti-${workerStatusState === 'completed' ? 'circle-check' : (workerStep === 4 ? 'loader' : 'circle')}`} />
                    <span>[4/4] DB 반영</span>
                  </div>
                  <div className="desc">저장 및 결과 동기화</div>
                </div>
              </div>

              {/* 터미널 로그 콘솔 */}
              <div className="an-wkterm" ref={wkTermRef}>
                {consoleLines.map((line, idx) => {
                  let cls = '';
                  if (typeof line === 'string') {
                    if (line.includes('[오류]') || line.includes('Error') || line.includes('Fail')) cls = 'err';
                    else if (line.includes('완료') || line.includes('Success') || line.includes('성공')) cls = 'ok';
                    else if (line.startsWith('[')) cls = 'dim';
                  }
                  return (
                    <div key={idx} className={`an-wkterm-line ${cls}`}>
                      {line}
                    </div>
                  );
                })}
                {consoleLines.length === 0 && (
                  <div className="an-wkterm-line dim">작업 시작 대기 중...</div>
                )}
              </div>
            </div>

            <div className="an-wkft">
              <span className="an-wkelapsed">
                경과 시간: <b>{String(Math.floor(workerElapsed / 60)).padStart(2, '0')}:{String(workerElapsed % 60).padStart(2, '0')}</b>
              </span>
              <div className="an-sp" />
              {workerRunning ? (
                <>
                  <button className="an-btn gh" onClick={() => setShowWorkerModal(false)} title="모달만 닫고 작업은 백그라운드에서 계속 진행합니다">
                    백그라운드로 전환
                  </button>
                  <button className="an-btn" style={{ borderColor: 'var(--red, #D9483B)', color: 'var(--red, #D9483B)' }} onClick={handleStopWorker}>
                    <i className="ti ti-square" />작업 중지
                  </button>
                </>
              ) : (
                <button className="an-btn br" onClick={() => setShowWorkerModal(false)}>
                  확인 및 닫기
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Phase D: 엑셀 파일 선택용 숨김 input */}
      <input
        type="file"
        ref={excelFileInputRef}
        style={{ display: 'none' }}
        accept=".xlsx,.xls"
        onChange={handleExcelFileChange}
      />

      {/* Phase D: 엑셀 업로드 미리보기 모달 */}
      {showExcelModal && excelPreviewData && (
        <div className="an-modal-overlay" onClick={() => setShowExcelModal(false)}>
          <div className="an-exmodal" onClick={e => e.stopPropagation()}>
            <div className="an-exmodal-hd">
              <div className="an-exmodal-title">
                <i className="ti ti-file-spreadsheet" />
                <span>엑셀 보기 목록 미리보기</span>
                <span className="an-exmodal-filetag">{excelUploadedFileName}</span>
              </div>
              <button className="an-btn gh xs" onClick={() => setShowExcelModal(false)}>
                <i className="ti ti-x" />
              </button>
            </div>

            <div className="an-exmodal-statbar">
              <span className="an-exbadge total">전체 {excelPreviewData.totalCount || 0}건</span>
              <span className="an-exbadge new">신규 {excelPreviewData.newCount || 0}건</span>
              <span className="an-exbadge update">수정 {excelPreviewData.updateCount || 0}건</span>
              <span className="an-exbadge keep">유지 {excelPreviewData.keepCount || 0}건</span>
              {excelPreviewData.errorCount > 0 && (
                <span className="an-exbadge error">오류 {excelPreviewData.errorCount}건</span>
              )}
            </div>

            <div className="an-exmodal-body">
              <table className="an-extable">
                <thead>
                  <tr>
                    <th style={{ width: '48px', textAlign: 'center' }}>행</th>
                    <th style={{ width: '64px', textAlign: 'center' }}>상태</th>
                    <th style={{ width: '90px' }}>코드</th>
                    <th>소분류명 (보기명)</th>
                    <th style={{ width: '130px' }}>중분류</th>
                    <th style={{ width: '130px' }}>대분류</th>
                    <th style={{ width: '84px', textAlign: 'center' }}>보기유형</th>
                    <th style={{ width: '140px' }}>상태 안내</th>
                  </tr>
                </thead>
                <tbody>
                  {excelPreviewData.items.map((it, idx) => {
                    const badgeCls = it.status === '신규' ? 'new' : it.status === '수정' ? 'update' : it.status === '유지' ? 'keep' : 'error';
                    return (
                      <tr key={idx} className={it.status === '오류' ? 'err' : ''}>
                        <td style={{ textAlign: 'center', color: 'var(--tx3)' }}>{it.rowNum}</td>
                        <td style={{ textAlign: 'center' }}>
                          <span className={`an-exbadge ${badgeCls}`} style={{ padding: '1px 6px', fontSize: '10.5px' }}>
                            {it.status}
                          </span>
                        </td>
                        <td style={{ fontFamily: 'ui-monospace, Consolas, monospace', fontWeight: 600 }}>{it.lv123code}</td>
                        <td style={{ fontWeight: 600 }}>{it.lv3}</td>
                        <td style={{ color: 'var(--tx2)' }}>{it.lv2 || '-'}</td>
                        <td style={{ color: 'var(--tx2)' }}>{it.lv1 || '-'}</td>
                        <td style={{ textAlign: 'center', fontSize: '11px', color: 'var(--tx2)' }}>{it.ex_gubun}</td>
                        <td style={{ fontSize: '11px', color: it.status === '오류' ? '#DC2626' : 'var(--tx3)' }}>{it.message}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <div className="an-exmodal-ft">
              <div className="an-exmode-sel">
                <span style={{ fontWeight: 600 }}>반영 방식:</span>
                <label>
                  <input
                    type="radio"
                    name="excelApplyMode"
                    value="merge"
                    checked={excelApplyMode === 'merge'}
                    onChange={() => setExcelApplyMode('merge')}
                  />
                  <span>기존 보기에 병합 추가</span>
                </label>
                <label>
                  <input
                    type="radio"
                    name="excelApplyMode"
                    value="overwrite"
                    checked={excelApplyMode === 'overwrite'}
                    onChange={() => setExcelApplyMode('overwrite')}
                  />
                  <span>기존 보기 전체 대체 (덮어쓰기)</span>
                </label>
              </div>

              <div style={{ display: 'flex', gap: '8px' }}>
                <button className="an-btn" onClick={() => setShowExcelModal(false)}>취소</button>
                <button className="an-btn br" onClick={handleApplyExcel}>
                  <i className="ti ti-check" />보기에 반영하기
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Phase D: 타 프로젝트 보기 불러오기 모달 */}
      {showImportModal && (
        <div className="an-modal-overlay" onClick={() => setShowImportModal(false)}>
          <div className="an-impmodal" onClick={e => e.stopPropagation()}>
            <div className="an-impmodal-hd">
              <div className="an-impmodal-title">
                <i className="ti ti-file-import" />
                <span>타 프로젝트 보기 불러오기</span>
              </div>
              <button className="an-btn gh xs" onClick={() => setShowImportModal(false)}>
                <i className="ti ti-x" />
              </button>
            </div>

            <div className="an-impmodal-split">
              {/* 좌측: 프로젝트/문항 리스트 */}
              <div className="an-implist-pane">
                <div className="an-impsearch-box">
                  <i className="ti ti-search" style={{ color: 'var(--tx3)' }} />
                  <input
                    type="text"
                    placeholder="프로젝트 번호, 프로젝트명, 문항 검색..."
                    value={importSearch}
                    onChange={e => handleSearchImportProjects(e.target.value)}
                  />
                </div>

                <div className="an-implist-scroll">
                  {isLoadingImportList ? (
                    <div style={{ textAlign: 'center', padding: '30px 0', color: 'var(--tx3)', fontSize: '12px' }}>
                      <i className="ti ti-loader an-spin" style={{ fontSize: '18px', display: 'block', marginBottom: '6px' }} />
                      목록을 불러오는 중...
                    </div>
                  ) : importProjectList.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '30px 10px', color: 'var(--tx3)', fontSize: '12px' }}>
                      검색된 프로젝트/문항이 없습니다.
                    </div>
                  ) : (
                    importProjectList.map((item, idx) => {
                      const isSel = selectedImportItem && selectedImportItem.projectnum === item.projectnum && selectedImportItem.qnum === item.qnum;
                      return (
                        <div
                          key={`${item.projectnum}_${item.qnum}_${idx}`}
                          className={`an-impitem${isSel ? ' on' : ''}`}
                          onClick={() => handleSelectImportItem(item)}
                        >
                          <div className="an-impitem-top">
                            <span className="an-imppn">{item.projectnum}</span>
                            <span className="an-impqn">{item.qnum}</span>
                            <span className="an-impcount">{item.codeCount}개 보기</span>
                          </div>
                          <div className="an-impttl" title={item.projectname}>
                            {item.projectname || '(제목 없음)'}
                          </div>
                          <div className="an-impdate">{item.insert_date || ''}</div>
                        </div>
                      );
                    })
                  )}
                </div>
              </div>

              {/* 우측: 선택된 문항 보기 데이터 미리보기 */}
              <div className="an-imppreview-pane">
                <div className="an-imppreview-hd">
                  {selectedImportItem ? (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <span style={{ fontWeight: 700, color: 'var(--tx)' }}>
                        [{selectedImportItem.projectnum}] {selectedImportItem.qnum}
                      </span>
                      <span style={{ color: 'var(--tx3)', fontSize: '12px' }}>
                        — {importCodes.length}개 보기 중 <b>{selectedImportCodes.size}</b>개 선택됨
                      </span>
                    </div>
                  ) : (
                    <span style={{ color: 'var(--tx3)', fontSize: '12px' }}>
                      좌측에서 프로젝트 및 문항을 선택해주세요.
                    </span>
                  )}

                  {importCodes.length > 0 && (
                    <button className="an-btn xs" onClick={handleToggleAllImportCodes}>
                      {selectedImportCodes.size === importCodes.length ? '선택 해제' : '전체 선택'}
                    </button>
                  )}
                </div>

                <div className="an-imppreview-body">
                  {isLoadingImportCodes ? (
                    <div style={{ textAlign: 'center', padding: '60px 0', color: 'var(--tx3)', fontSize: '12px' }}>
                      <i className="ti ti-loader an-spin" style={{ fontSize: '20px', display: 'block', marginBottom: '8px' }} />
                      보기 데이터를 가져오는 중...
                    </div>
                  ) : !selectedImportItem ? (
                    <div style={{ textAlign: 'center', padding: '80px 20px', color: 'var(--tx3)' }}>
                      <i className="ti ti-layout-sidebar-left-expand" style={{ fontSize: '32px', display: 'block', marginBottom: '10px' }} />
                      좌측 목록에서 가져올 프로젝트/문항을 클릭하세요.
                    </div>
                  ) : importCodes.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '60px 20px', color: 'var(--tx3)' }}>
                      해당 문항에 등록된 보기 데이터가 없습니다.
                    </div>
                  ) : (
                    <table className="an-imptable">
                      <thead>
                        <tr>
                          <th style={{ width: '40px', textAlign: 'center' }}>선택</th>
                          <th style={{ width: '90px' }}>코드</th>
                          <th>소분류명 (보기명)</th>
                          <th style={{ width: '130px' }}>중분류</th>
                          <th style={{ width: '130px' }}>대분류</th>
                          <th style={{ width: '80px', textAlign: 'center' }}>보기유형</th>
                        </tr>
                      </thead>
                      <tbody>
                        {importCodes.map((c, idx) => {
                          const codeStr = String(c.lv123code || '').trim();
                          const isChecked = selectedImportCodes.has(codeStr);
                          return (
                            <tr
                              key={idx}
                              onClick={() => handleToggleImportCode(codeStr)}
                              style={{ cursor: 'pointer', background: isChecked ? 'var(--brand-weak, #FFF3EC)' : 'transparent' }}
                            >
                              <td style={{ textAlign: 'center' }}>
                                <input
                                  type="checkbox"
                                  checked={isChecked}
                                  onChange={() => handleToggleImportCode(codeStr)}
                                  onClick={e => e.stopPropagation()}
                                />
                              </td>
                              <td style={{ fontFamily: 'ui-monospace, Consolas, monospace', fontWeight: 600 }}>{c.lv123code}</td>
                              <td style={{ fontWeight: 600 }}>{c.lv3}</td>
                              <td style={{ color: 'var(--tx2)' }}>{c.lv2 || '-'}</td>
                              <td style={{ color: 'var(--tx2)' }}>{c.lv1 || '-'}</td>
                              <td style={{ textAlign: 'center', fontSize: '11px', color: 'var(--tx2)' }}>{c.ex_gubun || '자동분석'}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  )}
                </div>
              </div>
            </div>

            <div className="an-impmodal-ft">
              <span style={{ fontSize: '12px', color: 'var(--tx2)' }}>
                선택된 보기: <b>{selectedImportCodes.size}</b>개
              </span>

              <div style={{ display: 'flex', gap: '8px' }}>
                <button className="an-btn" onClick={() => setShowImportModal(false)}>취소</button>
                <button
                  className="an-btn br"
                  disabled={selectedImportCodes.size === 0 || isApplyingImport}
                  onClick={handleApplyImport}
                >
                  <i className={`ti ti-${isApplyingImport ? 'loader an-spin' : 'check'}`} />
                  선택한 보기 가져오기
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
      {toastMsg && (
        <div style={{
          position: 'fixed',
          left: '50%',
          bottom: '24px',
          transform: 'translateX(-50%)',
          zIndex: 9999,
          background: '#22252A',
          color: '#fff',
          fontSize: '12.5px',
          padding: '10px 18px',
          borderRadius: '10px',
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          boxShadow: '0 10px 28px rgba(0,0,0,.26)'
        }}>
          <i className="ti ti-info-circle" style={{ color: 'var(--brand)' }} />
          <span>{toastMsg}</span>
        </div>
      )}
    </div>
  );
}
