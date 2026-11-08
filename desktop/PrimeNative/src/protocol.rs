use byteorder::{BigEndian, ByteOrder};

pub struct PrimeProtocol;

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
}
