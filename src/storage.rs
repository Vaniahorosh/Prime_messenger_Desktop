use crate::models::{ChatItem, ChatMessage, PacketType, UserSettings};
use parking_lot::Mutex;
use rusqlite::{params, Connection, Result};
use std::path::PathBuf;
use std::sync::Arc;

#[derive(Clone)]
pub struct Storage {
    conn: Arc<Mutex<Connection>>,
}

impl Storage {
    pub fn new(db_path: PathBuf) -> Result<Self> {
        if let Some(parent) = db_path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }

        let conn = Connection::open(db_path)?;
        conn.execute_batch(
            "
            CREATE TABLE IF NOT EXISTS chats (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                username TEXT NOT NULL,
                avatar_path TEXT,
                initials TEXT NOT NULL,
                last_message TEXT NOT NULL,
                last_message_time TEXT NOT NULL,
                unread_count INTEGER NOT NULL DEFAULT 0,
                is_online INTEGER NOT NULL DEFAULT 0,
                status_text TEXT NOT NULL DEFAULT '',
                is_muted INTEGER NOT NULL DEFAULT 0,
                peer_ip TEXT,
                peer_port INTEGER
            );

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                chat_id TEXT NOT NULL,
                sender TEXT NOT NULL,
                text TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                formatted_time TEXT NOT NULL,
                is_from_me INTEGER NOT NULL,
                packet_type INTEGER NOT NULL,
                reply_to_id TEXT,
                reply_to_sender TEXT,
                reply_to_text TEXT,
                file_name TEXT,
                file_size INTEGER,
                file_path TEXT,
                duration TEXT,
                is_voice INTEGER NOT NULL DEFAULT 0,
                is_photo INTEGER NOT NULL DEFAULT 0,
                is_video INTEGER NOT NULL DEFAULT 0,
                is_delivered INTEGER NOT NULL DEFAULT 1,
                is_read INTEGER NOT NULL DEFAULT 0,
                is_edited INTEGER NOT NULL DEFAULT 0,
                reactions_json TEXT NOT NULL DEFAULT '[]'
            );

            CREATE INDEX IF NOT EXISTS idx_messages_chat ON messages(chat_id, timestamp);

            CREATE TABLE IF NOT EXISTS settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                my_display_name TEXT NOT NULL,
                my_username TEXT NOT NULL,
                avatar_path TEXT,
                theme_mode TEXT NOT NULL,
                chat_wallpaper_path TEXT,
                wallpaper_opacity REAL NOT NULL,
                sound_enabled INTEGER NOT NULL,
                screenshot_protection INTEGER NOT NULL,
                avatar_corner_radius INTEGER NOT NULL,
                auto_download_media INTEGER NOT NULL
            );
            ",
        )?;

        Ok(Self {
            conn: Arc::new(Mutex::new(conn)),
        })
    }

    pub fn get_chats(&self) -> Result<Vec<ChatItem>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, display_name, username, avatar_path, initials, last_message,
                    last_message_time, unread_count, is_online, status_text, is_muted, peer_ip, peer_port
             FROM chats ORDER BY last_message_time DESC",
        )?;

        let rows = stmt.query_map([], |row| {
            Ok(ChatItem {
                id: row.get(0)?,
                display_name: row.get(1)?,
                username: row.get(2)?,
                avatar_path: row.get(3)?,
                initials: row.get(4)?,
                last_message: row.get(5)?,
                last_message_time: row.get(6)?,
                unread_count: row.get(7)?,
                is_online: row.get::<_, i32>(8)? == 1,
                status_text: row.get(9)?,
                is_muted: row.get::<_, i32>(10)? == 1,
                peer_ip: row.get(11)?,
                peer_port: row.get::<_, Option<u16>>(12)?,
            })
        })?;

        let mut list = Vec::new();
        for r in rows {
            list.push(r?);
        }
        Ok(list)
    }

    pub fn upsert_chat(&self, chat: &ChatItem) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "INSERT INTO chats (id, display_name, username, avatar_path, initials, last_message,
                                last_message_time, unread_count, is_online, status_text, is_muted, peer_ip, peer_port)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)
             ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                username = excluded.username,
                avatar_path = excluded.avatar_path,
                initials = excluded.initials,
                last_message = excluded.last_message,
                last_message_time = excluded.last_message_time,
                unread_count = excluded.unread_count,
                is_online = excluded.is_online,
                status_text = excluded.status_text,
                is_muted = excluded.is_muted,
                peer_ip = excluded.peer_ip,
                peer_port = excluded.peer_port",
            params![
                chat.id,
                chat.display_name,
                chat.username,
                chat.avatar_path,
                chat.initials,
                chat.last_message,
                chat.last_message_time,
                chat.unread_count,
                if chat.is_online { 1 } else { 0 },
                chat.status_text,
                if chat.is_muted { 1 } else { 0 },
                chat.peer_ip,
                chat.peer_port,
            ],
        )?;
        Ok(())
    }

    pub fn delete_chat(&self, chat_id: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM chats WHERE id = ?1", params![chat_id])?;
        conn.execute("DELETE FROM messages WHERE chat_id = ?1", params![chat_id])?;
        Ok(())
    }

    pub fn get_messages(&self, chat_id: &str) -> Result<Vec<ChatMessage>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, chat_id, sender, text, timestamp, formatted_time, is_from_me,
                    packet_type, reply_to_id, reply_to_sender, reply_to_text,
                    file_name, file_size, file_path, duration, is_voice, is_photo, is_video,
                    is_delivered, is_read, is_edited, reactions_json
             FROM messages WHERE chat_id = ?1 ORDER BY timestamp ASC",
        )?;

        let rows = stmt.query_map(params![chat_id], |row| {
            let rx_json: String = row.get(21)?;
            let reactions = serde_json::from_str(&rx_json).unwrap_or_default();
            let pt_num: u8 = row.get(7)?;

            Ok(ChatMessage {
                id: row.get(0)?,
                chat_id: row.get(1)?,
                sender: row.get(2)?,
                text: row.get(3)?,
                timestamp: row.get(4)?,
                formatted_time: row.get(5)?,
                is_from_me: row.get::<_, i32>(6)? == 1,
                packet_type: PacketType::from(pt_num),
                reply_to_id: row.get(8)?,
                reply_to_sender: row.get(9)?,
                reply_to_text: row.get(10)?,
                file_name: row.get(11)?,
                file_size: row.get(12)?,
                file_path: row.get(13)?,
                duration: row.get(14)?,
                is_voice: row.get::<_, i32>(15)? == 1,
                is_photo: row.get::<_, i32>(16)? == 1,
                is_video: row.get::<_, i32>(17)? == 1,
                is_delivered: row.get::<_, i32>(18)? == 1,
                is_read: row.get::<_, i32>(19)? == 1,
                is_edited: row.get::<_, i32>(20)? == 1,
                reactions,
                is_first_in_chat: false,
                is_last_in_chat: false,
            })
        })?;

        let mut list: Vec<ChatMessage> = Vec::new();
        for r in rows {
            list.push(r?);
        }

        // Compute top/bottom spacers dynamically
        if let Some(first) = list.first_mut() {
            first.is_first_in_chat = true;
        }
        if let Some(last) = list.last_mut() {
            last.is_last_in_chat = true;
        }

        Ok(list)
    }

    pub fn save_message(&self, msg: &ChatMessage) -> Result<()> {
        let conn = self.conn.lock();
        let reactions_json = serde_json::to_string(&msg.reactions).unwrap_or_else(|_| "[]".into());

        conn.execute(
            "INSERT INTO messages (id, chat_id, sender, text, timestamp, formatted_time,
                                  is_from_me, packet_type, reply_to_id, reply_to_sender, reply_to_text,
                                  file_name, file_size, file_path, duration, is_voice, is_photo, is_video,
                                  is_delivered, is_read, is_edited, reactions_json)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22)
             ON CONFLICT(id) DO UPDATE SET
                text = excluded.text,
                is_edited = excluded.is_edited,
                is_read = excluded.is_read,
                reactions_json = excluded.reactions_json",
            params![
                msg.id,
                msg.chat_id,
                msg.sender,
                msg.text,
                msg.timestamp,
                msg.formatted_time,
                if msg.is_from_me { 1 } else { 0 },
                msg.packet_type as u8,
                msg.reply_to_id,
                msg.reply_to_sender,
                msg.reply_to_text,
                msg.file_name,
                msg.file_size,
                msg.file_path,
                msg.duration,
                if msg.is_voice { 1 } else { 0 },
                if msg.is_photo { 1 } else { 0 },
                if msg.is_video { 1 } else { 0 },
                if msg.is_delivered { 1 } else { 0 },
                if msg.is_read { 1 } else { 0 },
                if msg.is_edited { 1 } else { 0 },
                reactions_json,
            ],
        )?;

        // Update chat last message
        conn.execute(
            "UPDATE chats SET last_message = ?1, last_message_time = ?2 WHERE id = ?3",
            params![msg.text, msg.formatted_time, msg.chat_id],
        )?;

        Ok(())
    }

    pub fn delete_message(&self, msg_id: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM messages WHERE id = ?1", params![msg_id])?;
        Ok(())
    }

    pub fn get_settings(&self) -> Result<UserSettings> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT my_display_name, my_username, avatar_path, theme_mode,
                    chat_wallpaper_path, wallpaper_opacity, sound_enabled,
                    screenshot_protection, avatar_corner_radius, auto_download_media
             FROM settings WHERE id = 1",
        )?;

        let mut rows = stmt.query([])?;
        if let Some(row) = rows.next()? {
            Ok(UserSettings {
                my_display_name: row.get(0)?,
                my_username: row.get(1)?,
                avatar_path: row.get(2)?,
                theme_mode: row.get(3)?,
                chat_wallpaper_path: row.get(4)?,
                wallpaper_opacity: row.get(5)?,
                sound_enabled: row.get::<_, i32>(6)? == 1,
                screenshot_protection: row.get::<_, i32>(7)? == 1,
                avatar_corner_radius: row.get(8)?,
                auto_download_media: row.get::<_, i32>(9)? == 1,
            })
        } else {
            let def = UserSettings::default();
            self.save_settings_inner(&conn, &def)?;
            Ok(def)
        }
    }

    fn save_settings_inner(&self, conn: &Connection, s: &UserSettings) -> Result<()> {
        conn.execute(
            "INSERT INTO settings (id, my_display_name, my_username, avatar_path, theme_mode,
                                   chat_wallpaper_path, wallpaper_opacity, sound_enabled,
                                   screenshot_protection, avatar_corner_radius, auto_download_media)
             VALUES (1, ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)
             ON CONFLICT(id) DO UPDATE SET
                my_display_name = excluded.my_display_name,
                my_username = excluded.my_username,
                avatar_path = excluded.avatar_path,
                theme_mode = excluded.theme_mode,
                chat_wallpaper_path = excluded.chat_wallpaper_path,
                wallpaper_opacity = excluded.wallpaper_opacity,
                sound_enabled = excluded.sound_enabled,
                screenshot_protection = excluded.screenshot_protection,
                avatar_corner_radius = excluded.avatar_corner_radius,
                auto_download_media = excluded.auto_download_media",
            params![
                s.my_display_name,
                s.my_username,
                s.avatar_path,
                s.theme_mode,
                s.chat_wallpaper_path,
                s.wallpaper_opacity,
                if s.sound_enabled { 1 } else { 0 },
                if s.screenshot_protection { 1 } else { 0 },
                s.avatar_corner_radius,
                if s.auto_download_media { 1 } else { 0 },
            ],
        )?;
        Ok(())
    }

    pub fn save_settings(&self, s: &UserSettings) -> Result<()> {
        let conn = self.conn.lock();
        self.save_settings_inner(&conn, s)
    }
}
