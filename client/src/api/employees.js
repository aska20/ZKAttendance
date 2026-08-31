import api from './client'

// GET /api/Employees?departmentId=
export async function listEmployees(departmentId) {
  const { data } = await api.get('/Employees', {
    params: departmentId ? { departmentId } : undefined,
  })
  return data
}

// GET /api/Employees/{id}
export async function getEmployee(id) {
  const { data } = await api.get(`/Employees/${id}`)
  return data
}

// POST /api/Employees
export async function createEmployee(payload) {
  const { data } = await api.post('/Employees', payload)
  return data
}

// PUT /api/Employees/{id}
export async function updateEmployee(id, payload) {
  const { data } = await api.put(`/Employees/${id}`, payload)
  return data
}

// POST /api/Employees/{id}/deactivate
export async function deactivateEmployee(id) {
  const { data } = await api.post(`/Employees/${id}/deactivate`)
  return data
}

// GET /api/Departments  (for the department picker)
export async function listDepartments() {
  const { data } = await api.get('/Departments')
  return data
}
