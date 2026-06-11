using System.Windows.Controls;
using System.Windows.Input;

namespace ZombieNightProtocol.App;

public sealed class JumpSlider : Slider
{
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsMouseDirectlyOverThumb(e.OriginalSource))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        var point = e.GetPosition(this);
        var ratio = Orientation == Orientation.Horizontal
            ? point.X / Math.Max(1d, ActualWidth)
            : 1d - (point.Y / Math.Max(1d, ActualHeight));
        Value = Math.Clamp(Minimum + ((Maximum - Minimum) * ratio), Minimum, Maximum);
        e.Handled = true;
    }

    private static bool IsMouseDirectlyOverThumb(object source)
    {
        if (source is not System.Windows.DependencyObject element)
        {
            return false;
        }

        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return false;
    }
}
