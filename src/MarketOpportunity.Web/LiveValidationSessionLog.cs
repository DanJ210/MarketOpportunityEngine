using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace MarketOpportunity.Web;

public static class LiveValidationSession
{
    public static string ValidateSessionId(string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
        {
            throw new InvalidOperationException("Diagnostic export requires a session ID.");
        }
        if (session.Length > 64 || session.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new InvalidOperationException("Diagnostic export requires a filename-safe session ID.");
        }
        return session;
    }

    public static string ApplicationLogPath(string directory, string? session) =>
        Path.GetFullPath(Path.Combine(directory, ValidateSessionId(session) + ".app.log"));
}

// Removes configured secret values and common credential assignments before any text is written.
public sealed partial class LogRedactor
{
    public const string Mask = "[REDACTED]";
    private static readonly string[] SensitiveKeyParts =
        ["password", "pwd", "secret", "apikey", "api_key", "accesskey", "privatekey", "token", "credential"];
    private readonly string[] _secrets;

    public LogRedactor(IEnumerable<string> secrets)
    {
        _secrets = secrets.Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 4)
            .Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();
    }

    public static LogRedactor FromConfiguration(IConfiguration configuration)
    {
        var secrets = new List<string>();
        foreach (var (key, value) in configuration.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(value)) { continue; }
            if (key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
            {
                secrets.Add(value);
                secrets.AddRange(ConnectionStringSecrets(value));
                continue;
            }
            var name = key[(key.LastIndexOf(':') + 1)..].ToLowerInvariant();
            if (SensitiveKeyParts.Any(name.Contains))
            {
                secrets.Add(value);
            }
        }
        return new LogRedactor(secrets);
    }

    public string Redact(string text)
    {
        var builder = new StringBuilder(text);
        foreach (var secret in _secrets)
        {
            builder.Replace(secret, Mask);
        }
        return CredentialAssignment().Replace(builder.ToString(), match => match.Groups["name"].Value + "=" + Mask);
    }

    private static IEnumerable<string> ConnectionStringSecrets(string value)
    {
        var builder = new DbConnectionStringBuilder();
        try { builder.ConnectionString = value; }
        catch (ArgumentException) { yield break; }
        foreach (string key in builder.Keys)
        {
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase)
                || key.Equals("pwd", StringComparison.OrdinalIgnoreCase))
            {
                if (builder[key]?.ToString() is { Length: > 0 } secret) { yield return secret; }
            }
        }
    }

    [GeneratedRegex(
        @"(?<name>\b(?:password|pwd|secret(?:key)?|api[_-]?key|access[_-]?token|token)\b)\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^;\s,""']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignment();
}

public sealed class SessionFileLoggerProvider : ILoggerProvider
{
    private const long MaximumBytes = 64L * 1024 * 1024;
    private static readonly UTF8Encoding Encoding = new(false);
    private readonly object _sync = new();
    private readonly LogRedactor _redactor;
    private readonly TimeProvider _clock;
    private long _length;
    private bool _truncated;
    private bool _disposed;

    public SessionFileLoggerProvider(string path, LogRedactor redactor, TimeProvider clock)
    {
        Path = path;
        _redactor = redactor;
        _clock = clock;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        // Validates writability at startup; no handle is held between entries.
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _length = stream.Length;
    }

    public string Path { get; }

    public ILogger CreateLogger(string categoryName) => new SessionFileLogger(this, categoryName);

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var line = $"{_clock.GetUtcNow():O} [{level}] {category}: {message}";
        if (exception is not null) { line += Environment.NewLine + exception; }
        line = _redactor.Redact(line) + Environment.NewLine;
        lock (_sync)
        {
            if (_disposed || _truncated) { return; }
            if (_length + Encoding.GetByteCount(line) > MaximumBytes)
            {
                _truncated = true;
                line = $"{_clock.GetUtcNow():O} [Warning] {nameof(SessionFileLoggerProvider)}: "
                    + "Session log size limit reached; later entries are omitted from this file."
                    + Environment.NewLine;
            }
            var bytes = Encoding.GetBytes(line);
            using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes);
            _length += bytes.Length;
        }
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; }
    }

    private sealed class SessionFileLogger(SessionFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }
            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
