using System.Windows;
using System.Windows.Controls;
using PcMonitor.ViewModels;

namespace PcMonitor.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    // при каждом открытии страницы подтягиваем свежие данные из базы
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && vm.RefreshCommand.CanExecute(null))
            vm.RefreshCommand.Execute(null);
    }
}
