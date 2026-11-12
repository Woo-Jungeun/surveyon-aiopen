import React from 'react';

export default function ProgressBarModal({ percent, message, currentCount, totalCount }) {
  return (
    <div className="progress-modal-overlay">
      <div className="progress-modal-card">
        <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: '#1f2937', marginBottom: '8px' }}>
          프로젝트 목록 동기화 진행 중
        </h3>
        
        <p style={{ fontSize: '0.85rem', color: '#6b7280', marginBottom: '20px' }}>
          {message || '외부 프로젝트 데이터 및 권한을 수집 중입니다...'}
        </p>

        {/* Progress Bar */}
        <div className="progress-bar-track">
          <div className="progress-bar-fill" style={{ width: `${percent}%` }} />
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', color: '#6b7280' }}>
          <span>진행률: <strong style={{ color: '#f97316' }}>{percent}%</strong></span>
          {totalCount > 0 && (
            <span>처리 건수: <strong>{currentCount} / {totalCount}</strong></span>
          )}
        </div>
      </div>
    </div>
  );
}
