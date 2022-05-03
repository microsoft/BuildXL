// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Native.Users.Unix
{
    /// <inheritdoc />
    public sealed class UserUtilitiesUnix : IUserUtilities
    {
        /// <inheritdoc />
        public string CurrentUserName()
        {
#if NETCOREAPP
            // This approximates what the WindowsIdentity implementation is returning eg. 'macbookpro\admin'
            return Environment.UserDomainName + "\\" + Environment.UserName;
#else
            throw new NotImplementedException();
#endif
        }
    }
}
