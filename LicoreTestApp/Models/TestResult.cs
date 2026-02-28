using LicoreTestApp.Interop;

namespace LicoreTestApp.Models;

/// <summary>
/// Immutable snapshot of a single licore API call and its outcome.
/// </summary>
public sealed record TestResult
{
    /// <summary>Name of the licore API function that was invoked.</summary>
    public required string FunctionName { get; init; }

    /// <summary>Top-level return value of the API call.</summary>
    public required LicoreApi.LcResult ApiResult { get; init; }

    /// <summary>
    /// Semantic reason code from the out_reason parameter, if the function produces one;
    /// <see langword="null"/> for functions that do not expose a reason code (e.g. lc_ping, lc_version).
    /// </summary>
    public LicoreApi.LcReason? ReasonCode { get; init; }

    /// <summary>
    /// Human-readable description of <see cref="ReasonCode"/> obtained via lc_reason_message();
    /// <see langword="null"/> when no reason code is available or the lookup fails.
    /// </summary>
    public string? ReasonMessage { get; init; }

    /// <summary>
    /// Optional output text produced by the call (e.g. the JSON from lc_generate_request());
    /// <see langword="null"/> when the function does not produce text output.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>UTC instant at which the API call was made.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// <see langword="true"/> when <see cref="ApiResult"/> is <see cref="LicoreApi.LcResult.Ok"/>.
    /// </summary>
    public bool IsSuccess => ApiResult == LicoreApi.LcResult.Ok;
}
