// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
