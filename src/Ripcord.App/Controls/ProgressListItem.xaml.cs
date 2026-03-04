#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ripcord.App.Controls
{
    public sealed partial class ProgressListItem : UserControl
    {
        public ProgressListItem() => InitializeComponent();

        public string FilePath { get => (string)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(ProgressListItem), new PropertyMetadata(""));

        public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(string), typeof(ProgressListItem), new PropertyMetadata(""));

        public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
        public static readonly DependencyProperty PercentProperty =
            DependencyProperty.Register(nameof(Percent), typeof(double), typeof(ProgressListItem), new PropertyMetadata(0d));

        public long BytesProcessed { get => (long)GetValue(BytesProcessedProperty); set => SetValue(BytesProcessedProperty, value); }
        public static readonly DependencyProperty BytesProcessedProperty =
            DependencyProperty.Register(nameof(BytesProcessed), typeof(long), typeof(ProgressListItem), new PropertyMetadata(0L));

        public long BytesTotal { get => (long)GetValue(BytesTotalProperty); set => SetValue(BytesTotalProperty, value); }
        public static readonly DependencyProperty BytesTotalProperty =
            DependencyProperty.Register(nameof(BytesTotal), typeof(long), typeof(ProgressListItem), new PropertyMetadata(0L));
    }
}
