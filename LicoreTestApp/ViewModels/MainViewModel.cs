using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using System.Windows.Input;
using LicoreTestApp.Interop;
using LicoreTestApp.Models;
using LicoreTestApp.Services;

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
/// ViewModel for <c>Views/MainWindow</c>.
/// All DLL call logic lives in private <c>Exec*</c> methods; command handlers wrap them
/// with IsBusy, Log, and try/catch. <see cref="RunAllCommand"/> calls the same Exec* methods.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // ── Observable properties ─────────────────────────────────────────────────

    /// <summary>Append-only log of API call results displayed in the ListView.</summary>
    public ObservableCollection<TestResult> Results { get; } = new();

    private bool _isBusy;

    /// <summary>When <see langword="true"/>, a native call is in progress.</summary>
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

    /// <summary>Inverse of <see cref="IsBusy"/>; bound to <c>IsEnabled</c> on API buttons.</summary>
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
    /// <summary>When non-empty, set as <c>LICORE_TEST_FINGERPRINT</c> before validation calls.</summary>
    public string TestFingerprint
    {
        get => _testFingerprint;
        set { if (_testFingerprint == value) return; _testFingerprint = value; OnPropertyChanged(); }
    }

    private string _testToday = string.Empty;
    /// <summary>When non-empty, set as <c>LICORE_TEST_TODAY</c> before validation calls.</summary>
    public string TestToday
    {
        get => _testToday;
        set { if (_testToday == value) return; _testToday = value; OnPropertyChanged(); }
    }

    private string _testBasedir = string.Empty;
    /// <summary>When non-empty, set as <c>LICORE_TEST_BASEDIR</c> to redirect license store away from real ProgramData.</summary>
    public string TestBasedir
    {
        get => _testBasedir;
        set { if (_testBasedir == value) return; _testBasedir = value; OnPropertyChanged(); }
    }

    private string _testNowEpoch = string.Empty;
    /// <summary>When non-empty, set as <c>LICORE_TEST_NOW_EPOCH</c> (Unix seconds) to control anti-rollback.</summary>
    public string TestNowEpoch
    {
        get => _testNowEpoch;
        set { if (_testNowEpoch == value) return; _testNowEpoch = value; OnPropertyChanged(); }
    }

    private string _testSkPemPath = Environment.GetEnvironmentVariable("LICORE_TEST_SK_PEM_PATH") ?? string.Empty;
    /// <summary>
    /// Path to the Ed25519 test private key PEM file (kid="k1").
    /// Used by <see cref="IssueLicenseCommand"/> and the E2E autonomous flow.
    /// Pre-populated from <c>LICORE_TEST_SK_PEM_PATH</c> env var if set.
    /// </summary>
    public string TestSkPemPath
    {
        get => _testSkPemPath;
        set { if (_testSkPemPath == value) return; _testSkPemPath = value; OnPropertyChanged(); }
    }

    private string _issueExpirationDate = string.Empty;
    /// <summary>Optional expiry date "YYYY-MM-DD" for the issued test .lic. Empty = 1 year from today.</summary>
    public string IssueExpirationDate
    {
        get => _issueExpirationDate;
        set { if (_issueExpirationDate == value) return; _issueExpirationDate = value; OnPropertyChanged(); }
    }

    private string _entitlementCode = "ENT-0001";
    /// <summary>Entitlement / SKU code passed to lc_generate_request.</summary>
    public string EntitlementCode
    {
        get => _entitlementCode;
        set { if (_entitlementCode == value) return; _entitlementCode = value; OnPropertyChanged(); }
    }

    private string _customerName = string.Empty;
    /// <summary>Customer display name passed to lc_generate_request.</summary>
    public string CustomerName
    {
        get => _customerName;
        set { if (_customerName == value) return; _customerName = value; OnPropertyChanged(); }
    }

    private string _taxId = string.Empty;
    /// <summary>Customer tax/VAT identifier passed to lc_generate_request.</summary>
    public string TaxId
    {
        get => _taxId;
        set { if (_taxId == value) return; _taxId = value; OnPropertyChanged(); }
    }

    private string _email = string.Empty;
    /// <summary>Customer e-mail address passed to lc_generate_request.</summary>
    public string Email
    {
        get => _email;
        set { if (_email == value) return; _email = value; OnPropertyChanged(); }
    }

    private string _generatedJson = string.Empty;
    /// <summary>Most recent JSON from lc_generate_request. Read-only from the UI.</summary>
    public string GeneratedJson
    {
        get => _generatedJson;
        private set { if (_generatedJson == value) return; _generatedJson = value; OnPropertyChanged(); }
    }

    private string _licensePath = string.Empty;
    /// <summary>Path to the .lic file selected via <see cref="BrowseLicenseCommand"/>.</summary>
    public string LicensePath
    {
        get => _licensePath;
        set { if (_licensePath == value) return; _licensePath = value; OnPropertyChanged(); }
    }

    private string _runAllSummary = string.Empty;
    /// <summary>Summary produced after RunAll, e.g. "7/8 OK — 1 error".</summary>
    public string RunAllSummary
    {
        get => _runAllSummary;
        private set { if (_runAllSummary == value) return; _runAllSummary = value; OnPropertyChanged(); }
    }

    /// <summary>Results from the negative-test suite (VF scenarios).</summary>
    public ObservableCollection<NegativeTestResult> NegativeTestResults { get; } = new();

    private string _negativeSummary = string.Empty;
    /// <summary>Summary after running negative cases, e.g. "12/12 PASS".</summary>
    public string NegativeSummary
    {
        get => _negativeSummary;
        private set { if (_negativeSummary == value) return; _negativeSummary = value; OnPropertyChanged(); }
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

    /// <summary>Runs lc_generate_request with the form fields and stores the resulting JSON.</summary>
    public ICommand GenerateRequestCommand { get; }

    /// <summary>Opens SaveFileDialog and persists <see cref="GeneratedJson"/> via lc_write_request_file.</summary>
    public ICommand SaveRequestFileCommand { get; }

    /// <summary>Opens OpenFileDialog and populates <see cref="LicensePath"/>. Does not call the DLL.</summary>
    public ICommand BrowseLicenseCommand { get; }

    /// <summary>Installs the .lic at <see cref="LicensePath"/> and runs a post-install lc_validate_full.</summary>
    public ICommand InstallLicenseCommand { get; }

    /// <summary>Validates via lc_validate_full. Applies optional test env vars before calling.</summary>
    public ICommand ValidateFullCommand { get; }

    /// <summary>Validates via lc_validate_cached. Applies optional test env vars before calling.</summary>
    public ICommand ValidateCachedCommand { get; }

    /// <summary>Executes all test steps in sequence; continues past individual failures.</summary>
    public ICommand RunAllCommand { get; }

    /// <summary>Runs all predefined VF negative-test scenarios in isolated temp directories.</summary>
    public ICommand RunAllNegativeCasesCommand { get; }

    /// <summary>Issues a test .lic from <see cref="GeneratedJson"/> using the Ed25519 test key.</summary>
    public ICommand IssueLicenseCommand { get; }

    /// <summary>Opens a file dialog to locate the Ed25519 test private key PEM file.</summary>
    public ICommand BrowseSkPemCommand { get; }

    /// <summary>Runs the complete autonomous E2E flow: gen-req → issue-lic → install → validate.</summary>
    public ICommand RunE2eAutonomousCommand { get; }

    private readonly RelayCommand _copyLogCmd;

    /// <summary>
    /// Copies all log entries to the clipboard.
    /// Format per line: <c>[HH:mm:ss] FunctionName | ApiResult | ReasonCode | Detail</c>
    /// </summary>
    public ICommand CopyLogCommand => _copyLogCmd;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel()
    {
        ClearLogCommand        = new RelayCommand(_ => ClearLog());
        PingCommand            = new RelayCommand(_ => ExecutePing());
        ReasonMessageCommand   = new RelayCommand(_ => ExecuteReasonMessage());
        GenerateRequestCommand = new RelayCommand(_ => ExecuteGenerateRequest());
        SaveRequestFileCommand = new RelayCommand(_ => ExecuteSaveRequestFile());
        BrowseLicenseCommand   = new RelayCommand(_ => ExecuteBrowseLicense());
        InstallLicenseCommand  = new RelayCommand(_ => ExecuteInstallLicense());
        ValidateFullCommand    = new RelayCommand(_ => ExecuteValidate(cached: false));
        ValidateCachedCommand  = new RelayCommand(_ => ExecuteValidate(cached: true));
        RunAllCommand                = new RelayCommand(_ => ExecuteRunAll());
        RunAllNegativeCasesCommand   = new RelayCommand(_ => ExecuteRunAllNegativeCases());
        IssueLicenseCommand          = new RelayCommand(_ => ExecuteIssueLicense());
        BrowseSkPemCommand           = new RelayCommand(_ => ExecuteBrowseSkPem());
        RunE2eAutonomousCommand      = new RelayCommand(_ => ExecuteRunE2eAutonomous());
        _copyLogCmd                  = new RelayCommand(_ => CopyLog(), _ => Results.Count > 0);

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

    // ── Exec* — pure DLL logic (no IsBusy, no try/catch, no Log) ─────────────

    private TestResult ExecPing()
    {
        LicoreApi.LcResult result = LicoreApi.Ping();
        string version = LicoreApi.GetVersion();
        return new TestResult
        {
            FunctionName = "lc_ping/lc_version",
            ApiResult    = result,
            Detail       = $"version={version}",
            Timestamp    = DateTime.Now,
        };
    }

    private TestResult ExecReasonMessage(int reasonCode)
    {
        string? msg = LicoreApi.GetReasonMessage(reasonCode);
        return new TestResult
        {
            FunctionName  = "lc_reason_message",
            ApiResult     = msg is not null ? LicoreApi.LcResult.Ok : LicoreApi.LcResult.InvalidArgument,
            ReasonCode    = (LicoreApi.LcReason)reasonCode,
            ReasonMessage = msg,
            Timestamp     = DateTime.Now,
        };
    }

    private void ApplyTestEnvVars()
    {
        if (!string.IsNullOrEmpty(TestFingerprint))
            Environment.SetEnvironmentVariable("LICORE_TEST_FINGERPRINT", TestFingerprint);
        if (!string.IsNullOrEmpty(TestToday))
            Environment.SetEnvironmentVariable("LICORE_TEST_TODAY", TestToday);
        if (!string.IsNullOrEmpty(TestBasedir))
            Environment.SetEnvironmentVariable("LICORE_TEST_BASEDIR", TestBasedir);
        if (!string.IsNullOrEmpty(TestNowEpoch))
            Environment.SetEnvironmentVariable("LICORE_TEST_NOW_EPOCH", TestNowEpoch);
    }

    private TestResult ExecValidateFull(string fnName = "lc_validate_full")
    {
        ApplyTestEnvVars();
        var (apiResult, reason) = LicoreApi.ValidateFull(ProductName, ProductVersion);
        return new TestResult
        {
            FunctionName  = fnName,
            ApiResult     = apiResult,
            ReasonCode    = reason,
            ReasonMessage = LicoreApi.GetReasonMessage((int)reason),
            Timestamp     = DateTime.Now,
        };
    }

    private TestResult ExecValidateCached()
    {
        ApplyTestEnvVars();
        var (apiResult, reason) = LicoreApi.ValidateCached(ProductName, ProductVersion);
        return new TestResult
        {
            FunctionName  = "lc_validate_cached",
            ApiResult     = apiResult,
            ReasonCode    = reason,
            ReasonMessage = LicoreApi.GetReasonMessage((int)reason),
            Detail        = "(cached)",
            Timestamp     = DateTime.Now,
        };
    }

    private TestResult ExecGenerateRequest()
    {
        var (apiResult, reason, json) = LicoreApi.GenerateRequest(
            ProductName, ProductVersion, EntitlementCode, CustomerName, TaxId, Email);

        if (apiResult == LicoreApi.LcResult.Ok && json is not null)
        {
            GeneratedJson = json;
            string preview = json.Length > 120 ? json[..120] + "..." : json;
            return new TestResult
            {
                FunctionName = "lc_generate_request",
                ApiResult    = apiResult,
                ReasonCode   = reason,
                Detail       = preview,
                Timestamp    = DateTime.Now,
            };
        }

        GeneratedJson = string.Empty;
        return new TestResult
        {
            FunctionName  = "lc_generate_request",
            ApiResult     = apiResult,
            ReasonCode    = reason,
            ReasonMessage = LicoreApi.GetReasonMessage((int)reason),
            Timestamp     = DateTime.Now,
        };
    }

    private TestResult ExecWriteRequestFile(string path)
    {
        var (apiResult, reason) = LicoreApi.WriteRequestFile(path, GeneratedJson);
        return new TestResult
        {
            FunctionName  = "lc_write_request_file",
            ApiResult     = apiResult,
            ReasonCode    = reason,
            ReasonMessage = LicoreApi.GetReasonMessage((int)reason),
            Detail        = path,
            Timestamp     = DateTime.Now,
        };
    }

    private TestResult ExecInstallLicense(string path)
    {
        var (apiResult, reason) = LicoreApi.InstallLicense(path);
        return new TestResult
        {
            FunctionName  = "lc_install_license",
            ApiResult     = apiResult,
            ReasonCode    = reason,
            ReasonMessage = LicoreApi.GetReasonMessage((int)reason),
            Detail        = Path.GetFileName(path),
            Timestamp     = DateTime.Now,
        };
    }

    // ── Execute* — ICommand handlers (IsBusy + Log + guard + try/catch) ───────

    private void ExecutePing()
    {
        IsBusy = true;
        try   { Log(ExecPing()); }
        catch (SEHException ex) { Log(ErrorResult("lc_ping/lc_version", $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { Log(ErrorResult("lc_ping/lc_version", ex.Message)); }
        finally { IsBusy = false; }
    }

    private void ExecuteReasonMessage()
    {
        IsBusy = true;
        try   { Log(ExecReasonMessage(_selectedReasonCode)); }
        catch (SEHException ex) { Log(ErrorResult("lc_reason_message", $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { Log(ErrorResult("lc_reason_message", ex.Message)); }
        finally { IsBusy = false; }
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

        IsBusy = true;
        try   { Log(cached ? ExecValidateCached() : ExecValidateFull()); }
        catch (SEHException ex) { Log(ErrorResult(fnName, $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { Log(ErrorResult(fnName, ex.Message)); }
        finally { IsBusy = false; }
    }

    private void ExecuteGenerateRequest()
    {
        if (string.IsNullOrWhiteSpace(ProductName)     ||
            string.IsNullOrWhiteSpace(ProductVersion)  ||
            string.IsNullOrWhiteSpace(EntitlementCode) ||
            string.IsNullOrWhiteSpace(CustomerName))
        {
            Log(new TestResult
            {
                FunctionName = "lc_generate_request",
                ApiResult    = LicoreApi.LcResult.InvalidArgument,
                Detail       = "ProductName, ProductVersion, EntitlementCode y CustomerName son obligatorios.",
                Timestamp    = DateTime.Now,
            });
            return;
        }

        IsBusy = true;
        try
        {
            Log(ExecGenerateRequest());
        }
        catch (SEHException ex) { GeneratedJson = string.Empty; Log(ErrorResult("lc_generate_request", $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { GeneratedJson = string.Empty; Log(ErrorResult("lc_generate_request", ex.Message)); }
        finally { IsBusy = false; }
    }

    private void ExecuteSaveRequestFile()
    {
        if (string.IsNullOrEmpty(GeneratedJson))
        {
            Log(new TestResult
            {
                FunctionName = "lc_write_request_file",
                ApiResult    = LicoreApi.LcResult.InvalidArgument,
                Detail       = "Sin .req generado. Ejecute primero 'Generar .req'.",
                Timestamp    = DateTime.Now,
            });
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter     = "Request file (*.req)|*.req",
            DefaultExt = "req",
            FileName   = $"{ProductName}_{ProductVersion}.req",
        };

        if (dlg.ShowDialog() != true) return;

        string path = dlg.FileName;
        IsBusy = true;
        try   { Log(ExecWriteRequestFile(path)); }
        catch (SEHException ex) { Log(ErrorResult("lc_write_request_file", $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { Log(ErrorResult("lc_write_request_file", ex.Message)); }
        finally { IsBusy = false; }
    }

    private void ExecuteBrowseLicense()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "License file (*.lic)|*.lic",
            Title  = "Seleccionar licencia",
        };
        if (dlg.ShowDialog() == true)
            LicensePath = dlg.FileName;
    }

    private void ExecuteInstallLicense()
    {
        if (string.IsNullOrWhiteSpace(LicensePath))
        {
            Log(ErrorResult("lc_install_license", "Selecciona un archivo .lic primero.",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }
        if (!File.Exists(LicensePath))
        {
            Log(ErrorResult("lc_install_license", $"Archivo no encontrado: {LicensePath}",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }

        IsBusy = true;
        try
        {
            var installResult = ExecInstallLicense(LicensePath);
            Log(installResult);

            if (installResult.ApiResult == LicoreApi.LcResult.Ok &&
                installResult.ReasonCode == LicoreApi.LcReason.Ok)
                Log(ExecValidateFull("lc_validate_full (post-install)"));
        }
        catch (SEHException ex) { Log(ErrorResult("lc_install_license", $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { Log(ErrorResult("lc_install_license", ex.Message)); }
        finally { IsBusy = false; }
    }

    // ── Negative test cases (VF scenarios from integration-reference.md) ─────

    private static readonly NegativeTestCase[] _negativeCases =
    [
        new("VF-01", "Firma valida",                  "license_signed_valid.lic",            "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.Ok),
        new("VF-02", "Sin licencia (basedir vacio)",  null,                                  "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.MissingLicense),
        new("VF-03", "JSON invalido",                 "license_invalid_json.lic",            "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.InvalidFormat),
        new("VF-04", "Fecha invalida",                "license_signed_invalid_date.lic",     "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.InvalidFormat),
        new("VF-05", "Firma invalida",                "license_signed_bad_signature.lic",    "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.TamperedSignature),
        new("VF-06", "Payload alterado",              "license_signed_tampered_payload.lic", "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.TamperedSignature),
        new("VF-07", "Kid desconocido",               "license_signed_wrong_kid.lic",        "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.TamperedSignature),
        new("VF-08", "Vendor/family incorrecto",      "license_signed_wrong_vendor.lic",     "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.WrongVendorOrFamily),
        new("VF-09", "Producto incorrecto",           "license_signed_wrong_product.lic",    "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.WrongProduct),
        new("VF-10", "Device mismatch",               "license_signed_wrong_device.lic",     "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.DeviceMismatch),
        new("VF-11", "Licencia vencida",              "license_signed_expired.lic",          "FINGERPRINT_OK", "2027-01-01", "APPX", "1.0.0", LicoreApi.LcReason.Expired),
        new("VF-12", "Keys reordenadas (canonical)",  "license_signed_valid_reordered.lic",  "FINGERPRINT_OK", "2026-02-22", "APPX", "1.0.0", LicoreApi.LcReason.Ok),
    ];

    private void ExecuteRunAllNegativeCases()
    {
        IsBusy = true;
        NegativeTestResults.Clear();
        NegativeSummary = "Ejecutando...";

        string fixturesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestFixtures");
        int pass = 0;

        try
        {
            foreach (var tc in _negativeCases)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), $"licore_neg_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                bool passed = false;
                LicoreApi.LcReason actualReason = LicoreApi.LcReason.InternalError;
                string actualMessage = string.Empty;

                try
                {
                    if (tc.FixtureFileName is not null)
                    {
                        // LICORE_TEST_BASEDIR acts as %ProgramData% replacement;
                        // the DLL appends impulso-informatico/desktop-suite/license.lic to it.
                        string licDir = Path.Combine(tempDir, "impulso-informatico", "desktop-suite");
                        Directory.CreateDirectory(licDir);
                        string src = Path.Combine(fixturesDir, tc.FixtureFileName);
                        File.Copy(src, Path.Combine(licDir, "license.lic"), overwrite: true);
                    }

                    Environment.SetEnvironmentVariable("LICORE_TEST_BASEDIR",      tempDir);
                    Environment.SetEnvironmentVariable("LICORE_TEST_FINGERPRINT",  tc.Fingerprint);
                    Environment.SetEnvironmentVariable("LICORE_TEST_TODAY",        tc.Today);

                    var (apiResult, reason) = LicoreApi.ValidateFull(tc.ProductName, tc.ProductVersion);
                    actualReason  = reason;
                    actualMessage = LicoreApi.GetReasonMessage((int)reason) ?? reason.ToString();
                    passed        = apiResult == LicoreApi.LcResult.Ok && reason == tc.ExpectedReason;
                }
                catch (SEHException ex) { actualMessage = $"SEHException: {ex.Message}"; }
                catch (Exception    ex) { actualMessage = ex.Message; }
                finally
                {
                    Environment.SetEnvironmentVariable("LICORE_TEST_BASEDIR",     null);
                    Environment.SetEnvironmentVariable("LICORE_TEST_FINGERPRINT", null);
                    Environment.SetEnvironmentVariable("LICORE_TEST_TODAY",       null);
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }

                if (passed) pass++;
                NegativeTestResults.Add(new NegativeTestResult
                {
                    Case          = tc,
                    Passed        = passed,
                    ActualReason  = actualReason,
                    ActualMessage = actualMessage,
                    Timestamp     = DateTime.Now,
                });
            }
        }
        finally
        {
            int total = _negativeCases.Length;
            int fail  = total - pass;
            NegativeSummary = fail == 0
                ? $"{pass}/{total} PASS"
                : $"{pass}/{total} PASS — {fail} FAIL";
            IsBusy = false;
        }
    }

    // ── Issue .lic [TEST] ─────────────────────────────────────────────────────

    private void ExecuteBrowseSkPem()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "PEM key file (*.pem)|*.pem|All files (*.*)|*.*",
            Title  = "Seleccionar clave privada Ed25519 (test)",
        };
        if (dlg.ShowDialog() == true)
            TestSkPemPath = dlg.FileName;
    }

    private void ExecuteIssueLicense()
    {
        if (string.IsNullOrEmpty(GeneratedJson))
        {
            Log(ErrorResult("lc_issue_license [TEST]",
                "Sin .req generado. Ejecute primero 'Generar .req'.",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }
        if (string.IsNullOrWhiteSpace(TestSkPemPath))
        {
            Log(ErrorResult("lc_issue_license [TEST]", "TestSkPemPath es obligatorio.",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }
        if (!File.Exists(TestSkPemPath))
        {
            Log(ErrorResult("lc_issue_license [TEST]",
                $"Archivo PEM no encontrado: {TestSkPemPath}",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }

        IsBusy = true;
        try
        {
            string pem     = File.ReadAllText(TestSkPemPath);
            string licJson = LocalLicIssuer.Issue(GeneratedJson, pem,
                string.IsNullOrWhiteSpace(IssueExpirationDate) ? null : IssueExpirationDate);

            string tempLic = Path.Combine(Path.GetTempPath(),
                $"{ProductName}_{ProductVersion}_test_{DateTime.Now:HHmmss}.lic");
            // UTF-8 without BOM — jsmn (used by licore.dll) fails if the file starts with the BOM bytes EF BB BF.
            File.WriteAllText(tempLic, licJson, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LicensePath = tempLic;

            Log(new TestResult
            {
                FunctionName = "lc_issue_license [TEST]",
                ApiResult    = LicoreApi.LcResult.Ok,
                Detail       = tempLic,
                Timestamp    = DateTime.Now,
            });
        }
        catch (Exception ex) { Log(ErrorResult("lc_issue_license [TEST]", ex.Message)); }
        finally { IsBusy = false; }
    }

    // ── E2E Autónomo ──────────────────────────────────────────────────────────

    private void ExecuteRunE2eAutonomous()
    {
        if (string.IsNullOrWhiteSpace(ProductName)     ||
            string.IsNullOrWhiteSpace(ProductVersion)  ||
            string.IsNullOrWhiteSpace(EntitlementCode) ||
            string.IsNullOrWhiteSpace(CustomerName))
        {
            Log(ErrorResult("E2E Autónomo",
                "Producto, Versión, Entitlement y Cliente son obligatorios.",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }
        if (string.IsNullOrWhiteSpace(TestSkPemPath) || !File.Exists(TestSkPemPath))
        {
            Log(ErrorResult("E2E Autónomo",
                "TestSkPemPath no apunta a un archivo PEM válido.",
                LicoreApi.LcResult.InvalidArgument));
            return;
        }

        IsBusy = true;
        Results.Clear();
        RunAllSummary = "Ejecutando E2E autónomo...";
        int total = 0, ok = 0;

        string tempBasedir = Path.Combine(Path.GetTempPath(), $"licore_e2e_{Guid.NewGuid():N}");
        string? tempLicFile = null;

        try
        {
            Directory.CreateDirectory(tempBasedir);

            // Set overrides for the entire flow
            Environment.SetEnvironmentVariable("LICORE_TEST_BASEDIR",     tempBasedir);
            Environment.SetEnvironmentVariable("LICORE_TEST_FINGERPRINT", "FINGERPRINT_OK");

            // 1 — lc_generate_request
            RunStep(ref total, ref ok, "lc_generate_request", () => ExecGenerateRequest());
            if (string.IsNullOrEmpty(GeneratedJson))
                throw new InvalidOperationException("lc_generate_request falló; no se puede continuar.");

            // 2 — Issue .lic locally [TEST]
            string pem     = File.ReadAllText(TestSkPemPath);
            string licJson = LocalLicIssuer.Issue(GeneratedJson, pem,
                string.IsNullOrWhiteSpace(IssueExpirationDate) ? null : IssueExpirationDate);

            tempLicFile = Path.Combine(Path.GetTempPath(), $"licore_e2e_{Guid.NewGuid():N}.lic");
            // UTF-8 without BOM — jsmn fails if the file starts with EF BB BF.
            File.WriteAllText(tempLicFile, licJson, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LicensePath = tempLicFile;

            total++; ok++;
            Log(new TestResult
            {
                FunctionName = "lc_issue_license [TEST]",
                ApiResult    = LicoreApi.LcResult.Ok,
                Detail       = $"→ {Path.GetFileName(tempLicFile)}",
                Timestamp    = DateTime.Now,
            });

            // 3 — lc_install_license (DLL installs into LICORE_TEST_BASEDIR)
            var installResult = RunStep(ref total, ref ok, "lc_install_license",
                () => ExecInstallLicense(tempLicFile));

            if (installResult.ApiResult != LicoreApi.LcResult.Ok ||
                installResult.ReasonCode  != LicoreApi.LcReason.Ok)
                throw new InvalidOperationException(
                    $"lc_install_license falló: {installResult.ReasonCode} — " +
                    (installResult.ReasonMessage ?? installResult.Detail));

            // 4 — lc_validate_full (E2E)
            RunStep(ref total, ref ok, "lc_validate_full (E2E)", () =>
            {
                var (apiResult, reason) = LicoreApi.ValidateFull(ProductName, ProductVersion);
                return new TestResult
                {
                    FunctionName  = "lc_validate_full (E2E)",
                    ApiResult     = apiResult,
                    ReasonCode    = reason,
                    ReasonMessage = LicoreApi.GetReasonMessage((int)reason),
                    Timestamp     = DateTime.Now,
                };
            });
        }
        catch (SEHException ex) { Log(ErrorResult("E2E Autónomo", $"SEHException: {ex.Message}")); }
        catch (Exception    ex) { Log(ErrorResult("E2E Autónomo", ex.Message)); }
        finally
        {
            Environment.SetEnvironmentVariable("LICORE_TEST_BASEDIR",     null);
            Environment.SetEnvironmentVariable("LICORE_TEST_FINGERPRINT", null);
            try { Directory.Delete(tempBasedir, recursive: true); } catch { }
            // tempLicFile is kept; LicensePath still points to it for optional inspection.

            int errors = total - ok;
            RunAllSummary = errors == 0
                ? $"E2E: {ok}/{total} OK"
                : $"E2E: {ok}/{total} OK — {errors} error{(errors == 1 ? "" : "es")}";
            IsBusy = false;
        }
    }

    // ── RunAll ────────────────────────────────────────────────────────────────

    private void ExecuteRunAll()
    {
        IsBusy = true;
        Results.Clear();
        RunAllSummary = "Ejecutando...";
        int total = 0, ok = 0;

        try
        {
            // 1 — lc_ping + lc_version
            RunStep(ref total, ref ok, "lc_ping/lc_version", () => ExecPing());

            // 2 — lc_reason_message(0)
            RunStep(ref total, ref ok, "lc_reason_message", () => ExecReasonMessage(0));

            // 3 — lc_validate_full
            RunStep(ref total, ref ok, "lc_validate_full", () => ExecValidateFull());

            // 4 — lc_validate_cached
            RunStep(ref total, ref ok, "lc_validate_cached", () => ExecValidateCached());

            // 5 — lc_generate_request
            RunStep(ref total, ref ok, "lc_generate_request", () => ExecGenerateRequest());

            // 6 — lc_write_request_file (only if paso 5 produced JSON)
            if (!string.IsNullOrEmpty(GeneratedJson))
            {
                string reqPath = Path.Combine(Path.GetTempPath(), "licore_test.req");
                RunStep(ref total, ref ok, "lc_write_request_file", () => ExecWriteRequestFile(reqPath));
            }

            // 7 — lc_install_license (conditional; log skip if not configured)
            TestResult? installResult = null;
            if (!string.IsNullOrWhiteSpace(LicensePath) && File.Exists(LicensePath))
            {
                installResult = RunStep(ref total, ref ok, "lc_install_license",
                    () => ExecInstallLicense(LicensePath));
            }
            else
            {
                Log(new TestResult
                {
                    FunctionName = "lc_install_license",
                    ApiResult    = LicoreApi.LcResult.Ok,
                    Detail       = "(skip — LicensePath no configurado)",
                    Timestamp    = DateTime.Now,
                });
            }

            // 8 — post-install validate (only if paso 7 executed and succeeded)
            if (installResult?.ApiResult == LicoreApi.LcResult.Ok &&
                installResult.ReasonCode  == LicoreApi.LcReason.Ok)
            {
                RunStep(ref total, ref ok, "lc_validate_full (post-install)",
                    () => ExecValidateFull("lc_validate_full (post-install)"));
            }
        }
        finally
        {
            int errors = total - ok;
            RunAllSummary = errors == 0
                ? $"{ok}/{total} OK"
                : $"{ok}/{total} OK — {errors} error{(errors == 1 ? "" : "es")}";

            WriteSessionLog();
            IsBusy = false;
        }
    }

    /// <summary>
    /// Executes one RunAll step: increments <paramref name="total"/>, catches DLL exceptions,
    /// logs the result, and increments <paramref name="ok"/> on success.
    /// </summary>
    private TestResult RunStep(ref int total, ref int ok, string fnName, Func<TestResult> step)
    {
        total++;
        TestResult result;
        try
        {
            result = step();
        }
        catch (SEHException ex) { result = ErrorResult(fnName, $"SEHException: {ex.Message}"); }
        catch (Exception    ex) { result = ErrorResult(fnName, ex.Message); }
        Log(result);
        if (result.IsSuccess) ok++;
        return result;
    }

    private void WriteSessionLog()
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(),
                $"licore_testapp_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            var lines = Results.Select(r =>
                $"[{r.Timestamp:HH:mm:ss}] {r.FunctionName} | {r.ApiResult} | " +
                $"{r.ReasonCode?.ToString() ?? "-"} | {r.ReasonMessage ?? r.Detail ?? string.Empty}");
            File.WriteAllLines(path, lines);
            StatusMessage = $"Log guardado: {path}";
        }
        catch { /* silencioso — no propagar excepción de logging */ }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static TestResult ErrorResult(
        string fnName, string detail,
        LicoreApi.LcResult apiResult = LicoreApi.LcResult.InternalError) =>
        new()
        {
            FunctionName = fnName,
            ApiResult    = apiResult,
            Detail       = detail,
            Timestamp    = DateTime.Now,
        };

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
