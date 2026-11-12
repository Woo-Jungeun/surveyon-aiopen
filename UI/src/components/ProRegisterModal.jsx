import React, { useState, useEffect } from 'react';
import { apiRequest, INTERNAL_KEY, API_BASE } from '../api/client';

export default function ProRegisterModal({ isOpen, onClose, projectnum, proname, server, pof, user, onSuccess }) {
  const [activeTab, setActiveTab] = useState('db'); // 'db' | 'excel'
  
  // DB Tab State
  const [dbItems, setDbItems] = useState([]);
  const [dbFilter, setDbFilter] = useState('all'); // 'all' | 'registered' | 'new'
  const [loadingDb, setLoadingDb] = useState(false);
  const [submittingDb, setSubmittingDb] = useState(false);

  // Excel Tab State
  const [selectedModel, setSelectedModel] = useState('설문온');
  const [excelFile, setExcelFile] = useState(null);
  const [parsedColumns, setParsedColumns] = useState([]);
  const [sampleRows, setSampleRows] = useState([]);
  const [idColumn, setIdColumn] = useState('파일 선택시 자동 활성화');
  const [selectedQnums, setSelectedQnums] = useState(new Set());
  const [showQnumSelectModal, setShowQnumSelectModal] = useState(false);
  const [loadingExcelParse, setLoadingExcelParse] = useState(false);
  const [submittingExcel, setSubmittingExcel] = useState(false);
  
  const [toastMsg, setToastMsg] = useState('');

  const showToast = (msg) => {
    setToastMsg(msg);
    setTimeout(() => setToastMsg(''), 4000);
  };

  // Fetch DB tab data when modal opens or tab changes to 'db'
  useEffect(() => {
    if (!isOpen || activeTab !== 'db' || !projectnum) return;

    const controller = new AbortController();
    fetchDbSelectInfo(controller.signal);

    return () => {
      controller.abort();
    };
  }, [isOpen, activeTab, projectnum, server]);

  const fetchDbSelectInfo = async (signal = null) => {
    if (!projectnum) return;
    setLoadingDb(true);
    try {
      const data = await apiRequest('/pro_register/db_select', {
        projectnum,
        server: (server || 'rps').toLowerCase(),
        user: user || ''
      }, { signal });

      if (signal && signal.aborted) return;
      if (data && data.success === '499') return; // Cancelled

      if (data && (data.success === '777' || data.code === '777')) {
        let items = [];
        if (Array.isArray(data.resultjson)) {
          items = data.resultjson;
        } else if (data.resultjson && Array.isArray(data.resultjson.resultjson)) {
          items = data.resultjson.resultjson;
        } else if (Array.isArray(data.data)) {
          items = data.data;
        } else if (data.data && Array.isArray(data.data.resultjson)) {
          items = data.data.resultjson;
        } else if (typeof data.resultjson === 'string') {
          try {
            const parsed = JSON.parse(data.resultjson);
            items = Array.isArray(parsed) ? parsed : (parsed.resultjson || []);
          } catch { }
        }
        setDbItems(Array.isArray(items) ? items : []);
      } else {
        setDbItems([]);
        showToast((data && (data.Message || data.message)) || 'DB 문항 목록을 불러오지 못했습니다.');
      }
    } catch (err) {
      if (signal && signal.aborted) return;
      console.error(err);
      showToast('DB 문항 목록 조회 중 오류가 발생했습니다.');
    } finally {
      if (!signal || !signal.aborted) {
        setLoadingDb(false);
      }
    }
  };

  // Submit DB Tab (db_enter)
  const handleDbEnter = async () => {
    setSubmittingDb(true);
    try {
      const data = await apiRequest('/pro_register/db_enter', {
        projectnum,
        server: (server || 'rps').toLowerCase(),
        pof: pof || '',
        proname: proname || '',
        user: user || ''
      });

      if (data && (data.success === '777' || data.code === '777')) {
        const respData = data.resultjson || data;
        const msg = respData.message || respData.Message || '오픈데이터가 성공적으로 등록되었습니다.';
        showToast(msg);
        
        // Auto refresh DB tab list so registered PID counts update in real-time
        await fetchDbSelectInfo();

        if (onSuccess) onSuccess();
      } else {
        showToast((data && (data.Message || data.message)) || '오픈데이터 등록에 실패했습니다.');
      }
    } catch (err) {
      console.error(err);
      showToast('오픈데이터 등록 처리 중 오류가 발생했습니다.');
    } finally {
      setSubmittingDb(false);
    }
  };

  // Calculate statistics for DB tab
  const totalCount = dbItems.length;
  const pendingCount = dbItems.filter(i => (i.new_pid_count || 0) > 0).length;
  const newVarCount = dbItems.filter(i => !i.is_registered).length;
  const completedCount = dbItems.filter(i => i.is_registered && (i.new_pid_count || 0) === 0).length;
  const totalNewPidCount = dbItems.reduce((acc, i) => acc + (i.new_pid_count || 0), 0);

  const filteredDbItems = dbItems.filter(item => {
    if (dbFilter === 'pending') return (item.new_pid_count || 0) > 0;
    if (dbFilter === 'completed') return item.is_registered && (item.new_pid_count || 0) === 0;
    if (dbFilter === 'newvar') return !item.is_registered;
    return true;
  });

  // Excel Sample Download (POST fetch + Blob download)
  const handleSampleDownload = async () => {
    try {
      const token = localStorage.getItem('X-Auth-Token') || '';
      showToast('샘플 파일을 다운로드 중입니다...');

      const response = await fetch(`${API_BASE}/pro_register/excel_sample`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Auth-Token': token,
          'Authorization': token ? `Bearer ${token}` : '',
          'X-Internal-Key': INTERNAL_KEY
        },
        body: JSON.stringify({ projectnum: projectnum || '' })
      });

      if (!response.ok) {
        showToast('샘플 파일 다운로드 실패');
        return;
      }

      const blob = await response.blob();
      const downloadUrl = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = downloadUrl;
      a.download = 'HR_DB_Upload_Sample.xlsx';
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(downloadUrl);
      showToast('샘플 파일 다운로드가 완료되었습니다.');
    } catch (err) {
      console.error(err);
      showToast('샘플 파일 다운로드 중 오류가 발생했습니다.');
    }
  };

  // Excel File Change & Parse
  const handleFileChange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;

    setExcelFile(file);
    setLoadingExcelParse(true);
    const formData = new FormData();
    formData.append('file', file);

    try {
      const token = localStorage.getItem('X-Auth-Token') || '';
      const res = await fetch(`${API_BASE}/pro_register/excel_parse`, {
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
        const result = data.resultjson || {};
        setIdColumn(result.id_column || 'id');
        const cols = result.columns || [];
        setParsedColumns(cols);
        setSampleRows(result.sample_rows || []);
        
        // Default: Select all parsed question columns
        const allSet = new Set(cols.map(c => c.qnum));
        setSelectedQnums(allSet);
        showToast(`엑셀 파싱 완료: 문항 ${cols.length}개가 감지되었습니다.`);
      } else {
        showToast((data && data.Message) || '엑셀 파일 파싱에 실패했습니다.');
      }
    } catch (err) {
      console.error(err);
      showToast('엑셀 파일 분석 중 오류가 발생했습니다.');
    } finally {
      setLoadingExcelParse(false);
    }
  };

  // Toggle Single Question in Excel picker
  const toggleQnum = (qnum) => {
    const next = new Set(selectedQnums);
    if (next.has(qnum)) next.delete(qnum);
    else next.add(qnum);
    setSelectedQnums(next);
  };

  // Toggle All Questions in Excel picker
  const toggleSelectAllQnums = () => {
    if (selectedQnums.size === parsedColumns.length) {
      setSelectedQnums(new Set());
    } else {
      setSelectedQnums(new Set(parsedColumns.map(c => c.qnum)));
    }
  };

  // Submit Excel Tab (excel_enter)
  const handleExcelEnter = async () => {
    if (!excelFile) {
      showToast('업로드할 엑셀 파일을 선택하세요.');
      return;
    }
    if (selectedQnums.size === 0) {
      showToast('등록할 문항을 최소 1개 이상 선택하세요.');
      return;
    }

    setSubmittingExcel(true);

    try {
      const token = localStorage.getItem('X-Auth-Token') || '';
      const formData = new FormData();
      formData.append('file', excelFile);
      formData.append('projectnum', projectnum || '');
      formData.append('projectname', proname || '');
      formData.append('projectpof', pof || '');
      formData.append('model', selectedModel || 'excel');
      formData.append('user', user || '');
      formData.append('selected_qnums', JSON.stringify(Array.from(selectedQnums)));

      const res = await fetch(`${API_BASE}/pro_register/excel_enter`, {
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
        showToast('엑셀 오픈데이터가 성공적으로 등록되었습니다.');
        if (onSuccess) onSuccess();
        setTimeout(() => onClose(), 1000);
      } else {
        showToast((data && (data.Message || data.message)) || '엑셀 데이터 등록에 실패했습니다.');
      }
    } catch (err) {
      console.error(err);
      showToast('엑셀 데이터 등록 중 오류가 발생했습니다.');
    } finally {
      setSubmittingExcel(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className="prm-overlay" onClick={onClose}>
      <div className="prm-modal" onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div className="prm-header">
          <h2><i className="ti ti-plus" /> 문항 등록</h2>
          <button className="prm-close" onClick={onClose}><i className="ti ti-x" /></button>
        </div>

        {/* Tabs */}
        <div className="prm-tabs">
          <button
            className={`prm-tab ${activeTab === 'db' ? 'active' : ''}`}
            onClick={() => setActiveTab('db')}
          >
            <i className="ti ti-database" /> DB
          </button>
          <button
            className={`prm-tab ${activeTab === 'excel' ? 'active' : ''}`}
            onClick={() => setActiveTab('excel')}
          >
            <i className="ti ti-file-spreadsheet" /> Excel
          </button>
        </div>

        {/* Modal Body */}
        <div className="prm-body">
          {toastMsg && <div className="prm-toast">{toastMsg}</div>}

          {/* ════════ DB TAB ════════ */}
          {activeTab === 'db' && (
            <div className="prm-tab-content">
              {/* Sub Filter Tabs */}
              <div className="prm-filter-tabs">
                <button
                  className={`prm-filter-btn ${dbFilter === 'all' ? 'active' : ''}`}
                  onClick={() => setDbFilter('all')}
                >
                  전체 ({totalCount})
                </button>
                <button
                  className={`prm-filter-btn ${dbFilter === 'pending' ? 'active' : ''}`}
                  onClick={() => setDbFilter('pending')}
                >
                  PID 추가 대기 ({totalNewPidCount > 0 ? totalNewPidCount : pendingCount})
                </button>
                <button
                  className={`prm-filter-btn ${dbFilter === 'completed' ? 'active' : ''}`}
                  onClick={() => setDbFilter('completed')}
                >
                  등록 완료 ({completedCount})
                </button>
              </div>

              {/* Table Container */}
              <div className="prm-table-wrapper">
                <table className="prm-table">
                  <thead>
                    <tr>
                      <th style={{ width: 50 }}>no</th>
                      <th style={{ width: 110 }}>구분</th>
                      <th style={{ width: 110 }}>qnum</th>
                      <th>등록된 웹의 오픈문항</th>
                      <th style={{ width: 95 }} className="center">기존 PID</th>
                      <th style={{ width: 95 }} className="center">추가 PID</th>
                      <th style={{ width: 95 }} className="center">전체 PID</th>
                    </tr>
                  </thead>
                  <tbody>
                    {loadingDb ? (
                      <tr>
                        <td colSpan={7} className="prm-empty">
                          <i className="ti ti-loader spin" /> 데이터 로딩 중...
                        </td>
                      </tr>
                    ) : filteredDbItems.length === 0 ? (
                      <tr>
                        <td colSpan={7} className="prm-empty">조회 조건에 해당하는 오픈 문항이 없습니다.</td>
                      </tr>
                    ) : (
                      filteredDbItems.map((item, idx) => {
                        const isReg = item.is_registered;
                        const regPid = item.registered_pid_count || 0;
                        const newPid = item.new_pid_count || 0;
                        const totalPid = item.total_pid_count || (regPid + newPid);

                        return (
                          <tr
                            key={idx}
                            className={!isReg ? 'prm-row-new' : newPid > 0 ? 'prm-row-registered' : 'prm-row-completed'}
                          >
                            <td className="center">{item.cnt || idx + 1}</td>
                            <td className="center">
                              {!isReg ? (
                                <span className="prm-tag new">신규 변수</span>
                              ) : newPid > 0 ? (
                                <span className="prm-tag registered">PID 추가 대기</span>
                              ) : (
                                <span className="prm-tag completed">등록 완료</span>
                              )}
                            </td>
                            <td className="bold">{item.qnum}</td>
                            <td>{item.question}</td>
                            <td className="center bold">{isReg ? `${regPid}명` : '-'}</td>
                            <td className="center bold">
                              {newPid > 0 ? (
                                <span className="prm-pid-add">+{newPid}명</span>
                              ) : (
                                <span style={{ color: '#94a3b8' }}>0명</span>
                              )}
                            </td>
                            <td className="center bold">{totalPid}명</td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </div>

              {/* Bottom Form Box */}
              <div className="prm-form-box">
                <div className="prm-field-group">
                  <label>웹프로젝트명</label>
                  <input type="text" value={projectnum || ''} readOnly className="readonly-input" />
                </div>
                <div className="prm-field-group">
                  <label>조사명</label>
                  <input type="text" value={proname || ''} readOnly className="readonly-input" />
                </div>

                <div className="prm-action-row">
                  <button
                    className="prm-btn-orange"
                    onClick={handleDbEnter}
                    disabled={submittingDb || loadingDb}
                  >
                    {submittingDb ? <><i className="ti ti-loader spin" /> 등록 중...</> : '등록'}
                  </button>
                  <p className="prm-help-text">문항추가, 응답자 데이터 추가는 자동으로 등록합니다.</p>
                </div>
              </div>
            </div>
          )}

          {/* ════════ EXCEL TAB ════════ */}
          {activeTab === 'excel' && (
            <div className="prm-tab-content">
              <div className="prm-excel-form-box">
                <div className="prm-excel-title">
                  <h3>Excel 파일 업로드</h3>
                  <p>엑셀 파일을 업로드하여 문항을 등록하세요.</p>
                  <button className="prm-guide-btn" onClick={() => showToast('1행: 문항설명 / 2행: 컬럼변수명 / 1열: 응답자 ID')}>
                    ? 가이드
                  </button>
                </div>

                <div className="prm-grid-2">
                  <div className="prm-field-group">
                    <label>웹프로젝트명</label>
                    <input type="text" value={projectnum || ''} readOnly className="readonly-input" />
                  </div>
                  <div className="prm-field-group">
                    <label>조사명</label>
                    <input type="text" value={proname || ''} readOnly className="readonly-input" />
                  </div>
                </div>

                <div className="prm-field-group">
                  <label>분석모델선택 <span className="req">*</span></label>
                  <select
                    value={selectedModel}
                    onChange={e => setSelectedModel(e.target.value)}
                    className="prm-select"
                  >
                    <option value="설문온">설문온</option>
                    <option value="OpenAI">OpenAI</option>
                    <option value="Claude">Claude</option>
                  </select>
                </div>

                <div className="prm-field-group">
                  <label>파일 업로드 <span className="req">*</span></label>
                  <div className="prm-file-input-wrapper">
                    <label className="prm-file-btn">
                      파일 선택
                      <input
                        type="file"
                        accept=".xlsx, .xls"
                        onChange={handleFileChange}
                        style={{ display: 'none' }}
                      />
                    </label>
                    <span className="prm-file-name">
                      {loadingExcelParse ? '파일 분석 중...' : excelFile ? excelFile.name : '선택된 파일 없음'}
                    </span>
                    <button
                      type="button"
                      className="prm-btn-sample"
                      onClick={handleSampleDownload}
                    >
                      샘플 다운로드
                    </button>
                  </div>
                </div>

                <div className="prm-field-group">
                  <label>아이디컬럼 <span className="req">*</span></label>
                  <input
                    type="text"
                    value={idColumn}
                    readOnly
                    className="readonly-input disabled"
                  />
                </div>

                <div className="prm-field-group">
                  <label>문항선택 <span className="req">*</span></label>
                  <div className="prm-qselect-wrapper">
                    <button
                      type="button"
                      className="prm-btn-qselect"
                      onClick={() => {
                        if (parsedColumns.length === 0) {
                          showToast('엑셀 파일을 먼저 업로드하세요.');
                        } else {
                          setShowQnumSelectModal(true);
                        }
                      }}
                    >
                      문항 선택하기
                    </button>
                    <input
                      type="text"
                      readOnly
                      className="readonly-input"
                      value={
                        selectedQnums.size > 0
                          ? `선택된 문항 ${selectedQnums.size}개`
                          : '선택된 문항이 없습니다.'
                      }
                    />
                  </div>
                </div>

                <div className="prm-excel-action">
                  <button
                    className="prm-btn-orange lg"
                    onClick={handleExcelEnter}
                    disabled={submittingExcel || !excelFile}
                  >
                    {submittingExcel ? <><i className="ti ti-loader spin" /> 등록 중...</> : '등록하기'}
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ════════ QNUM SELECTION SUB-MODAL ════════ */}
      {showQnumSelectModal && (
        <div className="prm-sub-overlay" onClick={() => setShowQnumSelectModal(false)}>
          <div className="prm-sub-modal" onClick={e => e.stopPropagation()}>
            <div className="prm-sub-header">
              <h4><i className="ti ti-checkbox" /> 등록할 문항 선택</h4>
              <button className="prm-close" onClick={() => setShowQnumSelectModal(false)}><i className="ti ti-x" /></button>
            </div>
            <div className="prm-sub-body">
              <div className="prm-sub-allselect">
                <label className="prm-checkbox-label">
                  <input
                    type="checkbox"
                    checked={parsedColumns.length > 0 && selectedQnums.size === parsedColumns.length}
                    onChange={toggleSelectAllQnums}
                  />
                  <span><strong>전체 선택 ({selectedQnums.size} / {parsedColumns.length})</strong></span>
                </label>
              </div>
              <div className="prm-sub-qlist">
                {parsedColumns.map((col, idx) => {
                  const isChecked = selectedQnums.has(col.qnum);
                  return (
                    <label key={idx} className={`prm-sub-qitem ${isChecked ? 'selected' : ''}`}>
                      <input
                        type="checkbox"
                        checked={isChecked}
                        onChange={() => toggleQnum(col.qnum)}
                      />
                      <div className="prm-sub-qinfo">
                        <span className="qnum">[{col.qnum}]</span>
                        <span className="qtext">{col.qnum_text}</span>
                      </div>
                    </label>
                  );
                })}
              </div>
            </div>
            <div className="prm-sub-footer">
              <button
                className="prm-btn-orange"
                onClick={() => setShowQnumSelectModal(false)}
              >
                선택 완료 ({selectedQnums.size}개)
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
