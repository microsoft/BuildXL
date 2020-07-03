// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Launch request; value of the <see cref="IRequest.Command"/> field is "launch".
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface ILaunchCommand : ICommand<ILaunchResult>
    {
        bool NoDebug { get; }
    }

    /// <summary>
    /// Response to <see cref="ILaunchCommand"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface ILaunchResult { }
}
