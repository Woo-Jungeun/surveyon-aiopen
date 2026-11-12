import * as signalR from '@microsoft/signalr';
import { apiRequest, API_BASE, INTERNAL_KEY } from './client';

/**
 * AI오픈분석 › 분석 페이지 API 래퍼
 * - /analysis/info: 문항 설정 및 API 키 목록, 국가/모델 코드
 * - /analysis/responses: 응답 데이터 (호출 시 NewOldCheck 수행)
 * - /analysis/codes: 보기 데이터 목록
 * - /analysis/rawdata: 원시 응답 데이터 목록
 * - /analysis/status: 분석/완료 상태
 * - /analysis/popup_check: 팝업 확인 플래그 조회 및 해제
 * - /analysis/save/info: 문항 설정 저장
 * - /analysis/save/codes: 보기 목록 저장
 * - /analysis/save/responses: 응답 목록 및 중복코드제거 저장
 * - /analysis/save/compare: DB 반영 비교
 * - /analysis/worker/start: worker_0427 / python 작업 실행
 * - /analysis/worker/status: worker 로그/상태 폴링
 * - /analysis/worker/stop: worker 프로세스 중지
 * - /hubs/worker: worker 실시간 로그 및 상태값 스트리밍 (SignalR)
 */

export async function getAnalysisInfo(projectnum, qnum, user, signal) {
  return await apiRequest('/analysis/info', { projectnum, qnum, user }, { signal });
}

export async function getAnalysisResponses(projectnum, qnum, signal) {
  return await apiRequest('/analysis/responses', { projectnum, qnum }, { signal });
}

export async function getAnalysisCodes(projectnum, qnum, signal) {
  return await apiRequest('/analysis/codes', { projectnum, qnum }, { signal });
}

export async function getAnalysisRawdata(projectnum, qnum, signal) {
  return await apiRequest('/analysis/rawdata', { projectnum, qnum }, { signal });
}

export async function getAnalysisStatus(projectnum, qnum, qid, signal) {
  return await apiRequest('/analysis/status', { projectnum, qnum, qid }, { signal });
}

export async function checkAnalysisPopup(projectnum, qnum, qid, checkyn = '0', signal) {
  return await apiRequest('/analysis/popup_check', { projectnum, qnum, qid, checkyn }, { signal });
}

export async function saveAnalysisInfo(projectnum, qnum, data, user, signal) {
  return await apiRequest('/analysis/save/info', { projectnum, qnum, user, data }, { signal });
}

export async function saveAnalysisCodes(projectnum, qnum, data, user, lvcode, signal) {
  return await apiRequest('/analysis/save/codes', { projectnum, qnum, user, data, lvcode }, { signal });
}

export async function saveAnalysisResponses(projectnum, qnum, duplicate_answer, data, user, lvcode, signal) {
  return await apiRequest('/analysis/save/responses', { projectnum, qnum, user, duplicate_answer, data, lvcode }, { signal });
}

export async function compareAnalysisResponses(projectnum, qnum, data, user, signal) {
  return await apiRequest('/analysis/save/compare', { projectnum, qnum, user, data }, { signal });
}

export async function startAnalysisWorker(projectnum, qnum, action, user, options = {}, signal) {
  const payload = { projectnum, qnum, user, action, ...options };
  return await apiRequest('/analysis/worker/start', payload, { signal });
}

/**
 * SignalR WorkerHub 연결 인스턴스 생성
 * - /hubs/worker 엔드포인트 연결 (자동 재연결 지원)
 */
export function createWorkerHubConnection() {
  const token = localStorage.getItem('X-Auth-Token') || '';
  return new signalR.HubConnectionBuilder()
    .withUrl('/hubs/worker', {
      accessTokenFactory: () => token,
      transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}

export async function getAnalysisWorkerStatus(job, offset = 0, signal) {
  const token = localStorage.getItem('X-Auth-Token') || '';
  const url = `${API_BASE}/analysis/worker/status?job=${encodeURIComponent(job)}&offset=${offset}`;
  try {
    const res = await fetch(url, {
      method: 'GET',
      headers: {
        'X-Auth-Token': token,
        'Authorization': token ? `Bearer ${token}` : '',
        'X-Internal-Key': INTERNAL_KEY
      },
      signal
    });
    return await res.json();
  } catch (err) {
    if (err.name === 'AbortError') return { ok: false, error: 'aborted' };
    return { ok: false, error: err.message };
  }
}

export async function stopAnalysisWorker(jobId, projectnum, qnum, user, signal) {
  return await apiRequest('/analysis/worker/stop', { jobId, projectnum, qnum, user, action: jobId }, { signal });
}

/** Phase D: Aspose.Cells 엑셀 다운로드 (Blob 다운로드 처리) */
export async function downloadAnalysisExcel(projectnum, qnum, user) {
  const token = localStorage.getItem('X-Auth-Token') || '';
  const url = `${API_BASE}/analysis/export/excel?projectnum=${encodeURIComponent(projectnum)}&qnum=${encodeURIComponent(qnum)}&user=${encodeURIComponent(user || '')}`;
  const res = await fetch(url, {
    method: 'GET',
    headers: {
      'X-Auth-Token': token,
      'Authorization': token ? `Bearer ${token}` : '',
      'X-Internal-Key': INTERNAL_KEY
    }
  });

  if (!res.ok) {
    throw new Error(`엑셀 다운로드 실패 (상태코드: ${res.status})`);
  }

  const blob = await res.blob();
  const disposition = res.headers.get('content-disposition');
  let filename = `hrcopen_분석_${projectnum}_${qnum}.xlsx`;
  if (disposition && disposition.includes('filename=')) {
    const match = disposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
    if (match && match[1]) {
      filename = decodeURIComponent(match[1].replace(/['"]/g, ''));
    }
  }

  const downloadUrl = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = downloadUrl;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.URL.revokeObjectURL(downloadUrl);
  return { ok: true, filename };
}

/** Phase D: 엑셀 파일 업로드 및 미리보기 파싱 */
export async function previewExcelCodes(file, projectnum, qnum) {
  const token = localStorage.getItem('X-Auth-Token') || '';
  const formData = new FormData();
  formData.append('file', file);
  formData.append('projectnum', projectnum);
  formData.append('qnum', qnum);

  const res = await fetch(`${API_BASE}/analysis/import/excel-preview`, {
    method: 'POST',
    headers: {
      'X-Auth-Token': token,
      'Authorization': token ? `Bearer ${token}` : '',
      'X-Internal-Key': INTERNAL_KEY
    },
    body: formData
  });

  if (!res.ok) {
    throw new Error(`엑셀 업로드 실패 (상태코드: ${res.status})`);
  }

  return await res.json();
}

/** Phase D: 타 프로젝트/문항 검색 */
export async function searchImportProjects(search = '') {
  const token = localStorage.getItem('X-Auth-Token') || '';
  const url = `${API_BASE}/analysis/import/projects?search=${encodeURIComponent(search)}`;
  const res = await fetch(url, {
    method: 'GET',
    headers: {
      'X-Auth-Token': token,
      'Authorization': token ? `Bearer ${token}` : '',
      'X-Internal-Key': INTERNAL_KEY
    }
  });
  return await res.json();
}

/** Phase D: 타 문항 보기 목록 조회 */
export async function getImportCodes(targetPn, targetQnum) {
  const token = localStorage.getItem('X-Auth-Token') || '';
  const url = `${API_BASE}/analysis/import/codes?target_projectnum=${encodeURIComponent(targetPn)}&target_qnum=${encodeURIComponent(targetQnum)}`;
  const res = await fetch(url, {
    method: 'GET',
    headers: {
      'X-Auth-Token': token,
      'Authorization': token ? `Bearer ${token}` : '',
      'X-Internal-Key': INTERNAL_KEY
    }
  });
  return await res.json();
}

/** Phase D: 선택한 보기 목록 현재 문항에 반영 */
export async function applyImportCodes(projectnum, qnum, selectedCodes, user) {
  return await apiRequest('/analysis/import/apply', {
    projectnum,
    qnum,
    user,
    data: selectedCodes
  });
}

/** 한큐분석: 프로젝트 범용 옵션(option_json_all) 조회 */
export async function getGlobalOptionAll(projectnum, user, signal) {
  return await apiRequest('/pro_list/option_all', { projectnum, user }, { signal });
}

/** 한큐분석: 프로젝트 범용 옵션(option_json_all) 저장 */
export async function saveGlobalOptionAll(projectnum, user, settings, signal) {
  return await apiRequest('/pro_list/option_all/save', { projectnum, user, settings }, { signal });
}


