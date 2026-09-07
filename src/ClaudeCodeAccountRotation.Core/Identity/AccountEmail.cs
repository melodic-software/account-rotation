namespace ClaudeCodeAccountRotation.Core.Identity;

/// <summary>
/// The e-mail address that identifies a Claude account, normalized to lowercase.
/// Equality is ordinal on <see cref="Value"/>. Production code constructs one
/// through <see cref="Parse"/>; the positional constructor exists for the
/// sanitizer's tests and for values already validated elsewhere.
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
        }

        return Result<AccountEmail, string>.Success(new AccountEmail(trimmed.ToLowerInvariant()));
    }

    public override string ToString() => Value;

    private static Result<AccountEmail, string> Failure(string reason) => Result<AccountEmail, string>.Failure(reason);
}
