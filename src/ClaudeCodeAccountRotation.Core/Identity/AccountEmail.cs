namespace ClaudeCodeAccountRotation.Core.Identity;

/// <summary>
/// The e-mail address that identifies a Claude account, normalized to lowercase.
/// Equality is ordinal on <see cref="Value"/>. Production code constructs one
/// through <see cref="Parse"/>; the positional constructor exists for the
/// sanitizer's tests and for values already validated elsewhere.
/// <para>
/// <see cref="Parse"/> is a trust boundary, so the character rule is an
/// allowlist: letters, digits, and <c>. _ - + @</c>, and nothing else. A
/// blocklist here would have to enumerate every character that is dangerous
/// somewhere downstream, and the address travels a long way. It becomes a
/// command-interpreter operand when the CLI is an npm shim, and it becomes a
/// profile folder name, where the sanitizer that maps <c>&lt;</c> and <c>&gt;</c>
/// onto <c>_</c> would give two accounts one folder and so one refresh token
/// two holders. The dot is admitted but never at either end, because a trailing
/// dot is trimmed off a folder name and would collide the same way.
/// </para>
/// </summary>
public readonly record struct AccountEmail(string Value)
{
    private const int MinimumLength = 3;
    private const int MaximumLength = 254;

    public static Result<AccountEmail, string> Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string trimmed = value.Trim();

        if (trimmed.Length is < MinimumLength or > MaximumLength)
        {
            return Failure("an account e-mail is 3 to 254 characters long");
        }

        int atCount = trimmed.Count(static character => character == '@');
        if (atCount != 1)
        {
            return Failure("an account e-mail contains exactly one @");
        }

        foreach (char character in trimmed)
        {
            if (IsAdmitted(character))
            {
                continue;
            }

            // The three shapes an operator actually mistypes get their own reason;
            // everything else the allowlist refuses gets the rule itself.
            if (char.IsWhiteSpace(character))
            {
                return Failure("an account e-mail contains no whitespace");
            }

            if (character is '/' or '\\')
            {
                return Failure("an account e-mail contains no path separator");
            }

            if (char.IsControl(character))
            {
                return Failure("an account e-mail contains no control character");
            }

            return Failure("an account e-mail holds only letters, digits, and . _ - + @");
        }

        if (trimmed.StartsWith('.') || trimmed.EndsWith('.'))
        {
            return Failure("an account e-mail does not begin or end with a dot");
        }

        return Result<AccountEmail, string>.Success(new AccountEmail(trimmed.ToLowerInvariant()));
    }

    private static bool IsAdmitted(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '+' or '@';

    public override string ToString() => Value;

    private static Result<AccountEmail, string> Failure(string reason) => Result<AccountEmail, string>.Failure(reason);
}
