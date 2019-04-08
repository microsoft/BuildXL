// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native
{
    /// <summary>
    /// Utilities and helpers for interacting with users and accounts
    /// </summary>
    public interface IUserUtilities
    {
        /// <summary>
        /// Gets the current user's name.
        /// </summary>
        string CurrentUserName();
    }
}
