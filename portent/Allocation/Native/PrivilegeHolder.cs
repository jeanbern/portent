/*
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace portent
{
    /// <summary>
    /// Disposable class to manage the lifetime of a token privilege.
    /// </summary>
    /// <see>
    /// <cref>https://github.com/dotnet/corefx/blob/master/src/System.Security.AccessControl/src/System/Security/AccessControl/Privilege.cs</cref>
    /// </see>
    internal sealed class PrivilegeHolder : IDisposable
    {
        private static Luid LuidFromPrivilege(string privilege, out bool success)
        {
            success = NativeMethods.LookupPrivilegeValue(null, privilege, out var luid);
            return luid;
        }

        [ThreadStatic]
        private static TlsContents? _ttlsSlotData;

        private bool _needToRevert;
        private bool _initialState;
        private bool _stateWasChanged;
        private readonly Luid _luid;
        private readonly Thread _currentThread = Thread.CurrentThread;
        private readonly TlsContents _tlsContents;

        private bool ObtainPrivilege()
        {
            // Check if we have the privilege, this is different than having it enabled.
            if (!NativeMethods.HasPrivilege(_tlsContents.ThreadHandle, _luid))
            {
                Reset();
                return false;
                // Nothing else gets run. We return.
            }

            var privileges = new LuidAndAttributes(_luid, SePrivilegeAttributes.SePrivilegeEnabled);
            var newState = new TokenPrivilege(privileges);

            // Place the new privilege on the thread token and remember the previous state.
            if (!NativeMethods.AdjustTokenPrivileges(
                _tlsContents.ThreadHandle,
                false,
                ref newState,
                (uint)Unsafe.SizeOf<TokenPrivilege>(),
                out var previousState,
                out _))
            {
                return Marshal.GetLastWin32Error() == 0;
            }

            const int errorNotAllAssigned = 0x514;
            if (errorNotAllAssigned == Marshal.GetLastWin32Error())
            {
                return false;
            }

            // This is the initial state that revert will have to go back to
            _initialState = (previousState.Privileges.Attributes & SePrivilegeAttributes.SePrivilegeEnabled) != 0;

            // Remember whether state has changed at all (in this case, only changed if it was NOT set previously)
            _stateWasChanged = !_initialState;

            // If we had to impersonate, or if the privilege state changed we'll need to revert
            _needToRevert = _tlsContents.IsImpersonating || _stateWasChanged;
            return true;
        }

        public static PrivilegeHolder? EnablePrivilege(string privilegeName)
        {
            var luid = LuidFromPrivilege(privilegeName, out var luidSuccess);
            if (!luidSuccess)
            {
                return null;
            }

            PrivilegeHolder? holder = null;
            var success = false;
            try
            {
                // The payload is entirely in the finally block
                // This is how we ensure that the code will not be
                // interrupted by catastrophic exceptions
            }
            finally
            {
                try
                {
                    // Retrieve TLS state
                    var tlsContents = _ttlsSlotData;
                    if (tlsContents == null)
                    {
                        _ttlsSlotData = tlsContents = TlsContents.Create();
                    }
                    else
                    {
                        tlsContents.IncrementReferenceCount();
                    }

                    if (tlsContents != null)
                    {
                        holder = new PrivilegeHolder(tlsContents, luid);
                        if (holder.ObtainPrivilege())
                        {
                            success = true;
                        }
                    }
                }
                finally
                {
                    if (holder?._needToRevert == false)
                    {
                        holder.Reset();
                    }

                    if (!success)
                    {
                        holder?.Dispose();
                        holder = null;
                    }
                }
            }

            return holder;
        }

        private PrivilegeHolder(TlsContents contents, in Luid luid)
        {
            _tlsContents = contents;
            _luid = luid;
        }

        private void Reset()
        {
            _stateWasChanged = false;
            _initialState = false;
            _needToRevert = false;

            _tlsContents?.DecrementReferenceCount();
        }

        ~PrivilegeHolder()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool _disposed;

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _tlsContents?.Dispose();
            }

            if (!_needToRevert)
            {
                _disposed = true;
                return;
            }

            if (!_currentThread.Equals(Thread.CurrentThread))
            {
                throw new InvalidOperationException("Start and end thread must be the same.");
            }

            // This code must be eagerly prepared and non-interruptible.
            var error = 0;
            try
            {
                // The payload is entirely in the finally block
                // This is how we ensure that the code will not be
                // interrupted by catastrophic exceptions
            }
            finally
            {
                var success = true;

                try
                {
                    // Only call AdjustTokenPrivileges if we're not going to be reverting to self,
                    // on this Revert, since doing the latter obliterates the thread token anyway

                    if (_stateWasChanged
                        && _tlsContents != null
                        && (_tlsContents.ReferenceCountValue > 1
                            || !_tlsContents.IsImpersonating))
                    {
                        var attribute = _initialState ? SePrivilegeAttributes.SePrivilegeEnabled : SePrivilegeAttributes.SePrivilegeDisabled;
                        var privileges = new LuidAndAttributes(_luid, attribute);
                        var newState = new TokenPrivilege(privileges);

                        if (!NativeMethods.AdjustTokenPrivileges(
                            _tlsContents.ThreadHandle,
                            false,
                            ref newState,
                            0,
                            out _,
                            out _))
                        {
#pragma warning disable S1854 // Dead stores should be removed - How is this a dead store?
                            error = Marshal.GetLastWin32Error();
#pragma warning restore S1854 // Dead stores should be removed
                            success = false;
                        }
                    }
                }
                finally
                {
                    if (success)
                    {
                        Reset();
                    }
                }
            }

            if (error != 0)
            {
                throw new InvalidOperationException();
            }

            _disposed = true;
        }

        private static class NativeMethods
        {
            /// <summary>
            /// The LookupPrivilegeValue function retrieves the locally unique identifier (LUID) used on a specified system to locally represent the specified privilege name.
            /// </summary>
            /// <param name="lpSystemName">
            /// A pointer to a null-terminated string that specifies the name of the system on which the privilege name is retrieved.
            /// If a null string is specified, the function attempts to find the privilege name on the local system.
            /// </param>
            /// <param name="lpName">
            /// A pointer to a null-terminated string that specifies the name of the privilege, as defined in the Winnt.h header file.
            /// For example, this parameter could specify the constant, SE_SECURITY_NAME, or its corresponding string, "SeSecurityPrivilege".
            /// </param>
            /// <param name="lpLuid">
            /// A pointer to a variable that receives the LUID by which the privilege is known on the system specified by the lpSystemName parameter.
            /// </param>
            /// <returns>
            /// If the function succeeds, the function returns nonzero.
            /// If the function fails, it returns zero. To get extended error information, call GetLastError.
            /// </returns>
            /// <see>
            ///     <cref>https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupprivilegevaluew</cref>
            ///     <cref>https://github.com/dotnet/corefx/blob/master/src/Common/src/Interop/Windows/Advapi32/Interop.LookupPrivilegeValue.cs</cref>
            /// </see>
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false, EntryPoint = "LookupPrivilegeValueW")]
            internal static extern bool LookupPrivilegeValue([MarshalAs(UnmanagedType.LPTStr)] string? lpSystemName, [MarshalAs(UnmanagedType.LPTStr)] string lpName, out Luid lpLuid);

            /// <summary>
            /// The AdjustTokenPrivileges function enables or disables privileges in the specified access token.
            /// Enabling or disabling privileges in an access token requires TOKEN_ADJUST_PRIVILEGES access.
            /// </summary>
            /// <param name="tokenHandle">
            /// A handle to the access token that contains the privileges to be modified.
            /// The handle must have TOKEN_ADJUST_PRIVILEGES access to the token.
            /// If the PreviousState parameter is not NULL, the handle must also have TOKEN_QUERY access.
            /// </param>
            /// <param name="disableAllPrivileges">
            /// Specifies whether the function disables all of the token's privileges.
            /// If this value is TRUE, the function disables all privileges and ignores the NewState parameter.
            /// If it is FALSE, the function modifies privileges based on the information pointed to by the NewState parameter.
            /// </param>
            /// <param name="newState">
            /// A pointer to a TOKEN_PRIVILEGES structure that specifies an array of privileges and their attributes.
            /// If the DisableAllPrivileges parameter is FALSE, the AdjustTokenPrivileges function enables, disables, or removes these privileges for the token.
            /// If DisableAllPrivileges is TRUE, the function ignores this parameter.
            /// </param>
            /// <param name="bufferLength">
            /// Specifies the size, in bytes, of the buffer pointed to by the PreviousState parameter. This parameter can be zero if the PreviousState parameter is NULL.
            /// </param>
            /// <param name="previousState">
            /// A pointer to a buffer that the function fills with a TOKEN_PRIVILEGES structure that contains the previous state of any privileges that the function modifies.
            /// That is, if a privilege has been modified by this function, the privilege and its previous state are contained in the TOKEN_PRIVILEGES structure referenced by PreviousState.
            /// If the PrivilegeCount member of TOKEN_PRIVILEGES is zero, then no privileges have been changed by this function.
            /// This parameter can be NULL.
            /// If you specify a buffer that is too small to receive the complete list of modified privileges, the function fails and does not adjust any privileges.
            /// In this case, the function sets the variable pointed to by the ReturnLength parameter to the number of bytes required to hold the complete list of modified privileges.
            /// </param>
            /// <param name="returnLength">
            /// A pointer to a variable that receives the required size, in bytes, of the buffer pointed to by the PreviousState parameter.
            /// This parameter can be NULL if PreviousState is NULL.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// To determine whether the function adjusted all of the specified privileges, call GetLastError.
            /// </returns>
            /// <remarks>
            /// This implementation uses <see cref="TokenPrivilege"/> which restricts it to checking one privilege at a time.
            /// </remarks>
            /// <see>
            ///     <cref>https://docs.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges</cref>
            ///     <cref>https://github.com/dotnet/corefx/blob/master/src/Common/src/Interop/Windows/Advapi32/Interop.AdjustTokenPrivileges.cs</cref>
            /// </see>
            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool AdjustTokenPrivileges(
                SafeTokenHandle tokenHandle,
                bool disableAllPrivileges,
                ref TokenPrivilege newState,
                uint bufferLength,
                out TokenPrivilege previousState,
                out uint returnLength);

            /// <summary>
            /// The GetTokenInformation function retrieves a specified type of information about an access token.
            /// The calling process must have appropriate access rights to obtain the information.
            /// To determine if a user is a member of a specific group, use the CheckTokenMembership function.
            /// To determine group membership for app container tokens, use the CheckTokenMembershipEx function.
            /// </summary>
            /// <param name="tokenHandle">
            /// A handle to an access token from which information is retrieved.
            /// If TokenInformationClass specifies TokenSource, the handle must have TOKEN_QUERY_SOURCE access.
            /// For all other TokenInformationClass values, the handle must have TOKEN_QUERY access.
            /// </param>
            /// <param name="tokenInformationClass">
            /// Specifies a value from the TOKEN_INFORMATION_CLASS enumerated type to identify the type of information the function retrieves.
            /// Any callers who check the TokenIsAppContainer and have it return 0 should also verify that the caller token is not an identify level impersonation token.
            /// If the current token is not an app container but is an identity level token, you should return AccessDenied.
            /// </param>
            /// <param name="tokenInformation">
            /// A pointer to a buffer the function fills with the requested information.
            /// The structure put into this buffer depends upon the type of information specified by the TokenInformationClass parameter.
            /// </param>
            /// <param name="tokenInformationLength">
            /// Specifies the size, in bytes, of the buffer pointed to by the TokenInformation parameter.
            /// If TokenInformation is NULL, this parameter must be zero.
            /// </param>
            /// <param name="returnLength">
            /// A pointer to a variable that receives the number of bytes needed for the buffer pointed to by the TokenInformation parameter.
            /// If this value is larger than the value specified in the TokenInformationLength parameter, the function fails and stores no data in the buffer.
            /// If the value of the TokenInformationClass parameter is TokenDefaultDacl and the token has no default DACL, the function sets the variable pointed to by ReturnLength to sizeof(TOKEN_DEFAULT_DACL) and sets the DefaultDacl member of the TOKEN_DEFAULT_DACL structure to NULL.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero. To get extended error information, call GetLastError.
            /// </returns>
            /// <see>
            /// <cref>https://docs.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation</cref>
            /// </see>
            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern bool GetTokenInformation(
                SafeTokenHandle tokenHandle,
                TokenInformationClass tokenInformationClass,
                ref byte tokenInformation,
                uint tokenInformationLength,
                out uint returnLength);

            /// <summary>
            /// Checks whether an access token has a given privilege.
            /// </summary>
            /// <param name="tokenHandle">
            /// A handle to an access token from which information is retrieved.
            /// </param>
            /// <param name="privilegeLuid">
            /// The privilege for which to check.
            /// </param>
            /// <returns>
            /// Whether or not the privilege is defined on the access token.
            /// </returns>
            internal static bool HasPrivilege(SafeTokenHandle tokenHandle, in Luid privilegeLuid)
            {
                const int defaultPrivilegeCount = 30;
                Debug.Assert(Unsafe.SizeOf<LuidAndAttributes>() == 3 * sizeof(int));
                // ReSharper disable once ArrangeRedundantParentheses
                var size = sizeof(uint) + (defaultPrivilegeCount * Unsafe.SizeOf<LuidAndAttributes>());
                Span<byte> allocated = stackalloc byte[size];
                ref var asRef = ref MemoryMarshal.GetReference(allocated);
                if (GetTokenInformation(tokenHandle,
                        TokenInformationClass.TokenPrivileges,
                        ref asRef,
                        (uint)size,
                        out var returnLength
                    ))
                {
                    Debug.Assert(asRef <= defaultPrivilegeCount);
                    // ReSharper disable once ArrangeRedundantParentheses
                    Debug.Assert(sizeof(uint) + (asRef * Unsafe.SizeOf<LuidAndAttributes>()) == returnLength);
                    return HasPrivilege(ref Unsafe.As<byte, int>(ref asRef), privilegeLuid);
                }

                if (returnLength <= size)
                {
                    return false;
                }

                Span<byte> largerAllocated = stackalloc byte[(int)returnLength];
                asRef = ref MemoryMarshal.GetReference(largerAllocated);
                if (!GetTokenInformation(tokenHandle,
                    TokenInformationClass.TokenPrivileges,
                    ref asRef,
                    returnLength,
                    out var returnLength2
                ))
                {
                    return false;
                }

                Debug.Assert(returnLength == returnLength2);
                // ReSharper disable once ArrangeRedundantParentheses
                Debug.Assert(sizeof(uint) + (asRef * Unsafe.SizeOf<LuidAndAttributes>()) == returnLength2);
                return HasPrivilege(ref Unsafe.As<byte, int>(ref asRef), privilegeLuid);
            }

            private static bool HasPrivilege(ref int resultPointer, in Luid privilegeLuid)
            {
                var resultCount = resultPointer;
                for (var i = 0; i < resultCount; i++)
                {
                    var high = Unsafe.Add(ref resultPointer, 1 + (3 * i));
                    var low = Unsafe.Add(ref resultPointer, 2 + (3 * i));
                    if (high == privilegeLuid.HighPart && low == privilegeLuid.LowPart)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
