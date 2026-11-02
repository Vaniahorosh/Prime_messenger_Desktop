using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PrimeDesktop.Models;

namespace PrimeDesktop.Services;

public class StorageService
{
    public static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrimeDesktop");

    private static readonly string SettingsFile = Path.Combine(BaseDirectory, "settings.json");
    private static readonly string ChatsFile = Path.Combine(BaseDirectory, "chats.dat");
    private static readonly string ChatsBackupFile = Path.Combine(BaseDirectory, "chats.dat.bak");
    private static readonly string ChatsTempFile = Path.Combine(BaseDirectory, "chats.dat.tmp");

    public bool HasChatsFile => File.Exists(ChatsFile) || File.Exists(ChatsBackupFile);
    public static readonly string MediaDirectory = Path.Combine(BaseDirectory, "Media");

    private static readonly SemaphoreSlim _fileLock = new(1, 1);

    public StorageService()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(MediaDirectory);
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                string json = await File.ReadAllTextAsync(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _fileLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            string tempSettings = SettingsFile + ".tmp";
            await File.WriteAllTextAsync(tempSettings, json);
            File.Move(tempSettings, SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Log($"[Storage ERROR] SaveSettingsAsync failed: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<ChatItem>> LoadChatsAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            // 1. Try reading primary file
            if (File.Exists(ChatsFile))
            {
                var list = await TryReadChatsFileAsync(ChatsFile);
                if (list != null && list.Count > 0) return list;
            }

            // 2. If primary file missing or corrupted, try backup file
            if (File.Exists(ChatsBackupFile))
            {
                App.Log("[Storage WARN] Primary chats.dat failed or empty, recovering from chats.dat.bak...");
                var backupList = await TryReadChatsFileAsync(ChatsBackupFile);
                if (backupList != null && backupList.Count > 0) return backupList;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Storage ERROR] LoadChatsAsync failed: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
        return new List<ChatItem>();
    }

    private static async Task<List<ChatItem>?> TryReadChatsFileAsync(string filePath)
    {
        try
        {
            byte[] raw = await File.ReadAllBytesAsync(filePath);
            if (raw.Length == 0) return null;
            string json;
            try
            {
                byte[] decrypted = SecurityManager.Decrypt(raw);
                json = System.Text.Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // Fallback if file was plain json
                json = System.Text.Encoding.UTF8.GetString(raw);
            }
            return JsonSerializer.Deserialize<List<ChatItem>>(json);
        }
        catch (Exception ex)
        {
            App.Log($"[Storage ERROR] Reading '{filePath}' failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Thread-safe, atomic save matching Android's AtomicFile and singleThreadExecutor architecture.
    /// Safely clones chat and message lists to prevent "Collection was modified" exceptions.
    /// </summary>
    public async Task SaveChatsAsync(IEnumerable<ChatItem> chats)
    {
        if (chats == null) return;

        // Create an in-memory snapshot before entering lock or serializing
        // to avoid "Collection was modified" when UI thread mutates ObservableCollection
        List<ChatItem> snapshot;
        try
        {
            snapshot = chats.Select(c => c.CloneSnapshot()).ToList();
        }
        catch
        {
            // If cloning on background thread collided, retry on UI dispatcher or copy safe array
            snapshot = chats.ToList();
        }

        await _fileLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
            byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(json);
            byte[] encrypted = SecurityManager.Encrypt(plaintext);

            // Write to temporary file first (Atomic write pattern)
            await File.WriteAllBytesAsync(ChatsTempFile, encrypted);

            // Create backup of previous valid file before overwriting
            if (File.Exists(ChatsFile))
            {
                try { File.Copy(ChatsFile, ChatsBackupFile, overwrite: true); } catch { }
            }

            // Atomically replace target file
            File.Move(ChatsTempFile, ChatsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Log($"[Storage ERROR] SaveChatsAsync atomic write failed: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }
}