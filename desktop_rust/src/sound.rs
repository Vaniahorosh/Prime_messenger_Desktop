use std::f64::consts::PI;
use std::sync::atomic::{AtomicBool, Ordering};

static SOUND_ENABLED: AtomicBool = AtomicBool::new(true);

pub fn set_sound_enabled(enabled: bool) {
    SOUND_ENABLED.store(enabled, Ordering::Relaxed);
}

pub fn is_sound_enabled() -> bool {
    SOUND_ENABLED.load(Ordering::Relaxed)
}

#[derive(Debug, Clone, Copy)]
pub enum SoundEffect {
    Sent,
    Received,
    RecordStart,
    RecordStop,
    Pop,
}

#[cfg(windows)]
mod win_sound {
    use super::*;

    const SND_ASYNC: u32 = 0x0001;
    const SND_NODEFAULT: u32 = 0x0002;
    const SND_MEMORY: u32 = 0x0004;

    extern "system" {
        fn PlaySoundW(pszSound: *const u16, hmod: *mut std::ffi::c_void, fdwSound: u32) -> i32;
    }

    pub fn play_wav_bytes(wav: &[u8]) {
        if !is_sound_enabled() || wav.is_empty() {
            return;
        }

        let wav_vec = wav.to_vec();
        std::thread::spawn(move || unsafe {
            let ptr = wav_vec.as_ptr() as *const u16;
            PlaySoundW(ptr, std::ptr::null_mut(), SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            // Sleep briefly to allow winmm async playback to initialize
            std::thread::sleep(std::time::Duration::from_millis(500));
        });
    }
}

#[cfg(not(windows))]
mod win_sound {
    pub fn play_wav_bytes(_wav: &[u8]) {}
}

pub fn play_sound(effect: SoundEffect, volume: f64) {
    if !is_sound_enabled() {
        return;
    }

    let vol = volume.clamp(0.0, 1.0);
    let wav = match effect {
        SoundEffect::Sent => generate_sent_wav(vol),
        SoundEffect::Received => generate_received_wav(vol),
        SoundEffect::RecordStart => generate_ramp_wav(440.0, 660.0, 0.065, vol),
        SoundEffect::RecordStop => generate_ramp_wav(660.0, 440.0, 0.065, vol),
        SoundEffect::Pop => generate_pop_wav(vol),
    };

    win_sound::play_wav_bytes(&wav);
}

// ========================================================
// In-Memory Procedural WAV Audio Synthesizers
// ========================================================

fn write_wav_header(sample_count: usize, sample_rate: u32) -> Vec<u8> {
    let subchunk2_size = (sample_count * 2) as u32; // 16-bit mono = 2 bytes per sample
    let chunk_size = 36 + subchunk2_size;
    let byte_rate = sample_rate * 2;
    let block_align = 2u16;
    let bits_per_sample = 16u16;

    let mut buf = Vec::with_capacity(44 + subchunk2_size as usize);
    buf.extend_from_slice(b"RIFF");
    buf.extend_from_slice(&chunk_size.to_le_bytes());
    buf.extend_from_slice(b"WAVE");
    buf.extend_from_slice(b"fmt ");
    buf.extend_from_slice(&16u32.to_le_bytes()); // Subchunk1Size (16 for PCM)
    buf.extend_from_slice(&1u16.to_le_bytes());  // AudioFormat (1 for PCM)
    buf.extend_from_slice(&1u16.to_le_bytes());  // NumChannels (1 = Mono)
    buf.extend_from_slice(&sample_rate.to_le_bytes());
    buf.extend_from_slice(&byte_rate.to_le_bytes());
    buf.extend_from_slice(&block_align.to_le_bytes());
    buf.extend_from_slice(&bits_per_sample.to_le_bytes());
    buf.extend_from_slice(b"data");
    buf.extend_from_slice(&subchunk2_size.to_le_bytes());
    buf
}

fn generate_sent_wav(volume: f64) -> Vec<u8> {
    let sample_rate = 44100u32;
    let duration = 0.12; // 120ms gentle bubble pop
    let sample_count = (sample_rate as f64 * duration) as usize;
    let mut header = write_wav_header(sample_count, sample_rate);

    for i in 0..sample_count {
        let t = i as f64 / sample_rate as f64;
        let progress = t / duration;

        // Smooth pitch glide 520Hz -> 780Hz
        let freq = 520.0 + 260.0 * progress;
        let envelope = (1.0 - progress).powi(2) * (t / (duration * 0.1)).min(1.0);
        let sample = (2.0 * PI * freq * t).sin() * envelope * volume * 0.45;
        let pcm = (sample.clamp(-1.0, 1.0) * i16::MAX as f64) as i16;
        header.extend_from_slice(&pcm.to_le_bytes());
    }

    header
}

fn generate_received_wav(volume: f64) -> Vec<u8> {
    let sample_rate = 44100u32;
    let duration = 0.22; // 220ms double chime
    let sample_count = (sample_rate as f64 * duration) as usize;
    let mut header = write_wav_header(sample_count, sample_rate);

    for i in 0..sample_count {
        let t = i as f64 / sample_rate as f64;
        let mut sample = 0.0;

        // Note 1: 659.25 Hz (E5)
        if t < 0.14 {
            let t1 = t;
            let env1 = (1.0 - (t1 / 0.14)).powi(2) * (t1 / 0.015).min(1.0);
            sample += (2.0 * PI * 659.25 * t1).sin() * env1;
        }

        // Note 2: 880.00 Hz (A5)
        if t > 0.06 {
            let t2 = t - 0.06;
            let env2 = (1.0 - (t2 / 0.16)).powi(2) * (t2 / 0.015).min(1.0);
            sample += (2.0 * PI * 880.00 * t2).sin() * env2;
        }

        let pcm = ((sample * 0.35 * volume).clamp(-1.0, 1.0) * i16::MAX as f64) as i16;
        header.extend_from_slice(&pcm.to_le_bytes());
    }

    header
}

fn generate_ramp_wav(start_f: f64, end_f: f64, duration: f64, volume: f64) -> Vec<u8> {
    let sample_rate = 44100u32;
    let sample_count = (sample_rate as f64 * duration) as usize;
    let mut header = write_wav_header(sample_count, sample_rate);

    for i in 0..sample_count {
        let t = i as f64 / sample_rate as f64;
        let progress = t / duration;
        let freq = start_f + (end_f - start_f) * progress;
        let envelope = (1.0 - progress) * (t / (duration * 0.15)).min(1.0);
        let sample = (2.0 * PI * freq * t).sin() * envelope * volume * 0.4;
        let pcm = (sample.clamp(-1.0, 1.0) * i16::MAX as f64) as i16;
        header.extend_from_slice(&pcm.to_le_bytes());
    }

    header
}

fn generate_pop_wav(volume: f64) -> Vec<u8> {
    let sample_rate = 44100u32;
    let duration = 0.06;
    let sample_count = (sample_rate as f64 * duration) as usize;
    let mut header = write_wav_header(sample_count, sample_rate);

    for i in 0..sample_count {
        let t = i as f64 / sample_rate as f64;
        let progress = t / duration;
        let freq = 750.0 - 400.0 * progress;
        let envelope = (1.0 - progress).powi(3);
        let sample = (2.0 * PI * freq * t).sin() * envelope * volume * 0.35;
        let pcm = (sample.clamp(-1.0, 1.0) * i16::MAX as f64) as i16;
        header.extend_from_slice(&pcm.to_le_bytes());
    }

    header
}
