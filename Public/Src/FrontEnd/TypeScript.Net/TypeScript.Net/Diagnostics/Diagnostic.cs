// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Diagnostics
{
    /// <nodoc />
    public enum DiagnosticCategory
    {
        /// <nodoc />
        Warning,

        /// <nodoc />
        Error,

        /// <nodoc />
        Message,
    }

    /// <summary>
    /// File-level diagnostic object.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public sealed class Diagnostic
    {
        /// <nodoc />
        public Diagnostic([JetBrains.Annotations.NotNull]ISourceFile file, int start, int length, [JetBrains.Annotations.NotNull]Message messageText, DiagnosticCategory category, int code)
        {
            File = file;
            Start = start;
            Length = length;
            MessageText = messageText;
            Category = category;
            Code = code;
        }

        /// <nodoc />
        public Diagnostic([JetBrains.Annotations.NotNull]string text, DiagnosticCategory category, int code)
        {
            MessageText = text;
            Category = category;
            Code = code;
        }

        /// <summary>
        /// Creates diagnostic instance for the file.
        /// </summary>
        public static Diagnostic CreateFileDiagnostic([JetBrains.Annotations.NotNull]ISourceFile file, int start, int length, IDiagnosticMessage message, params object[] args)
        {
            Contract.Requires(start >= 0, "start must be non-negative");
            Contract.Requires(length >= 0, "length must be non-negative");

            var text = DiagnosticUtilities.GetLocaleSpecificMessage(message);

            // arguments is a javascript concept!!
            if (args?.Length > 0)
            {
                text = DiagnosticUtilities.FormatStringFromArgs(text, args);
            }

            return new Diagnostic(file, start, length, text, message.Category, message.Code);
        }

        /// <summary>
        /// Creates global, compilation-wide diagnostic.
        /// </summary>
        public static Diagnostic CreateCompilerDiagnostic(IDiagnosticMessage message, params object[] args)
        {
            var text = DiagnosticUtilities.GetLocaleSpecificMessage(message);

            if (args?.Length > 0)
            {
                text = DiagnosticUtilities.FormatStringFromArgs(text, args);
            }

            return new Diagnostic(text, message.Category, message.Code);
        }

        /// <summary>
        /// Creates diagnostic for specific node.
        /// </summary>
        public static Diagnostic CreateDiagnosticForNode([JetBrains.Annotations.NotNull]INode node, [JetBrains.Annotations.NotNull]IDiagnosticMessage message, params object[] args)
        {
            var sourceFile = NodeStructureExtensions.GetSourceFile(node);
            var span = DiagnosticUtilities.GetErrorSpanForNode(sourceFile, node);

            return CreateFileDiagnostic(sourceFile, span.Start, span.Length, message, args);
        }

        /// <summary>
        /// Creates diagnostic for specific location
        /// </summary>
        public static Diagnostic CreateDiagnosticAtLocation([JetBrains.Annotations.NotNull]ISourceFile sourceFile, Location location, int length, [JetBrains.Annotations.NotNull]IDiagnosticMessage message, params object[] args)
        {
            var position = location.GetAbsolutePosition(sourceFile);

            return CreateFileDiagnostic(sourceFile, position, length, message, args);
        }

        /// <nodoc />
        public static Diagnostic CreateDiagnosticForNode([JetBrains.Annotations.NotNull]INode node, [JetBrains.Annotations.NotNull]IDiagnosticMessage message)
        {
            var sourceFile = NodeStructureExtensions.GetSourceFile(node);
            var span = DiagnosticUtilities.GetErrorSpanForNode(sourceFile, node);

            return CreateFileDiagnostic(sourceFile, span.Start, span.Length, message);
        }

        /// <summary>
        /// Creates diagnostic for specific node with a message chain.
        /// </summary>
        public static Diagnostic CreateDiagnosticForNodeFromMessageChain([JetBrains.Annotations.NotNull]INode node, [JetBrains.Annotations.NotNull]DiagnosticMessageChain messageChain)
        {
            var sourceFile = NodeStructureExtensions.GetSourceFile(node);
            var span = DiagnosticUtilities.GetErrorSpanForNode(sourceFile, node);

            Message message = messageChain.Next != null ? messageChain : (Message)messageChain.MessageText;

            return new Diagnostic(
                sourceFile,
                span.Start,
                span.Length,
                message,
                messageChain.Category,
                messageChain.Code);
        }

        /// <nodoc />
        [CanBeNull]
        public ISourceFile File { get; }

        /// <nodoc />
        public int? Start { get; }

        /// <nodoc />
        public int? Length { get; }

        /// <nodoc />
        [JetBrains.Annotations.NotNull]
        public Message MessageText { get; }

        /// <nodoc />
        public DiagnosticCategory Category { get; }

        /// <nodoc />
        public int Code { get; }

        /// <nodoc />
        public int TextSpanEnd => (Start ?? 0) + (Length ?? 0);

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{File?.Name}({Start}): {Category} {Code}: {MessageText}");
        }
    }
}
