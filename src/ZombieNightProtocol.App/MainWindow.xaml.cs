using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ZombieNightProtocol.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private GameSessionViewModel? _observedGame;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyButtonClick));
    }

    public void ApplyUiScale(int percentage)
    {
        var scale = Math.Clamp(percentage / 100d, 0.9, 1.1);
        ScreenRoot.RenderTransformOrigin = new Point(0.5, 0.5);
        ScreenRoot.RenderTransform = new ScaleTransform(scale, scale);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCharacter) && _viewModel.SelectedCharacter is not null)
        {
            if (_viewModel.IsCharacterSelection)
            {
                _viewModel.PlayUiClick();
            }
            CharacterDetailPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(100)));
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.Game))
        {
            if (_observedGame is not null)
            {
                _observedGame.PropertyChanged -= OnGamePropertyChanged;
            }
            _observedGame = _viewModel.Game;
            if (_observedGame is not null)
            {
                _observedGame.PropertyChanged += OnGamePropertyChanged;
            }
        }

        if (e.PropertyName != nameof(MainViewModel.Screen))
        {
            return;
        }

        if (_viewModel.IsIntro)
        {
            IntroMedia.Position = TimeSpan.Zero;
            IntroMedia.Play();
        }
        else
        {
            IntroMedia.Stop();
        }

        var animation = new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ScreenRoot.BeginAnimation(OpacityProperty, animation);
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_observedGame is null)
        {
            return;
        }
        if (e.PropertyName == nameof(GameSessionViewModel.NarrativeFadeToken))
        {
            NarrativeText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(850)));
        }
        else if (e.PropertyName == nameof(GameSessionViewModel.ShakeRequestToken) && _observedGame.ShakeIntensity > 0)
        {
            var transform = new TranslateTransform();
            GameScreen.RenderTransform = transform;
            var amplitude = _observedGame.ShakeIntensity;
            var x = new DoubleAnimationUsingKeyFrames();
            var y = new DoubleAnimationUsingKeyFrames();
            for (var index = 0; index < 8; index++)
            {
                var time = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(index * 42));
                x.KeyFrames.Add(new LinearDoubleKeyFrame(index % 2 == 0 ? amplitude : -amplitude, time));
                y.KeyFrames.Add(new LinearDoubleKeyFrame(index % 3 == 0 ? -amplitude * 0.55 : amplitude * 0.4, time));
            }
            x.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350))));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350))));
            transform.BeginAnimation(TranslateTransform.XProperty, x);
            transform.BeginAnimation(TranslateTransform.YProperty, y);
        }
    }

    private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Dispatcher.BeginInvoke(() => _viewModel.SettingsChanged(previewAudio: true));
    }

    private void SettingsControl_Changed(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => _viewModel.SettingsChanged());
    }

    private void SettingsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Dispatcher.BeginInvoke(() => _viewModel.SettingsChanged());
    }

    private void SettingsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => _viewModel.SettingsChanged());
    }

    private void OnAnyButtonClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsLoading && !_viewModel.IsIntro)
        {
            _viewModel.PlayUiClick();
        }
    }

    private void Narrative_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.Game?.ShowInstantlyCommand.Execute(null);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_viewModel.IsConfirmingCharacter)
        {
            _viewModel.CancelCharacterConfirmationCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.Game is not { } game)
        {
            return;
        }

        if (game.IsSavePanelOpen)
        {
            game.CloseSavePanelCommand.Execute(null);
            e.Handled = true;
        }
        else if (game.IsMessagePanelOpen)
        {
            game.ToggleMessagePanelCommand.Execute(null);
            e.Handled = true;
        }
        else if (game.IsInventoryUsePanelOpen)
        {
            game.ToggleInventoryUsePanelCommand.Execute(null);
            e.Handled = true;
        }
        else if (game.IsStatusPanelOpen)
        {
            game.ToggleStatusPanelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SelectionPreview_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void IntroMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _viewModel.SkipIntroCommand.Execute(null);
    }

    private void IntroMedia_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _viewModel.SkipIntroCommand.Execute(null);
    }
}
