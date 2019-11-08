using JetBrains.Annotations;

namespace portent
{
    /// <summary>
    /// The TOKEN_TYPE enumeration contains values that differentiate between a primary token and an impersonation token.
    /// </summary>
    /// <see>
    /// <cref>https://docs.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_type</cref>
    /// </see>
    [PublicAPI]
    internal enum TokenType
    {
        TokenPrimary = 1,
        TokenImpersonation
    }
}
