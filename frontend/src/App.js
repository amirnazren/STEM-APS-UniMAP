/**
 * @file Root component — client-side routing for the Teacher Dashboard.
 *
 * Routes
 * ------
 *  - `/`                    {@link TeacherLogin}     teacher signs in with a room PIN or creates a room
 *  - `/dashboard/:roomPin`  {@link TeacherDashboard} main teacher view with controls & analytics
 *  - `/class-hud/:roomPin`  {@link ClassroomHUD}     full-screen projector view (no controls)
 */
import React from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import TeacherLogin from './components/Dashboard/TeacherLogin';
import TeacherDashboard from './components/Dashboard/TeacherDashboard';
import ClassroomHUD from './components/HUD/ClassroomHUD';

/**
 * Application shell.
 * Unknown URLs redirect to the login page.
 * @returns {JSX.Element}
 */
function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<TeacherLogin />} />
        <Route path="/dashboard/:roomPin" element={<TeacherDashboard />} />
        <Route path="/class-hud/:roomPin" element={<ClassroomHUD />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
