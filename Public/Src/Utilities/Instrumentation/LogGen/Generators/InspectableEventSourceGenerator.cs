// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGen.Generators
{
    /// <summary>
    /// ETW event source which generates a proper manifest and logs self describing events.
    /// </summary>
    internal sealed class InspectableEventSourceGenerator : GeneratorBase
    {
        /// <summary>
        /// Current event source generator allows to inspect (i.e., intersect) every logger invocation
        /// in a generic form.
        /// To do this, partial method invocation is generated for every call and author of the specific
        /// logger may decide to provide <code>partial void (int logEventId, EventLevel level, string message)</code>
        /// to process each logged event in a special way.
        /// </summary>
        private const string InspectMessageFunctionName = "InspectMessage";

        private static readonly SymbolDisplayFormat s_symbolDisplayFormat =
            new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        /// <inheritdoc/>
        public override void GenerateAdditionalLoggerMembers()
        {
        }

        /// <inheritdoc/>
        public override void GenerateLogMethodBody(LoggingSite site, Func<string> getMessageExpression)
        {
            var locationParameter = GetLocationParameter(site);
            m_codeGenerator.Ln("if ({0})", nameof(LoggerBase.InspectMessageEnabled));
            using (m_codeGenerator.Br)
            {
                m_codeGenerator.Ln("{0}({1}, EventLevel.{2}, {3}, {4});", InspectMessageFunctionName, site.Id, site.Level, getMessageExpression(), locationParameter);
            }
            m_codeGenerator.Ln();
        }

        private static string GetLocationParameter(LoggingSite site)
        {
            var location = site.Payload.FirstOrDefault(p => p.Type.ToDisplayString(s_symbolDisplayFormat) == "BuildXL.Utilities.Instrumentation.Common.Location");
            return location?.Name ?? "null";
        }
    }
}
