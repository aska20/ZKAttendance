# ZKAttendance — Web client

React single-page app (Vite + JavaScript + Tailwind CSS v4) for the
ZKAttendance API. The backend is `ZKAttendance.Api` in the repo root.

## Running

```bash
cd client
npm install
cp .env.example .env      # adjust if your API runs on a different port
npm run dev               # http://localhost:5173
```

The dev server proxies `/api/*` to the backend (`VITE_API_PROXY_TARGET`,
default `http://localhost:5107`), so start `ZKAttendance.Api` first:

```bash
cd ../ZKAttendance.Api
dotnet run
```

## Configuration

| Variable | Meaning |
|---|---|
| `VITE_API_URL` | Base path the app calls. `/api` in dev (uses the proxy). In production, the deployed API origin, e.g. `https://zkattendance-api.onrender.com`. |
| `VITE_API_PROXY_TARGET` | Where the dev proxy forwards `/api`. Ignored in production builds. |

## Layout

```
src/
  api/
    client.js       axios instance — Bearer injection, 401 -> refresh-token retry
    tokenStore.js   localStorage persistence (single source of truth)
    resources.js    per-resource request helpers (departments, branches, …)
    auth.js         login / revoke
  context/
    AuthContext.jsx sign-in / sign-out, current user + role + isManager
  components/
    Layout.jsx          sidebar shell; manager vs employee nav
    ProtectedRoute.jsx   auth gate + <RoleGate> for per-route roles
    ui.jsx               shared primitives (Table, Modal, Field, Button, …)
  hooks/useAsync.js      load / loading / error / reload
  pages/
    Login, Dashboard, Employees, Departments, WorkShifts, Branches,
    Devices, AttendanceLog, DailyReport, RangeReport, UnregisteredIds,
    Profile, RegisterUser, MyAttendance
```

## Auth flow

1. `POST /api/Auth/login` returns `{ accessToken, refreshToken, expiresAt, username, role }`.
2. Both tokens are stored in `localStorage` under `zkattendance.auth`.
3. Every request carries `Authorization: Bearer <accessToken>`.
4. On a `401`, the client calls `POST /api/Auth/refresh` once, stores the
   rotated pair, and replays the original request. If refresh fails, the
   session is cleared and the user is sent to `/login`.

Only `Admin` and `HR` accounts can reach the current screens (matches the
`[Authorize(Roles = "Admin,HR")]` on the API controllers).

## Ported so far

Dashboard, Employees, Departments, Work Shifts, Branches, Devices (incl.
test-connection / sync / deactivate), Attendance Logs (filtered + paged),
Daily Report, Range Report, Unregistered Biometric IDs, Profile + change
password, Register User, My Attendance.

## Not yet ported

- **Report export** to Excel / PDF — was a `// TODO` in the old code too
  (`ExcelReportService` / `PdfReportService` are wired but unimplemented).
- **Employee ⇄ device enrolment UI** — the API exists
  (`POST /api/Employees/{id}/enroll-on-devices`) but there is no screen yet.
- **Manual attendance entry** — API exists (`POST /api/Attendance/manual`),
  no screen.
