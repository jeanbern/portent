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
using JetBrains.Annotations;

namespace Portent
{
    /// <summary>
    /// Contains values specifying the attributes available for privileges.
    /// </summary>
    /// <see>
    /// <cref>https://github.com/dotnet/corefx/blob/master/src/Common/src/Interop/Windows/Advapi32/Interop.ProcessOptions.cs</cref>
    /// </see>
    [Flags]
    [PublicAPI]
    public enum SePrivilegeAttributes : uint
    {
        None = 0,
        SePrivilegeDisabled = None,
        SePrivilegeEnabledByDefault = 0x00000001,
        SePrivilegeEnabled = 0x00000002,
        SePrivilegeRemoved = 0x00000004,
        SePrivilegeUsedForAccess = 0x80000000
    }
}
