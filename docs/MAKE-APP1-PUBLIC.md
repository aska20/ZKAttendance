# Making App1 reachable by App2

App1 currently listens on `localhost:5107`. `localhost` means *this computer only*,
so App2 on another machine cannot reach it. Three ways to fix that, easiest first.

---

## Option A — same office network (do this first)

Fastest way to prove the two apps talk. No hosting, no accounts.

**1. Listen on every network address**

`ZKAttendance.Api/Properties/launchSettings.json`:

```json
"applicationUrl": "http://0.0.0.0:5107"
```

`0.0.0.0` means "any address this machine has", not just loopback.

**2. Find the machine's LAN address**

```
ipconfig        # Windows, look for IPv4 Address, e.g. 192.168.1.50
```

**3. Open the port in Windows Firewall**

```powershell
New-NetFirewallRule -DisplayName "ZKAttendance API" -Direction Inbound `
  -LocalPort 5107 -Protocol TCP -Action Allow
```

Without this the connection is refused with no useful error, and it is the single
most common reason "it works on my machine" fails here.

**4. Test from the other PC**

```
curl http://192.168.1.50:5107/swagger/index.html
```

App2 then uses `http://192.168.1.50:5107` as its base URL.

**Limitation:** only works inside the office. Fine for building and testing App2.

---

## Option B — Cloudflare Tunnel (for the demo)

Gives a real `https://` address pointing at your machine. No router config, no
static IP, free.

```
winget install --id Cloudflare.cloudflared
cloudflared tunnel --url http://localhost:5107
```

It prints something like:

```
https://random-words-here.trycloudflare.com
```

That URL now reaches App1 from anywhere. Put it in App2's config as the base URL.

**Caveats:** the quick tunnel's address changes every restart, and it stops when you
close the terminal. For a stable address, create a named tunnel with a Cloudflare
account — still free.

`ngrok http 5107` does the same thing if you prefer it.

---

## Option C — a real VPS

For actual deployment. Half a day's work.

1. Publish: `dotnet publish -c Release`
2. Copy to the server, run behind IIS or Nginx
3. HTTPS certificate via Let's Encrypt
4. SQL Server reachable from the host, or move the database with it
5. Update the connection string
6. Keep `DeviceProtocol` as `Fake` on the public box — it can never reach the
   devices anyway. Only App2 talks to hardware.

---

## Changes App1 needs in all three cases

### CORS

`appsettings.json`:

```json
"Cors": {
  "AllowedOrigins": [ "http://localhost:5173", "http://192.168.1.60:5200" ]
}
```

Add App2's address. **Note:** CORS only affects browsers. App2's server-side
`HttpClient` calls are unaffected, so this only matters if App2 has a web UI that
calls App1 directly from the browser.

### Agent authentication

Already built. `LocalServers` holds an agent key and a salted hash of its secret,
and `/api/Agent/*` verifies both on every call. Nothing to configure — just register
an agent and copy its credentials into App2.

### What NOT to expose

Once App1 is on a public address, everything on it is reachable. Before any real
deployment:

- Change the default admin password
- Turn off Swagger outside Development
- Keep the Hangfire dashboard Admin-only (already done)
- Use HTTPS, not plain HTTP, so agent secrets are not sent in the clear

---

## Registering an agent

In App1, as Admin:

```
POST /api/LocalServers
{ "serverName": "Head office agent", "branchId": 1 }
```

The reply contains `agentKey` and `secret`. **The secret is shown once and never
again** — only its hash is stored. Copy it straight into App2's config.

If you lose it: `POST /api/LocalServers/{id}/rotate-secret` issues a new one. The old
one stops working immediately, so update App2 before its next sync.

### Pointing devices at the agent

```
POST /api/LocalServers/assign-device?deviceId=1&localServerId=1
```

Not strictly required. An agent already sees any **unassigned** device in its own
branch, so an existing setup works the moment an agent is registered. Assign
explicitly when one branch has two agents.

---

## Quick check that it is all wired

```bash
# 1. reachable at all
curl http://192.168.1.50:5107/swagger/index.html

# 2. agent credentials valid
curl -X POST http://192.168.1.50:5107/api/Agent/login \
  -H "Content-Type: application/json" \
  -d '{"agentKey":"agt_...","secret":"...","agentVersion":"0.1"}'

# 3. agent can see its devices
curl "http://192.168.1.50:5107/api/Agent/devices?agentKey=agt_...&secret=..."
```

If all three work, App2 has everything it needs. Nothing else in App1 has to change.
