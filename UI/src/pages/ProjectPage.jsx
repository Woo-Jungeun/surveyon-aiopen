import React, { useState, useEffect, useRef } from 'react';
import { apiRequest } from '../api/client';
import ProgressBarModal from '../components/ProgressBarModal';
import QmProjectModal from '../components/QmProjectModal';
import NewProjectModal from '../components/NewProjectModal';

// 모든 중첩/래핑/JSON 문자열 구조를 자동 감지하여 프로젝트 배열을 안전하게 추출하는 범용 데이터 추출기
const extractProjectArray = (res) => {
  if (!res) return [];

  // 1. BaseController 응답 페이로드 (contents 또는 output 또는 res 자체)
  const payload = res.contents || res.output || res;
  if (Array.isArray(payload)) return payload;

  // 2. payload 내부의 resultjson / result / data / items 탐색
  let rawData = payload.resultjson ?? payload.result ?? payload.data ?? payload.items;
  if (!rawData && typeof payload === 'object') {
    rawData = payload;
  }

  // 3. 문자열(string)인 경우 안전하게 JSON.parse
  if (typeof rawData === 'string') {
    try {
      rawData = JSON.parse(rawData);
    } catch (e) {
      console.warn('[ProjectPage] JSON.parse failed for rawData:', e);
      return [];
    }
  }

  // 4. 1차 파싱 결과가 배열인 경우 즉시 반환
  if (Array.isArray(rawData)) return rawData;

  // 5. 객체 내부의 resultjson 이중 탐색
  if (rawData && typeof rawData === 'object') {
    if (Array.isArray(rawData.resultjson)) return rawData.resultjson;
    if (typeof rawData.resultjson === 'string') {
      try {
        const inner = JSON.parse(rawData.resultjson);
        if (Array.isArray(inner)) return inner;
      } catch {}
    }
  }

  return [];
};

export default function ProjectPage({ userInfo, onLogout, onSelectProject }) {
  const user = (userInfo && userInfo.userid) || 'syhong';
  const username = (userInfo && userInfo.username) || '홍성용';

  const [search, setSearch] = useState('');
  const [projectList, setProjectList] = useState([]);
  const [loading, setLoading] = useState(false);
  const [statusMsg, setStatusMsg] = useState('');
  const [activeProjectTab, setActiveProjectTab] = useState('select'); // 'select' | 'register'

  // Progress Bar State
  const [showProgress, setShowProgress] = useState(false);
  const [progressState, setProgressState] = useState({
    percent: 0,
    message: '',
    currentCount: 0,
    totalCount: 0
  });
  const pollingTimerRef = useRef(null);

  // 1. [gb: "list_start"] 동기화 작업 시작 요청
  const handleStartProjectList = async (forceNew = false) => {
    if (pollingTimerRef.current) {
      clearInterval(pollingTimerRef.current);
      pollingTimerRef.current = null;
    }

    setLoading(true);
    setStatusMsg('동기화 작업 시작 (list_start 호출 중)...');

    const res = await apiRequest('/Project', {
      gb: 'list_start',
      user: user,
      search: search,
      forceNew: forceNew
    });

    if (res && res.success === '777') {
      const output = res.output || res.contents || {};
      const percent = Number(output.percent ?? output.percenter ?? 0);
      const status = output.status || '';
      const checkProgressYn = Number(output.check_progress_yn ?? 0);

      // [완료 조건] percent >= 100 또는 status == "completed" 또는 check_progress_yn == 0 일 경우 목록 추출 및 표출
      const isCompleted = (percent >= 100) || (status === 'completed') || (checkProgressYn === 0);

      if (isCompleted) {
        const listData = extractProjectArray(res);
        setProjectList(listData);
        setShowProgress(false);
        setLoading(false);
        setStatusMsg(`조회 완료. 총 ${listData.length}건`);
      } else {
        // 동기화 진행 필요 -> 프로그래스바 구동 및 폴링 시작
        const jobId = output.jobId || '';
        setShowProgress(true);
        setProgressState({
          percent: percent,
          message: output.message || '프로젝트 동기화 작업 진행 중...',
          currentCount: output.currentCount || 0,
          totalCount: output.totalCount || 0
        });

        startProgressPolling(jobId);
      }
    } else {
      setLoading(false);
      setShowProgress(false);
      setStatusMsg((res && res.message) || '프로젝트 목록 조회에 실패했습니다.');
    }
  };

  // 2. [gb: "list_progress"] 1초 주기 폴링 함수
  const startProgressPolling = (jobId) => {
    pollingTimerRef.current = setInterval(async () => {
      const res = await apiRequest('/Project', {
        gb: 'list_progress',
        jobid: jobId,
        user: user
      });

      if (res && res.success === '777') {
        const output = res.output || res.contents || {};
        const percent = Number(output.percent ?? output.percenter ?? 0);
        const currentStatus = output.status || 'processing';

        setProgressState({
          percent: percent,
          message: output.message || '동기화 진행 중...',
          currentCount: output.currentCount || 0,
          totalCount: output.totalCount || 0
        });

        // [완료 조건] percent >= 100 또는 status == "completed"
        if (percent >= 100 || currentStatus === 'completed') {
          clearInterval(pollingTimerRef.current);
          pollingTimerRef.current = null;

          const listData = extractProjectArray(res);
          setProjectList(listData);
          setShowProgress(false);
          setLoading(false);
          setStatusMsg(`동기화 완료. 총 ${listData.length}건`);
        } else if (currentStatus === 'failed') {
          clearInterval(pollingTimerRef.current);
          pollingTimerRef.current = null;

          setShowProgress(false);
          setLoading(false);
          setStatusMsg('동기화 오류 발생: ' + (output.errorMessage || '작업 실패'));
        }
      }
    }, 1000);
  };

  // 3. [gb: "top_toggle"] 프로젝트 상단 고정 토글
  const handleToggleTop = async (e, pn) => {
    e.stopPropagation();
    if (!pn) return;

    // 클라이언트 낙관적 업데이트 (Optimistic UI Update)
    const currentItem = projectList.find(p => (p.merge_pn || p.projectnum || p.pn) === pn);
    const newTopYn = currentItem && currentItem.topyn === 'y' ? null : 'y';

    const updatedList = projectList.map(item => {
      const itemPn = item.merge_pn || item.projectnum || item.pn;
      if (itemPn === pn) {
        return { ...item, topyn: newTopYn };
      }
      return item;
    });

    // 상단고정('y' 우선) -> 등록일 내림차순 -> id 내림차순 정렬
    const sorted = [...updatedList].sort((a, b) => {
      const aTop = a.topyn === 'y' ? 0 : 1;
      const bTop = b.topyn === 'y' ? 0 : 1;
      if (aTop !== bTop) return aTop - bTop;

      const aDate = a.register_date || a.project_register_date || '';
      const bDate = b.register_date || b.project_register_date || '';
      if (aDate !== bDate) return bDate.localeCompare(aDate);

      const aId = Number(a.id || 0);
      const bId = Number(b.id || 0);
      return bId - aId;
    });

    setProjectList(sorted);

    try {
      const res = await apiRequest('/Project', {
        gb: 'top_toggle',
        user: user,
        projectnum: pn,
        topyn: newTopYn
      });

      if (!res || res.success !== '777') {
        console.warn('top_toggle failed, reloading list', res);
        handleStartProjectList(true);
      }
    } catch (err) {
      console.error('top_toggle error:', err);
      handleStartProjectList(true);
    }
  };

  useEffect(() => {
    handleStartProjectList(false);
    return () => {
      if (pollingTimerRef.current) {
        clearInterval(pollingTimerRef.current);
      }
    };
  }, []);

  // Modals State
  const [showQmModal, setShowQmModal] = useState(false);
  const [showNewModal, setShowNewModal] = useState(false);

  const handleLogoutClick = async () => {
    await apiRequest('/Login/logout');
    localStorage.removeItem('X-Auth-Token');
    localStorage.removeItem('userInfo');
    onLogout();
  };

  return (
    <div className="project-container">
      {/* Header: Title + Close */}
      <div className="project-header">
        <div className="project-header-title">
          <span className="icon">📋</span>
          프로젝트 선택
        </div>
        <button className="project-close-btn" onClick={handleLogoutClick} title="로그아웃">
          ✕
        </button>
      </div>

      {/* Tabs */}
      <div className="project-tabs">
        <button
          className={`project-tab ${activeProjectTab === 'select' ? 'active' : ''}`}
          onClick={() => setActiveProjectTab('select')}
        >
          📋 프로젝트 선택
          <span className="count-badge">{projectList.length || 0}</span>
        </button>
        <button
          className={`project-tab ${activeProjectTab === 'register' ? 'active' : ''}`}
          onClick={() => { setActiveProjectTab('register'); setShowNewModal(true); }}
        >
          + 프로젝트 등록
          <span className="count-badge">0</span>
        </button>
      </div>

      {/* Search Area */}
      <div className="project-search-wrap">
        <input
          type="text"
          className="project-search-input"
          placeholder="프로젝트명, 번호로 검색하세요."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleStartProjectList(true)}
        />
      </div>

      {/* Status Message */}
      {statusMsg && (
        <div style={{ padding: '0 32px 8px', fontSize: '0.8rem', color: '#6b7280' }}>
          {statusMsg}
        </div>
      )}

      {/* Project Table */}
      <div className="project-table-wrap">
        <table className="table-light">
          <thead>
            <tr>
              <th style={{ width: '45px', textAlign: 'center' }}>고정</th>
              <th style={{ width: '50px' }}>no</th>
              <th style={{ width: '110px' }}>프로젝트<br/>번호</th>
              <th style={{ width: '120px' }}>웹프로젝트<br/>번호</th>
              <th>프로젝트명</th>
              <th style={{ width: '80px' }}>등록자명</th>
              <th style={{ width: '110px' }}>등록일</th>
              <th style={{ width: '80px' }}>오픈<br/>서버정보</th>
              <th style={{ width: '80px' }}>오픈<br/>작업자</th>
              <th style={{ width: '110px' }}>업데이트<br/>일자</th>
              <th style={{ width: '70px' }}>작업현황</th>
              <th style={{ width: '70px' }}>DB<br/>완료수</th>
            </tr>
          </thead>
          <tbody>
            {projectList.length === 0 ? (
              <tr className="empty-row">
                <td colSpan="12">
                  {loading ? '프로젝트 동기화 작업 진행 중입니다...' : '등록된 프로젝트가 없습니다.'}
                </td>
              </tr>
            ) : (
              projectList.map((item, idx) => {
                const pn = item.merge_pn || item.projectnum || item.pn || '';
                const projectName = item.projectname || '';
                const statusText = item.project_status || item.status || '';
                const registerDate = item.register_date || item.project_register_date || '';
                const updateDate = item.update_date || '';
                const dbCount = item.postgrecompletecount || item.db_complete_count || '';
                const serverNameVal = (item.servername || 'rps').toLowerCase();
                const isTop = item.topyn === 'y';

                return (
                  <tr
                    key={idx}
                    className={isTop ? 'row-pinned' : ''}
                    onClick={() => onSelectProject && onSelectProject(pn, projectName, serverNameVal, item.projectpof || '')}
                  >
                    <td style={{ textAlign: 'center' }} onClick={(e) => handleToggleTop(e, pn)}>
                      <button
                        type="button"
                        className={`btn-pin ${isTop ? 'is-pinned' : 'is-unpinned'}`}
                        title={isTop ? '상단 고정 해제' : '상단 고정'}
                      >
                        📌
                      </button>
                    </td>
                    <td className="td-muted">{item.id || `1···`}</td>
                    <td className="td-pn">{item.projectpof || ''}</td>
                    <td className="td-pn">{pn}</td>
                    <td className="td-name">{projectName}</td>
                    <td>{item.register_userid || item.project_use_name || ''}</td>
                    <td className="td-muted">
                      {registerDate ? registerDate.split(' ')[0] : ''}
                      {registerDate && registerDate.split(' ')[1] && (
                        <><br/><span style={{ fontSize: '0.75rem' }}>{registerDate.split(' ')[1]}</span></>
                      )}
                    </td>
                    <td>
                      <span style={{ fontWeight: 500 }}>
                        {(item.servername || 'rps').toUpperCase()}
                      </span>
                    </td>
                    <td>{item.open_worker || ''}</td>
                    <td className="td-muted">
                      {updateDate ? updateDate.split(' ')[0] : ''}
                      {updateDate && updateDate.split(' ')[1] && (
                        <><br/><span style={{ fontSize: '0.75rem' }}>{updateDate.split(' ')[1]}</span></>
                      )}
                    </td>
                    <td className={statusText === '진행중' ? 'td-status-진행중' : 'td-status-완료'}>
                      {statusText || ''}
                    </td>
                    <td className="td-count">{dbCount || ''}</td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Footer */}
      <div className="app-footer">
        Hankook <span className="brand">Research</span> © 2026 설문온 SulmunOn. All rights reserved.
        <span style={{ float: 'right' }}><span className="status-dot"></span> 시스템 정상 운영중</span>
      </div>

      {/* 3. Progress Bar Modal */}
      {showProgress && (
        <ProgressBarModal
          percent={progressState.percent}
          message={progressState.message}
          currentCount={progressState.currentCount}
          totalCount={progressState.totalCount}
        />
      )}

      {/* Qm Modal */}
      {showQmModal && (
        <QmProjectModal
          user={user}
          onClose={() => setShowQmModal(false)}
          onRegisterSuccess={() => handleStartProjectList(true)}
        />
      )}

      {/* New Project Modal */}
      {showNewModal && (
        <NewProjectModal
          user={user}
          onClose={() => setShowNewModal(false)}
          onCreateSuccess={() => handleStartProjectList(true)}
        />
      )}
    </div>
  );
}
