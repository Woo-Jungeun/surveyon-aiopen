// 웹서버(APIs/o) 및 로컬 Vite Proxy(APIs/o -> localhost:5120) 이중화 경로 지원
export const API_BASE = (typeof __API_BASE__ !== 'undefined')
  ? __API_BASE__
  : (import.meta.env.VITE_API_BASE ?? (import.meta.env.MODE === 'production' ? '/APIs/o' : '/APIs/o'));

// appsettings.json 기반 InternalAuth 및 AES256 기본 키 정보
export const INTERNAL_KEY = 'cGZeWyxlli5N4FRSwMAsbbRoNzJEhFkocore';
export const AES_KEY = 'cGZeWyxlli5N4FRSwMAsbbRoNzJEhFko';
export const AES_IV  = 'cGZeWyxlli5N4FRS';

export async function apiRequest(endpoint, body = {}, customHeaders = {}, options = {}) {
  const token = localStorage.getItem('X-Auth-Token') || '';

  const { signal, ...restHeaders } = customHeaders || {};
  const activeSignal = options?.signal || signal;

  const headers = {
    'Content-Type': 'application/json',
    'X-Auth-Token': token,
    'Authorization': token ? `Bearer ${token}` : '',
    'X-Internal-Key': INTERNAL_KEY,
    ...restHeaders
  };

  try {
    const response = await fetch(`${API_BASE}${endpoint}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
      signal: activeSignal
    });

    if (!response.ok) {
      console.warn(`[API Response Status] ${endpoint}: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    if (error.name === 'AbortError') {
      return { success: '499', message: '요청이 취소되었습니다.' };
    }
    console.error(`[API Error] ${endpoint}:`, error);
    return { success: '761', message: 'API 서버(포트 5120) 연결에 실패했습니다. API 구동 상태를 확인하세요.' };
  }
}
