import React, { useState, useEffect, useRef } from 'react';
import {
  startAnalysisWorker,
  createWorkerHubConnection,
  getAnalysisWorkerStatus,
  stopAnalysisWorker,
  getGlobalOptionAll
} from '../api/analysisApi';

export default function HanqProgressModal({
  projectnum,
  user,
  targetQuestions = [],
  onClose,
  onCompleteAll
}) {
  const [queueStatus, setQueueStatus] = useState('idle'); // 'idle' | 'running' | 'paused' | 'stopped' | 'completed'
  const [currentQIndex, setCurrentQIndex] = useState(0);
  const [currentStep, setCurrentStep] = useState(1); // 1: analyze, 2: respondent, 3: empty_etc
  const [currentJobId, setCurrentJobId] = useState('');
  const [consoleLogs, setConsoleLogs] = useState([]);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [estimatedRemainingSec, setEstimatedRemainingSec] = useState(null);
  const [questionProgress, setQuestionProgress] = useState({}); // { [qnum]: { 1: 'done'|'running'|'error', 2: ..., 3: ... } }

  const totalQuestions = targetQuestions.length;
  const totalSteps = totalQuestions * 3;

  // Refs for loop control and SignalR
  const isPausedRef = useRef(false);
  const isStoppedRef = useRef(false);
  const workerHubRef = useRef(null);
  const pollTimerRef = useRef(null);
  const elapsedTimerRef = useRef(null);
  const stepDurationsRef = useRef([]); // array of measured seconds per step
  const terminalEndRef = useRef(null);
  const globalOptionsRef = useRef(null);

  // Completed steps count
  const completedStepsCount = Object.values(questionProgress).reduce((acc, q) => {
    let count = 0;
    if (q[1] === 'done') count++;
    if (q[2] === 'done') count++;
    if (q[3] === 'done') count++;
    return acc + count;
  }, 0);

  const progressPercent = totalSteps > 0 ? Math.min(100, Math.round((completedStepsCount / totalSteps) * 100)) : 0;

  // Format seconds to mm:ss
  const formatTime = (sec) => {
    if (sec === null || sec === undefined || isNaN(sec) || sec < 0) return '--:--';
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  };

  // Auto scroll console
  useEffect(() => {
    if (terminalEndRef.current) {
      terminalEndRef.current.scrollIntoView({ behavior: 'smooth' });
    }
  }, [consoleLogs]);

  // Elapsed timer
  useEffect(() => {
    if (queueStatus === 'running') {
      elapsedTimerRef.current = setInterval(() => {
        setElapsedSeconds(s => {
          const next = s + 1;
          // Dynamically compute remaining ETA based on average step duration
          const remainingSteps = totalSteps - completedStepsCount;
          if (remainingSteps <= 0) {
            setEstimatedRemainingSec(0);
          } else if (stepDurationsRef.current.length > 0) {
            const sum = stepDurationsRef.current.reduce((a, b) => a + b, 0);
            const avg = sum / stepDurationsRef.current.length;
            setEstimatedRemainingSec(Math.round(avg * remainingSteps));
          } else {
            // Initial rapid estimate: 25s per step
            setEstimatedRemainingSec(remainingSteps * 25);
          }
          return next;
        });
      }, 1000);
    } else {
      if (elapsedTimerRef.current) clearInterval(elapsedTimerRef.current);
    }
    return () => {
      if (elapsedTimerRef.current) clearInterval(elapsedTimerRef.current);
    };
  }, [queueStatus, totalSteps, completedStepsCount]);

  // Auto start on mount
  useEffect(() => {
    let isCancelled = false;
    async function initAndStart() {
      if (targetQuestions.length === 0) {
        setConsoleLogs(['[오류] 분석 대상 문항이 없습니다. "분석"으로 설정된 문항을 확인하세요.']);
        return;
      }

      setConsoleLogs([`[한큐분석] 총 ${targetQuestions.length}개 대상 문항의 3단계 파이프라인 분석을 준비합니다...`]);

      // 1. Load global options
      try {
        const optRes = await getGlobalOptionAll(projectnum, user);
        if (optRes && optRes.success === '777') {
          globalOptionsRef.current = optRes.resultjson?.data || {};
          setConsoleLogs(l => [...l, `[설정 로드] 범용 옵션(option_json_all)을 성공적으로 불러왔습니다.`]);
        }
      } catch (err) {
        console.warn('Failed to load global options, using standard defaults:', err);
      }

      if (!globalOptionsRef.current) {
        globalOptionsRef.current = {
          lvcode: '3',
          model_select: 'GPT-5.2',
          result_lang: 'Korean',
          temperature: 0.2,
          open_item_lv1: '0~50',
          open_item_lv2: '0',
          open_item_lv3: '0',
          prompt_string: ''
        };
      }

      // 2. Start running queue
      if (!isCancelled) {
        runQueue();
      }
    }

    initAndStart();

    return () => {
      isCancelled = true;
      isStoppedRef.current = true;
      if (workerHubRef.current) {
        try { workerHubRef.current.stop(); } catch (e) {}
      }
      if (pollTimerRef.current) clearInterval(pollTimerRef.current);
      if (elapsedTimerRef.current) clearInterval(elapsedTimerRef.current);
    };
  }, []);

  // Main Queue Runner Loop
  const runQueue = async () => {
    setQueueStatus('running');
    isPausedRef.current = false;
    isStoppedRef.current = false;

    // Concurrency check (default 1)
    const concurrency = parseInt(globalOptionsRef.current?.concurrency || '1', 10);
    setConsoleLogs(l => [...l, `[한큐분석] 실행 모드: ${concurrency === 2 ? '2개 문항 병렬' : '1개 문항 순차'} 파이프라인 시작`]);

    for (let qIdx = currentQIndex; qIdx < targetQuestions.length; qIdx++) {
      if (isStoppedRef.current) break;

      // Handle pause
      while (isPausedRef.current && !isStoppedRef.current) {
        await new Promise(r => setTimeout(r, 500));
      }
      if (isStoppedRef.current) break;

      setCurrentQIndex(qIdx);
      const question = targetQuestions[qIdx];
      const qTitle = question.question_title || question.qnum_text || question.qnum;
      const qKey = question.qnum;

      setConsoleLogs(l => [
        ...l,
        `\n======================================================`,
        `[문항 ${qIdx + 1}/${totalQuestions}] [${question.qnum_text || question.qnum}] ${qTitle}`,
        `======================================================`
      ]);

      // Execute 3 steps sequentially for this question
      const steps = [
        { stepNum: 1, action: 'analyze', name: '보기분석' },
        { stepNum: 2, action: 'respondent', name: '응답자분석(전체)' },
        { stepNum: 3, action: 'empty_etc', name: '응답자분석(빈셀)' }
      ];

      let questionSuccess = true;

      for (const step of steps) {
        if (isStoppedRef.current) break;
        while (isPausedRef.current && !isStoppedRef.current) {
          await new Promise(r => setTimeout(r, 500));
        }
        if (isStoppedRef.current) break;

        setCurrentStep(step.stepNum);
        setQuestionProgress(prev => ({
          ...prev,
          [qKey]: { ...(prev[qKey] || {}), [step.stepNum]: 'running' }
        }));

        setConsoleLogs(l => [...l, `\n▶ [${step.stepNum}/3 단계] ${step.name} 시작...`]);

        const stepStartMs = Date.now();
        const success = await executeStep(question, step.action, step.name);
        const stepElapsedSec = Math.max(1, Math.round((Date.now() - stepStartMs) / 1000));
        stepDurationsRef.current.push(stepElapsedSec);

        if (success) {
          setQuestionProgress(prev => ({
            ...prev,
            [qKey]: { ...(prev[qKey] || {}), [step.stepNum]: 'done' }
          }));
          setConsoleLogs(l => [...l, `✔ [${step.stepNum}/3 단계] ${step.name} 완료 (${stepElapsedSec}초 소요)`]);
        } else {
          questionSuccess = false;
          setQuestionProgress(prev => ({
            ...prev,
            [qKey]: { ...(prev[qKey] || {}), [step.stepNum]: 'error' }
          }));
          setConsoleLogs(l => [...l, `✖ [${step.stepNum}/3 단계] ${step.name} 실패`]);
          // If a step fails, log warning and proceed to next question
          break;
        }
      }
    }

    if (!isStoppedRef.current) {
      setQueueStatus('completed');
      setConsoleLogs(l => [
        ...l,
        `\n======================================================`,
        `🎉 [한큐분석 완료] 총 ${totalQuestions}개 문항의 분석 파이프라인이 모두 완료되었습니다!`,
        `총 소요 시간: ${formatTime(elapsedSeconds)}`,
        `======================================================`
      ]);
      if (onCompleteAll) onCompleteAll();
    }
  };

  // Execute a single step with SignalR + fallback polling
  const executeStep = (question, actionType, actTitle) => {
    return new Promise(async (resolve) => {
      const gOpt = globalOptionsRef.current || {};
      const options = {
        isHanq: true,
        qid: question.id ? String(question.id) : undefined,
        settings: {
          ...gOpt,
          isHanq: true,
          question_fin: question.question_title || question.qnum_text || question.qnum,
          apikeyid: gOpt.apikeyid || undefined
        }
      };

      if (actionType === 'analyze') {
        options.classified = 'Y';
        options.opencodeCategory = 'Y';
        options.response = 'N';
        options.opencodeResponse = 'N';
      } else if (actionType === 'respondent') {
        options.classified = 'N';
        options.opencodeCategory = 'N';
        options.response = 'Y';
        options.opencodeResponse = 'Y';
        options.recallResponse = 'Y';
      } else if (actionType === 'empty_etc') {
        options.recallResponse = 'Y';
      }

      try {
        const res = await startAnalysisWorker(projectnum, question.qnum, actionType, user, options);
        if (!res || (!res.ok && res.success !== '777')) {
          setConsoleLogs(l => [...l, `[오류] 워커 실행 요청 실패: ${res?.message || '서버 오류'}`]);
          resolve(false);
          return;
        }

        const jobId = res.jobId || res.job_id;
        setCurrentJobId(jobId);
        setConsoleLogs(l => [...l, `[Job: ${jobId}] 워커 프로세스가 구동되었습니다.`]);

        let isFinished = false;
        let logOffset = 0;

        // Cleanup previous SignalR connection
        if (workerHubRef.current) {
          try { workerHubRef.current.stop(); } catch (e) {}
        }

        const hub = createWorkerHubConnection();
        workerHubRef.current = hub;

        const completeStep = (success) => {
          if (isFinished) return;
          isFinished = true;
          if (pollTimerRef.current) clearInterval(pollTimerRef.current);
          try { hub.invoke('Leave', jobId); } catch (e) {}
          try { hub.stop(); } catch (e) {}
          resolve(success);
        };

        hub.on('onLine', (jobKey, line, isError) => {
          if (jobKey && jobId && jobKey !== jobId) return;
          const displayLine = isError && !line.startsWith('ERROR:') && !line.startsWith('[오류]')
            ? `[오류] ${line}`
            : line;
          setConsoleLogs(l => [...l, displayLine]);
        });

        hub.on('onCompleted', (jobKey, hasError, exitCode) => {
          if (jobKey && jobId && jobKey !== jobId) return;
          completeStep(!hasError);
        });

        hub.start()
          .then(async () => {
            await hub.invoke('Join', jobId);
          })
          .catch(err => {
            console.warn('[SignalR] WebSocket 연결 실패, Fallback HTTP 폴링 유지:', err);
          });

        // Fallback polling in case SignalR events are dropped
        if (pollTimerRef.current) clearInterval(pollTimerRef.current);
        pollTimerRef.current = setInterval(async () => {
          if (isFinished) return;
          try {
            const statusRes = await getAnalysisWorkerStatus(jobId, logOffset);
            if (statusRes && statusRes.ok) {
              if (statusRes.output) {
                const lines = statusRes.output.split('\n').filter(Boolean);
                if (lines.length > 0) {
                  setConsoleLogs(l => [...l, ...lines]);
                }
              }
              if (statusRes.nextOffset) logOffset = statusRes.nextOffset;

              if (statusRes.isCompleted) {
                completeStep(!statusRes.hasError);
              }
            }
          } catch (e) {}
        }, 2000);

      } catch (err) {
        setConsoleLogs(l => [...l, `[예외 발생] ${err.message}`]);
        resolve(false);
      }
    });
  };

  // Handlers
  const handleTogglePause = () => {
    if (queueStatus === 'running') {
      isPausedRef.current = true;
      setQueueStatus('paused');
      setConsoleLogs(l => [...l, `\n⏸ [일시 정지] 현재 실행 중인 단계 완료 후 일시 대기합니다.`]);
    } else if (queueStatus === 'paused') {
      isPausedRef.current = false;
      setQueueStatus('running');
      setConsoleLogs(l => [...l, `\n▶ [작업 재개] 한큐분석 파이프라인을 재개합니다.`]);
    }
  };

  const handleStop = async () => {
    if (!window.confirm('정말 한큐분석 작업을 중지하시겠습니까? 현재 진행 중인 워커 프로세스가 강제 종료됩니다.')) return;
    isStoppedRef.current = true;
    setQueueStatus('stopped');
    if (currentJobId) {
      try {
        await stopAnalysisWorker(currentJobId, projectnum, targetQuestions[currentQIndex]?.qnum, user);
      } catch (e) {}
    }
    setConsoleLogs(l => [...l, `\n⛔ [강제 중지] 사용자에 의해 한큐분석 큐가 중지되었습니다.`]);
  };

  const currentQ = targetQuestions[currentQIndex] || {};

  return (
    <div className="modal-overlay" style={{ zIndex: 1100 }}>
      <div className="modal-content" style={{
        maxWidth: '840px', width: '95%', maxHeight: '90vh',
        display: 'flex', flexDirection: 'column',
        borderRadius: '16px', padding: '24px 28px',
        boxShadow: '0 25px 50px -12px rgba(0,0,0,0.25)',
        background: '#fff'
      }}>
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderBottom: '1px solid #e2e8f0', paddingBottom: '14px', marginBottom: '16px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <span style={{
              width: '40px', height: '40px', borderRadius: '12px',
              background: 'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              color: '#fff', fontSize: '20px',
              boxShadow: '0 4px 12px rgba(99,102,241,0.35)'
            }}>
              ⚡
            </span>
            <div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <h3 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 800, color: '#0f172a' }}>
                  한큐분석 자동 실행 큐
                </h3>
                <span style={{
                  fontSize: '0.75rem', fontWeight: 700, padding: '2px 8px', borderRadius: '9999px',
                  background: queueStatus === 'running' ? '#dcfce7' : (queueStatus === 'completed' ? '#dbeafe' : (queueStatus === 'paused' ? '#fef3c7' : '#fee2e2')),
                  color: queueStatus === 'running' ? '#15803d' : (queueStatus === 'completed' ? '#1e40af' : (queueStatus === 'paused' ? '#b45309' : '#b91c1c'))
                }}>
                  {queueStatus === 'running' ? '실행 중' : (queueStatus === 'completed' ? '완료됨' : (queueStatus === 'paused' ? '일시정지' : '중지됨'))}
                </span>
              </div>
              <p style={{ margin: '2px 0 0', fontSize: '0.8rem', color: '#64748b' }}>
                보기분석 ➔ 응답자분석(전체) ➔ 응답자분석(빈셀) 3단계 파이프라인 자동 완주
              </p>
            </div>
          </div>
          <button
            className="modal-close-btn"
            onClick={onClose}
            disabled={queueStatus === 'running'}
            title={queueStatus === 'running' ? '작업 실행 중에는 닫을 수 없습니다. 일시정지 또는 중지 후 닫으세요.' : '닫기'}
            style={{ fontSize: '20px', background: 'none', border: 'none', cursor: queueStatus === 'running' ? 'not-allowed' : 'pointer', color: '#94a3b8' }}
          >
            ✕
          </button>
        </div>

        {/* Progress Overview Card */}
        <div style={{
          background: 'linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%)',
          borderRadius: '12px', padding: '16px 20px',
          border: '1px solid #e2e8f0', marginBottom: '16px'
        }}>
          {/* Progress bar header */}
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
            <span style={{ fontSize: '0.88rem', fontWeight: 700, color: '#1e293b' }}>
              전체 진행율: {progressPercent}%
              <span style={{ marginLeft: '8px', fontSize: '0.8rem', fontWeight: 500, color: '#64748b' }}>
                ({completedStepsCount} / {totalSteps} 단계 완료 · 문항 {Math.min(currentQIndex + 1, totalQuestions)} / {totalQuestions})
              </span>
            </span>
            <div style={{ display: 'flex', gap: '16px', fontSize: '0.82rem' }}>
              <span>
                ⏱️ 경과 시간: <b>{formatTime(elapsedSeconds)}</b>
              </span>
              <span>
                ⏳ 예상 잔여 시간: <b style={{ color: '#6366f1' }}>{estimatedRemainingSec !== null ? (estimatedRemainingSec === 0 ? '완료' : `약 ${formatTime(estimatedRemainingSec)}`) : '계산 중...'}</b>
              </span>
            </div>
          </div>

          {/* Progress Bar */}
          <div style={{ width: '100%', height: '10px', background: '#cbd5e1', borderRadius: '9999px', overflow: 'hidden' }}>
            <div style={{
              width: `${progressPercent}%`, height: '100%',
              background: 'linear-gradient(90deg, #6366f1 0%, #8b5cf6 50%, #ec4899 100%)',
              borderRadius: '9999px', transition: 'width 0.3s ease'
            }} />
          </div>

          {/* Current Question & 3-Step Indicator */}
          <div style={{ marginTop: '14px', paddingTop: '12px', borderTop: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: '0.82rem', color: '#64748b', marginBottom: '6px' }}>
              현재 처리 중: <b style={{ color: '#0f172a' }}>[{currentQ.qnum_text || currentQ.qnum}]</b> {currentQ.question_title}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '10px' }}>
              {[
                { num: 1, name: '1. 보기분석', desc: '카테고리/보기 생성' },
                { num: 2, name: '2. 응답자(전체)', desc: '전체 응답 자동 분류' },
                { num: 3, name: '3. 응답자(빈셀)', desc: '미분류/기타 재분류' }
              ].map(st => {
                const qProg = questionProgress[currentQ.qnum] || {};
                const stStatus = qProg[st.num] || (st.num < currentStep ? 'done' : (st.num === currentStep ? 'running' : 'pending'));
                const isRunning = stStatus === 'running';
                const isDone = stStatus === 'done';
                const isError = stStatus === 'error';

                return (
                  <div
                    key={st.num}
                    style={{
                      background: isRunning ? '#eef2ff' : (isDone ? '#f0fdf4' : (isError ? '#fef2f2' : '#fff')),
                      border: isRunning ? '2px solid #6366f1' : (isDone ? '1px solid #86efac' : (isError ? '1px solid #fca5a5' : '1px solid #e2e8f0')),
                      borderRadius: '8px', padding: '8px 12px',
                      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                      transition: 'all 0.2s ease'
                    }}
                  >
                    <div>
                      <div style={{ fontSize: '0.82rem', fontWeight: isRunning ? 700 : 600, color: isRunning ? '#4f46e5' : (isDone ? '#166534' : '#334155') }}>
                        {st.name}
                      </div>
                      <div style={{ fontSize: '0.72rem', color: '#64748b' }}>{st.desc}</div>
                    </div>
                    <span>
                      {isRunning && <i className="ti ti-loader" style={{ animation: 'spin 1s linear infinite', color: '#6366f1', fontSize: '18px' }} />}
                      {isDone && <span style={{ color: '#16a34a', fontWeight: 700, fontSize: '16px' }}>✔</span>}
                      {isError && <span style={{ color: '#dc2626', fontWeight: 700, fontSize: '16px' }}>✖</span>}
                      {!isRunning && !isDone && !isError && <span style={{ color: '#94a3b8', fontSize: '12px' }}>대기</span>}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Real-time Console Terminal */}
        <div style={{ flex: 1, minHeight: '220px', display: 'flex', flexDirection: 'column', background: '#0f172a', borderRadius: '10px', overflow: 'hidden' }}>
          <div style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            padding: '8px 14px', background: '#1e293b', borderBottom: '1px solid #334155'
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <i className="ti ti-terminal-2" style={{ color: '#38bdf8' }} />
              <span style={{ fontSize: '0.78rem', fontWeight: 600, color: '#e2e8f0' }}>
                SignalR Worker 실시간 실행 로그
              </span>
            </div>
            <span style={{ fontSize: '0.72rem', color: '#94a3b8' }}>
              Job: {currentJobId || '대기 중'}
            </span>
          </div>

          <div style={{
            flex: 1, padding: '12px 14px', overflowY: 'auto',
            fontFamily: 'Consolas, Monaco, "Courier New", monospace', fontSize: '0.78rem',
            lineHeight: 1.5, color: '#e2e8f0', whiteSpace: 'pre-wrap', wordBreak: 'break-all'
          }}>
            {consoleLogs.map((log, idx) => {
              const isErr = log.includes('ERROR:') || log.includes('[오류]') || log.includes('실패');
              const isSucc = log.includes('✔') || log.includes('완료') || log.includes('성공');
              const isHead = log.includes('===');
              return (
                <div
                  key={idx}
                  style={{
                    color: isErr ? '#f87171' : (isSucc ? '#4ade80' : (isHead ? '#38bdf8' : '#e2e8f0')),
                    fontWeight: (isHead || isSucc) ? 700 : 400
                  }}
                >
                  {log}
                </div>
              );
            })}
            <div ref={terminalEndRef} />
          </div>
        </div>

        {/* Bottom Actions */}
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          marginTop: '16px', paddingTop: '12px', borderTop: '1px solid #e2e8f0'
        }}>
          <div style={{ fontSize: '0.8rem', color: '#64748b' }}>
            {queueStatus === 'completed'
              ? '모든 분석이 완료되었습니다. 창을 닫으셔도 됩니다.'
              : '작업이 완료될 때까지 브라우저 창을 닫지 마세요.'}
          </div>

          <div style={{ display: 'flex', gap: '10px' }}>
            {queueStatus === 'running' && (
              <button
                type="button"
                onClick={handleTogglePause}
                style={{
                  padding: '8px 16px', borderRadius: '8px',
                  border: '1px solid #cbd5e1', background: '#fff',
                  color: '#475569', fontSize: '0.85rem', fontWeight: 600,
                  cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px'
                }}
              >
                <i className="ti ti-player-pause" />일시 정지
              </button>
            )}

            {queueStatus === 'paused' && (
              <button
                type="button"
                onClick={handleTogglePause}
                style={{
                  padding: '8px 16px', borderRadius: '8px',
                  border: 'none', background: '#3b82f6',
                  color: '#fff', fontSize: '0.85rem', fontWeight: 600,
                  cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px'
                }}
              >
                <i className="ti ti-player-play" />작업 계속
              </button>
            )}

            {(queueStatus === 'running' || queueStatus === 'paused') && (
              <button
                type="button"
                onClick={handleStop}
                style={{
                  padding: '8px 16px', borderRadius: '8px',
                  border: '1px solid #fca5a5', background: '#fef2f2',
                  color: '#dc2626', fontSize: '0.85rem', fontWeight: 600,
                  cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px'
                }}
              >
                <i className="ti ti-player-stop" />중지
              </button>
            )}

            {(queueStatus === 'completed' || queueStatus === 'stopped') && (
              <button
                type="button"
                onClick={onClose}
                style={{
                  padding: '8px 24px', borderRadius: '8px',
                  border: 'none', background: '#1e293b',
                  color: '#fff', fontSize: '0.88rem', fontWeight: 700,
                  cursor: 'pointer'
                }}
              >
                확인 및 닫기
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
