use crate::models::{ChatItem, ChatMessage, PacketType, PeerDiscoveryInfo};
use crate::protocol::PrimePacket;
use crate::storage::Storage;
use parking_lot::RwLock;
use std::collections::HashMap;
use std::net::SocketAddr;
use std::path::PathBuf;
use std::sync::Arc;
use tokio::net::{TcpListener, TcpStream, UdpSocket};
use tokio::sync::Mutex;

pub type PeerConnections = Arc<RwLock<HashMap<String, Arc<Mutex<tokio::net::tcp::OwnedWriteHalf>>>>>;

#[derive(Clone)]
pub struct NetworkManager {
    pub handle: tokio::runtime::Handle,
    storage: Storage,
    peers: PeerConnections,
    media_dir: PathBuf,
}

impl NetworkManager {
    pub fn new(handle: tokio::runtime::Handle, storage: Storage, media_dir: PathBuf) -> Self {
        let _ = std::fs::create_dir_all(&media_dir);
        Self {
            handle,
            storage,
            peers: Arc::new(RwLock::new(HashMap::new())),
            media_dir,
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

    /// Scans LAN for Prime peers
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

                            results.push(PeerDiscoveryInfo {
                                ip: src.ip().to_string(),
                                port,
                                name,
                                username,
                                device_type: "android/pc".into(),
                            });
                        }
                    }
                }
            }
        }
        results
    }

    /// Starts TCP Listener on port 8989
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
                        mgr.handle_connection(stream, addr.ip().to_string()).await;
                    });
                }
            }
        });
    }

    /// Connects to a remote peer via IP:Port
    pub async fn connect_to_peer(&self, peer_ip: &str, port: u16) -> Result<(), String> {
        let addr = format!("{}:{}", peer_ip, port);
        let stream = TcpStream::connect(&addr).await.map_err(|e| e.to_string())?;

        let settings = self.storage.get_settings().unwrap_or_default();
        let handshake = PrimePacket::new(
            PacketType::Text,
            PrimePacket::build_handshake_payload(&settings.my_display_name, &settings.my_username, false),
        );

        let (mut read_half, mut write_half) = stream.into_split();
        handshake.write_to_stream(&mut write_half).await.map_err(|e| e.to_string())?;

        let write_arc = Arc::new(Mutex::new(write_half));
        self.peers.write().insert(peer_ip.to_string(), write_arc);

        let mgr = self.clone();
        let ip_clone = peer_ip.to_string();
        self.handle.spawn(async move {
            mgr.read_loop(&mut read_half, &ip_clone).await;
        });

        Ok(())
    }

    async fn handle_connection(&self, stream: TcpStream, peer_ip: String) {
        let (mut read_half, write_half) = stream.into_split();
        let write_arc = Arc::new(Mutex::new(write_half));
        self.peers.write().insert(peer_ip.clone(), write_arc);

        self.read_loop(&mut read_half, &peer_ip).await;
        self.peers.write().remove(&peer_ip);
    }

    async fn read_loop<R: tokio::io::AsyncRead + Unpin>(&self, reader: &mut R, peer_ip: &str) {
        while let Ok(Some(packet)) = PrimePacket::read_from_stream(reader).await {
            match packet.packet_type {
                PacketType::Text => {
                    let text_str = String::from_utf8_lossy(&packet.payload);
                    if text_str.starts_with("HANDSHAKE:") || text_str.starts_with("HANDSHAKE_ACK:") {
                        let (name, login, _, is_ack) = PrimePacket::parse_handshake_payload(&packet.payload);
                        let display_name = name.unwrap_or_else(|| "Собеседник".into());
                        let username = login.unwrap_or_else(|| "peer".into());
                        let initials = display_name.chars().take(2).collect::<String>().to_uppercase();

                        let chat = ChatItem {
                            id: username.clone(),
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
                            peer_ip: Some(peer_ip.to_string()),
                            peer_port: Some(8989),
                        };
                        let _ = self.storage.upsert_chat(&chat);

                        if !is_ack {
                            // Reply with HANDSHAKE_ACK
                            let settings = self.storage.get_settings().unwrap_or_default();
                            let ack_packet = PrimePacket::new(
                                PacketType::Text,
                                PrimePacket::build_handshake_payload(&settings.my_display_name, &settings.my_username, true),
                            );
                            let _ = self.send_raw_packet(peer_ip, &ack_packet).await;
                        }
                    } else {
                        // Standard text message
                        let (msg_id, body, r_id, r_sender, r_text) = PrimePacket::parse_text_payload(&packet.payload);
                        let msg = ChatMessage {
                            id: msg_id,
                            chat_id: peer_ip.to_string(),
                            sender: peer_ip.to_string(),
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
                            is_first_in_chat: false,
                            is_last_in_chat: false,
                        };
                        let _ = self.storage.save_message(&msg);
                    }
                }
                PacketType::Reaction => {
                    let (msg_id, reaction, author) = PrimePacket::parse_reaction_payload(&packet.payload);
                    let _ = self.apply_reaction(&msg_id, reaction, &author);
                }
                PacketType::DeleteMessage => {
                    let msg_id = String::from_utf8_lossy(&packet.payload).trim().to_string();
                    let _ = self.storage.delete_message(&msg_id);
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
                            chat_id: peer_ip.to_string(),
                            sender: peer_ip.to_string(),
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
                            is_first_in_chat: false,
                            is_last_in_chat: false,
                        };
                        let _ = self.storage.save_message(&msg);
                    }
                }
                PacketType::Ping => {
                    let ack = PrimePacket::new(PacketType::Ack, vec![]);
                    let _ = self.send_raw_packet(peer_ip, &ack).await;
                }
                _ => {}
            }
        }
    }

    pub async fn send_raw_packet(&self, peer_ip: &str, packet: &PrimePacket) -> Result<(), String> {
        let stream_opt = {
            let map = self.peers.read();
            map.get(peer_ip).cloned()
        };

        if let Some(stream_arc) = stream_opt {
            let mut writer = stream_arc.lock().await;
            packet.write_to_stream(&mut *writer).await.map_err(|e| e.to_string())?;
            Ok(())
        } else {
            Err(format!("Peer {} not connected", peer_ip))
        }
    }

    fn apply_reaction(&self, _msg_id: &str, _emoji_opt: Option<String>, _author: &str) -> Result<(), String> {
        Ok(())
    }
}
