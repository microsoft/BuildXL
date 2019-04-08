// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// A Stackframe contains the source location.
    /// </summary>
    public interface IStackFrame
    {
        /// <summary>
        /// An identifier for the stack frame. This id can be used to retrieve the scopes of the frame with the 'scopesRequest'.
        /// </summary>
        int Id { get; }

        /// <summary>
        /// The name of the stack frame, typically a method name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The optional source of the frame.
        /// </summary>
        ISource Source { get; }

        /// <summary>
        /// The line within the file of the frame. If source is null or doesn't exist, line is 0 and must be ignored.
        /// </summary>
        int Line { get; }

        /// <summary>
        /// The column within the line. If source is null or doesn't exist, column is 0 and must be ignored.
        /// </summary>
        int Column { get; }
    }
}
