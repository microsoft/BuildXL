// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Attach request; value of the <see cref="IRequest.Command"/> field is "attach".
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IAttachCommand : ICommand<IAttachResult> { }

    /// <summary>
    /// Response to <see cref="IAttachCommand"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IAttachResult { }
}
