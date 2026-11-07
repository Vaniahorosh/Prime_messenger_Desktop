#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod anonymizer;
mod bluetooth;
mod commands;
mod crypto;
mod link_preview;
mod models;
mod network;
mod protocol;
mod sound;
mod storage;

use commands::AppState;
use network::NetworkManager;
use storage::Storage;
use std::sync::Arc;
use tauri::tray::{TrayIconBuilder, MouseButton, MouseButtonState, TrayIconEvent};
use tauri::menu::{Menu, MenuItem};
use tauri::Manager;

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

    sound::set_sound_enabled(settings.sound_enabled);

    let network = Arc::new(NetworkManager::new(handle, storage.clone(), media_dir.clone()));

    // Start background network workers
    network.start_udp_responder(settings.my_display_name, settings.my_username);
    network.start_tcp_server();

    let app_state = AppState {
        storage,
        network: network.clone(),
        media_dir,
    };

    tauri::Builder::default()
        .manage(app_state)
        .setup(move |app| {
            let app_handle = app.handle().clone();
            network.set_app_handle(app_handle.clone());

            // Build System Tray
            let quit_item = MenuItem::with_id(app, "quit", "Выход из Prime", true, None::<&str>)?;
            let show_item = MenuItem::with_id(app, "show", "Открыть Prime Messenger", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_item, &quit_item])?;

            let _tray = TrayIconBuilder::new()
                .menu(&menu)
                .tooltip("Prime Messenger Desktop (Rust Native)")
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" => {
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                    "quit" => {
                        app.exit(0);
                    }
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        let app = tray.app_handle();
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                })
                .build(app)?;

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::get_chats,
            commands::get_messages,
            commands::send_message,
            commands::edit_message,
            commands::delete_message,
            commands::delete_chat,
            commands::send_file,
            commands::send_reaction,
            commands::send_typing,
            commands::mark_chat_read,
            commands::get_settings,
            commands::save_settings,
            commands::scan_network,
            commands::connect_peer,
            commands::play_sound_effect,
            commands::fetch_link_preview,
            commands::set_screenshot_protection,
            commands::scan_bluetooth_devices,
            commands::send_voice_recording,
            commands::send_edited_photo,
            commands::get_browser_history,
            commands::add_browser_history,
            commands::clear_browser_history,
            commands::get_bookmarks,
            commands::add_bookmark,
            commands::delete_bookmark,
            commands::verify_pin,
            commands::set_pin,
            commands::pick_screen_color,
        ])
        .run(tauri::generate_context!())
        .expect("error while running Prime Messenger");
}
