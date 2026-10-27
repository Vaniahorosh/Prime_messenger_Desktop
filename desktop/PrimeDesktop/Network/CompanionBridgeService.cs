using System;

using System.Collections.Concurrent;

using System.Collections.Generic;

using System.IO;

using System.Net;

using System.Net.Sockets;

using System.Threading;

using System.Threading.Tasks;

using PrimeDesktop.Models;

using PrimeDesktop.Services;

namespace PrimeDesktop.Network;

/// <summary>

/// Companion Bridge for local Wi-Fi / LAN connection between Windows PC and Phone.

/// Enables seamless sync when phone and PC are on the same Wi-Fi network.

/// Completely hides IP addresses behind opaque PeerId.

/// Includes KeepAlive heartbeat and socket optimization to prevent premature disconnections.

/// </summary>

public class CompanionBridgeService : IPrimeNetworkService

{
    public event Action<string, DeviceConnectionState, string>? ConnectionStateChanged;

    public event Action<string, PacketType, byte[]>? PacketReceived;

    public event Action<string, string>? ErrorOccurred;

    private TcpListener? _listener;

    private TcpClient? _client;

    private NetworkStream? _stream;

    private CancellationTokenSource? _readCts;

    private CancellationTokenSource? _keepAliveCts;

    private readonly int _port;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private DateTime _lastActivityTime = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, string> _peerToIp = new();

    private readonly ConcurrentDictionary<string, string> _ipToPeer = new();

    public string MyDisplayName { get; set; } = Environment.MachineName;

    public string MyUsername { get; set; } = "prime_desktop";

    public CompanionBridgeService(int port = 8989)

    {
        _port = port;

    }

    private string GetOrCreatePeerId(string rawIp)

    {
        return _ipToPeer.GetOrAdd(rawIp, ip =>

        {
            string peerId = DeviceAnonymizer.GetOpaquePeerId("ip_" + ip);

            _peerToIp[peerId] = ip;

            return peerId;

        });

    }

    public Task StartServerAsync()

    {
        try

        {
            _listener = new TcpListener(IPAddress.Any, _port);

            _listener.Start();

            _ = Task.Run(async () =>

            {
                while (_listener != null)

                {
                    try

                    {
                        var tcpClient = await _listener.AcceptTcpClientAsync();

                        tcpClient.NoDelay = true;

                        tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                        string remoteIp = ((IPEndPoint)tcpClient.Client.RemoteEndPoint!).Address.ToString();

                        string peerId = GetOrCreatePeerId(remoteIp);

                        _keepAliveCts?.Cancel();

                        _readCts?.Cancel();

                        _stream?.Dispose();

                        _client?.Dispose();

                        _client = tcpClient;

                        _stream = tcpClient.GetStream();

                        ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connected, "Prime Телефон (Wi-Fi)");

                        // Start periodic KeepAlive heartbeat

                        StartKeepAlive(peerId);

                        // Send handshake & presence

                        byte[] handshake = PrimePacket.BuildHandshakePayload(MyDisplayName, MyUsername, isAck: false);

                        await SendPacketAsync(peerId, PacketType.Text, handshake);

                        await SendPacketAsync(peerId, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE"));

                        StartReading(peerId, _stream);

                    }

                    catch

                    {
                        break;

                    }

                }

            });

        }

        catch (Exception ex)

        {
            ErrorOccurred?.Invoke("BridgeServer", $"Failed to start Wi-Fi bridge listener: {ex.Message}");

        }

        return Task.CompletedTask;

    }

    public Task StopServerAsync()

    {
        try

        {
            _keepAliveCts?.Cancel();

            _readCts?.Cancel();

            _stream?.Dispose();

            _client?.Dispose();

            _listener?.Stop();

            _listener = null;

        }

        catch { }

        return Task.CompletedTask;

    }

    public void ForgetDevice(string peerIdOrIp)

    {
        string targetIp = peerIdOrIp;

        if (_peerToIp.TryRemove(peerIdOrIp, out var mapped))

        {
            targetIp = mapped;

        }

        _ipToPeer.TryRemove(targetIp, out _);

        _ = DisconnectAsync(peerIdOrIp);

    }

    public void RegisterPeerAddress(string peerId, string rawHardwareAddress)
    {
        if (!string.IsNullOrWhiteSpace(peerId) && !string.IsNullOrWhiteSpace(rawHardwareAddress))
        {
            _peerToIp[peerId] = rawHardwareAddress.Trim();
            _ipToPeer[rawHardwareAddress.Trim()] = peerId;
        }
    }

    public string ResolveHardwareAddress(string peerIdOrAddress)
    {
        if (string.IsNullOrWhiteSpace(peerIdOrAddress)) return string.Empty;
        return _peerToIp.TryGetValue(peerIdOrAddress, out var ip) ? ip : peerIdOrAddress;
    }

    public string ResolvePeerId(string hardwareAddressOrPeerId)
    {
        if (string.IsNullOrWhiteSpace(hardwareAddressOrPeerId)) return string.Empty;
        if (hardwareAddressOrPeerId.StartsWith("peer_", StringComparison.OrdinalIgnoreCase))
            return hardwareAddressOrPeerId;
        return _ipToPeer.TryGetValue(hardwareAddressOrPeerId, out var peer) ? peer : GetOrCreatePeerId(hardwareAddressOrPeerId);
    }

    public async Task<bool> ConnectAsync(string peerIdOrIp)

    {
        string targetIp = peerIdOrIp;

        if (_peerToIp.TryGetValue(peerIdOrIp, out var mapped))

        {
            targetIp = mapped;

        }

        string peerId = GetOrCreatePeerId(targetIp);

        try

        {
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connecting, "Prime Телефон");

            _keepAliveCts?.Cancel();

            _readCts?.Cancel();

            _stream?.Dispose();

            _client?.Dispose();

            _client = new TcpClient();

            _client.NoDelay = true;

            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            await _client.ConnectAsync(targetIp, _port);

            _stream = _client.GetStream();

            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connected, "Prime Телефон (Wi-Fi)");

            // Start KeepAlive

            StartKeepAlive(peerId);

            byte[] handshake = PrimePacket.BuildHandshakePayload(MyDisplayName, MyUsername, isAck: false);

            await SendPacketAsync(peerId, PacketType.Text, handshake);

            await SendPacketAsync(peerId, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE"));

            StartReading(peerId, _stream);

            return true;

        }

        catch (Exception ex)

        {
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Ошибка подключения Wi-Fi");

            ErrorOccurred?.Invoke(peerId, $"Wi-Fi bridge connection error: {ex.Message}");

            return false;

        }

    }

    public Task DisconnectAsync(string peerId)

    {
        try

        {
            _keepAliveCts?.Cancel();

            _readCts?.Cancel();

            _stream?.Dispose();

            _client?.Dispose();

            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Отключено");

        }

        catch { }

        return Task.CompletedTask;

    }

    private void StartKeepAlive(string peerId)

    {
        _keepAliveCts?.Cancel();

        _keepAliveCts = new CancellationTokenSource();

        var token = _keepAliveCts.Token;

        Task.Run(async () =>

        {
            byte[] empty = Array.Empty<byte>();

            while (!token.IsCancellationRequested)

            {
                try

                {
                    await Task.Delay(3500, token);

                    if (_stream != null && _client != null && _client.Connected)

                    {
                        await SendPacketAsync(peerId, PacketType.Ping, empty);

                    }

                }

                catch

                {
                    break;

                }

            }

        }, token);

    }

    public async Task SendPacketAsync(string targetPeerId, PacketType type, byte[] payload)

    {
        if (_stream == null || _client == null || !_client.Connected)

        {
            throw new InvalidOperationException("Wi-Fi companion bridge not connected.");

        }

        await Task.Run(() =>

        {
            lock (_stream)

            {
                PrimePacket.WriteToStream(_stream, type, payload);

            }

        });

    }

    public async Task<List<ChatItem>> DiscoverDevicesAsync()

    {
        var result = new List<ChatItem>();

        using var udp = new UdpClient();

        udp.EnableBroadcast = true;

        udp.Client.ReceiveTimeout = 2000;

        try

        {
            byte[] msg = System.Text.Encoding.UTF8.GetBytes("PRIME_PC_DISCOVER");

            var endpoint = new IPEndPoint(IPAddress.Broadcast, 8988);

            await udp.SendAsync(msg, msg.Length, endpoint);

            var receiveTask = udp.ReceiveAsync();

            if (await Task.WhenAny(receiveTask, Task.Delay(2000)) == receiveTask)

            {
                var response = receiveTask.Result;

                string text = System.Text.Encoding.UTF8.GetString(response.Buffer);

                if (text.StartsWith("PRIME_PHONE_HERE:"))

                {
                    string phoneIp = text.Substring(17).Trim();

                    string peerId = GetOrCreatePeerId(phoneIp);

                    result.Add(new ChatItem

                    {
                        Id = peerId,

                        PeerId = peerId,

                        DisplayName = "Prime Телефон (Wi-Fi)",

                        Username = "prime_companion",

                        IsBluetoothDevice = false,

                        ConnectionState = DeviceConnectionState.Disconnected

                    });

                }

            }

        }

        catch { }

        return result;

    }

    private void StartReading(string peerId, NetworkStream stream)

    {
        _readCts?.Cancel();

        _readCts = new CancellationTokenSource();

        var token = _readCts.Token;

        Task.Run(() =>

        {
            try

            {
                while (!token.IsCancellationRequested)

                {
                    var packet = PrimePacket.ReadFromStream(stream);

                    if (packet == null) break;

                    // Automatically reply to PING with ACK

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

            catch { }

            finally

            {
                _keepAliveCts?.Cancel();

                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Связь потеряна");

            }

        }, token);

    }
}

