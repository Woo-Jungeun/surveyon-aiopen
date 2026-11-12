import React, { useState } from 'react';
import { apiRequest } from '../api/client';

export default function NewProjectModal({ user, onClose, onCreateSuccess }) {
  const [pof, setPof] = useState('2026-07-0001');
  const [projectname, setProjectname] = useState('');
  const [loading, setLoading] = useState(false);
  const [msg, setMsg] = useState('');

  const handleCreate = async (e) => {
    e.preventDefault();
    if (!pof || !projectname) {
      setMsg('POF 번호와 프로젝트명을 모두 입력해주세요.');
      return;
    }

    setLoading(true);
    setMsg('');

    const res = await apiRequest('/Project', {
      gb: 'NewProject',
      pof,
      projectname,
      user
    });

    setLoading(false);

    if (res && res.success === '777') {
      const pnum = (res.output && res.output.projectnum) || (res.contents && res.contents.projectnum) || '';
      alert(`신규 프로젝트가 성공적으로 생성을 완료했습니다! (프로젝트 번호: ${pnum})`);
      onCreateSuccess();
      onClose();
    } else {
      setMsg((res && res.message) || '신규 프로젝트 생성에 실패했습니다.');
    }
  };

  return (
    <div className="modal-overlay">
      <div className="modal-content" style={{ maxWidth: '520px' }}>
        <div className="modal-header">
          <h3 className="modal-title">신규 프로젝트 생성</h3>
          <button className="modal-close-btn" onClick={onClose}>✕</button>
        </div>

        {msg && (
          <div style={{ background: '#fef2f2', border: '1px solid #fecaca', color: '#dc2626', padding: '10px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.85rem' }}>
            {msg}
          </div>
        )}

        <form onSubmit={handleCreate}>
          <div className="form-group">
            <label className="form-label">POF 번호</label>
            <input
              type="text"
              className="form-input"
              value={pof}
              onChange={(e) => setPof(e.target.value)}
              placeholder="예: 2026-07-0001"
              required
            />
          </div>

          <div className="form-group" style={{ marginBottom: '24px' }}>
            <label className="form-label">프로젝트명</label>
            <input
              type="text"
              className="form-input"
              value={projectname}
              onChange={(e) => setProjectname(e.target.value)}
              placeholder="프로젝트 제목을 입력하세요"
              required
            />
          </div>

          <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
            <button type="button" className="btn-secondary" onClick={onClose} disabled={loading}>
              취소
            </button>
            <button type="submit" className="btn-orange" disabled={loading} style={{ padding: '8px 20px' }}>
              {loading ? '생성 중...' : '프로젝트 생성'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
