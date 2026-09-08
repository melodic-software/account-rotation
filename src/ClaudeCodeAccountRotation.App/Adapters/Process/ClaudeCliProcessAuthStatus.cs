using System.Diagnostics;
using System.Text.Json;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Ports;
using Microsoft.Extensions.Logging;

namespace ClaudeCodeAccountRotation.App.Adapters.Process;

/// <summary>
/// Runs the unmodified CLI with an argument list (never a joined command line),
/// optionally under <c>CLAUDE_CONFIG_DIR</c>, and kills the process at the
/// timeout. A non-zero exit, a timeout, or unparsable output is a failure
/// carrying the diagnostic text. Two commands run this way: <c>auth status
/// --json</c>, the authority on which account a folder holds, and <c>auth
/// logout</c>, which revokes a removed account's login.
/// <para>
/// A failure carries the command and its exit code and nothing else. What the
/// child wrote goes to the log, never into the returned string: those strings
/// are embedded verbatim in the responses the page renders, and the child's
/// output is on the wrong side of that boundary whatever it happens to hold.
/// </para>
/// </summary>
internal sealed partial class ClaudeCliProcessAuthStatus : IClaudeCliAuthStatus, IClaudeCliLogout
{
    private static readonly string[] _statusArguments = ["auth", "status", "--json"];
    private static readonly string[] _logoutArguments = ["auth", "logout"];

    private readonly ClaudeExecutable _executable;
    private readonly TimeSpan _timeout;
    private readonly ILogger<ClaudeCliProcessAuthStatus> _logger;

    public ClaudeCliProcessAuthStatus(ClaudeExecutable executable, TimeSpan timeout, ILogger<ClaudeCliProcessAuthStatus> logger)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(logger);
        _executable = executable;
        _timeout = timeout;
        _logger = logger;
    }

    public async Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken)
    {
        Result<string, string> run = await RunAsync(_statusArguments, configDirectory, cancellationToken);
        return run.IsFailure ? Result<ClaudeAuthStatus, string>.Failure(run.Error) : Parse(run.Value);
    }

    public async Task<Result<Unit, string>> LogoutAsync(string configDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        Result<string, string> run = await RunAsync(_logoutArguments, configDirectory, cancellationToken);
        return run.IsFailure ? Result<Unit, string>.Failure(run.Error) : Result<Unit, string>.Success(Unit.Value);
    }

    private async Task<Result<string, string>> RunAsync(string[] arguments, string? configDirectory, CancellationToken cancellationToken)
    {
        string command = "claude " + string.Join(' ', arguments);
        ProcessStartInfo startInfo = _executable.StartInfo(arguments, configDirectory);

        using System.Diagnostics.Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return Result<string, string>.Failure("could not start " + _executable.FileName + ": " + exception.Message);
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ChildProcess.TryKill(process);
            return Result<string, string>.Failure(command + " timed out after " + _timeout.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s and was killed");
        }
        catch (OperationCanceledException)
        {
            ChildProcess.TryKill(process);
            throw;
        }

        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode == 0)
        {
            return Result<string, string>.Success(output);
        }

        string exitCode = process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LogNonZeroExit(command, exitCode, (error + output).Trim());
        return Result<string, string>.Failure(command + " exited with code " + exitCode + "; see the log for what it printed");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Command} exited with code {ExitCode} and printed: {Output}")]
    private partial void LogNonZeroExit(string command, string exitCode, string output);

    private static Result<ClaudeAuthStatus, string> Parse(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output.Trim());
            JsonElement root = document.RootElement;
            return Result<ClaudeAuthStatus, string>.Success(new ClaudeAuthStatus(
                LoggedIn: root.TryGetProperty("loggedIn", out JsonElement loggedIn) && loggedIn.ValueKind == JsonValueKind.True,
                Email: Text(root, "email"),
                AuthMethod: Text(root, "authMethod"),
                OrganizationName: Text(root, "orgName"),
                SubscriptionType: Text(root, "subscriptionType"),
                ProjectsDirectory: Text(root, "projectsDirectory")));
        }
        catch (JsonException exception)
        {
            return Result<ClaudeAuthStatus, string>.Failure("claude auth status printed no JSON object: " + exception.Message);
        }
    }

    private static string? Text(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
