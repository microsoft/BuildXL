// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.CodeAnalysis;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.LogGen.Generators
{
    /// <summary>
    /// ETW event source which generates a proper manifest and logs self describing events.
    /// </summary>
    internal sealed class ManifestedEventSource : GeneratorBase
    {
       /// <inheritdoc/>
        public override void GenerateLogMethodBody(LoggingSite site, Func<string> getMessageExpression)
        {
            m_codeGenerator.Ln("if ({0}.ETWLogger.Log.IsEnabled({1}, (EventKeywords){2}))", m_globalNamespace, CreateEventLevel(site.Level), site.EventKeywords);
            using (m_codeGenerator.Br)
            {
                var relatedActivityIdArg = site.EventOpcode == (int)EventOpcode.Start ?
                    I($"{site.LoggingContextParameterName}.ParentActivityId") :
                    I($"{site.LoggingContextParameterName}.Session.RelatedActivityId");

                m_codeGenerator.Ln(
                    "{0}.ETWLogger.Log.{1}({2}{3}{4});",
                    m_globalNamespace,
                    site.Method.Name,
                    relatedActivityIdArg,
                    site.FlattenedPayload.Count > 0 ? ", " : string.Empty,
                    site.GetFlattenedPayloadArgs());
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<Tuple<string, string>> ConsumedNamespaces
        {
            get
            {
                yield return new Tuple<string, string>("System.Diagnostics.Tracing", "");
            }
        }

        private static string CreateEventLevel(Level level)
        {
            switch (level)
            {
                case Level.Critical:
                    return "EventLevel.Critical";
                case Level.Error:
                    return "EventLevel.Error";
                case Level.Informational:
                    return "EventLevel.Informational";
                case Level.LogAlways:
                    return "EventLevel.LogAlways";
                case Level.Verbose:
                    return "EventLevel.Verbose";
                case Level.Warning:
                    return "EventLevel.Warning";
                default:
                    Contract.Assert(false, "Unknown Level " + level.ToString());
                    return null;
            }
        }

        /// <inheritdoc/>
        public override void GenerateClass()
        {
            m_codeGenerator.Ln("namespace {0}", m_globalNamespace);
            using (m_codeGenerator.Br)
            {
                m_codeGenerator.Ln("using global::System;");
                m_codeGenerator.Ln("using global::System.CodeDom.Compiler;");
                m_codeGenerator.Ln("using global::BuildXL.Utilities.Instrumentation.Common;");
                m_codeGenerator.Ln("using global::System.Diagnostics.Tracing;");
                m_codeGenerator.Ln("using global::System.Runtime.CompilerServices;");
                m_codeGenerator.Ln();
                m_codeGenerator.GenerateSummaryComment("Output logger that logs event into ETW");
                m_codeGenerator.WriteGeneratedAttribute();
                m_codeGenerator.Ln("[EventSource(Name = \"{0}\")]", m_globalNamespace + ".ETWLogger");
                m_codeGenerator.Ln("public class ETWLogger : EventSource");
                using (m_codeGenerator.Br)
                {
                    m_codeGenerator.Ln("private static readonly ETWLogger s_log = new ETWLogger();");
                    m_codeGenerator.Ln();
                    m_codeGenerator.GenerateSummaryComment("Gets the primary event source instance.");
                    m_codeGenerator.Ln("public static ETWLogger Log");
                    using (m_codeGenerator.Br)
                    {
                        m_codeGenerator.Ln("get { return s_log; }");
                    }

                    m_codeGenerator.Ln();
                    m_codeGenerator.Ln("private ETWLogger()");
                    m_codeGenerator.Ln("#if NET_FRAMEWORK_451");
                    m_codeGenerator.Ln("  : base()");
                    m_codeGenerator.Ln("#else");
                    m_codeGenerator.Ln("  : base(EventSourceSettings.EtwSelfDescribingEventFormat)");
                    m_codeGenerator.Ln("#endif");
                    using (m_codeGenerator.Br)
                    {
                    }

                    m_codeGenerator.Ln();

                    // Generate an event method for each site
                    foreach (var site in m_loggingSites.Where(s => (s.EventGenerators & BuildXL.Utilities.Instrumentation.Common.Generators.ManifestedEventSource) != 0))
                    {
                        m_codeGenerator.GenerateSummaryComment(site.Method.Name);
                        m_codeGenerator.Ln("[Event(");
                        using (m_codeGenerator.Indent)
                        {
                            m_codeGenerator.Ln("{0},", site.Id);
                            m_codeGenerator.Ln("Level = {0},", CreateEventLevel(site.Level));
                            if (site.EventKeywords != 0)
                            {
                                m_codeGenerator.Ln("Keywords = (EventKeywords){0},", site.EventKeywords);
                            }

                            if (site.EventTask != 0)
                            {
                                m_codeGenerator.Ln("Task = (EventTask){0},", site.EventTask);
                            }

                            if (site.EventOpcode != 0)
                            {
                                m_codeGenerator.Ln("Opcode = (EventOpcode){0},", site.EventOpcode);
                            }

                            // The message for EventSource is tricky. LogGen allows a pretty flexible message that may
                            // consume particular nested fields or the ToString() value of a class/struct. We flatten the
                            // EventSource event payload, but someone might refer to the ToString() of an unflattened
                            // class/struct in the message. We cannot allow this because the unflattened items don't get
                            // to the EventSource payload
                            IEnumerable<string> disallowedInFormatString = site.Payload.Select(i => i.Name).Except(site.FlattenedPayload.Select(i => i.Address));
                            foreach (string disallowed in disallowedInFormatString)
                            {
                                if (site.SpecifiedMessageFormat.Contains('{' + disallowed + '}'))
                                {
                                    m_errorReport.ReportError(site.Method, "ManifestedEventSource format string may not contain ToString() of classes/structs.");
                                }
                            }

                            m_codeGenerator.Ln("Message = \"{0}\")]", site.GetNormalizedMessageFormat());
                        }

                        string arguments = string.Join(",", site.FlattenedPayload.Select(i => i.Type.ToDisplayString() + " " + i.AddressForMethodParameter));
                        string relatedActivityIdArg = "Guid relatedActivityId";
                        if (string.IsNullOrEmpty(arguments))
                        {
                            arguments = relatedActivityIdArg;
                        }
                        else
                        {
                            arguments = relatedActivityIdArg + ", " + arguments;
                        }

                        m_codeGenerator.Ln("public unsafe void {0}({1})", site.Method.Name, arguments);
                        using (m_codeGenerator.Br)
                        {
                            // No nice indent formating to avoid recursive usings
                            int numberOfBrackets = 0;

                            int parametersCount = site.FlattenedPayload.Count;
                            string dataParam;
                            if (parametersCount > 0)
                            {
                                dataParam = "data";
                                m_codeGenerator.Ln("EventSource.EventData* {0} = stackalloc EventSource.EventData[{1}];", dataParam, parametersCount);

                                for (var i = 0; i < parametersCount; i++)
                                {
                                    var param = site.FlattenedPayload[i];

                                    if (param.Type.TypeKind == TypeKind.Enum)
                                    {
                                        // Convert to int or long depending of the base type. This implementation mimics the one in the VarArg version.
                                        // TODO: Check if it is better (and possible) to trat enums as strings. What happens if the implementation disagrees with the manifest?
                                        switch (param.Type.BaseType.SpecialType)
                                        {
                                            case SpecialType.System_Int64:
                                            case SpecialType.System_UInt64:
                                                m_codeGenerator.Ln("long {0}Bytes = (long){0};", param.AddressForMethodParameter);
                                                m_codeGenerator.Ln("{0}[{1}].Size = 8;", dataParam, i);
                                                break;
                                            default:
                                                m_codeGenerator.Ln("int {0}Bytes = (int){0};", param.AddressForMethodParameter);
                                                m_codeGenerator.Ln("{0}[{1}].Size = 4;", dataParam, i);
                                                break;
                                        }

                                        m_codeGenerator.Ln("{0}[{1}].DataPointer = (IntPtr)(&{2}Bytes);", dataParam, i, param.AddressForMethodParameter);
                                        continue;
                                    }

                                    string typeString = param.Type.ToDisplayString();

                                    if (typeString == "System.Guid")
                                    {
                                        m_codeGenerator.Ln("{0}[{1}].DataPointer = (IntPtr)(&{2});", dataParam, i, param.AddressForMethodParameter);
                                        m_codeGenerator.Ln("{0}[{1}].Size = 16;", dataParam, i);
                                        continue;
                                    }

                                    switch (param.Type.SpecialType)
                                    {
                                        case SpecialType.System_String:
                                            m_codeGenerator.Ln("{0} = {0} ?? String.Empty;", param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("fixed (char* {0}Bytes = {0}) {{", param.AddressForMethodParameter);
                                            ++numberOfBrackets;
                                            m_codeGenerator.Ln("{0}[{1}].DataPointer = (IntPtr){2}Bytes;", dataParam, i, param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("{0}[{1}].Size = (({2}.Length + 1) * 2);", dataParam, i, param.AddressForMethodParameter);
                                            break;
                                        case SpecialType.System_Char:
                                        case SpecialType.System_Byte:
                                        case SpecialType.System_Int16:
                                        case SpecialType.System_Int32:
                                        case SpecialType.System_Int64:
                                        case SpecialType.System_UInt16:
                                        case SpecialType.System_UInt32:
                                        case SpecialType.System_UInt64:
                                        case SpecialType.System_Double:
                                            m_codeGenerator.Ln("{0}[{1}].DataPointer = (IntPtr)(&{2});", dataParam, i, param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("{0}[{1}].Size = sizeof({2});", dataParam, i, typeString);
                                            break;
                                        case SpecialType.System_Boolean:
                                            // WIN32 Bool is 4 bytes
                                            m_codeGenerator.Ln("int {0}Bytes = {0} ? 1 : 0;", param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("{0}[{1}].DataPointer = (IntPtr)(&{2}Bytes);", dataParam, i, param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("{0}[{1}].Size = 4;", dataParam, i);
                                            break;
                                        case SpecialType.System_DateTime:
                                            m_codeGenerator.Ln("long {0}Bytes = {0}.ToFileTimeUtc();", param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("{0}[{1}].DataPointer = (IntPtr)(&{2}Bytes);", dataParam, i, param.AddressForMethodParameter);
                                            m_codeGenerator.Ln("{0}[{1}].Size = sizeof(long);", dataParam, i);
                                            break;
                                        default:
                                            m_errorReport.ReportError(site.Method, "Parameter '{0}' is not supported by ManifestedEventSource", param.Address);
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                dataParam = "null";
                            }

                            m_codeGenerator.Ln("WriteEventWithRelatedActivityIdCore({0}, &relatedActivityId, {1}, {2});", site.Id, parametersCount, dataParam);

                            for (int idx = 0; idx < numberOfBrackets; ++idx)
                            {
                                m_codeGenerator.Ln("}");
                            }
                        }

                        m_codeGenerator.Ln();
                    }

                    // EventSource requires having nested classes for keywords and tasks to be able to generate an event manifest.
                    // Both are specified on the containing class of the method defining the LoggingSite. So we can get
                    // away with just checking and using the first since all LoggingSites within the same class will have
                    // the same setting
                    if (m_loggingSites.Count > 0 && m_loggingSites[0].KeywordsType != null)
                    {
                        m_codeGenerator.Ln();
                        m_codeGenerator.GenerateSummaryComment("Event Keywords");
                        m_codeGenerator.Ln("public static class Keywords");
                        using (m_codeGenerator.Br)
                        {
                            foreach (IFieldSymbol field in m_loggingSites[0].KeywordsType.GetMembers().OfType<IFieldSymbol>())
                            {
                                m_codeGenerator.GenerateSummaryComment(field.Name);
                                m_codeGenerator.Ln("public const EventKeywords {0} = (EventKeywords)({1});", field.Name, field.ConstantValue);
                                m_codeGenerator.Ln();
                            }
                        }
                    }

                    if (m_loggingSites.Count > 0 && m_loggingSites[0].TasksType != null)
                    {
                        m_codeGenerator.Ln();
                        m_codeGenerator.GenerateSummaryComment("Event Tasks");
                        m_codeGenerator.Ln("public static class Tasks");
                        using (m_codeGenerator.Br)
                        {
                            foreach (IFieldSymbol field in m_loggingSites[0].TasksType.GetMembers().OfType<IFieldSymbol>())
                            {
                                m_codeGenerator.GenerateSummaryComment(field.Name);
                                m_codeGenerator.Ln("public const EventTask {0} = (EventTask)({1});", field.Name, field.ConstantValue);
                                m_codeGenerator.Ln();
                            }
                        }
                    }

                    m_codeGenerator.Ln();
                }
            }
        }
    }
}
