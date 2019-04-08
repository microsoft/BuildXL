// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace TypeScript.Net.Binding
{
    /// <nodoc />
    [Flags]
    public enum ContainerFlags
    {
        /// <summary>
        /// The current node is not a container, and no container manipulation should happen before
        /// recursing into it.
        /// </summary>
        None = 0,

        /// <summary>
        /// The current node is a container.  It should be set as the current container (and block-
        /// container) before recursing into it.  The current node does not have locals.  Examples:
        ///
        ///      Classes, ObjectLiterals, TypeLiterals, Interfaces...
        /// </summary>
        IsContainer = 1 << 0,

        /// <summary>
        /// The current node is a block-scoped-container.  It should be set as the current block-
        /// container before recursing into it.  Examples:
        ///
        ///      Blocks (when not parented by functions), Catch clauses, For/For-in/For-of statements...
        /// </summary>
        IsBlockScopedContainer = 1 << 1,

        /// <nodoc />
        HasLocals = 1 << 2,

        /// <summary>
        /// The current node is a container that also contains locals.  Examples:
        ///
        ///      Functions, Methods, Modules, Source-files.
        /// </summary>
        IsContainerWithLocals = IsContainer | HasLocals,
    }
}
