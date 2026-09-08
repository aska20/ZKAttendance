using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Devices
{
    /// <summary>
    /// Talks to a ZKTeco terminal over TCP port 4370 using the vendor
    /// "standalone" protocol — no zkemkeeper.dll, no COM, no x86 requirement,
    /// runs anywhere .NET runs.
    ///
    /// This is a focused port of the connect / read-attendance / clock subset of
    /// the widely used <c>pyzk</c> library. It has NOT been tested against a
    /// physical device inside this repo. The protocol itself is stable and this
    /// follows pyzk closely, but firmware varies between models. Verify with one
    /// device using POST /api/Devices/{id}/test-connection and then
    /// POST /api/Devices/{id}/sync before relying on the 5-minute background job.
    ///
    /// Attendance records are parsed in the common 40-byte layout. If a device
    /// returns another record size the read throws with the size in the message
    /// so support for it can be added.
    /// </summary>
    public sealed class ZkTcpDeviceReader : IZkDeviceReader
    {
        // ── command codes ────────────────────────────────────────────────
        private const int CMD_CONNECT = 1000;
        private const int CMD_EXIT = 1001;
        private const int CMD_ENABLEDEVICE = 1002;
        private const int CMD_DISABLEDEVICE = 1003;
        private const int CMD_ACK_OK = 2000;
        private const int CMD_ACK_ERROR = 2001;
        private const int CMD_ACK_DATA = 2002;
        private const int CMD_ACK_UNAUTH = 2005;
        private const int CMD_PREPARE_DATA = 1500;
        private const int CMD_DATA = 1501;
        private const int CMD_FREE_DATA = 1502;
        private const int CMD_DATA_WRRQ = 1503;
        private const int CMD_DATA_RDY = 1504;
        private const int CMD_AUTH = 1102;
        private const int CMD_GET_TIME = 201;
        private const int CMD_SET_TIME = 202;
        private const int CMD_VERSION = 1100;
        private const int CMD_OPTIONS_RRQ = 11;
        private const int CMD_ATTLOG_RRQ = 13;
        private const int CMD_USER_WRQ = 8;
        private const int CMD_DB_RRQ = 7;
        private const int CMD_USERTEMP_RRQ = 9;
        private const int CMD_USERTEMP_WRQ = 10;
        private const int CMD_DELETE_USER = 18;
        private const int CMD_DELETE_USERTEMP = 19;
        private const int CMD_STARTVERIFY = 60;
        private const int CMD_STARTENROLL = 61;
        private const int CMD_CANCELCAPTURE = 62;
        private const int CMD_REFRESHDATA = 1013;

        // "function code" selectors for buffered reads
        private const int FCT_USER = 5;
        private const int FCT_FINGERTMP = 2;

        private const int USHRT_MAX = 65535;
        private const int MAX_CHUNK = 0xFFC0; // 65472

        private static readonly byte[] TcpTop = { 0x50, 0x50, 0x82, 0x7d };

        private readonly ILogger<ZkTcpDeviceReader> _logger;
        private int _commPassword;
        private readonly int _timeoutMs;

        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private int _sessionId;
        private int _replyId = USHRT_MAX - 1;
        private string _ip = "";
        private int _userPacketSize = 72;
        private int _templateFormatVersion = 10;
        private readonly Dictionary<string, int> _uidCache = new();

        public ZkTcpDeviceReader(ILogger<ZkTcpDeviceReader> logger, int commPassword = 0, int timeoutMs = 5000)
        {
            _logger = logger;
            _commPassword = commPassword;
            _timeoutMs = timeoutMs;
        }

        // ── IZkDeviceReader ──────────────────────────────────────────────

        public async Task<bool> ConnectAsync(string ip, int port, int commPassword = 0)
        {
            _ip = ip;
            _commPassword = commPassword;
            _tcp = new TcpClient { ReceiveTimeout = _timeoutMs, SendTimeout = _timeoutMs };

            using (var cts = new CancellationTokenSource(_timeoutMs))
            {
                try
                {
                    await _tcp.ConnectAsync(ip, port, cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TCP connect to {Ip}:{Port} failed", ip, port);
                    return false;
                }
            }

            _stream = _tcp.GetStream();
            _sessionId = 0;
            _replyId = USHRT_MAX - 1;
            _uidCache.Clear();

            // The handshake needs the same protection as the dial. A terminal
            // that accepts the socket and then goes quiet - wrong port, firewall,
            // half-open link, or another operator already mid-scan on it - used
            // to throw out of here and surface to the caller as a bare 500.
            // A device we cannot talk to is a false return, not an exception.
            try
            {
                var (code, payload) = await SendAsync(CMD_CONNECT, Array.Empty<byte>());

                if (code == CMD_ACK_UNAUTH)
                {
                    _logger.LogInformation("Device {Ip} requires a comm key; authenticating", ip);
                    var key = MakeCommKey(_commPassword, _sessionId);
                    (code, payload) = await SendAsync(CMD_AUTH, key);
                }

                if (code != CMD_ACK_OK)
                {
                    _logger.LogWarning(
                        "Device {Ip} refused the connection (code {Code}). A wrong comm key is the usual cause.",
                        ip, code);
                    await DisconnectAsync();
                    return false;
                }

                _ = payload;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Handshake with {Ip}:{Port} failed", ip, port);
                await DisconnectAsync();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_stream is not null)
                    await SendAsync(CMD_EXIT, Array.Empty<byte>());
            }
            catch { /* best effort */ }
            finally
            {
                _stream?.Dispose();
                _tcp?.Dispose();
                _stream = null;
                _tcp = null;
            }
        }

        public async Task<DeviceInfo?> GetDeviceInfoAsync()
        {
            if (_stream is null) return null;

            var serial = await GetOptionAsync("~SerialNumber") ?? "";
            var firmware = "";
            try
            {
                var (code, payload) = await SendAsync(CMD_VERSION, Array.Empty<byte>());
                if (code == CMD_ACK_OK) firmware = Ascii(payload);
            }
            catch { /* non-fatal */ }

            var deviceTime = await GetDeviceTimeAsync() ?? DateTime.Now;

            // Which fingerprint template dialect this unit speaks. Cached so
            // template propagation can refuse a v10 -> v9 write instead of
            // storing one that will never match.
            var fpVersion = await GetOptionAsync("~ZKFPVersion");
            if (int.TryParse(fpVersion, out var v) && (v == 9 || v == 10))
                _templateFormatVersion = v;

            return new DeviceInfo(serial, firmware, 0, 0, deviceTime);
        }

        public async Task<bool> SetDeviceTimeAsync(DateTime serverTime)
        {
            if (_stream is null) return false;
            var body = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(body, EncodeTime(serverTime));
            var (code, _) = await SendAsync(CMD_SET_TIME, body);
            return code == CMD_ACK_OK;
        }


        public async Task<List<DevicePunch>> GetAttendanceLogsAsync(DateTime? since = null)
        {
            if (_stream is null)
                throw new InvalidOperationException("Not connected");

            // Freeze the device while we read so its buffer does not move.
            await SafeSendAsync(CMD_DISABLEDEVICE);
            try
            {
                var buffer = await ReadWithBufferAsync(CMD_ATTLOG_RRQ);
                var punches = ParseAttendance(buffer);

                if (since is { } cutoff)
                    punches = punches.Where(p => p.PunchTime >= cutoff).ToList();

                _logger.LogInformation(
                    "ZkTcpDeviceReader read {Count} punch(es) from {Ip}", punches.Count, _ip);

                return punches.OrderBy(p => p.PunchTime).ToList();
            }
            finally
            {
                await SafeSendAsync(CMD_ENABLEDEVICE);
            }
        }

        /// <summary>
        /// Every user record on the terminal.
        ///
        /// Two packet layouts are in the wild: 28 bytes on older firmware and
        /// 72 bytes on everything current. The size is inferred from the total
        /// payload rather than from a firmware string, because the firmware
        /// string is not reliable across OEM rebadges.
        /// </summary>
        public async Task<List<DeviceUser>> GetUsersAsync()
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");

            await SafeSendAsync(CMD_DISABLEDEVICE);
            try
            {
                // CMD_USERTEMP_RRQ is the command being buffer-read; the transport
                // command (CMD_DATA_WRRQ) is applied inside ReadWithBufferAsync.
                var buffer = await ReadWithBufferAsync(CMD_USERTEMP_RRQ, FCT_USER);

                // Parsing lives in a synchronous helper: Span<byte> cannot be a
                // local in an async method, and copying the buffer just to keep
                // the parse inline would double the allocation on a device with
                // a few thousand users.
                var users = ParseUsers(buffer, out var recordSize);
                if (recordSize > 0) _userPacketSize = recordSize;

                _logger.LogInformation("Read {Count} user(s) from {Ip}", users.Count, _ip);
                return users;
            }
            finally
            {
                await SafeSendAsync(CMD_ENABLEDEVICE);
            }
        }

        /// <summary>
        /// Two packet layouts are in the wild: 28 bytes on older firmware and
        /// 72 bytes on everything current. The size is inferred from the total
        /// payload rather than from a firmware string, because the firmware
        /// string is not reliable across OEM rebadges.
        /// </summary>
        private List<DeviceUser> ParseUsers(byte[] buffer, out int recordSize)
        {
            recordSize = 0;
            var users = new List<DeviceUser>();
            if (buffer.Length <= 4) return users;

            var declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0));
            var body = buffer.AsSpan(4);
            if (declared <= 0 || declared > body.Length) declared = body.Length;
            if (declared == 0) return users;

            if (declared % 72 == 0) recordSize = 72;
            else if (declared % 28 == 0) recordSize = 28;
            else
            {
                _logger.LogWarning(
                    "Device {Ip} returned {Bytes} bytes of user data \u2014 neither a 28- nor 72-byte multiple; skipping",
                    _ip, declared);
                return users;
            }

            for (var off = 0; off + recordSize <= declared; off += recordSize)
            {
                var rec = body.Slice(off, recordSize);
                var uid = BinaryPrimitives.ReadUInt16LittleEndian(rec);
                var privilege = rec[2];

                string name, userId;
                if (recordSize == 72)
                {
                    name = NullTerminated(rec.Slice(11, 24));
                    userId = NullTerminated(rec.Slice(48, 24));
                }
                else
                {
                    name = NullTerminated(rec.Slice(8, 8));
                    userId = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(24, 4)).ToString();
                }

                if (string.IsNullOrWhiteSpace(userId)) userId = uid.ToString();
                if (string.IsNullOrWhiteSpace(name)) name = $"User {userId}";

                // The high bit of privilege is the "disabled" flag on most
                // firmware; the low nibble carries the role (0 user, 14 admin).
                users.Add(new DeviceUser(userId, name, privilege & 0x0F, (privilege & 0x80) == 0)
                {
                    Uid = uid
                });
            }

            return users;
        }

        // ── write half ──────────────────────────────────────────────────

        public async Task<bool> SetUserAsync(
            string biometricUserId,
            string name,
            int privilege = 0,
            string password = "",
            bool enabled = true)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (string.IsNullOrWhiteSpace(biometricUserId))
                throw new ArgumentException("A biometric / enrol number is required", nameof(biometricUserId));

            // Reuse the slot if this enrol number is already on the device,
            // otherwise take the next free one. Writing to an occupied slot
            // would silently overwrite a different person.
            var uid = await ResolveUidAsync(biometricUserId, allocateIfMissing: true);
            if (uid <= 0)
            {
                _logger.LogWarning("No free user slot on {Ip} for enrol number {Id}", _ip, biometricUserId);
                return false;
            }

            var priv = (byte)((privilege & 0x0F) | (enabled ? 0x00 : 0x80));

            byte[] body;
            if (_userPacketSize == 28)
            {
                body = new byte[28];
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), (ushort)uid);
                body[2] = priv;
                WriteFixedAscii(body.AsSpan(3, 5), password);
                WriteFixedAscii(body.AsSpan(8, 8), name);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), 0); // card
                body[21] = 0;                                                 // group
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(22), 0); // timezone
                BinaryPrimitives.WriteUInt32LittleEndian(
                    body.AsSpan(24),
                    uint.TryParse(biometricUserId, out var n) ? n : (uint)uid);
            }
            else
            {
                body = new byte[72];
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), (ushort)uid);
                body[2] = priv;
                WriteFixedAscii(body.AsSpan(3, 8), password);
                WriteFixedAscii(body.AsSpan(11, 24), name);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(35), 0); // card
                body[39] = 0;                                                 // padding
                WriteFixedAscii(body.AsSpan(40, 7), "0");                     // group
                body[47] = 0;                                                 // padding
                WriteFixedAscii(body.AsSpan(48, 24), biometricUserId);
            }

            await SafeSendAsync(CMD_DISABLEDEVICE);
            try
            {
                var (code, _) = await SendAsync(CMD_USER_WRQ, body);
                if (code != CMD_ACK_OK)
                {
                    _logger.LogWarning("Device {Ip} rejected the user write for {Id} (code {Code})", _ip, biometricUserId, code);
                    return false;
                }
            }
            finally
            {
                await SafeSendAsync(CMD_ENABLEDEVICE);
            }

            await RefreshDataAsync();
            _uidCache[biometricUserId] = uid;
            _logger.LogInformation("Wrote user {Id} ('{Name}') to {Ip} in slot {Uid}", biometricUserId, name, _ip, uid);
            return true;
        }

        public async Task<bool> DeleteUserAsync(string biometricUserId)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");

            var uid = await ResolveUidAsync(biometricUserId, allocateIfMissing: false);
            if (uid <= 0) return false;

            var body = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)uid);

            var (code, _) = await SendAsync(CMD_DELETE_USER, body);
            if (code != CMD_ACK_OK) return false;

            _uidCache.Remove(biometricUserId);
            await RefreshDataAsync();
            return true;
        }

        /// <summary>
        /// Puts the terminal into capture mode. On a fingerprint unit the
        /// screen changes to "place finger"; on a face/palm unit it opens the
        /// registration camera. The command returns immediately — the person
        /// then has as long as the terminal's own timeout allows.
        /// </summary>
        public async Task<EnrollResult> StartRemoteEnrollAsync(string biometricUserId, int fingerIndex = 0)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (fingerIndex is < 0 or > 9)
                return new EnrollResult(false, "Finger index must be 0-9.", fingerIndex);

            // A terminal already waiting on a scan will refuse a second request.
            await CancelCaptureAsync();

            // TCP firmware takes the enrol number as a 24-byte string;
            // the older serial framing took it as a 4-byte int.
            var body = new byte[26];
            WriteFixedAscii(body.AsSpan(0, 24), biometricUserId);
            body[24] = (byte)fingerIndex;
            body[25] = 1; // 1 = start

            var (code, _) = await SendAsync(CMD_STARTENROLL, body);
            if (code != CMD_ACK_OK)
            {
                _logger.LogWarning(
                    "Device {Ip} refused enrolment for {Id} (code {Code}). The user record must exist on the device first.",
                    _ip, biometricUserId, code);
                return new EnrollResult(false,
                    "The terminal refused to start enrolment. Check the user exists on it and that no one else is mid-scan.",
                    fingerIndex);
            }

            _logger.LogInformation("Enrolment started on {Ip} for {Id}, finger {Finger}", _ip, biometricUserId, fingerIndex);
            return new EnrollResult(true, "The terminal is waiting for the scan.", fingerIndex);
        }

        public async Task<bool> CancelCaptureAsync()
        {
            if (_stream is null) return false;
            try
            {
                var (code, _) = await SendAsync(CMD_CANCELCAPTURE, Array.Empty<byte>());
                return code == CMD_ACK_OK;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "cancel-capture failed (non-fatal)");
                return false;
            }
        }

        /// <summary>
        /// Reads every stored template, then filters. Terminals expose the
        /// template table as one blob; there is no per-user read that works
        /// across firmware families, so one bulk read and a filter is both
        /// simpler and more portable than guessing at the per-user command.
        /// </summary>
        public async Task<List<FingerTemplate>> GetTemplatesAsync(string? biometricUserId = null)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");

            // Uid -> enrol number, so templates can be attributed to a person.
            var users = await GetUsersAsync();
            var byUid = users.ToDictionary(u => u.Uid, u => u.BiometricUserId);

            await SafeSendAsync(CMD_DISABLEDEVICE);
            try
            {
                var buffer = await ReadWithBufferAsync(CMD_DB_RRQ, FCT_FINGERTMP);
                var result = ParseTemplates(buffer, byUid, biometricUserId);

                _logger.LogInformation(
                    "Read {Count} template(s) from {Ip}{Filter}",
                    result.Count, _ip, biometricUserId is null ? "" : $" for {biometricUserId}");

                return result;
            }
            finally
            {
                await SafeSendAsync(CMD_ENABLEDEVICE);
            }
        }

        /// <summary>
        /// The template table is one blob of variable-length records:
        ///     [size:u16][uid:u16][fingerIndex:i8][valid:i8][template...]
        /// Sync, for the same Span reason as ParseUsers.
        /// </summary>
        private List<FingerTemplate> ParseTemplates(
            byte[] buffer, Dictionary<int, string> byUid, string? filterUserId)
        {
            var result = new List<FingerTemplate>();
            if (buffer.Length <= 4) return result;

            var declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0));
            var body = buffer.AsSpan(4);
            if (declared <= 0 || declared > body.Length) declared = body.Length;

            var offset = 0;
            while (offset + 6 <= declared)
            {
                var size = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset, 2));
                if (size < 6 || offset + size > declared) break;

                var uid = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset + 2, 2));
                var fid = (sbyte)body[offset + 4];
                var valid = body[offset + 5];

                var data = body.Slice(offset + 6, size - 6).ToArray();
                var enrolNumber = byUid.TryGetValue(uid, out var u) ? u : uid.ToString();

                if (filterUserId is null ||
                    string.Equals(enrolNumber, filterUserId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new FingerTemplate(enrolNumber, fid, data, _templateFormatVersion)
                    {
                        Uid = uid,
                        Valid = valid != 0
                    });
                }

                offset += size;
            }

            return result;
        }

        /// <summary>
        /// Writes one cached template onto this terminal.
        ///
        /// HARDWARE VERIFICATION NEEDED
        /// ----------------------------
        /// Everything else in this class either follows a byte layout that is
        /// stable across the ZKTeco range or is a fixed-size command. Template
        /// WRITE is the one operation whose framing varies by firmware family,
        /// and it has not been exercised against a physical unit from this
        /// repository. Test it on one slave device before trusting automatic
        /// provisioning; DeviceEnrollmentOrchestrator is written so that a
        /// failure here degrades to "user created, finger still to be enrolled
        /// on this device" rather than to a silent gap.
        /// </summary>
        public async Task<bool> SetTemplateAsync(FingerTemplate template)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");
            if (template.Data.Length == 0) return false;

            if (template.FormatVersion != _templateFormatVersion)
            {
                _logger.LogWarning(
                    "Refusing to write a v{Src} template to {Ip}, which speaks v{Dst}. " +
                    "The template would be accepted and then never match.",
                    template.FormatVersion, _ip, _templateFormatVersion);
                return false;
            }

            var uid = await ResolveUidAsync(template.BiometricUserId, allocateIfMissing: false);
            if (uid <= 0)
            {
                _logger.LogWarning(
                    "Cannot write a template for {Id} to {Ip}: the user does not exist there yet",
                    template.BiometricUserId, _ip);
                return false;
            }

            // record = [size:u16][uid:u16][fingerIndex:i8][valid:i8][template…]
            var size = template.Data.Length + 6;
            var record = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0), (ushort)size);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), (ushort)uid);
            record[4] = (byte)template.FingerIndex;
            record[5] = (byte)(template.Valid ? 1 : 0);
            Buffer.BlockCopy(template.Data, 0, record, 6, template.Data.Length);

            await SafeSendAsync(CMD_DISABLEDEVICE);
            try
            {
                if (!await SendWithBufferAsync(record)) return false;

                var tail = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(0), (uint)record.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(4), (ushort)uid);
                BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(6), (ushort)template.FingerIndex);

                var (code, _) = await SendAsync(CMD_USERTEMP_WRQ, tail);
                if (code != CMD_ACK_OK)
                {
                    _logger.LogWarning(
                        "Device {Ip} rejected the template write for {Id} finger {Finger} (code {Code})",
                        _ip, template.BiometricUserId, template.FingerIndex, code);
                    return false;
                }
            }
            finally
            {
                await SafeSendAsync(CMD_ENABLEDEVICE);
            }

            await RefreshDataAsync();
            return true;
        }

        public async Task<bool> RefreshDataAsync()
        {
            if (_stream is null) return false;
            try
            {
                var (code, _) = await SendAsync(CMD_REFRESHDATA, Array.Empty<byte>());
                return code == CMD_ACK_OK;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "refresh-data failed (non-fatal)");
                return false;
            }
        }

        // ── write-half plumbing ─────────────────────────────────────────

        /// <summary>
        /// The mirror of ReadWithBufferAsync: announce a size, then stream the
        /// payload in chunks the firmware will accept.
        /// </summary>
        private async Task<bool> SendWithBufferAsync(byte[] payload)
        {
            const int chunk = 1024;

            var head = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)payload.Length);

            var (code, _) = await SendAsync(CMD_PREPARE_DATA, head);
            if (code != CMD_ACK_OK)
            {
                _logger.LogWarning("Device {Ip} would not accept a {Bytes}-byte buffer (code {Code})", _ip, payload.Length, code);
                return false;
            }

            for (var sent = 0; sent < payload.Length; sent += chunk)
            {
                var take = Math.Min(chunk, payload.Length - sent);
                var slice = new byte[take];
                Buffer.BlockCopy(payload, sent, slice, 0, take);

                var (cCode, _) = await SendAsync(CMD_DATA, slice);
                if (cCode != CMD_ACK_OK && cCode != CMD_DATA)
                {
                    _logger.LogWarning("Chunk write to {Ip} failed at byte {Offset} (code {Code})", _ip, sent, cCode);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Enrol number → the device's internal slot. Cached per connection,
        /// because every user write would otherwise re-read the whole table.
        /// </summary>
        private async Task<int> ResolveUidAsync(string biometricUserId, bool allocateIfMissing)
        {
            if (_uidCache.TryGetValue(biometricUserId, out var cached)) return cached;

            List<DeviceUser> users;
            try
            {
                users = await GetUsersAsync();
            }
            catch (Exception ex)
            {
                // Some firmware refuses a bulk user-table read. Without the table
                // we cannot see which slots are taken, but an enrol number that is
                // numeric almost always maps to the same slot on ZK hardware, so
                // fall back to that rather than failing enrolment outright.
                if (!allocateIfMissing) throw;

                if (int.TryParse(biometricUserId, out var numeric) && numeric is > 0 and <= 65534)
                {
                    _logger.LogWarning(ex,
                        "Could not read the user table from {Ip}; assuming slot {Uid} for enrol number {Id}",
                        _ip, numeric, biometricUserId);
                    _uidCache[biometricUserId] = numeric;
                    return numeric;
                }

                throw new IOException(
                    $"The user table on {_ip} could not be read, and enrol number '{biometricUserId}' " +
                    "is not numeric, so no device slot can be worked out. Enrol this person on the terminal directly.", ex);
            }

            foreach (var u in users) _uidCache[u.BiometricUserId] = u.Uid;

            if (_uidCache.TryGetValue(biometricUserId, out var found)) return found;
            if (!allocateIfMissing) return -1;

            // Next free slot. Firmware indexes from 1; 0 is not a valid uid.
            var taken = new HashSet<int>(users.Select(u => u.Uid));
            for (var candidate = 1; candidate <= 65534; candidate++)
                if (!taken.Contains(candidate)) return candidate;

            return -1;
        }

        private static void WriteFixedAscii(Span<byte> target, string? value)
        {
            target.Clear();
            if (string.IsNullOrEmpty(value)) return;
            var bytes = Encoding.ASCII.GetBytes(value);
            var take = Math.Min(bytes.Length, target.Length - 1); // always null-terminated
            bytes.AsSpan(0, take).CopyTo(target);
        }

        public void Dispose()
        {
            try { DisconnectAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        }

        // ── protocol plumbing ───────────────────────────────────────────

        private async Task SafeSendAsync(int command)
        {
            try { await SendAsync(command, Array.Empty<byte>()); }
            catch (Exception ex) { _logger.LogDebug(ex, "command {Cmd} failed (non-fatal)", command); }
        }

        private async Task<(int code, byte[] payload)> ReadResponseAsync()
        {
            // Response: 8-byte TCP top (magic + declared length), then that many bytes.
            var top = await ReadExactAsync(8);
            if (top[0] != TcpTop[0] || top[1] != TcpTop[1] || top[2] != TcpTop[2] || top[3] != TcpTop[3])
                throw new IOException("Unexpected TCP header from device");

            var declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(top.AsSpan(4));
            var body = await ReadExactAsync(declared);

            // body = [cmd:2][checksum:2][session:2][replyId:2][payload...]
            var code = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0));
            _sessionId = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(4));
            _replyId = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(6));

            var payload = body.Length > 8 ? body[8..] : Array.Empty<byte>();
            return (code, payload);
        }

        /// <summary>
        /// Send one command packet and return (responseCode, payloadWithoutHeader).
        /// The full declared TCP frame is always drained, so the payload is complete.
        /// </summary>
        private async Task<(int code, byte[] payload)> SendAsync(int command, byte[] data)
        {
            if (_stream is null) throw new InvalidOperationException("Not connected");

            var header = BuildCommandPacket(command, data);
            var frame = new byte[8 + header.Length];
            Buffer.BlockCopy(TcpTop, 0, frame, 0, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)header.Length);
            Buffer.BlockCopy(header, 0, frame, 8, header.Length);

            await _stream.WriteAsync(frame);
            return await ReadResponseAsync();
        }

        private byte[] BuildCommandPacket(int command, byte[] data)
        {
            var buf = new byte[8 + data.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), (ushort)command);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), 0); // checksum placeholder
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(4), (ushort)_sessionId);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(6), (ushort)_replyId);
            if (data.Length > 0)
                Buffer.BlockCopy(data, 0, buf, 8, data.Length);

            var checksum = Checksum(buf);

            _replyId++;
            if (_replyId >= USHRT_MAX)
                _replyId -= USHRT_MAX;

            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), checksum);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(6), (ushort)_replyId);
            return buf;
        }

        private static ushort Checksum(byte[] p)
        {
            long sum = 0;
            int i = 0;
            for (; i + 1 < p.Length; i += 2)
                sum += p[i] | (p[i + 1] << 8);
            if (i < p.Length)
                sum += p[i];

            while (sum > USHRT_MAX)
                sum -= USHRT_MAX;

            sum = ~sum;
            while (sum < 0)
                sum += USHRT_MAX;

            return (ushort)sum;
        }

        private async Task<byte[]> ReadExactAsync(int count)
        {
            var buf = new byte[count];
            var read = 0;
            while (read < count)
            {
                using var cts = new CancellationTokenSource(_timeoutMs);
                var n = await _stream!.ReadAsync(buf.AsMemory(read, count - read), cts.Token);
                if (n == 0) throw new IOException("Device closed the connection");
                read += n;
            }
            return buf;
        }

        /// <summary>
        /// The "read a large object" dance: WRRQ, then either the data comes
        /// straight back (CMD_DATA) or the device announces a size and we pull it
        /// in <see cref="MAX_CHUNK"/>-sized pieces with CMD_DATA_RDY.
        /// Returns the raw payload (the leading 4-byte total-size word included).
        /// </summary>
        private async Task<byte[]> ReadWithBufferAsync(int command, int fct = 0, int ext = 0)
        {
            // pack('<b h i i', 1, command, fct, ext)
            var req = new byte[11];
            req[0] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(req.AsSpan(1), (short)command);
            BinaryPrimitives.WriteInt32LittleEndian(req.AsSpan(3), fct);
            BinaryPrimitives.WriteInt32LittleEndian(req.AsSpan(7), ext);

            var (code, payload) = await SendAsync(CMD_DATA_WRRQ, req);

            if (code == CMD_DATA)
                return payload; // small result, delivered inline

            if (code != CMD_ACK_OK && code != CMD_PREPARE_DATA)
                throw new IOException(
                    $"Device refused a buffered read of command {command} (reply code {code}). " +
                    "This firmware may not support bulk reads for that table.");

            if (payload.Length < 5)
                throw new IOException("Device announced a buffered read with no size");

            // payload = [0x00][size:uint32][...]
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1));

            var result = new byte[size];
            var written = 0;
            while (written < size)
            {
                var chunk = Math.Min(MAX_CHUNK, size - written);

                var rdy = new byte[8];
                BinaryPrimitives.WriteInt32LittleEndian(rdy.AsSpan(0), written);
                BinaryPrimitives.WriteInt32LittleEndian(rdy.AsSpan(4), chunk);

                var (cCode, cData) = await SendAsync(CMD_DATA_RDY, rdy);
                byte[] chunkPayload;
                if (cCode == CMD_PREPARE_DATA)
                {
                    // In TCP mode, the device follows CMD_PREPARE_DATA with a CMD_DATA frame and then CMD_ACK_OK
                    var (dCode, dData) = await ReadResponseAsync();
                    if (dCode != CMD_DATA)
                        throw new IOException($"Expected CMD_DATA in chunk read, got {dCode}");
                    chunkPayload = dData;

                    var (ackCode, _) = await ReadResponseAsync();
                    if (ackCode != CMD_ACK_OK)
                        _logger.LogDebug("Chunk read ACK returned code {AckCode}", ackCode);
                }
                else if (cCode == CMD_DATA)
                {
                    chunkPayload = cData;
                }
                else
                {
                    throw new IOException($"Chunk read returned code {cCode}");
                }

                var take = Math.Min(chunkPayload.Length, size - written);
                Buffer.BlockCopy(chunkPayload, 0, result, written, take);
                written += take;

                if (take == 0) break; // avoid a spin if the device stops sending
            }

            await SafeSendAsync(CMD_FREE_DATA);
            return result;
        }

        private static List<DevicePunch> ParseAttendance(byte[] buffer)
        {
            var punches = new List<DevicePunch>();
            if (buffer.Length < 4) return punches;

            var totalSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0));
            var records = buffer.AsSpan(4);
            if (totalSize <= 0 || records.Length < totalSize)
                totalSize = records.Length;

            if (totalSize == 0) return punches;
            if (totalSize % 40 != 0)
                throw new NotSupportedException(
                    $"Device returned {totalSize} bytes of attendance data, not a multiple of 40. " +
                    "Report this record size so the 16- or 8-byte layout can be added.");

            for (var off = 0; off + 40 <= totalSize; off += 40)
            {
                var rec = records.Slice(off, 40);

                var userId = NullTerminated(rec.Slice(2, 24));
                var status = rec[26];
                var t = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(27, 4));
                var punch = rec[31];

                var when = DecodeTime(t);
                if (when == default) continue;

                punches.Add(new DevicePunch(userId, when, status, punch, 0));
            }

            return punches;
        }

        // ── device options / time ───────────────────────────────────────

        private async Task<string?> GetOptionAsync(string name)
        {
            try
            {
                var body = Encoding.ASCII.GetBytes(name + "\0");
                var (code, payload) = await SendAsync(CMD_OPTIONS_RRQ, body);
                if (code != CMD_ACK_OK) return null;

                var text = Ascii(payload);           // e.g. "~SerialNumber=ABC123"
                var eq = text.IndexOf('=');
                return eq >= 0 ? text[(eq + 1)..].Trim() : null;
            }
            catch { return null; }
        }

        private async Task<DateTime?> GetDeviceTimeAsync()
        {
            try
            {
                var (code, payload) = await SendAsync(CMD_GET_TIME, Array.Empty<byte>());
                if (code == CMD_ACK_OK && payload.Length >= 4)
                    return DecodeTime(BinaryPrimitives.ReadUInt32LittleEndian(payload));
            }
            catch { /* ignore */ }
            return null;
        }

        // ── encoding helpers ────────────────────────────────────────────

        private static DateTime DecodeTime(uint t)
        {
            var second = (int)(t % 60); t /= 60;
            var minute = (int)(t % 60); t /= 60;
            var hour = (int)(t % 24); t /= 24;
            var day = (int)(t % 31) + 1; t /= 31;
            var month = (int)(t % 12) + 1; t /= 12;
            var year = (int)t + 2000;

            try { return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local); }
            catch { return default; }
        }

        private static uint EncodeTime(DateTime d)
        {
            var v = ((d.Year % 100) * 12 * 31 + (d.Month - 1) * 31 + d.Day - 1) * 24L * 60 * 60
                    + (d.Hour * 60 + d.Minute) * 60 + d.Second;
            return (uint)v;
        }

        /// <summary>pyzk make_commkey — obfuscates the comm password with the session id.</summary>
        private static byte[] MakeCommKey(int key, int sessionId, int ticks = 50)
        {
            uint k = 0;
            for (var i = 0; i < 32; i++)
                k = ((key & (1 << i)) != 0) ? (k << 1 | 1) : (k << 1);

            k = unchecked(k + (uint)sessionId);

            var b = BitConverter.GetBytes(k); // little-endian
            b[0] ^= (byte)'Z';
            b[1] ^= (byte)'K';
            b[2] ^= (byte)'S';
            b[3] ^= (byte)'O';

            // swap the two 16-bit halves
            (b[0], b[1], b[2], b[3]) = (b[2], b[3], b[0], b[1]);

            var t = (byte)(ticks & 0xff);
            b[0] ^= t;
            b[1] ^= t;
            b[2] = t;
            b[3] = (byte)(t ^ b[3]);

            return b;
        }

        private static string NullTerminated(ReadOnlySpan<byte> span)
        {
            var end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            return Encoding.ASCII.GetString(span[..end]).Trim();
        }

        private static string Ascii(byte[] data)
        {
            var end = Array.IndexOf(data, (byte)0);
            if (end < 0) end = data.Length;
            return Encoding.ASCII.GetString(data, 0, end).Trim();
        }
    }
}
