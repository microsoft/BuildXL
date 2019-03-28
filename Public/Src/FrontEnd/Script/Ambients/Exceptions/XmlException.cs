// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Ambients.Exceptions
{
    /// <nodoc />
    public sealed class XmlUnsuportedTypeForSerializationException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public XmlUnsuportedTypeForSerializationException(string encounteredType, ErrorContext errorContext)
            : base("Unsupported type for xml writing", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(encounteredType));

            EncounteredType = encounteredType;
        }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string EncounteredType { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportXmlUnsuportedTypeForSerialization(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class XmlInvalidStructureException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public XmlInvalidStructureException(string encounteredType, string expectedType, string fieldName, string nodetype, ErrorContext errorContext)
            : base("Unsupported type for xml writing", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(encounteredType));

            EncounteredType = encounteredType;
            ExpectedType = expectedType;
            FieldName = fieldName;
            NodeType = NodeType;
        }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string EncounteredType { get; }

        /// <summary>
        /// Gets the expected type
        /// </summary>
        public string ExpectedType { get; }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string FieldName { get; }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string NodeType { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportXmlInvalidStructure(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class XmlReadException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public XmlReadException(string filePath, int line, int column, string xmlErrorMessage, ErrorContext errorContext)
            : base("Xml read error", errorContext)
        {
            FilePath = filePath;
            Line = line;
            Column = column;
            XmlErrorMessage = xmlErrorMessage;
        }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets the expected type
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string XmlErrorMessage { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportXmlReadError(environment, this, location);
        }
    }
}
