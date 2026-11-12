import React, { useState, useEffect } from 'react';
import { apiRequest } from '../api/client';

export default function QmProjectModal({ user, onClose, onRegisterSuccess }) {
  const [search, setSearch] = useState('');
  const [qmList, setQmList] = useState([]);
  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [msg, setMsg] = useState('');

  const fetchQmList = async () => {
    setLoading(true);
    setMsg('');
    const res = await apiRequest('/Project', {
      gb: 'qmlist',
      user: user,
      search: search,
      gubun: '1'
    });
    setLoading(false);

    if (res && res.success === '777') {
      try {
        const parsed = typeof res.output === 'string' ? JSON.parse(res.output) : (res.output || []);
        setQmList(parsed);
      } catch {
        setQmList([]);
      }
    } else {
      setMsg('큐마 목록을 불러오지 못했습니다.');
    }
  };

  useEffect(() => {
    fetchQmList();
  }, []);

  const handleSelectProject = async (item) => {
    setActionLoading(true);
    setMsg('');
    const res = await apiRequest('/Project', {
      gb: 'qmlistselect',
      projectnum: item.projectnum,
      pof: item.pof,
      questionnaireName: item.questionnaireName,
      servername: (item.servername || '').toLowerCase(),
      user: user
    });
    setActionLoading(false);

    if (res && res.success === '777') {
      alert('큐마 프로젝트가 성공적으로 등록되었습니다!');
      onRegisterSuccess();
      onClose();
    } else {
      setMsg((res && res.message) || '큐마 프로젝트 등록 실패');
    }
  };

  return (
    <div className="modal-overlay">
      <div className="modal-content">
        <div className="modal-header">
          <h3 className="modal-title">큐마 (RPS/QM) 프로젝트 목록 불러오기</h3>
          <button className="modal-close-btn" onClick={onClose}>✕</button>
        </div>

        {/* Search Header */}
        <div style={{ display: 'flex', gap: '10px', marginBottom: '20px' }}>
          <input
            type="text"
            className="form-input"
            style={{ flex: 1 }}
            placeholder="프로젝트명, POF, 번호 검색..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && fetchQmList()}
          />
          <button className="btn-primary" onClick={fetchQmList} disabled={loading}>
            {loading ? '검색 중...' : '검색'}
          </button>
        </div>

        {msg && (
          <div style={{ background: '#fef2f2', border: '1px solid #fecaca', color: '#dc2626', padding: '10px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.85rem' }}>
            {msg}
          </div>
        )}

        {/* Qm List Table */}
        <div style={{ maxHeight: '420px', overflowY: 'auto' }}>
          <table className="table-light">
            <thead>
              <tr>
                <th>서버</th>
                <th>프로젝트 번호</th>
                <th>POF</th>
                <th>프로젝트명</th>
                <th>제작자/담당자</th>
                <th>등록상태</th>
                <th>작동</th>
              </tr>
            </thead>
            <tbody>
              {qmList.length === 0 ? (
                <tr className="empty-row">
                  <td colSpan="7">
                    {loading ? '큐마 프로젝트 목록 조회 중...' : '조회된 큐마 프로젝트가 없습니다.'}
                  </td>
                </tr>
              ) : (
                qmList.map((item, idx) => (
                  <tr key={idx}>
                    <td>
                      <span className={`badge ${item.servername === 'qm' ? 'badge-amber' : 'badge-blue'}`}>
                        {(item.servername || 'RPS').toUpperCase()}
                      </span>
                    </td>
                    <td className="td-pn">{item.projectnum}</td>
                    <td>{item.pof}</td>
                    <td className="td-name">{item.questionnaireName}</td>
                    <td className="td-muted">{item.user아이디 || item.questionnairePersonName}</td>
                    <td>
                      <span className={`badge ${item.gubunYN === '등록' ? 'badge-green' : 'badge-gray'}`}>
                        {item.gubunYN || '미등록'}
                      </span>
                    </td>
                    <td>
                      <button
                        className="btn-orange"
                        style={{ padding: '5px 12px', fontSize: '0.78rem' }}
                        disabled={actionLoading}
                        onClick={() => handleSelectProject(item)}
                      >
                        선택 등록
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
