#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod crypto;
mod models;
mod network;
mod protocol;
mod storage;

use commands::AppState;
use network::NetworkManager;
use storage::Storage;
use std::sync::Arc;

fn main() {
    let rt = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .expect("Failed to create Tokio runtime");
    let handle = rt.handle().clone();
    std::mem::forget(rt);

    let app_data = dirs::data_dir()
        .unwrap_or_else(|| std::path::PathBuf::from("."))
        .join("PrimeMessenger");
    let _ = std::fs::create_dir_all(&app_data);

    let db_path = app_data.join("prime_v2.db");
    let media_dir = app_data.join("Media");

    let storage = Storage::new(db_path).expect("Failed to initialize SQLite storage");
    let settings = storage.get_settings().unwrap_or_default();

    let network = Arc::new(NetworkManager::new(handle, storage.clone(), media_dir));

    // Start background network workers
    network.start_udp_responder(settings.my_display_name, settings.my_username);
    network.start_tcp_server();

    let app_state = AppState {
        storage,
        network,
    };

    tauri::Builder::default()
        .manage(app_state)
        .invoke_handler(tauri::generate_handler![
            commands::get_chats,
            commands::get_messages,
            commands::send_message,
            commands::send_file,
            commands::send_reaction,
            commands::delete_message,
            commands::delete_chat,
            commands::get_settings,
            commands::save_settings,
            commands::scan_network,
            commands::connect_peer,
        ])
        .run(tauri::generate_context!())
        .expect("error while running Prime Messenger");
}
