use crate::models::LinkPreviewData;
use regex::Regex;
use std::path::PathBuf;
use std::sync::LazyLock;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;

static URL_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?i)https?://[^\s<>"'{}|\\^`]+"#).unwrap()
});

static OG_TITLE_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?i)<meta[^>]+property=["'](?:og:title|twitter:title)["'][^>]+content=["']([^"']+)["']|<meta[^>]+content=["']([^"']+)["'][^>]+property=["'](?:og:title|twitter:title)["']"#).unwrap()
});

static OG_DESC_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?i)<meta[^>]+property=["'](?:og:description|twitter:description|description)["'][^>]+content=["']([^"']+)["']|<meta[^>]+content=["']([^"']+)["'][^>]+property=["'](?:og:description|twitter:description|description)["']"#).unwrap()
});

static OG_IMAGE_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?i)<meta[^>]+property=["'](?:og:image|twitter:image)["'][^>]+content=["']([^"']+)["']|<meta[^>]+content=["']([^"']+)["'][^>]+property=["'](?:og:image|twitter:image)["']"#).unwrap()
});

static TITLE_REGEX: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r#"(?i)<title[^>]*>([^<]+)</title>"#).unwrap()
});

/// Extracts the first URL found in a text message.
pub fn extract_first_url(text: &str) -> Option<String> {
    URL_REGEX.find(text).map(|m| m.as_str().to_string())
}

/// Fetches OpenGraph preview data for the specified URL.
pub async fn fetch_preview(raw_url: &str, cache_dir: &PathBuf) -> Option<LinkPreviewData> {
    let url_str = raw_url.trim();
    if url_str.is_empty() {
        return None;
    }

    let _ = std::fs::create_dir_all(cache_dir);

    // Parse domain
    let without_proto = url_str
        .strip_prefix("https://")
        .or_else(|| url_str.strip_prefix("http://"))
        .unwrap_or(url_str);
    let domain = without_proto
        .split('/')
        .next()
        .unwrap_or(url_str)
        .split(':')
        .next()
        .unwrap_or(url_str)
        .to_string();

    // Fast HTTP GET implementation
    let is_https = url_str.starts_with("https://");
    let port = if is_https { 443 } else { 80 };
    let host = domain.clone();
    let path = without_proto.find('/').map(|i| &without_proto[i..]).unwrap_or("/");

    let html_body = fetch_http_body(&host, port, path, is_https).await.unwrap_or_default();
    if html_body.is_empty() {
        return Some(LinkPreviewData {
            url: url_str.to_string(),
            domain,
            title: url_str.to_string(),
            description: String::new(),
            image_url: None,
            local_image_path: None,
        });
    }

    // Extract title
    let mut title = String::new();
    if let Some(caps) = OG_TITLE_REGEX.captures(&html_body) {
        title = caps.get(1).or_else(|| caps.get(2)).map_or("", |m| m.as_str()).to_string();
    }
    if title.is_empty() {
        if let Some(caps) = TITLE_REGEX.captures(&html_body) {
            title = caps.get(1).map_or("", |m| m.as_str()).trim().to_string();
        }
    }
    if title.is_empty() {
        title = domain.clone();
    }

    // Extract description
    let mut description = String::new();
    if let Some(caps) = OG_DESC_REGEX.captures(&html_body) {
        description = caps.get(1).or_else(|| caps.get(2)).map_or("", |m| m.as_str()).to_string();
    }

    // Extract image url
    let mut image_url = None;
    if let Some(caps) = OG_IMAGE_REGEX.captures(&html_body) {
        let img = caps.get(1).or_else(|| caps.get(2)).map_or("", |m| m.as_str()).to_string();
        if !img.is_empty() {
            image_url = Some(img);
        }
    }

    Some(LinkPreviewData {
        url: url_str.to_string(),
        domain,
        title,
        description,
        image_url,
        local_image_path: None,
    })
}

async fn fetch_http_body(host: &str, port: u16, path: &str, _is_https: bool) -> Option<String> {
    let addr = format!("{}:{}", host, port);
    let mut stream = tokio::time::timeout(
        std::time::Duration::from_millis(2500),
        TcpStream::connect(&addr),
    )
    .await
    .ok()?
    .ok()?;

    let request = format!(
        "GET {} HTTP/1.1\r\nHost: {}\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) PrimeDesktop/2.0\r\nConnection: close\r\nAccept: text/html\r\n\r\n",
        path, host
    );

    stream.write_all(request.as_bytes()).await.ok()?;

    let mut response = Vec::with_capacity(32768);
    let mut buf = [0u8; 4096];
    let mut total_read = 0;

    while let Ok(n) = stream.read(&mut buf).await {
        if n == 0 || total_read > 65536 {
            break;
        }
        response.extend_from_slice(&buf[..n]);
        total_read += n;
    }

    let text = String::from_utf8_lossy(&response);
    if let Some(idx) = text.find("\r\n\r\n") {
        Some(text[idx + 4..].to_string())
    } else {
        Some(text.to_string())
    }
}
