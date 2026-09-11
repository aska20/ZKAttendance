# App2 will not start

## "Windows Firewall asks about Public and Private networks"

**That is expected. Tick "Private networks" and click Allow.**

It is not an error. Both apps bind to `0.0.0.0` so the other machine can reach
them, and Windows always asks before letting a new program accept connections
from the network.

- **Private networks** — your office LAN. Tick this.
- **Public networks** — cafes, airports. Leave unticked.

If you clicked Cancel by mistake, nothing will be able to connect. Fix it with:

```powershell
New-NetFirewallRule -DisplayName "ZKAttendance Agent" -Direction Inbound `
  -LocalPort 5200 -Protocol TCP -Action Allow -Profile Private
New-NetFirewallRule -DisplayName "ZKAttendance API" -Direction Inbound `
  -LocalPort 5107 -Protocol TCP -Action Allow -Profile Private
```

Run PowerShell as Administrator.

---

## Build fails

### Restore the packages first

```
dotnet restore
dotnet build
```

App2 uses only packages App1 already uses, at the same versions, so nothing new
downloads for it. **App1 does need two new ones** — `Hangfire.AspNetCore` and
`Hangfire.SqlServer` 1.8.14. If restore cannot reach nuget.org, App1 fails to
build and the whole solution goes red with it.

Check the internet is reachable, then:

```
dotnet nuget locals all --clear
dotnet restore --force
```

### If only App1 fails and the errors mention Hangfire

The three Hangfire files are the only code here that was never compiled, because
NuGet was unreachable in the environment they were written in. If a signature is
wrong it will be in one of:

- `ZKAttendance.Api/Jobs/AttendanceDailyJob.cs`
- `ZKAttendance.Api/Security/HangfireAdminFilter.cs`
- the Hangfire block in `ZKAttendance.Api/Program.cs`

**To get building again while that is sorted out**, comment out the Hangfire
lines in `Program.cs` and remove the two `PackageReference` lines. Nothing else
depends on them: the end-of-day run is still on the API as
`POST /api/Attendance/daily/process`, and the buttons on the Monthly Report page
still work. Hangfire only decides *when* it runs automatically.

---

## Backend starts but the client shows nothing

### Check the backend is actually up

```
curl http://localhost:5200/api/agent/status
```

Expected while unconfigured:

```json
{ "configured": false, "message": "No agent key set..." }
```

If this fails, the backend is not running. Look at its console output.

### Check the database

App2 creates `ZKAgent` on first run using `ConnectionStrings:AgentConnection`,
which defaults to LocalDB. If LocalDB is not installed, the console shows
"Could not open the agent database".

Point it at the same SQL Server App1 uses instead:

```json
"AgentConnection": "Server=localhost;Database=ZKAgent;Trusted_Connection=True;TrustServerCertificate=True"
```

The agent needs permission to CREATE DATABASE the first time.

### Check the client is talking to the right port

`client/vite.config.js` proxies `/api` to `http://localhost:5200`. If you changed
App2's port, change it here too.

---

## Running the four pieces

```
cd ZKAttendance.Api            && dotnet run     :5107
cd client                      && npm run dev    :5173
cd ZKAttendance.Agent          && dotnet run     :5200
cd ZKAttendance.Agent/client   && npm install && npm run dev    :5201
```

`npm install` in App2's client is needed once. It is a separate app from App1's
client and has its own `node_modules`.

For the office PC, build the client once and let the agent serve it:

```
cd ZKAttendance.Agent/client && npm run build     # outputs to ../wwwroot
cd ZKAttendance.Agent        && dotnet run        # everything on :5200
```

---

## Order things must start in

1. **SQL Server** running
2. **App1** (`:5107`) — App2 asks it for the device list, so App2 is useless without it
3. **App2** (`:5200`)
4. The clients, in any order

App2 starting before App1 is fine. It will show "offline" and reconnect on its
own. Punches still queue in the outbox.

---

## Still stuck

Copy the **exact error text** from the console or the Error List. "Not working"
could be a restore failure, a port clash, a missing database or a compile error,
and the fix is different for each.
