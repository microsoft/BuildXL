// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Context object used for node deserialization.
    /// </summary>
    public sealed class DeserializationContext
    {
        /// <summary>
        /// Gets the line map for the current file
        /// </summary>
        public LineMap LineMap { get; }

        /// <summary>
        /// Gets the current file being deserialized.
        /// </summary>
        public FileModuleLiteral CurrentFile { get; }

        /// <summary>
        /// Gets the stream reader.
        /// </summary>
        public BuildXLReader Reader { get; }

        /// <nodoc />
        public PathTable PathTable { get; }

        /// <nodoc />
        public DeserializationContext(FileModuleLiteral currentFile, BuildXLReader reader, PathTable pathTable, LineMap lineMap)
        {
            LineMap = lineMap;
            CurrentFile = currentFile;
            Reader = reader;
            PathTable = pathTable;
        }
    }
}
