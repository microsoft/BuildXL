// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Native.Users.Unix
{
    /// <inheritdoc />
    public sealed class UserUtilitiesUnix : IUserUtilities
    {
        /// <inheritdoc />
        public string CurrentUserName()
        {
#if NET_CORE
            // This approximates what the WindowsIdentity implementation is returning eg. 'macbookpro\admin'
            return Environment.UserDomainName + "\\" + Environment.UserName;
#else
            throw new NotImplementedException();
#endif
        }
    }
}
