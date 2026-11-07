use crate::bluetooth::scan_bluetooth_devices as scan_bt;
use crate::link_preview::{extract_first_url, fetch_preview};
use crate::models::{
    BluetoothDeviceInfo, BrowserBookmark, ChatItem, ChatMessage, LinkPreviewData, PacketType,
    PeerDiscoveryInfo, RecentBrowserSite, UserSettings,
};
use crate::network::NetworkManager;
use crate::protocol::PrimePacket;
use crate::sound::{play_sound, set_sound_enabled, SoundEffect};
use crate::storage::Storage;
use std::path::PathBuf;
use std::sync::Arc;
use tauri::State;

fn decode_base64(input: &str) -> Result<Vec<u8>, String> {
    let clean = input.trim().split(',').last().unwrap_or(input);
    let mut bytes = Vec::new();
    let mut buf = 0u32;
    let mut bits = 0u32;
    for &b in clean.as_bytes() {
        let val = match b {
            b'A'..=b'Z' => (b - b'A') as u32,
            b'a'..=b'z' => (b - b'a' + 26) as u32,
            b'0'..=b'9' => (b - b'0' + 52) as u32,
            b'+' => 62,
            b'/' => 63,
            b'=' | b' ' | b'\n' | b'\r' => continue,
            _ => continue,
        };
        buf = (buf << 6) | val;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            bytes.push((buf >> bits) as u8);
        }
    }
    Ok(bytes)
}

pub struct AppState {
    pub storage: Storage,
    pub network: Arc<NetworkManager>,
    pub media_dir: PathBuf,
}

#[tauri::command]
pub async fn get_chats(state: State<'_, AppState>) -> Result<Vec<ChatItem>, String> {
    state.storage.get_chats().map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn get_messages(chat_id: String, state: State<'_, AppState>) -> Result<Vec<ChatMessage>, String> {
    state.storage.get_messages(&chat_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn send_message(
    chat_id: String,
    text: String,
    reply_to_id: Option<String>,
    reply_to_sender: Option<String>,
    reply_to_text: Option<String>,
    state: State<'_, AppState>,
) -> Result<ChatMessage, String> {
    let msg_id = uuid::Uuid::new_v4().to_string();
    let formatted_time = chrono::Local::now().format("%H:%M").to_string();
    let timestamp = chrono::Utc::now().timestamp_millis();

    // Check for OpenGraph link preview
    let link_preview = if let Some(url) = extract_first_url(&text) {
        let cache_dir = state.media_dir.join("LinkPreviews");
        fetch_preview(&url, &cache_dir).await
    } else {
        None
    };

    let msg = ChatMessage {
        id: msg_id.clone(),
        chat_id: chat_id.clone(),
        sender: "Me".into(),
        text: text.clone(),
        timestamp,
        formatted_time,
        is_from_me: true,
        packet_type: PacketType::Text,
        reply_to_id: reply_to_id.clone(),
        reply_to_sender: reply_to_sender.clone(),
        reply_to_text: reply_to_text.clone(),
        file_name: None,
        file_size: None,
        file_path: None,
        duration: None,
        is_voice: false,
        is_photo: false,
        is_video: false,
        is_delivered: true,
        is_read: false,
        is_edited: false,
        reactions: Vec::new(),
        link_preview,
        is_first_in_chat: false,
        is_last_in_chat: true,
    };

    state.storage.save_message(&msg).map_err(|e| e.to_string())?;
    play_sound(SoundEffect::Sent, 0.7);

    // Send across network if peer is connected (or queued to outbox)
    let payload = PrimePacket::build_text_payload(
        &msg_id,
        &text,
        reply_to_id.as_deref(),
        reply_to_sender.as_deref(),
        reply_to_text.as_deref(),
    );
    let packet = PrimePacket::new(PacketType::Text, payload);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;

    Ok(msg)
}

#[tauri::command]
pub async fn edit_message(
    chat_id: String,
    msg_id: String,
    new_text: String,
    state: State<'_, AppState>,
) -> Result<(), String> {
    state.storage.edit_message(&msg_id, &new_text).map_err(|e| e.to_string())?;

    let payload = PrimePacket::build_edit_payload(&msg_id, &new_text);
    let packet = PrimePacket::new(PacketType::Edit, payload);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;

    Ok(())
}

#[tauri::command]
pub async fn delete_message(
    chat_id: String,
    msg_id: String,
    for_everyone: bool,
    state: State<'_, AppState>,
) -> Result<(), String> {
    state.storage.delete_message(&msg_id).map_err(|e| e.to_string())?;

    if for_everyone {
        let payload = PrimePacket::build_delete_payload(&msg_id);
        let packet = PrimePacket::new(PacketType::DeleteMessage, payload);
        let _ = state.network.send_raw_packet(&chat_id, &packet).await;
    }

    Ok(())
}

#[tauri::command]
pub async fn delete_chat(chat_id: String, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.delete_chat(&chat_id).map_err(|e| e.to_string())?;

    let packet = PrimePacket::new(PacketType::ChatDeleted, vec![]);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;

    Ok(())
}

#[tauri::command]
pub async fn send_file(
    chat_id: String,
    file_path: String,
    caption: String,
    is_voice: bool,
    duration: Option<String>,
    state: State<'_, AppState>,
) -> Result<ChatMessage, String> {
    let path = std::path::Path::new(&file_path);
    let file_name = path.file_name().unwrap_or_default().to_string_lossy().to_string();
    let file_bytes = std::fs::read(&path).map_err(|e| e.to_string())?;
    let file_size = file_bytes.len() as i64;

    let msg_id = uuid::Uuid::new_v4().to_string();
    let formatted_time = chrono::Local::now().format("%H:%M").to_string();
    let timestamp = chrono::Utc::now().timestamp_millis();
    let dur = duration.unwrap_or_else(|| "00:00".into());

    let is_photo = file_name.ends_with(".jpg") || file_name.ends_with(".png") || file_name.ends_with(".webp");
    let is_video = file_name.ends_with(".mp4") || file_name.ends_with(".webm");

    let p_type = if is_voice {
        PacketType::Voice
    } else if is_photo {
        PacketType::Photo
    } else {
        PacketType::File
    };

    let msg = ChatMessage {
        id: msg_id.clone(),
        chat_id: chat_id.clone(),
        sender: "Me".into(),
        text: caption.clone(),
        timestamp,
        formatted_time,
        is_from_me: true,
        packet_type: p_type,
        reply_to_id: None,
        reply_to_sender: None,
        reply_to_text: None,
        file_name: Some(file_name.clone()),
        file_size: Some(file_size),
        file_path: Some(file_path),
        duration: Some(dur.clone()),
        is_voice,
        is_photo,
        is_video,
        is_delivered: true,
        is_read: false,
        is_edited: false,
        reactions: Vec::new(),
        link_preview: None,
        is_first_in_chat: false,
        is_last_in_chat: true,
    };

    state.storage.save_message(&msg).map_err(|e| e.to_string())?;
    play_sound(SoundEffect::Sent, 0.7);

    let payload = PrimePacket::build_file_payload(
        &msg_id,
        &file_name,
        &file_bytes,
        &dur,
        is_voice,
        &caption,
    );
    let packet = PrimePacket::new(p_type, payload);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;

    Ok(msg)
}

#[tauri::command]
pub async fn send_reaction(
    chat_id: String,
    msg_id: String,
    emoji: Option<String>,
    state: State<'_, AppState>,
) -> Result<(), String> {
    state.storage.apply_reaction(&msg_id, emoji.clone(), "Me").map_err(|e| e.to_string())?;
    play_sound(SoundEffect::Pop, 0.6);

    let payload = PrimePacket::build_reaction_payload(&msg_id, emoji.as_deref(), "Me");
    let packet = PrimePacket::new(PacketType::Reaction, payload);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;

    Ok(())
}

#[tauri::command]
pub async fn send_typing(chat_id: String, state: State<'_, AppState>) -> Result<(), String> {
    let packet = PrimePacket::new(PacketType::Typing, vec![]);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;
    Ok(())
}

#[tauri::command]
pub async fn mark_chat_read(chat_id: String, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.mark_messages_read(&chat_id).map_err(|e| e.to_string())?;
    let packet = PrimePacket::new(PacketType::ReadReceipt, PrimePacket::build_read_receipt_payload(&chat_id));
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;
    Ok(())
}

#[tauri::command]
pub async fn get_settings(state: State<'_, AppState>) -> Result<UserSettings, String> {
    state.storage.get_settings().map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn save_settings(settings: UserSettings, state: State<'_, AppState>) -> Result<(), String> {
    set_sound_enabled(settings.sound_enabled);
    state.storage.save_settings(&settings).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn scan_network() -> Result<Vec<PeerDiscoveryInfo>, String> {
    Ok(NetworkManager::scan_network().await)
}

#[tauri::command]
pub async fn connect_peer(peer_ip: String, port: u16, state: State<'_, AppState>) -> Result<String, String> {
    state.network.connect_to_peer(&peer_ip, port).await
}

#[tauri::command]
pub fn play_sound_effect(effect: String, volume: Option<f64>) -> Result<(), String> {
    let vol = volume.unwrap_or(0.8);
    match effect.as_str() {
        "sent" => play_sound(SoundEffect::Sent, vol),
        "received" => play_sound(SoundEffect::Received, vol),
        "record_start" => play_sound(SoundEffect::RecordStart, vol),
        "record_stop" => play_sound(SoundEffect::RecordStop, vol),
        "pop" => play_sound(SoundEffect::Pop, vol),
        _ => {}
    }
    Ok(())
}

#[tauri::command]
pub async fn fetch_link_preview(url: String, state: State<'_, AppState>) -> Result<Option<LinkPreviewData>, String> {
    let cache_dir = state.media_dir.join("LinkPreviews");
    Ok(fetch_preview(&url, &cache_dir).await)
}

#[tauri::command]
pub fn set_screenshot_protection(enabled: bool) -> Result<bool, String> {
    #[cfg(windows)]
    {
        extern "system" {
            fn GetForegroundWindow() -> *mut std::ffi::c_void;
            fn SetWindowDisplayAffinity(hwnd: *mut std::ffi::c_void, affinity: u32) -> i32;
        }
        unsafe {
            let hwnd = GetForegroundWindow();
            if !hwnd.is_null() {
                let affinity = if enabled { 1 } else { 0 }; // WDA_MONITOR = 1, WDA_NONE = 0
                return Ok(SetWindowDisplayAffinity(hwnd, affinity) != 0);
            }
        }
    }
    let _ = enabled;
    Ok(false)
}

#[tauri::command]
pub async fn scan_bluetooth_devices() -> Result<Vec<BluetoothDeviceInfo>, String> {
    Ok(scan_bt())
}

#[tauri::command]
pub async fn send_voice_recording(
    chat_id: String,
    base64_audio: String,
    duration: String,
    state: State<'_, AppState>,
) -> Result<ChatMessage, String> {
    let bytes = decode_base64(&base64_audio)?;
    let file_id = uuid::Uuid::new_v4().to_string();
    let file_name = format!("voice_{}.wav", file_id);
    let file_path = state.media_dir.join(&file_name);

    std::fs::write(&file_path, &bytes).map_err(|e| format!("Failed to save voice note: {}", e))?;

    send_file(
        chat_id,
        file_path.to_string_lossy().to_string(),
        "".into(),
        true,
        Some(duration),
        state,
    )
    .await
}

#[tauri::command]
pub async fn send_edited_photo(
    chat_id: String,
    base64_image: String,
    caption: String,
    state: State<'_, AppState>,
) -> Result<ChatMessage, String> {
    let bytes = decode_base64(&base64_image)?;
    let file_id = uuid::Uuid::new_v4().to_string();
    let file_name = format!("photo_{}.png", file_id);
    let file_path = state.media_dir.join(&file_name);

    std::fs::write(&file_path, &bytes).map_err(|e| format!("Failed to save edited photo: {}", e))?;

    send_file(
        chat_id,
        file_path.to_string_lossy().to_string(),
        caption,
        false,
        None,
        state,
    )
    .await
}

#[tauri::command]
pub async fn get_browser_history(state: State<'_, AppState>) -> Result<Vec<RecentBrowserSite>, String> {
    state.storage.get_browser_history().map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn add_browser_history(url: String, title: String, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.add_browser_history(&url, &title).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn clear_browser_history(state: State<'_, AppState>) -> Result<(), String> {
    state.storage.clear_browser_history().map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn get_bookmarks(state: State<'_, AppState>) -> Result<Vec<BrowserBookmark>, String> {
    state.storage.get_bookmarks().map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn add_bookmark(url: String, title: String, icon: Option<String>, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.add_bookmark(&url, &title, icon.as_deref()).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn delete_bookmark(id: i64, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.delete_bookmark(id).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn verify_pin(pin: String, state: State<'_, AppState>) -> Result<bool, String> {
    let settings = state.storage.get_settings().map_err(|e| e.to_string())?;
    if let Some(stored) = settings.pin_code {
        if stored.trim().is_empty() {
            return Ok(true);
        }
        return Ok(stored == pin);
    }
    Ok(true)
}

#[tauri::command]
pub async fn set_pin(pin: Option<String>, state: State<'_, AppState>) -> Result<(), String> {
    let mut settings = state.storage.get_settings().map_err(|e| e.to_string())?;
    settings.pin_code = pin;
    state.storage.save_settings(&settings).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn pick_screen_color() -> Result<String, String> {
    #[cfg(windows)]
    {
        #[repr(C)]
        struct POINT {
            x: i32,
            y: i32,
        }

        extern "system" {
            fn GetCursorPos(pt: *mut POINT) -> i32;
            fn GetDC(hwnd: *mut std::ffi::c_void) -> *mut std::ffi::c_void;
            fn ReleaseDC(hwnd: *mut std::ffi::c_void, hdc: *mut std::ffi::c_void) -> i32;
            fn GetPixel(hdc: *mut std::ffi::c_void, x: i32, y: i32) -> u32;
        }

        unsafe {
            let mut pt = POINT { x: 0, y: 0 };
            if GetCursorPos(&mut pt) != 0 {
                let hdc = GetDC(std::ptr::null_mut());
                if !hdc.is_null() {
                    let pixel = GetPixel(hdc, pt.x, pt.y);
                    ReleaseDC(std::ptr::null_mut(), hdc);
                    let r = (pixel & 0xFF) as u8;
                    let g = ((pixel >> 8) & 0xFF) as u8;
                    let b = ((pixel >> 16) & 0xFF) as u8;
                    return Ok(format!("#{:02X}{:02X}{:02X}", r, g, b));
                }
            }
        }
    }
    Ok("#4D9FFF".to_string())
}
