// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Roxis.Common
{
    /// <summary>
    /// Type of <see cref="Command"/> supported by Roxis. Used for serialization and deserialization only.
    /// </summary>
    public enum CommandType
    {
        Get,
        Set,
        CompareExchange,
        CompareRemove,
        Remove,
        PrefixEnumerate,
    }
}
