using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using LicoreTestApp.Models;

namespace LicoreTestApp.ViewModels;

/// <summary>
/// Returns the first non-null, non-empty string from a MultiBinding.
/// Used in XAML to render <c>ReasonMessage ?? Detail</c> in the log grid.
/// </summary>
public sealed class FirstNonNullConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.OfType<string>().FirstOrDefault(s => s.Length > 0) ?? string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// ViewModel for <c>Views/MainWindow</c>. Provides observable state and commands for the UI shell.
/// Contains no licore.dll call logic — that belongs to future hitos.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // ── Observable properties ─────────────────────────────────────────────────

    /// <summary>Append-only log of API call results displayed in the ListView.</summary>
    public ObservableCollection<TestResult> Results { get; } = new();

    private bool _isBusy;

    /// <summary>
    /// When <see langword="true"/>, a native call is in progress and the API buttons are disabled.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    /// <summary>Inverse of <see cref="IsBusy"/>; bound to <c>IsEnabled</c> on the API buttons.</summary>
    public bool IsNotBusy => !_isBusy;

    private string _statusMessage = "Listo.";

    /// <summary>One-line status displayed in the window footer StatusBar.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Clears all entries from <see cref="Results"/> and resets the status line.</summary>
    public ICommand ClearLogCommand { get; }

    private readonly RelayCommand _copyLogCmd;

    /// <summary>
    /// Copies all log entries to the system clipboard as plain text.
    /// Each line format: <c>[HH:mm:ss] FunctionName | ApiResult | ReasonCode | Detail</c>
    /// </summary>
    public ICommand CopyLogCommand => _copyLogCmd;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel()
    {
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        _copyLogCmd     = new RelayCommand(_ => CopyLog(), _ => Results.Count > 0);

        Results.CollectionChanged += OnResultsChanged;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends <paramref name="result"/> to <see cref="Results"/> and updates <see cref="StatusMessage"/>.
    /// Must be called on the UI thread.
    /// </summary>
    public void Log(TestResult result)
    {
        Results.Add(result);
        StatusMessage = $"{result.Timestamp:HH:mm:ss}  {result.FunctionName}: {result.ApiResult}" +
                        (result.ReasonCode.HasValue ? $"  ({result.ReasonCode})" : string.Empty);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ClearLog()
    {
        Results.Clear();
        StatusMessage = "Log limpiado.";
    }

    private void CopyLog()
    {
        if (Results.Count == 0) return;

        var lines = Results.Select(r =>
            $"[{r.Timestamp:HH:mm:ss}] {r.FunctionName} | {r.ApiResult} | " +
            $"{r.ReasonCode?.ToString() ?? "-"} | {r.ReasonMessage ?? r.Detail ?? string.Empty}");

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        StatusMessage = $"Log copiado ({Results.Count} entradas).";
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _copyLogCmd.RaiseCanExecuteChanged();

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── RelayCommand (nested) ─────────────────────────────────────────────────

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter)     => _execute(parameter);

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
