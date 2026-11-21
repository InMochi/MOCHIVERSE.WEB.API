using Soenneker.Hashing.Argon2;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Argon2id password hashing and verification helper.
/// </summary>
public sealed class Hasher
{
    public async Task<string> HashPasswordAsync(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return await Argon2HashingUtil.Hash(password);
    }

    public async Task<bool> VerifyPasswordAsync(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        return await Argon2HashingUtil.Verify(password, passwordHash);
    }
}
