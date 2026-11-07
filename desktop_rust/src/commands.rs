use crate::models::{ChatItem, ChatMessage, PacketType, PeerDiscoveryInfo, UserSettings};
use crate::network::NetworkManager;
use crate::protocol::PrimePacket;
use crate::storage::Storage;
use std::sync::Arc;
use tauri::State;

pub struct AppState {
    pub storage: Storage,
    pub network: Arc<NetworkManager>,
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
        is_first_in_chat: false,
        is_last_in_chat: true,
    };

    state.storage.save_message(&msg).map_err(|e| e.to_string())?;

    // Send across network if peer is connected
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

    let ptype = if is_voice {
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
        packet_type: ptype,
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
        is_first_in_chat: false,
        is_last_in_chat: true,
    };

    state.storage.save_message(&msg).map_err(|e| e.to_string())?;

    let payload = PrimePacket::build_file_payload(&msg_id, &file_name, &file_bytes, &dur, is_voice, &caption);
    let packet = PrimePacket::new(ptype, payload);
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
    let settings = state.storage.get_settings().unwrap_or_default();
    let payload = PrimePacket::build_reaction_payload(&msg_id, emoji.as_deref(), &settings.my_username);
    let packet = PrimePacket::new(PacketType::Reaction, payload);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;
    Ok(())
}

#[tauri::command]
pub async fn delete_message(chat_id: String, msg_id: String, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.delete_message(&msg_id).map_err(|e| e.to_string())?;
    let payload = msg_id.as_bytes().to_vec();
    let packet = PrimePacket::new(PacketType::DeleteMessage, payload);
    let _ = state.network.send_raw_packet(&chat_id, &packet).await;
    Ok(())
}

#[tauri::command]
pub async fn delete_chat(chat_id: String, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.delete_chat(&chat_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn get_settings(state: State<'_, AppState>) -> Result<UserSettings, String> {
    state.storage.get_settings().map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn save_settings(settings: UserSettings, state: State<'_, AppState>) -> Result<(), String> {
    state.storage.save_settings(&settings).map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn scan_network() -> Result<Vec<PeerDiscoveryInfo>, String> {
    Ok(NetworkManager::scan_network().await)
}

#[tauri::command]
pub async fn connect_peer(peer_ip: String, port: u16, state: State<'_, AppState>) -> Result<(), String> {
    state.network.connect_to_peer(&peer_ip, port).await
}
