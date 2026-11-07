use serde::{Deserialize, Serialize};

#[repr(u8)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum PacketType {
    Unknown = 0x00,
    Text = 0x01,
    Photo = 0x02,
    Ping = 0x03,
    Typing = 0x04,
    Edit = 0x05,
    Ack = 0x06,
    Avatar = 0x07,
    ChatDeleted = 0x08,
    Disconnect = 0x09,
    ReadReceipt = 0x0A,
    DeleteMessage = 0x0B,
    Presence = 0x0C,
    Reaction = 0x0D,
    File = 0x0E,
    ProfileUpdate = 0x0F,
    Voice = 0x10,
    KeepAlive = 0xFE,
}

impl From<u8> for PacketType {
    fn from(val: u8) -> Self {
        match val {
            0x01 => PacketType::Text,
            0x02 => PacketType::Photo,
            0x03 => PacketType::Ping,
            0x04 => PacketType::Typing,
            0x05 => PacketType::Edit,
            0x06 => PacketType::Ack,
            0x07 => PacketType::Avatar,
            0x08 => PacketType::ChatDeleted,
            0x09 => PacketType::Disconnect,
            0x0A => PacketType::ReadReceipt,
            0x0B => PacketType::DeleteMessage,
            0x0C => PacketType::Presence,
            0x0D => PacketType::Reaction,
            0x0E => PacketType::File,
            0x0F => PacketType::ProfileUpdate,
            0x10 => PacketType::Voice,
            0xFE => PacketType::KeepAlive,
            _ => PacketType::Unknown,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ReactionGroup {
    pub emoji: String,
    pub count: i32,
    pub is_selected_by_me: bool,
    pub users: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LinkPreviewData {
    pub url: String,
    pub domain: String,
    pub title: String,
    pub description: String,
    pub image_url: Option<String>,
    pub local_image_path: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ChatMessage {
    pub id: String,
    pub chat_id: String,
    pub sender: String,
    pub text: String,
    pub timestamp: i64,
    pub formatted_time: String,
    pub is_from_me: bool,
    pub packet_type: PacketType,
    pub reply_to_id: Option<String>,
    pub reply_to_sender: Option<String>,
    pub reply_to_text: Option<String>,
    pub file_name: Option<String>,
    pub file_size: Option<i64>,
    pub file_path: Option<String>,
    pub duration: Option<String>,
    pub is_voice: bool,
    pub is_photo: bool,
    pub is_video: bool,
    pub is_delivered: bool,
    pub is_read: bool,
    pub is_edited: bool,
    pub reactions: Vec<ReactionGroup>,
    pub link_preview: Option<LinkPreviewData>,
    pub is_first_in_chat: bool,
    pub is_last_in_chat: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ChatItem {
    pub id: String,
    pub display_name: String,
    pub username: String,
    pub avatar_path: Option<String>,
    pub initials: String,
    pub last_message: String,
    pub last_message_time: String,
    pub unread_count: i32,
    pub is_online: bool,
    pub status_text: String,
    pub is_muted: bool,
    pub peer_ip: Option<String>,
    pub peer_port: Option<u16>,
    pub device_type: String, // "android" | "pc" | "unknown"
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserSettings {
    pub my_display_name: String,
    pub my_username: String,
    pub avatar_path: Option<String>,
    pub theme_mode: String, // "dark" | "light" | "oled" | "cyberpunk" | "emerald"
    pub accent_color: String,
    pub chat_wallpaper_path: Option<String>,
    pub wallpaper_opacity: f32,
    pub sound_enabled: bool,
    pub sound_volume: i32,
    pub screenshot_protection: bool,
    pub avatar_corner_radius: i32,
    pub bubble_radius: i32,
    pub auto_download_media: bool,
    pub e2ee_enabled: bool,
    pub screen_watermark: bool,
    pub pin_code: Option<String>,
    pub auto_lock_minutes: i32,
    pub enable_acrylic: bool,
    pub enable_animations: bool,
    pub use_internal_browser: bool,
}

impl Default for UserSettings {
    fn default() -> Self {
        Self {
            my_display_name: "Пользователь".into(),
            my_username: "prime_user".into(),
            avatar_path: None,
            theme_mode: "dark".into(),
            accent_color: "#2EA043".into(),
            chat_wallpaper_path: None,
            wallpaper_opacity: 0.15,
            sound_enabled: true,
            sound_volume: 80,
            screenshot_protection: false,
            avatar_corner_radius: 14,
            bubble_radius: 16,
            auto_download_media: true,
            e2ee_enabled: true,
            screen_watermark: false,
            pin_code: None,
            auto_lock_minutes: 0,
            enable_acrylic: true,
            enable_animations: true,
            use_internal_browser: true,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BluetoothDeviceInfo {
    pub name: String,
    pub mac: String,
    pub opaque_id: String,
    pub is_connected: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RecentBrowserSite {
    pub id: i64,
    pub url: String,
    pub title: String,
    pub domain: String,
    pub timestamp: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BrowserBookmark {
    pub id: i64,
    pub url: String,
    pub title: String,
    pub icon: Option<String>,
    pub timestamp: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PeerDiscoveryInfo {
    pub ip: String,
    pub port: u16,
    pub name: String,
    pub username: String,
    pub device_type: String,
    pub opaque_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OutboxItem {
    pub id: String,
    pub peer_id: String,
    pub packet_type: PacketType,
    pub payload: Vec<u8>,
    pub retry_count: i32,
    pub created_at: i64,
}
