namespace Rift.Automation.Guest.Live;

/// <summary>Address-free candidate discovery report, not a qualified health source.</summary>
public sealed record TargetVitalsCandidateDiagnostic(
    string Status, int CandidateCount, long? Sequence, string? TargetId, double? Health, double? HealthMax);

public static class TargetVitalsCandidateReporting
{
    public static TargetVitalsCandidateDiagnostic Build(
        LiveScanResult scan, string challenge, bool identityStable)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (!identityStable || !scan.Completed)
            return new("unavailable", 0, null, null, null, null);
        var selected = Infrastructure.TargetVitalsV2CandidateSelector.Select(
            scan.TargetVitalsCandidates, challenge, scan.Completed);
        if (selected is null)
            return new(scan.TargetVitalsCandidates.Count == 0 ? "not-found" : "conflict",
                scan.TargetVitalsCandidates.Count, null, null, null, null);
        var value = selected.Observation;
        return new("candidate-unqualified", scan.TargetVitalsCandidates.Count,
            value.Sequence, value.TargetId, value.Health, value.HealthMax);
    }
}
