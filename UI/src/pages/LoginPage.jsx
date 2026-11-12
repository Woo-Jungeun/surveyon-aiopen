import React, { useState } from 'react';
import { apiRequest } from '../api/client';

export default function LoginPage({ onLoginSuccess }) {
  const [user, setUser] = useState('syhong');
  const [pass, setPass] = useState('2402033');
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState('');
  const [rememberMe, setRememberMe] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!user || !pass) {
      setErrorMsg('아이디와 비밀번호를 입력해주세요.');
      return;
    }

    setLoading(true);
    setErrorMsg('');

    // POST /Login/check 호출
    const res = await apiRequest('/Login/check', { user, pass });

    setLoading(false);

    if (res && res.success === '777') {
      const token = res.Token || (res.output && res.output[0] && res.output[0].loginkey) || user;
      const userInfo = (res.output && res.output[0]) || { userid: user, username: user };

      localStorage.setItem('X-Auth-Token', token);
      localStorage.setItem('userInfo', JSON.stringify(userInfo));

      onLoginSuccess(userInfo);
    } else {
      setErrorMsg(res.message || '로그인에 실패했습니다 (에러 코드: ' + (res.success || res.ErrorCode || '702') + ')');
    }
  };

  return (
    <div className="login-bg">
      {/* Home Button (top-left) */}
      <button className="login-home-btn">
        ← 홈으로
      </button>

      {/* Logo Badge */}
      <div className="login-logo-badge">
        <span>📋</span>
        설문온
      </div>

      {/* Subtitle */}
      <p className="login-subtitle">AI 기반 통합 설문조사 플랫폼</p>

      {/* White Login Card */}
      <div className="login-card">
        {/* Card Title */}
        <div className="login-card-title">
          <div className="login-card-title-icon">→</div>
          <h2>로그인</h2>
        </div>
        <p className="login-card-desc">계정에 로그인하여 모든 기능을 이용하세요.</p>

        {/* Error Alert */}
        {errorMsg && (
          <div className="login-error">{errorMsg}</div>
        )}

        {/* Login Form */}
        <form onSubmit={handleSubmit}>
          {/* ID Field */}
          <div className="login-field">
            <label className="login-field-label">아이디</label>
            <div className="login-field-input-wrap">
              <span className="login-field-icon">✉</span>
              <input
                type="text"
                className="login-field-input"
                value={user}
                onChange={(e) => setUser(e.target.value)}
                placeholder="아이디를 입력하세요."
                required
              />
            </div>
          </div>

          {/* Password Field */}
          <div className="login-field">
            <label className="login-field-label">비밀번호</label>
            <div className="login-field-input-wrap">
              <span className="login-field-icon">🔒</span>
              <input
                type="password"
                className="login-field-input"
                value={pass}
                onChange={(e) => setPass(e.target.value)}
                placeholder="비밀번호를 입력하세요."
                required
              />
            </div>
          </div>

          {/* Remember Me */}
          <label className="login-remember">
            <input
              type="checkbox"
              checked={rememberMe}
              onChange={(e) => setRememberMe(e.target.checked)}
            />
            <span>아이디 기억하기</span>
          </label>

          {/* Submit Button */}
          <button
            type="submit"
            className="login-submit-btn"
            disabled={loading}
          >
            <span>→</span>
            {loading ? '로그인 처리 중...' : '로그인'}
          </button>
        </form>
      </div>

      {/* Footer */}
      <div className="login-footer">
        <span>설문제작</span>
        <span>•</span>
        <span>데이터분석</span>
        <span>•</span>
        <span>AI보고서</span>
      </div>
    </div>
  );
}
