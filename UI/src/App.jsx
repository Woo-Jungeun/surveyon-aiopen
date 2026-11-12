import React, { useState, useEffect } from 'react';
import LoginPage from './pages/LoginPage';
import ProjectPage from './pages/ProjectPage';
import ProListPage from './pages/ProListPage';
import AnalysisPage from './pages/AnalysisPage';

export default function App() {
  const [userInfo, setUserInfo] = useState(null);
  const [activeTab, setActiveTab] = useState('projects'); // 'projects' | 'pro_list' | 'analysis'
  const [selectedPn, setSelectedPn] = useState('');
  const [selectedProjectName, setSelectedProjectName] = useState('');
  const [selectedServername, setSelectedServername] = useState('');
  const [selectedPof, setSelectedPof] = useState('');

  // 분석 페이지 전환 상태
  const [analysisTarget, setAnalysisTarget] = useState(null);
  const [analysisNavList, setAnalysisNavList] = useState([]);

  useEffect(() => {
    const storedUser = localStorage.getItem('userInfo');
    const token = localStorage.getItem('X-Auth-Token');
    if (storedUser && token) {
      try {
        setUserInfo(JSON.parse(storedUser));
      } catch {
        localStorage.removeItem('userInfo');
        localStorage.removeItem('X-Auth-Token');
      }
    }

    // 세션스토리지에서 마지막 작업 상태 복원 (새로고침 지원)
    try {
      const savedPn = sessionStorage.getItem('lastSelectedPn');
      const savedName = sessionStorage.getItem('lastSelectedProjectName');
      const savedTab = sessionStorage.getItem('lastActiveTab');
      const savedTarget = sessionStorage.getItem('lastAnalysisTarget');
      const savedNav = sessionStorage.getItem('lastAnalysisNavList');

      if (savedPn) {
        setSelectedPn(savedPn);
        setSelectedProjectName(savedName || savedPn);
        if (savedTab === 'analysis' && savedTarget) {
          setAnalysisTarget(JSON.parse(savedTarget));
          if (savedNav) setAnalysisNavList(JSON.parse(savedNav));
          setActiveTab('analysis');
        } else if (savedTab === 'pro_list') {
          setActiveTab('pro_list');
        }
      }
    } catch {}
  }, []);

  const handleLoginSuccess = (info) => {
    setUserInfo(info);
  };

  const handleLogout = () => {
    setUserInfo(null);
    setActiveTab('projects');
    setSelectedPn('');
    setSelectedProjectName('');
    setSelectedServername('');
    setSelectedPof('');
    setAnalysisTarget(null);
    setAnalysisNavList([]);
    try {
      sessionStorage.removeItem('lastSelectedPn');
      sessionStorage.removeItem('lastSelectedProjectName');
      sessionStorage.removeItem('lastActiveTab');
      sessionStorage.removeItem('lastAnalysisTarget');
      sessionStorage.removeItem('lastAnalysisNavList');
    } catch {}
  };

  const handleSelectProject = (pn, projectName, servername = '', pof = '') => {
    if (pn) {
      setSelectedPn(pn);
      setSelectedProjectName(projectName || pn);
      setSelectedServername((servername || '').toLowerCase());
      setSelectedPof(pof || '');
      setActiveTab('pro_list');
      try {
        sessionStorage.setItem('lastSelectedPn', pn);
        sessionStorage.setItem('lastSelectedProjectName', projectName || pn);
        sessionStorage.setItem('lastActiveTab', 'pro_list');
      } catch {}
    }
  };

  const handleOpenAnalysis = (target, navList) => {
    setAnalysisTarget(target);
    setAnalysisNavList(navList || []);
    setActiveTab('analysis');
    try {
      sessionStorage.setItem('lastActiveTab', 'analysis');
      sessionStorage.setItem('lastAnalysisTarget', JSON.stringify(target));
      sessionStorage.setItem('lastAnalysisNavList', JSON.stringify(navList || []));
    } catch {}
  };

  const handleBackToList = () => {
    setActiveTab('pro_list');
    try {
      sessionStorage.setItem('lastActiveTab', 'pro_list');
    } catch {}
  };

  const handleBackToProjects = () => {
    setActiveTab('projects');
    try {
      sessionStorage.setItem('lastActiveTab', 'projects');
    } catch {}
  };

  if (!userInfo) {
    return <LoginPage onLoginSuccess={handleLoginSuccess} />;
  }

  return (
    <>
      {/* 1. 프로젝트 목록 화면 */}
      {activeTab === 'projects' && (
        <ProjectPage
          userInfo={userInfo}
          onLogout={handleLogout}
          onSelectProject={handleSelectProject}
        />
      )}

      {/* 2. 문항 목록 화면 (U1: 분석 화면 전환 시에도 언마운트하지 않고 hidden 처리하여 검색/스크롤 보존) */}
      {selectedPn && (
        <div style={{ display: activeTab === 'pro_list' ? 'block' : 'none', height: '100%' }}>
          <ProListPage
            userInfo={userInfo}
            selectedPn={selectedPn}
            selectedProjectName={selectedProjectName}
            selectedServername={selectedServername}
            selectedPof={selectedPof}
            onBackToProjects={handleBackToProjects}
            onLogout={handleLogout}
            onOpenAnalysis={handleOpenAnalysis}
          />
        </div>
      )}

      {/* 3. 분석 화면 */}
      {activeTab === 'analysis' && (
        <AnalysisPage
          userInfo={userInfo}
          projectnum={selectedPn}
          projectName={selectedProjectName}
          initialTarget={analysisTarget}
          navList={analysisNavList}
          onBackToList={handleBackToList}
          onBackToProjects={handleBackToProjects}
          onLogout={handleLogout}
        />
      )}
    </>
  );
}
