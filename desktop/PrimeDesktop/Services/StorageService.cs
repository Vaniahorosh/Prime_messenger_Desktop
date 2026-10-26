using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PrimeDesktop.Models;

namespace PrimeDesktop.Services;

public class StorageService
{
    private static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrimeDesktop");

    private static readonly string SettingsFile = Path.Combine(BaseDirectory, "settings.json");
    private static readonly string ChatsFile = Path.Combine(BaseDirectory, "chats.dat");
    public bool HasChatsFile => File.Exists(ChatsFile);
    public static readonly string MediaDirectory = Path.Combine(BaseDirectory, "Media");

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
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsFile, json);
        }
        catch { }
    }

    public async Task<List<ChatItem>> LoadChatsAsync()
    {
        try
        {
            if (File.Exists(ChatsFile))
            {
                byte[] raw = await File.ReadAllBytesAsync(ChatsFile);
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
                return JsonSerializer.Deserialize<List<ChatItem>>(json) ?? new List<ChatItem>();
            }
        }
        catch { }
        return new List<ChatItem>();
    }

    public async Task SaveChatsAsync(IEnumerable<ChatItem> chats)
    {
        try
        {
            string json = JsonSerializer.Serialize(chats, new JsonSerializerOptions { WriteIndented = false });
            byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(json);
            byte[] encrypted = SecurityManager.Encrypt(plaintext);
            await File.WriteAllBytesAsync(ChatsFile, encrypted);
        }
        catch { }
    }
}