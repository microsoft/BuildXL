// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if !NETSTANDARD2_0
using System.Security.Principal;
#endif

namespace BuildXL.Native.Users.Windows
{
    /// <inheritdoc />
    public sealed class UserUtilitiesWin : IUserUtilities
    {
        /// <inheritdoc />
        public string CurrentUserName()
        {
            #if NETSTANDARD2_0
                return System.Environment.UserName;
            #else
                return WindowsIdentity.GetCurrent().Name;
            #endif
        }
    }
}
