// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Static information about a username
    /// </summary>
    public static class UserName
    {
        /// <summary>
        /// True if collection of usernames is allowed for Microsoft internal users
        /// </summary>
        public static bool IsInternalCollectionAllowed
        {
            get
            {
                return true;
            }
        }
    }
}
