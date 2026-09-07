using System.Diagnostics;
using System.Text.Json;
using ClaudeCodeAccountRotation.Core;
using ClaudeCodeAccountRotation.Core.Ports;

namespace ClaudeCodeAccountRotation.App.Adapters.Process;

/// <summary>
/// Runs the unmodified CLI with an argument list (never a joined command line),
/// optionally under <c>CLAUDE_CONFIG_DIR</c>, and kills the process at the
/// timeout. A non-zero exit, a timeout, or unparsable output is a failure
/// carrying the diagnostic text. Two commands run this way: <c>auth status
/// --json</c>, the authority on which account a folder holds, and <c>auth
/// logout</c>, which revokes a removed account's login.
/// </summary>
internal sealed class ClaudeCliProcessAuthStatus : IClaudeCliAuthStatus, IClaudeCliLogout
{
    private static readonly string[] _statusArguments = ["auth", "status", "--json"];
    private static readonly string[] _logoutArguments = ["auth", "logout"];

    private readonly ClaudeExecutable _executable;
    private readonly TimeSpan _timeout;

    public ClaudeCliProcessAuthStatus(ClaudeExecutable executable, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(executable);
        _executable = executable;
        _timeout = timeout;
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
        ProcessStartInfo startInfo = new(_executable.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (_executable.Shim is string shim)
        {
            // cmd.exe does not parse its command line by the argument-list rules, and an
            // argument-list entry is quoted only when it holds a space or a quote, so a
            // shim path carrying "&" would be read as a command separator. With /s the
            // interpreter strips the outer quotes and runs the rest; the inner quotes keep
            // the path one operand. A Windows path can never contain a quote itself.
            startInfo.Arguments = "/d /s /c \"\"" + shim + "\" " + string.Join(' ', arguments) + "\"";
        }
        else
        {
            foreach (string argument in _executable.ArgumentPrefix.Concat(arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (configDirectory is not null)
        {
            startInfo.Environment["CLAUDE_CONFIG_DIR"] = configDirectory;
        }

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
            TryKill(process);
            return Result<string, string>.Failure(command + " timed out after " + _timeout.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s and was killed");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string output = await standardOutput;
        string error = await standardError;
        return process.ExitCode == 0
            ? Result<string, string>.Success(output)
            : Result<string, string>.Failure(
                command + " exited with code " + process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + (error + output).Trim());
    }

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

    private static void TryKill(System.Diagnostics.Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Could not be killed; nothing more to do here.
        }
    }
}
