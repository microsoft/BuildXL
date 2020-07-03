// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
