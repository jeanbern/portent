using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace portent
{
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
        private Luid _luid;
        private readonly Thread _currentThread = Thread.CurrentThread;
        private readonly TlsContents? _tlsContents;

        public PrivilegeHolder(string privilegeName, out bool success)
        {
            var error = 0;
            _luid = LuidFromPrivilege(privilegeName, out var luidSuccess);
            if (!luidSuccess)
            {
                success = false;
                return;
            }

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
                    _tlsContents = TtlsSlotData;

                    if (_tlsContents == null)
                    {
                        _tlsContents = new TlsContents(out success);
                        TtlsSlotData = _tlsContents;
                    }
                    else
                    {
                        success = true;
                        _tlsContents.IncrementReferenceCount();
                    }

                    // Check if we have the privilege, this is different than having it enabled.
                    if (success && NativeMethods.HasPrivilege(_tlsContents.ThreadHandle, _luid))
                    {
                        const int errorNotAllAssigned = 0x514;

                        TokenPrivilege newState;
                        newState.PrivilegeCount = 1;
                        newState.Privileges.Luid = _luid;
                        newState.Privileges.Attributes = SePrivilegeAttributes.SePrivilegeEnabled;

                        TokenPrivilege previousState = new TokenPrivilege();

                        // Place the new privilege on the thread token and remember the previous state.
                        if (!NativeMethods.AdjustTokenPrivileges(
                            _tlsContents.ThreadHandle,
                            false,
                            ref newState,
                            TokenPrivilege.SizeOf,
                            out previousState,
                            out _))
                        {
                            error = Marshal.GetLastWin32Error();
                        }
                        else if (errorNotAllAssigned == Marshal.GetLastWin32Error())
                        {
                            error = errorNotAllAssigned;
                        }
                        else
                        {
                            // This is the initial state that revert will have to go back to
                            _initialState = (previousState.Privileges.Attributes & SePrivilegeAttributes.SePrivilegeEnabled) != 0;

                            // Remember whether state has changed at all
                            _stateWasChanged = true;

                            // If we had to impersonate, or if the privilege state changed we'll need to revert
                            _needToRevert = _tlsContents.IsImpersonating || _stateWasChanged;
                        }
                    }
                    else
                    {
                        Reset();
                        error = 0x5;
                        // Nothing else gets run. We return.
                    }
                }
                finally
                {
                    if (!_needToRevert)
                    {
                        Reset();
                    }
                }
            }

            success = error == 0;
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
                        out var resultLength
                    )
                    && tokenInformation.PrivilegeCount <= 30)
                {
                    var privilegeStart = (int*)tokenInformation.FakePrivileges;
                    for (var i = 0; i < tokenInformation.PrivilegeCount; i++)
                    {
                        var high = privilegeStart[0];
                        var low = privilegeStart[1];
                        var flag = privilegeStart[2];
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