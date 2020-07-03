// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
