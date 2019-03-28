// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// A Source is a descriptor for source code.
    ///
    /// It is returned from the debug adapter as part of a <code cref="IStackFrame"/> and it is used by clients when specifying breakpoints.
    /// </summary>
    public interface ISource
    {
        /// <summary>
        /// The short name of the source. Every source returned from the debug adapter has a name.
        /// When specifying a source to the debug adapter this name is optional.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The long (absolute) path of the source. It is not guaranteed that the source exists at this location.
        /// </summary>
        string Path { get; }

        /// <summary>
        /// If <code><see cref="SourceReference"/> &gt; 0</code> the contents of the source can be retrieved through
        /// the <code cref="ISourceCommand"/>. A sourceReference is only valid for a session, so it must not be used to persist a source.
        /// </summary>
        int SourceReference { get; }

        /// <summary>
        /// The (optional) origin of this source: possible values "internal module", "inlined content from source map", etc.
        /// </summary>
        string Origin { get; }

        /// <summary>
        /// Optional data that a debug adapter might want to loop through the client.
        /// The client should leave the data intact and persist it across sessions. The client should not interpret the data.
        /// </summary>
        object AdapterData { get; }
    }
}
