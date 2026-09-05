namespace AccountRotation.Core.Identity;

/// <summary>
/// The e-mail address that identifies a Claude account. Equality is ordinal on
/// <see cref="Value"/>.
/// </summary>
public readonly record struct AccountEmail(string Value);
