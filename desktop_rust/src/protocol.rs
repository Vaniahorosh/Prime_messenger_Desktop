use crate::models::PacketType;
use byteorder::{BigEndian, ByteOrder};
use std::io;
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};

pub struct PrimePacket {
    pub packet_type: PacketType,
    pub payload: Vec<u8>,
}

impl PrimePacket {
    pub fn new(packet_type: PacketType, payload: Vec<u8>) -> Self {
        Self {
            packet_type,
            payload,
        }
    }

    /// Writes atomic 5-byte header (1 byte Type + 4 bytes Big-Endian Length), then payload in 16KB chunks.
    pub async fn write_to_stream<W: AsyncWrite + Unpin>(
        &self,
        writer: &mut W,
    ) -> io::Result<()> {
        let mut header = [0u8; 5];
        header[0] = self.packet_type as u8;
        BigEndian::write_u32(&mut header[1..5], self.payload.len() as u32);
        writer.write_all(&header).await?;

        if !self.payload.is_empty() {
            let chunk_size = 16384;
            let mut offset = 0;
            while offset < self.payload.len() {
                let end = (offset + chunk_size).min(self.payload.len());
                writer.write_all(&self.payload[offset..end]).await?;
                offset = end;
            }
        }
        writer.flush().await?;
        Ok(())
    }

    /// Reads atomic 5-byte header, then reads the exact payload length.
    pub async fn read_from_stream<R: AsyncRead + Unpin>(
        reader: &mut R,
    ) -> io::Result<Option<Self>> {
        let mut header = [0u8; 5];
        match reader.read_exact(&mut header).await {
            Ok(_) => {}
            Err(e) if e.kind() == io::ErrorKind::UnexpectedEof => return Ok(None),
            Err(e) => return Err(e),
        }

        let packet_type = PacketType::from(header[0]);
        let length = BigEndian::read_u32(&header[1..5]) as usize;

        if length > 100 * 1024 * 1024 {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                format!("Packet payload too large: {} bytes", length),
            ));
        }

        let mut payload = vec![0u8; length];
        reader.read_exact(&mut payload).await?;

        Ok(Some(Self {
            packet_type,
            payload,
        }))
    }

    // ==========================================
    // Payload Formatters & Parsers
    // ==========================================

    pub fn build_text_payload(
        msg_id: &str,
        text: &str,
        reply_to_id: Option<&str>,
        reply_to_sender: Option<&str>,
        reply_to_text: Option<&str>,
    ) -> Vec<u8> {
        let mut result = format!("{}:::{}", msg_id, text);
        if let Some(r_id) = reply_to_id {
            result.push_str(":::REPLY:::");
            result.push_str(r_id);
            result.push_str(":::");
            result.push_str(reply_to_sender.unwrap_or(""));
            result.push_str(":::");
            result.push_str(reply_to_text.unwrap_or(""));
        }
        result.into_bytes()
    }

    pub fn parse_text_payload(
        payload: &[u8],
    ) -> (String, String, Option<String>, Option<String>, Option<String>) {
        let raw = String::from_utf8_lossy(payload);
        let mut msg_id = uuid::Uuid::new_v4().to_string();
        let mut body = raw.to_string();

        if let Some(sep) = raw.find(":::") {
            msg_id = raw[..sep].to_string();
            body = raw[sep + 3..].to_string();
        }

        let mut reply_id = None;
        let mut reply_sender = None;
        let mut reply_text = None;

        if let Some(reply_sep) = body.find(":::REPLY:::") {
            let reply_part = body[reply_sep + 11..].to_string();
            body = body[..reply_sep].to_string();

            let parts: Vec<&str> = reply_part.split(":::").collect();
            if !parts.is_empty() {
                reply_id = Some(parts[0].to_string());
            }
            if parts.len() > 1 {
                reply_sender = Some(parts[1].to_string());
            }
            if parts.len() > 2 {
                reply_text = Some(parts[2].to_string());
            }
        }

        (msg_id, body, reply_id, reply_sender, reply_text)
    }

    pub fn build_handshake_payload(display_name: &str, username: &str, is_ack: bool) -> Vec<u8> {
        let prefix = if is_ack { "HANDSHAKE_ACK:" } else { "HANDSHAKE:" };
        format!(
            "{}name={};login={};version=100;features=vn;presence=ONLINE;device=pc;platform=desktop",
            prefix, display_name, username
        )
        .into_bytes()
    }

    pub fn parse_handshake_payload(payload: &[u8]) -> (Option<String>, Option<String>, Option<String>, bool) {
        let text = String::from_utf8_lossy(payload);
        let is_ack = text.starts_with("HANDSHAKE_ACK:");
        let prefix = if is_ack { "HANDSHAKE_ACK:" } else { "HANDSHAKE:" };
        let data = if text.starts_with(prefix) {
            &text[prefix.len()..]
        } else {
            &text
        };

        let mut name = None;
        let mut login = None;
        let mut presence = None;

        for part in data.split(';') {
            let p = part.trim();
            if let Some(val) = p.strip_prefix("name=") {
                name = Some(val.to_string());
            } else if let Some(val) = p.strip_prefix("login=") {
                login = Some(val.to_string());
            } else if let Some(val) = p.strip_prefix("presence=") {
                presence = Some(val.to_string());
            }
        }

        (name, login, presence, is_ack)
    }

    pub fn build_reaction_payload(msg_id: &str, emoji: Option<&str>, author: &str) -> Vec<u8> {
        let rx = emoji.unwrap_or("REMOVE");
        format!("{}:::{}:::{}", msg_id, rx, author).into_bytes()
    }

    pub fn parse_reaction_payload(payload: &[u8]) -> (String, Option<String>, String) {
        let raw = String::from_utf8_lossy(payload);
        let parts: Vec<&str> = raw.split(":::").collect();
        let msg_id = parts.first().unwrap_or(&"").trim().to_string();
        let reaction = parts.get(1).map(|r| r.trim().to_string()).filter(|r| !r.is_empty() && r != "REMOVE");
        let author = parts.get(2).unwrap_or(&"").trim().to_string();
        (msg_id, reaction, author)
    }

    pub fn build_file_payload(
        msg_id: &str,
        file_name: &str,
        file_bytes: &[u8],
        duration: &str,
        is_voice: bool,
        caption: &str,
    ) -> Vec<u8> {
        let msg_type = if is_voice { "VOICE" } else { "FILE" };
        let text = if !caption.is_empty() {
            caption
        } else if is_voice {
            "Голосовое сообщение"
        } else {
            file_name
        };
        let is_vid_str = if text.starts_with("VIDEO_NOTE:") || file_name.starts_with("vn_") { "1" } else { "0" };

        let header = format!(
            "{}:::{}:::{}:::{}:::DURATION:::{}:::IS_VIDEO:::{}:::MSG_TYPE:::{}:::HEADER_END:::",
            msg_id, file_name, file_bytes.len(), text, duration, is_vid_str, msg_type
        );
        let header_bytes = header.as_bytes();

        let mut combined = Vec::with_capacity(header_bytes.len() + file_bytes.len());
        combined.extend_from_slice(header_bytes);
        combined.extend_from_slice(file_bytes);
        combined
    }

    pub fn parse_file_payload(
        payload: &[u8],
    ) -> Option<(String, String, String, bool, Vec<u8>, String)> {
        let tag = b":::HEADER_END:::";
        let tag_idx = payload.windows(tag.len()).position(|window| window == tag)?;

        let header_str = String::from_utf8_lossy(&payload[..tag_idx]);
        let file_bytes = payload[tag_idx + tag.len()..].to_vec();

        let parts: Vec<&str> = header_str.split(":::").collect();
        let msg_id = parts.first()?.to_string();
        let file_name = parts.get(1)?.to_string();
        let caption = parts.get(3).unwrap_or(&"").to_string();

        let mut duration = "00:00".to_string();
        let mut is_voice = false;

        let mut i = 4;
        while i < parts.len() {
            if parts[i] == "DURATION" && i + 1 < parts.len() {
                duration = parts[i + 1].to_string();
                i += 2;
            } else if parts[i] == "MSG_TYPE" && i + 1 < parts.len() {
                if parts[i + 1] == "VOICE" {
                    is_voice = true;
                }
                i += 2;
            } else {
                i += 1;
            }
        }

        Some((msg_id, file_name, duration, is_voice, file_bytes, caption))
    }
}
