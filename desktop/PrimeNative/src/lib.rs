pub mod anonymizer;
pub mod crypto;
pub mod protocol;

use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::slice;

use anonymizer::{get_opaque_peer_id, is_hardware_identifier};
use crypto::PrimeCrypto;
use protocol::PrimeProtocol;

static VERSION: &str = "1.1.0-rust-native\0";

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

/// Role arbitration: returns 1 if local device should initiate connection, 0 otherwise
#[no_mangle]
pub extern "C" fn prime_protocol_is_client_initiator(
    local_name: *const c_char,
    remote_name: *const c_char,
) -> i32 {
    if local_name.is_null() || remote_name.is_null() {
        return 1;
    }
    let loc = match unsafe { CStr::from_ptr(local_name) }.to_str() {
        Ok(s) => s,
        Err(_) => return 1,
    };
    let rem = match unsafe { CStr::from_ptr(remote_name) }.to_str() {
        Ok(s) => s,
        Err(_) => return 1,
    };
    if PrimeProtocol::is_client_initiator(loc, rem) { 1 } else { 0 }
}

/// Formats text message payload with optional quoted reply preview
#[no_mangle]
pub extern "C" fn prime_protocol_build_text_payload(
    msg_id: *const c_char,
    text: *const c_char,
    reply_to_id: *const c_char,
    reply_to_sender: *const c_char,
    reply_to_text: *const c_char,
    out_buf: *mut u8,
    out_buf_max: usize,
    out_len: *mut usize,
) -> i32 {
    if msg_id.is_null() || text.is_null() || out_buf.is_null() || out_len.is_null() {
        return -1;
    }
    let m_id = match unsafe { CStr::from_ptr(msg_id) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let t = match unsafe { CStr::from_ptr(text) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let r_id = if reply_to_id.is_null() { None } else { unsafe { CStr::from_ptr(reply_to_id) }.to_str().ok() };
    let r_sender = if reply_to_sender.is_null() { None } else { unsafe { CStr::from_ptr(reply_to_sender) }.to_str().ok() };
    let r_text = if reply_to_text.is_null() { None } else { unsafe { CStr::from_ptr(reply_to_text) }.to_str().ok() };

    let payload = PrimeProtocol::build_text_payload(m_id, t, r_id, r_sender, r_text);
    if payload.len() > out_buf_max {
        return -3;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(payload.as_ptr(), out_buf, payload.len());
        *out_len = payload.len();
    }
    0
}

/// Formats reaction payload
#[no_mangle]
pub extern "C" fn prime_protocol_build_reaction_payload(
    msg_id: *const c_char,
    reaction: *const c_char,
    author_login: *const c_char,
    out_buf: *mut u8,
    out_buf_max: usize,
    out_len: *mut usize,
) -> i32 {
    if msg_id.is_null() || author_login.is_null() || out_buf.is_null() || out_len.is_null() {
        return -1;
    }
    let m_id = match unsafe { CStr::from_ptr(msg_id) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let auth = match unsafe { CStr::from_ptr(author_login) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let rx = if reaction.is_null() { None } else { unsafe { CStr::from_ptr(reaction) }.to_str().ok() };

    let payload = PrimeProtocol::build_reaction_payload(m_id, rx, auth);
    if payload.len() > out_buf_max {
        return -3;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(payload.as_ptr(), out_buf, payload.len());
        *out_len = payload.len();
    }
    0
}

/// Formats edit message payload
#[no_mangle]
pub extern "C" fn prime_protocol_build_edit_payload(
    msg_id: *const c_char,
    new_text: *const c_char,
    out_buf: *mut u8,
    out_buf_max: usize,
    out_len: *mut usize,
) -> i32 {
    if msg_id.is_null() || new_text.is_null() || out_buf.is_null() || out_len.is_null() {
        return -1;
    }
    let m_id = match unsafe { CStr::from_ptr(msg_id) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let txt = match unsafe { CStr::from_ptr(new_text) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let payload = PrimeProtocol::build_edit_payload(m_id, txt);
    if payload.len() > out_buf_max {
        return -3;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(payload.as_ptr(), out_buf, payload.len());
        *out_len = payload.len();
    }
    0
}

/// Formats handshake payload
#[no_mangle]
pub extern "C" fn prime_protocol_build_handshake_payload(
    display_name: *const c_char,
    username: *const c_char,
    is_ack: i32,
    out_buf: *mut u8,
    out_buf_max: usize,
    out_len: *mut usize,
) -> i32 {
    if display_name.is_null() || username.is_null() || out_buf.is_null() || out_len.is_null() {
        return -1;
    }
    let name = match unsafe { CStr::from_ptr(display_name) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let login = match unsafe { CStr::from_ptr(username) }.to_str() {
        Ok(s) => s,
        Err(_) => return -2,
    };
    let payload = PrimeProtocol::build_handshake_payload(name, login, is_ack != 0);
    if payload.len() > out_buf_max {
        return -3;
    }
    unsafe {
        std::ptr::copy_nonoverlapping(payload.as_ptr(), out_buf, payload.len());
        *out_len = payload.len();
    }
    0
}

/// Parses handshake payload extracting name, login, presence, and ACK status
#[no_mangle]
pub extern "C" fn prime_protocol_parse_handshake_payload(
    payload: *const u8,
    payload_len: usize,
    out_name: *mut c_char,
    out_name_max: usize,
    out_login: *mut c_char,
    out_login_max: usize,
    out_presence: *mut c_char,
    out_presence_max: usize,
    out_is_ack: *mut i32,
) -> i32 {
    if payload.is_null() || out_name.is_null() || out_login.is_null() || out_presence.is_null() || out_is_ack.is_null() {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(payload, payload_len) };
    match PrimeProtocol::parse_handshake_payload(bytes) {
        Some(info) => {
            let c_name = CString::new(info.display_name).unwrap_or_default();
            let c_login = CString::new(info.username).unwrap_or_default();
            let c_pres = CString::new(info.presence).unwrap_or_default();

            if c_name.as_bytes_with_nul().len() > out_name_max
                || c_login.as_bytes_with_nul().len() > out_login_max
                || c_pres.as_bytes_with_nul().len() > out_presence_max
            {
                return -3;
            }

            unsafe {
                std::ptr::copy_nonoverlapping(c_name.as_ptr(), out_name, c_name.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_login.as_ptr(), out_login, c_login.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_pres.as_ptr(), out_presence, c_pres.as_bytes_with_nul().len());
                *out_is_ack = if info.is_ack { 1 } else { 0 };
            }
            0
        }
        None => -2,
    }
}

/// Parses handshake payload extracting name, login, presence, platform, version, and ACK status
#[no_mangle]
pub extern "C" fn prime_protocol_parse_handshake_ex(
    payload: *const u8,
    payload_len: usize,
    out_name: *mut c_char,
    out_name_max: usize,
    out_login: *mut c_char,
    out_login_max: usize,
    out_presence: *mut c_char,
    out_presence_max: usize,
    out_platform: *mut c_char,
    out_platform_max: usize,
    out_version: *mut i32,
    out_is_ack: *mut i32,
) -> i32 {
    if payload.is_null() || out_name.is_null() || out_login.is_null() || out_presence.is_null() || out_platform.is_null() || out_version.is_null() || out_is_ack.is_null() {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(payload, payload_len) };
    match PrimeProtocol::parse_handshake_payload(bytes) {
        Some(info) => {
            let c_name = CString::new(info.display_name).unwrap_or_default();
            let c_login = CString::new(info.username).unwrap_or_default();
            let c_pres = CString::new(info.presence).unwrap_or_default();
            let c_plat = CString::new(info.platform).unwrap_or_default();

            if c_name.as_bytes_with_nul().len() > out_name_max
                || c_login.as_bytes_with_nul().len() > out_login_max
                || c_pres.as_bytes_with_nul().len() > out_presence_max
                || c_plat.as_bytes_with_nul().len() > out_platform_max
            {
                return -3;
            }

            unsafe {
                std::ptr::copy_nonoverlapping(c_name.as_ptr(), out_name, c_name.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_login.as_ptr(), out_login, c_login.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_pres.as_ptr(), out_presence, c_pres.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_plat.as_ptr(), out_platform, c_plat.as_bytes_with_nul().len());
                *out_version = info.version;
                *out_is_ack = if info.is_ack { 1 } else { 0 };
            }
            0
        }
        None => -2,
    }
}

/// Parses photo payload to extract metadata, caption, and photo data offsets
#[no_mangle]
pub extern "C" fn prime_protocol_parse_photo_meta(
    payload: *const u8,
    payload_len: usize,
    out_msg_id: *mut c_char,
    out_msg_id_max: usize,
    out_caption: *mut c_char,
    out_caption_max: usize,
    out_is_collage: *mut i32,
    out_photo_offset: *mut usize,
    out_photo_len: *mut usize,
) -> i32 {
    if payload.is_null() || out_msg_id.is_null() || out_caption.is_null() || out_is_collage.is_null()
        || out_photo_offset.is_null() || out_photo_len.is_null()
    {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(payload, payload_len) };
    let meta = PrimeProtocol::parse_photo_meta(bytes);

    let c_id = CString::new(meta.msg_id).unwrap_or_default();
    let c_cap = CString::new(meta.caption).unwrap_or_default();

    if c_id.as_bytes_with_nul().len() > out_msg_id_max || c_cap.as_bytes_with_nul().len() > out_caption_max {
        return -3;
    }

    unsafe {
        std::ptr::copy_nonoverlapping(c_id.as_ptr(), out_msg_id, c_id.as_bytes_with_nul().len());
        std::ptr::copy_nonoverlapping(c_cap.as_ptr(), out_caption, c_cap.as_bytes_with_nul().len());
        *out_is_collage = if meta.is_collage { 1 } else { 0 };
        *out_photo_offset = meta.photo_offset;
        *out_photo_len = meta.photo_length;
    }
    0
}

/// Parses file payload header and offsets
#[no_mangle]
pub extern "C" fn prime_protocol_parse_file_header(
    payload: *const u8,
    payload_len: usize,
    out_msg_id: *mut c_char,
    out_msg_id_max: usize,
    out_file_name: *mut c_char,
    out_file_name_max: usize,
    out_duration: *mut c_char,
    out_duration_max: usize,
    out_is_voice: *mut i32,
    out_caption: *mut c_char,
    out_caption_max: usize,
    out_file_offset: *mut usize,
    out_file_len: *mut usize,
) -> i32 {
    if payload.is_null() || out_msg_id.is_null() || out_file_name.is_null() || out_duration.is_null()
        || out_is_voice.is_null() || out_caption.is_null() || out_file_offset.is_null() || out_file_len.is_null()
    {
        return -1;
    }
    let bytes = unsafe { slice::from_raw_parts(payload, payload_len) };
    match PrimeProtocol::parse_file_header(bytes) {
        Some(hdr) => {
            let c_id = CString::new(hdr.msg_id).unwrap_or_default();
            let c_name = CString::new(hdr.file_name).unwrap_or_default();
            let c_dur = CString::new(hdr.duration).unwrap_or_default();
            let c_cap = CString::new(hdr.caption).unwrap_or_default();

            if c_id.as_bytes_with_nul().len() > out_msg_id_max
                || c_name.as_bytes_with_nul().len() > out_file_name_max
                || c_dur.as_bytes_with_nul().len() > out_duration_max
                || c_cap.as_bytes_with_nul().len() > out_caption_max
            {
                return -3;
            }

            unsafe {
                std::ptr::copy_nonoverlapping(c_id.as_ptr(), out_msg_id, c_id.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_name.as_ptr(), out_file_name, c_name.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_dur.as_ptr(), out_duration, c_dur.as_bytes_with_nul().len());
                std::ptr::copy_nonoverlapping(c_cap.as_ptr(), out_caption, c_cap.as_bytes_with_nul().len());
                *out_is_voice = if hdr.is_voice { 1 } else { 0 };
                *out_file_offset = hdr.body_start_offset;
                *out_file_len = hdr.file_length;
            }
            0
        }
        None => -2,
    }
}
