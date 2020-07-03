// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if !NET_STANDARD_20
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
            #if NET_STANDARD_20
                return System.Environment.UserName;
            #else
                return WindowsIdentity.GetCurrent().Name;
            #endif
        }
    }
}
