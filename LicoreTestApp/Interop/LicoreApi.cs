using System.Runtime.InteropServices;

namespace LicoreTestApp.Interop;

/// <summary>
/// P/Invoke bindings for licore.dll (x64, cdecl, ANSI strings).
/// All DllImport methods map 1-to-1 to exported C symbols; no business logic here.
/// </summary>
public static class LicoreApi
{
    // ── Enumerations ─────────────────────────────────────────────────────────

    /// <summary>Top-level return code of every licore API function (lc_result_t).</summary>
    public enum LcResult : int
    {
        Ok             = 0,
        InvalidArgument = 1,
        InternalError  = 2,
        BufferTooSmall = 3,
    }

    /// <summary>
    /// Semantic reason codes written to out_reason parameters.
    /// Cast the raw int returned via out parameters to this enum.
    /// </summary>
    public enum LcReason : int
    {
        Ok                = 0,
        MissingLicense    = 10,
        InvalidFormat     = 11,
        TamperedSignature = 12,
        WrongVendorOrFamily = 13,
        WrongProduct      = 14,
        DeviceMismatch    = 15,
        Expired           = 16,
        ClockRollback     = 17,
        IoError           = 18,
        InternalError     = 19,
    }

    // ── Raw P/Invoke declarations ─────────────────────────────────────────────

    /// <summary>
    /// Verifies that the native library is reachable and operational.
    /// </summary>
    /// <returns><see cref="LcResult"/> cast from int; <see cref="LcResult.Ok"/> on success.</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int lc_ping();

    /// <summary>
    /// Returns a pointer to a static, library-owned ANSI string with the library version.
    /// Do NOT free the returned pointer — memory belongs to licore.dll.
    /// Use <see cref="GetVersion"/> to obtain a managed <see cref="string"/> instead.
    /// </summary>
    /// <returns>Pointer to a null-terminated static ANSI string (e.g. "0.1.0").</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr lc_version();

    /// <summary>
    /// Retrieves the human-readable description for a reason code.
    /// The string at <paramref name="outMsg"/> is static library memory — do NOT free it.
    /// Use <see cref="GetReasonMessage"/> to obtain a managed <see cref="string"/> instead.
    /// </summary>
    /// <param name="reasonCode">Integer value of an <see cref="LcReason"/> member.</param>
    /// <param name="outMsg">Receives a pointer to the static ANSI description string.</param>
    /// <returns><see cref="LcResult"/> indicating success or failure.</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int lc_reason_message(int reasonCode, out IntPtr outMsg);

    /// <summary>
    /// Performs a full (disk + network) license validation for the given product.
    /// </summary>
    /// <param name="productName">Product identifier (ANSI).</param>
    /// <param name="version">Product version string (ANSI).</param>
    /// <param name="outReason">
    /// Receives an <see cref="LcReason"/> integer on failure; undefined on success.
    /// </param>
    /// <returns><see cref="LcResult.Ok"/> when the license is valid.</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int lc_validate_full(string productName, string version, out int outReason);

    /// <summary>
    /// Validates the license using a locally cached state (no network round-trip).
    /// </summary>
    /// <param name="productName">Product identifier (ANSI).</param>
    /// <param name="version">Product version string (ANSI).</param>
    /// <param name="outReason">
    /// Receives an <see cref="LcReason"/> integer on failure; undefined on success.
    /// </param>
    /// <returns><see cref="LcResult.Ok"/> when the cached license is valid.</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int lc_validate_cached(string productName, string version, out int outReason);

    /// <summary>
    /// Generates a license-request JSON string using the two-step buffer pattern.
    /// <para>
    /// Step 1 — size query: pass <paramref name="outJson"/> = <see langword="null"/>,
    /// <paramref name="outJsonCap"/> = 0.  The call returns
    /// <see cref="LcResult.BufferTooSmall"/> and writes the required byte count to
    /// <paramref name="outJsonLen"/>.
    /// </para>
    /// <para>
    /// Step 2 — fill: allocate <c>outJsonLen + 1</c> bytes, pass the buffer and its
    /// capacity.  On success the buffer contains a null-terminated JSON string.
    /// The buffer is caller-owned; licore.dll does NOT retain a reference to it.
    /// </para>
    /// </summary>
    /// <param name="productName">Product identifier (ANSI).</param>
    /// <param name="version">Product version string (ANSI).</param>
    /// <param name="entitlementCode">Entitlement / SKU code (ANSI).</param>
    /// <param name="customerName">Customer display name (ANSI).</param>
    /// <param name="taxId">Customer tax or VAT identifier (ANSI).</param>
    /// <param name="email">Customer e-mail address (ANSI).</param>
    /// <param name="outJson">Caller-owned output buffer, or <see langword="null"/> on the size-query call.</param>
    /// <param name="outJsonCap">Capacity of <paramref name="outJson"/> in bytes (0 on size-query call).</param>
    /// <param name="outJsonLen">Receives the number of bytes written, or the required capacity.</param>
    /// <param name="outReason">Receives an <see cref="LcReason"/> integer on failure.</param>
    /// <returns>
    /// <see cref="LcResult.BufferTooSmall"/> on the size-query call;
    /// <see cref="LcResult.Ok"/> after a successful fill call.
    /// </returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int lc_generate_request(
        string productName,
        string version,
        string entitlementCode,
        string customerName,
        string taxId,
        string email,
        [Out] byte[]? outJson,
        int outJsonCap,
        out int outJsonLen,
        out int outReason);

    /// <summary>
    /// Serialises a license-request JSON string to a file.
    /// </summary>
    /// <param name="reqPath">Destination file path (ANSI).</param>
    /// <param name="reqJson">JSON content to write (ANSI).</param>
    /// <param name="outReason">Receives an <see cref="LcReason"/> integer on failure.</param>
    /// <returns><see cref="LcResult.Ok"/> on success.</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int lc_write_request_file(string reqPath, string reqJson, out int outReason);

    /// <summary>
    /// Installs a license file into the system license store.
    /// </summary>
    /// <param name="licensePath">Path to the license file (ANSI).</param>
    /// <param name="outReason">Receives an <see cref="LcReason"/> integer on failure.</param>
    /// <returns><see cref="LcResult.Ok"/> on success.</returns>
    [DllImport("licore.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int lc_install_license(string licensePath, out int outReason);

    // ── Managed helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Calls <c>lc_ping()</c> and returns the result as <see cref="LcResult"/>.
    /// </summary>
    public static LcResult Ping() => (LcResult)lc_ping();

    /// <summary>
    /// Returns the library version string (e.g. <c>"0.1.0"</c>).
    /// Wraps <c>lc_version()</c>; the underlying static memory is not freed.
    /// </summary>
    public static string GetVersion() =>
        Marshal.PtrToStringAnsi(lc_version()) ?? string.Empty;

    /// <summary>
    /// Returns the human-readable description for <paramref name="reasonCode"/>,
    /// or <see langword="null"/> if the native call fails.
    /// Wraps <c>lc_reason_message()</c>; the underlying static memory is not freed.
    /// </summary>
    /// <param name="reasonCode">Integer value of an <see cref="LcReason"/> member.</param>
    public static string? GetReasonMessage(int reasonCode)
    {
        int rc = lc_reason_message(reasonCode, out IntPtr ptr);
        return rc == (int)LcResult.Ok ? Marshal.PtrToStringAnsi(ptr) : null;
    }

    /// <summary>
    /// Calls <c>lc_validate_full</c> and returns a tuple of (ApiResult, Reason).
    /// </summary>
    public static (LcResult ApiResult, LcReason Reason) ValidateFull(string productName, string version)
    {
        int rc = lc_validate_full(productName, version, out int reason);
        return ((LcResult)rc, (LcReason)reason);
    }

    /// <summary>
    /// Calls <c>lc_validate_cached</c> and returns a tuple of (ApiResult, Reason).
    /// </summary>
    public static (LcResult ApiResult, LcReason Reason) ValidateCached(string productName, string version)
    {
        int rc = lc_validate_cached(productName, version, out int reason);
        return ((LcResult)rc, (LcReason)reason);
    }
}
