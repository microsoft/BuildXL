// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.CodeAnalysis;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;

namespace BuildXL.LogGen.Core
{
    /// <summary>
    /// Set of helper functions for Parsing Logging classes/functions.
    /// </summary>
    public static class ParserHelpers
    {
        /// <summary>
        /// Try generating logging classes for a given set of candidates.
        /// </summary>
        public static bool TryGenerateLoggingClasses(
            List<INamedTypeSymbol> candidates,
            IReadOnlyDictionary<string, IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>> errorsPerFile,
            ErrorReport errorReport,
            IReadOnlyDictionary<string, string> aliases,
            out List<LoggingClass> loggingClasses)
        {
            loggingClasses = new List<LoggingClass>();
            foreach (var classSymbol in candidates)
            {
                if (GetAttribute(classSymbol, errorsPerFile, nameof(LoggingDetailsAttribute), errorReport, out var loggingDetailsAttributeData))
                {
                    // Validate Attribute
                    if (!TryParseLoggingDetailsAttribute(classSymbol, loggingDetailsAttributeData, errorReport, out var loggingDetails))
                    {
                        return false;
                    }

                    // Validate Class
                    var loggingClass = new LoggingClass(loggingDetails, classSymbol);

                    foreach (var member in classSymbol.GetMembers())
                    {
                        if (member is IMethodSymbol method)
                        {
                            if (!method.IsAbstract &&
                                method.MethodKind != MethodKind.Constructor && method.MethodKind != MethodKind.StaticConstructor &&  // okay to have constructors
                                method.MethodKind != MethodKind.PropertyGet && method.MethodKind != MethodKind.PropertySet)  // okay to have properties (for now).
                            {
                                errorReport.ReportError(member, $"All methods must be abstract. Invalid method: {method.Name}");
                            }

                            if (GetAttribute(method, errorsPerFile, nameof(GeneratedEventAttribute), errorReport, out var generatedEventData))
                            {
                                if (!ParseAndValidateLogSite(method, generatedEventData, aliases, errorReport, out var site))
                                {
                                    // The error should be logged at this point.
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
        /// <summary>
        /// Parses an validates the logging attributes and message in a logging method.
        /// </summary>
        public static bool ParseAndValidateLogSite(IMethodSymbol method, AttributeData attributeData, IReadOnlyDictionary<string, string> aliases, ErrorReport errorReport, out LoggingSite loggingSite)
        {
            loggingSite = new LoggingSite();
            loggingSite.Aliases = aliases;
            loggingSite.Method = method;

            if (!loggingSite.SetPayload(errorReport, method.Parameters.Skip(1)))
            {
                return false;
            }

            // Pull the data out of the GeneratedEventAttribute
            foreach (var argument in attributeData.NamedArguments)
            {
                if (argument.Value.IsNull)
                {
                    System.Diagnostics.Debugger.Launch();
                    errorReport.ReportError(method, $"Argument.Value is null. Key={argument.Key}");
                    continue;
                }

                Contract.Assert(argument.Value.Value is not null);
                
                switch (argument.Key)
                {
                    case nameof(GeneratedEventAttribute.EventLevel):
                        if (argument.Value.Value.GetType() != typeof(int))
                        {
                            errorReport.ReportError(method, $"Unsupported {nameof(GeneratedEventAttribute.EventLevel)} value '{argument.Value.Value.ToString()}'");
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
                                errorReport.ReportError(method, $"Unsupported {nameof(GeneratedEventAttribute.EventLevel)} value '{value}'");
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
                errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} is required");
                return false;
            }

            if (loggingSite.SpecifiedMessageFormat.StartsWith(EventConstants.LabeledProvenancePrefix, StringComparison.Ordinal) && method.Parameters.Length >= 2 && method.Parameters[1].Name != "location")
            {
                errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} is using provenance prefix information to indicate line information. Therefore the location must be the first parameter after the {nameof(LoggingContext)}. This method declares '{method.Parameters[1].Name}' as that parameter");
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
                        errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Found '}}' without matching '{{'");
                        return false;
                    }

                    openBracketPos = curlyBracketPos;
                }
                else
                {
                    if (normalizedMessageFormat[curlyBracketPos] == '{')
                    {
                        errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Found too many nested '{{'");
                        return false;
                    }

                    string format = normalizedMessageFormat.Substring(openBracketPos + 1, curlyBracketPos - openBracketPos - 1);
                    int idx;
                    if (!int.TryParse(format.Split(':')[0], out idx))
                    {
                        errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Unknown parameter: {{{format}}}");
                        return false;
                    }

                    if (idx < 0 || idx >= loggingSite.FlattenedPayload.Count)
                    {
                        errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Index out of range: {{{format}}}");
                        return false;
                    }

                    openBracketPos = -1;
                }

                curlyBracketPos = normalizedMessageFormat.IndexOfAny(new char[] { '{', '}' }, curlyBracketPos + 1);
            }

            if (openBracketPos >= 0)
            {
                errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.Message)} format error: Found '{{' without matching '}}'");
                return false;
            }

            // Only perform this check if telemetry is turned on. Otherwise events that only send to telemetry will
            // trigger the error
            if (loggingSite.EventGenerators == EventGenerators.None)
            {
                errorReport.ReportError(method, $"{nameof(GeneratedEventAttribute.EventGenerators)}  not specified");
                return false;
            }

            if (attributeData.ConstructorArguments.Length > 0 && attributeData.ConstructorArguments[0].Value.GetType() == typeof(ushort))
            {
                loggingSite.Id = (ushort)attributeData.ConstructorArguments[0].Value;
            }
            else
            {
                errorReport.ReportError(method, "First constructor argument should be an ushort");
                return false;
            }

            if (method.Parameters.Length > 0 && method.Parameters[0].Type.Name == "LoggingContext")
            {
                loggingSite.LoggingContextParameterName = method.Parameters[0].Name;
            }
            else
            {
                errorReport.ReportError(method, $"First method argument must be a {nameof(LoggingContext)}");
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

        /// <summary>
        /// Tries to parse <see cref="LoggingDetailsAttribute"/> from a symbol.
        /// </summary>
        public static bool TryParseLoggingDetailsAttribute(ISymbol symbolForError, AttributeData attributeData, ErrorReport errorReport, out LoggingDetailsAttribute result)
        {
            if (attributeData.ConstructorArguments.Length > 0 && attributeData.ConstructorArguments[0].Value is string)
            {
                var name = (string)attributeData.ConstructorArguments[0].Value;
                if (string.IsNullOrEmpty(name))
                {
                    errorReport.ReportError(symbolForError, $"Unsupported constructor argument value {nameof(LoggingDetailsAttribute.Name)}: Cannot be null or empty.");
                    result = null;
                    return false;
                }
                result = new LoggingDetailsAttribute(name);
            }
            else
            {
                errorReport.ReportError(symbolForError, "First constructor argument should be a string");
                result = null;
                return false;
            }

            // Pull the data out of the GeneratedEventAttribute
            foreach (var argument in attributeData.NamedArguments)
            {
                switch (argument.Key)
                {
                    case nameof(LoggingDetailsAttribute.Name):
                        if (!ParseValue<string>(
                            argument.Value,
                            symbolForError,
                            value => string.IsNullOrEmpty(value) ? $"'{nameof(LoggingDetailsAttribute.Name)}' cannot be null or empty." : null,
                            errorReport,
                            out var name))
                        {
                            result = null;
                            return false;
                        }

                        result.Name = name;

                        break;
                    case nameof(LoggingDetailsAttribute.InstanceBasedLogging):
                        if (!ParseValue<bool>(
                            argument.Value,
                            symbolForError,
                            _ => null,
                            errorReport,
                            out var instanceBasedLogging))
                        {
                            result = null;
                            return false;
                        }

                        result.InstanceBasedLogging = instanceBasedLogging;
                        break;

                    case nameof(LoggingDetailsAttribute.EmitDebuggingInfo):
                        if (!ParseValue<bool>(
                            argument.Value,
                            symbolForError,
                            _ => null,
                            errorReport,
                            out var emitDebuggingInfo))
                        {
                            result = null;
                            return false;
                        }

                        result.EmitDebuggingInfo = emitDebuggingInfo;
                        break;

                    default:
                        errorReport.ReportError(symbolForError, $"Unsupported attribute property '{argument.Key}' with value '{argument.Value.Value.ToString()}'");
                        result = null;
                        return false;
                }
            }

            return true;
        }

        private static bool ParseValue<TResult>(TypedConstant argument, ISymbol symbolForError, Func<TResult, string> validate, ErrorReport errorReport, out TResult result)
        {
            var value = argument.Value;
            if (value.GetType() != typeof(TResult))
            {
                errorReport.ReportError(symbolForError, $"Unsupported argument '{value.ToString()}'. Argument is of  type: '{value.GetType().FullName}', expected: '{typeof(TResult).FullName}'.");
                result = default(TResult);
                return false;
            }

            result = (TResult)value;

            if (validate != null)
            {
                var errorMessage = validate(result);
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    errorReport.ReportError(symbolForError, $"Unsupported argument. Invalid value: '{errorMessage}'.");
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

        /// <nodoc />
        public static bool GetAttribute(ISymbol symbol, IReadOnlyDictionary<string, IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic>> errorsByFile, string attributeClassName, ErrorReport errorReport, out AttributeData attributeData)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass.Name == attributeClassName)
                {
                    attributeData = attribute;

                    // Check the errors we stashed off to see if any pertain to this event. If so, display them
                    // as they may cause us to generate the event incorrectly
                    bool eventHasErrors = false;
                    IReadOnlyList< Microsoft.CodeAnalysis.Diagnostic > diagnostics;
                    if (errorsByFile.TryGetValue(symbol.Locations[0].SourceTree.FilePath, out diagnostics))
                    {
                        foreach (var d in diagnostics)
                        {
                            if (d.Location.SourceSpan.OverlapsWith(symbol.Locations[0].SourceSpan) ||
                                d.Location.SourceSpan.OverlapsWith(attributeData.ApplicationSyntaxReference.Span))
                            {
                                errorReport.ReportError(d.ToString());
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

        /// <nodoc />
        public static void FindTypesInNamespace(INamespaceSymbol namespaceSymbol, Func<INamedTypeSymbol, bool> filterFunc, List<INamedTypeSymbol> result, bool nestedTypeRecursive)
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
