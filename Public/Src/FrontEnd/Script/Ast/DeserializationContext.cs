// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
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
