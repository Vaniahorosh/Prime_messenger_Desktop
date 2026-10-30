using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrimeDesktop.Models;
using PrimeDesktop.Services;

namespace PrimeDesktop.Network;

/// <summary>
/// Companion Bridge for local Wi-Fi / LAN connection between Windows PC and Phones/Peers.
/// Enables multi-peer sync and chat over local Wi-Fi without Bluetooth constraints.
/// Supports multiple concurrent device connections simultaneously.
/// </summary>
public class CompanionBridgeService : IPrimeNetworkService
{
    public event Action<string, DeviceConnectionState, string>? ConnectionStateChanged;
    public event Action<string, PacketType, byte[]>? PacketReceived;
    public event Action<string, string>? ErrorOccurred;

    private TcpListener? _listener;
    private UdpClient? _udpResponder;
    private CancellationTokenSource? _udpResponderCts;

    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<string, NetworkStream> _streams = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _readCts = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _keepAliveCts = new();

    private readonly int _port;
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

            // 1. TCP Server for accepting phone connections
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

                        DisconnectPeer(peerId);

                        var stream = tcpClient.GetStream();
                        _clients[peerId] = tcpClient;
                        _streams[peerId] = stream;

                        ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connected, "Prime Телефон (Wi-Fi)");

                        StartKeepAlive(peerId);

                        byte[] handshake = PrimePacket.BuildHandshakePayload(MyDisplayName, MyUsername, isAck: false);
                        _ = SendPacketAsync(peerId, PacketType.Text, handshake);
                        _ = SendPacketAsync(peerId, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE"));

                        StartReading(peerId, stream);
                    }
                    catch
                    {
                        break;
                    }
                }
            });

            // 2. UDP Responder for phone discovery requests
            StartUdpResponder();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("BridgeServer", $"Failed to start Wi-Fi bridge listener: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private void StartUdpResponder()
    {
        try
        {
            _udpResponderCts?.Cancel();
            _udpResponderCts = new CancellationTokenSource();
            var token = _udpResponderCts.Token;

            _udpResponder = new UdpClient();
            _udpResponder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpResponder.ExclusiveAddressUse = false;
            _udpResponder.Client.Bind(new IPEndPoint(IPAddress.Any, 8988));

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _udpResponder != null)
                {
                    try
                    {
                        var result = await _udpResponder.ReceiveAsync(token);
                        string msg = System.Text.Encoding.UTF8.GetString(result.Buffer);
                        if (msg.StartsWith("PRIME_DISCOVER_ALL") || msg.StartsWith("PRIME_PHONE_DISCOVER"))
                        {
                            string myIp = GetLocalIpAddress() ?? "127.0.0.1";
                            string replyStr = $"PRIME_PC_HERE:{myIp};name={MyDisplayName};login={MyUsername}";
                            byte[] replyBytes = System.Text.Encoding.UTF8.GetBytes(replyStr);
                            await _udpResponder.SendAsync(replyBytes, replyBytes.Length, result.RemoteEndPoint);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
        }
        catch { }
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return null;
    }

    public Task StopServerAsync()
    {
        try
        {
            _udpResponderCts?.Cancel();
            _udpResponder?.Dispose();
            _udpResponder = null;

            foreach (var peer in _clients.Keys.ToList())
            {
                DisconnectPeer(peer);
            }

            _listener?.Stop();
            _listener = null;
        }
        catch { }

        return Task.CompletedTask;
    }

    public bool IsConnected(string peerIdOrIp)
    {
        if (string.IsNullOrWhiteSpace(peerIdOrIp))
            return _clients.Values.Any(c => c.Connected);

        string peerId = ResolvePeerId(peerIdOrIp);
        if (_clients.TryGetValue(peerId, out var client) && client.Connected)
            return true;

        string ip = ResolveHardwareAddress(peerIdOrIp);
        if (_clients.TryGetValue(ip, out var clientIp) && clientIp.Connected)
            return true;

        if (_clients.Count == 1 && _clients.Values.First().Connected)
            return true;

        return false;
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
        string res = _peerToIp.TryGetValue(peerIdOrAddress, out var ip) ? ip : peerIdOrAddress;
        if (res.StartsWith("ip_", StringComparison.OrdinalIgnoreCase))
            res = res.Substring(3);
        return res;
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
        string targetIp = ResolveHardwareAddress(peerIdOrIp);
        if (string.IsNullOrWhiteSpace(targetIp)) return false;
        string peerId = GetOrCreatePeerId(targetIp);

        try
        {
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connecting, "Prime Телефон");

            DisconnectPeer(peerId);

            var tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            await tcpClient.ConnectAsync(targetIp, _port);
            var stream = tcpClient.GetStream();

            _clients[peerId] = tcpClient;
            _streams[peerId] = stream;

            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Connected, "Prime Телефон (Wi-Fi)");

            StartKeepAlive(peerId);

            byte[] handshake = PrimePacket.BuildHandshakePayload(MyDisplayName, MyUsername, isAck: false);
            await SendPacketAsync(peerId, PacketType.Text, handshake);
            await SendPacketAsync(peerId, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE"));

            StartReading(peerId, stream);
            return true;
        }
        catch (Exception ex)
        {
            ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Ошибка подключения Wi-Fi");
            ErrorOccurred?.Invoke(peerId, $"Wi-Fi bridge connection error: {ex.Message}");
            return false;
        }
    }

    public Task DisconnectAsync(string peerIdOrIp)
    {
        string peerId = ResolvePeerId(peerIdOrIp);
        DisconnectPeer(peerId);
        ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Отключено");
        return Task.CompletedTask;
    }

    private void DisconnectPeer(string peerId)
    {
        if (_keepAliveCts.TryRemove(peerId, out var kCts))
        {
            try { kCts.Cancel(); kCts.Dispose(); } catch { }
        }
        if (_readCts.TryRemove(peerId, out var rCts))
        {
            try { rCts.Cancel(); rCts.Dispose(); } catch { }
        }
        if (_streams.TryRemove(peerId, out var stream))
        {
            try { stream.Dispose(); } catch { }
        }
        if (_clients.TryRemove(peerId, out var client))
        {
            try { client.Dispose(); } catch { }
        }
    }

    private void StartKeepAlive(string peerId)
    {
        if (_keepAliveCts.TryGetValue(peerId, out var oldCts))
        {
            try { oldCts.Cancel(); oldCts.Dispose(); } catch { }
        }

        var cts = new CancellationTokenSource();
        _keepAliveCts[peerId] = cts;
        var token = cts.Token;

        Task.Run(async () =>
        {
            byte[] empty = Array.Empty<byte>();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(3500, token);
                    if (_streams.TryGetValue(peerId, out var stream) && _clients.TryGetValue(peerId, out var client) && client.Connected)
                    {
                        await SendPacketAsync(peerId, PacketType.Ping, empty);
                    }
                    else
                    {
                        break;
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
        string peerId = ResolvePeerId(targetPeerId);
        string ip = ResolveHardwareAddress(targetPeerId);

        NetworkStream? stream = null;
        if (!_streams.TryGetValue(peerId, out stream))
        {
            _streams.TryGetValue(ip, out stream);
        }

        if (stream == null && _streams.Count == 1)
        {
            stream = _streams.Values.FirstOrDefault();
        }

        if (stream == null && !string.IsNullOrWhiteSpace(ip) && (ip.Contains('.') || !ip.StartsWith("peer_")))
        {
            bool connected = await ConnectAsync(ip);
            if (connected)
            {
                if (!_streams.TryGetValue(peerId, out stream))
                {
                    _streams.TryGetValue(ip, out stream);
                }
            }
        }

        if (stream == null)
        {
            throw new InvalidOperationException($"Wi-Fi companion bridge not connected to peer '{targetPeerId}'.");
        }

        var sem = _writeLocks.GetOrAdd(peerId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            PrimePacket.WriteToStream(stream, type, payload);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<List<ChatItem>> DiscoverDevicesAsync()
    {
        var result = new List<ChatItem>();
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.ReceiveTimeout = 2000;

        try
        {
            byte[] msg = System.Text.Encoding.UTF8.GetBytes("PRIME_PC_DISCOVER");
            var endpoint = new IPEndPoint(IPAddress.Broadcast, 8988);
            await udp.SendAsync(msg, msg.Length, endpoint);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var receiveTask = udp.ReceiveAsync();
                    var completed = await Task.WhenAny(receiveTask, Task.Delay(2000, cts.Token));
                    if (completed != receiveTask) break;

                    var response = receiveTask.Result;
                    string text = System.Text.Encoding.UTF8.GetString(response.Buffer);
                    if (text.StartsWith("PRIME_PHONE_HERE:") || text.StartsWith("PRIME_PEER_HERE:"))
                    {
                        string data = text.Substring(text.IndexOf(':') + 1).Trim();
                        string phoneIp = response.RemoteEndPoint.Address.ToString();
                        string devName = "Prime Телефон";
                        string devLogin = "prime_companion";

                        var parts = data.Split(';');
                        foreach (var p in parts)
                        {
                            if (p.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) devName = p.Substring(5).Trim();
                            else if (p.StartsWith("login=", StringComparison.OrdinalIgnoreCase)) devLogin = p.Substring(6).Trim();
                            else if (p.Contains('.') && !p.Contains('=')) phoneIp = p.Trim();
                        }

                        if (seenIps.Add(phoneIp))
                        {
                            string peerId = GetOrCreatePeerId(phoneIp);
                            string cleanName = DeviceAnonymizer.SanitizeDisplayName(devName, phoneIp);
                            string cleanUser = DeviceAnonymizer.SanitizeUsername(devLogin, phoneIp);

                            result.Add(new ChatItem
                            {
                                Id = peerId,
                                PeerId = peerId,
                                DisplayName = cleanName,
                                Username = cleanUser,
                                DeviceAddress = phoneIp,
                                IsBluetoothDevice = false,
                                ConnectionState = DeviceConnectionState.Disconnected
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
        catch { }

        return result;
    }

    private void StartReading(string peerId, NetworkStream stream)
    {
        if (_readCts.TryGetValue(peerId, out var oldCts))
        {
            try { oldCts.Cancel(); oldCts.Dispose(); } catch { }
        }

        var cts = new CancellationTokenSource();
        _readCts[peerId] = cts;
        var token = cts.Token;

        Task.Run(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var packet = PrimePacket.ReadFromStream(stream);
                    if (packet == null) break;

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
                DisconnectPeer(peerId);
                ConnectionStateChanged?.Invoke(peerId, DeviceConnectionState.Disconnected, "Связь потеряна");
            }
        }, token);
    }
}
