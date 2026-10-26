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
    private readonly ConcurrentDictionary<string, string> _peerToHardwareAddress = new();
    private readonly ConcurrentDictionary<string, string> _hardwareAddressToPeer = new();

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

    private string GetOrCreatePeerId(string rawHardwareAddress)
    {
        return _hardwareAddressToPeer.GetOrAdd(rawHardwareAddress, addr =>
        {
            string peerId = DeviceAnonymizer.GetOpaquePeerId(addr);
            _peerToHardwareAddress[peerId] = addr;
            return peerId;
        });
    }

    private void RecordActivity(string rawAddress)
    {
        _lastActivityTime[rawAddress] = DateTime.UtcNow;
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

            // Populate SDP Service Record
            using (var writer = new DataWriter())
            {
                writer.WriteByte(0x0C); // SDP String type
                writer.WriteByte((byte)"PrimeChat".Length);
                writer.WriteString("PrimeChat");
                _rfcommProvider.SdpRawAttributes.Add(0x0100, writer.DetachBuffer());
            }

            _rfcommProvider.StartAdvertising(_serverListener, true);
        }
        catch (Exception ex)
        {
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
        string rawAddress = socket.Information.RemoteAddress.CanonicalName;
        string peerId = GetOrCreatePeerId(rawAddress);
        string friendlyName = DeviceAnonymizer.SanitizeDisplayName(socket.Information.RemoteHostName?.DisplayName, rawAddress);

        _manuallyDisconnected.TryRemove(rawAddress, out _);
        _manuallyDisconnected.TryRemove(peerId, out _);
        _retryCounts.TryRemove(rawAddress, out _);

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
            }
            catch { }
        });

        StartReading(peerId, rawAddress, inStream);
    }

    public async Task<bool> ConnectAsync(string peerIdOrAddress)
    {
        string rawAddress = peerIdOrAddress;
        if (_peerToHardwareAddress.TryGetValue(peerIdOrAddress, out var mapped))
        {
            rawAddress = mapped;
        }

        string peerId = GetOrCreatePeerId(rawAddress);
        _manuallyDisconnected.TryRemove(rawAddress, out _);
        _manuallyDisconnected.TryRemove(peerId, out _);

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
                device = await BluetoothDevice.FromIdAsync(rawAddress);
            }

            if (device == null)
            {
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Устройство не найдено");
                ErrorOccurred?.Invoke(peerId, "Bluetooth device not found.");
                return false;
            }

            var rfcommServices = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(PrimeServiceUuid));
            if (rfcommServices.Services.Count == 0)
            {
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, device.Name);
                ErrorOccurred?.Invoke(peerId, "Prime Messenger service not available on device.");
                return false;
            }

            var service = rfcommServices.Services[0];
            var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            string addressKey = device.DeviceId;
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
        string rawAddress = peerId;
        if (_peerToHardwareAddress.TryGetValue(peerId, out var mapped))
        {
            rawAddress = mapped;
        }

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
        string rawAddress = peerIdOrAddress;
        if (_peerToHardwareAddress.TryRemove(peerIdOrAddress, out var mapped))
        {
            rawAddress = mapped;
        }

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
        string rawAddress = targetPeerId;
        if (_peerToHardwareAddress.TryGetValue(targetPeerId, out var mapped))
        {
            rawAddress = mapped;
        }

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

                try
                {
                    var btDev = await BluetoothDevice.FromIdAsync(dev.Id);
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

                string peerId = GetOrCreatePeerId(dev.Id);
                string cleanName = DeviceAnonymizer.SanitizeDisplayName(dev.Name, dev.Id);
                string cleanUser = DeviceAnonymizer.SanitizeUsername(null, dev.Id);

                result.Add(new ChatItem
                {
                    Id = peerId,
                    PeerId = peerId,
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

                    // Automatically reply to PING with ACK to maintain link
                    if (packet.Type == PacketType.Ping)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await SendPacketAsync(peerId, PacketType.Ack, Array.Empty<byte>()); } catch { }
                        });
                    }

                    PacketReceived?.Invoke(peerId, packet.Type, packet.Payload);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(peerId, $"Read error: {ex.Message}");
            }
            finally
            {
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
