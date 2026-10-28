namespace PrimeDesktop.Models;

/// <summary>
/// Packet types matching Prime Messenger Android protocol
/// </summary>
public enum PacketType : byte
{
    Text = 0x01,
    Photo = 0x02,
    Ping = 0x03,
    Typing = 0x04,
    Edit = 0x05,
    Ack = 0x06,
    Avatar = 0x07,
    ChatDeleted = 0x08,
    Disconnect = 0x09,
    ReadReceipt = 0x0A,
    DeleteMessage = 0x0B,
    Presence = 0x0C,
    Reaction = 0x0D,
    File = 0x0E,
    ProfileUpdate = 0x0F,
    Voice = 0x10,
    KeepAlive = 0xFE
}
