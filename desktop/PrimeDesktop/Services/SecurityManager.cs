using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;

namespace PrimeDesktop.Services
{
    public static class SecurityManager
    {
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private static readonly byte[] _key;

        static SecurityManager()
        {
            string keyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop");
            Directory.CreateDirectory(keyDir);
            string keyPath = Path.Combine(keyDir, "e2ee_hardware.key");

            if (File.Exists(keyPath))
            {
                byte[] raw = File.ReadAllBytes(keyPath);
                try
                {
                    _key = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    _key = GenerateAndSaveKey(keyPath);
                }
            }
            else
            {
                _key = GenerateAndSaveKey(keyPath);
            }
        }

        private static byte[] GenerateAndSaveKey(string path)
        {
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            try
            {
                byte[] encrypted = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, encrypted);
            }
            catch
            {
                // Fallback to direct bytes if DPAPI is unavailable
                File.WriteAllBytes(path, key);
            }
            return key;
        }

        public static bool SetScreenshotProtection(Window window, bool enable)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd != IntPtr.Zero)
                {
                    return SetWindowDisplayAffinity(hwnd, enable ? WDA_MONITOR : WDA_NONE);
                }
            }
            catch { }
            return false;
        }

        public static byte[] Encrypt(byte[] plaintext)
        {
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            byte[] result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);
            return result;
        }

        public static byte[] Decrypt(byte[] encryptedData)
        {
            if (encryptedData.Length < 28)
                throw new ArgumentException("Недопустимый размер зашифрованных данных.");

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            int cipherLen = encryptedData.Length - 28;
            byte[] ciphertext = new byte[cipherLen];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, 12);
            Buffer.BlockCopy(encryptedData, 12, tag, 0, 16);
            Buffer.BlockCopy(encryptedData, 28, ciphertext, 0, cipherLen);

            byte[] plaintext = new byte[cipherLen];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
    }
}