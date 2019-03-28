// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Runtime
{
    /// <summary>
    /// The unique set of types that objects can have at runtime in BuildXL.
    /// </summary>
    public enum RuntimeTypeId : byte
    {
        /// <summary>
        /// Unknown invalid typeof value.
        /// </summary>
        Unknown = 0,

        /// <nodoc />
        Undefined,

        /// <nodoc />
        String,

        /// <nodoc />
        Boolean,

        /// <nodoc />
        Number,

        /// <nodoc />
        Array,

        /// <nodoc />
        Object,

        /// <nodoc />
        Enum,

        /// <nodoc />
        Function,

        /// <nodoc />
        ModuleLiteral,

        /// <nodoc />
        Map,

        /// <nodoc />
        Set,

        /// <nodoc />
        Path,

        /// <nodoc />
        File,

        /// <nodoc />
        Directory,

        /// <nodoc />
        StaticDirectory,

        /// <nodoc />
        SharedOpaqueDirectory,

        /// <nodoc />
        ExclusiveOpaqueDirectory,

        /// <nodoc />
        SourceTopDirectory,

        /// <nodoc />
        SourceAllDirectory,

        /// <nodoc />
        FullStaticContentDirectory,

        /// <nodoc />
        PartialStaticContentDirectory,

        /// <nodoc />
        RelativePath,

        /// <nodoc />
        PathAtom,
    }
}
