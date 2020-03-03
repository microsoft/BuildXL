// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.LogGen.Generators;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
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

        public bool DiscoverLoggingSites(out List<LoggingClass> loggingClasses)
        {
            loggingClasses = new List<LoggingClass>();

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
                if (GetAttribute(symbol, errorsByFile, nameof(LoggingDetailsAttribute), out var loggingDetailsData))
                {
                    if (!TryParseLoggingDetailsAttribute(symbol, loggingDetailsData, out var loggingDetails))
                    {
                        return false;
                    }

                    var loggingClass = new LoggingClass(loggingDetails.Name, symbol);

                    foreach (var member in symbol.GetMembers())
                    {
                        if (member is IMethodSymbol method)
                        {
                            if (GetAttribute(method, errorsByFile, nameof(GeneratedEventAttribute), out var generatedEventData))
                            {
                                if (!ParseAndValidateLogSite(method, generatedEventData, out var site))
                                {
                                    return false;
                                }

                                loggingClass.Sites.Add(site);
                            }
                        }
                    }

                    loggingClasses.Add(loggingClass);
                }
            }

            return true;
        }

        private bool ParseAndValidateLogSite(IMethodSymbol method, AttributeData attributeData, out LoggingSite loggingSite)
        {
            loggingSite = new LoggingSite();
            loggingSite.Aliases = m_configuration.Aliases;
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
                    case nameof(GeneratedEventAttribute.EventLevel):
                        if (argument.Value.Value.GetType() != typeof(int))
                        {
                            m_errorReport.ReportError(method, $"Unsupported {nameof(GeneratedEventAttribute.EventLevel)} value '{argument.Value.Value.ToString()}'");
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
                                m_errorReport.ReportError(method, $"Unsupported {nameof(GeneratedEventAttribute.EventLevel)} value '{value}'");
                                break;
                        }

                        break;
                    case nameof(GeneratedEventAttribute.EventGenerators):
                        loggingSite.EventGenerators = (EventGenerators)argument.Value.Value;
                        break;
                    case nameof(GeneratedEventAttribute.Message):
                        loggingSite.SpecifiedMessageFormat = EscapeMessageString((string)argument.Value.Value);
                        break;
                    case nameof(GeneratedEventAttribute.EventOpcode):
                        loggingSite.EventOpcode = (byte)argument.Value.Value;
                        break;
                    case nameof(GeneratedEventAttribute.Keywords):
                        loggingSite.EventKeywords = (int)argument.Value.Value;
                        break;
                    case nameof(GeneratedEventAttribute.EventTask):
                        loggingSite.EventTask = (ushort)argument.Value.Value;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(loggingSite.SpecifiedMessageFormat))
            {
                m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} is required");
                return false;
            }

            if (loggingSite.SpecifiedMessageFormat.StartsWith(EventConstants.LabeledProvenancePrefix, StringComparison.Ordinal) && method.Parameters.Length >= 2 && method.Parameters[1].Name != "location")
            {
                m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} is using provenance prefix information to indicate line information. Therefore the location must be the first parameter after the {nameof(LoggingContext)}. This method declares '{method.Parameters[1].Name}' as that parameter");
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
                        m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Found '}}' without matching '{{'");
                        return false;
                    }

                    openBracketPos = curlyBracketPos;
                }
                else
                {
                    if (normalizedMessageFormat[curlyBracketPos] == '{')
                    {
                        m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Found too many nested '{{'");
                        return false;
                    }

                    string format = normalizedMessageFormat.Substring(openBracketPos + 1, curlyBracketPos - openBracketPos - 1);
                    int idx;
                    if (!int.TryParse(format.Split(':')[0], out idx))
                    {
                        m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Unknown parameter: {{{format}}}");
                        return false;
                    }

                    if (idx < 0 || idx >= loggingSite.FlattenedPayload.Count)
                    {
                        m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Index out of range: {{{format}}}");
                        return false;
                    }

                    openBracketPos = -1;
                }

                curlyBracketPos = normalizedMessageFormat.IndexOfAny(new char[] { '{', '}' }, curlyBracketPos + 1);
            }

            if (openBracketPos >= 0)
            {
                m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Found '{{' without matching '}}'");
                return false;
            }

            // Only perform this check if telemetry is turned on. Otherwise events that only send to telemetry will
            // trigger the error
            if (loggingSite.EventGenerators == EventGenerators.None)
            {
                m_errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.EventGenerators)}  not specified");
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
                m_errorReport.ReportError(method, $"First method argument must be a {nameof(LoggingContext)}");
                return false;
            }

            foreach (AttributeData attribute in method.ContainingType.GetAttributes())
            {
                switch (attribute.AttributeClass.Name)
                {
                    case nameof(EventKeywordsTypeAttribute):
                        loggingSite.KeywordsType = (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
                        break;
                    case nameof(EventTasksTypeAttribute):
                        loggingSite.TasksType = (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
                        break;
                }
            }

            return true;
        }


        public bool TryParseLoggingDetailsAttribute(ISymbol symbolForError, AttributeData attributeData, out LoggingDetailsAttribute result)
        {
            if (attributeData.ConstructorArguments.Length > 0 && attributeData.ConstructorArguments[0].Value is string)
            {
                var name = (string)attributeData.ConstructorArguments[0].Value;
                if (string.IsNullOrEmpty(name))
                {
                    m_errorReport.ReportError(symbolForError, $"Unsupported constructor argument value {nameof(LoggingDetailsAttribute.Name)}: Cannot be null or empty.");
                    result = null;
                    return false;
                }
                result = new LoggingDetailsAttribute(name);
            }
            else
            {
                m_errorReport.ReportError(symbolForError, "First constructor argument should be a string");
                result = null;
                return false;
            }

            // Pull the data out of the GeneratedEventAttribute
            foreach (var argument in attributeData.NamedArguments)
            {
                switch (argument.Key)
                {
                    case nameof(LoggingDetailsAttribute.Name):
                        var value = argument.Value.Value;
                        if (value.GetType() != typeof(string))
                        {
                            m_errorReport.ReportError(symbolForError, "Unsupported constructor argument type: '{0}'", argument.Value.Value.ToString());
                            return false;
                        }

                        result.Name = (string)value;
                        if (string.IsNullOrEmpty(result.Name))
                        {
                            m_errorReport.ReportError(symbolForError, $"Unsupported property '{nameof(LoggingDetailsAttribute.Name)}': Cannot be null or empty.");
                            result = null;
                            return false;
                        }

                        break;
                    default:
                        m_errorReport.ReportError(symbolForError, $"Unsupported attribute property '{argument.Key}' with value '{argument.Value.Value.ToString()}'");
                        result = null;
                        return false;
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

        private bool GetAttribute(ISymbol symbol, MultiValueDictionary<string, Diagnostic> errorsByFile, string attributeClassName, out AttributeData attributeData)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass.Name == attributeClassName)
                {
                    attributeData = attribute;

                    // Check the errors we stashed off to see if any pertain to this event. If so, display them
                    // as they may cause us to generate the event incorrectly
                    bool eventHasErrors = false;
                    IReadOnlyList<Diagnostic> diagnostics;
                    if (errorsByFile.TryGetValue(symbol.Locations[0].SourceTree.FilePath, out diagnostics))
                    {
                        foreach (var d in diagnostics)
                        {
                            if (d.Location.SourceSpan.OverlapsWith(symbol.Locations[0].SourceSpan) ||
                                d.Location.SourceSpan.OverlapsWith(attributeData.ApplicationSyntaxReference.Span))
                            {
                                m_errorReport.ReportError(d.ToString());
                                eventHasErrors = true;
                            }
                        }
                    }

                    return !eventHasErrors;
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
