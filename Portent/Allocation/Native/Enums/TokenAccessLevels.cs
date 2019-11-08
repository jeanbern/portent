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
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace Portent
{
    /// <summary>
    /// Contains values specifying possible token access levels.
    /// </summary>
    /// <see>
    /// <cref>https://github.com/dotnet/corefx/blob/master/src/System.Security.Principal.Windows/src/System/Security/Principal/TokenAccessLevels.cs</cref>
    /// </see>
    [Flags]
    [SuppressMessage("Major Code Smell", "S4070:Non-flags enums should not be marked with \"FlagsAttribute\"", Justification = "Copied from Microsoft.")]
    [PublicAPI]
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
#pragma warning disable RCS1135 // Declare enum member with zero value (when enum has FlagsAttribute).
    public enum TokenAccessLevels
#pragma warning restore RCS1135 // Declare enum member with zero value (when enum has FlagsAttribute).
#pragma warning restore CA2217 // Do not mark enums with FlagsAttribute
    {
        AssignPrimary = 0x00000001,
        Duplicate = 0x00000002,
        Impersonate = 0x00000004,
        Query = 0x00000008,
        QuerySource = 0x00000010,
        AdjustPrivileges = 0x00000020,
        AdjustGroups = 0x00000040,
        AdjustDefault = 0x00000080,
        AdjustSessionId = 0x00000100,

#pragma warning disable RCS1157 // Composite enum value contains undefined flag.
        Read = 0x00020000 | Query,

        Write = 0x00020000 | AdjustPrivileges | AdjustGroups | AdjustDefault,

        AllAccess = 0x000F0000 |
                              AssignPrimary |
                              Duplicate |
                              Impersonate |
                              Query |
                              QuerySource |
                              AdjustPrivileges |
                              AdjustGroups |
                              AdjustDefault |
                              AdjustSessionId,
#pragma warning restore RCS1157 // Composite enum value contains undefined flag.

        MaximumAllowed = 0x02000000
    }
}
