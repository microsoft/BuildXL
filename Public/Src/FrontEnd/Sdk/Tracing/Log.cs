// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.FrontEnd.Sdk.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            2809,
            EventGenerators = Generators.ManifestedEventSource,
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Parser,
            Message = Events.LabeledProvenancePrefix + "Unsupported Qualifier Value. The reference passes a qualifier key '{unsupportedQualifierValue.QualifierKey}' with value '{unsupportedQualifierValue.InvalidValue}'. Legal values are: '{unsupportedQualifierValue.LegalValues}'. Please update the import statement to override the key with a legal value.",
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Diagnostics | Events.Keywords.UserError))]
        public abstract void ErrorUnsupportedQualifierValue(LoggingContext context, Location location, UnsupportedQualifierValue unsupportedQualifierValue);

        [GeneratedEvent(
            2810,
            EventGenerators = Generators.ManifestedEventSource,
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Parser,
            Message = "Duplicate frontend registration. FrontEnd '{frontEndType}' is registered twice.",
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Diagnostics | Events.Keywords.UserError))]
        public abstract void DuplicateFrontEndRegistration(LoggingContext context, string frontEndType);

        [GeneratedEvent(
            2811,
            EventGenerators = Generators.ManifestedEventSource,
            EventLevel = Level.Error,
            EventTask = (ushort)Events.Tasks.Parser,
            Message = "Duplicate resolver registration. Resolver '{resolverKind}' is registered by both frontEnd '{frontEndTypeFirst}' and frontend '{frontEndTypeSecond}'. Only one is allowed.",
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Diagnostics | Events.Keywords.UserError))]
        public abstract void DuplicateResolverRegistration(LoggingContext context, string resolverKind, string frontEndTypeFirst, string frontEndTypeSecond);
    }
}
