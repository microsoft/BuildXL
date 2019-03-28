// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// A Thread.
    /// </summary>
    public interface IThread
    {
        /// <summary>
        /// Unique identifier for the thread.
        /// </summary>
        int Id { get; }

        /// <summary>
        /// A name of the thread.
        /// </summary>
        string Name { get; }
    }
}
