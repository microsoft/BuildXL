// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Native.Users
{
    /// <summary>
    /// Static facade with utilities for interacting with users and accounts.
    /// Serves as an entry point for user interaction throughout BuildXL's code base and proxies its calls to platform specific implementations of IUserUtilities.
    /// </summary>
    public static class UserUtilities
    {
        private static readonly IUserUtilities s_userUtilities = OperatingSystemHelper.IsUnixOS
            ? (IUserUtilities) new Unix.UserUtilitiesUnix()
            : (IUserUtilities) new Windows.UserUtilitiesWin();

        /// <summary>
        /// See <see cref="IUserUtilities.CurrentUserName"/>
        /// </summary>
        public static string CurrentUserName()
        {
            return EngineEnvironmentSettings.BuildXLUserName.Value ?? s_userUtilities.CurrentUserName();
        }
    }
}
