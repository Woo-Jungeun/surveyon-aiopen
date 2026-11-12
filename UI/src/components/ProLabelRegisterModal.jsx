import React, { useState } from 'react';
import { INTERNAL_KEY, API_BASE } from '../api/client';

export default function ProLabelRegisterModal({ isOpen, onClose, projectnum, user, onSuccess }) {
  const [file, setFile] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const [toastMsg, setToastMsg] = useState('');

  const showToast = (msg) => {
    setToastMsg(msg);
    setTimeout(() => setToastMsg(''), 4000);
  };

  if (!isOpen) return null;

  const handleFileChange = (e) => {
    const selected = e.target.files?.[0];
    if (selected) {
      setFile(selected);
    }
  };

  const handleImportSubmit = async () => {
    if (!file) {
      showToast('업로드할 보기 엑셀 파일(.xlsx)을 선택하세요.');
      return;
    }

    setSubmitting(true);
    const formData = new FormData();
    formData.append('file', file);
    formData.append('projectnum', projectnum || '');
    formData.append('user', user || '');

    try {
      const token = localStorage.getItem('X-Auth-Token') || '';
      const res = await fetch(`${API_BASE}/pro_list/import_lb_all_excel`, {
        method: 'POST',
        headers: {
          'X-Auth-Token': token,
          'Authorization': token ? `Bearer ${token}` : '',
          'X-Internal-Key': INTERNAL_KEY
        },
        body: formData
      });

      const data = await res.json();
      if (data && (data.success === '777' || data.code === '777')) {
        const payload = data.resultjson || data;
        const msg = payload.message || '보기 일괄 등록이 성공적으로 완료되었습니다.';
        showToast(msg);
        if (onSuccess) onSuccess();
        setTimeout(() => {
          onClose();
        }, 1200);
      } else {
        showToast((data && (data.message || data.Message)) || '보기 일괄 등록에 실패했습니다.');
      }
    } catch (err) {
      console.error(err);
      showToast('보기 엑셀 등록 중 오류가 발생했습니다.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="prm-overlay" onClick={onClose}>
      <div className="prm-modal" style={{ maxWidth: '780px' }} onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div className="prm-header">
          <h2>
            <i className="ti ti-file-spreadsheet" style={{ color: '#f97316' }} />
            보기등록 (전체문항) 엑셀 업로드
          </h2>
          <button className="prm-close" onClick={onClose}>
            <i className="ti ti-x" />
          </button>
        </div>

        {/* Body */}
        <div className="prm-body">
          {toastMsg && <div className="prm-toast">{toastMsg}</div>}

          {/* Format Guide with Visual Excel Grid */}
          <div style={{ background: '#F8FAFC', border: '1px solid #E2E8F0', borderRadius: 12, padding: '16px 20px', marginBottom: 20 }}>
            <div style={{ fontSize: '13.5px', fontWeight: 700, color: '#0F172A', marginBottom: 8, display: 'flex', alignItems: 'center', gap: 6 }}>
              <i className="ti ti-info-circle" style={{ color: '#f97316', fontSize: 18 }} />
              <span>엑셀 양식 구조 가이드 (HR_Sample_LB.xlsx)</span>
              <span style={{ fontSize: '11.5px', fontWeight: 500, color: '#64748B', marginLeft: 'auto' }}>
                * <b>3행부터</b> 실제 보기 항목 데이터가 수집됩니다.
              </span>
            </div>

            <div style={{ overflowX: 'auto', border: '1px solid #CBD5E1', borderRadius: 8, background: '#FFFFFF', marginTop: 10 }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '11.5px', textAlign: 'center' }}>
                <thead>
                  <tr style={{ background: '#F1F5F9', color: '#475569', height: 28 }}>
                    <th style={{ width: 45, borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1', background: '#E2E8F0' }}>구분</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>A (변수명)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>B (변수명2)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>C (문항)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>D (코드)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>E (라벨)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>F (1단계CD)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>G (1단계NM)</th>
                    <th style={{ padding: '4px 6px', borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #CBD5E1' }}>H (2단계CD)</th>
                    <th style={{ padding: '4px 6px', borderBottom: '1px solid #CBD5E1' }}>I (2단계NM)</th>
                  </tr>
                </thead>
                <tbody>
                  <tr style={{ height: 26, background: '#FFF7ED', color: '#C2410C', fontWeight: 700 }}>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', background: '#FFEDD5' }}>1행</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', textAlign: 'left', paddingLeft: 8 }}>{projectnum || 'q260000test'}</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>—</td>
                    <td style={{ borderBottom: '1px solid #E2E8F0' }}>—</td>
                  </tr>
                  <tr style={{ height: 26, background: '#F8FAFC', color: '#334155', fontWeight: 600 }}>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', background: '#F1F5F9' }}>2행</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>변수명</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>변수명2</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>문항</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>코드</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>라벨</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>1단계CD</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>1단계NM</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>2단계CD</td>
                    <td style={{ borderBottom: '1px solid #E2E8F0' }}>2단계NM</td>
                  </tr>
                  <tr style={{ height: 26, color: '#1E293B' }}>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', background: '#F8FAFC', fontWeight: 600 }}>3행~</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', fontFamily: 'monospace', fontWeight: 700, color: '#EA580C' }}>q2700_op1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', fontFamily: 'monospace' }}>q2700_op1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>문6. 최근 기사</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', fontWeight: 600 }}>1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>팩트시트</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>이미 다듬은 말</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}></td>
                    <td style={{ borderBottom: '1px solid #E2E8F0' }}>9</td>
                  </tr>
                  <tr style={{ height: 26, color: '#1E293B', background: '#FAFAFA' }}>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', background: '#F8FAFC', fontWeight: 600 }}>4행</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', fontFamily: 'monospace', fontWeight: 700, color: '#EA580C' }}>q2700_op1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', fontFamily: 'monospace' }}>q2700_op1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>문6. 최근 기사</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0', fontWeight: 600 }}>2</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>다운턴</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}>이미 다듬은 말</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', borderBottom: '1px solid #E2E8F0' }}></td>
                    <td style={{ borderBottom: '1px solid #E2E8F0' }}>9</td>
                  </tr>
                  <tr style={{ height: 26, color: '#1E293B' }}>
                    <td style={{ borderRight: '1px solid #CBD5E1', background: '#F8FAFC', fontWeight: 600 }}>5행</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', fontFamily: 'monospace', fontWeight: 700, color: '#EA580C' }}>q90_op1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', fontFamily: 'monospace' }}>q90_op1</td>
                    <td style={{ borderRight: '1px solid #CBD5E1' }}>문2. 이유</td>
                    <td style={{ borderRight: '1px solid #CBD5E1', fontWeight: 600 }}>9997</td>
                    <td style={{ borderRight: '1px solid #CBD5E1' }}>기타</td>
                    <td style={{ borderRight: '1px solid #CBD5E1' }}></td>
                    <td style={{ borderRight: '1px solid #CBD5E1' }}></td>
                    <td style={{ borderRight: '1px solid #CBD5E1' }}></td>
                    <td></td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          {/* Upload Form */}
          <div className="prm-field-group" style={{ marginBottom: 20 }}>
            <label>
              엑셀 파일 선택 <span className="req">*</span>
            </label>
            <div className="prm-file-input-wrapper">
              <input
                type="text"
                className="readonly-input"
                readOnly
                value={file ? file.name : '선택된 파일 없음 (.xlsx)'}
              />
              <label htmlFor="lb-excel-input" className="prm-file-btn" style={{ cursor: 'pointer' }}>
                <i className="ti ti-folder-open" style={{ marginRight: 4 }} />
                파일 찾기
              </label>
              <input
                id="lb-excel-input"
                type="file"
                accept=".xlsx, .xls"
                onChange={handleFileChange}
                style={{ display: 'none' }}
              />
            </div>
          </div>

          {file && (
            <div style={{ background: '#FFF7ED', border: '1px solid #FFEDD5', borderRadius: 8, padding: '12px 16px', fontSize: '13px', color: '#C2410C', display: 'flex', alignItems: 'center', gap: 8 }}>
              <i className="ti ti-check-circle" />
              <span>파일이 준비되었습니다: <b>{file.name}</b> ({(file.size / 1024).toFixed(1)} KB)</span>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="modal-footer" style={{ padding: '14px 24px', background: '#F8FAFC', borderTop: '1px solid #E2E8F0', display: 'flex', alignItems: 'center' }}>
          <span style={{ fontSize: '12px', color: '#64748B' }}>
            * 엑셀 파싱 및 보기 데이터 저장이 일괄 진행됩니다.
          </span>
          <div className="pl-sp" />
          <button className="pl-btn" style={{ padding: '8px 18px', marginRight: 8 }} onClick={onClose}>취소</button>
          <button
            className="pl-btn br"
            style={{ padding: '8px 24px', fontWeight: 700 }}
            disabled={!file || submitting}
            onClick={handleImportSubmit}
          >
            {submitting ? '등록 처리 중...' : '보기 일괄 등록'}
          </button>
        </div>
      </div>
    </div>
  );
}
