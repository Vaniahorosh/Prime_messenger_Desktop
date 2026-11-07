#![allow(dead_code)]

use regex::Regex;
use sha2::{Digest, Sha256};
use std::sync::LazyLock;

static MAC_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^([0-9A-Fa-f]{12})$").unwrap()
});

static IP_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?$").unwrap()
});

static EMBEDDED_IDENTIFIER_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)\b([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})\b|\b([0-9A-Fa-f]{12})\b|\bpeer_[0-9a-fA-F]+\b|\bBluetooth#[^\s,\)\]]+").unwrap()
});

/// Returns true if the given value contains hardware address signatures (MAC, IP, Bluetooth).
pub fn is_hardware_identifier(value: &str) -> bool {
    let clean = value.trim();
    if clean.is_empty() {
        return false;
    }

    clean.starts_with("Bluetooth#")
        || clean.starts_with("peer_")
        || MAC_REGEX.is_match(clean)
        || IP_REGEX.is_match(clean)
        || EMBEDDED_IDENTIFIER_REGEX.is_match(clean)
}

/// Computes a deterministic opaque peer ID from a hardware address: `peer_{sha256[..8]}`
pub fn get_opaque_peer_id(raw_hardware_id: &str) -> String {
    let clean = raw_hardware_id.trim().to_uppercase();
    if clean.is_empty() {
        return "peer_unknown".to_string();
    }

    let mut hasher = Sha256::new();
    hasher.update(clean.as_bytes());
    let result = hasher.finalize();
    let hex = format!("{:x}", result);
    let short_id = if hex.len() >= 8 { &hex[..8] } else { &hex };
    format!("peer_{}", short_id)
}

/// Normalizes MAC address string to standard uppercase format (XX:XX:XX:XX:XX:XX)
pub fn normalize_mac_address(address_or_id: &str) -> String {
    let mut cleaned = address_or_id.trim().trim_matches(|c| c == '(' || c == ')' || c == '[' || c == ']').to_string();

    if let Some(pos) = cleaned.rfind('-') {
        if cleaned.contains('#') && pos < cleaned.len() - 1 {
            cleaned = cleaned[pos + 1..].to_string();
        }
    }

    let hex_only: String = cleaned.chars().filter(|c| c.is_ascii_hexdigit()).collect();
    if hex_only.len() == 12 {
        if let Ok(val) = u64::from_str_radix(&hex_only, 16) {
            return format!(
                "{:02X}:{:02X}:{:02X}:{:02X}:{:02X}:{:02X}",
                (val >> 40) & 0xFF,
                (val >> 32) & 0xFF,
                (val >> 24) & 0xFF,
                (val >> 16) & 0xFF,
                (val >> 8) & 0xFF,
                val & 0xFF
            );
        }
    }

    cleaned.to_uppercase()
}

/// Masks physical hardware addresses in strings with anonymized peer tags.
pub fn mask_hardware_addresses(input: &str) -> String {
    EMBEDDED_IDENTIFIER_REGEX
        .replace_all(input, |caps: &regex::Captures| {
            let matched = caps.get(0).map_or("", |m| m.as_str());
            get_opaque_peer_id(matched)
        })
        .to_string()
}
