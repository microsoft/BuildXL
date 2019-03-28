// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Result of parsing a file.
    /// </summary>
    public class SourceFileParseResult : ParseResult<SourceFile>
    {
        internal SourceFileParseResult(SourceFile sourceFile, FileModuleLiteral module, QualifierSpaceId qualifierSpaceId)
            : base(sourceFile)
        {
            Contract.Requires(sourceFile != null || module != null, "sourceFile or module should not be null");

            SourceFile = sourceFile;
            Module = module;
            QualifierSpaceId = qualifierSpaceId;
        }

        internal SourceFileParseResult(int errorCount)
            : base(errorCount)
        {
            Contract.Requires(errorCount != 0, "If parsing was successful, another constructor should be used that takes SourceFile and ModuleLiteral.");

            QualifierSpaceId = QualifierSpaceId.Invalid;
        }

        /// <nodoc />
        public SourceFile SourceFile { get; }

        /// <nodoc />
        public FileModuleLiteral Module { get; }

        /// <nodoc />
        public QualifierSpaceId QualifierSpaceId { get; }

        /// <nodoc />
        public static SourceFileParseResult Read(BuildXLReader reader, GlobalModuleLiteral outerScope, ModuleRegistry moduleRegistry, PathTable pathTable)
        {
            var moduleLiteral = FileModuleLiteral.Read(reader, pathTable, outerScope, moduleRegistry);

            var context = new DeserializationContext(moduleLiteral, reader, pathTable, moduleLiteral.LineMap);

            var sourceFile = new SourceFile(AbsolutePath.Invalid, CollectionUtilities.EmptyArray<Declaration>());
            var qualifierSpaceId = context.Reader.ReadQualifierSpaceId();

            return new SourceFileParseResult(sourceFile, moduleLiteral, qualifierSpaceId);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            Module.Serialize(writer);

            // SourceFile itself doesn't have any useful information.
            writer.Write(QualifierSpaceId);
        }
    }
}
