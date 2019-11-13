// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Something with a string name.
    /// </summary>
    public interface IName
    {
        /// <summary>
        ///     Gets its name.
        /// </summary>
        string Name { get; }
    }
}
