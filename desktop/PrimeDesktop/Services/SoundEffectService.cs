using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PrimeDesktop.Models;

namespace PrimeDesktop.Services
{
    public static class SoundEffectService
    {
        [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = true)]
        private static extern bool PlaySound(byte[]? ptrToSound, IntPtr hmod, int fdwSound);

        private const int SND_ASYNC = 0x0001;
        private const int SND_NODEFAULT = 0x0002;
        private const int SND_MEMORY = 0x0004;
        private const int Flags = SND_ASYNC | SND_MEMORY | SND_NODEFAULT;

        private static int _cachedVolume = -1;
        private static byte[]? _cachedSent;
        private static byte[]? _cachedReceived;
        private static byte[]? _cachedRecStart;
        private static byte[]? _cachedRecStop;

        private static void EnsureCached(int volumePercent)
        {
            if (_cachedVolume == volumePercent && _cachedSent != null) return;

            _cachedVolume = volumePercent;
            double vol = Math.Clamp(volumePercent / 100.0, 0.0, 1.0);

            _cachedSent = GenerateSentWav(vol);
            _cachedReceived = GenerateReceivedWav(vol);
            _cachedRecStart = GenerateRampWav(440.0, 660.0, 0.065, vol);
            _cachedRecStop = GenerateRampWav(660.0, 440.0, 0.065, vol);
        }

        public static void PlaySent(AppSettings? settings)
        {
            if (settings == null || !settings.PlayNotificationSounds || !settings.PlayOutgoingSound)
                return;

            EnsureCached(settings.SoundEffectsVolume);
            if (_cachedSent != null)
            {
                PlaySound(_cachedSent, IntPtr.Zero, Flags);
            }
        }

        public static void PlayReceived(AppSettings? settings)
        {
            if (settings == null || !settings.PlayNotificationSounds || !settings.PlayIncomingSound)
                return;

            EnsureCached(settings.SoundEffectsVolume);
            if (_cachedReceived != null)
            {
                PlaySound(_cachedReceived, IntPtr.Zero, Flags);
            }
        }

        public static void PlayRecordStart(AppSettings? settings)
        {
            if (settings == null || !settings.PlayNotificationSounds || !settings.PlayVoiceRecordSound)
                return;

            EnsureCached(settings.SoundEffectsVolume);
            if (_cachedRecStart != null)
            {
                PlaySound(_cachedRecStart, IntPtr.Zero, Flags);
            }
        }

        public static void PlayRecordStop(AppSettings? settings)
        {
            if (settings == null || !settings.PlayNotificationSounds || !settings.PlayVoiceRecordSound)
                return;

            EnsureCached(settings.SoundEffectsVolume);
            if (_cachedRecStop != null)
            {
                PlaySound(_cachedRecStop, IntPtr.Zero, Flags);
            }
        }

        public static void PlayTest(AppSettings? settings)
        {
            int vol = settings?.SoundEffectsVolume ?? 80;
            EnsureCached(vol);
            if (_cachedReceived != null)
            {
                PlaySound(_cachedReceived, IntPtr.Zero, Flags);
            }
        }

        private static byte[] GenerateSentWav(double volume)
        {
            const int sampleRate = 44100;
            int numSamples = (int)(sampleRate * 0.085); // 85ms
            short[] samples = new short[numSamples];
            double phase = 0;

            for (int i = 0; i < numSamples; i++)
            {
                double progress = (double)i / numSamples;
                double freq = 560.0 + (360.0 * Math.Pow(progress, 0.7));
                phase += 2.0 * Math.PI * freq / sampleRate;

                double env = Math.Sin(Math.PI * Math.Pow(1.0 - progress, 1.8));
                double s = Math.Sin(phase) * 0.85 + Math.Sin(phase * 2.0) * 0.15;
                samples[i] = (short)(s * env * volume * 32767);
            }

            return PackPcmWav(samples, sampleRate);
        }

        private static byte[] GenerateReceivedWav(double volume)
        {
            const int sampleRate = 44100;
            int numSamples = (int)(sampleRate * 0.28); // 280ms
            short[] samples = new short[numSamples];
            double phase1 = 0;
            double phase2 = 0;

            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                double s = 0;

                // First chime tone (C6: 1046.5Hz)
                if (t < 0.16)
                {
                    double env1 = Math.Exp(-t * 22.0);
                    phase1 += 2.0 * Math.PI * 1046.5 / sampleRate;
                    s += Math.Sin(phase1) * env1 * 0.65;
                }

                // Second chime tone (G6: 1567.98Hz), begins at 75ms
                if (t >= 0.075)
                {
                    double t2 = t - 0.075;
                    double env2 = Math.Exp(-t2 * 14.0);
                    phase2 += 2.0 * Math.PI * 1567.98 / sampleRate;
                    s += (Math.Sin(phase2) * 0.85 + Math.Sin(phase2 * 2.0) * 0.15) * env2;
                }

                samples[i] = (short)(Math.Clamp(s, -1.0, 1.0) * volume * 32767);
            }

            return PackPcmWav(samples, sampleRate);
        }

        private static byte[] GenerateRampWav(double startFreq, double endFreq, double durationSeconds, double volume)
        {
            const int sampleRate = 44100;
            int numSamples = (int)(sampleRate * durationSeconds);
            short[] samples = new short[numSamples];
            double phase = 0;

            for (int i = 0; i < numSamples; i++)
            {
                double progress = (double)i / numSamples;
                double freq = startFreq + (endFreq - startFreq) * progress;
                phase += 2.0 * Math.PI * freq / sampleRate;

                double env = Math.Sin(Math.PI * progress);
                double s = Math.Sin(phase);
                samples[i] = (short)(s * env * volume * 32767);
            }

            return PackPcmWav(samples, sampleRate);
        }

        private static byte[] PackPcmWav(short[] samples, int sampleRate)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            int subChunk2Size = samples.Length * 2;
            int chunkSize = 36 + subChunk2Size;

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(chunkSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // subchunk1size (16 for PCM)
            bw.Write((short)1); // audio format (1 = PCM)
            bw.Write((short)1); // num channels (1 = mono)
            bw.Write(sampleRate); // sample rate
            bw.Write(sampleRate * 2); // byte rate
            bw.Write((short)2); // block align
            bw.Write((short)16); // bits per sample
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(subChunk2Size);

            for (int i = 0; i < samples.Length; i++)
            {
                bw.Write(samples[i]);
            }

            return ms.ToArray();
        }
    }
}
