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
using System.Threading;

namespace portent
{
    /// <summary>
    ///
    /// </summary>
    /// <see cref="https://github.com/dotnet/corefx/blob/master/src/System.Security.AccessControl/src/System/Security/AccessControl/Privilege.cs"/>
    internal class PrivilegeHolder : IDisposable
    {
        private static Luid LuidFromPrivilege(string privilege, out bool success)
        {
            Luid luid;
            luid.LowPart = 0;
            luid.HighPart = 0;

            success = NativeMethods.LookupPrivilegeValue(null, privilege, out luid);
            return luid;
        }

        [ThreadStatic]
        private static TlsContents? TtlsSlotData;

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

            TokenPrivilege newState;
            newState.PrivilegeCount = 1;
            newState.Privileges.Luid = _luid;
            newState.Privileges.Attributes = SePrivilegeAttributes.SePrivilegeEnabled;

            TokenPrivilege previousState;

            // Place the new privilege on the thread token and remember the previous state.
            if (!NativeMethods.AdjustTokenPrivileges(
                _tlsContents.ThreadHandle,
                false,
                ref newState,
                TokenPrivilege.SizeOf,
                out previousState,
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
                    var _tlsContents = TtlsSlotData;
                    if (_tlsContents == null)
                    {
                        TtlsSlotData = _tlsContents = TlsContents.Create();
                    }
                    else
                    {
                        _tlsContents.IncrementReferenceCount();
                    }

                    if (_tlsContents != null)
                    {
                        holder = new PrivilegeHolder(_tlsContents, luid);
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

        private PrivilegeHolder(TlsContents contents, Luid luid)
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

            if (_needToRevert)
            {
                if (!_needToRevert)
                {
                    return;
                }

                if (!_currentThread.Equals(Thread.CurrentThread))
                {
                    throw new InvalidOperationException("Start and end thread must be the same.");
                }

                // This code must be eagerly prepared and non-interruptible.
                int error = 0;
                try
                {
                    // The payload is entirely in the finally block
                    // This is how we ensure that the code will not be
                    // interrupted by catastrophic exceptions
                }
                finally
                {
                    bool success = true;

                    try
                    {
                        // Only call AdjustTokenPrivileges if we're not going to be reverting to self,
                        // on this Revert, since doing the latter obliterates the thread token anyway

                        if (_stateWasChanged
                            && _tlsContents != null
                            && (_tlsContents.ReferenceCountValue > 1
                              || !_tlsContents.IsImpersonating))
                        {
                            TokenPrivilege newState;
                            newState.PrivilegeCount = 1;
                            newState.Privileges.Luid = _luid;
                            newState.Privileges.Attributes = _initialState ? SePrivilegeAttributes.SePrivilegeEnabled : SePrivilegeAttributes.SePrivilegeDisabled;

                            if (!NativeMethods.AdjustTokenPrivileges(
                                              _tlsContents.ThreadHandle,
                                              false,
                                              ref newState,
                                              0,
                                              out _,
                                              out _))
                            {
                                error = Marshal.GetLastWin32Error();
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
            }

            if (disposing)
            {
                _tlsContents?.Dispose();
            }

            _disposed = true;
        }

        private static class NativeMethods
        {
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false, EntryPoint = "LookupPrivilegeValueW")]
            internal static extern bool LookupPrivilegeValue([MarshalAs(UnmanagedType.LPTStr)] string? lpSystemName, [MarshalAs(UnmanagedType.LPTStr)] string lpName, out Luid lpLuid);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool AdjustTokenPrivileges(
                SafeTokenHandle tokenHandle,
                bool disableAllPrivileges,
                ref TokenPrivilege newState,
                uint bufferLength,
                out TokenPrivilege previousState,
                out uint returnLength);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool GetTokenInformation(
                SafeTokenHandle tokenHandle,
                TokenInformationClass tokenInformationClass,
                in FakeTokenPrivileges tokenInformation,
                uint tokenInformationLength,
                out uint returnLength);

            internal static unsafe bool HasPrivilege(SafeTokenHandle tokenHandle, Luid privilegeLuid)
            {
                var tokenInformation = new FakeTokenPrivileges(1);
                if (GetTokenInformation(tokenHandle,
                        TokenInformationClass.TokenPrivileges,
                        in tokenInformation,
                        sizeof(uint) + (tokenInformation.PrivilegeCount * 12),
                        out _
                    )
                    && tokenInformation.PrivilegeCount <= 30)
                {
                    var privilegeStart = (int*)tokenInformation.FakePrivileges;
                    for (var i = 0; i < tokenInformation.PrivilegeCount; i++)
                    {
                        var high = privilegeStart[0];
                        var low = privilegeStart[1];
                        privilegeStart += 3;
                        if (high == privilegeLuid.HighPart && low == privilegeLuid.LowPart)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
