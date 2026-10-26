using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using PrimeDesktop.Models;
using PrimeDesktop.Services;

namespace PrimeDesktop.Network;

/// <summary>
/// Native Windows Bluetooth Classic RFCOMM service.
/// Provides 100% anonymized peer communication matching Prime Messenger on Android.
/// Features async non-blocking I/O, write serialization (SemaphoreSlim), smart KeepAlive,
/// role arbitration, and auto-retry reconnection matching Android's BluetoothConnectionManager.
/// </summary>
public class BluetoothRfcommService : IPrimeNetworkService
{
    public static readonly Guid PrimeServiceUuid = Guid.Parse("fa87c0d0-afac-11de-8a39-0800200c9a66");

    public event Action<string, DeviceConnectionState, string>? ConnectionStateChanged;
    public event Action<string, PacketType, byte[]>? PacketReceived;
    public event Action<string, string>? ErrorOccurred;

    private StreamSocketListener? _serverListener;
    private RfcommServiceProvider? _rfcommProvider;

    // Strict internal mapping between Opaque Peer ID and physical hardware address
    private readonly ConcurrentDictionary<string, string> _peerToHardwareAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _hardwareAddressToPeer = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, StreamSocket> _activeSockets = new();
    private readonly ConcurrentDictionary<string, Stream> _activeStreams = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastActivityTime = new();

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _readCancellation = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _keepAliveCancellation = new();

    // Auto-retry state matching Android BluetoothConnectionManager
    private readonly ConcurrentDictionary<string, int> _retryCounts = new();
    private readonly ConcurrentDictionary<string, bool> _manuallyDisconnected = new();

    public string MyDisplayName { get; set; } = Environment.MachineName;
    public string MyUsername { get; set; } = "prime_desktop";

    public static string NormalizeMacAddress(string? addressOrId)
    {
        if (string.IsNullOrWhiteSpace(addressOrId)) return string.Empty;

        string cleaned = addressOrId.Trim().Trim('(', ')', '[', ']');

        int lastHyphen = cleaned.LastIndexOf('-');
        if (lastHyphen >= 0 && lastHyphen < cleaned.Length - 1 && cleaned.Contains('#'))
        {
            cleaned = cleaned.Substring(lastHyphen + 1);
        }

        string hexOnly = cleaned.Replace(":", "").Replace("-", "");
        if (hexOnly.Length == 12 && ulong.TryParse(hexOnly, System.Globalization.NumberStyles.HexNumber, null, out ulong mac))
        {
            return $"{((mac >> 40) & 0xFF):X2}:{((mac >> 32) & 0xFF):X2}:{((mac >> 24) & 0xFF):X2}:{((mac >> 16) & 0xFF):X2}:{((mac >> 8) & 0xFF):X2}:{(mac & 0xFF):X2}";
        }

        return cleaned.ToUpperInvariant();
    }

    public string ResolveHardwareAddress(string peerIdOrAddress)
    {
        if (_peerToHardwareAddress.TryGetValue(peerIdOrAddress, out var mapped))
        {
            return mapped;
        }
        return NormalizeMacAddress(peerIdOrAddress);
    }

    public void RegisterPeerAddress(string peerId, string rawHardwareAddress)
    {
        string norm = NormalizeMacAddress(rawHardwareAddress);
        if (!string.IsNullOrEmpty(norm) && !string.IsNullOrEmpty(peerId))
        {
            _peerToHardwareAddress[peerId] = norm;
            _hardwareAddressToPeer[norm] = peerId;
        }
    }

    private string GetOrCreatePeerId(string rawHardwareAddress)
    {
        string norm = NormalizeMacAddress(rawHardwareAddress);
        string peerId = _hardwareAddressToPeer.GetOrAdd(norm, addr => DeviceAnonymizer.GetOpaquePeerId(addr));
        _peerToHardwareAddress[peerId] = norm;
        return peerId;
    }

    private void RecordActivity(string rawAddress)
    {
        _lastActivityTime[NormalizeMacAddress(rawAddress)] = DateTime.UtcNow;
    }

    /// <summary>
    /// Role arbitration matching Android BluetoothConnectionManager.isClientInitiator
    /// Prevents simultaneous RFCOMM connection collisions.
    /// </summary>
    public static bool IsClientInitiator(string localName, string remoteName)
    {
        if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(remoteName)) return true;
        string loc = localName.Trim().ToUpperInvariant();
        string rem = remoteName.Trim().ToUpperInvariant();
        int comp = string.Compare(loc, rem, StringComparison.Ordinal);
        if (comp != 0) return comp > 0;
        return loc.GetHashCode() >= rem.GetHashCode();
    }

    public async Task StartServerAsync()
    {
        try
        {
            _rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(PrimeServiceUuid));
            _serverListener = new StreamSocketListener();
            _serverListener.ConnectionReceived += OnConnectionReceived;

            await _serverListener.BindServiceNameAsync(
                _rfcommProvider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            // Populate SDP Service Record (0x25 = SDP Text String with 1-byte length)
            using (var writer = new DataWriter())
            {
                writer.WriteByte(0x25); // SDP String type: Type 4, 1-byte length follows
                writer.WriteByte((byte)"PrimeChat".Length);
                writer.WriteString("PrimeChat");
                _rfcommProvider.SdpRawAttributes.Add(0x0100, writer.DetachBuffer());
            }

            _rfcommProvider.StartAdvertising(_serverListener, true);
            App.Log("[Bluetooth] RFCOMM Server started advertising.");
        }
        catch (Exception ex)
        {
            App.Log($"[Bluetooth ERROR] Failed to start server: {ex.Message}");
            ErrorOccurred?.Invoke("LocalServer", $"Failed to start Bluetooth RFCOMM server: {ex.Message}");
        }
    }

    public Task StopServerAsync()
    {
        try
        {
            _rfcommProvider?.StopAdvertising();
            _rfcommProvider = null;

            _serverListener?.Dispose();
            _serverListener = null;

            foreach (var kvp in _activeSockets)
            {
                DisconnectInternal(kvp.Key);
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var socket = args.Socket;
        string rawAddress = NormalizeMacAddress(socket.Information.RemoteAddress.CanonicalName);
        string peerId = GetOrCreatePeerId(rawAddress);
        string friendlyName = DeviceAnonymizer.SanitizeDisplayName(socket.Information.RemoteHostName?.DisplayName, rawAddress);

        App.Log($"[Bluetooth] ConnectionReceived from '{rawAddress}' ({socket.Information.RemoteAddress.CanonicalName})");

        _peerToHardwareAddress[peerId] = rawAddress;
        _hardwareAddressToPeer[rawAddress] = peerId;

        _manuallyDisconnected.TryRemove(rawAddress, out _);
        _manuallyDisconnected.TryRemove(peerId, out _);
        _retryCounts.TryRemove(rawAddress, out _);

        // Clean up any stale active socket for this device
        if (_activeSockets.TryGetValue(rawAddress, out var existingSocket))
        {
            App.Log($"[Bluetooth] Disconnecting previous stale socket for '{rawAddress}'");
            DisconnectInternal(rawAddress);
        }

        _activeSockets[rawAddress] = socket;
        var inStream = socket.InputStream.AsStreamForRead();
        var outStream = socket.OutputStream.AsStreamForWrite();
        _activeStreams[rawAddress] = new BidirectionalStream(inStream, outStream);
        RecordActivity(rawAddress);

        ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connected, friendlyName);

        // Start bidirectional KeepAlive
        StartKeepAlive(peerId, rawAddress);

        // Send Initial Handshake & Presence
        _ = Task.Run(async () =>
        {
            try
            {
                byte[] handshake = PrimePacket.BuildHandshakePayload(MyDisplayName, MyUsername, isAck: false);
                await SendPacketAsync(peerId, PacketType.Text, handshake);
                await SendPacketAsync(peerId, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE"));
                App.Log($"[Bluetooth] Sent initial handshake to '{peerId}' ({rawAddress})");
            }
            catch (Exception ex)
            {
                App.Log($"[Bluetooth ERROR] Failed to send initial handshake to '{peerId}': {ex.Message}");
            }
        });

        StartReading(peerId, rawAddress, inStream);
    }

    public Task<bool> ConnectAsync(string peerIdOrAddress) => ConnectAsync(peerIdOrAddress, forceClient: false);

    public async Task<bool> ConnectAsync(string peerIdOrAddress, bool forceClient)
    {
        string rawAddress = ResolveHardwareAddress(peerIdOrAddress);
        string peerId = GetOrCreatePeerId(rawAddress);
        App.Log($"[Bluetooth] ConnectAsync called: '{peerIdOrAddress}' -> raw='{rawAddress}', peerId='{peerId}', forceClient={forceClient}");
        _manuallyDisconnected.TryRemove(rawAddress, out _);
        _manuallyDisconnected.TryRemove(peerId, out _);

        // If already connected, do not re-connect
        if (_activeSockets.ContainsKey(rawAddress) && _activeStreams.ContainsKey(rawAddress))
        {
            App.Log($"[Bluetooth] ConnectAsync: Already connected to '{rawAddress}'");
            return true;
        }

        try
        {
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connecting, "Установка связи...");

            BluetoothDevice? device = null;
            if (ulong.TryParse(rawAddress.Replace(":", "").Replace("-", ""), System.Globalization.NumberStyles.HexNumber, null, out ulong mac))
            {
                device = await BluetoothDevice.FromBluetoothAddressAsync(mac);
            }
            else
            {
                device = await BluetoothDevice.FromIdAsync(peerIdOrAddress);
            }

            if (device == null)
            {
                App.Log($"[Bluetooth ERROR] Device not found for '{rawAddress}'");
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Устройство не найдено");
                ErrorOccurred?.Invoke(peerId, "Bluetooth device not found.");
                return false;
            }

            // Always standardize rawAddress from the resolved BluetoothDevice
            string normMac = NormalizeMacAddress(device.BluetoothAddress.ToString("X12"));
            if (!string.IsNullOrEmpty(normMac))
            {
                rawAddress = normMac;
                _peerToHardwareAddress[peerId] = rawAddress;
                _hardwareAddressToPeer[rawAddress] = peerId;
            }

            // Role arbitration matching Android BluetoothConnectionManager
            if (!forceClient && !IsClientInitiator(MyDisplayName, device.Name))
            {
                App.Log($"[Bluetooth] Role arbitration: Local='{MyDisplayName}' vs Remote='{device.Name}' -> Acting as server, waiting 3.5s...");
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connecting, "Ожидание входящего подключения...");
                await Task.Delay(3500);

                if (_activeSockets.ContainsKey(rawAddress) && _activeStreams.ContainsKey(rawAddress))
                {
                    App.Log($"[Bluetooth] Connected via incoming socket while waiting.");
                    return true;
                }

                if (_manuallyDisconnected.ContainsKey(rawAddress) || _manuallyDisconnected.ContainsKey(peerId))
                {
                    return false;
                }
                App.Log($"[Bluetooth] 3.5s timeout expired, proceeding as client initiator...");
            }

            // CRITICAL FIX: Query RFCOMM services with Uncached mode first to avoid stale cached RFCOMM channels
            App.Log($"[Bluetooth] Querying RFCOMM services for '{rawAddress}' (Uncached)...");
            var rfcommServices = await device.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromUuid(PrimeServiceUuid),
                BluetoothCacheMode.Uncached);

            if (rfcommServices.Services.Count == 0)
            {
                App.Log($"[Bluetooth] Uncached query returned 0 services, trying Cached...");
                rfcommServices = await device.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(PrimeServiceUuid),
                    BluetoothCacheMode.Cached);
            }

            if (rfcommServices.Services.Count == 0)
            {
                App.Log($"[Bluetooth ERROR] Prime Messenger service not found on '{rawAddress}'");
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, device.Name);
                ErrorOccurred?.Invoke(peerId, "Prime Messenger service not available on device.");
                return false;
            }

            // Clean up any stale active socket before creating a new one
            if (_activeSockets.TryGetValue(rawAddress, out var existingSocket))
            {
                DisconnectInternal(rawAddress);
            }

            var service = rfcommServices.Services[0];
            App.Log($"[Bluetooth] Connecting StreamSocket to '{service.ConnectionHostName?.DisplayName}' : '{service.ConnectionServiceName}'...");
            var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            App.Log($"[Bluetooth] StreamSocket connected successfully!");

            string addressKey = rawAddress;
            _peerToHardwareAddress[peerId] = addressKey;
            _hardwareAddressToPeer[addressKey] = peerId;

            _activeSockets[addressKey] = socket;
            var inStream = socket.InputStream.AsStreamForRead();
            var outStream = socket.OutputStream.AsStreamForWrite();
            _activeStreams[addressKey] = new BidirectionalStream(inStream, outStream);
            RecordActivity(addressKey);
            _retryCounts.TryRemove(addressKey, out _);

            string friendlyName = DeviceAnonymizer.SanitizeDisplayName(device.Name, rawAddress);
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connected, friendlyName);

            // Start KeepAlive heartbeat
            StartKeepAlive(peerId, addressKey);

            // Send handshake & presence
            byte[] handshake = PrimePacket.BuildHandshakePayload(MyDisplayName, MyUsername, isAck: false);
            await SendPacketAsync(peerId, PacketType.Text, handshake);
            await SendPacketAsync(peerId, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE"));

            StartReading(peerId, addressKey, inStream);
            return true;
        }
        catch (Exception ex)
        {
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Ошибка подключения");
            ErrorOccurred?.Invoke(peerId, $"Bluetooth connection error: {ex.Message}");
            return false;
        }
    }

    public Task DisconnectAsync(string peerId)
    {
        string rawAddress = ResolveHardwareAddress(peerId);

        _manuallyDisconnected[rawAddress] = true;
        _manuallyDisconnected[peerId] = true;
        _retryCounts.TryRemove(rawAddress, out _);
        _retryCounts.TryRemove(peerId, out _);

        DisconnectInternal(rawAddress);
        ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Отключено");
        return Task.CompletedTask;
    }

    public void ForgetDevice(string peerIdOrAddress)
    {
        string rawAddress = ResolveHardwareAddress(peerIdOrAddress);

        _peerToHardwareAddress.TryRemove(peerIdOrAddress, out _);
        _hardwareAddressToPeer.TryRemove(rawAddress, out _);
        _manuallyDisconnected[rawAddress] = true;
        _manuallyDisconnected[peerIdOrAddress] = true;
        _retryCounts.TryRemove(rawAddress, out _);
        _retryCounts.TryRemove(peerIdOrAddress, out _);

        DisconnectInternal(rawAddress);
    }

    private void DisconnectInternal(string rawAddress)
    {
        StopKeepAlive(rawAddress);

        if (_readCancellation.TryRemove(rawAddress, out var cts))
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }

        if (_activeStreams.TryRemove(rawAddress, out var stream))
        {
            try { stream.Dispose(); } catch { }
        }

        if (_activeSockets.TryRemove(rawAddress, out var socket))
        {
            try { socket.Dispose(); } catch { }
        }

        if (_writeLocks.TryRemove(rawAddress, out var sem))
        {
            try { sem.Dispose(); } catch { }
        }

        _lastActivityTime.TryRemove(rawAddress, out _);
    }

    private void StartKeepAlive(string peerId, string rawAddress)
    {
        StopKeepAlive(rawAddress);
        var cts = new CancellationTokenSource();
        _keepAliveCancellation[rawAddress] = cts;

        Task.Run(async () =>
        {
            byte[] empty = Array.Empty<byte>();
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(3500, cts.Token);
                    if (!_activeStreams.ContainsKey(rawAddress)) break;

                    // Smart KeepAlive: ping only if idle for > 6 seconds to prevent flooding
                    if (_lastActivityTime.TryGetValue(rawAddress, out var lastActive))
                    {
                        if ((DateTime.UtcNow - lastActive).TotalSeconds < 6.0)
                        {
                            continue;
                        }
                    }

                    await SendPacketAsync(peerId, PacketType.Ping, empty);
                }
                catch
                {
                    break;
                }
            }
        }, cts.Token);
    }

    private void StopKeepAlive(string rawAddress)
    {
        if (_keepAliveCancellation.TryRemove(rawAddress, out var cts))
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }
    }

    public async Task SendPacketAsync(string targetPeerId, PacketType type, byte[] payload)
    {
        string rawAddress = ResolveHardwareAddress(targetPeerId);

        if (!_activeStreams.TryGetValue(rawAddress, out var stream))
        {
            throw new InvalidOperationException($"Not connected to peer: {targetPeerId}");
        }

        var sem = _writeLocks.GetOrAdd(rawAddress, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            await PrimePacket.WriteToStreamAsync(stream, type, payload).ConfigureAwait(false);
            RecordActivity(rawAddress);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<List<ChatItem>> DiscoverDevicesAsync()
    {
        var result = new List<ChatItem>();
        try
        {
            string selector = BluetoothDevice.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);

            foreach (var dev in devices)
            {
                if (DeviceAnonymizer.IsDisallowedPeripherals(dev.Name))
                {
                    continue;
                }

                BluetoothDevice? btDev = null;
                try
                {
                    btDev = await BluetoothDevice.FromIdAsync(dev.Id);
                    if (btDev != null)
                    {
                        var cod = btDev.ClassOfDevice;
                        if (cod != null)
                        {
                            var major = cod.MajorClass;
                            if (major == BluetoothMajorClass.AudioVideo ||
                                major == BluetoothMajorClass.Wearable ||
                                major == BluetoothMajorClass.Peripheral ||
                                major == BluetoothMajorClass.Imaging ||
                                major == BluetoothMajorClass.Toy ||
                                major == BluetoothMajorClass.Health)
                            {
                                continue;
                            }
                        }
                    }
                }
                catch { }

                string normMac = btDev != null
                    ? NormalizeMacAddress(btDev.BluetoothAddress.ToString("X12"))
                    : NormalizeMacAddress(dev.Id);

                string peerId = GetOrCreatePeerId(normMac);
                string cleanName = DeviceAnonymizer.SanitizeDisplayName(dev.Name, normMac);
                string cleanUser = DeviceAnonymizer.SanitizeUsername(null, normMac);

                result.Add(new ChatItem
                {
                    Id = peerId,
                    PeerId = peerId,
                    DeviceAddress = normMac,
                    DisplayName = cleanName,
                    Username = cleanUser,
                    IsBluetoothDevice = true,
                    ConnectionState = DeviceConnectionState.Disconnected
                });
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("Discovery", $"Device scan failed: {ex.Message}");
        }
        return result;
    }

    private void StartReading(string peerId, string rawAddress, Stream inStream)
    {
        var cts = new CancellationTokenSource();
        _readCancellation[rawAddress] = cts;

        Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var packet = await PrimePacket.ReadFromStreamAsync(inStream, cts.Token);
                    if (packet == null) break;

                    RecordActivity(rawAddress);
                    App.Log($"[Bluetooth] Received packet {packet.Type} ({packet.Payload?.Length ?? 0} bytes) from '{peerId}' ({rawAddress})");

                    // Automatically reply to PING with ACK to maintain link
                    if (packet.Type == PacketType.Ping)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await SendPacketAsync(peerId, PacketType.Ack, Array.Empty<byte>()); } catch { }
                        });
                    }

                    try
                    {
                        PacketReceived?.Invoke(peerId, packet.Type, packet.Payload ?? Array.Empty<byte>());
                    }
                    catch (Exception handlerEx)
                    {
                        App.Log($"[Bluetooth ERROR] PacketReceived handler threw exception for '{peerId}': {handlerEx.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.Log($"[Bluetooth ERROR] Read exception for '{rawAddress}': {ex.Message}");
                ErrorOccurred?.Invoke(peerId, $"Read error: {ex.Message}");
            }
            finally
            {
                App.Log($"[Bluetooth] Read loop finished for '{rawAddress}', closing connection");
                DisconnectInternal(rawAddress);
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "не в сети");

                // Auto-retry connection if connection dropped unexpectedly
                _ = TriggerAutoRetryAsync(peerId, rawAddress);
            }
        }, cts.Token);
    }

    private async Task TriggerAutoRetryAsync(string peerId, string rawAddress)
    {
        if (_manuallyDisconnected.ContainsKey(rawAddress) || _manuallyDisconnected.ContainsKey(peerId))
            return;

        int count = _retryCounts.AddOrUpdate(rawAddress, 1, (_, c) => c + 1);
        if (count > 4)
        {
            _retryCounts.TryRemove(rawAddress, out _);
            return;
        }

        int delayMs = count switch
        {
            1 => 1000,
            2 => 2000,
            3 => 4000,
            _ => 8000
        };

        await Task.Delay(delayMs);
        if (!_activeSockets.ContainsKey(rawAddress) && !_manuallyDisconnected.ContainsKey(rawAddress))
        {
            _ = ConnectAsync(peerId);
        }
    }

    private class BidirectionalStream : Stream
    {
        private readonly Stream _inStream;
        private readonly Stream _outStream;

        public BidirectionalStream(Stream inStream, Stream outStream)
        {
            _inStream = inStream;
            _outStream = outStream;
        }

        public override bool CanRead => _inStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _outStream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _outStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _outStream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _inStream.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inStream.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => _outStream.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _outStream.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _outStream.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _inStream.Dispose(); } catch { }
                try { _outStream.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
