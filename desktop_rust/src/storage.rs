use crate::models::{
    BrowserBookmark, ChatItem, ChatMessage, LinkPreviewData, OutboxItem, PacketType,
    ReactionGroup, RecentBrowserSite, UserSettings,
};
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
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

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
                peer_port INTEGER,
                device_type TEXT NOT NULL DEFAULT 'unknown'
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
                reactions_json TEXT NOT NULL DEFAULT '[]',
                link_preview_json TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_messages_chat ON messages(chat_id, timestamp);

            CREATE TABLE IF NOT EXISTS settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                my_display_name TEXT NOT NULL,
                my_username TEXT NOT NULL,
                avatar_path TEXT,
                theme_mode TEXT NOT NULL,
                accent_color TEXT NOT NULL DEFAULT '#2EA043',
                chat_wallpaper_path TEXT,
                wallpaper_opacity REAL NOT NULL,
                sound_enabled INTEGER NOT NULL,
                sound_volume INTEGER NOT NULL DEFAULT 80,
                screenshot_protection INTEGER NOT NULL,
                avatar_corner_radius INTEGER NOT NULL,
                auto_download_media INTEGER NOT NULL,
                e2ee_enabled INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS outbox (
                id TEXT PRIMARY KEY,
                peer_id TEXT NOT NULL,
                packet_type INTEGER NOT NULL,
                payload BLOB NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS link_previews (
                url TEXT PRIMARY KEY,
                domain TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                image_url TEXT,
                local_image_path TEXT
            );

            CREATE TABLE IF NOT EXISTS browser_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                domain TEXT NOT NULL,
                timestamp INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS browser_bookmarks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                title TEXT NOT NULL,
                icon TEXT,
                timestamp INTEGER NOT NULL
            );
            ",
        )?;

        // Ensure newly added columns exist in settings table if migrating from previous schema
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN bubble_radius INTEGER NOT NULL DEFAULT 16", []);
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN screen_watermark INTEGER NOT NULL DEFAULT 0", []);
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN pin_code TEXT", []);
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN auto_lock_minutes INTEGER NOT NULL DEFAULT 0", []);
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN enable_acrylic INTEGER NOT NULL DEFAULT 1", []);
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN enable_animations INTEGER NOT NULL DEFAULT 1", []);
        let _ = conn.execute("ALTER TABLE settings ADD COLUMN use_internal_browser INTEGER NOT NULL DEFAULT 1", []);

        Ok(Self {
            conn: Arc::new(Mutex::new(conn)),
        })
    }

    // ==========================================
    // Chats Management
    // ==========================================

    pub fn get_chats(&self) -> Result<Vec<ChatItem>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, display_name, username, avatar_path, initials, last_message,
                    last_message_time, unread_count, is_online, status_text, is_muted,
                    peer_ip, peer_port, device_type
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
                is_online: row.get::<_, i32>(8)? != 0,
                status_text: row.get(9)?,
                is_muted: row.get::<_, i32>(10)? != 0,
                peer_ip: row.get(11)?,
                peer_port: row.get(12)?,
                device_type: row.get(13)?,
            })
        })?;

        let mut chats = Vec::new();
        for r in rows {
            chats.push(r?);
        }
        Ok(chats)
    }

    pub fn upsert_chat(&self, chat: &ChatItem) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "INSERT INTO chats (id, display_name, username, avatar_path, initials, last_message,
                                last_message_time, unread_count, is_online, status_text, is_muted,
                                peer_ip, peer_port, device_type)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14)
             ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                username = excluded.username,
                avatar_path = COALESCE(excluded.avatar_path, chats.avatar_path),
                initials = excluded.initials,
                last_message = excluded.last_message,
                last_message_time = excluded.last_message_time,
                is_online = excluded.is_online,
                status_text = excluded.status_text,
                peer_ip = COALESCE(excluded.peer_ip, chats.peer_ip),
                peer_port = COALESCE(excluded.peer_port, chats.peer_port),
                device_type = excluded.device_type",
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
                chat.device_type,
            ],
        )?;
        Ok(())
    }

    pub fn update_chat_status(&self, chat_id: &str, is_online: bool, status_text: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "UPDATE chats SET is_online = ?1, status_text = ?2 WHERE id = ?3",
            params![if is_online { 1 } else { 0 }, status_text, chat_id],
        )?;
        Ok(())
    }

    pub fn delete_chat(&self, chat_id: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM messages WHERE chat_id = ?1", params![chat_id])?;
        conn.execute("DELETE FROM chats WHERE id = ?1", params![chat_id])?;
        Ok(())
    }

    // ==========================================
    // Messages Management
    // ==========================================

    pub fn get_messages(&self, chat_id: &str) -> Result<Vec<ChatMessage>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, chat_id, sender, text, timestamp, formatted_time, is_from_me,
                    packet_type, reply_to_id, reply_to_sender, reply_to_text,
                    file_name, file_size, file_path, duration, is_voice, is_photo, is_video,
                    is_delivered, is_read, is_edited, reactions_json, link_preview_json
             FROM messages WHERE chat_id = ?1 ORDER BY timestamp ASC",
        )?;

        let rows = stmt.query_map(params![chat_id], |row| {
            let rx_json: String = row.get(21)?;
            let reactions: Vec<ReactionGroup> = serde_json::from_str(&rx_json).unwrap_or_default();
            let preview_json: Option<String> = row.get(22)?;
            let link_preview: Option<LinkPreviewData> = preview_json.and_then(|j| serde_json::from_str(&j).ok());

            Ok(ChatMessage {
                id: row.get(0)?,
                chat_id: row.get(1)?,
                sender: row.get(2)?,
                text: row.get(3)?,
                timestamp: row.get(4)?,
                formatted_time: row.get(5)?,
                is_from_me: row.get::<_, i32>(6)? != 0,
                packet_type: PacketType::from(row.get::<_, u8>(7)?),
                reply_to_id: row.get(8)?,
                reply_to_sender: row.get(9)?,
                reply_to_text: row.get(10)?,
                file_name: row.get(11)?,
                file_size: row.get(12)?,
                file_path: row.get(13)?,
                duration: row.get(14)?,
                is_voice: row.get::<_, i32>(15)? != 0,
                is_photo: row.get::<_, i32>(16)? != 0,
                is_video: row.get::<_, i32>(17)? != 0,
                is_delivered: row.get::<_, i32>(18)? != 0,
                is_read: row.get::<_, i32>(19)? != 0,
                is_edited: row.get::<_, i32>(20)? != 0,
                reactions,
                link_preview,
                is_first_in_chat: false,
                is_last_in_chat: false,
            })
        })?;

        let mut messages = Vec::new();
        for r in rows {
            messages.push(r?);
        }

        if let Some(first) = messages.first_mut() {
            first.is_first_in_chat = true;
        }
        if let Some(last) = messages.last_mut() {
            last.is_last_in_chat = true;
        }

        Ok(messages)
    }

    pub fn save_message(&self, msg: &ChatMessage) -> Result<()> {
        let conn = self.conn.lock();
        let reactions_json = serde_json::to_string(&msg.reactions).unwrap_or_else(|_| "[]".into());
        let preview_json = msg.link_preview.as_ref().and_then(|lp| serde_json::to_string(lp).ok());

        conn.execute(
            "INSERT INTO messages (id, chat_id, sender, text, timestamp, formatted_time,
                                   is_from_me, packet_type, reply_to_id, reply_to_sender, reply_to_text,
                                   file_name, file_size, file_path, duration, is_voice, is_photo, is_video,
                                   is_delivered, is_read, is_edited, reactions_json, link_preview_json)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22, ?23)
             ON CONFLICT(id) DO UPDATE SET
                text = excluded.text,
                is_delivered = excluded.is_delivered,
                is_read = excluded.is_read,
                is_edited = excluded.is_edited,
                reactions_json = excluded.reactions_json,
                link_preview_json = excluded.link_preview_json",
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
                preview_json,
            ],
        )?;

        // Update last message in chat
        let preview_text = if !msg.text.is_empty() {
            msg.text.clone()
        } else if msg.is_voice {
            "Голосовое сообщение".to_string()
        } else if msg.is_photo {
            "Фотография".to_string()
        } else if let Some(ref fn_str) = msg.file_name {
            fn_str.clone()
        } else {
            "Сообщение".to_string()
        };

        conn.execute(
            "UPDATE chats SET last_message = ?1, last_message_time = ?2 WHERE id = ?3",
            params![preview_text, msg.formatted_time, msg.chat_id],
        )?;

        Ok(())
    }

    pub fn edit_message(&self, msg_id: &str, new_text: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "UPDATE messages SET text = ?1, is_edited = 1 WHERE id = ?2",
            params![new_text, msg_id],
        )?;
        Ok(())
    }

    pub fn delete_message(&self, msg_id: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM messages WHERE id = ?1", params![msg_id])?;
        Ok(())
    }

    pub fn mark_messages_read(&self, chat_id: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "UPDATE messages SET is_read = 1 WHERE chat_id = ?1 AND is_from_me = 0",
            params![chat_id],
        )?;
        conn.execute(
            "UPDATE chats SET unread_count = 0 WHERE id = ?1",
            params![chat_id],
        )?;
        Ok(())
    }

    // ==========================================
    // Reactions Management
    // ==========================================

    pub fn apply_reaction(&self, msg_id: &str, emoji_opt: Option<String>, author: &str) -> Result<()> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare("SELECT reactions_json FROM messages WHERE id = ?1")?;
        let json_opt: Option<String> = stmt.query_row(params![msg_id], |r| r.get(0)).ok();

        let mut groups: Vec<ReactionGroup> = json_opt
            .and_then(|j| serde_json::from_str(&j).ok())
            .unwrap_or_default();

        // Remove any previous reaction from this author
        for g in groups.iter_mut() {
            if let Some(pos) = g.users.iter().position(|u| u == author) {
                g.users.remove(pos);
                g.count -= 1;
            }
        }
        groups.retain(|g| g.count > 0);

        // Add new reaction if not None
        if let Some(emoji) = emoji_opt {
            let my_login = "Me";
            let is_me = author == my_login;

            if let Some(existing) = groups.iter_mut().find(|g| g.emoji == emoji) {
                existing.users.push(author.to_string());
                existing.count += 1;
                if is_me {
                    existing.is_selected_by_me = true;
                }
            } else {
                groups.push(ReactionGroup {
                    emoji,
                    count: 1,
                    is_selected_by_me: is_me,
                    users: vec![author.to_string()],
                });
            }
        }

        let new_json = serde_json::to_string(&groups).unwrap_or_else(|_| "[]".into());
        conn.execute(
            "UPDATE messages SET reactions_json = ?1 WHERE id = ?2",
            params![new_json, msg_id],
        )?;
        Ok(())
    }

    // ==========================================
    // Outbox & Offline Resending Queue
    // ==========================================

    pub fn queue_outbox(&self, item: &OutboxItem) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "INSERT INTO outbox (id, peer_id, packet_type, payload, retry_count, created_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)
             ON CONFLICT(id) DO UPDATE SET retry_count = outbox.retry_count + 1",
            params![
                item.id,
                item.peer_id,
                item.packet_type as u8,
                item.payload,
                item.retry_count,
                item.created_at,
            ],
        )?;
        Ok(())
    }

    pub fn get_outbox_for_peer(&self, peer_id: &str) -> Result<Vec<OutboxItem>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, peer_id, packet_type, payload, retry_count, created_at
             FROM outbox WHERE peer_id = ?1 ORDER BY created_at ASC",
        )?;

        let rows = stmt.query_map(params![peer_id], |row| {
            Ok(OutboxItem {
                id: row.get(0)?,
                peer_id: row.get(1)?,
                packet_type: PacketType::from(row.get::<_, u8>(2)?),
                payload: row.get(3)?,
                retry_count: row.get(4)?,
                created_at: row.get(5)?,
            })
        })?;

        let mut items = Vec::new();
        for r in rows {
            items.push(r?);
        }
        Ok(items)
    }

    pub fn delete_outbox_item(&self, item_id: &str) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM outbox WHERE id = ?1", params![item_id])?;
        Ok(())
    }

    // ==========================================
    // Settings Management
    // ==========================================

    pub fn get_settings(&self) -> Result<UserSettings> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT my_display_name, my_username, avatar_path, theme_mode, accent_color,
                    chat_wallpaper_path, wallpaper_opacity, sound_enabled, sound_volume,
                    screenshot_protection, avatar_corner_radius, bubble_radius, auto_download_media, e2ee_enabled,
                    screen_watermark, pin_code, auto_lock_minutes, enable_acrylic, enable_animations, use_internal_browser
             FROM settings WHERE id = 1",
        )?;

        let res = stmt.query_row([], |row| {
            Ok(UserSettings {
                my_display_name: row.get(0)?,
                my_username: row.get(1)?,
                avatar_path: row.get(2)?,
                theme_mode: row.get(3)?,
                accent_color: row.get(4)?,
                chat_wallpaper_path: row.get(5)?,
                wallpaper_opacity: row.get(6)?,
                sound_enabled: row.get::<_, i32>(7)? != 0,
                sound_volume: row.get(8)?,
                screenshot_protection: row.get::<_, i32>(9)? != 0,
                avatar_corner_radius: row.get(10)?,
                bubble_radius: row.get(11)?,
                auto_download_media: row.get::<_, i32>(12)? != 0,
                e2ee_enabled: row.get::<_, i32>(13)? != 0,
                screen_watermark: row.get::<_, i32>(14)? != 0,
                pin_code: row.get(15)?,
                auto_lock_minutes: row.get(16)?,
                enable_acrylic: row.get::<_, i32>(17)? != 0,
                enable_animations: row.get::<_, i32>(18)? != 0,
                use_internal_browser: row.get::<_, i32>(19)? != 0,
            })
        });

        match res {
            Ok(s) => Ok(s),
            Err(_) => {
                let default = UserSettings::default();
                drop(stmt);
                conn.execute(
                    "INSERT INTO settings (id, my_display_name, my_username, avatar_path, theme_mode,
                                          accent_color, chat_wallpaper_path, wallpaper_opacity, sound_enabled,
                                          sound_volume, screenshot_protection, avatar_corner_radius, bubble_radius,
                                          auto_download_media, e2ee_enabled, screen_watermark, pin_code, auto_lock_minutes,
                                          enable_acrylic, enable_animations, use_internal_browser)
                     VALUES (1, ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20)",
                    params![
                        default.my_display_name,
                        default.my_username,
                        default.avatar_path,
                        default.theme_mode,
                        default.accent_color,
                        default.chat_wallpaper_path,
                        default.wallpaper_opacity,
                        if default.sound_enabled { 1 } else { 0 },
                        default.sound_volume,
                        if default.screenshot_protection { 1 } else { 0 },
                        default.avatar_corner_radius,
                        default.bubble_radius,
                        if default.auto_download_media { 1 } else { 0 },
                        if default.e2ee_enabled { 1 } else { 0 },
                        if default.screen_watermark { 1 } else { 0 },
                        default.pin_code,
                        default.auto_lock_minutes,
                        if default.enable_acrylic { 1 } else { 0 },
                        if default.enable_animations { 1 } else { 0 },
                        if default.use_internal_browser { 1 } else { 0 },
                    ],
                )?;
                Ok(default)
            }
        }
    }

    pub fn save_settings(&self, settings: &UserSettings) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute(
            "INSERT INTO settings (id, my_display_name, my_username, avatar_path, theme_mode,
                                  accent_color, chat_wallpaper_path, wallpaper_opacity, sound_enabled,
                                  sound_volume, screenshot_protection, avatar_corner_radius, bubble_radius,
                                  auto_download_media, e2ee_enabled, screen_watermark, pin_code, auto_lock_minutes,
                                  enable_acrylic, enable_animations, use_internal_browser)
             VALUES (1, ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20)
             ON CONFLICT(id) DO UPDATE SET
                my_display_name = excluded.my_display_name,
                my_username = excluded.my_username,
                avatar_path = excluded.avatar_path,
                theme_mode = excluded.theme_mode,
                accent_color = excluded.accent_color,
                chat_wallpaper_path = excluded.chat_wallpaper_path,
                wallpaper_opacity = excluded.wallpaper_opacity,
                sound_enabled = excluded.sound_enabled,
                sound_volume = excluded.sound_volume,
                screenshot_protection = excluded.screenshot_protection,
                avatar_corner_radius = excluded.avatar_corner_radius,
                bubble_radius = excluded.bubble_radius,
                auto_download_media = excluded.auto_download_media,
                e2ee_enabled = excluded.e2ee_enabled,
                screen_watermark = excluded.screen_watermark,
                pin_code = excluded.pin_code,
                auto_lock_minutes = excluded.auto_lock_minutes,
                enable_acrylic = excluded.enable_acrylic,
                enable_animations = excluded.enable_animations,
                use_internal_browser = excluded.use_internal_browser",
            params![
                settings.my_display_name,
                settings.my_username,
                settings.avatar_path,
                settings.theme_mode,
                settings.accent_color,
                settings.chat_wallpaper_path,
                settings.wallpaper_opacity,
                if settings.sound_enabled { 1 } else { 0 },
                settings.sound_volume,
                if settings.screenshot_protection { 1 } else { 0 },
                settings.avatar_corner_radius,
                settings.bubble_radius,
                if settings.auto_download_media { 1 } else { 0 },
                if settings.e2ee_enabled { 1 } else { 0 },
                if settings.screen_watermark { 1 } else { 0 },
                settings.pin_code,
                settings.auto_lock_minutes,
                if settings.enable_acrylic { 1 } else { 0 },
                if settings.enable_animations { 1 } else { 0 },
                if settings.use_internal_browser { 1 } else { 0 },
            ],
        )?;
        Ok(())
    }

    // ==========================================
    // Browser History & Bookmarks Management
    // ==========================================

    pub fn get_browser_history(&self) -> Result<Vec<RecentBrowserSite>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, url, title, domain, timestamp
             FROM browser_history ORDER BY timestamp DESC LIMIT 50",
        )?;

        let rows = stmt.query_map([], |row| {
            Ok(RecentBrowserSite {
                id: row.get(0)?,
                url: row.get(1)?,
                title: row.get(2)?,
                domain: row.get(3)?,
                timestamp: row.get(4)?,
            })
        })?;

        let mut sites = Vec::new();
        for r in rows {
            sites.push(r?);
        }
        Ok(sites)
    }

    pub fn add_browser_history(&self, url: &str, title: &str) -> Result<()> {
        let domain = url
            .trim_start_matches("https://")
            .trim_start_matches("http://")
            .split('/')
            .next()
            .unwrap_or("")
            .to_string();

        let conn = self.conn.lock();
        let now = chrono::Utc::now().timestamp_millis();
        conn.execute(
            "INSERT INTO browser_history (url, title, domain, timestamp) VALUES (?1, ?2, ?3, ?4)",
            params![url, title, domain, now],
        )?;
        Ok(())
    }

    pub fn clear_browser_history(&self) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM browser_history", [])?;
        Ok(())
    }

    pub fn get_bookmarks(&self) -> Result<Vec<BrowserBookmark>> {
        let conn = self.conn.lock();
        let mut stmt = conn.prepare(
            "SELECT id, url, title, icon, timestamp
             FROM browser_bookmarks ORDER BY timestamp DESC",
        )?;

        let rows = stmt.query_map([], |row| {
            Ok(BrowserBookmark {
                id: row.get(0)?,
                url: row.get(1)?,
                title: row.get(2)?,
                icon: row.get(3)?,
                timestamp: row.get(4)?,
            })
        })?;

        let mut bookmarks = Vec::new();
        for r in rows {
            bookmarks.push(r?);
        }
        Ok(bookmarks)
    }

    pub fn add_bookmark(&self, url: &str, title: &str, icon: Option<&str>) -> Result<()> {
        let conn = self.conn.lock();
        let now = chrono::Utc::now().timestamp_millis();
        conn.execute(
            "INSERT INTO browser_bookmarks (url, title, icon, timestamp) VALUES (?1, ?2, ?3, ?4)",
            params![url, title, icon, now],
        )?;
        Ok(())
    }

    pub fn delete_bookmark(&self, id: i64) -> Result<()> {
        let conn = self.conn.lock();
        conn.execute("DELETE FROM browser_bookmarks WHERE id = ?1", params![id])?;
        Ok(())
    }
}
