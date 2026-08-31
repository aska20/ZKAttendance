import api from './client'

// GET /api/Dashboard/summary
export async function getDashboardSummary() {
  const { data } = await api.get('/Dashboard/summary')
  return data
}
