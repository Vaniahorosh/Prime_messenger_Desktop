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
}
