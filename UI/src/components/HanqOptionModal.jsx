import React, { useState, useEffect } from 'react';
import { getGlobalOptionAll, saveGlobalOptionAll } from '../api/analysisApi';

export default function HanqOptionModal({ projectnum, user, onClose, onSaveSuccess }) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState({ type: '', text: '' });

  // Form states
  const [lvcode, setLvcode] = useState('3');
  const [modelSelect, setModelSelect] = useState('GPT-5.2');
  const [resultLang, setResultLang] = useState('Korean');
  const [temperature, setTemperature] = useState(0.2);
  const [openLv1, setOpenLv1] = useState('0~50');
  const [openLv2, setOpenLv2] = useState('0');
  const [openLv3, setOpenLv3] = useState('0');
  const [promptString, setPromptString] = useState('');
  const [concurrency, setConcurrency] = useState('1'); // '1' (안정 순차) | '2' (2문항 병렬)

  useEffect(() => {
    let isMounted = true;
    async function loadData() {
      setLoading(true);
      try {
        const res = await getGlobalOptionAll(projectnum, user);
        if (isMounted && res && res.success === '777') {
          const opt = res.resultjson?.data || {};
          if (opt.lvcode) setLvcode(String(opt.lvcode));
          if (opt.model_select) setModelSelect(opt.model_select);
          if (opt.result_lang) setResultLang(opt.result_lang);
          if (opt.temperature !== undefined && opt.temperature !== null) {
            setTemperature(parseFloat(opt.temperature) || 0.2);
          }
          if (opt.open_item_lv1) setOpenLv1(opt.open_item_lv1);
          if (opt.open_item_lv2) setOpenLv2(opt.open_item_lv2);
          if (opt.open_item_lv3) setOpenLv3(opt.open_item_lv3);
          if (opt.prompt_string !== undefined) setPromptString(opt.prompt_string || '');
          if (opt.concurrency) setConcurrency(String(opt.concurrency));
        }
      } catch (err) {
        console.error('Failed to load global options:', err);
      } finally {
        if (isMounted) setLoading(false);
      }
    }
    loadData();
    return () => { isMounted = false; };
  }, [projectnum, user]);

  const handleResetDefaults = () => {
    if (!window.confirm('기본 표준 옵션값으로 초기화하시겠습니까?')) return;
    setLvcode('3');
    setModelSelect('GPT-5.2');
    setResultLang('Korean');
    setTemperature(0.2);
    setOpenLv1('0~50');
    setOpenLv2('0');
    setOpenLv3('0');
    setPromptString('');
    setConcurrency('1');
    setMsg({ type: 'info', text: '기본 설정값으로 재설정되었습니다. [저장]을 눌러야 반영됩니다.' });
  };

  const handleSave = async (e) => {
    if (e) e.preventDefault();
    setSaving(true);
    setMsg({ type: '', text: '' });

    const settings = {
      lvcode,
      model_select: modelSelect,
      result_lang: resultLang,
      temperature,
      open_item_lv1: openLv1,
      open_item_lv2: openLv2,
      open_item_lv3: openLv3,
      prompt_string: promptString,
      concurrency
    };

    try {
      const res = await saveGlobalOptionAll(projectnum, user, settings);
      setSaving(false);
      if (res && res.success === '777') {
        if (onSaveSuccess) onSaveSuccess(settings);
        onClose();
      } else {
        setMsg({ type: 'error', text: (res && res.message) || '범용 옵션 저장에 실패했습니다.' });
      }
    } catch (err) {
      setSaving(false);
      setMsg({ type: 'error', text: '저장 중 네트워크 오류가 발생했습니다: ' + err.message });
    }
  };

  return (
    <div className="modal-overlay" style={{ zIndex: 1100 }}>
      <div className="modal-content" style={{ maxWidth: '640px', width: '92%', borderRadius: '14px', padding: '24px 28px', boxShadow: '0 20px 40px rgba(0,0,0,0.2)' }}>
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderBottom: '1px solid #e2e8f0', paddingBottom: '14px', marginBottom: '18px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{
              width: '36px', height: '36px', borderRadius: '10px',
              background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
              display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#fff', fontSize: '18px',
              boxShadow: '0 4px 10px rgba(99,102,241,0.3)'
            }}>
              ⚙️
            </span>
            <div>
              <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 700, color: '#1e293b' }}>
                한큐분석 범용 옵션 설정
              </h3>
              <p style={{ margin: '2px 0 0', fontSize: '0.8rem', color: '#64748b' }}>
                이 프로젝트의 모든 분석 대상 문항에 공통으로 일괄 적용될 기본 분석 옵션입니다.
              </p>
            </div>
          </div>
          <button className="modal-close-btn" onClick={onClose} style={{ fontSize: '18px', background: 'none', border: 'none', cursor: 'pointer', color: '#94a3b8' }}>✕</button>
        </div>

        {/* Message Banner */}
        {msg.text && (
          <div style={{
            background: msg.type === 'error' ? '#fef2f2' : '#f0fdf4',
            border: `1px solid ${msg.type === 'error' ? '#fecaca' : '#bbf7d0'}`,
            color: msg.type === 'error' ? '#dc2626' : '#15803d',
            padding: '10px 14px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.85rem'
          }}>
            {msg.text}
          </div>
        )}

        {loading ? (
          <div style={{ padding: '40px 0', textAlign: 'center', color: '#64748b' }}>
            <i className="ti ti-loader" style={{ animation: 'spin 1s linear infinite', fontSize: '24px', display: 'inline-block', marginBottom: '8px' }} />
            <div>범용 옵션 정보를 불러오는 중입니다...</div>
          </div>
        ) : (
          <form onSubmit={handleSave}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
              {/* 분류 단계 (lvcode) */}
              <div style={{ gridColumn: 'span 2' }}>
                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                  분류 단계 선택
                </label>
                <div style={{ display: 'flex', gap: '8px' }}>
                  {[
                    { id: '1', label: '1단계 (소)' },
                    { id: '2', label: '2단계 (중,소)' },
                    { id: '3', label: '3단계 (대,중,소)' }
                  ].map(step => (
                    <button
                      key={step.id}
                      type="button"
                      onClick={() => setLvcode(step.id)}
                      style={{
                        flex: 1,
                        padding: '9px 12px',
                        borderRadius: '8px',
                        border: lvcode === step.id ? '2px solid #6366f1' : '1px solid #cbd5e1',
                        background: lvcode === step.id ? '#eef2ff' : '#f8fafc',
                        color: lvcode === step.id ? '#4f46e5' : '#475569',
                        fontWeight: lvcode === step.id ? 700 : 500,
                        fontSize: '0.88rem',
                        cursor: 'pointer',
                        transition: 'all 0.15s ease'
                      }}
                    >
                      {step.label}
                    </button>
                  ))}
                </div>
              </div>

              {/* AI 모델 선택 */}
              <div>
                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                  AI 모델
                </label>
                <select
                  value={modelSelect}
                  onChange={(e) => setModelSelect(e.target.value)}
                  style={{ width: '100%', padding: '9px 12px', borderRadius: '8px', border: '1px solid #cbd5e1', fontSize: '0.88rem', background: '#fff' }}
                >
                  <option value="GPT-5.2">GPT-5.2 (권장)</option>
                  <option value="GPT-4o">GPT-4o</option>
                  <option value="GPT-4o-mini">GPT-4o-mini (고속)</option>
                  <option value="o1-preview">o1-preview (심층 추론)</option>
                </select>
              </div>

              {/* 결과 언어 */}
              <div>
                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                  결과 언어
                </label>
                <select
                  value={resultLang}
                  onChange={(e) => setResultLang(e.target.value)}
                  style={{ width: '100%', padding: '9px 12px', borderRadius: '8px', border: '1px solid #cbd5e1', fontSize: '0.88rem', background: '#fff' }}
                >
                  <option value="Korean">한국어 (Korean)</option>
                  <option value="English">영어 (English)</option>
                  <option value="Japanese">일본어 (Japanese)</option>
                  <option value="Chinese">중국어 (Chinese)</option>
                </select>
              </div>

              {/* 창의성 (Temperature) */}
              <div style={{ gridColumn: 'span 2' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px' }}>
                  <label style={{ fontSize: '0.85rem', fontWeight: 600, color: '#334155' }}>
                    창의성 (Temperature)
                  </label>
                  <span style={{ fontSize: '0.88rem', fontWeight: 700, color: '#4f46e5' }}>
                    {temperature.toFixed(1)}
                  </span>
                </div>
                <input
                  type="range"
                  min="0"
                  max="1"
                  step="0.1"
                  value={temperature}
                  onChange={(e) => setTemperature(parseFloat(e.target.value))}
                  style={{ width: '100%', accentColor: '#6366f1', cursor: 'pointer' }}
                />
              </div>

              {/* 분류 개수 설정 */}
              <div style={{ gridColumn: 'span 2' }}>
                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                  분류 개수 설정 (단계별 생성 한도)
                </label>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '10px' }}>
                  <div>
                    <span style={{ fontSize: '0.78rem', color: '#64748b', display: 'block', marginBottom: '3px' }}>소분류</span>
                    <input
                      type="text"
                      value={openLv1}
                      onChange={(e) => setOpenLv1(e.target.value)}
                      placeholder="0~50"
                      style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                    />
                  </div>
                  <div>
                    <span style={{ fontSize: '0.78rem', color: '#64748b', display: 'block', marginBottom: '3px' }}>중분류</span>
                    <input
                      type="text"
                      value={openLv2}
                      onChange={(e) => setOpenLv2(e.target.value)}
                      placeholder="0"
                      style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                    />
                  </div>
                  <div>
                    <span style={{ fontSize: '0.78rem', color: '#64748b', display: 'block', marginBottom: '3px' }}>대분류</span>
                    <input
                      type="text"
                      value={openLv3}
                      onChange={(e) => setOpenLv3(e.target.value)}
                      placeholder="0"
                      style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                    />
                  </div>
                </div>
              </div>

              {/* 공통 프롬프트 지침 */}
              <div style={{ gridColumn: 'span 2' }}>
                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                  공통 프롬프트 지침
                </label>
                <textarea
                  value={promptString}
                  onChange={(e) => setPromptString(e.target.value)}
                  placeholder="모든 문항 분석 시 공통으로 반영할 프롬프트 지침을 입력하세요 (비워두면 시스템 기본 지침 적용)"
                  rows={3}
                  style={{ width: '100%', padding: '10px 12px', borderRadius: '8px', border: '1px solid #cbd5e1', fontSize: '0.85rem', resize: 'vertical', fontFamily: 'inherit' }}
                />
              </div>

              {/* 동시 실행 문항 수 (분산처리/Concurrency) */}
              <div style={{ gridColumn: 'span 2', background: '#f8fafc', padding: '12px 14px', borderRadius: '8px', border: '1px solid #e2e8f0' }}>
                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '6px' }}>
                  한큐분석 동시 실행 문항 수 (분산처리 병렬도)
                </label>
                <div style={{ display: 'flex', gap: '16px', alignItems: 'center' }}>
                  <label style={{ display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', fontSize: '0.85rem', color: '#1e293b' }}>
                    <input
                      type="radio"
                      name="concurrency"
                      value="1"
                      checked={concurrency === '1'}
                      onChange={() => setConcurrency('1')}
                    />
                    <b>1개 문항씩 순차 실행 (권장)</b> — API Rate Limit 에러 0% 안정 모드
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer', fontSize: '0.85rem', color: '#1e293b' }}>
                    <input
                      type="radio"
                      name="concurrency"
                      value="2"
                      checked={concurrency === '2'}
                      onChange={() => setConcurrency('2')}
                    />
                    <b>2개 문항 병렬 실행</b> — 고속 모드 (약 40% 시간 단축)
                  </label>
                </div>
              </div>
            </div>

            {/* Actions */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid #e2e8f0', paddingTop: '16px', marginTop: '12px' }}>
              <button
                type="button"
                onClick={handleResetDefaults}
                style={{ padding: '8px 14px', borderRadius: '6px', border: '1px solid #cbd5e1', background: '#fff', color: '#64748b', fontSize: '0.82rem', cursor: 'pointer' }}
              >
                기본값 복원
              </button>
              <div style={{ display: 'flex', gap: '10px' }}>
                <button
                  type="button"
                  onClick={onClose}
                  disabled={saving}
                  style={{ padding: '9px 18px', borderRadius: '8px', border: '1px solid #cbd5e1', background: '#fff', color: '#475569', fontSize: '0.88rem', fontWeight: 600, cursor: 'pointer' }}
                >
                  취소
                </button>
                <button
                  type="submit"
                  disabled={saving}
                  style={{
                    padding: '9px 24px', borderRadius: '8px', border: 'none',
                    background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
                    color: '#fff', fontSize: '0.88rem', fontWeight: 700, cursor: 'pointer',
                    boxShadow: '0 4px 12px rgba(99,102,241,0.3)'
                  }}
                >
                  {saving ? '저장 중...' : '범용 옵션 저장'}
                </button>
              </div>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
