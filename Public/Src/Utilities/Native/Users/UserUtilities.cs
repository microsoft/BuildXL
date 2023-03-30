// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Native.Users
{
    /// <summary>
    /// Static facade with utilities for interacting with users and accounts.
    /// Serves as an entry point for user interaction throughout BuildXL's code base and proxies its calls to platform specific implementations of IUserUtilities.
    /// </summary>
    public static class UserUtilities
    {
        private static readonly IUserUtilities s_userUtilities = OperatingSystemHelper.IsUnixOS
            ? new Unix.UserUtilitiesUnix()
            : new Windows.UserUtilitiesWin();

        /// <summary>
        /// See <see cref="IUserUtilities.CurrentUserName"/>
        /// </summary>
        public static string CurrentUserName(string userName) => string.IsNullOrEmpty(userName)
            ? s_userUtilities.CurrentUserName()
            : userName;
    }
}
