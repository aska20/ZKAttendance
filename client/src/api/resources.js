import api from './client'

const get = (url, params) => api.get(url, { params }).then((r) => r.data)
const post = (url, body) => api.post(url, body).then((r) => r.data)
const put = (url, body) => api.put(url, body).then((r) => r.data)
const del = (url) => api.delete(url).then((r) => r.data)

// ── Departments ──────────────────────────────────────────────
export const departments = {
  list: () => get('/Departments'),
  get: (id) => get(`/Departments/${id}`),
  create: (b) => post('/Departments', b),
  update: (id, b) => put(`/Departments/${id}`, b),
  remove: (id) => del(`/Departments/${id}`),
}

// ── Branches ─────────────────────────────────────────────────
export const branches = {
  list: (withDevices) => get('/Branches', withDevices ? { withDevices: true } : undefined),
  get: (id) => get(`/Branches/${id}`),
  create: (b) => post('/Branches', b),
  update: (id, b) => put(`/Branches/${id}`, b),
  remove: (id) => del(`/Branches/${id}`),
}

// ── Devices ──────────────────────────────────────────────────
export const devices = {
  list: (onlineOnly) => get('/Devices', onlineOnly ? { onlineOnly: true } : undefined),
  get: (id) => get(`/Devices/${id}`),
  create: (b) => post('/Devices', b),
  update: (id, b) => put(`/Devices/${id}`, b),
  deactivate: (id) => post(`/Devices/${id}/deactivate`),
  testConnection: (id) => post(`/Devices/${id}/test-connection`),
  sync: (id) => post(`/Devices/${id}/sync`),
}

// ── Employees ────────────────────────────────────────────────
export const employees = {
  list: (params) => get('/Employees', typeof params === 'object' ? params : (params ? { departmentId: params } : undefined)),
  get: (id) => get(`/Employees/${id}`),
  create: (b) => post('/Employees', b),
  update: (id, b) => put(`/Employees/${id}`, b),
  deactivate: (id) => post(`/Employees/${id}/deactivate`),
  remove: (id) => del(`/Employees/${id}`),
  unregistered: () => get('/Employees/unregistered'),
  enrollOnDevices: (id, b) => post(`/Employees/${id}/enroll-on-devices`, b),
  pending: () => get('/Employees/pending'),
  approve: (id) => post(`/Employees/${id}/approve`),
  reject: (id, reason) => post(`/Employees/${id}/reject`, { reason }),
  createLogin: (id, b) => post(`/Employees/${id}/create-login`, b),
}

// ── Notifications ────────────────────────────────────────────
export const notifications = {
  list: (params) => get('/Notifications', params),
  unreadCount: () => get('/Notifications/unread-count'),
  markRead: (id) => post(`/Notifications/${id}/read`),
  markAllRead: () => post('/Notifications/read-all'),
}

// ── Attendance ───────────────────────────────────────────────
export const attendance = {
  log: (params) => get('/Attendance/log', params),
  logFilters: () => get('/Attendance/log/filters'),
  my: (params) => get('/Attendance/my', params),
  manual: (b) => post('/Attendance/manual', b),
  punches: (params) => get('/Attendance/punches', params),
  deletePunch: (logId) => del(`/Attendance/punches/${logId}`),
  purge: (params) => post(`/Attendance/punches/purge?${new URLSearchParams(params)}`),
  day: (employeeId, date) => get('/Attendance/day', { employeeId, date }),
}

// ── Overview (pivot) ─────────────────────────────────────────
export const overview = {
  get: (params) => get('/Overview', params),
}

// ── Holidays ─────────────────────────────────────────────────
export const holidays = {
  list: (params) => get('/Holidays', params),
  create: (b) => post('/Holidays', b),
  removeOnDate: (date) => del(`/Holidays/on/${date}`),
}

// ── Error log ────────────────────────────────────────────────
export const errorLog = {
  list: (params) => get('/ErrorLog', params),
  openCount: () => get('/ErrorLog/open-count'),
  resolve: (id, resolution) => post(`/ErrorLog/${id}/resolve`, { resolution }),
  clearResolved: () => post('/ErrorLog/clear-resolved'),
}

// ── Reports ──────────────────────────────────────────────────
export const reports = {
  daily: (params) => get('/Reports/daily', params),
  range: (params) => get('/Reports/range', params),
  dailySummary: (params) => get('/Reports/daily-summary', params),
}

// ── Account / users ──────────────────────────────────────────
export const account = {
  profile: () => get('/Account/profile'),
  changePassword: (b) => post('/Account/change-password', b),
}

export const users = {
  register: (b) => post('/Auth/register', b),
}

// ── Dashboard ────────────────────────────────────────────────
export const dashboard = {
  summary: () => get('/Dashboard/summary'),
}
