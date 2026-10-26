using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

// Bug reports are saved to a local file only. Nothing is sent to the developer.
internal sealed class BugReportService
{
    private const int MaxDiagnosticLogLength = 40 * 1024;
    private const int MaxPayloadLength = 56 * 1024;
    private const int MaxActivityLogLines = 200;
    private const int MaxDebugLogLines = 400;
    private const string TrimmedMarker = "[trimmed]";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BugReportsFolder => Path.Combine(AppDataPaths.RootFolder, "BugReports");

    public async Task<BugReportSaveResult> SaveLocallyAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = PreparePayloadJson(submission);
            Directory.CreateDirectory(BugReportsFolder);
            var filePath = Path.Combine(
                BugReportsFolder,
                $"bug-report-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken);
            return BugReportSaveResult.Success(filePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BugReportSaveResult.Failure("Saving the bug report was canceled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return BugReportSaveResult.Failure($"Crystal Relay could not save the bug report: {ex.Message}");
        }
    }

    public string BuildActivityLogSection(IEnumerable<string> activityLogEntries)
    {
        var recentEntries = activityLogEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Take(MaxActivityLogLines)
            .Reverse()
            .ToArray();
        if (recentEntries.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent Activity Log");
        builder.AppendLine(new string('-', 40));
        foreach (var entry in recentEntries)
        {
            builder.AppendLine(entry);
        }

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 16 * 1024);
    }

    public string BuildDebugLogSection()
    {
        var recentDebugLogLines = DebugLogService.ReadRecentLines(MaxDebugLogLines);
        if (recentDebugLogLines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent Debug Logs");
        builder.AppendLine(new string('-', 40));
        foreach (var line in recentDebugLogLines)
        {
            builder.AppendLine(line);
        }

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 16 * 1024);
    }

    public string BuildCrashLogSection()
    {
        var latestCrashLog = TryReadLatestCrashLog();
        if (string.IsNullOrWhiteSpace(latestCrashLog))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Latest Crash Log");
        builder.AppendLine(new string('-', 40));
        builder.AppendLine(latestCrashLog);

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 12 * 1024);
    }

    internal static string PreparePayloadJson(BugReportSubmission submission)
    {
        var payload = CreatePayload(submission);
        return SerializeWithinPayloadLimit(payload);
    }

    private static BugReportPayload CreatePayload(BugReportSubmission submission)
    {
        var payload = new BugReportPayload(
            SanitizeAndTrimForTransport(submission.Title, 120),
            SanitizeAndTrimRequiredForTransport(submission.WhatHappened, 5000, 20),
            SanitizeAndTrimRequiredForTransport(submission.ExpectedBehavior, 5000, 20),
            SanitizeAndTrimRequiredForTransport(submission.StepsToReproduce, 5000, 20),
            SanitizeAndTrimForTransport(submission.ContactName, 120),
            SanitizeAndTrimForTransport(submission.AppVersion, 80),
            TrimForTransport(submission.Category, 40),
            TrimForTransport(submission.Severity, 40),
            SanitizeAndTrimUtf8(submission.Snapshot, 2 * 1024),
            SanitizeAndTrimOptionalUtf8(submission.ActivityLog, 16 * 1024),
            SanitizeAndTrimOptionalUtf8(submission.DebugLog, 16 * 1024),
            SanitizeAndTrimOptionalUtf8(submission.CrashLog, 12 * 1024),
            DateTimeOffset.UtcNow);

        return TrimDiagnosticsToTotalBudget(payload);
    }

    private static string SerializeWithinPayloadLimit(BugReportPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { DebugLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { ActivityLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { CrashLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { Snapshot = TrimToUtf8Length(payload.Snapshot, 1 * 1024) };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = TrimReportTextFieldsToPayloadLimit(payload);
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        return json;
    }

    private static BugReportPayload TrimReportTextFieldsToPayloadLimit(BugReportPayload payload)
    {
        var current = payload;
        var json = JsonSerializer.Serialize(current, JsonOptions);
        var previousBytes = int.MaxValue;

        while (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength
            && TryTrimLargestReportTextField(current, out var trimmed))
        {
            current = trimmed;
            json = JsonSerializer.Serialize(current, JsonOptions);
            var currentBytes = Encoding.UTF8.GetByteCount(json);
            if (currentBytes >= previousBytes)
            {
                break;
            }

            previousBytes = currentBytes;
        }

        return current;
    }

    private static bool TryTrimLargestReportTextField(BugReportPayload payload, out BugReportPayload trimmed)
    {
        var titleBytes = Encoding.UTF8.GetByteCount(payload.Title);
        var whatHappenedBytes = Encoding.UTF8.GetByteCount(payload.WhatHappened);
        var expectedBehaviorBytes = Encoding.UTF8.GetByteCount(payload.ExpectedBehavior);
        var stepsToReproduceBytes = Encoding.UTF8.GetByteCount(payload.StepsToReproduce);
        var contactNameBytes = Encoding.UTF8.GetByteCount(payload.ContactName);

        var fieldName = string.Empty;
        var maxBytes = 0;
        var minBytes = 0;
        ConsiderField(nameof(payload.Title), titleBytes, 8);
        ConsiderField(nameof(payload.WhatHappened), whatHappenedBytes, 20);
        ConsiderField(nameof(payload.ExpectedBehavior), expectedBehaviorBytes, 20);
        ConsiderField(nameof(payload.StepsToReproduce), stepsToReproduceBytes, 20);
        ConsiderField(nameof(payload.ContactName), contactNameBytes, 0);

        if (string.IsNullOrEmpty(fieldName))
        {
            trimmed = payload;
            return false;
        }

        var targetBytes = Math.Max(minBytes, maxBytes / 2);
        trimmed = fieldName switch
        {
            nameof(payload.Title) => payload with { Title = TrimToUtf8Length(payload.Title, targetBytes) },
            nameof(payload.WhatHappened) => payload with { WhatHappened = TrimToUtf8Length(payload.WhatHappened, targetBytes) },
            nameof(payload.ExpectedBehavior) => payload with { ExpectedBehavior = TrimToUtf8Length(payload.ExpectedBehavior, targetBytes) },
            nameof(payload.StepsToReproduce) => payload with { StepsToReproduce = TrimToUtf8Length(payload.StepsToReproduce, targetBytes) },
            nameof(payload.ContactName) => payload with { ContactName = TrimToUtf8Length(payload.ContactName, targetBytes) },
            _ => payload
        };
        return !ReferenceEquals(trimmed, payload);

        void ConsiderField(string candidateName, int candidateBytes, int candidateMinBytes)
        {
            if (candidateBytes <= candidateMinBytes || candidateBytes <= maxBytes)
            {
                return;
            }

            fieldName = candidateName;
            maxBytes = candidateBytes;
            minBytes = candidateMinBytes;
        }
    }

    private static BugReportPayload TrimDiagnosticsToTotalBudget(BugReportPayload payload)
    {
        var remainingBytes = MaxDiagnosticLogLength;
        var snapshot = TrimToUtf8Length(payload.Snapshot, Math.Min(2 * 1024, remainingBytes));
        remainingBytes -= Encoding.UTF8.GetByteCount(snapshot);

        var crashLog = TrimOptionalToRemaining(payload.CrashLog, 12 * 1024, ref remainingBytes);
        var activityLog = TrimOptionalToRemaining(payload.ActivityLog, 16 * 1024, ref remainingBytes);
        var debugLog = TrimOptionalToRemaining(payload.DebugLog, 16 * 1024, ref remainingBytes);

        return payload with
        {
            Snapshot = snapshot,
            ActivityLog = activityLog,
            DebugLog = debugLog,
            CrashLog = crashLog
        };
    }

    private static string? TrimOptionalToRemaining(string? value, int sectionMaxBytes, ref int remainingBytes)
    {
        if (string.IsNullOrEmpty(value) || remainingBytes <= 0)
        {
            return null;
        }

        var trimmed = TrimToUtf8Length(value, Math.Min(sectionMaxBytes, remainingBytes));
        remainingBytes -= Encoding.UTF8.GetByteCount(trimmed);
        return trimmed;
    }

    private static string TrimForTransport(string? value, int maxCharacters)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxCharacters
            ? trimmed
            : trimmed[..maxCharacters];
    }

    private static string SanitizeAndTrimForTransport(string? value, int maxCharacters)
    {
        var characterTrimmed = TrimForTransport(SensitiveTextSanitizer.Sanitize(value ?? string.Empty), maxCharacters);
        return TrimToUtf8Length(characterTrimmed, maxCharacters);
    }

    private static string SanitizeAndTrimRequiredForTransport(string? value, int maxCharacters, int minCharacters)
    {
        var sanitized = SanitizeAndTrimForTransport(value, maxCharacters);
        if (sanitized.Length >= minCharacters)
        {
            return sanitized;
        }

        var expanded = string.IsNullOrWhiteSpace(sanitized)
            ? "[redacted sensitive details]"
            : $"{sanitized} [redacted details]";
        return SanitizeAndTrimForTransport(expanded, maxCharacters);
    }

    private static string SanitizeAndTrimUtf8(string? value, int maxBytes) =>
        TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(value ?? string.Empty), maxBytes);

    private static string? SanitizeAndTrimOptionalUtf8(string? value, int maxBytes) =>
        string.IsNullOrEmpty(value) ? null : SanitizeAndTrimUtf8(value, maxBytes);

    private static string TrimToUtf8Length(string value, int maxBytes)
    {
        if (maxBytes <= 0 || string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var marker = $"{Environment.NewLine}{TrimmedMarker}";
        var markerBytes = Encoding.UTF8.GetByteCount(marker);
        if (markerBytes >= maxBytes)
        {
            return TrimMarkerToUtf8Length(marker, maxBytes);
        }

        var contentBudget = maxBytes - markerBytes;
        var builder = new StringBuilder(value.Length);
        var currentBytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(runeText);
            if (currentBytes + runeBytes > contentBudget)
            {
                break;
            }

            builder.Append(runeText);
            currentBytes += runeBytes;
        }

        return $"{builder.ToString().TrimEnd()}{marker}";
    }

    private static string TrimMarkerToUtf8Length(string marker, int maxBytes)
    {
        var builder = new StringBuilder(marker.Length);
        var currentBytes = 0;
        foreach (var rune in marker.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(runeText);
            if (currentBytes + runeBytes > maxBytes)
            {
                break;
            }

            builder.Append(runeText);
            currentBytes += runeBytes;
        }

        return builder.ToString();
    }

    private static string TryReadLatestCrashLog()
    {
        try
        {
            var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
            if (!File.Exists(latestCrashPath))
            {
                return string.Empty;
            }

            return File.ReadAllText(latestCrashPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not read latest crash log: {ex.Message}";
        }
    }

    private sealed record BugReportPayload(
        string Title,
        string WhatHappened,
        string ExpectedBehavior,
        string StepsToReproduce,
        string ContactName,
        string AppVersion,
        string Category,
        string Severity,
        string Snapshot,
        string? ActivityLog,
        string? DebugLog,
        string? CrashLog,
        DateTimeOffset SubmittedAtUtc);
}

internal sealed record BugReportSubmission(
    string Title,
    string WhatHappened,
    string ExpectedBehavior,
    string StepsToReproduce,
    string ContactName,
    string AppVersion,
    string Category,
    string Severity,
    string Snapshot,
    string? ActivityLog,
    string? DebugLog,
    string? CrashLog);

internal sealed record BugReportSaveResult(bool Succeeded, string? FilePath, string? ErrorMessage)
{
    public static BugReportSaveResult Success(string filePath) => new(true, filePath, null);

    public static BugReportSaveResult Failure(string errorMessage) => new(false, null, errorMessage);
}
