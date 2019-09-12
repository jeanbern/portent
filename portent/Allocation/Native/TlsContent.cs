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
using System.Runtime.InteropServices;

namespace portent
{
    /// <summary>
    /// Disposable class to manage the lifetime of a thread token.
    /// </summary>
    /// <see cref="https://github.com/dotnet/corefx/blob/master/src/System.Security.AccessControl/src/System/Security/AccessControl/Privilege.cs"/>
    internal sealed class TlsContents : IDisposable
    {
        private SafeTokenHandle _threadHandle = new SafeTokenHandle(IntPtr.Zero);
        private static volatile SafeTokenHandle ProcessHandle = new SafeTokenHandle(IntPtr.Zero);
        private static readonly object SyncRoot = new object();

        public int ReferenceCountValue { get; private set; } = 1;
        public SafeTokenHandle ThreadHandle => _threadHandle;
        public bool IsImpersonating { get; private set; }

        private bool OpenThreadToken()
        {
            //
            // Open the thread token; if there is no thread token, get one from
            // the process token by impersonating self.
            //

            SafeTokenHandle threadHandleBefore = _threadHandle;
            var error = NativeMethods.OpenThreadToken(
                          TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges,
                          WinSecurityContext.Process,
                          out _threadHandle);

            unchecked { error &= ~(int)0x80070000; }

            if (error == 0)
            {
                return true;
            }

            _threadHandle = threadHandleBefore;

            const int errorNoToken = 0x3f0;
            if (error != errorNoToken)
            {
                return false;
            }

            if (!NativeMethods.DuplicateTokenEx(
                            ProcessHandle,
                            TokenAccessLevels.Impersonate | TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges,
                            IntPtr.Zero,
                            SecurityImpersonationLevel.SecurityImpersonation,
                            TokenType.TokenImpersonation,
                            ref _threadHandle))
            {
                return false;
            }

            error = NativeMethods.SetThreadToken(_threadHandle);
            unchecked { error &= ~(int)0x80070000; }

            if (error != 0)
            {
                return false;
            }

            IsImpersonating = true;
            return true;
        }

        private TlsContents()
        {

        }

        public static TlsContents? Create()
        {
            if (ProcessHandle.IsInvalid)
            {
                lock (SyncRoot)
                {
                    if (ProcessHandle.IsInvalid && NativeMethods.OpenProcessToken(
                                        NativeMethods.GetCurrentProcess(),
                                        TokenAccessLevels.Duplicate,
                                        out SafeTokenHandle localProcessHandle))
                    {
                        ProcessHandle = localProcessHandle;
                    }
                }
            }

            var success = true;
            TlsContents? result = new TlsContents();
            try
            {
                // Make the sequence non-interruptible
            }
            finally
            {
                try
                {
                    success = result.OpenThreadToken();
                }
                finally
                {
                    if (!success)
                    {
                        result.Dispose();
                        result = null;
                    }
                }
            }

            return result;
        }

        private bool _disposed;

        ~TlsContents()
        {
            if (!_disposed)
            {
                Dispose(false);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing && _threadHandle != null)
            {
                _threadHandle.Dispose();
            }

            if (IsImpersonating)
            {
                NativeMethods.RevertToSelf();
            }

            _disposed = true;
        }

        public void IncrementReferenceCount() => ReferenceCountValue++;

        public int DecrementReferenceCount()
        {
            int result = --ReferenceCountValue;

            if (result == 0)
            {
                Dispose();
            }

            return result;
        }

        private static class NativeMethods
        {
            /// <summary>
            /// The DuplicateTokenEx function creates a new access token that duplicates an existing token. This function can create either a primary token or an impersonation token.
            /// </summary>
            /// <param name="existingTokenHandle">
            /// A handle to an access token opened with TOKEN_DUPLICATE access.
            /// </param>
            /// <param name="desiredAccess">
            /// Specifies the requested access rights for the new token.
            /// The DuplicateTokenEx function compares the requested access rights with the existing token's discretionary access control list (DACL) to determine which rights are granted or denied.
            /// To request the same access rights as the existing token, specify zero.
            /// To request all access rights that are valid for the caller, specify MAXIMUM_ALLOWED.
            /// For a list of access rights for access tokens, see Access Rights for Access-Token Objects.
            /// </param>
            /// <param name="tokenAttributes">
            /// A pointer to a SECURITY_ATTRIBUTES structure that specifies a security descriptor for the new token and determines whether child processes can inherit the token.
            /// If lpTokenAttributes is NULL, the token gets a default security descriptor and the handle cannot be inherited. If the security descriptor contains a system access control list (SACL), the token gets ACCESS_SYSTEM_SECURITY access right, even if it was not requested in dwDesiredAccess.
            /// </param>
            /// <param name="impersonationLevel">
            /// Specifies a value from the SECURITY_IMPERSONATION_LEVEL enumeration that indicates the impersonation level of the new token.
            /// </param>
            /// <param name="tokenType">
            /// Specifies one of the following values from the TOKEN_TYPE enumeration.
            /// TokenPrimary: The new token is a primary token that you can use in the CreateProcessAsUser function.
            /// TokenImpersonation: The new token is an impersonation token.
            /// </param>
            /// <param name="duplicateTokenHandle">
            /// A pointer to a HANDLE variable that receives the new token.
            /// </param>
            /// <returns>
            /// If the function succeeds, the function returns a nonzero value.
            /// If the function fails, it returns zero.To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-duplicatetokenex"/>
            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool DuplicateTokenEx(
                SafeTokenHandle existingTokenHandle,
                TokenAccessLevels desiredAccess,
                IntPtr tokenAttributes,
                SecurityImpersonationLevel impersonationLevel,
                TokenType tokenType,
                ref SafeTokenHandle duplicateTokenHandle);

            /// <summary>
            /// The RevertToSelf function terminates the impersonation of a client application.
            /// </summary>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero.To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-reverttoself"/>
            [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
            internal static extern bool RevertToSelf();

            /// <summary>
            /// The OpenProcessToken function opens the access token associated with a process.
            /// </summary>
            /// <param name="processToken">
            /// A handle to the process whose access token is opened. The process must have the PROCESS_QUERY_INFORMATION access permission.
            /// </param>
            /// <param name="desiredAccess">
            /// Specifies an access mask that specifies the requested types of access to the access token.
            /// These requested access types are compared with the discretionary access control list (DACL) of the token to determine which accesses are granted or denied.
            /// For a list of access rights for access tokens, see Access Rights for Access-Token Objects.
            /// </param>
            /// <param name="tokenHandle">
            /// A pointer to a handle that identifies the newly opened access token when the function returns.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero. To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken"/>
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool OpenProcessToken(IntPtr processToken, TokenAccessLevels desiredAccess, out SafeTokenHandle tokenHandle);

            /// <summary>
            /// Retrieves a pseudo handle for the current process.
            /// </summary>
            /// <returns>
            /// The return value is a pseudo handle to the current process.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentprocess"/>
            [DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "GetCurrentProcess", CharSet = CharSet.Unicode)]
            public static extern IntPtr GetCurrentProcess();

            /// <summary>
            /// The OpenThreadToken function opens the access token associated with a thread.
            /// </summary>
            /// <param name="threadHandle">
            /// A handle to the thread whose access token is opened.
            /// </param>
            /// <param name="dwDesiredAccess">
            /// Specifies an access mask that specifies the requested types of access to the access token.
            /// These requested access types are reconciled against the token's discretionary access control list (DACL) to determine which accesses are granted or denied.
            /// </param>
            /// <param name="bOpenAsSelf">
            /// TRUE if the access check is to be made against the process-level security context.
            /// FALSE if the access check is to be made against the current security context of the thread calling the OpenThreadToken function.
            /// The OpenAsSelf parameter allows the caller of this function to open the access token of a specified thread when the caller is impersonating a token at SecurityIdentification level.
            /// Without this parameter, the calling thread cannot open the access token on the specified thread because it is impossible to open executive-level objects by using the SecurityIdentification impersonation level.
            /// </param>
            /// <param name="phThreadToken">
            /// A pointer to a variable that receives the handle to the newly opened access token.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero.
            /// To get extended error information, call GetLastError.
            /// If the token has the anonymous impersonation level, the token will not be opened and OpenThreadToken sets ERROR_CANT_OPEN_ANONYMOUS as the error.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openthreadtoken"/>
            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern bool OpenThreadToken(IntPtr threadHandle, TokenAccessLevels dwDesiredAccess, bool bOpenAsSelf, out SafeTokenHandle phThreadToken);

            /// <summary>
            /// The OpenThreadToken function opens the access token associated with a thread.
            /// </summary>
            /// <param name="dwDesiredAccess">
            /// Specifies an access mask that specifies the requested types of access to the access token.
            /// These requested access types are reconciled against the token's discretionary access control list (DACL) to determine which accesses are granted or denied.
            /// For a list of access rights for access tokens, see Access Rights for Access-Token Objects.
            /// </param>
            /// <param name="dwOpenAs">
            /// A flag specifying whether the access check is to be made against the process-level security context, the current security context of the thread calling the OpenThreadToken function, or both.
            /// </param>
            /// <param name="phThreadToken">
            /// A pointer to a variable that receives the handle to the newly opened access token.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero.
            /// To get extended error information, call GetLastError.
            /// If the token has the anonymous impersonation level, the token will not be opened and OpenThreadToken sets ERROR_CANT_OPEN_ANONYMOUS as the error.
            /// </returns>
            /// <see cref="https://github.com/dotnet/corefx/blob/master/src/System.Security.AccessControl/src/System/Security/Principal/Win32.cs"/>
            internal static int OpenThreadToken(TokenAccessLevels dwDesiredAccess, WinSecurityContext dwOpenAs, out SafeTokenHandle phThreadToken)
            {
                var openAsSelf = dwOpenAs != WinSecurityContext.Thread;

                if (OpenThreadToken((IntPtr)(-2), dwDesiredAccess, openAsSelf, out phThreadToken))
                {
                    return 0;
                }

                if (dwOpenAs != WinSecurityContext.Both)
                {
                    phThreadToken = new SafeTokenHandle(IntPtr.Zero);
                    return Marshal.GetHRForLastWin32Error();
                }

                if (OpenThreadToken((IntPtr)(-2), dwDesiredAccess, false, out phThreadToken))
                {
                    return 0;
                }

                phThreadToken = new SafeTokenHandle(IntPtr.Zero);
                return Marshal.GetHRForLastWin32Error();
            }

            /// <summary>
            /// The SetThreadToken function assigns an impersonation token to a thread.
            /// The function can also cause a thread to stop using an impersonation token.
            /// </summary>
            /// <param name="threadHandle">
            /// A pointer to a handle to the thread to which the function assigns the impersonation token.
            /// If Thread is NULL, the function assigns the impersonation token to the calling thread.
            /// </param>
            /// <param name="hToken">
            /// A handle to the impersonation token to assign to the thread.
            /// This handle must have been opened with TOKEN_IMPERSONATE access rights.
            /// For more information, see Access Rights for Access-Token Objects.
            /// If Token is NULL, the function causes the thread to stop using an impersonation token.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadtoken"/>
            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern bool SetThreadToken(IntPtr threadHandle, SafeTokenHandle hToken);

            /// <summary>
            /// The SetThreadToken function assigns an impersonation token to the calling thread.
            /// The function can also cause the calling thread to stop using an impersonation token.
            /// </summary>
            /// <param name="hToken">
            /// A handle to the impersonation token to assign to the thread.
            /// This handle must have been opened with TOKEN_IMPERSONATE access rights.
            /// For more information, see Access Rights for Access-Token Objects.
            /// If Token is NULL, the function causes the thread to stop using an impersonation token.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// </returns>
            internal static int SetThreadToken(SafeTokenHandle hToken)
            {
                int hr = 0;
                if (!SetThreadToken(IntPtr.Zero, hToken))
                {
                    hr = Marshal.GetHRForLastWin32Error();
                }

                return hr;
            }
        }
    }
}
