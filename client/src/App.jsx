import { Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from './context/AuthContext'
import Layout from './components/Layout'
import ProtectedRoute, { RoleGate } from './components/ProtectedRoute'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Employees from './pages/Employees'
import Departments from './pages/Departments'
import Branches from './pages/Branches'
import Devices from './pages/Devices'
import Attendance from './pages/Attendance'
import ErrorLog from './pages/ErrorLog'
import DailyReport from './pages/DailyReport'
import SummaryReport from './pages/SummaryReport'
import Holidays from './pages/Holidays'
import Profile from './pages/Profile'
import MyAttendance from './pages/MyAttendance'
import PendingApprovals from './pages/PendingApprovals'
import UnregisteredIds from './pages/UnregisteredIds'

const MGMT = ['Admin', 'HR']

function Manager({ children }) {
  return <RoleGate roles={MGMT}>{children}</RoleGate>
}

function Home() {
  const { isManager } = useAuth()
  return isManager ? <Dashboard /> : <Navigate to="/my-attendance" replace />
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />

      <Route
        element={
          <ProtectedRoute>
            <Layout />
          </ProtectedRoute>
        }
      >
        <Route index element={<Home />} />

        <Route path="attendance" element={<Manager><Attendance /></Manager>} />
        <Route path="unregistered" element={<Manager><UnregisteredIds /></Manager>} />
        <Route path="employees" element={<Manager><Employees /></Manager>} />
        <Route path="departments" element={<Manager><Departments /></Manager>} />
        <Route path="branches" element={<Manager><Branches /></Manager>} />
        <Route path="devices" element={<Manager><Devices /></Manager>} />
        <Route path="holidays" element={<Manager><Holidays /></Manager>} />
        <Route path="errors" element={<Manager><ErrorLog /></Manager>} />
        <Route path="reports/daily" element={<Manager><DailyReport /></Manager>} />
        <Route path="reports/summary" element={<Manager><SummaryReport /></Manager>} />

        <Route path="employees/pending" element={<RoleGate roles={['Admin']}><PendingApprovals /></RoleGate>} />
        <Route path="profile" element={<Profile />} />
        <Route path="my-attendance" element={<MyAttendance />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
