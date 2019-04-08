// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Initialize request; value of the <see cref="IRequest.Command"/> field is "initialize".
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface IInitializeCommand : ICommand<ICapabilities>
    {
        /// <summary>
        /// The ID of the debugger adapter. Used to select or verify debugger adapter.
        /// </summary>
        string AdapterID { get; }

        /// <summary>
        /// If true all line numbers are 1-based (default).
        /// </summary>
        bool LinesStartAt1 { get; }

        /// <summary>
        /// If true all column numbers are 1-based (default).
        /// </summary>
        bool ColumnsStartAt1 { get; }

        /// <summary>
        /// Determines in what format paths are specified. Possible values are 'path' or 'uri'.
        /// The default is 'path', which is the native format.
        /// </summary>
        string PathFormat { get; }
    }
}
