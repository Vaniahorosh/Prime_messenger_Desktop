# Prime Messenger & Prime Desktop — Protocol Specification & Architecture Rules

## 1. Packet Framing & Transport
All RFCOMM (Bluetooth Classic) and TCP Companion Bridge communications share an identical binary frame:
```
[1 Byte: PacketType] [4 Bytes: Big-Endian PayloadLength] [N Bytes: Raw Payload]
```
- **Packet Header:** Always 5 bytes atomic (`header[0]` = type, `BinaryPrimitives.ReadInt32BigEndian(header[1..5])` = length).
- **Chunk Size:** Write payload in chunks of **16,384 bytes (16 KB)** with `Flush()`. This prevents RFCOMM stack buffer overflows on Windows WinRT and Android Linux BlueZ.
- **Maximum Buffer:** Guard limit of 100 MB (`0x06400000`) protects against memory exhaustion.
- **Write Serialization:** All socket writes must be synchronized via a single-queue / `SemaphoreSlim(1, 1)` to prevent COM socket aborts (`WSAECONNABORTED / 0x80072745`).

---

## 2. Packet Types
| Byte Code | Enum | Description |
|-----------|------|-------------|
| `0x01` | `Text` | UTF-8 Text message, Handshake, or Handshake Ack |
| `0x02` | `Photo` | Raw image bytes (JPEG/PNG/WEBP) |
| `0x03` | `Ping` | Heartbeat keep-alive check |
| `0x04` | `Disconnect` | Graceful disconnection notice |
| `0x06` | `Ack` | Heartbeat keep-alive reply |
| `0x07` | `ReadReceipt` | Message delivery & read confirmation (UTF-8 `msgId`) |
| `0x08` | `ChatDeleted` | Peer deleted conversation; recipient immediately wipes chat and forgets device |
| `0x09` | `Edit` | Message edit payload (`msgId:::newText`) |
| `0x0A` | `DeleteMessage` | Message deletion payload (UTF-8 `msgId`) |
| `0x0B` | `Typing` | User typing indicator (expires after 3 seconds) |
| `0x0C` | `Presence` | Presence string (`ONLINE` or `OFFLINE`) |
| `0x0D` | `Reaction` | Emoji reaction (`msgId:::emoji:::authorLogin`) or removal (`msgId:::REMOVE:::authorLogin`) |
| `0x0E` | `Voice` / `File` | Voice notes (M4A) or arbitrary binary files |

---

## 3. Connection Handshake & Version Negotiation
Immediately upon RFCOMM connection establishment, both endpoints exchange a handshake text packet:
```
HANDSHAKE:name={DisplayName};login={Username};version=100;features=vn;presence=ONLINE;device=pc;platform=desktop
```
- **Platform Identification:** `platform=desktop` and `device=pc` inform the Android client that the peer is Prime Desktop.
- **Version Compatibility:** Version `100` satisfies Android's `isRecipientVersionBelow64()` check, suppressing version warning banners ("Пользователь использует другую версию").
- **Reply:** Recipient responds with `HANDSHAKE_ACK:` matching the same parameter format.

---

## 4. Connection States & UI Status Indicators
Both Android and Desktop clients display synchronized, standardized connection indicators:

| State | Status Text | Hex Color | Indicator Dot |
|-------|-------------|-----------|---------------|
| `Connecting` / `Listening` | **Установка связи...** | `#D29922` (Amber) | Hidden / Pulsing |
| `Connected` | **в сети** | `#2EA043` (Green) | Visible (Green) |
| `Typing` | **Печатает...** | `#2EA043` (Green) | Visible (Green) |
| `Disconnected` | **не в сети** | `#8B949E` (Gray) | Hidden |

---

## 5. Role Arbitration & Auto-Reconnect
1. **Arbitration (`IsClientInitiator`):**
   - When both sides initiate connection simultaneously, `IsClientInitiator(localName, remoteName)` determines roles deterministically using string comparison.
   - The device with the higher alphabetical name connects as RFCOMM Client; the other acts as Server.
   - If the server receives no client connection within **3.5 seconds**, it falls back to connecting as client.
2. **Smart KeepAlive:**
   - Both sides monitor socket activity.
   - KeepAlive `Ping` (`0x03`) is transmitted **only if idle for > 6.0 seconds**, preventing ping flooding.
   - Receiving `Ping` immediately triggers an `Ack` (`0x06`) reply.
3. **Auto-Retry Backoff:**
   - If an active connection drops unexpectedly (not manually disconnected), the client triggers automatic background reconnection up to **4 attempts** with backoff:
     - Attempt 1: 1.0s
     - Attempt 2: 2.0s
     - Attempt 3: 4.0s
     - Attempt 4: 8.0s

---

## 6. Privacy & Security Rules
- **No Physical MAC Exposure:** Hardware MAC addresses are strictly isolated in `BluetoothRfcommService`. UI, view models, and persistent storage must use anonymized `PeerId`.
- **Chat Isolation:** Outgoing messages and packets must always bind to a local snapshot (`var targetChat = SelectedChat; if (targetChat == null) return;`) to prevent misdirected sends when users switch chats.
