// The API returns errors as { message, detail }. Fall back sensibly when it
// doesn't (network error, unexpected 500 body).
export function apiErrorMessage(error, fallback = 'Something went wrong') {
  return (
    error?.response?.data?.message ||
    error?.response?.data?.title ||
    error?.message ||
    fallback
  )
}
