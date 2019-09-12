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
    ///
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
            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern
            bool DuplicateTokenEx(SafeTokenHandle existingTokenHandle,
                TokenAccessLevels desiredAccess,
                IntPtr tokenAttributes,
                SecurityImpersonationLevel impersonationLevel,
                TokenType tokenType,
                ref SafeTokenHandle duplicateTokenHandle);

            [DllImport("advapi32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
            internal static extern bool RevertToSelf();

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool OpenProcessToken(IntPtr processToken, TokenAccessLevels desiredAccess, out SafeTokenHandle tokenHandle);

            [DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "GetCurrentProcess", CharSet = CharSet.Unicode)]
            public static extern IntPtr GetCurrentProcess();

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool OpenThreadToken(IntPtr threadHandle, TokenAccessLevels dwDesiredAccess, bool bOpenAsSelf, out SafeTokenHandle phThreadToken);

            /// <summary>
            ///
            /// </summary>
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

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool SetThreadToken(IntPtr threadHandle, SafeTokenHandle hToken);

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
