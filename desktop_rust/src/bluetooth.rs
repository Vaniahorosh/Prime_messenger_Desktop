use crate::anonymizer::{get_opaque_peer_id, normalize_mac_address};
use crate::models::BluetoothDeviceInfo;

#[cfg(windows)]
mod win_bt {
    use super::*;

    const HKEY_LOCAL_MACHINE: isize = 0x80000002u32 as i32 as isize;
    const KEY_READ: u32 = 0x20019;
    const ERROR_SUCCESS: u32 = 0;

    extern "system" {
        fn RegOpenKeyExW(
            hKey: isize,
            lpSubKey: *const u16,
            ulOptions: u32,
            samDesired: u32,
            phkResult: *mut isize,
        ) -> u32;

        fn RegEnumKeyExW(
            hKey: isize,
            dwIndex: u32,
            lpName: *mut u16,
            lpcchName: *mut u32,
            lpReserved: *mut u32,
            lpClass: *mut u16,
            lpcchClass: *mut u32,
            lpftLastWriteTime: *mut std::ffi::c_void,
        ) -> u32;

        fn RegQueryValueExW(
            hKey: isize,
            lpValueName: *const u16,
            lpReserved: *mut u32,
            lpType: *mut u32,
            lpData: *mut u8,
            lpcbData: *mut u32,
        ) -> u32;

        fn RegCloseKey(hKey: isize) -> u32;
    }

    fn to_wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    pub fn scan_paired_devices() -> Vec<BluetoothDeviceInfo> {
        let mut devices = Vec::new();
        let subkey = to_wide(r"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
        let mut hkey: isize = 0;

        unsafe {
            if RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey.as_ptr(), 0, KEY_READ, &mut hkey) != ERROR_SUCCESS {
                return devices;
            }

            let mut index = 0u32;
            let mut name_buf = [0u16; 256];

            loop {
                let mut name_len = name_buf.len() as u32;
                let status = RegEnumKeyExW(
                    hkey,
                    index,
                    name_buf.as_mut_ptr(),
                    &mut name_len,
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                );

                if status != ERROR_SUCCESS {
                    break;
                }

                let mac_str = String::from_utf16_lossy(&name_buf[..name_len as usize]);
                index += 1;

                // Open subkey for this device to read its friendly Name
                let dev_subkey = to_wide(&format!(r"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\{}", mac_str));
                let mut hdev_key: isize = 0;

                let mut friendly_name = "Bluetooth Device".to_string();
                if RegOpenKeyExW(HKEY_LOCAL_MACHINE, dev_subkey.as_ptr(), 0, KEY_READ, &mut hdev_key) == ERROR_SUCCESS {
                    let val_name = to_wide("Name");
                    let mut data_buf = [0u8; 512];
                    let mut data_len = data_buf.len() as u32;
                    let mut val_type = 0u32;

                    if RegQueryValueExW(
                        hdev_key,
                        val_name.as_ptr(),
                        std::ptr::null_mut(),
                        &mut val_type,
                        data_buf.as_mut_ptr(),
                        &mut data_len,
                    ) == ERROR_SUCCESS && data_len > 0 {
                        let raw_bytes = &data_buf[..data_len as usize];
                        // Trim null bytes
                        let clean_bytes: Vec<u8> = raw_bytes.iter().cloned().take_while(|&b| b != 0).collect();
                        if let Ok(name_str) = String::from_utf8(clean_bytes) {
                            if !name_str.trim().is_empty() {
                                friendly_name = name_str.trim().to_string();
                            }
                        }
                    }
                    RegCloseKey(hdev_key);
                }

                let normalized_mac = normalize_mac_address(&mac_str);
                let opaque_id = get_opaque_peer_id(&normalized_mac);

                devices.push(BluetoothDeviceInfo {
                    name: friendly_name,
                    mac: normalized_mac,
                    opaque_id,
                    is_connected: true,
                });
            }

            RegCloseKey(hkey);
        }

        devices
    }
}

#[cfg(not(windows))]
mod win_bt {
    use super::*;
    pub fn scan_paired_devices() -> Vec<BluetoothDeviceInfo> {
        Vec::new()
    }
}

pub fn scan_bluetooth_devices() -> Vec<BluetoothDeviceInfo> {
    win_bt::scan_paired_devices()
}
