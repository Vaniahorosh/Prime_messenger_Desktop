using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PrimeDesktop.Models;

namespace PrimeDesktop.Network;

public interface IPrimeNetworkService
{
    event Action<string, DeviceConnectionState, string> ConnectionStateChanged;
    event Action<string, PacketType, byte[]> PacketReceived;
    event Action<string, string> ErrorOccurred;

    Task StartServerAsync();
    Task StopServerAsync();

    Task<bool> ConnectAsync(string deviceAddressOrIp);
    Task DisconnectAsync(string deviceAddressOrIp);
    void ForgetDevice(string deviceAddressOrIp);
    void RegisterPeerAddress(string peerId, string rawHardwareAddress);
    string ResolveHardwareAddress(string peerIdOrAddress);
    string ResolvePeerId(string hardwareAddressOrPeerId);

    Task SendPacketAsync(string targetAddress, PacketType type, byte[] payload);
    Task<List<ChatItem>> DiscoverDevicesAsync();
}
