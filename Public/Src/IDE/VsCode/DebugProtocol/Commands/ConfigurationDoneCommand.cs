// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// ConfigurationDone request; value of the <see cref="IRequest.Command"/> field is "configurationDone".
    ///
    /// The client of the debug protocol must send this request at the end of the sequence
    /// of configuration requests (which was started by the <code cref="IInitializedEvent"/>)
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IConfigurationDoneCommand : ICommand<IConfigurationDoneResult> { }

    /// <summary>
    /// Response to <see cref="IConfigurationDoneCommand"/>
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IConfigurationDoneResult { }
}
