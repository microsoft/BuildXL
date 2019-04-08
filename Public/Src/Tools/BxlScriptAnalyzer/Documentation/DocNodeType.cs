// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    /// <summary>
    /// Types of nodes
    /// </summary>
    public enum DocNodeType
    {
        /// <nodoc />
        Namespace,

        /// <nodoc />
        Interface,

        /// <nodoc />
        Type,

        /// <nodoc />
        Enum,

        /// <nodoc />
        Function,

        /// <nodoc />
        InstanceFunction,

        /// <nodoc />
        Value,

        /// <nodoc />
        Property,

        /// <nodoc />
        EnumMember,
    }
}
