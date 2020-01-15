// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Principal;

namespace BuildXL.Native.Users.Windows
{
    /// <inheritdoc />
    public sealed class UserUtilitiesWin : IUserUtilities
    {
        /// <inheritdoc />
        public string CurrentUserName()
        {
            return WindowsIdentity.GetCurrent().Name;
        }
    }
}
