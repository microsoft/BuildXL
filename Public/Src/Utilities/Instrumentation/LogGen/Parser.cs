// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.LogGen.Generators;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Microsoft.CodeAnalysis.Diagnostic;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;

namespace BuildXL.LogGen
{
    internal sealed class Parser
    {
        private readonly Configuration m_configuration;
        private readonly ErrorReport m_errorReport;

        /// <summary>
        /// Mapping of Generator types to a factory to create a new generator.
        /// </summary>
        internal static Dictionary<EventGenerators, Func<GeneratorBase>> SupportedGenerators = new Dictionary<EventGenerators, Func<GeneratorBase>>()
        {
            { EventGenerators.InspectableLogger, () => new InspectableEventSourceGenerator() },
            { EventGenerators.ManifestedEventSource, () => new ManifestedEventSource() },
#if FEATURE_ARIA_TELEMETRY
            { EventGenerators.AriaV2, () => new AriaV2() },
#else
            { EventGenerators.AriaV2, () => new Noop() },
#endif
            { EventGenerators.Statistics, () => new BuildXLStatistic() },
        };

        public Parser(Configuration configuration, ErrorReport errorReport)
        {
            m_configuration = configuration;
            m_errorReport = errorReport;
        }

        public bool DiscoverLoggingSites(out List<LoggingSite> loggingSites)
        {
            loggingSites = new List<LoggingSite>();

            // First create a compilation to act upon values and run codegen
            var syntaxTrees = new ConcurrentBag<SyntaxTree>();

            CSharpParseOptions opts = new CSharpParseOptions(
                preprocessorSymbols: m_configuration.PreprocessorDefines.ToArray(),
                languageVersion: LanguageVersion.Latest);

            Parallel.ForEach(
                m_configuration.SourceFiles.Distinct(StringComparer.OrdinalIgnoreCase),
                file =>
                {
                    if (File.Exists(file))
                    {
                        string text = File.ReadAllText(file);
                        syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, path: file, options: opts));
                    }
                });

            var metadataFileReferences = new ConcurrentBag<MetadataReference>();
            Parallel.ForEach(
                m_configuration.References.Distinct(StringComparer.OrdinalIgnoreCase),
                reference =>
                {
                    if (File.Exists(reference))
                    {
                        metadataFileReferences.Add(MetadataReference.CreateFromFile(reference));
                    }
                });

            if (m_errorReport.Errors != 0)
            {
                return false;
            }

            Compilation compilation = CSharpCompilation.Create(
                "temp",
                syntaxTrees,
                metadataFileReferences,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    deterministic: true
                )
            );

            // Hold on to all of the errors. Most are probably ok but some may be relating to event definitions and
            // should cause errors
            MultiValueDictionary<string, Diagnostic> errorsByFile = new MultiValueDictionary<string, Diagnostic>(StringComparer.OrdinalIgnoreCase);
            foreach (Diagnostic d in compilation.GetDiagnostics())
            {
                if (d.Location == null || d.Location.SourceTree == null)
                {
                    continue; // TODO
                }

                if (d.Severity == DiagnosticSeverity.Error)
                {
                    Console.WriteLine(d.ToString());
                    errorsByFile.Add(d.Location.SourceTree.FilePath, d);
                }
            }

            List<INamedTypeSymbol> symbols = new List<INamedTypeSymbol>();

            FindTypesInNamespace(compilation.Assembly.GlobalNamespace, (symbol) => true, symbols, true);

            foreach (var symbol in symbols)
            {
                foreach (var member in symbol.GetMembers())
                {
                    IMethodSymbol method = member as IMethodSymbol;
                    if (method != null)
                    {
                        AttributeData attributeData;
                        if (GetGeneratedEventAttribute(method, out attributeData))
                        {
                            // Check the errors we stashed off to see if any pretain to this event. If so, display them
                            // as they may cause us to generate the event incorrectly
                            bool eventHasErrors = false;
                            IReadOnlyList<Diagnostic> diagnostics;
                            if (errorsByFile.TryGetValue(method.Locations[0].SourceTree.FilePath, out diagnostics))
                            {
                                foreach (Diagnostic d in diagnostics)
                                {
                                    if (d.Location.SourceSpan.OverlapsWith(method.Locations[0].SourceSpan) ||
                                        d.Location.SourceSpan.OverlapsWith(attributeData.ApplicationSyntaxReference.Span))
                                    {
                                        m_errorReport.ReportError(d.ToString());
                                        eventHasErrors = true;
                                    }
                                }
                            }

                            if (!eventHasErrors)
                            {
                                LoggingSite site;
                                if (!ParseAndValidateLogSite(method, attributeData, out site))
                                {
                                    return false;
                                }

                                loggingSites.Add(site);
                            }
                        }
                    }
                }
            }

            return true;
        }

        private bool ParseAndValidateLogSite(IMethodSymbol method, AttributeData attributeData, out LoggingSite loggingSite)
        {
            loggingSite = new LoggingSite();
            loggingSite.Aliases = this.m_configuration.Aliases;
            loggingSite.Method = method;

            if (!loggingSite.SetPayload(m_errorReport, method.Parameters.Skip(1)))
            {
                return false;
            }

            // Pull the data out of the GeneratedEventAttribute
            foreach (var argument in attributeData.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "EventLevel":
                        if (argument.Value.Value.GetType() != typeof(int))
                        {
                            m_errorReport.ReportError(method, "Unsupported EventLevel value '{0}'", argument.Value.Value.ToString());
                            return false;
                        }

                        int value = (int)argument.Value.Value;
                        switch (value)
                        {
                            case 0:
                                loggingSite.Level = Level.LogAlways;
                                break;
                            case 1:
                                loggingSite.Level = Level.Critical;
                                break;
                            case 2:
                                loggingSite.Level = Level.Error;
                                break;
                            case 3:
                                loggingSite.Level = Level.Warning;
                                break;
                            case 4:
                                loggingSite.Level = Level.Informational;
                                break;
                            case 5:
                                loggingSite.Level = Level.Verbose;
                                break;
                            default:
                                m_errorReport.ReportError(method, "Unsupported EventLevel value '{0}'", value);
                                break;
                        }

                        break;
                    case "EventGenerators":
                        loggingSite.EventGenerators = (EventGenerators)argument.Value.Value;
                        break;
                    case "Message":
                        loggingSite.SpecifiedMessageFormat = EscapeMessageString((string)argument.Value.Value);
                        break;
                    case "EventOpcode":
                        loggingSite.EventOpcode = (byte)argument.Value.Value;
                        break;
                    case "Keywords":
                        loggingSite.EventKeywords = (int)argument.Value.Value;
                        break;
                    case "EventTask":
                        loggingSite.EventTask = (ushort)argument.Value.Value;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(loggingSite.SpecifiedMessageFormat))
            {
                m_errorReport.ReportError(method, "Message is required");
                return false;
            }

            if (loggingSite.SpecifiedMessageFormat.StartsWith(EventConstants.LabeledProvenancePrefix, StringComparison.Ordinal) && method.Parameters.Length >= 2 && method.Parameters[1].Name != "location")
            {
                m_errorReport.ReportError(method, "Message is using provenance prefix information to indicate line information. Therefore the location must be the first parameter after the LoggingContext. This method declares '{0}' as that parameter", method.Parameters[1].Name);
                return false;
            }

            // Verify the message format
            string normalizedMessageFormat = loggingSite.GetNormalizedMessageFormat();
            normalizedMessageFormat = normalizedMessageFormat.Replace("{{", string.Empty).Replace("}}", string.Empty);
            int openBracketPos = -1;
            int curlyBracketPos = normalizedMessageFormat.IndexOfAny(new char[] { '{', '}' });
            while (curlyBracketPos >= 0)
            {
                if (openBracketPos < 0)
                {
                    if (normalizedMessageFormat[curlyBracketPos] == '}')
                    {
                        m_errorReport.ReportError(method, "Message format error: Found '}}' without matching '{{'");
                        return false;
                    }

                    openBracketPos = curlyBracketPos;
                }
                else
                {
                    if (normalizedMessageFormat[curlyBracketPos] == '{')
                    {
                        m_errorReport.ReportError(method, "Message format error: Found too many nested '{{'");
                        return false;
                    }

                    string format = normalizedMessageFormat.Substring(openBracketPos + 1, curlyBracketPos - openBracketPos - 1);
                    int idx;
                    if (!int.TryParse(format.Split(':')[0], out idx))
                    {
                        m_errorReport.ReportError(method, "Message format error: Unknown parameter: {{{0}}}", format);
                        return false;
                    }

                    if (idx < 0 || idx >= loggingSite.FlattenedPayload.Count)
                    {
                        m_errorReport.ReportError(method, "Message format error: Index out of range: {{{0}}}", format);
                        return false;
                    }

                    openBracketPos = -1;
                }

                curlyBracketPos = normalizedMessageFormat.IndexOfAny(new char[] { '{', '}' }, curlyBracketPos + 1);
            }

            if (openBracketPos >= 0)
            {
                m_errorReport.ReportError(method, "Message format error: Found '{{' without matching '}}'");
                return false;
            }

            // Only perform this check if telemetry is turned on. Otherwise events that only send to telemetry will
            // trigger the error
            if (loggingSite.EventGenerators == EventGenerators.None)
            {
                m_errorReport.ReportError(method, "EventGenerators not specified");
                return false;
            }

            if (attributeData.ConstructorArguments.Length > 0 && attributeData.ConstructorArguments[0].Value.GetType() == typeof(ushort))
            {
                loggingSite.Id = (ushort)attributeData.ConstructorArguments[0].Value;
            }
            else
            {
                m_errorReport.ReportError(method, "First constructor argument should be an ushort");
                return false;
            }

            if (method.Parameters.Length > 0 && method.Parameters[0].Type.Name == "LoggingContext")
            {
                loggingSite.LoggingContextParameterName = method.Parameters[0].Name;
            }
            else
            {
                m_errorReport.ReportError(method, "First method argument must be a LoggingContext");
                return false;
            }

            foreach (AttributeData attribute in method.ContainingType.GetAttributes())
            {
                switch (attribute.AttributeClass.Name)
                {
                    case "EventKeywordsTypeAttribute":
                        loggingSite.KeywordsType = (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
                        break;
                    case "EventTasksTypeAttribute":
                        loggingSite.TasksType = (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
                        break;
                }
            }

            return true;
        }

        private static string EscapeMessageString(string message)
        {
            string result = message;
            foreach (var item in s_escapeCharacters)
            {
                if (result.Contains(item.Key))
                {
                    result = result.Replace(item.Key, item.Value);
                }
            }

            return result;
        }

        private static readonly Dictionary<string, string> s_escapeCharacters = new Dictionary<string, string>()
        {
            { "\n", @"\n" },
            { "\r", @"\r" },
        };

        private static bool GetGeneratedEventAttribute(IMethodSymbol symbol, out AttributeData attributeData)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass.Name == "GeneratedEventAttribute")
                {
                    attributeData = attribute;
                    return true;
                }
            }

            attributeData = null;
            return false;
        }

        internal static void FindTypesInNamespace(INamespaceSymbol namespaceSymbol, Func<INamedTypeSymbol, bool> filterFunc, List<INamedTypeSymbol> result, bool nestedTypeRecursive)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                FindTypesInType(type, filterFunc, result, nestedTypeRecursive);
            }

            foreach (var childNs in namespaceSymbol.GetNamespaceMembers())
            {
                FindTypesInNamespace(childNs, filterFunc, result, nestedTypeRecursive);
            }
        }

        private static void FindTypesInType(INamedTypeSymbol type, Func<INamedTypeSymbol, bool> filterFunc, List<INamedTypeSymbol> result, bool nestedTypeRecursive)
        {
            if (filterFunc(type))
            {
                result.Add(type);
            }

            if (nestedTypeRecursive)
            {
                foreach (var nested in type.GetTypeMembers())
                {
                    FindTypesInType(nested, filterFunc, result, nestedTypeRecursive);
                }
            }
        }
    }
}
