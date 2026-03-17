using LicoreTestApp.Interop;

namespace LicoreTestApp.Models;

/// <summary>
/// Result of executing one <see cref="NegativeTestCase"/>.
/// </summary>
public sealed record NegativeTestResult
{
    public required NegativeTestCase Case    { get; init; }
    public required bool             Passed  { get; init; }
    public required LicoreApi.LcReason ActualReason  { get; init; }
    public required string           ActualMessage   { get; init; }
    public required DateTime         Timestamp       { get; init; }

    public string StatusLabel    => Passed ? "PASS" : "FAIL";
    public string ExpectedLabel  => $"{(int)Case.ExpectedReason} {Case.ExpectedReason}";
    public string ActualLabel    => $"{(int)ActualReason} {ActualReason}";
}
