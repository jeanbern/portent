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
    /// <see cref="https://github.com/dotnet/corefx/blob/master/src/System.Security.Principal.Windows/src/Microsoft/Win32/SafeHandles/SafeAccessTokenHandle.cs"/>
    internal sealed class SafeTokenHandle : SafeHandle
    {
        private SafeTokenHandle() : base(IntPtr.Zero, true) { }

        // 0 is an Invalid Handle
        internal SafeTokenHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            SetHandle(handle);
        }

        internal static SafeTokenHandle InvalidHandle
        {
            get { return new SafeTokenHandle(IntPtr.Zero); }
        }

        public override bool IsInvalid
        {
            get
            { return handle == new IntPtr(0) || handle == new IntPtr(-1); }
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CloseHandle(IntPtr handle);
        }
    }
}
