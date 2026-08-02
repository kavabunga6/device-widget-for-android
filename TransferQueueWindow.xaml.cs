using System.Windows;
using AndroidWidget.Presentation.Transfers;

namespace AndroidWidget;

public partial class TransferQueueWindow : Window
{
    private readonly TransferQueueService _queue;

    public TransferQueueWindow(TransferQueueService queue)
    {
        _queue = queue;
        InitializeComponent();
        _queue.Changed += QueueChanged;
        Closed += (_, _) => _queue.Changed -= QueueChanged;
        Refresh();
    }

    private void QueueChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var jobs = _queue.Snapshot.Select(JobViewModel.From).ToList();
        JobsList.ItemsSource = jobs;
        EmptyText.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: JobViewModel job })
            _queue.Cancel(job.Id);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record JobViewModel(Guid Id, string Name, string Detail, double ProgressPercent,
        bool IsIndeterminate, bool CanCancel)
    {
        public static JobViewModel From(TransferJobSnapshot job)
        {
            var kind = job.Kind switch
            {
                TransferJobKind.Download => "На компьютер",
                TransferJobKind.InstallApk => "Установка APK",
                _ => "На телефон"
            };
            var state = job.State switch
            {
                TransferJobState.Queued => "в очереди",
                TransferJobState.Running => job.Message,
                TransferJobState.Completed => "готово",
                TransferJobState.Cancelled => "отменено",
                _ => job.Message
            };
            return new JobViewModel(job.Id, job.Name, $"{kind} · {state}",
                (job.Progress ?? 0) * 100, job.State == TransferJobState.Running && job.Progress is null,
                job.CanCancel);
        }
    }
}
