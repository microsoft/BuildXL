// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Source request; value of the <see cref="IRequest.Command"/> field is "source".
    ///
    /// The request retrieves the source code for a given source reference.
    /// </summary>
    public interface ISourceCommand : ICommand<ISourceResult>
    {
        /// <summary>
        /// The reference to the source. This is the value received in
        /// <code cref="ISource.SourceReference"/> Source.reference.
        /// </summary>
        int SourceReference { get; }
    }

    /// <summary>
    /// Response to <code cref="ISourceCommand"/>.
    /// </summary>
    public interface ISourceResult
    {
        /// <summary>
        /// Content of the source reference.
        /// </summary>
        string Content { get; }
    }
}
