using LicoreTestApp.Interop;

namespace LicoreTestApp.Models;

/// <summary>
/// Defines a single negative-test scenario for lc_validate_full.
/// Each case copies a fixture file (or leaves the basedir empty) and asserts the expected reason code.
/// </summary>
public sealed record NegativeTestCase(
    string Id,
    string Label,
    string? FixtureFileName,    // null → empty basedir (tests LC_MISSING_LICENSE)
    string Fingerprint,
    string Today,
    string ProductName,
    string ProductVersion,
    LicoreApi.LcReason ExpectedReason);
