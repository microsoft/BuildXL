// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// On error, that is, whenever <code cref="IResponse.Success"/> is false,
    /// this body can provide more details.
    /// </summary>
    public interface IErrorResult
    {
        /// <summary>
        /// An optional, structured error message.
        /// </summary>
        IMessage Error { get; }
    }
}
