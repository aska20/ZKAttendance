import { useCallback, useEffect, useState } from 'react'
import { apiErrorMessage } from '../lib/errors'

// Run an async loader, track loading/error/data, and expose a reload().
export function useAsync(loader, deps = []) {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const run = useCallback(() => {
    setLoading(true)
    setError('')
    return loader()
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  useEffect(() => {
    run()
  }, [run])

  return { data, loading, error, reload: run, setData }
}
