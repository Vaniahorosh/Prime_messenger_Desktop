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
