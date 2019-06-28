// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.FrontEnd.Script.Tracing
{
    /// <summary>
    /// Policy errors found by lint rules
    /// </summary>
    public abstract partial class Logger
    {
        private const string PolicyProvenancePrefix = EventConstants.LabeledProvenancePrefix + "[{ruleName}] ";

        [GeneratedEvent(
            (ushort)LogEventId.GlobFunctionsAreNotAllowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = PolicyProvenancePrefix + "Globbing is not allowed.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportGlobFunctionsIsNotAllowed(LoggingContext context, Location location, string ruleName);

        [GeneratedEvent(
            (ushort)LogEventId.AmbientTransformerIsDisallowed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = PolicyProvenancePrefix + "'Transformer' namespace from the prelude is not allowed. Use 'Sdk.Transformers' module instead.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportAmbientTransformerIsDisallowed(LoggingContext context, Location location, string ruleName);

        [GeneratedEvent(
            (ushort)LogEventId.MissingTypeAnnotationOnTopLevelDeclaration,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = PolicyProvenancePrefix + "Type annotation is missing for top-level variable declaration '{variableDeclarationName}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportMissingTypeAnnotationOnTopLevelDeclaration(LoggingContext context, Location location, string variableDeclarationName, string ruleName);

        [GeneratedEvent(
            (ushort)LogEventId.NotAllowedTypeAnyOnTopLevelDeclaration,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = PolicyProvenancePrefix + "Type annotation 'any' is not allowed for top-level variable declaration '{variableDeclarationName}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportNotAllowedTypeAnyOnTopLevelDeclaration(LoggingContext context, Location location, string variableDeclarationName, string ruleName);

        [GeneratedEvent(
            (ushort)LogEventId.MissingPolicies,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = "The following policy rules were requested but not found: {missingPolicies}. Available policy rules are: {allAvailablePolicies}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]

        // TODO: computing provenance here is not straightforward, but it'd good to add
        public abstract void ReportMissingPolicies(LoggingContext context, string missingPolicies, string allAvailablePolicies);

        [GeneratedEvent(
            (ushort)LogEventId.FunctionShouldDeclareReturnType,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = PolicyProvenancePrefix + "Function '{functionName}' must declare its return type (and 'any' is not a valid one).",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void ReportFunctionShouldDeclareReturnType(LoggingContext context, Location location, string functionName, string ruleName);
    }
}

#pragma warning restore CA1823 // Unused field
#pragma warning restore SA1600 // Element must be documented
