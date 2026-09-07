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

            var (code, payload) = await SendAsync(CMD_CONNECT, Array.Empty<byte>());

            if (code == CMD_ACK_UNAUTH)
            {
                _logger.LogInformation("Device {Ip} requires a comm key; authenticating", ip);
                var key = MakeCommKey(_commPassword, _sessionId);
                (code, payload) = await SendAsync(CMD_AUTH, key);
            }

            if (code != CMD_ACK_OK)
            {
                _logger.LogWarning("Device {Ip} refused the connection (code {Code})", ip, code);
                await DisconnectAsync();
                return false;
            }

            _ = payload;
            return true;
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

        /// <summary>Not implemented over raw TCP yet — the current sync flow maps
        /// biometric ids through the EmployeeDevices table, not this call.</summary>
        public Task<List<DeviceUser>> GetUsersAsync() => Task.FromResult(new List<DeviceUser>());

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
        private async Task<byte[]> ReadWithBufferAsync(int command)
        {
            // pack('<b h i i', 1, command, 0, 0)
            var req = new byte[11];
            req[0] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(req.AsSpan(1), (short)command);
            BinaryPrimitives.WriteInt32LittleEndian(req.AsSpan(3), 0);
            BinaryPrimitives.WriteInt32LittleEndian(req.AsSpan(7), 0);

            var (code, payload) = await SendAsync(CMD_DATA_WRRQ, req);

            if (code == CMD_DATA)
                return payload; // small result, delivered inline

            if (code != CMD_ACK_OK && code != CMD_PREPARE_DATA)
                throw new IOException($"Device did not accept the buffered read (code {code})");

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
