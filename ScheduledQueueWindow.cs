using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

public sealed class ScheduledQueueWindow : Window
{
    private readonly ScheduledMailStore _store = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _detail = new();
    private readonly Button _retry = new();
    private readonly Button _cancel = new();

    public ScheduledQueueWindow()
    {
        Title = Localizer.T("scheduledQueue");
        Width = 670; Height = 430; MinWidth = 510; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Canvas");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        WindowTheme.Attach(this);
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = Title.ToString(), FontSize = 18,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        _list.DisplayMemberPath = nameof(ScheduledMail.Summary);
        _list.SelectionChanged += (_, _) => UpdateSelection();
        Grid.SetRow(_list, 1); root.Children.Add(_list);
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.Foreground = (Brush)FindResource("Muted");
        _detail.Margin = new Thickness(0, 10, 0, 10);
        Grid.SetRow(_detail, 2); root.Children.Add(_detail);
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        _retry.Content = Localizer.T("scheduledRetry");
        _cancel.Content = Localizer.T("scheduledCancel");
        _retry.Margin = _cancel.Margin = new Thickness(0, 0, 8, 0);
        _retry.Click += (_, _) =>
        {
            if (_list.SelectedItem is ScheduledMail job) _store.Retry(job.Id);
            Refresh();
        };
        _cancel.Click += (_, _) =>
        {
            if (_list.SelectedItem is ScheduledMail job)
            {
                if (job.State == "pending") _store.TryCancelPending(job.Id);
                else if (job.State is "failed" or "interrupted") _store.SetState(job.Id, "cancelled");
            }
            Refresh();
        };
        var close = new Button { Content = Localizer.T("close") };
        close.Click += (_, _) => Close();
        actions.Children.Add(_retry); actions.Children.Add(_cancel); actions.Children.Add(close);
        Grid.SetRow(actions, 3); root.Children.Add(actions);
        Content = root;
        Refresh();
    }

    private void Refresh()
    {
        _list.ItemsSource = _store.List().Where(item => item.State != "cancelled").ToList();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var item = _list.SelectedItem as ScheduledMail;
        _retry.IsEnabled = item?.State is "failed" or "interrupted";
        _cancel.IsEnabled = item?.State is "pending" or "failed" or "interrupted";
        _detail.Text = item is null ? Localizer.T("scheduleHint") :
            item.State is "failed" or "interrupted" ?
                Localizer.T("scheduledRetryWarning") + (item.Error.Length > 0 ? "\n" + item.Error : "") :
                item.Error.Length > 0 ? item.Error : Localizer.T("scheduleHint");
    }
}
