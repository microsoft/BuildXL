// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
