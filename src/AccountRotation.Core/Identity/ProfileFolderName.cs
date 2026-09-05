namespace AccountRotation.Core.Identity;

/// <summary>
/// Derives the on-disk folder name of a parked profile from its account e-mail.
/// The folder name is a convenience only; identity comes from the profile file.
/// </summary>
public static class ProfileFolderName
{
    private static readonly char[] _forbiddenCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string FromEmail(AccountEmail email)
    {
        char[] characters = email.Value.ToLowerInvariant().ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            if (char.IsControl(characters[index]) || Array.IndexOf(_forbiddenCharacters, characters[index]) >= 0)
            {
                characters[index] = '_';
            }
        }

        string folderName = new string(characters).TrimEnd('.', ' ');
        return folderName.Length == 0 ? "unknown" : folderName;
    }
}
