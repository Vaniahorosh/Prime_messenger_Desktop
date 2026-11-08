use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use sha2::{Digest, Sha256};

pub struct PrimeCrypto;

impl PrimeCrypto {
    /// Derives 32-byte AES-256 key from a passphrase or identity string using SHA-256
    pub fn derive_key(passphrase: &str) -> [u8; 32] {
        let mut hasher = Sha256::new();
        hasher.update(passphrase.as_bytes());
        let result = hasher.finalize();
        let mut key = [0u8; 32];
        key.copy_from_slice(&result[..32]);
        key
    }

    /// Encrypts plaintext using AES-256-GCM with a 12-byte nonce prepended to the output.
    pub fn encrypt(data: &[u8], key_bytes: &[u8; 32]) -> Result<Vec<u8>, String> {
        let cipher = Aes256Gcm::new_from_slice(key_bytes)
            .map_err(|e| format!("Failed to create cipher: {}", e))?;

        let mut nonce_bytes = [0u8; 12];
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default()
            .as_nanos();
        let hash = Sha256::digest(now.to_le_bytes());
        nonce_bytes.copy_from_slice(&hash[..12]);

        let nonce = Nonce::from_slice(&nonce_bytes);

        let ciphertext = cipher
            .encrypt(nonce, data)
            .map_err(|e| format!("Encryption error: {}", e))?;

        let mut output = Vec::with_capacity(12 + ciphertext.len());
        output.extend_from_slice(&nonce_bytes);
        output.extend_from_slice(&ciphertext);
        Ok(output)
    }

    /// Decrypts AES-256-GCM payload where the first 12 bytes are the nonce.
    pub fn decrypt(data: &[u8], key_bytes: &[u8; 32]) -> Result<Vec<u8>, String> {
        if data.len() < 12 {
            return Err("Payload too short for nonce".into());
        }

        let cipher = Aes256Gcm::new_from_slice(key_bytes)
            .map_err(|e| format!("Failed to create cipher: {}", e))?;

        let nonce = Nonce::from_slice(&data[..12]);
        let ciphertext = &data[12..];

        cipher
            .decrypt(nonce, ciphertext)
            .map_err(|e| format!("Decryption error: {}", e))
    }

    /// Computes SHA-256 hex string
    pub fn hash_sha256(data: &[u8]) -> String {
        let mut hasher = Sha256::new();
        hasher.update(data);
        format!("{:x}", hasher.finalize())
    }

    /// Computes Blake3 hex string for ultra-fast hashing
    pub fn hash_blake3(data: &[u8]) -> String {
        let hash = blake3::hash(data);
        hash.to_hex().to_string()
    }
}
