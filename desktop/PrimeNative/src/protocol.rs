use byteorder::{BigEndian, ByteOrder};

pub struct PrimeProtocol;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HandshakeInfo {
    pub display_name: String,
    pub username: String,
    pub presence: String,
    pub is_ack: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TextPayloadInfo {
    pub msg_id: String,
    pub text: String,
    pub reply_to_id: Option<String>,
    pub reply_to_sender: Option<String>,
    pub reply_to_text: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReactionInfo {
    pub msg_id: String,
    pub reaction: Option<String>,
    pub author_login: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FileHeaderInfo {
    pub msg_id: String,
    pub file_name: String,
    pub duration: String,
    pub is_voice: bool,
    pub caption: String,
    pub file_length: usize,
    pub body_start_offset: usize,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PhotoMetaInfo {
    pub msg_id: String,
    pub caption: String,
    pub is_collage: bool,
    pub photo_offset: usize,
    pub photo_length: usize,
}

impl PrimeProtocol {
    /// Builds 5-byte header frame: [Type: 1B][Length: 4B Big-Endian] followed by payload.
    pub fn build_frame(packet_type: u8, payload: &[u8]) -> Vec<u8> {
        let mut frame = Vec::with_capacity(5 + payload.len());
        frame.push(packet_type);
        let mut len_bytes = [0u8; 4];
        BigEndian::write_u32(&mut len_bytes, payload.len() as u32);
        frame.extend_from_slice(&len_bytes);
        frame.extend_from_slice(payload);
        frame
    }

    /// Parses 5-byte header into packet type and payload length.
    pub fn parse_header(header: &[u8]) -> Result<(u8, u32), &'static str> {
        if header.len() < 5 {
            return Err("Header buffer must be at least 5 bytes");
        }
        let packet_type = header[0];
        let length = BigEndian::read_u32(&header[1..5]);
        Ok((packet_type, length))
    }

    /// Java-compatible String.hashCode() implemented over UTF-16 code units.
    pub fn java_string_hash_code(s: &str) -> i32 {
        let mut h: i32 = 0;
        for ch in s.encode_utf16() {
            h = (31i32.wrapping_mul(h)).wrapping_add(ch as i32);
        }
        h
    }

    /// Deterministic role arbitration matching Android BluetoothConnectionManager.isClientInitiator.
    /// Returns true if local device should initiate RFCOMM client connection.
    pub fn is_client_initiator(local_name: &str, remote_name: &str) -> bool {
        let loc = local_name.trim().to_uppercase();
        let rem = remote_name.trim().to_uppercase();
        if loc.is_empty() || rem.is_empty() {
            return true;
        }
        let comp = loc.cmp(&rem);
        if comp != std::cmp::Ordering::Equal {
            return comp == std::cmp::Ordering::Greater;
        }
        Self::java_string_hash_code(&loc) >= Self::java_string_hash_code(&rem)
    }

    /// Formats text message payload with optional quoted reply preview.
    pub fn build_text_payload(
        msg_id: &str,
        text: &str,
        reply_to_id: Option<&str>,
        reply_to_sender: Option<&str>,
        reply_to_text: Option<&str>,
    ) -> Vec<u8> {
        let mut s = format!("{}:::{}", msg_id, text);
        if let Some(r_id) = reply_to_id {
            if !r_id.is_empty() {
                s.push_str(":::REPLY:::");
                s.push_str(r_id);
                s.push_str(":::");
                s.push_str(reply_to_sender.unwrap_or(""));
                s.push_str(":::");
                s.push_str(reply_to_text.unwrap_or(""));
            }
        }
        s.into_bytes()
    }

    /// Parses text message payload extracting message ID, body, and quoted reply info.
    pub fn parse_text_payload(payload: &[u8]) -> TextPayloadInfo {
        let raw = String::from_utf8_lossy(payload);
        let (msg_id, body) = match raw.find(":::") {
            Some(idx) => (raw[..idx].to_string(), &raw[idx + 3..]),
            None => (String::new(), raw.as_ref()),
        };

        if let Some(reply_idx) = body.find(":::REPLY:::") {
            let actual_text = body[..reply_idx].to_string();
            let reply_str = &body[reply_idx + 11..];
            let parts: Vec<&str> = reply_str.split(":::").collect();
            let r_id = parts.get(0).copied().filter(|s| !s.is_empty()).map(String::from);
            let r_sender = parts.get(1).copied().filter(|s| !s.is_empty()).map(String::from);
            let r_text = parts.get(2).copied().filter(|s| !s.is_empty()).map(String::from);
            TextPayloadInfo {
                msg_id,
                text: actual_text,
                reply_to_id: r_id,
                reply_to_sender: r_sender,
                reply_to_text: r_text,
            }
        } else {
            TextPayloadInfo {
                msg_id,
                text: body.to_string(),
                reply_to_id: None,
                reply_to_sender: None,
                reply_to_text: None,
            }
        }
    }

    /// Builds handshake payload for RFCOMM or Companion Bridge.
    pub fn build_handshake_payload(display_name: &str, username: &str, is_ack: bool) -> Vec<u8> {
        let prefix = if is_ack { "HANDSHAKE_ACK:" } else { "HANDSHAKE:" };
        let content = format!(
            "{}name={};login={};version=100;features=vn;presence=ONLINE;device=pc;platform=desktop",
            prefix, display_name, username
        );
        content.into_bytes()
    }

    /// Parses handshake payload extracting display name, username, presence, and ACK flag.
    pub fn parse_handshake_payload(payload: &[u8]) -> Option<HandshakeInfo> {
        let text = String::from_utf8_lossy(payload);
        let is_ack = text.starts_with("HANDSHAKE_ACK:");
        let prefix = if is_ack { "HANDSHAKE_ACK:" } else { "HANDSHAKE:" };
        if !text.starts_with(prefix) && !text.starts_with("HANDSHAKE:") {
            return None;
        }

        let data = if text.starts_with(prefix) {
            text[prefix.len()..].trim()
        } else {
            text["HANDSHAKE:".len()..].trim()
        };

        let mut name = String::new();
        let mut login = String::new();
        let mut presence = "ONLINE".to_string();

        if data.contains("login=") || data.contains("name=") || data.contains("presence=") {
            for part in data.split(';') {
                let part = part.trim();
                if let Some(val) = part.strip_prefix("name=") {
                    name = val.trim().to_string();
                } else if let Some(val) = part.strip_prefix("login=") {
                    login = val.trim().to_string();
                } else if let Some(val) = part.strip_prefix("presence=") {
                    presence = val.trim().to_string();
                }
            }
        } else {
            name = data.to_string();
        }

        Some(HandshakeInfo {
            display_name: name,
            username: login,
            presence,
            is_ack,
        })
    }

    /// Builds reaction payload (format: msgId:::reaction:::authorLogin).
    pub fn build_reaction_payload(msg_id: &str, reaction: Option<&str>, author_login: &str) -> Vec<u8> {
        let rx = match reaction {
            Some(r) if !r.trim().is_empty() => r.trim(),
            _ => "REMOVE",
        };
        format!("{}:::{}:::{}", msg_id, rx, author_login).into_bytes()
    }

    /// Parses reaction payload into msg_id, optional reaction (None if REMOVE), and author_login.
    pub fn parse_reaction_payload(payload: &[u8]) -> ReactionInfo {
        let raw = String::from_utf8_lossy(payload);
        let parts: Vec<&str> = raw.split(":::").collect();
        let msg_id = parts.get(0).copied().unwrap_or("").trim().to_string();
        let rx_raw = parts.get(1).copied().unwrap_or("").trim();
        let reaction = if rx_raw.is_empty() || rx_raw.eq_ignore_ascii_case("REMOVE") {
            None
        } else {
            Some(rx_raw.to_string())
        };
        let author_login = parts.get(2).copied().unwrap_or("").trim().to_string();

        ReactionInfo {
            msg_id,
            reaction,
            author_login,
        }
    }

    /// Builds edit message payload (msgId:::newText).
    pub fn build_edit_payload(msg_id: &str, new_text: &str) -> Vec<u8> {
        format!("{}:::{}", msg_id, new_text).into_bytes()
    }

    /// Parses edit message payload into msg_id and new_text.
    pub fn parse_edit_payload(payload: &[u8]) -> (String, String) {
        let raw = String::from_utf8_lossy(payload);
        match raw.find(":::") {
            Some(idx) => (raw[..idx].trim().to_string(), raw[idx + 3..].to_string()),
            None => (raw.trim().to_string(), String::new()),
        }
    }

    /// Builds file payload combining header metadata and raw file bytes.
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
        let is_vid_str = if text.starts_with("VIDEO_NOTE:") || file_name.starts_with("vn_") {
            "1"
        } else {
            "0"
        };
        let header = format!(
            "{}:::{}:::{}:::{}:::DURATION:::{}:::IS_VIDEO:::{}:::MSG_TYPE:::{}:::HEADER_END:::",
            msg_id, file_name, file_bytes.len(), text, duration, is_vid_str, msg_type
        );
        let header_bytes = header.into_bytes();
        let mut result = Vec::with_capacity(header_bytes.len() + file_bytes.len());
        result.extend_from_slice(&header_bytes);
        result.extend_from_slice(file_bytes);
        result
    }

    /// Parses file payload header and offsets.
    pub fn parse_file_header(payload: &[u8]) -> Option<FileHeaderInfo> {
        let tag = b":::HEADER_END:::";
        let tag_idx = payload.windows(tag.len()).position(|window| window == tag)?;
        let header_str = String::from_utf8_lossy(&payload[..tag_idx]);
        let body_start = tag_idx + tag.len();
        let file_length = payload.len().saturating_sub(body_start);

        let parts: Vec<&str> = header_str.split(":::").collect();
        let msg_id = parts.get(0).copied().unwrap_or("").to_string();
        let file_name = parts.get(1).copied().unwrap_or("file.bin").to_string();
        let caption = parts.get(3).copied().unwrap_or("").to_string();

        let mut duration = "00:00".to_string();
        let mut is_voice = false;

        for i in 0..parts.len() {
            if parts[i] == "DURATION" && i + 1 < parts.len() {
                duration = parts[i + 1].to_string();
            } else if parts[i] == "MSG_TYPE" && i + 1 < parts.len() && parts[i + 1] == "VOICE" {
                is_voice = true;
            }
        }

        Some(FileHeaderInfo {
            msg_id,
            file_name,
            duration,
            is_voice,
            caption,
            file_length,
            body_start_offset: body_start,
        })
    }

    /// Builds single photo payload with message ID and optional |PRM| caption trailer.
    pub fn build_photo_payload(msg_id: &str, photo_bytes: &[u8], caption: &str) -> Vec<u8> {
        let prefix = format!("{}:::", msg_id).into_bytes();
        let mut result = Vec::with_capacity(prefix.len() + photo_bytes.len() + caption.len() + 9);
        result.extend_from_slice(&prefix);
        result.extend_from_slice(photo_bytes);

        if !caption.is_empty() {
            let cap_bytes = caption.as_bytes();
            result.extend_from_slice(cap_bytes);
            let mut len_bytes = [0u8; 4];
            BigEndian::write_u32(&mut len_bytes, cap_bytes.len() as u32);
            result.extend_from_slice(&len_bytes);
            result.extend_from_slice(b"|PRM|");
        }

        result
    }

    /// Parses photo payload to extract msg_id, caption, photo data offset, and length.
    pub fn parse_photo_meta(payload: &[u8]) -> PhotoMetaInfo {
        if payload.is_empty() {
            return PhotoMetaInfo {
                msg_id: String::new(),
                caption: String::new(),
                is_collage: false,
                photo_offset: 0,
                photo_length: 0,
            };
        }

        // Check if collage
        let prefix_len = std::cmp::min(payload.len(), 350);
        let check_str = String::from_utf8_lossy(&payload[..prefix_len]);
        if check_str.contains(":::MULTI:") {
            let tag = b":::HEADER_END:::";
            if let Some(tag_idx) = payload.windows(tag.len()).position(|w| w == tag) {
                let header_str = String::from_utf8_lossy(&payload[..tag_idx]);
                let msg_id = header_str.split(":::").next().unwrap_or("").to_string();
                let mut caption = String::new();
                if let Some(multi_idx) = header_str.find(":::MULTI:") {
                    if let Some(sizes_idx) = header_str.find(":::SIZES:") {
                        if sizes_idx > multi_idx {
                            let sub = &header_str[multi_idx + 9..sizes_idx];
                            if let Some(col) = sub.find(":::") {
                                caption = sub[col + 3..].to_string();
                            }
                        }
                    }
                }
                let body_start = tag_idx + tag.len();
                return PhotoMetaInfo {
                    msg_id,
                    caption,
                    is_collage: true,
                    photo_offset: body_start,
                    photo_length: payload.len().saturating_sub(body_start),
                };
            }
        }

        // Single photo: find msgId:::
        let sep_tag = b":::";
        let sep_idx = payload[..std::cmp::min(payload.len(), 120)]
            .windows(sep_tag.len())
            .position(|w| w == sep_tag);

        let (msg_id, data_start) = match sep_idx {
            Some(idx) => (
                String::from_utf8_lossy(&payload[..idx]).to_string(),
                idx + 3,
            ),
            None => (String::new(), 0),
        };

        let mut data_len = payload.len().saturating_sub(data_start);
        let mut caption = String::new();

        if data_len > 9 {
            let end = payload.len();
            if &payload[end - 5..end] == b"|PRM|" {
                let cap_len = BigEndian::read_u32(&payload[end - 9..end - 5]) as usize;
                if cap_len > 0 && cap_len < data_len - 9 {
                    let cap_start = end - 9 - cap_len;
                    caption = String::from_utf8_lossy(&payload[cap_start..cap_start + cap_len]).to_string();
                    data_len -= 9 + cap_len;
                }
            }
        }

        PhotoMetaInfo {
            msg_id,
            caption,
            is_collage: false,
            photo_offset: data_start,
            photo_length: data_len,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_build_and_parse_frame() {
        let payload = b"Hello Prime!";
        let frame = PrimeProtocol::build_frame(0x01, payload);
        assert_eq!(frame.len(), 5 + payload.len());
        assert_eq!(frame[0], 0x01);

        let (p_type, len) = PrimeProtocol::parse_header(&frame[..5]).unwrap();
        assert_eq!(p_type, 0x01);
        assert_eq!(len as usize, payload.len());
    }

    #[test]
    fn test_role_arbitration() {
        assert!(PrimeProtocol::is_client_initiator("PrimeClient", "AlicePhone"));
        assert!(!PrimeProtocol::is_client_initiator("AlicePhone", "PrimeClient"));
        assert!(PrimeProtocol::is_client_initiator("SAME", "SAME"));
    }

    #[test]
    fn test_text_payload_with_reply() {
        let payload = PrimeProtocol::build_text_payload(
            "msg-123",
            "This is a message",
            Some("reply-456"),
            Some("Alice"),
            Some("Original text"),
        );
        let parsed = PrimeProtocol::parse_text_payload(&payload);
        assert_eq!(parsed.msg_id, "msg-123");
        assert_eq!(parsed.text, "This is a message");
        assert_eq!(parsed.reply_to_id.as_deref(), Some("reply-456"));
        assert_eq!(parsed.reply_to_sender.as_deref(), Some("Alice"));
        assert_eq!(parsed.reply_to_text.as_deref(), Some("Original text"));
    }

    #[test]
    fn test_handshake_parsing() {
        let hs = PrimeProtocol::build_handshake_payload("DeskUser", "desk_login", false);
        let parsed = PrimeProtocol::parse_handshake_payload(&hs).unwrap();
        assert_eq!(parsed.display_name, "DeskUser");
        assert_eq!(parsed.username, "desk_login");
        assert_eq!(parsed.presence, "ONLINE");
        assert!(!parsed.is_ack);

        let ack = PrimeProtocol::build_handshake_payload("DeskUser", "desk_login", true);
        let parsed_ack = PrimeProtocol::parse_handshake_payload(&ack).unwrap();
        assert!(parsed_ack.is_ack);
    }

    #[test]
    fn test_reaction_payload() {
        let rx = PrimeProtocol::build_reaction_payload("m-1", Some("🔥"), "alice");
        let parsed = PrimeProtocol::parse_reaction_payload(&rx);
        assert_eq!(parsed.msg_id, "m-1");
        assert_eq!(parsed.reaction.as_deref(), Some("🔥"));
        assert_eq!(parsed.author_login, "alice");

        let remove = PrimeProtocol::build_reaction_payload("m-1", None, "alice");
        let parsed_remove = PrimeProtocol::parse_reaction_payload(&remove);
        assert_eq!(parsed_remove.reaction, None);
    }

    #[test]
    fn test_file_payload() {
        let file_data = b"BINARY_CONTENT_12345";
        let payload = PrimeProtocol::build_file_payload(
            "f-1",
            "document.pdf",
            file_data,
            "00:00",
            false,
            "My caption",
        );
        let header = PrimeProtocol::parse_file_header(&payload).unwrap();
        assert_eq!(header.msg_id, "f-1");
        assert_eq!(header.file_name, "document.pdf");
        assert_eq!(header.caption, "My caption");
        assert_eq!(header.file_length, file_data.len());
        assert_eq!(&payload[header.body_start_offset..], file_data);
    }

    #[test]
    fn test_photo_payload_with_caption() {
        let photo_data = b"JPEG_RAW_DATA_BYTES";
        let payload = PrimeProtocol::build_photo_payload("p-1", photo_data, "Sunset photo");
        let meta = PrimeProtocol::parse_photo_meta(&payload);
        assert_eq!(meta.msg_id, "p-1");
        assert_eq!(meta.caption, "Sunset photo");
        assert!(!meta.is_collage);
        assert_eq!(
            &payload[meta.photo_offset..meta.photo_offset + meta.photo_length],
            photo_data
        );
    }
}
