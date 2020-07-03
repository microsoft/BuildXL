// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
