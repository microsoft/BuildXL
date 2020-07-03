// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
