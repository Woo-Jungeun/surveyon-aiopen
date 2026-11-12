import React, { useState, useEffect, useRef } from 'react';
import { apiRequest, INTERNAL_KEY, API_BASE } from '../api/client';
import ProRegisterModal from '../components/ProRegisterModal';
import ProLabelRegisterModal from '../components/ProLabelRegisterModal';
import HanqOptionModal from '../components/HanqOptionModal';
import HanqProgressModal from '../components/HanqProgressModal';

// 범용 문항 데이터 추출기 (resultjson 및 중첩 구조 지원)
const extractQuestionArray = (res) => {
  if (!res) return [];
  const payload = res.resultjson || res.contents || res.output || res;
  if (Array.isArray(payload)) return payload;
  let rawData = payload.resultjson ?? payload.result ?? payload.data ?? payload.items;
  if (!rawData && typeof payload === 'object') rawData = payload;
  if (typeof rawData === 'string') { try { rawData = JSON.parse(rawData); } catch {} }
  if (Array.isArray(rawData)) return rawData;
  if (rawData && typeof rawData === 'object') {
    if (Array.isArray(rawData.resultjson)) return rawData.resultjson;
    if (typeof rawData.resultjson === 'string') {
      try { const inner = JSON.parse(rawData.resultjson); if (Array.isArray(inner)) return inner; } catch {}
    }
  }
  return [];
};

// 진행상황 -> 칩 클래스 매핑
const getChipClass = (status) => {
  if (!status) return 'idle';
  const s = status.trim();
  if (s === '완료') return 'done';
  if (s === '분석중' || s === '진행중') return 'run';
  if (s === '대기') return 'wait';
  return 'idle';
};

export default function ProListPage({ userInfo, selectedPn, selectedProjectName, selectedServername, selectedPof, onBackToProjects, onLogout, onOpenAnalysis }) {
  const user = (userInfo && userInfo.userid) || 'syhong';
  const username = (userInfo && userInfo.username) || '홍소연';

  const [projectnum, setProjectnum] = useState(selectedPn || '');
  const [projectName, setProjectName] = useState(selectedProjectName || '');
  const [qList, setQList] = useState([]);
  const [loading, setLoading] = useState(false);
  const [statusMsg, setStatusMsg] = useState('');
  const [searchQuery, setSearchQuery] = useState('');

  // Dropdown
  const [ddOut, setDdOut] = useState(false);
  const [ddIn, setDdIn] = useState(false);

  // 사이드바 간소화 (접기/펼치기) 상태
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

  // Filter Modal state
  const [showFilterModal, setShowFilterModal] = useState(false);
  const [filterTargetQnum, setFilterTargetQnum] = useState('');
  const [filterTargetTitle, setFilterTargetTitle] = useState('');
  const [filterQList, setFilterQList] = useState([]);
  const [filterSearch, setFilterSearch] = useState('');
  const [filterChecked, setFilterChecked] = useState({});
  const [mergeQnumMap, setMergeQnumMap] = useState({});

  // Summary & Completed Stats state
  const [completedStats, setCompletedStats] = useState({ survey_count: 0, surveyon_count: 0, open_registered_count: 0, ai_wait_count: 0, diff_count: 0, has_diff: false });
  const [summaryInfo, setSummaryInfo] = useState({ total_qnum_count: 0, completed_qnum_count: 0, completed_ratio: 0, excluded_qnum_count: 0 });
  const [isDirectRegistering, setIsDirectRegistering] = useState(false);

  // PID Diff Modal state
  const [showPidDiffModal, setShowPidDiffModal] = useState(false);
  const [pidDiffData, setPidDiffData] = useState({ survey_count: 0, surveyon_count: 0, open_registered_count: 0, ai_wait_count: 0, diff_count: 0, only_in_survey: [], only_in_surveyon: [], unregistered_open: [], only_in_open: [] });
  const [activePidTab, setActivePidTab] = useState('unregistered_open');

  // Label / Data Export Modal state
  const [showLabelModal, setShowLabelModal] = useState(false);
  const [modalTitle, setModalTitle] = useState('');
  const [modalContent, setModalContent] = useState('');
  const [exportType, setExportType] = useState('');

  // (3) 체크박스 선택 state
  const [sel, setSel] = useState(new Set());       // 개별 문항 id 선택
  const [gsel, setGsel] = useState(new Set());     // 그룹 key 선택

  // 마우스 드래그 선택 state
  const [dragStartIdx, setDragStartIdx] = useState(null);
  const [dragCurrentIdx, setDragCurrentIdx] = useState(null);
  const isMouseDownRef = useRef(false);
  const dragInitialSelRef = useRef(new Set());
  const dragInitialGSelRef = useRef(new Set());

  // (5) 묶기 모드 state
  const [mergeMode, setMergeMode] = useState(false);

  // 문항 통합저장(merge_qnum 수정) 모달 state
  const [showMergeModal, setShowMergeModal] = useState(false);
  const [customMergeQnum, setCustomMergeQnum] = useState('');
  const [mergeModalTargets, setMergeModalTargets] = useState([]);

  // 묶음 그룹 펼침/접힘
  const [expandedGroups, setExpandedGroups] = useState(new Set());

  // 문항 등록 모달 state
  const [showProRegisterModal, setShowProRegisterModal] = useState(false);

  // 보기 등록(전체문항) 모달 state
  const [showLabelRegisterModal, setShowLabelRegisterModal] = useState(false);

  // 한큐분석 범용 옵션 설정 및 진행 모달 state
  const [showHanqOptionModal, setShowHanqOptionModal] = useState(false);
  const [showHanqProgressModal, setShowHanqProgressModal] = useState(false);

  // 문항최종 일괄 수정 모달 state
  const [showFinEditModal, setShowFinEditModal] = useState(false);
  const [finEditList, setFinEditList] = useState([]);
  const [finFindText, setFinFindText] = useState('');
  const [finReplaceText, setFinReplaceText] = useState('');
  const [finBulkText, setFinBulkText] = useState('');

  // Toast
  const [toastMsg, setToastMsg] = useState('');
  const toastTimer = useRef(null);
  const showToast = (msg) => {
    setToastMsg(msg);
    clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToastMsg(''), 2200);
  };

  // ============= API 연동 함수 (기존 100% 유지) =============

  // 1. [POST /pro_list/select] 문항 목록 및 집계 조회
  const fetchQuestionList = async (targetPn = null) => {
    const pnToFetch = targetPn || projectnum;
    if (!pnToFetch) return;
    setLoading(true);
    setStatusMsg('');

    const res = await apiRequest('/pro_list/select', {
      user: user,
      projectnum: pnToFetch,
      servername: (selectedServername || '').toLowerCase()
    });

    setLoading(false);

    if (res && res.success === '777') {
      const parsedArr = extractQuestionArray(res);
      setQList(parsedArr);
      const initialMergeMap = {};
      parsedArr.forEach(item => {
        if (item.id) initialMergeMap[item.id] = item.merge_qnum || '';
      });
      setMergeQnumMap(initialMergeMap);

      const payload = res.resultjson || res.contents || res.output || res;
      if (payload && typeof payload === 'object') {
        if (payload.completed_stats) setCompletedStats(payload.completed_stats);
        if (payload.summary) setSummaryInfo(payload.summary);
      }
    } else {
      setQList([]);
      setStatusMsg((res && res.message) || '문항 목록을 불러오지 못했습니다.');
    }
  };

  useEffect(() => {
    if (selectedPn) {
      setProjectnum(selectedPn);
      setProjectName(selectedProjectName || selectedPn);
      fetchQuestionList(selectedPn);
    }
  }, [selectedPn]);

  // PID 차이 모달 오픈 및 데이터 조회
  const handleOpenPidDiffModal = async () => {
    setShowPidDiffModal(true);
    const res = await apiRequest('/pro_list/pid_diff', {
      user: user,
      projectnum: projectnum,
      servername: (selectedServername || '').toLowerCase()
    });
    if (res && res.success === '777') {
      const payload = res.resultjson || res.contents || res.output || res;
      setPidDiffData(payload);
      if (payload.unregistered_open && payload.unregistered_open.length > 0) {
        setActivePidTab('unregistered_open');
      } else if (payload.only_in_survey && payload.only_in_survey.length > 0) {
        setActivePidTab('survey');
      } else if (payload.only_in_surveyon && payload.only_in_surveyon.length > 0) {
        setActivePidTab('surveyon');
      } else if (payload.only_in_open && payload.only_in_open.length > 0) {
        setActivePidTab('only_in_open');
      } else {
        setActivePidTab('survey');
      }
    }
  };

  // AI오픈대기수 클릭 시 데이터등록 -> 문항등록 -> 등록 바로 실행
  const handleDirectDbEnter = async () => {
    if (isDirectRegistering) return;
    if (!projectnum) {
      showToast('프로젝트 번호가 유효하지 않습니다.');
      return;
    }

    const server = (selectedServername || (qList[0] && (qList[0].servername || qList[0].projectpof)) || 'rps').toLowerCase();
    const pof = selectedPof || (qList[0] && qList[0].projectpof) || '';
    const proname = projectName || selectedProjectName || (qList[0] && qList[0].projectname) || '';

    setIsDirectRegistering(true);
    showToast('AI오픈 대기 응답자를 등록 중입니다...');

    try {
      const data = await apiRequest('/pro_register/db_enter', {
        projectnum,
        server,
        pof,
        proname,
        user
      });

      if (data && (data.success === '777' || data.code === '777')) {
        const respData = data.resultjson || data;
        const msg = respData.message || respData.Message || '오픈데이터가 성공적으로 등록되었습니다.';
        showToast(msg);
        // 문항 목록 및 완료자수/대기수 실시간 재조회
        await fetchQuestionList();
      } else {
        showToast((data && (data.Message || data.message)) || '오픈데이터 등록에 실패했습니다.');
      }
    } catch (err) {
      console.error(err);
      showToast('오픈데이터 등록 처리 중 오류가 발생했습니다.');
    } finally {
      setIsDirectRegistering(false);
    }
  };

  // 2. [POST /pro_list/analysis] 개별/그룹 분석 토글
  const handleToggleAnalysis = async (item, targetVal = null, isGroup = false, groupKey = null) => {
    const isCurrentlyAnalysis = item.useYN === '분석' || item.useYN === '머지';
    const nextVal = targetVal !== null ? targetVal : (isCurrentlyAnalysis ? '0' : '1');
    const mergeQnum = groupKey || item.merge_qnum || item.merge_qnum_check || '';

    const payload = isGroup && mergeQnum ? {
      user, projectnum,
      merge_qnum: mergeQnum,
      column: 'useYN',
      value: nextVal
    } : {
      user, projectnum,
      qid: item.id,
      qnum: item.qnum || item.qnum_text,
      column: 'useYN',
      value: nextVal
    };

    const res = await apiRequest('/pro_list/analysis', payload);
    if (res && res.success === '777') fetchQuestionList();
  };

  // 3. [POST /pro_list/allanalysis] 전체 분석 일괄 변경
  const handleAllAnalysis = async (nextVal) => {
    const res = await apiRequest('/pro_list/allanalysis', { user, projectnum, column: 'useYN', value: nextVal });
    if (res && res.success === '777') fetchQuestionList();
  };

  // 4. [POST /pro_list/rowEdit] 개별/그룹 수정 토글
  const handleToggleEdit = async (item, targetVal = null, isGroup = false, groupKey = null) => {
    const isEditable = item.project_lock === '수정가능' || item.project_lock === '수정' || item.project_lock === '가능';
    const nextVal = targetVal !== null ? targetVal : (isEditable ? '0' : '1');
    const mergeQnum = groupKey || item.merge_qnum || item.merge_qnum_check || '';

    const payload = isGroup && mergeQnum ? {
      user, projectnum,
      merge_qnum: mergeQnum,
      column: 'project_lock',
      value: nextVal
    } : {
      user, projectnum,
      qid: item.id,
      qnum: item.qnum || item.qnum_text,
      column: 'project_lock',
      value: nextVal
    };

    const res = await apiRequest('/pro_list/rowEdit', payload);
    if (res && res.success === '777') fetchQuestionList();
  };

  // 5. [POST /pro_list/allEdit] 전체 수정 일괄 변경
  const handleAllEdit = async (nextVal) => {
    const res = await apiRequest('/pro_list/allEdit', { user, projectnum, column: 'project_lock', value: nextVal });
    if (res && res.success === '777') fetchQuestionList();
  };

  // 5-1. 문항통합 입력 및 저장
  const handleMergeInputChange = (id, value) => {
    setMergeQnumMap(prev => ({ ...prev, [id]: value }));
  };

  const handleSaveAllMerge = async () => {
    const res = await apiRequest('/pro_list/allmerge', { user, projectnum, data: mergeQnumMap });
    if (res && res.success === '777') { fetchQuestionList(); showToast('문항통합 저장 완료'); }
  };

  // 6. 보기 추출 (개발자용 / DP용)
  const handleExportLabels = async (gbType) => {
    const isDp = gbType === 'export_lb_dp';
    setModalTitle(isDp ? '보기 추출 (DP용)' : '보기 추출 (개발자용)');
    setExportType(gbType);

    const res = await apiRequest(`/pro_list/${gbType}`, { user, projectnum });
    if (res && res.success === '777') {
      if (isDp && res.script_text) {
        setModalContent(res.script_text);
      } else {
        const parsed = extractQuestionArray(res);
        setModalContent(JSON.stringify(parsed.length > 0 ? parsed : (res.contents || res.output || {}), null, 2));
      }
      setShowLabelModal(true);
    }
  };

  // 7. 응답 추출 (ALL)
  const handleExportDataAll = async () => {
    setModalTitle('응답 수집 데이터 추출 (ALL)');
    setExportType('export_data_all');

    const res = await apiRequest('/pro_list/export_data_all', { user, projectnum });
    if (res && res.success === '777') {
      const parsed = extractQuestionArray(res);
      setModalContent(JSON.stringify(parsed.length > 0 ? parsed : (res.contents || res.output || {}), null, 2));
      setShowLabelModal(true);
    }
  };

  // 실제 파일 다운로드 핸들러 (인증 헤더 전송 + Blob 다운로드)
  const handleDownloadExportFile = async (type) => {
    let endpoint = '';
    let fileName = '';
    if (type === 'export_lb_excel') {
      endpoint = `/pro_list/export_lb_excel_file`;
      fileName = `${projectnum}_보기추출_개발자용.xlsx`;
    } else if (type === 'export_lb_dp') {
      endpoint = `/pro_list/export_lb_dp_file`;
      fileName = `${projectnum}_보기추출_DP용.txt`;
    } else if (type === 'export_data_all') {
      endpoint = `/pro_list/export_data_all_file`;
      fileName = `${projectnum}_응답추출_전체.xlsx`;
    }

    if (endpoint) {
      try {
        const token = localStorage.getItem('X-Auth-Token') || '';
        const urlWithQuery = `${API_BASE}${endpoint}?projectnum=${encodeURIComponent(projectnum)}&token=${encodeURIComponent(token)}`;
        
        showToast('파일 다운로드를 처리 중입니다...');

        const response = await fetch(urlWithQuery, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'X-Auth-Token': token,
            'Authorization': token ? `Bearer ${token}` : '',
            'X-Internal-Key': INTERNAL_KEY || 'cGZeWyxlli5N4FRSwMAsbbRoNzJEhFkocore'
          },
          body: JSON.stringify({ projectnum })
        });

        if (!response.ok) {
          const errText = await response.text();
          let msg = '파일 다운로드 실패 (권한 또는 서버 오류)';
          try {
            const errJson = JSON.parse(errText);
            if (errJson.Message) msg += `: ${errJson.Message}`;
            else if (errJson.message) msg += `: ${errJson.message}`;
          } catch {}
          showToast(msg);
          return;
        }

        const blob = await response.blob();
        const downloadUrl = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = downloadUrl;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        window.URL.revokeObjectURL(downloadUrl);
        showToast('파일 다운로드가 완료되었습니다.');
      } catch (err) {
        showToast('다운로드 중 오류가 발생했습니다: ' + err.message);
      }
    }
  };

  // 8. 필터 모달 오픈
  const handleOpenFilterModal = async (targetIdOrQnum = '', targetQnum = '', targetQnumText = '') => {
    let qnumParam = '';
    let titleLabel = '';

    if (typeof targetIdOrQnum === 'object' && targetIdOrQnum !== null) {
      const item = targetIdOrQnum;
      qnumParam = String(item.id || item.qnum || '');
      titleLabel = item.qnum ? (item.qnum_text ? `${item.qnum} / ${item.qnum_text}` : item.qnum) : (item.qnum_text || `ID:${item.id}`);
    } else if (targetIdOrQnum !== '' && targetIdOrQnum !== undefined && targetIdOrQnum !== null) {
      qnumParam = String(targetIdOrQnum);
      if (targetQnum && targetQnumText) {
        titleLabel = `${targetQnum} / ${targetQnumText}`;
      } else if (targetQnum) {
        titleLabel = String(targetQnum);
      } else {
        titleLabel = String(targetIdOrQnum);
      }
    }

    setFilterTargetQnum(qnumParam);
    setFilterTargetTitle(titleLabel);
    setFilterSearch('');
    const initialChecked = {};
    const res = await apiRequest('/pro_list/filter_select_qnum', { user, projectnum, qnum: qnumParam });
    if (res && res.success === '777') {
      const list = extractQuestionArray(res);
      setFilterQList(list);
      list.forEach(q => {
        const vKey = q.qnum_variable || q.qnum || q.qnum_text;
        if (q.qnum_db === 'true' || q.qnum_db === true) {
          initialChecked[vKey] = true;
        }
      });
    } else {
      setFilterQList([]);
    }
    setFilterChecked(initialChecked);
    setShowFilterModal(true);
  };

  // 9. 필터 체크박스 토글 (단발 개별 선택)
  const handleFilterCheck = (qnum) => {
    setFilterChecked(prev => {
      const next = { ...prev };
      if (next[qnum]) { delete next[qnum]; }
      else { next[qnum] = true; }
      return next;
    });
  };

  // 9-2. 필터 전체 선택 / 해제 (일괄 선택)
  const handleToggleAllFilter = () => {
    if (!filterQList || filterQList.length === 0) return;
    const allKeys = filterQList.map(q => q.qnum_variable || q.qnum || q.qnum_text);
    const isAllChecked = allKeys.length > 0 && allKeys.every(k => filterChecked[k]);

    if (isAllChecked) {
      setFilterChecked({});
    } else {
      const nextChecked = {};
      allKeys.forEach(k => { nextChecked[k] = true; });
      setFilterChecked(nextChecked);
    }
  };

  // 10. 필터 저장
  const handleSaveFilter = async () => {
    const selectedQnums = Object.keys(filterChecked);
    const datajsonArr = selectedQnums.map(v => ({ qnum_variable: v }));
    const endpoint = filterTargetQnum ? '/pro_list/filter_update_single' : '/pro_list/filter_update_all';
    const res = await apiRequest(endpoint, {
      user, projectnum,
      qnum: filterTargetQnum,
      data: { selected: selectedQnums },
      datajson: JSON.stringify(datajsonArr)
    });
    if (res && res.success === '777') {
      setShowFilterModal(false);
      fetchQuestionList();
      showToast(filterTargetQnum ? '단일 문항 필터가 저장되었습니다' : '전체 분석문항 필터가 일괄 저장되었습니다');
    }
  };

  const filteredFilterQList = filterQList.filter(q => {
    if (!filterSearch) return true;
    const searchLower = filterSearch.toLowerCase();
    return (q.qnum_text || '').toLowerCase().includes(searchLower) ||
           (q.qnum || '').toLowerCase().includes(searchLower) ||
           (q.qnum_variable || '').toLowerCase().includes(searchLower) ||
           (q.qnum_question || q.question_fin || '').toLowerCase().includes(searchLower);
  });
  const checkedCount = Object.keys(filterChecked).length;

  // ============= (3) 체크박스 이벤트 =============

  // 개별 문항 토글 (useYN === '머지' 묶음 문항은 선택 불가)
  const handleTogRow = (id) => {
    const item = qList.find(r => r.id === id);
    if (item && item.useYN === '머지') return;
    setSel(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  // 그룹 토글
  const handleTogGroup = (key) => {
    setGsel(prev => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  };

  // 전체 선택/해제
  const handleToggleAll = () => {
    const visibleIds = filteredQList.filter(r => !r._isGroupChild).map(r => r.id);
    const allSelected = visibleIds.length > 0 && visibleIds.every(id => sel.has(id));
    if (allSelected) {
      setSel(prev => { const next = new Set(prev); visibleIds.forEach(id => next.delete(id)); return next; });
    } else {
      setSel(prev => { const next = new Set(prev); visibleIds.forEach(id => next.add(id)); return next; });
    }
  };

  // 선택 초기화
  const handleClearSel = () => { setSel(new Set()); setGsel(new Set()); };

  // 일괄 대상 계산 (sel + gsel에 소속된 문항)
  const getBulkTargets = () => {
    return qList.filter(r => sel.has(r.id) || (r.merge_qnum && gsel.has(r.merge_qnum)));
  };

  // 일괄 분석/제외 (선택한 문항 대상만)
  const handleBulkMode = async (mode) => {
    const targets = getBulkTargets();
    if (!targets.length) return;
    const nextVal = mode === 'an' ? '1' : '0';
    const selIds = Array.from(sel);
    const gselKeys = Array.from(gsel);

    const res = await apiRequest('/pro_list/bulk_update', {
      user, projectnum,
      column: 'useYN',
      value: nextVal,
      ids: selIds,
      merge_qnums: gselKeys
    });

    if (res && res.success === '777') {
      handleClearSel();
      fetchQuestionList();
      showToast(mode === 'ex' ? `선택한 ${targets.length}개 문항을 분석에서 제외했습니다` : `선택한 ${targets.length}개 문항을 분석 대상으로 설정했습니다`);
    }
  };

  // 일괄 수정 잠금 (선택한 문항 대상만)
  const handleBulkLock = async (unlock) => {
    const targets = getBulkTargets();
    if (!targets.length) return;
    const nextVal = unlock ? '1' : '0';
    const selIds = Array.from(sel);
    const gselKeys = Array.from(gsel);

    const res = await apiRequest('/pro_list/bulk_update', {
      user, projectnum,
      column: 'project_lock',
      value: nextVal,
      ids: selIds,
      merge_qnums: gselKeys
    });

    if (res && res.success === '777') {
      handleClearSel();
      fetchQuestionList();
      showToast(unlock ? `선택한 ${targets.length}개 문항을 수정 가능(O)으로 열었습니다` : `선택한 ${targets.length}개 문항을 수정 잠금(X)으로 전환했습니다`);
    }
  };

  // 일괄 문항 삭제
  const handleBulkDelete = async () => {
    const targets = getBulkTargets();
    if (!targets.length) return;

    if (!window.confirm(`선택한 ${targets.length}개 문항을 삭제하시겠습니까?\n삭제된 문항은 project_list 및 pn_in에서 완전히 제거됩니다.`)) {
      return;
    }

    const targetQnums = Array.from(new Set(targets.map(t => t.qnum).filter(Boolean)));
    const targetIds = Array.from(new Set(targets.map(t => t.id).filter(Boolean).map(id => parseInt(id, 10))));

    const res = await apiRequest('/pro_list/qnum_delete', {
      projectnum,
      qnums: targetQnums,
      ids: targetIds
    });

    if (res && res.success === '777') {
      handleClearSel();
      fetchQuestionList();
      showToast(`선택한 ${targets.length}개 문항이 삭제되었습니다`);
    } else {
      showToast(res?.message || '문항 삭제에 실패했습니다');
    }
  };

  // 문항최종 일괄 수정 모달 열기
  const handleOpenFinEditModal = () => {
    const targets = getBulkTargets();
    if (!targets.length) {
      showToast('수정할 문항을 선택하세요');
      return;
    }
    const list = targets.map(item => ({
      id: item.id,
      qnum: item.qnum || '',
      qnum_text: item.qnum_text || '',
      merge_qnum: item.merge_qnum || '',
      question_fin: item.question_fin || item.question_origin || '',
      original_fin: item.question_fin || item.question_origin || '',
      question_origin: item.question_origin || ''
    }));
    setFinEditList(list);
    setFinFindText('');
    setFinReplaceText('');
    setFinBulkText('');
    setShowFinEditModal(true);
  };

  // 문항최종 모달 개별 레이블 수정
  const handleFinItemChange = (id, val) => {
    setFinEditList(prev => prev.map(item => item.id === id ? { ...item, question_fin: val } : item));
  };

  // 문항최종 모달 - 찾아 바꾸기 (Find & Replace)
  const handleFinBatchReplace = () => {
    if (!finFindText) { showToast('찾을 단어를 입력하세요'); return; }
    setFinEditList(prev => prev.map(item => ({
      ...item,
      question_fin: (item.question_fin || '').replaceAll(finFindText, finReplaceText)
    })));
    showToast(`'${finFindText}' → '${finReplaceText}' (으)로 일괄 치환되었습니다`);
  };

  // 문항최종 모달 - 일괄 적용 문구 적용 (모든 선택 문항 동일 입력)
  const handleFinApplyBulkText = () => {
    if (!finBulkText) { showToast('일괄 적용할 문구를 입력하세요'); return; }
    setFinEditList(prev => prev.map(item => ({
      ...item,
      question_fin: finBulkText
    })));
    showToast(`선택한 문항들에 '${finBulkText}' 문구가 일괄 적용되었습니다`);
  };

  // 문항최종 모달 - 원래대로 초기화 (question_origin 원본 문항 레이블 적용)
  const handleFinReset = () => {
    setFinEditList(prev => prev.map(item => ({
      ...item,
      question_fin: item.question_origin || ''
    })));
    showToast('원본(question_origin) 문항 레이블로 초기화되었습니다');
  };

  // 문항최종 모달 - 일괄 저장
  const handleSaveFinBatch = async () => {
    const data = {};
    finEditList.forEach(item => {
      data[item.id] = item.question_fin;
    });
    const res = await apiRequest('/pro_list/batch_question_fin', { user, projectnum, data });
    if (res && res.success === '777') {
      setShowFinEditModal(false);
      handleClearSel();
      fetchQuestionList();
      showToast(`${finEditList.length}개 문항의 문항최종 레이블이 저장되었습니다`);
    }
  };

  // 일괄 묶기 모달 오픈 (하단 컨트롤바 또는 트레이에서)
  const handleOpenMergeModal = (overrideTargets = null) => {
    let targets = overrideTargets;
    if (!targets) {
      if (mergeMode) {
        targets = qList.filter(r => sel.has(r.id));
      } else {
        targets = getBulkTargets().filter(r => r.useYN !== '머지');
      }
    }
    if (!targets || targets.length < 2) {
      showToast('묶을 2개 이상의 문항을 선택하세요');
      return;
    }
    const defaultLabel = targets[0].qnum_text || targets[0].qnum || '';
    setMergeModalTargets(targets);
    setCustomMergeQnum(defaultLabel);
    setShowMergeModal(true);
  };

  // 모달에서 수정된 merge_qnum으로 일괄 묶기 실행
  const executeMergeWithCustomValue = async () => {
    const finalMergeQnum = customMergeQnum.trim();
    if (!finalMergeQnum) {
      showToast('통합 문항번호(merge_qnum)를 입력하세요');
      return;
    }
    if (!mergeModalTargets.length) return;

    const data = {};
    mergeModalTargets.forEach(item => {
      data[item.id] = finalMergeQnum;
    });

    const res = await apiRequest('/pro_list/allmerge', { user, projectnum, data });
    if (res && res.success === '777') {
      setShowMergeModal(false);
      handleClearSel();
      if (mergeMode) exitMergeMode();
      fetchQuestionList();
      showToast(`${mergeModalTargets.length}개 문항을 '${finalMergeQnum}'(으)로 묶었습니다`);
    }
  };

  // 일괄 묶기 (하단 컨트롤바에서 일괄 묶기 버튼 클릭 시 모달 오픈)
  const handleBulkMerge = () => {
    handleOpenMergeModal();
  };

  // 묶기 실행 (트레이 등에서)
  const doMerge = () => {
    handleOpenMergeModal();
  };

  // 마우스 업 전역 릴리즈 처리
  useEffect(() => {
    const handleGlobalMouseUp = () => {
      if (isMouseDownRef.current) {
        isMouseDownRef.current = false;
        setDragStartIdx(null);
        setDragCurrentIdx(null);
      }
    };
    window.addEventListener('mouseup', handleGlobalMouseUp);
    return () => window.removeEventListener('mouseup', handleGlobalMouseUp);
  }, []);

  // 마우스 드래그 시작
  const handleRowMouseDown = (e, index, itemInfo) => {
    if (e.target.closest('button, input, select, textarea, a, .pl-ck, .pl-pck, .pl-tg, .pl-lk, .pl-chev, .pl-mgadd, .pl-btn-unmerge')) {
      return;
    }
    isMouseDownRef.current = true;
    setDragStartIdx(index);
    setDragCurrentIdx(index);
    dragInitialSelRef.current = new Set(sel);
    dragInitialGSelRef.current = new Set(gsel);

    if (itemInfo.type === 'group') {
      const key = itemInfo.key;
      setGsel(prev => {
        const next = new Set(prev);
        if (next.has(key)) next.delete(key); else next.add(key);
        return next;
      });
    } else if (itemInfo.id) {
      // useYN === '머지' 또는 child 행이면 선택 불가
      if (itemInfo.type === 'child' || itemInfo.item?.useYN === '머지') return;
      const id = itemInfo.id;
      setSel(prev => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
      });
    }
  };

  // 마우스 드래그 진행
  const handleRowMouseEnter = (index, flatList) => {
    if (!isMouseDownRef.current || dragStartIdx === null) return;
    setDragCurrentIdx(index);

    const start = Math.min(dragStartIdx, index);
    const end = Math.max(dragStartIdx, index);
    const rangeItems = flatList.slice(start, end + 1);

    setSel(() => {
      const next = new Set(dragInitialSelRef.current);
      rangeItems.forEach(row => {
        // 묶음(머지) 문항이나 child 행은 선택 대상에서 제외
        if (row.type !== 'group' && row.type !== 'child' && row.item?.useYN !== '머지' && row.id) {
          next.add(row.id);
        }
      });
      return next;
    });

    setGsel(() => {
      const next = new Set(dragInitialGSelRef.current);
      rangeItems.forEach(row => {
        if (row.type === 'group' && row.key) next.add(row.key);
      });
      return next;
    });
  };

  // ============= (5) 묶기 모드 =============

  // 묶기 모드 진입: "묶기" 버튼 클릭
  const startMergeFrom = (item) => {
    setMergeMode(true);
    setGsel(new Set());
    setSel(new Set([item.id]));
  };

  // 묶기 모드에서 문항 선택/해제
  const handleMergePick = (id) => {
    setSel(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };



  // 묶기 모드 종료
  const exitMergeMode = () => {
    setMergeMode(false);
    setSel(new Set());
    setGsel(new Set());
  };

  // 풀기: merge_qnum을 qnum_text + "_" + mapid 기준 업데이트 하여 중복없이 분리 + useYN 복원
  const handleUnmerge = async (groupItems) => {
    const data = {};
    groupItems.forEach((item) => {
      const baseText = item.qnum_text || item.qnum || 'Q';
      const mapIdVal = (item.mapid !== undefined && item.mapid !== null) ? item.mapid : item.id;
      data[item.id] = `${baseText}_${mapIdVal}`;
    });

    const res = await apiRequest('/pro_list/allmerge', { user, projectnum, data });
    if (res && res.success === '777') {
      // useYN이 '머지'인 문항들을 '분석'으로 되돌림
      for (const item of groupItems) {
        if (item.useYN === '머지') {
          await apiRequest('/pro_list/analysis', {
            user, projectnum,
            qid: item.id,
            qnum: item.qnum || item.qnum_text,
            column: 'useYN',
            value: '1'
          });
        }
      }
      fetchQuestionList();
      showToast(`묶음을 해제했습니다 (${groupItems.length}개 문항 qnum_text_mapid 분리 완료)`);
    }
  };

  // 묶음 그룹 펼침/접힘
  const toggleGroup = (groupKey) => {
    setExpandedGroups(prev => {
      const next = new Set(prev);
      if (next.has(groupKey)) next.delete(groupKey); else next.add(groupKey);
      return next;
    });
  };

  // ============= 검색 필터 + 그룹 빌드 (ASP.NET 정렬: 1순위 그룹 min(mapid), 2순위 mapid) =============

  // 1. merge_qnum 그룹별 mapid 최솟값 (group_min_mapid) 사전 계산
  const minMapIdMap = {};
  qList.forEach(item => {
    const key = item.merge_qnum || `single_${item.id}`;
    const m = Number(item.mapid ?? item.id ?? 0);
    if (minMapIdMap[key] === undefined || m < minMapIdMap[key]) {
      minMapIdMap[key] = m;
    }
  });

  // 2. 검색 필터 및 1순위: group_min_mapid, 2순위: mapid 정렬
  const filteredQList = [...qList].filter(item => {
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return (item.qnum || '').toLowerCase().includes(q) ||
           (item.qnum_text || '').toLowerCase().includes(q) ||
           (item.question_fin || '').toLowerCase().includes(q);
  }).sort((a, b) => {
    const keyA = a.merge_qnum || `single_${a.id}`;
    const keyB = b.merge_qnum || `single_${b.id}`;
    const minA = minMapIdMap[keyA] ?? Number(a.mapid ?? a.id ?? 0);
    const minB = minMapIdMap[keyB] ?? Number(b.mapid ?? b.id ?? 0);
    if (minA !== minB) return minA - minB;
    return Number(a.mapid ?? a.id ?? 0) - Number(b.mapid ?? b.id ?? 0);
  });

  // 묶음 그룹화 로직 — merge_qnum 값이 동일한 문항끼리 묶음, 1개면 단독
  const buildGroupedRows = () => {
    const groups = {};
    const order = [];
    const seen = new Set();

    filteredQList.forEach(item => {
      const mq = item.merge_qnum || '';

      if (mq) {
        // merge_qnum이 있으면 해당 값 기준으로 그룹에 추가
        if (!groups[mq]) groups[mq] = [];
        groups[mq].push(item);
        if (!seen.has(mq)) {
          seen.add(mq);
          order.push({ type: 'group', key: mq });
        }
      } else {
        order.push({ type: 'single', item });
      }
    });

    return order.map(entry => {
      if (entry.type === 'single') return { type: 'single', item: entry.item };
      const items = groups[entry.key] || [];
      // 1개 문항만 있으면 단독 행 처리
      if (items.length <= 1) {
        return { type: 'single', item: items[0] };
      }
      // 그룹 내 mapid 순으로 정렬
      items.sort((a, b) => Number(a.mapid ?? a.id ?? 0) - Number(b.mapid ?? b.id ?? 0));
      // 그룹 head: useYN이 '머지'가 아닌 첫 문항, 없으면 첫 번째
      const head = items.find(i => i.useYN !== '머지') || items[0];
      return { type: 'group', key: entry.key, head, children: items };
    });
  };

  const groupedRows = buildGroupedRows();

  // 드래그 범위를 매핑하기 위한 평탄화된 가시 행 목록
  const getFlatRowList = () => {
    const list = [];
    groupedRows.forEach(entry => {
      if (entry.type === 'single') {
        list.push({ type: 'single', id: entry.item.id, item: entry.item });
      } else {
        list.push({ type: 'group', id: entry.key, key: entry.key, head: entry.head });
        if (expandedGroups.has(entry.key)) {
          entry.children.forEach(child => {
            list.push({ type: 'child', id: child.id, item: child });
          });
        }
      }
    });
    return list;
  };
  const flatRowList = getFlatRowList();

  // 분석 대상 문항 목록 생성 (분석보기 전환 시 네비게이션용)
  const getAnalysisNavList = () => {
    const list = [];
    groupedRows.forEach(entry => {
      if (entry.type === 'single') {
        const item = entry.item;
        if (item.useYN === '분석') {
          list.push({
            qnum: item.merge_qnum || item.qnum,
            qnum_text: item.qnum_text || item.qnum,
            question_title: item.question_fin || item.question_origin || item.qnum,
            id: item.id,
            status: item.status_text || item.progress || item.status || '대기'
          });
        }
      } else {
        const { key, head, children } = entry;
        const isGrpAnalysis = head.useYN === '분석' || (children && children.some(c => c.useYN === '분석' || c.useYN === '포함'));
        if (isGrpAnalysis) {
          list.push({
            qnum: key,
            qnum_text: head.qnum_text || key,
            question_title: head.question_fin || head.question_origin || key,
            id: head.id,
            status: head.status_text || head.progress || head.status || '대기'
          });
        }
      }
    });
    return list;
  };

  // 한큐분석 대상 문항 목록 추출 (단독 문항 + 통합문항 대표 문항 중 '분석' 상태인 것만 추출)
  const getHanqTargetQuestions = () => {
    const list = [];
    groupedRows.forEach(entry => {
      if (entry.type === 'single') {
        const item = entry.item;
        if (item && item.useYN === '분석') {
          list.push({
            id: item.id,
            qnum: item.merge_qnum || item.qnum,
            qnum_text: item.qnum_text || item.qnum,
            question_title: item.question_fin || item.question_origin || item.qnum,
            isGroup: false
          });
        }
      } else if (entry.type === 'group') {
        const { key, head, children } = entry;
        const isGrpAnalysis = head.useYN === '분석' || (children && children.some(c => c.useYN === '분석' || c.useYN === '포함'));
        if (isGrpAnalysis) {
          list.push({
            id: head.id,
            qnum: key,
            qnum_text: head.qnum_text || key,
            question_title: head.question_fin || head.question_origin || key,
            isGroup: true,
            key
          });
        }
      }
    });
    return list;
  };

  const handleStartHanqAnalysis = () => {
    const targets = getHanqTargetQuestions();
    if (targets.length === 0) {
      showToast('한큐분석 대상인 [분석] 상태의 문항이 없습니다.');
      return;
    }
    setShowHanqProgressModal(true);
  };

  const handleOpenAnalysis = (item, isGroup = false, key = '') => {
    if (!onOpenAnalysis) return;
    const navList = getAnalysisNavList();
    const targetQnum = isGroup ? key : (item.merge_qnum || item.qnum);
    const target = {
      qnum: targetQnum,
      qnum_text: isGroup ? key : (item.qnum_text || item.qnum),
      question_title: isGroup ? (item.question_fin || item.question_origin || key) : (item.question_fin || item.question_origin || targetQnum),
      id: item.id,
      status: item.status_text || item.progress || item.status || '대기'
    };
    onOpenAnalysis(target, navList);
  };

  // 집계 파생 값
  const diffCount = completedStats.diff_count || 0;
  const hasDiff = completedStats.has_diff;
  const isManual = completedStats.is_manual;
  const completedRatio = summaryInfo.completed_ratio ?? 0;

  // 묶기 모드 트레이 정보
  const mergeSelectedItems = mergeMode ? qList.filter(r => sel.has(r.id)) : [];
  const mergeLabels = [...new Set(mergeSelectedItems.map(r => r.qnum_text || r.qnum || ''))];
  const mergeFirstLabel = mergeSelectedItems.length > 0 ? (mergeSelectedItems[0].qnum_text || mergeSelectedItems[0].qnum || '') : '';

  // thead 체크박스 상태 (묶음/머지 문항 제외)
  const visibleSingleIds = filteredQList.filter(r => {
    const isMerged = r.useYN === '머지';
    return !isMerged;
  }).map(r => r.id);
  const allChecked = visibleSingleIds.length > 0 && visibleSingleIds.every(id => sel.has(id));
  const someChecked = visibleSingleIds.some(id => sel.has(id));
  const thCheckClass = allChecked ? 'on' : (someChecked ? 'part' : '');

  // Close dropdowns on outside click
  useEffect(() => {
    const handler = () => { setDdOut(false); setDdIn(false); };
    document.addEventListener('click', handler);
    return () => document.removeEventListener('click', handler);
  }, []);

  // ============= 행 렌더 헬퍼 =============

  const renderRow = (item, idx, isChild, isHidden, globalIdx = null) => {
    const isAnalysis = item.useYN === '분석';
    const isMerge = item.useYN === '머지';
    const isEditable = item.project_lock === '수정가능' || item.project_lock === '수정' || item.project_lock === '가능';
    const hasFilter = (item.qnum_join_cnt && item.qnum_join_cnt !== '0') || item.filter_yn === '설정' || item.filter_yn === 'Y';
    const progressText = item.status_text || item.progress || item.status || '대기';
    const chipClass = getChipClass(progressText);

    const responseCount = item.status_cnt ?? item.response_count ?? 0;
    const analysisTarget = item.status_cnt_duplicated ?? item.analysis_target ?? 0;
    const analysisComplete = item.status_cnt_fin ?? item.analysis_complete ?? 0;
    const estimatedCost = item.tokens_text ?? item.estimated_cost ?? '0';
    const mergeQnum = mergeQnumMap[item.id] !== undefined ? mergeQnumMap[item.id] : (item.merge_qnum || '');
    const isSelected = sel.has(item.id);

    // === 묶기 모드 렌더 ===
    if (mergeMode && !isChild) {
      const eligible = (isAnalysis || isMerge) && !isChild;
      const alreadyGrouped = isMerge;
      const canPick = eligible && !alreadyGrouped;

      return (
        <tr
          key={`mm-${idx}`}
          className={isSelected ? 'pk' : (!canPick ? 'dis' : '')}
          onMouseDown={(e) => globalIdx !== null && handleRowMouseDown(e, globalIdx, { type: 'single', id: item.id, item })}
          onMouseEnter={() => globalIdx !== null && handleRowMouseEnter(globalIdx, flatRowList)}
        >
          <td className="sk" style={{ left: 0 }}>
            {canPick ? (
              <span className={`pl-pck${isSelected ? ' on' : ''}`} onClick={() => handleMergePick(item.id)}>
                <i className="ti ti-check" />
              </span>
            ) : (
              <span className="pl-pck lock" title="묶을 수 없는 문항"><i className="ti ti-minus" /></span>
            )}
          </td>
          <td className={`sk${isSelected ? ' accP' : ''}`} style={{ left: 42 }}>{item.id || ''}</td>
          <td className="sk" style={{ left: 92 }}>{item.model || '설문온'}</td>
          <td className="sk pl-mono" style={{ left: 162 }}>{item.qnum || ''}</td>
          <td className="sk" style={{ left: 262 }}><span className="pl-lbl">{item.qnum_text || ''}</span></td>
          <td className="sk pl-qt" style={{ left: 346, borderRight: '1px solid var(--line2)' }} title={item.question_fin || ''}>{item.question_fin || item.question_origin || '-'}</td>
          <td className="pl-num n3">{Number(responseCount).toLocaleString()}</td>
          <td className="pl-num n3">{analysisTarget ? Number(analysisTarget).toLocaleString() : <span className="pl-dim">—</span>}</td>
          <td className="pl-num n3">{analysisComplete ? Number(analysisComplete).toLocaleString() : <span className="pl-dim">—</span>}</td>
          <td>{isAnalysis ? <span className={`pl-chip ${chipClass}`}>{progressText}</span> : <span className="pl-chip exc">제외</span>}</td>
          <td></td><td className="pl-num pl-dim">—</td><td></td><td></td>
          <td>
            {isSelected ? <span className="pl-lbl" style={{ background: 'var(--brand)', color: '#fff', borderColor: 'var(--brand)' }}>선택됨</span>
            : canPick ? <span className="pl-dim" style={{ fontSize: '11px' }}>선택 가능</span>
            : <span className="pl-dim">—</span>}
          </td>
          <td></td>
        </tr>
      );
    }

    // === 자식(머지) 행 ===
    if (isChild) {
      return (
        <tr
          key={`ch-${idx}`}
          className={`ch${isHidden ? ' hide' : ''}`}
          onMouseDown={(e) => globalIdx !== null && handleRowMouseDown(e, globalIdx, { type: 'child', id: item.id, item })}
          onMouseEnter={() => globalIdx !== null && handleRowMouseEnter(globalIdx, flatRowList)}
        >
          <td className="sk" style={{ left: 0 }}>
            <i className="ti ti-stack-2 pl-chmark" title="묶음 소속 문항" />
          </td>
          <td className="sk accL pl-dim" style={{ left: 42 }}>{item.id || ''}</td>
          <td className="sk" style={{ left: 92 }}>{item.model || '설문온'}</td>
          <td className="sk pl-mono" style={{ left: 162 }}>{item.qnum || ''}</td>
          <td className="sk" style={{ left: 262 }}><span className="pl-lbl">{item.qnum_text || ''}</span></td>
          <td className="sk pl-qt" style={{ left: 346, borderRight: '1px solid var(--line2)' }} title={item.question_fin || item.question_origin || ''}>{item.question_fin || item.question_origin || '-'}</td>
          <td className="pl-num n3">{Number(responseCount).toLocaleString()}</td>
          <td className="pl-num n3"></td>
          <td className="pl-num n3"></td>
          <td><span className="pl-dim" style={{ fontSize: '11.5px' }}>통합 기준 처리</span></td>
          <td></td><td className="pl-num pl-dim">—</td><td></td><td></td><td></td><td></td>
        </tr>
      );
    }

    // === 단독 행 (일반 모드) ===
    return (
      <tr
        key={`row-${idx}`}
        className={`${isAnalysis ? '' : (isMerge ? '' : 'ex')}${isSelected ? ' sel' : ''}`}
        onMouseDown={(e) => globalIdx !== null && handleRowMouseDown(e, globalIdx, { type: 'single', id: item.id, item })}
        onMouseEnter={() => globalIdx !== null && handleRowMouseEnter(globalIdx, flatRowList)}
      >
        <td className="sk" style={{ left: 0 }}>
          {!isMerge ? (
            <span className={`pl-ck${isSelected ? ' on' : ''}`} onClick={() => handleTogRow(item.id)}>
              <i className="ti ti-check" />
            </span>
          ) : (
            <span className="pl-ck lock" title="묶음 문항 (선택 불가)" style={{ opacity: 0.35, cursor: 'not-allowed', background: 'var(--head)', borderColor: 'var(--line2)' }}>
              <i className="ti ti-minus" style={{ opacity: 1, color: 'var(--tx3)' }} />
            </span>
          )}
        </td>
        <td className="sk" style={{ left: 42 }}>{item.id || ''}</td>
        <td className="sk" style={{ left: 92 }}>{item.model || '설문온'}</td>
        <td className="sk pl-mono" style={{ left: 162 }}>{item.qnum || ''}</td>
        <td className="sk" style={{ left: 262 }}><span className="pl-lbl">{item.qnum_text || ''}</span></td>
        <td className="sk pl-qt" style={{ left: 346, borderRight: '1px solid var(--line2)' }} title={item.question_fin || ''}>{item.question_fin || item.question_origin || '-'}</td>
        <td className="pl-num n3">{Number(responseCount).toLocaleString()}</td>
        <td className="pl-num n3">{analysisTarget ? Number(analysisTarget).toLocaleString() : <span className="pl-dim">—</span>}</td>
        <td className="pl-num n3">{analysisComplete ? Number(analysisComplete).toLocaleString() : <span className="pl-dim">—</span>}</td>
        <td>
          {isAnalysis ? <span className={`pl-chip ${chipClass}`}>{progressText}</span>
          : isMerge ? <span className="pl-chip merge">묶음</span>
          : <span className="pl-chip exc">제외</span>}
        </td>
        <td>
          {(isAnalysis || isMerge) ? (
            <button className={`pl-fbtn${hasFilter ? ' set' : ''}`} onClick={() => handleOpenFilterModal(item.id, item.qnum, item.qnum_text)}>
              설정{hasFilter ? ` ${item.qnum_join_cnt || ''}` : ''}
            </button>
          ) : <span className="pl-dim">—</span>}
        </td>
        <td className="pl-num">{(isAnalysis || isMerge) && analysisTarget ? estimatedCost : <span className="pl-dim">—</span>}</td>
        <td>
          <span className="pl-tg">
            <span className={isAnalysis ? 'on' : ''} onClick={(e) => { e.stopPropagation(); handleToggleAnalysis(item, '1', false); }}>분석</span>
            <span className={(!isAnalysis && !isMerge) ? 'on ex' : ''} onClick={(e) => { e.stopPropagation(); handleToggleAnalysis(item, '0', false); }}>제외</span>
          </span>
        </td>
        <td>
          {(!isAnalysis && !isMerge) ? <span className="pl-dim">—</span>
          : progressText === '완료' ? <button className="pl-btn brl xs" onClick={() => handleOpenAnalysis(item, false)}>분석보기</button>
          : <button className="pl-btn br xs" onClick={() => handleOpenAnalysis(item, false)}>분석보기</button>}
        </td>
        <td>
          {(isAnalysis && !mergeQnum) ? (
            <button className="pl-mgadd" onClick={() => startMergeFrom(item)}><i className="ti ti-arrow-merge" /> 묶기</button>
          ) : mergeQnum ? (
            <span className="pl-mgname" title={mergeQnum}>{mergeQnum}</span>
          ) : <span className="pl-dim">—</span>}
        </td>
        <td>
          <span className={`pl-lk${isEditable ? ' open' : ''}`} onClick={(e) => { e.stopPropagation(); handleToggleEdit(item, null, false); }} title={isEditable ? '수정 가능 (O)' : '잠금 (X)'}>
            <i className={`ti ti-lock${isEditable ? '-open' : ''}`} />
          </span>
        </td>
      </tr>
    );
  };

  // ============= JSX 렌더링 =============

  return (
    <div className={`pl-app${isSidebarCollapsed ? ' sidebar-mini' : ''}`}>
      {/* ========== Sidebar ========== */}
      <aside className={`pl-aside${isSidebarCollapsed ? ' mini' : ''}`}>
        <div className="pl-brand">
          <i className="ti ti-home" onClick={onBackToProjects} title="홈 (프로젝트 목록)" />
          <span className="pl-brand-sep" />
          <span className="pl-brand-full">
            <i className="ti ti-menu-2" />
            <span className="lg"><em>AI</em>오픈분석</span>
          </span>
        </div>

        <div className="pl-prj" title={`${projectName || '프로젝트'} (${projectnum})`}>
          <div className="pl-prj-full">
            <span className="nm">{projectName || projectnum || '프로젝트'}</span>
            <span className="cd">{projectnum}</span>
            <div className="pl-sp" />
            <i className="ti ti-settings" />
          </div>
          <div className="pl-prj-mini" title={`${projectName || '프로젝트'} (${projectnum})`}>
            <i className="ti ti-folder" />
          </div>
        </div>

        <div className="pl-navt">분석 설정 <i className="ti ti-chevron-up" /></div>
        <div className="pl-nav">
          <div className="pl-nv on" title="문항 목록">
            <i className="ti ti-file-text" /><span>문항 목록</span>
          </div>
          <div className="pl-nv" title="분석" onClick={() => {
            const nav = getAnalysisNavList();
            if (nav.length > 0) {
              onOpenAnalysis && onOpenAnalysis(nav[0], nav);
            } else {
              showToast('분석 대상 문항이 없습니다.');
            }
          }}>
            <i className="ti ti-adjustments-alt" /><span>분석</span>
          </div>
          <div className="pl-nv" title="토큰 사용 내역" onClick={() => showToast('토큰 사용 내역 페이지는 준비 중입니다')}>
            <i className="ti ti-chart-bar" /><span>토큰 사용 내역</span>
          </div>
        </div>

        <div className="pl-sbf">
          <div className="pl-fcard" title="누적액 $ 360">
            <div className="pl-fcard-full" style={{ display: 'flex', alignItems: 'center', width: '100%', gap: '6px' }}>
              <span className="lb">누적액 $</span>
              <div className="pl-sp" />
              <span className="vl">360</span>
              <button className="btn-pill" onClick={() => showToast('사용량을 갱신했습니다')}>업데이트</button>
            </div>
            <div className="pl-fcard-mini" title="누적액: $360">
              <i className="ti ti-coin" />
            </div>
          </div>
          <div className="pl-fcard bal" title="OPEN AI 잔액: 약 $335.32">
            <div className="pl-fcard-full" style={{ display: 'flex', alignItems: 'center', width: '100%', gap: '6px' }}>
              <i className="ti ti-sparkles" style={{ color: 'var(--brand)' }} />
              <span>OPEN AI 잔액</span>
              <div className="pl-sp" />
              <span className="vl">약 $335.32</span>
            </div>
            <div className="pl-fcard-mini" title="OPEN AI 잔액: 약 $335.32">
              <i className="ti ti-sparkles" />
            </div>
          </div>
        </div>

        <div className="pl-usr" title={`${username}님`}>
          <i className="ti ti-user-circle" /><span>{username}님</span>
        </div>
      </aside>

      {/* ========== Main ========== */}
      <main className="pl-main">
        {/* Header */}
        <header className="pl-header">
          <span
            className="pl-collapse"
            onClick={() => setIsSidebarCollapsed(prev => !prev)}
            title={isSidebarCollapsed ? "사이드바 펼치기" : "사이드바 간소화"}
          >
            <i className={`ti ti-chevron-${isSidebarCollapsed ? 'right' : 'left'}`} />
          </span>
          <span className="pl-head-sep" />
          <h1>문항 목록</h1>
          <i className="ti ti-info-circle hi" title="문항 목록 도움말" />
          <div className="pl-sp" />

          {/* 한큐분석 범용 옵션 설정 톱니바퀴 버튼 */}
          <button
            type="button"
            className="pl-btn-icon-gear"
            onClick={() => setShowHanqOptionModal(true)}
            title="한큐분석 범용 옵션 설정 (톱니바퀴)"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: '34px',
              height: '32px',
              borderRadius: '7px',
              border: '1px solid #cbd5e1',
              background: '#fff',
              color: '#475569',
              cursor: 'pointer',
              fontSize: '15px',
              marginRight: '6px',
              transition: 'all 0.15s ease',
              boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
            }}
            onMouseEnter={e => { e.currentTarget.style.borderColor = '#818cf8'; e.currentTarget.style.color = '#4f46e5'; e.currentTarget.style.background = '#f5f3ff'; }}
            onMouseLeave={e => { e.currentTarget.style.borderColor = '#cbd5e1'; e.currentTarget.style.color = '#475569'; e.currentTarget.style.background = '#fff'; }}
          >
            <i className="ti ti-settings" style={{ fontSize: '17px' }} />
          </button>

          {/* 한큐분석 버튼 (눈에 띄는 보라/인디고 그라데이션) */}
          <button
            type="button"
            className="pl-btn-hanq"
            onClick={handleStartHanqAnalysis}
            title="문항목록의 모든 분석 대상 문항을 3단계(보기 ➔ 전체응답자 ➔ 빈셀)로 자동 완주합니다."
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '6px',
              height: '32px',
              padding: '0 13px',
              borderRadius: '7px',
              border: 'none',
              background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 50%, #7c3aed 100%)',
              color: '#fff',
              fontSize: '12.5px',
              fontWeight: 700,
              cursor: 'pointer',
              marginRight: '8px',
              boxShadow: '0 2px 7px rgba(99,102,241,0.35)',
              transition: 'all 0.18s ease'
            }}
            onMouseEnter={e => { e.currentTarget.style.transform = 'translateY(-1px)'; e.currentTarget.style.boxShadow = '0 4px 12px rgba(99,102,241,0.5)'; }}
            onMouseLeave={e => { e.currentTarget.style.transform = 'translateY(0)'; e.currentTarget.style.boxShadow = '0 2px 7px rgba(99,102,241,0.35)'; }}
          >
            <i className="ti ti-bolt" style={{ fontSize: '14px' }} />
            <span>한큐분석</span>
          </button>

          <div className={`pl-dd${ddOut ? ' on' : ''}`} onClick={e => e.stopPropagation()}>
            <button className="pl-btn bro" onClick={() => { setDdOut(!ddOut); setDdIn(false); }}>
              데이터추출 <i className="ti ti-chevron-down" />
            </button>
            <div className="pl-ddm">
              <button onClick={() => { setDdOut(false); handleExportLabels('export_lb_excel'); }}>보기추출(개발자용)</button>
              <button onClick={() => { setDdOut(false); handleExportLabels('export_lb_dp'); }}>보기추출(DP용)</button>
              <hr />
              <button onClick={() => { setDdOut(false); handleExportDataAll(); }}>응답추출(전체문항)</button>
            </div>
          </div>

          <div className={`pl-dd${ddIn ? ' on' : ''}`} onClick={e => e.stopPropagation()}>
            <button className="pl-btn br" onClick={() => { setDdIn(!ddIn); setDdOut(false); }}>
              데이터등록 <i className="ti ti-chevron-down" />
            </button>
            <div className="pl-ddm">
              <button onClick={() => { setDdIn(false); setShowProRegisterModal(true); }}>문항등록</button>
              <hr />
              <button onClick={() => { setDdIn(false); setShowLabelRegisterModal(true); }}>보기등록(전체문항)</button>
            </div>
          </div>
        </header>

        {/* Body */}
        <div className="pl-body">
          {/* 묶기 모드 안내 바 */}
          {mergeMode && (
            <div className="pl-mband">
              <span className="ic"><i className="ti ti-arrow-merge" /></span>
              <span>
                {mergeSelectedItems.length > 0
                  ? <><b>{mergeSelectedItems.length}개</b> 선택됨 — 문항번호가 <b>달라도</b> 하나로 묶을 수 있습니다</>
                  : <>묶을 문항을 자유롭게 선택하세요 · <b>문항번호가 달라도</b> 하나의 통합 문항으로 묶입니다</>}
              </span>
              <div className="pl-sp" />
              <button className="pl-btn" onClick={exitMergeMode}>취소</button>
            </div>
          )}

          <div className="pl-card">
            {/* Tool bar */}
            <div className="pl-tool">
              <div className="pl-kpi">
                <span className="k"><i className="ti ti-users" />총 완료자수</span>
                {(() => {
                  const c1 = completedStats.survey_count ?? 0;
                  const c2 = completedStats.surveyon_count ?? 0;
                  const c3 = completedStats.open_registered_count ?? 0;
                  const c4 = completedStats.ai_wait_count ?? 0;
                  const isAllMatch = completedStats.is_all_match ?? (isManual ? (c2 === c3) : (c1 === c2 && c2 === c3));

                  return (
                    <>
                      {!isManual ? (
                        <>
                          <span className="v">큐마DB <b>{c1.toLocaleString()}</b>명</span>
                          <span className="dot">·</span>
                        </>
                      ) : (
                        <>
                          <span className="v" style={{ color: 'var(--tx3)' }}>수동(NEW)</span>
                          <span className="dot">·</span>
                        </>
                      )}
                      <span className="v">설문온DB <b>{c2.toLocaleString()}</b>명</span>
                      <span className="dot">·</span>
                      <span className="v">AI오픈등록수 <b>{c3.toLocaleString()}</b>명</span>
                      <span className="dot">·</span>
                      <span
                        className={`v ${c4 >= 1 ? 'clickable-wait' : ''}`}
                        onClick={c4 >= 1 && !isDirectRegistering ? handleDirectDbEnter : undefined}
                        title={c4 >= 1 ? `클릭 시 [데이터등록 -> 문항등록 -> 등록]이 바로 실행되어 대기 중인 ${c4}명이 즉시 등록됩니다.` : "대기 중인 신규 응답자가 없습니다."}
                        style={c4 >= 1 ? {
                          cursor: isDirectRegistering ? 'wait' : 'pointer',
                          color: '#ea580c',
                          fontWeight: 700,
                          textDecoration: 'underline',
                          textUnderlineOffset: '3px',
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: 4
                        } : {}}
                      >
                        AI오픈대기수 <b>{c4.toLocaleString()}</b>명
                        {c4 >= 1 && (
                          <span style={{
                            fontSize: '10.5px',
                            background: '#ffedd5',
                            color: '#c2410c',
                            padding: '1px 6px',
                            borderRadius: '4px',
                            border: '1px solid #fed7aa',
                            fontWeight: 700,
                            textDecoration: 'none',
                            lineHeight: '14px',
                            boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                          }}>
                            {isDirectRegistering ? '등록 중...' : '등록하기'}
                          </span>
                        )}
                      </span>
                      <span
                        className={`pl-gap ${isAllMatch ? 'ok' : 'no'}`}
                        onClick={handleOpenPidDiffModal}
                        title={isAllMatch ? "큐마DB, 설문온DB, AI오픈등록수 완료자수가 일치합니다." : `수치 차이 발생 (큐마:${c1}명 / 설문온:${c2}명 / AI오픈:${c3}명 / 대기:${c4}명). 클릭하여 PID 비교분석`}
                        style={{ cursor: 'pointer' }}
                      >
                        {isAllMatch ? (
                          <>
                            <i className="ti ti-check" style={{ fontSize: 11, marginRight: 2 }} />
                            일치
                          </>
                        ) : (
                          <>
                            <i className="ti ti-alert-triangle" style={{ fontSize: 12, marginRight: 3 }} />
                            차이
                          </>
                        )}
                      </span>
                    </>
                  );
                })()}
                <span className="dv" />
                <span className="k"><i className="ti ti-list-details" />총 문항수</span>
                <span className="v"><b>{summaryInfo.total_qnum_count ?? qList.length}</b>개</span>
                <span className="dv" />
                <span className="k"><i className="ti ti-circle-check" />완료 문항수</span>
                <span className="v"><b>{summaryInfo.completed_qnum_count ?? 0}</b> / {summaryInfo.total_qnum_count ?? qList.length}개</span>
                <span className="pl-pbar"><span style={{ width: `${completedRatio}%` }} /></span>
                <span className="pl-kpct">{completedRatio}%</span>
              </div>
              <div className="pl-search">
                <i className="ti ti-search" />
                <input placeholder="문항명 · 문번호 · 문항번호" value={searchQuery} onChange={e => setSearchQuery(e.target.value)} />
              </div>
              <button className="pl-btn gh"><i className="ti ti-adjustments-horizontal" /></button>
            </div>

            {statusMsg && (
              <div style={{ padding: '8px 16px', fontSize: '12px', color: 'var(--warn)', background: 'var(--warn-weak)', borderBottom: '1px solid var(--warn-line)' }}>
                <i className="ti ti-alert-triangle" style={{ marginRight: 4 }} />{statusMsg}
              </div>
            )}

            {/* Table Wrapper */}
            <div className="pl-tw">
              <table className="pl-table">
                <colgroup>
                  <col style={{ width: '42px' }} /><col style={{ width: '50px' }} /><col style={{ width: '70px' }} /><col style={{ width: '100px' }} />
                  <col style={{ width: '84px' }} /><col style={{ width: '240px' }} />
                  <col style={{ width: '80px' }} /><col style={{ width: '88px' }} /><col style={{ width: '88px' }} />
                  <col style={{ width: '96px' }} /><col style={{ width: '68px' }} /><col style={{ width: '72px' }} />
                  <col style={{ width: '112px' }} /><col style={{ width: '86px' }} />
                  <col style={{ width: '152px' }} /><col style={{ width: '72px' }} />
                </colgroup>
                <thead>
                  <tr className="g1">
                    <th className="v sk" style={{ left: 0 }} />
                    <th className="v sk" style={{ left: 42 }} colSpan="5">VIEW</th>
                    <th className="n3" colSpan="3">응답 <i className="ti ti-arrow-narrow-right" /> 분석대상 <i className="ti ti-arrow-narrow-right" /> 완료</th>
                    <th className="v" colSpan="3" />
                    <th className="a" colSpan="2">ADMIN</th>
                    <th className="e" colSpan="2">EDIT</th>
                  </tr>
                  <tr className="g2">
                    <th className="sk" style={{ left: 0 }}>
                      <span className={`pl-ck ${thCheckClass}`} onClick={handleToggleAll}>
                        <i className="ti ti-check" />
                      </span>
                    </th>
                    <th className="sk" style={{ left: 42 }}>no</th>
                    <th className="sk" style={{ left: 92 }}>모델</th>
                    <th className="sk" style={{ left: 162 }}>문번호</th>
                    <th className="sk" style={{ left: 262 }}>문항번호</th>
                    <th className="sk" style={{ left: 346, textAlign: 'left', paddingLeft: 10, borderRight: '1px solid var(--line2)' }}>문항최종</th>
                    <th className="pl-num n3 pl-th2" title="응답 PID 수">응답수<span className="pl-thsub">PID · 명</span></th>
                    <th className="pl-num n3 pl-th2" title="중복제거 분석 대상">분석대상수<span className="pl-thsub">중복제거 · 건</span></th>
                    <th className="pl-num n3 pl-th2" title="분석 완료 건수">분석완료수<span className="pl-thsub">분석대상 중 · 건</span></th>
                    <th>진행상황</th>
                    <th>
                      필터문항
                      <div className="pl-hbtns">
                        <span className="pl-hb" onClick={() => handleOpenFilterModal('')} title="전체 분석문항 필터 일괄설정">일괄설정</span>
                      </div>
                    </th>
                    <th className="pl-num">예상비용</th>
                    <th className="a pl-th2">
                      분석 / 제외
                      <div className="pl-hbtns">
                        <span className="pl-hb" onClick={() => handleAllAnalysis('1')} title="전체 분석 대상으로 일괄 설정">분석</span>
                        <span className="pl-hb" onClick={() => handleAllAnalysis('0')} title="전체 분석 제외로 일괄 설정">제외</span>
                      </div>
                    </th>
                    <th className="a">분석보기</th>
                    <th className="e">
                      문항통합저장
                      <div className="pl-hbtns">
                        <span className="pl-hb" onClick={handleSaveAllMerge}>저장</span>
                      </div>
                    </th>
                    <th className="e pl-th2">
                      수정
                      <div className="pl-hbtns">
                        <span className="pl-hb" onClick={() => handleAllEdit('0')}>X</span>
                        <span className="pl-hb" onClick={() => handleAllEdit('1')}>O</span>
                      </div>
                    </th>
                  </tr>
                </thead>
                <tbody className={mergeMode ? 'mm' : ''}>
                  {groupedRows.length === 0 ? (
                    <tr>
                      <td colSpan="16" className="pl-empty">
                        {loading ? '문항 목록을 조회하는 중입니다...' : '등록된 문항이 없거나 프로젝트를 선택하세요.'}
                      </td>
                    </tr>
                  ) : (
                    (() => {
                      let globalIdxCounter = 0;
                      return groupedRows.map((entry, gIdx) => {
                        if (entry.type === 'single') {
                          const currentIdx = globalIdxCounter++;
                          return renderRow(entry.item, gIdx, false, false, currentIdx);
                        }
                        // 그룹 렌더
                        const { key, head, children } = entry;
                        const grpGlobalIdx = globalIdxCounter++;
                        const isExpanded = expandedGroups.has(key);
                        const totalResp = children.reduce((a, c) => a + Number(c.status_cnt ?? c.response_count ?? 0), 0);
                        const groupTgt = Number(head.status_cnt_duplicated ?? head.analysis_target ?? 0) ||
                          children.reduce((max, c) => Math.max(max, Number(c.status_cnt_duplicated ?? c.analysis_target ?? 0)), 0);
                        const groupDone = Number(head.status_cnt_fin ?? head.analysis_complete ?? 0) ||
                          children.reduce((max, c) => Math.max(max, Number(c.status_cnt_fin ?? c.analysis_complete ?? 0)), 0);
                        const headProgress = head.status_text || head.progress || head.status || '대기';
                        const headChip = getChipClass(headProgress);
                        const headEditable = head.project_lock === '수정가능' || head.project_lock === '수정' || head.project_lock === '가능';
                        const isGrpAnalysis = head.useYN === '분석' || children.some(c => c.useYN === '분석' || c.useYN === '머지');
                        const headFilter = (head.qnum_join_cnt && head.qnum_join_cnt !== '0') || head.filter_yn === '설정' || head.filter_yn === 'Y';
                        const isGrpSelected = gsel.has(key);

                        // 묶기 모드에서는 그룹 행 비활성
                        if (mergeMode) {
                          return (
                            <React.Fragment key={`grp-${key}-${gIdx}`}>
                              <tr className="grp dis">
                                <td className="sk" style={{ left: 0 }}><span className="pl-pck lock"><i className="ti ti-minus" /></span></td>
                                <td className="sk accL pl-dim" style={{ left: 42 }}>—</td>
                                <td className="sk" style={{ left: 92 }}>{head.model || '설문온'}</td>
                                <td className="sk pl-dim" style={{ left: 162 }}>—</td>
                                <td className="sk" style={{ left: 262 }}><span className="pl-lbl">{key}</span></td>
                                <td className="sk pl-qt" style={{ left: 346, borderRight: '1px solid var(--line2)' }}>
                                  <div className="pl-gq">
                                    <span className="pl-gbadge"><i className="ti ti-stack-2" />묶음 {children.length}</span>
                                    <span className="t">{head.question_fin || key}</span>
                                  </div>
                                </td>
                                <td className="pl-num n3">{totalResp.toLocaleString()}</td>
                                <td className="pl-num n3">{groupTgt ? groupTgt.toLocaleString() : <span className="pl-dim">—</span>}</td>
                                <td className="pl-num n3">{groupDone ? groupDone.toLocaleString() : <span className="pl-dim">—</span>}</td>
                                <td><span className={`pl-chip ${headChip}`}>{headProgress}</span></td>
                                <td></td><td className="pl-num pl-dim">—</td><td></td><td></td><td><span className="pl-dim">묶음</span></td><td></td>
                              </tr>
                            </React.Fragment>
                          );
                        }

                        const childRows = isExpanded ? children.map((child, cIdx) => {
                          const childGlobalIdx = globalIdxCounter++;
                          return renderRow(child, `${gIdx}-${cIdx}`, true, false, childGlobalIdx);
                        }) : [];

                        return (
                          <React.Fragment key={`grp-${key}-${gIdx}`}>
                            <tr
                              className={`grp${isGrpSelected ? ' sel' : ''}`}
                              onMouseDown={(e) => handleRowMouseDown(e, grpGlobalIdx, { type: 'group', key, id: head.id })}
                              onMouseEnter={() => handleRowMouseEnter(grpGlobalIdx, flatRowList)}
                            >
                              <td className="sk" style={{ left: 0 }}>
                                <span className={`pl-ck${isGrpSelected ? ' on' : ''}`} onClick={() => handleTogGroup(key)}>
                                  <i className="ti ti-check" />
                                </span>
                              </td>
                              <td className="sk accL pl-dim" style={{ left: 42 }}>—</td>
                              <td className="sk" style={{ left: 92 }}>{head.model || '설문온'}</td>
                              <td className="sk pl-dim" style={{ left: 162 }}>—</td>
                              <td className="sk" style={{ left: 262 }}><span className="pl-lbl">{key}</span></td>
                              <td className="sk pl-qt" style={{ left: 346, borderRight: '1px solid var(--line2)' }}>
                                <div className="pl-gq">
                                  <span className={`pl-chev${isExpanded ? ' op' : ''}`} onClick={() => toggleGroup(key)}>
                                    <i className="ti ti-chevron-right" />
                                  </span>
                                  <span className="pl-gbadge"><i className="ti ti-stack-2" />묶음 {children.length}</span>
                                  <span className="t">{head.question_fin || head.question_origin || key}</span>
                                </div>
                              </td>
                              <td className="pl-num n3">{totalResp.toLocaleString()}</td>
                              <td className="pl-num n3">{groupTgt ? groupTgt.toLocaleString() : <span className="pl-dim">—</span>}</td>
                              <td className="pl-num n3">{groupDone ? groupDone.toLocaleString() : <span className="pl-dim">—</span>}</td>
                              <td><span className={`pl-chip ${headChip}`}>{headProgress}</span></td>
                              <td>
                                <button className={`pl-fbtn${headFilter ? ' set' : ''}`} onClick={() => handleOpenFilterModal(head.id, head.qnum || head.merge_qnum || key, head.qnum_text || key)}>
                                  설정{headFilter ? ` ${head.qnum_join_cnt || ''}` : ''}
                                </button>
                              </td>
                              <td className="pl-num">{groupTgt ? (head.tokens_text ?? head.estimated_cost ?? '0') : <span className="pl-dim">—</span>}</td>
                              <td>
                                <span className="pl-tg">
                                  <span className={isGrpAnalysis ? 'on' : ''} onClick={(e) => { e.stopPropagation(); handleToggleAnalysis(head, '1', true, key); }}>분석</span>
                                  <span className={!isGrpAnalysis ? 'on ex' : ''} onClick={(e) => { e.stopPropagation(); handleToggleAnalysis(head, '0', true, key); }}>제외</span>
                                </span>
                              </td>
                              <td>
                                {headProgress === '완료' ? (
                                  <button className="pl-btn brl xs" onClick={() => handleOpenAnalysis(head, true, key)}>분석보기</button>
                                ) : (
                                  <button className="pl-btn br xs" onClick={() => handleOpenAnalysis(head, true, key)}>분석보기</button>
                                )}
                              </td>
                              <td>
                                <div className="pl-grpedit">
                                  <span className="pl-mgname" title={key}>{key}</span>
                                  <button className="pl-btn-unmerge" onClick={() => handleUnmerge(children)} title="묶음 해제">
                                    <i className="ti ti-arrows-split" />풀기
                                  </button>
                                </div>
                              </td>
                              <td>
                                <span className={`pl-lk${headEditable ? ' open' : ''}`} onClick={(e) => { e.stopPropagation(); handleToggleEdit(head, null, true, key); }} title={headEditable ? '수정 가능 (O)' : '잠금 (X)'}>
                                  <i className={`ti ti-lock${headEditable ? '-open' : ''}`} />
                                </span>
                              </td>
                            </tr>
                            {childRows}
                          </React.Fragment>
                        );
                      });
                    })()
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </main>

      {/* Toast */}
      <div className={`pl-toast${toastMsg ? ' on' : ''}`}>
        <i className="ti ti-circle-check" style={{ color: '#5FD3A6' }} />{toastMsg}
      </div>

      {/* ========== 일괄 액션 바 (시안 bulk bar) ========== */}
      <div className={`pl-bulk${(sel.size + gsel.size) > 0 && !mergeMode ? ' on' : ''}`}>
        <span className="n">{sel.size + gsel.size}개 선택</span>
        <span className="dv" />
        <button
          className="pl-btn br xs"
          disabled={getBulkTargets().filter(r => r.useYN !== '머지').length < 2}
          onClick={handleBulkMerge}
        >
          <i className="ti ti-arrow-merge" />일괄 묶기
        </button>
        <span className="dv" />
        <button className="pl-btn bro xs" onClick={handleOpenFinEditModal}><i className="ti ti-pencil" />문항최종 수정</button>
        <span className="dv" />
        <span className="blb">분석</span>
        <button className="pl-btn xs" onClick={() => handleBulkMode('an')}><i className="ti ti-circle-check" />분석 대상</button>
        <button className="pl-btn xs" onClick={() => handleBulkMode('ex')}><i className="ti ti-ban" />분석 제외</button>
        <span className="dv" />
        <span className="blb">수정</span>
        <button className="pl-btn xs" onClick={() => handleBulkLock(false)}><i className="ti ti-lock" />잠금 X</button>
        <button className="pl-btn xs" onClick={() => handleBulkLock(true)}><i className="ti ti-lock-open" />가능 O</button>
        <span className="dv" />
        <button className="pl-btn red xs" onClick={handleBulkDelete}><i className="ti ti-trash" />삭제</button>
        <span className="dv" />
        <button className="pl-btn gh xs" onClick={handleClearSel}><i className="ti ti-x" /></button>
      </div>

      {/* ========== 묶기 트레이 (시안 tray) ========== */}
      <div className={`pl-tray${mergeMode ? ' on' : ''}`}>
        <div className="tt">
          <i className="ti ti-arrow-merge" style={{ color: 'var(--brand)' }} />묶는 중
          <span className="lblnow">{mergeFirstLabel || '—'}</span>
        </div>
        <div className="chips">
          {mergeSelectedItems.length > 0 ? mergeSelectedItems.map(r => (
            <span className="pl-tchip" key={r.id}>
              <b style={{ color: 'var(--info)', fontWeight: 700 }}>{r.qnum_text || r.qnum}</b> {r.qnum}
              <i className="ti ti-x" onClick={() => handleMergePick(r.id)} />
            </span>
          )) : <span className="hint">아래에서 문항을 선택하세요</span>}
        </div>
        <span className="hint">
          {mergeSelectedItems.length === 0 ? '2개 이상 선택하세요'
          : mergeSelectedItems.length === 1 ? '1개 더 선택하면 묶을 수 있습니다'
          : `${mergeSelectedItems.length}개 선택됨`}
        </span>
        <span className="dv" />
        <button className="pl-btn br" disabled={mergeSelectedItems.length < 2} onClick={doMerge}>
          묶기 <i className="ti ti-arrow-right" />
        </button>
        <button className="pl-btn" onClick={exitMergeMode}>취소</button>
      </div>

      {/* ========== 문항 통합저장 (merge_qnum 수정) 모달 ========== */}
      {showMergeModal && (
        <div className="modal-overlay" onClick={() => setShowMergeModal(false)}>
          <div className="modal-content" style={{ maxWidth: '620px', borderRadius: 14 }} onClick={e => e.stopPropagation()}>
            <div className="modal-header" style={{ padding: '20px 24px 16px', borderBottom: '1px solid var(--line)' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <i className="ti ti-arrow-merge" style={{ fontSize: 22, color: 'var(--brand)' }} />
                <div>
                  <h3 className="modal-title" style={{ fontSize: 18, color: 'var(--tx)' }}>문항 통합저장</h3>
                  <div style={{ fontSize: '12px', color: 'var(--tx3)', marginTop: 2 }}>
                    선택한 {mergeModalTargets.length}개 문항의 통합 문항번호(merge_qnum)를 확인하고 수정할 수 있습니다.
                  </div>
                </div>
              </div>
              <div className="pl-sp" />
              <button className="modal-close-btn" onClick={() => setShowMergeModal(false)}><i className="ti ti-x" /></button>
            </div>

            <div style={{ padding: '20px 24px' }}>
              <div style={{ marginBottom: 18 }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: 700, marginBottom: 8, color: 'var(--tx)' }}>
                  통합 문항번호 (merge_qnum) <span style={{ color: 'var(--brand)' }}>*</span>
                </label>
                <input
                  type="text"
                  className="tx"
                  style={{
                    width: '100%',
                    padding: '10px 14px',
                    fontSize: '14px',
                    fontWeight: 600,
                    borderRadius: 8,
                    border: '1.5px solid var(--brand)',
                    fontFamily: 'ui-monospace, Consolas, monospace',
                    background: '#FFF8F4'
                  }}
                  value={customMergeQnum}
                  onChange={e => setCustomMergeQnum(e.target.value)}
                  placeholder="예: SQ1_Group, A1-1 등"
                  autoFocus
                />
                <div style={{ fontSize: '11.5px', color: 'var(--tx3)', marginTop: 6 }}>
                  * 이 번호로 묶여 저장되며, 코드화 결과도 입력한 통합 문항번호를 기준으로 묶입니다.
                </div>
              </div>

              <div style={{ marginBottom: 6 }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: 700, marginBottom: 8, color: 'var(--tx2)' }}>
                  묶을 선택 문항 ({mergeModalTargets.length}개)
                </label>
                <div style={{
                  maxHeight: 180,
                  overflowY: 'auto',
                  background: 'var(--head)',
                  border: '1px solid var(--line)',
                  borderRadius: 8,
                  padding: '8px 12px'
                }}>
                  {mergeModalTargets.map((item, idx) => (
                    <div key={item.id || idx} style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 10,
                      padding: '6px 0',
                      borderBottom: idx < mergeModalTargets.length - 1 ? '1px solid var(--line)' : 'none',
                      fontSize: '12.5px'
                    }}>
                      <span style={{ fontFamily: 'ui-monospace, Consolas, monospace', fontWeight: 700, color: 'var(--brand)', width: 80 }}>
                        {item.qnum_text || item.qnum}
                      </span>
                      <span style={{ flex: 1, color: 'var(--tx)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {item.question_fin || item.question_origin || '-'}
                      </span>
                      <span style={{ fontSize: '11.5px', color: 'var(--tx3)' }}>
                        응답 {(item.status_cnt ?? item.response_count ?? 0).toLocaleString()}명
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <div className="modal-footer" style={{ padding: '14px 24px', background: 'var(--head)', borderTop: '1px solid var(--line)', display: 'flex', alignItems: 'center' }}>
              <div className="pl-sp" />
              <button className="pl-btn" style={{ padding: '8px 16px', marginRight: 8 }} onClick={() => setShowMergeModal(false)}>취소</button>
              <button className="pl-btn br" style={{ padding: '8px 22px', fontWeight: 700 }} onClick={executeMergeWithCustomValue}>
                <i className="ti ti-check" style={{ marginRight: 4 }} />묶어서 저장
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ========== PID Diff Modal ========== */}
      {showPidDiffModal && (
        <div className="modal-overlay" onClick={() => setShowPidDiffModal(false)}>
          <div className="modal-content" style={{ maxWidth: '650px' }} onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <i className="ti ti-search" style={{ fontSize: 18, color: 'var(--info)' }} />
              <div>
                <h3 className="modal-title">완료자 PID 비교분석</h3>
                <div className="sub">[{projectnum}]</div>
              </div>
              <div className="pl-sp" />
              <button className="modal-close-btn" onClick={() => setShowPidDiffModal(false)}><i className="ti ti-x" /></button>
            </div>
            <div style={{ display: 'flex', gap: 10, background: 'var(--head)', padding: '10px 16px', fontSize: '12px', margin: '0 18px 14px', borderRadius: 8, flexWrap: 'wrap' }}>
              <span>큐마DB 완료: <b>{pidDiffData.survey_count || 0}</b>명</span>
              <span style={{ color: 'var(--line2)' }}>|</span>
              <span>설문온DB 완료: <b>{pidDiffData.surveyon_count || 0}</b>명</span>
              <span style={{ color: 'var(--line2)' }}>|</span>
              <span>AI오픈등록수: <b>{pidDiffData.open_registered_count || 0}</b>명</span>
              <span style={{ color: 'var(--line2)' }}>|</span>
              <span>AI오픈대기수: <b style={{ color: ((pidDiffData.ai_wait_count ?? pidDiffData.unregistered_open?.length ?? 0) > 0) ? '#ea580c' : 'inherit' }}>{pidDiffData.ai_wait_count ?? (pidDiffData.unregistered_open?.length || 0)}</b>명</span>
              <span style={{ color: 'var(--line2)' }}>|</span>
              <span style={{ color: (pidDiffData.diff_count || 0) > 0 ? 'var(--warn)' : 'var(--ok)', fontWeight: 700 }}>
                차이 건수: <b>{pidDiffData.diff_count || 0}</b>건
              </span>
            </div>
            <div style={{ display: 'flex', borderBottom: '1px solid var(--line)', margin: '0 18px', gap: 0, overflowX: 'auto' }}>
              {[
                { key: 'unregistered_open', label: 'AI오픈 대기 PID', count: pidDiffData.unregistered_open?.length || 0 },
                { key: 'survey', label: '큐마DB 전용', count: pidDiffData.only_in_survey?.length || 0 },
                { key: 'surveyon', label: '설문온DB 전용', count: pidDiffData.only_in_surveyon?.length || 0 },
                { key: 'only_in_open', label: '오픈 전용', count: pidDiffData.only_in_open?.length || 0 }
              ].map(t => (
                <span key={t.key}
                  style={{
                    padding: '10px 14px',
                    fontSize: '12.5px',
                    fontWeight: 600,
                    cursor: 'pointer',
                    whiteSpace: 'nowrap',
                    borderBottom: activePidTab === t.key ? '2px solid var(--brand)' : '2px solid transparent',
                    color: activePidTab === t.key ? 'var(--brand)' : 'var(--tx2)'
                  }}
                  onClick={() => setActivePidTab(t.key)}
                >
                  {t.label} ({t.count})
                </span>
              ))}
            </div>
            <div className="modal-body" style={{ maxHeight: 280, margin: '0 18px', padding: '14px 0' }}>
              {(() => {
                let list = [];
                let emptyHint = "전용 PID가 없습니다. (일치)";
                if (activePidTab === 'unregistered_open') {
                  list = pidDiffData.unregistered_open || [];
                  emptyHint = "AI오픈 대기 PID가 없습니다. (모든 오픈 응답자 등록 완료)";
                } else if (activePidTab === 'survey') {
                  list = pidDiffData.only_in_survey || [];
                  emptyHint = "큐마DB 전용 PID가 없습니다. (설문온DB와 일치)";
                } else if (activePidTab === 'surveyon') {
                  list = pidDiffData.only_in_surveyon || [];
                  emptyHint = "설문온DB 전용 PID가 없습니다. (큐마DB와 일치)";
                } else if (activePidTab === 'only_in_open') {
                  list = pidDiffData.only_in_open || [];
                  emptyHint = "오픈 전용 PID가 없습니다. (설문온 완료자 범위 내 전원 구성)";
                }

                return list.length > 0 ? (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, fontSize: '12px', fontFamily: 'ui-monospace, Consolas, monospace' }}>
                    {list.map((pid, i) => (
                      <span key={i} style={{ background: 'var(--head)', padding: '3px 9px', borderRadius: 5, border: '1px solid var(--line)' }}>{pid}</span>
                    ))}
                  </div>
                ) : (
                  <div style={{ width: '100%', textAlign: 'center', padding: 32, color: 'var(--tx3)' }}>{emptyHint}</div>
                );
              })()}
            </div>
            <div className="modal-footer"><div className="pl-sp" /><button className="pl-btn br" onClick={() => setShowPidDiffModal(false)}>닫기</button></div>
          </div>
        </div>
      )}

      {/* ========== Label / Export Result Modal ========== */}
      {showLabelModal && (
        <div className="modal-overlay" onClick={() => setShowLabelModal(false)}>
          <div className="modal-content" style={{ maxWidth: '800px', borderRadius: 16 }} onClick={e => e.stopPropagation()}>
            <div className="modal-header" style={{ padding: '20px 24px 16px', borderBottom: '1px solid var(--line)' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <i className="ti ti-download" style={{ fontSize: 22, color: 'var(--brand)' }} />
                <div>
                  <h3 className="modal-title" style={{ fontSize: 18 }}>{modalTitle}</h3>
                  <div style={{ fontSize: '12px', color: 'var(--tx3)', marginTop: 2 }}>
                    [{projectnum}] 추출 데이터 미리보기 및 파일 다운로드
                  </div>
                </div>
              </div>
              <div className="pl-sp" />
              <button
                className="pl-btn br"
                style={{ padding: '7px 16px', fontSize: '13px', fontWeight: 700 }}
                onClick={() => handleDownloadExportFile(exportType)}
              >
                <i className="ti ti-file-download" style={{ marginRight: 4 }} />
                {exportType === 'export_lb_dp' ? 'DP 스크립트 파일 다운로드 (.txt)' : '엑셀 파일 다운로드 (.xlsx)'}
              </button>
              <button className="modal-close-btn" style={{ marginLeft: 8 }} onClick={() => setShowLabelModal(false)}><i className="ti ti-x" /></button>
            </div>
            <div className="modal-body" style={{ padding: '20px 24px' }}>
              <pre style={{
                background: 'var(--head)', padding: 16, borderRadius: 8,
                color: 'var(--tx)', fontSize: '12px', maxHeight: 420, overflowY: 'auto',
                border: '1px solid var(--line)', fontFamily: 'ui-monospace, Consolas, monospace',
                whiteSpace: 'pre-wrap', wordBreak: 'break-all'
              }}>{modalContent}</pre>
            </div>
            <div className="modal-footer" style={{ padding: '14px 24px', background: 'var(--head)', borderTop: '1px solid var(--line)', display: 'flex', alignItems: 'center' }}>
              <span style={{ fontSize: '12px', color: 'var(--tx3)' }}>
                * 상단 버튼을 클릭하여 생성된 파일(.xlsx / .txt)을 컴퓨터로 다운로드할 수 있습니다.
              </span>
              <div className="pl-sp" />
              <button className="pl-btn" style={{ padding: '7px 18px' }} onClick={() => setShowLabelModal(false)}>닫기</button>
            </div>
          </div>
        </div>
      )}

      {/* ========== Filter Modal ========== */}
      {showFilterModal && (
        <div className="modal-overlay" onClick={() => setShowFilterModal(false)}>
          <div className="modal-content" style={{ maxWidth: '640px', borderRadius: 16, border: '1px solid #E5E7EB', boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1)' }} onClick={e => e.stopPropagation()}>
            <div className="modal-header" style={{ padding: '22px 26px 16px', borderBottom: 'none' }}>
              <h3 className="modal-title" style={{ fontSize: 19, fontWeight: 700, color: '#111827' }}>
                필터문항 {filterTargetTitle || filterTargetQnum ? <span style={{ color: '#f97316', fontWeight: 700, marginLeft: 4 }}>설정 [{filterTargetTitle || filterTargetQnum}]</span> : <span style={{ color: '#f97316', fontWeight: 700, marginLeft: 4 }}>전체 일괄설정</span>}
              </h3>
              <div className="pl-sp" />
              <button className="modal-close-btn" onClick={() => setShowFilterModal(false)}><i className="ti ti-x" style={{ fontSize: 20, color: '#4B5563' }} /></button>
            </div>
            <div style={{ padding: '0 26px 20px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, background: '#F8F9FA', border: '1px solid #E5E7EB', borderRadius: 10, padding: '10px 14px', marginBottom: 16 }}>
                <i className="ti ti-search" style={{ fontSize: 18, color: '#9CA3AF' }} />
                <input
                  placeholder="변수명 또는 문항을 검색하세요."
                  value={filterSearch}
                  onChange={e => setFilterSearch(e.target.value)}
                  style={{ flex: 1, border: 'none', background: 'transparent', outline: 'none', fontSize: '13.5px', color: '#1F2937' }}
                />
              </div>
              
              <div style={{ borderTop: '2px solid #1E3A2B', borderBottom: '1px solid #EAEDF0', marginBottom: 16 }}>
                <div style={{ display: 'flex', alignItems: 'center', padding: '10px 14px', background: '#F8F9FB', borderBottom: '1px solid #EAEDF0' }}>
                  <span
                    className={`pl-ck${filteredFilterQList.length > 0 && filteredFilterQList.every(q => filterChecked[q.qnum_variable || q.qnum || q.qnum_text]) ? ' on' : ''}`}
                    onClick={handleToggleAllFilter}
                    style={{ cursor: 'pointer', marginRight: 14 }}
                  >
                    <i className="ti ti-check" />
                  </span>
                  <span style={{ flex: 1, textAlign: 'center', fontWeight: 700, fontSize: '13.5px', color: '#1F2937' }}>변수명 / 문항</span>
                  <span style={{ textAlign: 'right', fontWeight: 700, fontSize: '13.5px', color: '#1F2937', minWidth: 90 }}>
                    타입<br />
                    <em style={{ fontSize: '11px', fontWeight: 600, color: '#E0403C', fontStyle: 'normal' }}>복수, 순위 제외</em>
                  </span>
                </div>
                <div style={{ maxHeight: 340, overflowY: 'auto' }}>
                  {filteredFilterQList.length > 0 ? filteredFilterQList.map((q, i) => {
                    const vKey = q.qnum_variable || q.qnum || q.qnum_text;
                    const questionText = q.qnum_question || q.question_fin || q.question_origin || '';
                    const qType = q.qnum_type || q.question_type || q.type || 'single';
                    const displayType = qType.toLowerCase().includes('open') || qType.toLowerCase().includes('string') ? 'open(문자)' : qType;

                    return (
                      <div
                        className={`filter-item${filterChecked[vKey] ? ' on' : ''}`}
                        key={q.qnum_id || vKey || i}
                        onClick={() => handleFilterCheck(vKey)}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          padding: '12px 14px',
                          borderBottom: '1px solid #EAEDF0',
                          cursor: 'pointer',
                          background: filterChecked[vKey] ? '#FFF7ED' : '#FFFFFF',
                          transition: 'background 0.15s'
                        }}
                      >
                        <span className={`pl-ck${filterChecked[vKey] ? ' on' : ''}`} style={{ marginRight: 14 }}>
                          <i className="ti ti-check" />
                        </span>
                        <span style={{ flex: 1, fontSize: '13px', color: '#1F2937', lineHeight: 1.4, paddingRight: 12 }}>
                          {vKey && (
                            <span style={{
                              fontFamily: 'Consolas, monospace',
                              background: '#EEF2F6',
                              color: '#1E3A2B',
                              fontWeight: 700,
                              fontSize: '11.5px',
                              padding: '2px 7px',
                              borderRadius: '4px',
                              border: '1px solid #D0D7DE',
                              marginRight: '8px',
                              display: 'inline-block'
                            }}>
                              {vKey}
                            </span>
                          )}
                          {questionText}
                        </span>
                        <span style={{ fontSize: '12.5px', color: '#4B5563', minWidth: 90, textAlign: 'right' }}>
                          {displayType}
                        </span>
                      </div>
                    );
                  }) : <div style={{ padding: 48, textAlign: 'center', color: '#9CA3AF', fontSize: 13 }}>검색 결과가 없습니다.</div>}
                </div>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', paddingTop: 4 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <b style={{ fontSize: '13.5px', color: '#111827' }}>선택된 문항 ({checkedCount}/5)</b>
                  <span style={{ fontSize: '12.5px', color: '#6B7280', maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {checkedCount === 0 ? '선택된 문항이 없습니다.' : Object.keys(filterChecked).join(', ')}
                  </span>
                </div>
                <button
                  style={{
                    fontSize: '14px',
                    fontWeight: 700,
                    padding: '10px 24px',
                    borderRadius: 8,
                    background: '#f97316',
                    color: '#ffffff',
                    border: 'none',
                    cursor: 'pointer',
                    boxShadow: '0 1px 2px rgba(0,0,0,0.05)'
                  }}
                  onClick={handleSaveFilter}
                >
                  선택 완료
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
      {/* ========== 문항최종 일괄 수정 모달 ========== */}
      {showFinEditModal && (
        <div className="modal-overlay" onClick={() => setShowFinEditModal(false)}>
          <div className="modal-content" style={{ maxWidth: '820px', borderRadius: 16 }} onClick={e => e.stopPropagation()}>
            <div className="modal-header" style={{ padding: '20px 26px 16px', borderBottom: '1px solid var(--line)' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <i className="ti ti-pencil" style={{ fontSize: 22, color: 'var(--brand)' }} />
                <div>
                  <h3 className="modal-title" style={{ fontSize: 18, color: 'var(--tx)' }}>문항최종 일괄 수정</h3>
                  <div style={{ fontSize: '12px', color: 'var(--tx3)', marginTop: 2 }}>
                    선택한 <b>{finEditList.length}개 문항</b>의 문항최종 레이블을 확인하고 개별 또는 일괄 도구로 수정할 수 있습니다.
                  </div>
                </div>
              </div>
              <div className="pl-sp" />
              <button className="modal-close-btn" onClick={() => setShowFinEditModal(false)}><i className="ti ti-x" /></button>
            </div>

            <div style={{ padding: '16px 26px' }}>
              {/* 일괄 편집 도구 바 (Find & Replace, Prefix/Suffix, Undo) */}
              <div style={{ background: 'var(--head)', border: '1px solid var(--line)', borderRadius: 10, padding: '12px 16px', marginBottom: 16 }}>
                <div style={{ fontSize: '12.5px', fontWeight: 700, color: 'var(--tx)', marginBottom: 8, display: 'flex', alignItems: 'center', gap: 6 }}>
                  <i className="ti ti-adjustments-horizontal" style={{ color: 'var(--brand)' }} />
                  <span>일괄 편집 도구</span>
                  <div className="pl-sp" />
                  <button type="button" className="pl-btn gh xs" onClick={handleFinReset} title="최초 레이블로 원복">
                    <i className="ti ti-rotate-clockwise" /> 원래대로 초기화
                  </button>
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'center' }}>
                  {/* 찾아 바꾸기 */}
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, flex: 1, minWidth: 260 }}>
                    <span style={{ fontSize: '11.5px', color: 'var(--tx2)', whiteSpace: 'nowrap' }}>찾을 단어:</span>
                    <input
                      type="text"
                      className="tx"
                      style={{ padding: '5px 8px', fontSize: '12px', width: 100, borderRadius: 6, border: '1px solid var(--line2)' }}
                      value={finFindText}
                      onChange={e => setFinFindText(e.target.value)}
                      placeholder="찾을 텍스트"
                    />
                    <span style={{ fontSize: '11.5px', color: 'var(--tx2)', whiteSpace: 'nowrap' }}>바꿀 단어:</span>
                    <input
                      type="text"
                      className="tx"
                      style={{ padding: '5px 8px', fontSize: '12px', width: 100, borderRadius: 6, border: '1px solid var(--line2)' }}
                      value={finReplaceText}
                      onChange={e => setFinReplaceText(e.target.value)}
                      placeholder="바꿀 텍스트"
                    />
                    <button type="button" className="pl-btn br xs" onClick={handleFinBatchReplace} style={{ padding: '5px 10px' }}>
                      치환
                    </button>
                  </div>

                  <span style={{ color: 'var(--line2)' }}>|</span>

                  {/* 일괄 적용 문구 */}
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, flex: 1, minWidth: 260 }}>
                    <span style={{ fontSize: '11.5px', color: 'var(--tx2)', whiteSpace: 'nowrap' }}>일괄 적용 문구:</span>
                    <input
                      type="text"
                      className="tx"
                      style={{ padding: '5px 8px', fontSize: '12px', flex: 1, borderRadius: 6, border: '1px solid var(--line2)' }}
                      value={finBulkText}
                      onChange={e => setFinBulkText(e.target.value)}
                      placeholder="일괄 적용할 레이블 문구 입력"
                    />
                    <button type="button" className="pl-btn bro xs" onClick={handleFinApplyBulkText} style={{ padding: '5px 12px', whiteSpace: 'nowrap' }}>
                      일괄 적용
                    </button>
                  </div>
                </div>
              </div>

              {/* 1:1 편집 테이블 */}
              <div style={{ maxHeight: 320, overflowY: 'auto', border: '1px solid var(--line)', borderRadius: 10 }}>
                <table className="pl-table" style={{ width: '100%', tableLayout: 'fixed' }}>
                  <colgroup>
                    <col style={{ width: '80px' }} />
                    <col style={{ width: '90px' }} />
                    <col style={{ width: '100px' }} />
                    <col style={{ width: 'auto' }} />
                  </colgroup>
                  <thead>
                    <tr style={{ background: 'var(--head)', fontSize: '12px', height: 36 }}>
                      <th style={{ padding: '6px 10px', textAlign: 'center' }}>문번호</th>
                      <th style={{ padding: '6px 10px', textAlign: 'center' }}>문항번호</th>
                      <th style={{ padding: '6px 10px', textAlign: 'center' }}>통합그룹</th>
                      <th style={{ padding: '6px 12px', textAlign: 'left' }}>문항최종 (수정)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {finEditList.map((item, idx) => (
                      <tr key={item.id} style={{ borderBottom: idx < finEditList.length - 1 ? '1px solid var(--line)' : 'none' }}>
                        <td style={{ textAlign: 'center', fontFamily: 'ui-monospace, Consolas, monospace', fontSize: '12px', color: 'var(--tx2)' }}>
                          {item.qnum || '-'}
                        </td>
                        <td style={{ textAlign: 'center', fontFamily: 'ui-monospace, Consolas, monospace', fontWeight: 700, fontSize: '12.5px', color: 'var(--brand)' }}>
                          {item.qnum_text || '-'}
                        </td>
                        <td style={{ textAlign: 'center', fontSize: '11.5px', color: 'var(--tx3)' }}>
                          {item.merge_qnum ? <span className="pl-mgname" style={{ fontSize: '11px' }}>{item.merge_qnum}</span> : '—'}
                        </td>
                        <td style={{ padding: '6px 10px' }}>
                          <input
                            type="text"
                            style={{
                              width: '100%',
                              padding: '6px 10px',
                              fontSize: '13px',
                              borderRadius: 6,
                              border: '1px solid var(--line2)',
                              background: item.question_fin !== item.original_fin ? '#FFF8F4' : '#FFF',
                              borderColor: item.question_fin !== item.original_fin ? 'var(--brand)' : 'var(--line2)',
                              color: 'var(--tx)'
                            }}
                            value={item.question_fin}
                            onChange={e => handleFinItemChange(item.id, e.target.value)}
                          />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="modal-footer" style={{ padding: '14px 26px', background: 'var(--head)', borderTop: '1px solid var(--line)', display: 'flex', alignItems: 'center' }}>
              <span style={{ fontSize: '12px', color: 'var(--tx3)' }}>
                * 저장 시 선택한 문항의 문항최종 레이블이 일괄 업데이트됩니다.
              </span>
              <div className="pl-sp" />
              <button className="pl-btn" style={{ padding: '8px 18px', marginRight: 8 }} onClick={() => setShowFinEditModal(false)}>취소</button>
              <button className="pl-btn br" style={{ padding: '8px 24px', fontWeight: 700 }} onClick={handleSaveFinBatch}>
                <i className="ti ti-check" style={{ marginRight: 4 }} />저장
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 문항 등록 모달 */}
      <ProRegisterModal
        isOpen={showProRegisterModal}
        onClose={() => setShowProRegisterModal(false)}
        projectnum={projectnum}
        proname={projectName || ''}
        server={selectedServername || 'rps'}
        pof={selectedPof || (qList.find(i => i.projectpof)?.projectpof) || ''}
        user={user}
        onSuccess={fetchQuestionList}
      />

      {/* 보기 등록 (전체문항) 엑셀 모달 */}
      <ProLabelRegisterModal
        isOpen={showLabelRegisterModal}
        onClose={() => setShowLabelRegisterModal(false)}
        projectnum={projectnum}
        user={user}
        onSuccess={fetchQuestionList}
      />

      {/* 한큐분석 범용 옵션 설정 팝업 모달 */}
      {showHanqOptionModal && (
        <HanqOptionModal
          projectnum={projectnum}
          user={user}
          onClose={() => setShowHanqOptionModal(false)}
          onSaveSuccess={() => {
            showToast('한큐분석 범용 옵션이 성공적으로 저장되었습니다.');
          }}
        />
      )}

      {/* 한큐분석 실시간 진행 대시보드 모달 */}
      {showHanqProgressModal && (
        <HanqProgressModal
          projectnum={projectnum}
          user={user}
          targetQuestions={getHanqTargetQuestions()}
          onClose={() => setShowHanqProgressModal(false)}
          onCompleteAll={() => {
            showToast('한큐분석 파이프라인이 성공적으로 완료되었습니다.');
            fetchQuestionList();
          }}
        />
      )}
    </div>
  );
}
