use serde::{Deserialize, Serialize};

#[repr(u8)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum PacketType {
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
    Unknown = 0x00,
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
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserSettings {
    pub my_display_name: String,
    pub my_username: String,
    pub avatar_path: Option<String>,
    pub theme_mode: String, // "dark" | "light"
    pub chat_wallpaper_path: Option<String>,
    pub wallpaper_opacity: f32,
    pub sound_enabled: bool,
    pub screenshot_protection: bool,
    pub avatar_corner_radius: i32,
    pub auto_download_media: bool,
}

impl Default for UserSettings {
    fn default() -> Self {
        Self {
            my_display_name: "Пользователь".into(),
            my_username: "user".into(),
            avatar_path: None,
            theme_mode: "dark".into(),
            chat_wallpaper_path: None,
            wallpaper_opacity: 0.15,
            sound_enabled: true,
            screenshot_protection: false,
            avatar_corner_radius: 14,
            auto_download_media: true,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PeerDiscoveryInfo {
    pub ip: String,
    pub port: u16,
    pub name: String,
    pub username: String,
    pub device_type: String,
}
