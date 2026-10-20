using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PcMonitor.Models;
using PcMonitor.Services;

namespace PcMonitor.ViewModels;

public sealed class ComponentViewModel
{
    public ComponentViewModel(ComponentInfo info)
    {
        Title = info.Title;
        Rows = info.Rows;
        Icon = Application.Current.TryFindResource(info.IconKey) as Geometry ?? Geometry.Empty;
    }
    public string Title { get; }
    public Geometry Icon { get; }
    public IReadOnlyList<KeyValuePair<string, string>> Rows { get; }
}

public sealed partial class SystemViewModel : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<ComponentViewModel> _components = Array.Empty<ComponentViewModel>();
    [ObservableProperty] private bool _isLoading = true;

    public bool IsDemo { get; }

    public SystemViewModel(IMetricsProvider provider)
    {
        IsDemo = provider.IsDemo;
        _ = LoadAsync(provider);
    }

    /// <summary>Сведения об оборудовании собираются в фоне (WMI медленный), UI при этом не блокируется.</summary>
    private async Task LoadAsync(IMetricsProvider provider)
    {
        IReadOnlyList<ComponentInfo> infos;
        try
        {
            infos = await Task.Run(() => provider.GetComponents());
        }
        catch
        {
            infos = Array.Empty<ComponentInfo>();
        }

        // ресурсы (иконки) читаем уже в UI-потоке
        Components = infos.Select(i => new ComponentViewModel(i)).ToList();
        IsLoading = false;
    }
}
