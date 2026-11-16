using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PrimeDesktop.Services;

/// <summary>
/// Bridge to high-performance Rust Native Core (prime_native.dll).
/// Provides hardware acceleration, Blake3/SHA-256 hashing, AES-256-GCM encryption,
/// 5-byte protocol framing, and device anonymization with seamless managed fallback.
/// </summary>
public static class PrimeNativeCore
{
    private const string DllName = "prime_native.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_native_version")]
    private static extern IntPtr NativeVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_native_is_healthy")]
    private static extern int NativeIsHealthy();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_crypto_derive_key")]
    private static extern int NativeDeriveKey(string passphrase, byte[] outKey);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_crypto_encrypt_aes256_gcm")]
    private static extern int NativeEncrypt(
        byte[] plaintext,
        UIntPtr plainLen,
        byte[] key32,
        byte[] outBuf,
        UIntPtr outBufMax,
        out UIntPtr outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_crypto_decrypt_aes256_gcm")]
    private static extern int NativeDecrypt(
        byte[] ciphertext,
        UIntPtr cipherLen,
        byte[] key32,
        byte[] outBuf,
        UIntPtr outBufMax,
        out UIntPtr outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_hash_sha256")]
    private static extern int NativeHashSha256(byte[] data, UIntPtr len, StringBuilder outHex, UIntPtr max);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_hash_blake3")]
    private static extern int NativeHashBlake3(byte[] data, UIntPtr len, StringBuilder outHex, UIntPtr max);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_packet_build_frame")]
    private static extern int NativeBuildFrame(
        byte packetType,
        byte[]? payload,
        UIntPtr payloadLen,
        byte[] outFrame,
        UIntPtr outFrameMax,
        out UIntPtr outFrameLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_packet_parse_header")]
    private static extern int NativeParseHeader(byte[] header5, out byte outType, out uint outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_anonymize_peer_id")]
    private static extern int NativeAnonymizePeerId(string hardwareId, StringBuilder outBuf, UIntPtr max);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_is_hardware_identifier")]
    private static extern int NativeIsHardwareIdentifier(string id);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_is_client_initiator")]
    private static extern int NativeIsClientInitiator(string localName, string remoteName);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_build_text_payload")]
    private static extern int NativeBuildTextPayload(
        string msgId,
        string text,
        string? replyToId,
        string? replyToSender,
        string? replyToText,
        byte[] outBuf,
        UIntPtr outBufMax,
        out UIntPtr outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_build_reaction_payload")]
    private static extern int NativeBuildReactionPayload(
        string msgId,
        string? reaction,
        string authorLogin,
        byte[] outBuf,
        UIntPtr outBufMax,
        out UIntPtr outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_build_edit_payload")]
    private static extern int NativeBuildEditPayload(
        string msgId,
        string newText,
        byte[] outBuf,
        UIntPtr outBufMax,
        out UIntPtr outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_build_handshake_payload")]
    private static extern int NativeBuildHandshakePayload(
        string displayName,
        string username,
        int isAck,
        byte[] outBuf,
        UIntPtr outBufMax,
        out UIntPtr outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_parse_handshake_payload")]
    private static extern int NativeParseHandshakePayload(
        byte[] payload,
        UIntPtr payloadLen,
        StringBuilder outName,
        UIntPtr outNameMax,
        StringBuilder outLogin,
        UIntPtr outLoginMax,
        StringBuilder outPresence,
        UIntPtr outPresenceMax,
        out int outIsAck);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_parse_photo_meta")]
    private static extern int NativeParsePhotoMeta(
        byte[] payload,
        UIntPtr payloadLen,
        StringBuilder outMsgId,
        UIntPtr outMsgIdMax,
        StringBuilder outCaption,
        UIntPtr outCaptionMax,
        out int outIsCollage,
        out UIntPtr outPhotoOffset,
        out UIntPtr outPhotoLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "prime_protocol_parse_file_header")]
    private static extern int NativeParseFileHeader(
        byte[] payload,
        UIntPtr payloadLen,
        StringBuilder outMsgId,
        UIntPtr outMsgIdMax,
        StringBuilder outFileName,
        UIntPtr outFileNameMax,
        StringBuilder outDuration,
        UIntPtr outDurationMax,
        out int outIsVoice,
        StringBuilder outCaption,
        UIntPtr outCaptionMax,
        out UIntPtr outFileOffset,
        out UIntPtr outFileLen);

    public static bool IsAvailable { get; }
    public static string Version { get; } = "Managed Core";

    static PrimeNativeCore()
    {
        try
        {
            if (NativeIsHealthy() == 1)
            {
                IntPtr verPtr = NativeVersion();
                Version = Marshal.PtrToStringAnsi(verPtr) ?? "1.0.0-rust-native";
                IsAvailable = true;
                App.Log($"[PrimeNativeCore] High-performance Rust native core active: {Version}");
                return;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PrimeNativeCore] Rust native core not present or fallback active: {ex.Message}");
        }

        IsAvailable = false;
    }

    public static byte[] DeriveKey(string passphrase)
    {
        if (IsAvailable)
        {
            try
            {
                byte[] key = new byte[32];
                if (NativeDeriveKey(passphrase, key) == 0)
                {
                    return key;
                }
            }
            catch { }
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(passphrase));
    }

    public static byte[]? EncryptAesGcm(byte[] plaintext, byte[] key)
    {
        if (plaintext == null || key == null || key.Length != 32) return null;

        if (IsAvailable)
        {
            try
            {
                int maxOut = plaintext.Length + 48;
                byte[] outBuf = new byte[maxOut];
                if (NativeEncrypt(plaintext, (UIntPtr)plaintext.Length, key, outBuf, (UIntPtr)maxOut, out UIntPtr written) == 0)
                {
                    byte[] result = new byte[(int)written];
                    Buffer.BlockCopy(outBuf, 0, result, 0, (int)written);
                    return result;
                }
            }
            catch { }
        }

        // Managed fallback
        try
        {
            byte[] nonce = new byte[12];
            System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using var aes = new System.Security.Cryptography.AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            byte[] full = new byte[12 + ciphertext.Length + 16];
            Buffer.BlockCopy(nonce, 0, full, 0, 12);
            Buffer.BlockCopy(ciphertext, 0, full, 12, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, full, 12 + ciphertext.Length, 16);
            return full;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? DecryptAesGcm(byte[] ciphertextWithNonce, byte[] key)
    {
        if (ciphertextWithNonce == null || ciphertextWithNonce.Length < 28 || key == null || key.Length != 32)
            return null;

        if (IsAvailable)
        {
            try
            {
                byte[] outBuf = new byte[ciphertextWithNonce.Length];
                if (NativeDecrypt(ciphertextWithNonce, (UIntPtr)ciphertextWithNonce.Length, key, outBuf, (UIntPtr)outBuf.Length, out UIntPtr written) == 0)
                {
                    byte[] result = new byte[(int)written];
                    Buffer.BlockCopy(outBuf, 0, result, 0, (int)written);
                    return result;
                }
            }
            catch { }
        }

        // Managed fallback
        try
        {
            byte[] nonce = new byte[12];
            Buffer.BlockCopy(ciphertextWithNonce, 0, nonce, 0, 12);
            int dataLen = ciphertextWithNonce.Length - 12 - 16;
            byte[] cipherOnly = new byte[dataLen];
            Buffer.BlockCopy(ciphertextWithNonce, 12, cipherOnly, 0, dataLen);
            byte[] tag = new byte[16];
            Buffer.BlockCopy(ciphertextWithNonce, ciphertextWithNonce.Length - 16, tag, 0, 16);

            byte[] plain = new byte[dataLen];
            using var aes = new System.Security.Cryptography.AesGcm(key, 16);
            aes.Decrypt(nonce, cipherOnly, tag, plain);
            return plain;
        }
        catch
        {
            return null;
        }
    }

    public static string HashBlake3(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;

        if (IsAvailable)
        {
            try
            {
                var sb = new StringBuilder(128);
                if (NativeHashBlake3(data, (UIntPtr)data.Length, sb, (UIntPtr)128) == 0)
                {
                    return sb.ToString();
                }
            }
            catch { }
        }

        // Fallback to SHA256 hex
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] BuildPacketFrame(byte packetType, byte[] payload)
    {
        int pLen = payload?.Length ?? 0;

        if (IsAvailable)
        {
            try
            {
                int maxFrame = 5 + pLen;
                byte[] frame = new byte[maxFrame];
                if (NativeBuildFrame(packetType, payload, (UIntPtr)pLen, frame, (UIntPtr)maxFrame, out UIntPtr written) == 0)
                {
                    return frame;
                }
            }
            catch { }
        }

        // Managed fallback: 1B Type + 4B Big-Endian Length + Payload
        byte[] f = new byte[5 + pLen];
        f[0] = packetType;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(f.AsSpan(1, 4), pLen);
        if (pLen > 0 && payload != null)
        {
            Buffer.BlockCopy(payload, 0, f, 5, pLen);
        }
        return f;
    }

    public static bool ParsePacketHeader(byte[] header5, out byte packetType, out int length)
    {
        packetType = 0;
        length = 0;
        if (header5 == null || header5.Length < 5) return false;

        if (IsAvailable)
        {
            try
            {
                if (NativeParseHeader(header5, out byte pType, out uint pLen) == 0)
                {
                    packetType = pType;
                    length = (int)pLen;
                    return true;
                }
            }
            catch { }
        }

        // Managed fallback
        packetType = header5[0];
        length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header5.AsSpan(1, 4));
        return true;
    }

    public static string AnonymizePeerId(string rawHardwareId)
    {
        if (string.IsNullOrWhiteSpace(rawHardwareId)) return "peer_unknown";

        if (IsAvailable)
        {
            try
            {
                var sb = new StringBuilder(64);
                if (NativeAnonymizePeerId(rawHardwareId, sb, (UIntPtr)64) == 0)
                {
                    return sb.ToString();
                }
            }
            catch { }
        }

        // Managed fallback
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(rawHardwareId.Trim().ToUpperInvariant()));
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"peer_{hex[..Math.Min(8, hex.Length)]}";
    }

    public static bool IsHardwareIdentifier(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        if (IsAvailable)
        {
            try
            {
                return NativeIsHardwareIdentifier(id) == 1;
            }
            catch { }
        }

        return id.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("peer_", StringComparison.OrdinalIgnoreCase) ||
               System.Text.RegularExpressions.Regex.IsMatch(id, @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$") ||
               System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}");
    }

    public static bool IsClientInitiator(string localName, string remoteName)
    {
        if (IsAvailable)
        {
            try
            {
                return NativeIsClientInitiator(localName ?? string.Empty, remoteName ?? string.Empty) == 1;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(remoteName)) return true;
        string loc = localName.Trim().ToUpperInvariant();
        string rem = remoteName.Trim().ToUpperInvariant();
        int comp = string.Compare(loc, rem, StringComparison.Ordinal);
        if (comp != 0) return comp > 0;

        int hLoc = 0;
        for (int i = 0; i < loc.Length; i++) hLoc = unchecked(31 * hLoc + loc[i]);
        int hRem = 0;
        for (int i = 0; i < rem.Length; i++) hRem = unchecked(31 * hRem + rem[i]);
        return hLoc >= hRem;
    }

    public static byte[] BuildTextPayload(string msgId, string text, string? replyToId = null, string? replyToSender = null, string? replyToText = null)
    {
        if (IsAvailable)
        {
            try
            {
                int maxBuf = (msgId?.Length ?? 0) + (text?.Length ?? 0) + (replyToId?.Length ?? 0) + (replyToSender?.Length ?? 0) + (replyToText?.Length ?? 0) + 128;
                maxBuf = Math.Max(maxBuf, 256) * 4;
                byte[] outBuf = new byte[maxBuf];
                if (NativeBuildTextPayload(msgId ?? "", text ?? "", replyToId, replyToSender, replyToText, outBuf, (UIntPtr)maxBuf, out UIntPtr written) == 0)
                {
                    byte[] res = new byte[(int)written];
                    Buffer.BlockCopy(outBuf, 0, res, 0, (int)written);
                    return res;
                }
            }
            catch { }
        }

        var sb = new StringBuilder();
        sb.Append(msgId).Append(":::").Append(text);
        if (!string.IsNullOrEmpty(replyToId))
        {
            sb.Append(":::REPLY:::")
              .Append(replyToId).Append(":::")
              .Append(replyToSender ?? "").Append(":::")
              .Append(replyToText ?? "");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] BuildReactionPayload(string msgId, string? reaction, string authorLogin)
    {
        if (IsAvailable)
        {
            try
            {
                int maxBuf = (msgId?.Length ?? 0) + (reaction?.Length ?? 0) + (authorLogin?.Length ?? 0) + 64;
                maxBuf = Math.Max(maxBuf, 128) * 4;
                byte[] outBuf = new byte[maxBuf];
                if (NativeBuildReactionPayload(msgId ?? "", reaction, authorLogin ?? "", outBuf, (UIntPtr)maxBuf, out UIntPtr written) == 0)
                {
                    byte[] res = new byte[(int)written];
                    Buffer.BlockCopy(outBuf, 0, res, 0, (int)written);
                    return res;
                }
            }
            catch { }
        }

        string rx = string.IsNullOrWhiteSpace(reaction) ? "REMOVE" : reaction;
        return Encoding.UTF8.GetBytes($"{msgId}:::{rx}:::{authorLogin}");
    }

    public static byte[] BuildEditPayload(string msgId, string newText)
    {
        if (IsAvailable)
        {
            try
            {
                int maxBuf = (msgId?.Length ?? 0) + (newText?.Length ?? 0) + 32;
                maxBuf = Math.Max(maxBuf, 128) * 4;
                byte[] outBuf = new byte[maxBuf];
                if (NativeBuildEditPayload(msgId ?? "", newText ?? "", outBuf, (UIntPtr)maxBuf, out UIntPtr written) == 0)
                {
                    byte[] res = new byte[(int)written];
                    Buffer.BlockCopy(outBuf, 0, res, 0, (int)written);
                    return res;
                }
            }
            catch { }
        }

        return Encoding.UTF8.GetBytes($"{msgId}:::{newText}");
    }

    public static byte[] BuildHandshakePayload(string displayName, string username, bool isAck = false)
    {
        if (IsAvailable)
        {
            try
            {
                int maxBuf = (displayName?.Length ?? 0) + (username?.Length ?? 0) + 256;
                byte[] outBuf = new byte[maxBuf * 4];
                if (NativeBuildHandshakePayload(displayName ?? "", username ?? "", isAck ? 1 : 0, outBuf, (UIntPtr)outBuf.Length, out UIntPtr written) == 0)
                {
                    byte[] res = new byte[(int)written];
                    Buffer.BlockCopy(outBuf, 0, res, 0, (int)written);
                    return res;
                }
            }
            catch { }
        }

        string prefix = isAck ? "HANDSHAKE_ACK:" : "HANDSHAKE:";
        string content = $"{prefix}name={displayName};login={username};version=100;features=vn;presence=ONLINE;device=pc;platform=desktop";
        return Encoding.UTF8.GetBytes(content);
    }

    public static bool TryParseHandshake(byte[] payload, out string? displayName, out string? username, out string? presence, out bool isAck)
    {
        displayName = null;
        username = null;
        presence = null;
        isAck = false;
        if (payload == null || payload.Length == 0) return false;

        if (IsAvailable)
        {
            try
            {
                var sbName = new StringBuilder(256);
                var sbLogin = new StringBuilder(256);
                var sbPres = new StringBuilder(64);
                if (NativeParseHandshakePayload(payload, (UIntPtr)payload.Length, sbName, (UIntPtr)256, sbLogin, (UIntPtr)256, sbPres, (UIntPtr)64, out int ackVal) == 0)
                {
                    displayName = sbName.ToString();
                    username = sbLogin.ToString();
                    presence = sbPres.ToString();
                    isAck = ackVal == 1;
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    public static bool TryParsePhotoMeta(byte[] payload, out string msgId, out string caption, out bool isCollage, out int photoOffset, out int photoLen)
    {
        msgId = string.Empty;
        caption = string.Empty;
        isCollage = false;
        photoOffset = 0;
        photoLen = 0;
        if (payload == null || payload.Length == 0) return false;

        if (IsAvailable)
        {
            try
            {
                var sbId = new StringBuilder(128);
                var sbCap = new StringBuilder(1024);
                if (NativeParsePhotoMeta(payload, (UIntPtr)payload.Length, sbId, (UIntPtr)128, sbCap, (UIntPtr)1024, out int colVal, out UIntPtr pOffset, out UIntPtr pLen) == 0)
                {
                    msgId = sbId.ToString();
                    caption = sbCap.ToString();
                    isCollage = colVal == 1;
                    photoOffset = (int)pOffset;
                    photoLen = (int)pLen;
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    public static bool TryParseFileHeader(byte[] payload, out string msgId, out string fileName, out string duration, out bool isVoice, out string caption, out int fileOffset, out int fileLen)
    {
        msgId = string.Empty;
        fileName = "file.bin";
        duration = "00:00";
        isVoice = false;
        caption = string.Empty;
        fileOffset = 0;
        fileLen = 0;
        if (payload == null || payload.Length == 0) return false;

        if (IsAvailable)
        {
            try
            {
                var sbId = new StringBuilder(128);
                var sbName = new StringBuilder(256);
                var sbDur = new StringBuilder(64);
                var sbCap = new StringBuilder(1024);
                if (NativeParseFileHeader(payload, (UIntPtr)payload.Length, sbId, (UIntPtr)128, sbName, (UIntPtr)256, sbDur, (UIntPtr)64, out int voiceVal, sbCap, (UIntPtr)1024, out UIntPtr fOffset, out UIntPtr fLen) == 0)
                {
                    msgId = sbId.ToString();
                    fileName = sbName.ToString();
                    duration = sbDur.ToString();
                    isVoice = voiceVal == 1;
                    caption = sbCap.ToString();
                    fileOffset = (int)fOffset;
                    fileLen = (int)fLen;
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
}
