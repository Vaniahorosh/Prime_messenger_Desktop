using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrimeDesktop.Models;

public class BrowserToolbarItemConfig : INotifyPropertyChanged
{
    private string _id = "";
    private string _name = "";
    private string _icon = "";
    private bool _isVisible = true;
    private string _placement = "left"; // "left" or "right"
    private int _order;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public string Placement
    {
        get => _placement;
        set
        {
            if (SetField(ref _placement, value))
            {
                OnPropertyChanged(nameof(IsLeft));
                OnPropertyChanged(nameof(IsRight));
                OnPropertyChanged(nameof(PlacementDisplay));
            }
        }
    }

    public int Order
    {
        get => _order;
        set => SetField(ref _order, value);
    }

    public bool IsLeft => Placement == "left";
    public bool IsRight => Placement == "right";
    public bool IsTabsLeft => Placement == "tabs_left";
    public bool IsTabsRight => Placement == "tabs_right";
    public string PlacementDisplay => Placement switch
    {
        "tabs_left" => "В строке вкладок (слева)",
        "tabs_right" => "В строке вкладок (справа)",
        "left" => "Внизу (слева)",
        "right" => "Внизу (справа)",
        _ => Placement
    };

    public static List<BrowserToolbarItemConfig> GetDefaultToolbar()
    {
        return new List<BrowserToolbarItemConfig>
        {
            new() { Id = "back", Name = "Назад", Icon = "IconArrowLeft", Placement = "left", Order = 0, IsVisible = true },
            new() { Id = "forward", Name = "Вперед", Icon = "IconArrowRight", Placement = "left", Order = 1, IsVisible = true },
            new() { Id = "refresh", Name = "Обновить", Icon = "IconRefresh", Placement = "left", Order = 2, IsVisible = true },
            new() { Id = "address_bar", Name = "Строка поиска и адреса", Icon = "IconSearch", Placement = "left", Order = 3, IsVisible = true },
            new() { Id = "desktop_mode", Name = "Режим ПК / Мобильный", Icon = "💻", Placement = "right", Order = 0, IsVisible = true },
            new() { Id = "extensions", Name = "Расширения Chrome", Icon = "IconExtension", Placement = "right", Order = 1, IsVisible = true },
            new() { Id = "external", Name = "Открыть во внешнем браузере", Icon = "IconOpenInNew", Placement = "right", Order = 2, IsVisible = true },
            new() { Id = "customize", Name = "Настройка кнопок (Карандаш)", Icon = "IconEdit", Placement = "right", Order = 3, IsVisible = true },
            new() { Id = "settings", Name = "Настройки браузера", Icon = "IconSettings", Placement = "right", Order = 4, IsVisible = true }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
