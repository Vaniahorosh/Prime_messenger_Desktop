using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrimeDesktop.Services
{
    public static class DeviceAnonymizer
    {
        private static readonly Regex MacRegex = new(
            @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^([0-9A-Fa-f]{12})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IpRegex = new(
            @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?$",
            RegexOptions.Compiled);

        private static readonly Regex EmbeddedMacOrPeerRegex = new(
            @"\b([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})\b|\b([0-9A-Fa-f]{12})\b|\bpeer_[0-9a-fA-F]+\b|\bBluetooth#[^\s,\)\]]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsHardwareIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string clean = value.Trim();
            return MacRegex.IsMatch(clean) || 
                   IpRegex.IsMatch(clean) || 
                   clean.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("peer_", StringComparison.OrdinalIgnoreCase);
        }

        public static string MaskMacAddresses(string? input, Func<string, string?>? resolver = null)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return EmbeddedMacOrPeerRegex.Replace(input, match =>
            {
                string val = match.Value;
                if (resolver != null)
                {
                    var resolved = resolver(val);
                    if (!string.IsNullOrWhiteSpace(resolved) && !IsHardwareIdentifier(resolved))
                        return resolved;
                }
                return AnonymizeIdentifier(val);
            });
        }

        public static string GetOpaquePeerId(string? rawHardwareIdentifier)
        {
            if (string.IsNullOrWhiteSpace(rawHardwareIdentifier))
                return "peer_unknown";

            string clean = rawHardwareIdentifier.Trim().ToUpperInvariant();
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(clean));
            string hex = Convert.ToHexString(hash)[..8].ToLowerInvariant();
            return $"peer_{hex}";
        }

        public static string AnonymizeIdentifier(string? rawAddress)
        {
            if (string.IsNullOrWhiteSpace(rawAddress))
                return "Собеседник";

            string clean = rawAddress.Trim();

            // Wi-Fi / LAN IP address
            if (IpRegex.IsMatch(clean))
            {
                return "Prime Телефон 📱";
            }

            // Deterministic 4-digit pseudonym (1000..9999)
            int hash = Math.Abs(clean.ToUpperInvariant().GetHashCode());
            int code = (hash % 9000) + 1000;
            return $"Собеседник #{code}";
        }

        public static string SanitizeDisplayName(string? candidateName, string? rawAddress = null)
        {
            if (string.IsNullOrWhiteSpace(candidateName) ||
                IsHardwareIdentifier(candidateName) ||
                candidateName.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                return AnonymizeIdentifier(rawAddress ?? candidateName);
            }

            return candidateName.Trim();
        }

        public static string SanitizeUsername(string? candidateUsername, string? rawAddress = null)
        {
            if (string.IsNullOrWhiteSpace(candidateUsername) ||
                IsHardwareIdentifier(candidateUsername) ||
                candidateUsername.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(rawAddress) && IpRegex.IsMatch(rawAddress.Trim()))
                    return "prime_companion";

                string seed = rawAddress ?? candidateUsername ?? "user";
                int hash = Math.Abs(seed.ToUpperInvariant().GetHashCode());
                int code = (hash % 9000) + 1000;
                return $"id_{code}";
            }

            return candidateUsername.Trim().TrimStart('@');
        }

        /// <summary>
        /// Identifies unwanted device names such as headphones, earbuds, smartwatches, mice, etc.
        /// </summary>
        public static bool IsDisallowedPeripherals(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            string name = deviceName.ToLowerInvariant();

            string[] blacklist = new[]
            {
                "buds", "airpods", "earphones", "headphone", "headset", "earbuds",
                "watch", "band", "gear", "fit", "mouse", "keyboard", "trackpad",
                "speaker", "soundbar", "jbl", "sony wh-", "sony wf-", "bose", "audio",
                "gamepad", "controller", "printer", "scanner", "tv", "dongle"
            };

            foreach (var b in blacklist)
            {
                if (name.Contains(b)) return true;
            }

            return false;
        }
    }
}
