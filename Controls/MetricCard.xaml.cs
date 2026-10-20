using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PcMonitor.Controls;

/// <summary>Карточка показателя. DataContext — MetricCardViewModel; иконка и акцентный цвет задаются снаружи.</summary>
public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(MetricCard));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(MetricCard));

    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public MetricCard() => InitializeComponent();
}
