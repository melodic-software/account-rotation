using System.Text.Json.Nodes;
using System.Threading.Channels;
using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.Core;

namespace ClaudeCodeAccountRotation.App.Tests;

/// <summary>
/// Stands in for a running <c>claude auth login</c>. The greeting reproduces
/// what the spike recorded on this machine: the authorize URL wrapped in OSC 8
/// hyperlink escapes, then the code prompt with no line ending. What a
/// submitted code does is the test's to choose.
/// </summary>
internal sealed class LoginChildScript
{
    public const string AuthorizeBase = "https://claude.com/cai/oauth/authorize";

    /// <summary>Every child started, in order, with the arguments and folder it was given.</summary>
    public List<ScriptedLoginChild> Children { get; } = [];

    public ScriptedLoginChild Last => Children[^1];

    public string? StartError { get; set; }

    /// <summary>Whether the greeting carries a URL at all; a login that prints none must fail cleanly.</summary>
    public bool PrintsUrl { get; set; } = true;

    /// <summary>
    /// The default answer to a code: rejected, with the code echoed back in the
    /// worst way a CLI could. Nothing the tool returns or logs may carry it.
    /// </summary>
    public Func<ScriptedLoginChild, string, Task> OnCode { get; set; } = (child, code) =>
    {
        child.Emit("Invalid code (" + code + "). Please make sure the full code was copied.\r\n");
        return Task.CompletedTask;
    };

    /// <summary>A code that works: the folder gains the pair and the state file, and the CLI exits.</summary>
    public static async Task CompleteLoginAsync(ScriptedLoginChild child, string code)
    {
        ArgumentNullException.ThrowIfNull(child);
        await CredentialFiles.WriteAsync(child.ConfigDirectory, "refresh-" + child.Email, CancellationToken.None);
        JsonObject state = new() { ["numStartups"] = 1, ["oauthAccount"] = AppFactory.AccountJson(child.Email) };
        await File.WriteAllTextAsync(Path.Combine(child.ConfigDirectory, ".claude.json"), state.ToJsonString(), CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(child.ConfigDirectory, "backups"));
        child.Emit("Login successful. Logged in as " + child.Email + ".\r\n");
        child.Exit();
    }

    public Result<ILoginChild, string> Start(IReadOnlyList<string> arguments, string configDirectory)
    {
        if (StartError is string error)
        {
            return Result<ILoginChild, string>.Failure(error);
        }

        ScriptedLoginChild child = new(this, arguments, configDirectory);
        Children.Add(child);
        return Result<ILoginChild, string>.Success(child);
    }
}

/// <summary>One scripted child: a channel of output, a list of codes it was given, and whether it was killed.</summary>
internal sealed class ScriptedLoginChild : ILoginChild
{
    /// <summary>The two bytes an OSC 8 hyperlink is framed with, by code point so this file holds no raw control character.</summary>
    private const char Escape = (char)0x1b;
    private const char Bell = (char)0x07;

    private readonly LoginChildScript _script;
    private readonly Channel<string> _chunks = Channel.CreateUnbounded<string>();

    public ScriptedLoginChild(LoginChildScript script, IReadOnlyList<string> arguments, string configDirectory)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(arguments);
        _script = script;
        Arguments = [.. arguments];
        ConfigDirectory = configDirectory;
        Email = arguments.SkipWhile(static argument => argument != "--email").Skip(1).FirstOrDefault() ?? "unknown@example.com";
        SignInUrl = LoginChildScript.AuthorizeBase + "?code=true&client_id=cli&redirect_uri="
            + Uri.EscapeDataString("https://platform.claude.com/oauth/code/callback")
            + "&login_hint=" + Uri.EscapeDataString(Email);
        if (script.PrintsUrl)
        {
            // The hyperlink's target, then the same URL again as its visible label.
            // A parser that took the line verbatim would capture every escape byte.
            Emit(Escape + "]8;;" + SignInUrl + Bell + SignInUrl + Escape + "]8;;" + Bell
                + "\r\nPaste code here if prompted > ");
        }
        else
        {
            Emit("Opening browser...\r\n");
        }
    }

    public IReadOnlyList<string> Arguments { get; }

    public string ConfigDirectory { get; }

    public string Email { get; }

    public string SignInUrl { get; }

    public List<string> CodesWritten { get; } = [];

    public bool Killed { get; private set; }

    public void Emit(string chunk) => _chunks.Writer.TryWrite(chunk);

    /// <summary>The CLI ending on its own, the way a completed login does.</summary>
    public void Exit() => _chunks.Writer.TryComplete();

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _chunks.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public Task WriteCodeAsync(string code, CancellationToken cancellationToken)
    {
        CodesWritten.Add(code);
        return _script.OnCode(this, code);
    }

    public void Kill()
    {
        Killed = true;
        _chunks.Writer.TryComplete();
    }

    public void Dispose() => Kill();
}
