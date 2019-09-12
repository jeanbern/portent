namespace portent
{
    /// <summary>
    /// The SECURITY_IMPERSONATION_LEVEL enumeration contains values that specify security impersonation levels.
    /// Security impersonation levels govern the degree to which a server process can act on behalf of a client process.
    /// </summary>
    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-security_impersonation_level"/>
    internal enum SecurityImpersonationLevel : uint
    {
        SecurityAnonymous = 0x0u,
        SecurityIdentification = 0x1u,
        SecurityImpersonation = 0x2u,
        SecurityDelegation = 0x3u,
    }
}
