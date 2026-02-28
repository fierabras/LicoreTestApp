using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using LicoreTestApp.Interop;
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

    private int _selectedReasonCode;

    /// <summary>Reason code to query via <c>lc_reason_message</c>; default 0 (LC_OK).</summary>
    public int SelectedReasonCode
    {
        get => _selectedReasonCode;
        set { if (_selectedReasonCode == value) return; _selectedReasonCode = value; OnPropertyChanged(); }
    }

    private string _productName = "GENERADOR_NOTAS";

    /// <summary>Product identifier passed to lc_validate_full / lc_validate_cached.</summary>
    public string ProductName
    {
        get => _productName;
        set { if (_productName == value) return; _productName = value; OnPropertyChanged(); }
    }

    private string _productVersion = "2.0.0";

    /// <summary>Product version passed to lc_validate_full / lc_validate_cached.</summary>
    public string ProductVersion
    {
        get => _productVersion;
        set { if (_productVersion == value) return; _productVersion = value; OnPropertyChanged(); }
    }

    private string _testFingerprint = string.Empty;

    /// <summary>
    /// When non-empty, set as <c>LICORE_TEST_FINGERPRINT</c> env var before validation calls.
    /// </summary>
    public string TestFingerprint
    {
        get => _testFingerprint;
        set { if (_testFingerprint == value) return; _testFingerprint = value; OnPropertyChanged(); }
    }

    private string _testToday = string.Empty;

    /// <summary>
    /// When non-empty, set as <c>LICORE_TEST_TODAY</c> env var before validation calls.
    /// </summary>
    public string TestToday
    {
        get => _testToday;
        set { if (_testToday == value) return; _testToday = value; OnPropertyChanged(); }
    }

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

    /// <summary>Calls lc_ping() and lc_version() and logs both results in one row.</summary>
    public ICommand PingCommand { get; }

    /// <summary>Calls lc_reason_message(<see cref="SelectedReasonCode"/>) and logs the result.</summary>
    public ICommand ReasonMessageCommand { get; }

    /// <summary>
    /// Validates <see cref="ProductName"/>/<see cref="ProductVersion"/> via lc_validate_full.
    /// Applies optional test env vars before calling.
    /// </summary>
    public ICommand ValidateFullCommand { get; }

    /// <summary>
    /// Validates <see cref="ProductName"/>/<see cref="ProductVersion"/> via lc_validate_cached.
    /// Applies optional test env vars before calling.
    /// </summary>
    public ICommand ValidateCachedCommand { get; }

    private readonly RelayCommand _copyLogCmd;

    /// <summary>
    /// Copies all log entries to the system clipboard as plain text.
    /// Each line format: <c>[HH:mm:ss] FunctionName | ApiResult | ReasonCode | Detail</c>
    /// </summary>
    public ICommand CopyLogCommand => _copyLogCmd;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel()
    {
        ClearLogCommand       = new RelayCommand(_ => ClearLog());
        PingCommand           = new RelayCommand(_ => ExecutePing());
        ReasonMessageCommand  = new RelayCommand(_ => ExecuteReasonMessage());
        ValidateFullCommand   = new RelayCommand(_ => ExecuteValidate(cached: false));
        ValidateCachedCommand = new RelayCommand(_ => ExecuteValidate(cached: true));
        _copyLogCmd           = new RelayCommand(_ => CopyLog(), _ => Results.Count > 0);

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

    // ── H3 command implementations ────────────────────────────────────────────

    private void ExecutePing()
    {
        IsBusy = true;
        try
        {
            LicoreApi.LcResult result = LicoreApi.Ping();
            string version = LicoreApi.GetVersion();
            Log(new TestResult
            {
                FunctionName = "lc_ping/lc_version",
                ApiResult    = result,
                Detail       = $"version={version}",
                Timestamp    = DateTime.Now,
            });
        }
        catch (SEHException ex)
        {
            Log(new TestResult
            {
                FunctionName = "lc_ping/lc_version",
                ApiResult    = LicoreApi.LcResult.InternalError,
                Detail       = $"SEHException: {ex.Message}",
                Timestamp    = DateTime.Now,
            });
        }
        catch (Exception ex)
        {
            Log(new TestResult
            {
                FunctionName = "lc_ping/lc_version",
                ApiResult    = LicoreApi.LcResult.InternalError,
                Detail       = ex.Message,
                Timestamp    = DateTime.Now,
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteReasonMessage()
    {
        IsBusy = true;
        try
        {
            string? msg      = LicoreApi.GetReasonMessage(_selectedReasonCode);
            LicoreApi.LcResult lcResult = msg is not null
                ? LicoreApi.LcResult.Ok
                : LicoreApi.LcResult.InvalidArgument;
            Log(new TestResult
            {
                FunctionName  = "lc_reason_message",
                ApiResult     = lcResult,
                ReasonCode    = (LicoreApi.LcReason)_selectedReasonCode,
                ReasonMessage = msg,
                Timestamp     = DateTime.Now,
            });
        }
        catch (SEHException ex)
        {
            Log(new TestResult
            {
                FunctionName = "lc_reason_message",
                ApiResult    = LicoreApi.LcResult.InternalError,
                Detail       = $"SEHException: {ex.Message}",
                Timestamp    = DateTime.Now,
            });
        }
        catch (Exception ex)
        {
            Log(new TestResult
            {
                FunctionName = "lc_reason_message",
                ApiResult    = LicoreApi.LcResult.InternalError,
                Detail       = ex.Message,
                Timestamp    = DateTime.Now,
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExecuteValidate(bool cached)
    {
        string fnName = cached ? "lc_validate_cached" : "lc_validate_full";

        if (string.IsNullOrWhiteSpace(ProductName) || string.IsNullOrWhiteSpace(ProductVersion))
        {
            Log(new TestResult
            {
                FunctionName = fnName,
                ApiResult    = LicoreApi.LcResult.InvalidArgument,
                Detail       = "ProductName y ProductVersion son obligatorios.",
                Timestamp    = DateTime.Now,
            });
            return;
        }

        if (!string.IsNullOrEmpty(TestFingerprint))
            Environment.SetEnvironmentVariable("LICORE_TEST_FINGERPRINT", TestFingerprint);
        if (!string.IsNullOrEmpty(TestToday))
            Environment.SetEnvironmentVariable("LICORE_TEST_TODAY", TestToday);

        IsBusy = true;
        try
        {
            var (apiResult, reason) = cached
                ? LicoreApi.ValidateCached(ProductName, ProductVersion)
                : LicoreApi.ValidateFull(ProductName, ProductVersion);

            string? msg = LicoreApi.GetReasonMessage((int)reason);

            Log(new TestResult
            {
                FunctionName  = fnName,
                ApiResult     = apiResult,
                ReasonCode    = reason,
                ReasonMessage = msg,
                Detail        = cached ? "(cached)" : null,
                Timestamp     = DateTime.Now,
            });
        }
        catch (SEHException ex)
        {
            Log(new TestResult
            {
                FunctionName = fnName,
                ApiResult    = LicoreApi.LcResult.InternalError,
                Detail       = $"SEHException: {ex.Message}",
                Timestamp    = DateTime.Now,
            });
        }
        catch (Exception ex)
        {
            Log(new TestResult
            {
                FunctionName = fnName,
                ApiResult    = LicoreApi.LcResult.InternalError,
                Detail       = ex.Message,
                Timestamp    = DateTime.Now,
            });
        }
        finally
        {
            IsBusy = false;
        }
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
