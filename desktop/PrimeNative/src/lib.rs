pub mod anonymizer;
pub mod crypto;
pub mod protocol;

use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::slice;

use anonymizer::{get_opaque_peer_id, is_hardware_identifier};
use crypto::PrimeCrypto;
use protocol::PrimeProtocol;

static VERSION: &str = "1.0.0-rust-native\0";

/// Returns native core version string
#[no_mangle]
pub extern "C" fn prime_native_version() -> *const c_char {
    VERSION.as_ptr() as *const c_char
}

/// Health check: returns 1 if native core is loaded and operational
#[no_mangle]
pub extern "C" fn prime_native_is_healthy() -> i32 {
    1
}

/// Derives a 32-byte key from passphrase via SHA-256
#[no_mangle]
pub extern "C" fn prime_crypto_derive_key(
    passphrase: *const c_char,
    out_key: *mut u8,
) -> i32 {
    if passphrase.is_null() || out_key.is_null() {
        return -1;
    }
    let c_str = unsafe { CStr::from_ptr(passphrase) };
    let pass_str = match c_str.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let key = PrimeCrypto::derive_key(pass_str);
    unsafe {
        std::ptr::copy_nonoverlapping(key.as_ptr(), out_key, 32);
    }
    0
}

/// Encrypts plaintext using AES-256-GCM.
/// Out buffer receives 12-byte nonce followed by ciphertext.
#[no_mangle]
pub extern "C" fn prime_crypto_encrypt_aes256_gcm(
    plaintext: *const u8,
    plaintext_len: usize,
    key_32bytes: *const u8,
    out_buf: *mut u8,
    out_buf_max: usize,
    out_len: *mut usize,
) -> i32 {
    if plaintext.is_null() || key_32bytes.is_null() || out_buf.is_null() || out_len.is_null() {
        return -1;
    }
    let data = unsafe { slice::from_raw_parts(plaintext, plaintext_len) };
    let mut key = [0u8; 32];
    unsafe {
        std::ptr::copy_nonoverlapping(key_32bytes, key.as_mut_ptr(), 32);
    }

    match PrimeCrypto::encrypt(data, &key) {
        Ok(ciphertext) => {
            if ciphertext.len() > out_buf_max {
                return -3; // Output buffer too small
            }
            unsafe {
                std::ptr::copy_nonoverlapping(ciphertext.as_ptr(), out_buf, ciphertext.len());
                *out_len = ciphertext.len();
            }
            0
        }
        Err(_) => -2,
    }
}

/// Decrypts AES-256-GCM payload with prepended 12-byte nonce.
#[no_mangle]
pub extern "C" fn prime_crypto_decrypt_aes256_gcm(
    ciphertext: *const u8,
    ciphertext_len: usize,
    key_32bytes: *const u8,
    out_buf: *mut u8,
    out_buf_max: usize,
    out_len: *mut usize,
) -> i32 {
    if ciphertext.is_null() || key_32bytes.is_null() || out_buf.is_null() || out_len.is_null() {
        return -1;
    }
    let data = unsafe { slice::from_raw_parts(ciphertext, ciphertext_len) };
    let mut key = [0u8; 32];
    unsafe {
        std::ptr::copy_nonoverlapping(key_32bytes, key.as_mut_ptr(), 32);
    }

    match PrimeCrypto::decrypt(data, &key) {
        Ok(plaintext) => {
            if plaintext.len() > out_buf_max {
                return -3; // Buffer too small
            }
            unsafe {
                std::ptr::copy_nonoverlapping(plaintext.as_ptr(), out_buf, plaintext.len());
                *out_len = plaintext.len();
            }
            0
        }
        Err(_) => -2,
    }
}

/// Computes Blake3 hash as hex string
#[no_mangle]
pub extern "C" fn prime_hash_blake3(
    data: *const u8,
    len: usize,
    out_hex: *mut c_char,
    out_hex_max: usize,
) -> i32 {
    if data.is_null() || out_hex.is_null() || out_hex_max < 65 {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(data, len) };
    let hex = PrimeCrypto::hash_blake3(bytes);
    let c_str = match CString::new(hex) {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let c_bytes = c_str.as_bytes_with_nul();
    if c_bytes.len() > out_hex_max {
        return -3;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(c_bytes.as_ptr() as *const c_char, out_hex, c_bytes.len());
    }
    0
}

/// Computes SHA-256 hash as hex string
#[no_mangle]
pub extern "C" fn prime_hash_sha256(
    data: *const u8,
    len: usize,
    out_hex: *mut c_char,
    out_hex_max: usize,
) -> i32 {
    if data.is_null() || out_hex.is_null() || out_hex_max < 65 {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(data, len) };
    let hex = PrimeCrypto::hash_sha256(bytes);
    let c_str = match CString::new(hex) {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let c_bytes = c_str.as_bytes_with_nul();
    if c_bytes.len() > out_hex_max {
        return -3;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(c_bytes.as_ptr() as *const c_char, out_hex, c_bytes.len());
    }
    0
}

/// Builds 5-byte header frame: [Type: 1B][Length: 4B Big-Endian][Payload]
#[no_mangle]
pub extern "C" fn prime_packet_build_frame(
    packet_type: u8,
    payload: *const u8,
    payload_len: usize,
    out_frame: *mut u8,
    out_frame_max: usize,
    out_frame_len: *mut usize,
) -> i32 {
    if out_frame.is_null() || out_frame_len.is_null() {
        return -1;
    }
    let total_len = 5 + payload_len;
    if total_len > out_frame_max {
        return -3;
    }

    let payload_bytes = if payload.is_null() || payload_len == 0 {
        &[]
    } else {
        unsafe { slice::from_raw_parts(payload, payload_len) }
    };

    let frame = PrimeProtocol::build_frame(packet_type, payload_bytes);
    unsafe {
        std::ptr::copy_nonoverlapping(frame.as_ptr(), out_frame, frame.len());
        *out_frame_len = frame.len();
    }
    0
}

/// Parses 5-byte header into packet type and payload length
#[no_mangle]
pub extern "C" fn prime_packet_parse_header(
    header_5bytes: *const u8,
    out_type: *mut u8,
    out_len: *mut u32,
) -> i32 {
    if header_5bytes.is_null() || out_type.is_null() || out_len.is_null() {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(header_5bytes, 5) };
    match PrimeProtocol::parse_header(bytes) {
        Ok((p_type, len)) => {
            unsafe {
                *out_type = p_type;
                *out_len = len;
            }
            0
        }
        Err(_) => -2,
    }
}

/// Computes deterministic opaque peer ID from hardware address
#[no_mangle]
pub extern "C" fn prime_anonymize_peer_id(
    hardware_id: *const c_char,
    out_buf: *mut c_char,
    out_buf_max: usize,
) -> i32 {
    if hardware_id.is_null() || out_buf.is_null() || out_buf_max < 16 {
        return -1;
    }
    let c_str = unsafe { CStr::from_ptr(hardware_id) };
    let id_str = match c_str.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let opaque = get_opaque_peer_id(id_str);
    let c_opaque = match CString::new(opaque) {
        Ok(s) => s,
        Err(_) => return -3,
    };
    let bytes = c_opaque.as_bytes_with_nul();
    if bytes.len() > out_buf_max {
        return -4;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr() as *const c_char, out_buf, bytes.len());
    }
    0
}

/// Checks if string contains hardware address signatures
#[no_mangle]
pub extern "C" fn prime_is_hardware_identifier(identifier: *const c_char) -> i32 {
    if identifier.is_null() {
        return 0;
    }
    let c_str = unsafe { CStr::from_ptr(identifier) };
    match c_str.to_str() {
        Ok(s) => if is_hardware_identifier(s) { 1 } else { 0 },
        Err(_) => 0,
    }
}
