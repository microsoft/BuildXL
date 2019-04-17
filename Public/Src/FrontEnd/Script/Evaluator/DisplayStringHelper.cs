// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.FrontEnd.Script.Ambients.Map;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Helper class for creating string representation of the nodes.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "Struct is never compared, but passed to ETW to let it unwrap.")]
    public readonly struct DisplayStringHelper
    {
        private static readonly Dictionary<Type, string> s_typeToStringMap = CreateTypeToStringMap();
        private readonly ImmutableContextBase m_context;

        /// <nodoc />
        public DisplayStringHelper(ImmutableContextBase context) => m_context = context;

        /// <nodoc />
        public DisplayStackTraceEntry[] GetStackTrace(ModuleLiteral env, Node node)
        {
            return GetStackTrace(node.Location, env.GetPath(m_context));
        }

        /// <nodoc />
        public DisplayStackTraceEntry[] GetStackTrace(in UniversalLocation location)
        {
            return GetStackTrace(location.AsLineInfo(), location.File);
        }

        /// <nodoc />
        public DisplayStackTraceEntry[] GetStackTrace(LineInfo location, AbsolutePath path)
        {
            LineInfo lastCallSite = location;
            AbsolutePath lastPath = path;

            var result = new List<DisplayStackTraceEntry>(m_context.CallStackSize + 1);
            foreach (var entry in m_context.CallStack)
            {
                result.Add(entry.CreateDisplayStackTraceEntry(m_context, lastCallSite));

                lastCallSite = entry.InvocationLocation;
                lastPath = entry.Path;
            }

            if (lastPath.IsValid)
            {
                result.Add(CreateDisplayStackTraceEntry(GetLocation(lastPath, lastCallSite), entry: null, functionName: null));
            }

            return result.ToArray();
        }

        /// <summary>
        /// Get's string representation of the stack trace for current context.
        /// </summary>
        public string GetStackTraceAsString(in UniversalLocation location, QualifierTable qualifierTable)
        {
            return string.Join(Environment.NewLine, GetStackTrace(location).Select(entry => entry.ToDisplayString(qualifierTable)));
        }

        /// <nodoc />
        public string ToErrorString(in ErrorContext errorContext)
        {
            var receiverAsString = ErrorReceiverAsString(errorContext);
            var valueAsString = ErrorValueAsString(errorContext);

            if (string.IsNullOrWhiteSpace(valueAsString))
            {
                return receiverAsString;
            }

            return I($"{receiverAsString} with value '{valueAsString}'");
        }

        /// <nodoc />
        public string ErrorReceiverAsString(in ErrorContext errorContext)
        {
            switch (errorContext.ObjectCtx)
            {
                case ArrayLiteral arrayLiteral:
                    if (errorContext.Name.IsValid)
                    {
                        return string.Format(
                            CultureInfo.InvariantCulture,
                            "for element {0} of array declared at '{1}' for member '{2}'",
                            errorContext.Pos,
                            GetLocation(arrayLiteral.Path, arrayLiteral.Location).ToDisplayString(),
                            errorContext.Name.ToString(m_context.StringTable));
                    }

                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "for element {0} of array declared at '{1}'",
                        errorContext.Pos,
                        GetLocation(arrayLiteral.Path, arrayLiteral.Location).ToDisplayString());
                case ObjectLiteral objectLiteral:
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "for field '{0}' of object declared at '{1}'",
                        ToString(errorContext.Name),
                        GetLocation(objectLiteral.Path, objectLiteral.Location).ToDisplayString());
                case null:
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "for {0}",
                        errorContext.Pos == 0 ? "'receiver'" : "argument " + errorContext.Pos);
            }

            return string.Empty;
        }

        /// <nodoc />
        public static string ValueToString(EvaluationResult value, ImmutableContextBase context)
        {
            return new DisplayStringHelper(context).ToStringValue(value.Value);
        }

        /// <nodoc />
        public static string TypeToString(Type type, ImmutableContextBase context)
        {
            return new DisplayStringHelper(context).TypeToString(type);
        }

        /// <nodoc />
        public static string ValueTypeToString(EvaluationResult value, ImmutableContextBase context)
        {
            if (value.Value == null)
            {
                return "undefined";
            }

            return TypeToString(value.Value.GetType(), context);
        }

        /// <nodoc />
        public static string UnionTypeToString(ImmutableContextBase context, params Type[] types)
        {
            return new DisplayStringHelper(context).UnionTypeToString(types);
        }

        /// <nodoc />
        public string ErrorValueAsString(in ErrorContext errorContext)
        {
            switch (errorContext.ObjectCtx)
            {
                case ArrayLiteral arrayLiteral when errorContext.Pos >= 0 && errorContext.Pos < arrayLiteral.Length:
                    return ToStringValue(arrayLiteral[errorContext.Pos].Value);
                case ObjectLiteral objectLiteral when errorContext.Name.IsValid:
                    return ToStringValue(objectLiteral[errorContext.Name].Value);
            }

            return string.Empty;
        }

        /// <nodoc />
        public string TypeToString(Type type)
        {
            if (type == null)
            {
                return "undefined";
            }

            // For generic types like array or object literal need to get generic underlying type first
            if (type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            // Looking in the type map first
            if (s_typeToStringMap.TryGetValue(type, out string result))
            {
                return result;
            }

            // Rolling back to Type.ToString implementation
            try
            {
                // TODO: this is inherently unsafe and can throw for many reasons because it requires that type.Name is a valid identifier!
                return Converter.GetType(type, ((ModuleRegistry)m_context.FrontEndHost.ModuleRegistry).PrimitiveTypes).ToDisplayString(m_context);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return type.Name;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        internal static DisplayStackTraceEntry CreateDisplayStackTraceEntry(Location loc, StackEntry entry, string functionName)
        {
            return new DisplayStackTraceEntry(loc.File, loc.Line, loc.Position, functionName, entry);
        }

        /// <nodoc />
        public string ToString(FullSymbol symbol) => ToString(symbol, m_context.FrontEndContext.SymbolTable);
        
        /// <nodoc />
        public static string ToString(FullSymbol symbol, SymbolTable symbolTable)
        {
            if (!symbol.IsValid)
            {
                return "undefined";
            }

            return symbol.ToString(symbolTable);
        }

        /// <nodoc />
        public string ToString(SymbolAtom symbol)
        {
            if (!symbol.IsValid)
            {
                return "undefined";
            }

            return ToString(symbol.StringId);
        }

        /// <nodoc />
        public string ToString(StringId stringId)
        {
            if (!stringId.IsValid)
            {
                return "undefined";
            }

            return stringId.ToString(m_context.FrontEndContext.StringTable);
        }

        /// <nodoc />
        public string ToString(AbsolutePath path)
        {
            if (!path.IsValid)
            {
                return "undefined";
            }

            return path.ToString(m_context.FrontEndContext.PathTable);
        }

        /// <nodoc />
        public string ToString(ModuleLiteralId moduleLiteralId)
        {
            return ToString(moduleLiteralId.Path) + (moduleLiteralId.Name.IsValid ? ":" + ToString(moduleLiteralId.Name) : string.Empty);
        }

        private static Dictionary<Type, string> CreateTypeToStringMap()
        {
            return new Dictionary<Type, string>
                   {
                       [typeof(ObjectLiteral0)] = "object literal",
                       [typeof(ObjectLiteralSlim<>)] = "object literal",
                       [typeof(ObjectLiteralN)] = "object literal",
                       [typeof(ArrayLiteral)] = "Array",
                       [typeof(EnumValue)] = "enum",
                       [typeof(Closure)] = "function",
                       [typeof(OrderedMap)] = "Map",
                       [typeof(OrderedSet)] = "Set",
                       [typeof(int)] = "number",
                       [typeof(string)] = "string",
                       [typeof(bool)] = "boolean",
                       [typeof(UndefinedValue)] = "undefined",
                   };
        }

        private Location GetLocation(AbsolutePath path, LineInfo lineInfo)
        {
            return new Location { File = ToString(path), Line = lineInfo.Line, Position = lineInfo.Position };
        }

        private string ToStringValue(object value)
        {
            string valueStr = ToStringConverter.ObjectToString(m_context, value);

            // TODO:ST: remove hardcoded values! Move them to ObjectToString.
            // Cut if it is too long.
            if (valueStr.Length > 80)
            {
                valueStr = valueStr.Substring(0, 76) + " ...";
            }

            return valueStr;
        }

        private string UnionTypeToString(Type[] types)
        {
            Contract.Requires(types != null);

            if (types.Length == 0)
            {
                return string.Empty;
            }

            if (types.Length == 1)
            {
                return TypeToString(types[0]);
            }

            return string.Join(", ", types.Take(types.Length - 1).Select(TypeToString)) + " or " + TypeToString(types.Last());
        }
    }
}
