use crate::anonymizer::get_opaque_peer_id;
use crate::models::{ChatItem, ChatMessage, OutboxItem, PacketType, PeerDiscoveryInfo};
use crate::protocol::PrimePacket;
use crate::sound::{play_sound, SoundEffect};
use crate::storage::Storage;
use parking_lot::RwLock;
use std::collections::HashMap;
use std::net::SocketAddr;
use std::path::PathBuf;
use std::sync::Arc;
use tauri::{AppHandle, Emitter};
use tokio::net::{TcpListener, TcpStream, UdpSocket};
use tokio::sync::Mutex;

pub type PeerConnections = Arc<RwLock<HashMap<String, Arc<Mutex<tokio::net::tcp::OwnedWriteHalf>>>>>;

#[derive(Clone)]
pub struct NetworkManager {
    pub handle: tokio::runtime::Handle,
    pub app_handle: Arc<RwLock<Option<AppHandle>>>,
    storage: Storage,
    peers: PeerConnections,
    peer_addrs: Arc<RwLock<HashMap<String, (String, u16)>>>, // peer_id -> (ip, port)
    media_dir: PathBuf,
}

impl NetworkManager {
    pub fn new(handle: tokio::runtime::Handle, storage: Storage, media_dir: PathBuf) -> Self {
        let _ = std::fs::create_dir_all(&media_dir);
        Self {
            handle,
            app_handle: Arc::new(RwLock::new(None)),
            storage,
            peers: Arc::new(RwLock::new(HashMap::new())),
            peer_addrs: Arc::new(RwLock::new(HashMap::new())),
            media_dir,
        }
    }

    pub fn set_app_handle(&self, app_handle: AppHandle) {
        *self.app_handle.write() = Some(app_handle);
    }

    fn emit_event<S: serde::Serialize + Clone>(&self, event: &str, payload: S) {
        if let Some(ref app) = *self.app_handle.read() {
            let _ = app.emit(event, payload);
        }
    }

    /// Starts UDP responder on port 8988
    pub fn start_udp_responder(&self, my_name: String, my_username: String) {
        self.handle.spawn(async move {
            let socket = match UdpSocket::bind("0.0.0.0:8988").await {
                Ok(s) => s,
                Err(e) => {
                    eprintln!("Failed to bind UDP responder port 8988: {}", e);
                    return;
                }
            };

            let mut buf = [0u8; 1024];
            loop {
                if let Ok((len, src)) = socket.recv_from(&mut buf).await {
                    let msg = String::from_utf8_lossy(&buf[..len]);
                    if msg.contains("PRIME_PC_DISCOVER") {
                        let response = format!(
                            "PRIME_PEER:ip={};name={};login={};port=8989",
                            socket.local_addr().map(|a| a.ip().to_string()).unwrap_or_default(),
                            my_name,
                            my_username
                        );
                        let _ = socket.send_to(response.as_bytes(), src).await;
                    }
                }
            }
        });
    }

    /// Scans LAN for Prime peers (Phones and PCs)
    pub async fn scan_network() -> Vec<PeerDiscoveryInfo> {
        let mut results = Vec::new();
        let socket = match UdpSocket::bind("0.0.0.0:0").await {
            Ok(s) => s,
            Err(_) => return results,
        };
        let _ = socket.set_broadcast(true);

        let broadcast_addr: SocketAddr = "255.255.255.255:8988".parse().unwrap();
        let _ = socket.send_to(b"PRIME_PC_DISCOVER", broadcast_addr).await;

        let mut buf = [0u8; 1024];
        let timeout = tokio::time::sleep(tokio::time::Duration::from_millis(1500));
        tokio::pin!(timeout);

        loop {
            tokio::select! {
                _ = &mut timeout => break,
                res = socket.recv_from(&mut buf) => {
                    if let Ok((len, src)) = res {
                        let text = String::from_utf8_lossy(&buf[..len]);
                        if text.starts_with("PRIME_PEER:") {
                            let data = &text["PRIME_PEER:".len()..];
                            let mut name = "Peer".to_string();
                            let mut username = "peer".to_string();
                            let mut port = 8989u16;

                            for part in data.split(';') {
                                if let Some(v) = part.strip_prefix("name=") { name = v.to_string(); }
                                else if let Some(v) = part.strip_prefix("login=") { username = v.to_string(); }
                                else if let Some(v) = part.strip_prefix("port=") { port = v.parse().unwrap_or(8989); }
                            }

                            let ip_str = src.ip().to_string();
                            let opaque_id = get_opaque_peer_id(&format!("{}:{}", ip_str, port));

                            results.push(PeerDiscoveryInfo {
                                ip: ip_str,
                                port,
                                name,
                                username,
                                device_type: "android/pc".into(),
                                opaque_id,
                            });
                        }
                    }
                }
            }
        }
        results
    }

    /// Starts TCP Server on port 8989
    pub fn start_tcp_server(&self) {
        let this = self.clone();
        let handle = self.handle.clone();
        self.handle.spawn(async move {
            let listener = match TcpListener::bind("0.0.0.0:8989").await {
                Ok(l) => l,
                Err(e) => {
                    eprintln!("Failed to bind TCP server on 8989: {}", e);
                    return;
                }
            };

            loop {
                if let Ok((stream, addr)) = listener.accept().await {
                    let mgr = this.clone();
                    handle.spawn(async move {
                        mgr.handle_connection(stream, addr.ip().to_string(), addr.port()).await;
                    });
                }
            }
        });
    }

    /// Connects to a remote peer via IP:Port
    pub async fn connect_to_peer(&self, peer_ip: &str, port: u16) -> Result<String, String> {
        let addr = format!("{}:{}", peer_ip, port);
        let stream = TcpStream::connect(&addr).await.map_err(|e| e.to_string())?;

        let opaque_id = get_opaque_peer_id(&format!("{}:{}", peer_ip, port));
        self.peer_addrs.write().insert(opaque_id.clone(), (peer_ip.to_string(), port));

        let settings = self.storage.get_settings().unwrap_or_default();
        let handshake = PrimePacket::new(
            PacketType::Text,
            PrimePacket::build_handshake_payload(&settings.my_display_name, &settings.my_username, false),
        );

        let (mut read_half, mut write_half) = stream.into_split();
        handshake.write_to_stream(&mut write_half).await.map_err(|e| e.to_string())?;

        let write_arc = Arc::new(Mutex::new(write_half));
        self.peers.write().insert(opaque_id.clone(), write_arc);

        // Send queued outbox items
        self.flush_outbox(&opaque_id).await;

        let mgr = self.clone();
        let peer_id_clone = opaque_id.clone();
        self.handle.spawn(async move {
            mgr.read_loop(&mut read_half, &peer_id_clone).await;
        });

        // Start KeepAlive
        self.start_keepalive(opaque_id.clone());

        Ok(opaque_id)
    }

    async fn handle_connection(&self, stream: TcpStream, peer_ip: String, port: u16) {
        let opaque_id = get_opaque_peer_id(&format!("{}:{}", peer_ip, port));
        self.peer_addrs.write().insert(opaque_id.clone(), (peer_ip, port));

        let (mut read_half, mut write_half) = stream.into_split();

        // Send our handshake
        let settings = self.storage.get_settings().unwrap_or_default();
        let handshake = PrimePacket::new(
            PacketType::Text,
            PrimePacket::build_handshake_payload(&settings.my_display_name, &settings.my_username, false),
        );
        let _ = handshake.write_to_stream(&mut write_half).await;

        let write_arc = Arc::new(Mutex::new(write_half));
        self.peers.write().insert(opaque_id.clone(), write_arc);

        self.flush_outbox(&opaque_id).await;
        self.start_keepalive(opaque_id.clone());

        self.read_loop(&mut read_half, &opaque_id).await;
        self.peers.write().remove(&opaque_id);

        let _ = self.storage.update_chat_status(&opaque_id, false, "не в сети");
        self.emit_event("peer-status", serde_json::json!({ "chat_id": opaque_id, "is_online": false, "status_text": "не в сети" }));
    }

    fn start_keepalive(&self, peer_id: String) {
        let mgr = self.clone();
        self.handle.spawn(async move {
            loop {
                tokio::time::sleep(tokio::time::Duration::from_secs(6)).await;
                if !mgr.is_peer_connected(&peer_id) {
                    break;
                }
                let ping = PrimePacket::new(PacketType::Ping, vec![]);
                if mgr.send_raw_packet(&peer_id, &ping).await.is_err() {
                    break;
                }
            }
        });
    }

    pub fn is_peer_connected(&self, peer_id: &str) -> bool {
        self.peers.read().contains_key(peer_id)
    }

    async fn flush_outbox(&self, peer_id: &str) {
        if let Ok(items) = self.storage.get_outbox_for_peer(peer_id) {
            for item in items {
                let packet = PrimePacket::new(item.packet_type, item.payload);
                if self.send_raw_packet(peer_id, &packet).await.is_ok() {
                    let _ = self.storage.delete_outbox_item(&item.id);
                }
            }
        }
    }

    async fn read_loop<R: tokio::io::AsyncRead + Unpin>(&self, reader: &mut R, peer_id: &str) {
        while let Ok(Some(packet)) = PrimePacket::read_from_stream(reader).await {
            match packet.packet_type {
                PacketType::Text => {
                    let text_str = String::from_utf8_lossy(&packet.payload);
                    if text_str.starts_with("HANDSHAKE:") || text_str.starts_with("HANDSHAKE_ACK:") {
                        let (name, login, _, device, is_ack) = PrimePacket::parse_handshake_payload(&packet.payload);
                        let display_name = name.unwrap_or_else(|| "Prime Собеседник".into());
                        let username = login.unwrap_or_else(|| "peer".into());
                        let initials = display_name.chars().take(2).collect::<String>().to_uppercase();
                        let device_type = device.unwrap_or_else(|| "android".into());

                        let (ip, port) = self.peer_addrs.read().get(peer_id).cloned().unwrap_or_default();

                        let chat = ChatItem {
                            id: peer_id.to_string(),
                            display_name,
                            username,
                            avatar_path: None,
                            initials,
                            last_message: "Подключено".into(),
                            last_message_time: chrono::Local::now().format("%H:%M").to_string(),
                            unread_count: 0,
                            is_online: true,
                            status_text: "в сети".into(),
                            is_muted: false,
                            peer_ip: Some(ip),
                            peer_port: Some(port),
                            device_type,
                        };
                        let _ = self.storage.upsert_chat(&chat);
                        self.emit_event("peer-status", serde_json::json!({ "chat_id": peer_id, "is_online": true, "status_text": "в сети" }));

                        if !is_ack {
                            let settings = self.storage.get_settings().unwrap_or_default();
                            let ack_packet = PrimePacket::new(
                                PacketType::Text,
                                PrimePacket::build_handshake_payload(&settings.my_display_name, &settings.my_username, true),
                            );
                            let _ = self.send_raw_packet(peer_id, &ack_packet).await;
                        }
                    } else {
                        // Standard text message
                        let (msg_id, body, r_id, r_sender, r_text) = PrimePacket::parse_text_payload(&packet.payload);
                        let msg = ChatMessage {
                            id: msg_id.clone(),
                            chat_id: peer_id.to_string(),
                            sender: peer_id.to_string(),
                            text: body,
                            timestamp: chrono::Utc::now().timestamp_millis(),
                            formatted_time: chrono::Local::now().format("%H:%M").to_string(),
                            is_from_me: false,
                            packet_type: PacketType::Text,
                            reply_to_id: r_id,
                            reply_to_sender: r_sender,
                            reply_to_text: r_text,
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
                            link_preview: None,
                            is_first_in_chat: false,
                            is_last_in_chat: false,
                        };
                        let _ = self.storage.save_message(&msg);
                        play_sound(SoundEffect::Received, 0.8);
                        self.emit_event("new-message", msg);

                        // Send ReadReceipt
                        let rr = PrimePacket::new(PacketType::ReadReceipt, PrimePacket::build_read_receipt_payload(&msg_id));
                        let _ = self.send_raw_packet(peer_id, &rr).await;
                    }
                }
                PacketType::Edit => {
                    if let Some((msg_id, new_text)) = PrimePacket::parse_edit_payload(&packet.payload) {
                        let _ = self.storage.edit_message(&msg_id, &new_text);
                        self.emit_event("message-edited", serde_json::json!({ "id": msg_id, "text": new_text }));
                    }
                }
                PacketType::DeleteMessage => {
                    let msg_id = PrimePacket::parse_delete_payload(&packet.payload);
                    let _ = self.storage.delete_message(&msg_id);
                    self.emit_event("message-deleted", serde_json::json!({ "id": msg_id }));
                }
                PacketType::ReadReceipt => {
                    let _msg_id = PrimePacket::parse_read_receipt_payload(&packet.payload);
                    let _ = self.storage.mark_messages_read(peer_id);
                    self.emit_event("messages-read", serde_json::json!({ "chat_id": peer_id }));
                }
                PacketType::Typing => {
                    self.emit_event("peer-typing", serde_json::json!({ "chat_id": peer_id }));
                }
                PacketType::Presence => {
                    let status = String::from_utf8_lossy(&packet.payload).trim().to_string();
                    let is_online = status == "ONLINE";
                    let status_text = if is_online { "в сети" } else { "не в сети" };
                    let _ = self.storage.update_chat_status(peer_id, is_online, status_text);
                    self.emit_event("peer-status", serde_json::json!({ "chat_id": peer_id, "is_online": is_online, "status_text": status_text }));
                }
                PacketType::Reaction => {
                    let (msg_id, reaction, author) = PrimePacket::parse_reaction_payload(&packet.payload);
                    let _ = self.storage.apply_reaction(&msg_id, reaction.clone(), &author);
                    self.emit_event("reaction-updated", serde_json::json!({ "msg_id": msg_id, "reaction": reaction, "author": author }));
                    play_sound(SoundEffect::Pop, 0.6);
                }
                PacketType::File | PacketType::Photo | PacketType::Voice => {
                    if let Some((msg_id, file_name, duration, is_voice, file_bytes, caption)) =
                        PrimePacket::parse_file_payload(&packet.payload)
                    {
                        let file_path = self.media_dir.join(&file_name);
                        let _ = std::fs::write(&file_path, &file_bytes);

                        let is_photo = file_name.ends_with(".jpg") || file_name.ends_with(".png") || file_name.ends_with(".webp");
                        let is_video = file_name.ends_with(".mp4") || file_name.ends_with(".webm");

                        let msg = ChatMessage {
                            id: msg_id,
                            chat_id: peer_id.to_string(),
                            sender: peer_id.to_string(),
                            text: caption,
                            timestamp: chrono::Utc::now().timestamp_millis(),
                            formatted_time: chrono::Local::now().format("%H:%M").to_string(),
                            is_from_me: false,
                            packet_type: packet.packet_type,
                            reply_to_id: None,
                            reply_to_sender: None,
                            reply_to_text: None,
                            file_name: Some(file_name),
                            file_size: Some(file_bytes.len() as i64),
                            file_path: Some(file_path.to_string_lossy().to_string()),
                            duration: Some(duration),
                            is_voice,
                            is_photo,
                            is_video,
                            is_delivered: true,
                            is_read: false,
                            is_edited: false,
                            reactions: Vec::new(),
                            link_preview: None,
                            is_first_in_chat: false,
                            is_last_in_chat: false,
                        };
                        let _ = self.storage.save_message(&msg);
                        play_sound(SoundEffect::Received, 0.8);
                        self.emit_event("new-message", msg);
                    }
                }
                PacketType::Ping => {
                    let ack = PrimePacket::new(PacketType::Ack, vec![]);
                    let _ = self.send_raw_packet(peer_id, &ack).await;
                }
                _ => {}
            }
        }
    }

    pub async fn send_raw_packet(&self, peer_id: &str, packet: &PrimePacket) -> Result<(), String> {
        let stream_opt = {
            let map = self.peers.read();
            map.get(peer_id).cloned()
        };

        if let Some(stream_arc) = stream_opt {
            let mut writer = stream_arc.lock().await;
            packet.write_to_stream(&mut *writer).await.map_err(|e| e.to_string())?;
            Ok(())
        } else {
            // Queue to outbox for offline delivery
            let outbox = OutboxItem {
                id: uuid::Uuid::new_v4().to_string(),
                peer_id: peer_id.to_string(),
                packet_type: packet.packet_type,
                payload: packet.payload.clone(),
                retry_count: 0,
                created_at: chrono::Utc::now().timestamp_millis(),
            };
            let _ = self.storage.queue_outbox(&outbox);
            Err(format!("Peer {} not connected, queued to outbox", peer_id))
        }
    }
}
