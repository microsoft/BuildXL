// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.CodeAnalysis;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;

namespace BuildXL.LogGen
{
    /// <summary>
    /// Logging site that needs to have a corresponding partial method generated
    /// </summary>
    internal sealed class LoggingSite
    {
        /// <summary>
        /// The method for the logging site
        /// </summary>
        public IMethodSymbol Method { get; set; }

        /// <summary>
        /// The level
        /// </summary>
        public Level Level { get; set; }

        /// <summary>
        /// The EventGenerators configured for this site
        /// </summary>
        public EventGenerators EventGenerators = EventGenerators.None;

        /// <summary>
        /// Id of the event
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// EventSource Keywords
        /// </summary>
        public int EventKeywords { get; set; }

        /// <summary>
        /// EventSource Opcodes
        /// </summary>
        public int EventOpcode { get; set; }

        /// <summary>
        /// EventSource task
        /// </summary>
        public ushort EventTask { get; set; }

        /// <summary>
        /// The type that is the static keywords class for this logging site
        /// </summary>
        public INamedTypeSymbol KeywordsType { get; set; }

        /// <summary>
        /// The type that is the static tasks class for this logging site
        /// </summary>
        public INamedTypeSymbol TasksType { get; set; }

        /// <summary>
        /// Name of the LoggingContext parameter passed to the method defining this LoggingSite
        /// </summary>
        public string LoggingContextParameterName { get; set; }

        /// <summary>
        /// Payload for the log event. These are parameters after the required ones. Different generators may choose to support
        /// different types here. A generator should produce an error if it encounters an unsupported type in the payload
        /// </summary>
        public IReadOnlyList<IParameterSymbol> Payload { get; private set; }

        /// <summary>
        /// The predefined aliases to substitute.
        /// </summary>
        public IReadOnlyDictionary<string, string > Aliases { get; set; }

        /// <summary>
        /// Sets the payload of the LogginSite
        /// </summary>
        public bool SetPayload(ErrorReport errorReport, IEnumerable<IParameterSymbol> payload)
        {
            Payload = new List<IParameterSymbol>(payload);

            List<AddressedType> flattenedPayload = new List<AddressedType>();
            foreach (var item in Payload)
            {
                bool flattenSuccess = true;
                flattenedPayload.AddRange(FlattenSymbol(errorReport, ref flattenSuccess, item, item.Type, 0, item.Name));
                if (!flattenSuccess)
                {
                    return false;
                }
            }

            FlattenedPayload = flattenedPayload;
            return true;
        }

        /// <summary>
        /// Flattened payload
        /// </summary>
        public IReadOnlyList<AddressedType> FlattenedPayload { get; private set; }

        /// <summary>
        /// The format string for the message as specified in the logging site's declaration
        /// </summary>
        public string SpecifiedMessageFormat { get; set; }

        /// <summary>
        /// Format string for the message with references normalized to expanded parameters
        /// </summary>
        public string GetNormalizedMessageFormat()
        {
            string result = SpecifiedMessageFormat;

            int counter = 0;
            foreach (var payloadItem in FlattenedPayload)
            {
                result = result.Replace("{" + payloadItem.Address + "}", "{" + counter.ToString(CultureInfo.InvariantCulture) + "}");
                result = result.Replace("{" + payloadItem.Address + ":", "{" + counter.ToString(CultureInfo.InvariantCulture) + ":");
                counter++;
            }

            foreach (var payloadItem in Payload)
            {
                result = result.Replace("{" + payloadItem.Name + "}", "{" + counter.ToString(CultureInfo.InvariantCulture) + "}");
                result = result.Replace("{" + payloadItem.Name + ":", "{" + counter.ToString(CultureInfo.InvariantCulture) + ":");
                counter++;
            }

            // Process predefined aliases:
            foreach (var alias in Aliases)
            {
                result = result.Replace("{" + alias.Key + "}", alias.Value);
            }

            return result;
        }

        /// <summary>
        /// The parameter names for the normalized message format string
        /// </summary>
        public IEnumerable<string> GetMessageFormatParameters()
        {
            return FlattenedPayload.Select(p => p.Address).Concat(Payload.Select(p => p.Name));
        }

        /// <summary>
        /// Gets a list of flattened payload arguments
        /// </summary>
        public string GetFlattenedPayloadArgs()
        {
            return string.Join(",", FlattenedPayload.Select(i => i.Address));
        }

        /// <summary>
        /// A type and its address
        /// </summary>
        internal sealed class AddressedType
        {
            /// <summary>
            /// Address to an item possibly deeply nested within a LoggingSite's payload
            /// </summary>
            public string Address;

            /// <summary>
            /// The type
            /// </summary>
            public ITypeSymbol Type;

            /// <summary>
            /// Gets an address that is suitable for a name of a method parameter
            /// </summary>
            public string AddressForMethodParameter => Address.Replace(".", "_");

            /// <summary>
            /// Address for telemetry string
            /// </summary>
            public string AddressForTelemetryString => char.ToUpperInvariant(AddressForMethodParameter[0]) + AddressForMethodParameter.Substring(1, AddressForMethodParameter.Length - 1);
        }

        private static List<AddressedType> FlattenSymbol(ErrorReport errorReport, ref bool success, IParameterSymbol root, ITypeSymbol item, int depth, string prefix = "")
        {
            List<AddressedType> result = new List<AddressedType>();

            if (depth > 4)
            {
                errorReport.ReportError(root, "Log method parameter type contained too much nesting and exceeded maximum recursion when flattening. Flattened chain was: {0}", prefix);
                success = false;
                return result;
            }

            if (item.SpecialType == SpecialType.System_Enum)
            {
                result.Add(new AddressedType() { Type = item, Address = prefix });
            }

            if (item.ToDisplayString() == "System.Guid")
            {
                result.Add(new AddressedType() { Type = item, Address = prefix });
            }
            else if (IsNonSpecialClassLike(item))
            {
                if (item.BaseType != null && IsNonSpecialClassLike(item.BaseType))
                {
                    result.AddRange(FlattenSymbol(errorReport, ref success, root, item.BaseType, depth + 1, prefix));
                }

                foreach (var member in item.GetMembers().OfType<IPropertySymbol>().Where(FilterMembersToFlatten))
                {
                    result.AddRange(FlattenSymbol(errorReport, ref success, root, member.Type, depth + 1, prefix + '.' + member.Name));
                }

                foreach (var member in item.GetMembers().OfType<IFieldSymbol>().Where(FilterMembersToFlatten))
                {
                    result.AddRange(FlattenSymbol(errorReport, ref success, root, member.Type, depth + 1, prefix + '.' + member.Name));
                }
            }
            else
            {
                result.Add(new AddressedType() { Type = item, Address = prefix });
            }

            Contract.Assume(depth > 0 || result.Count > 0, "FlattenSymbol skipped or didn't know how to handle a symbol of type " + item.TypeKind.ToString());

            return result;
        }

        private static bool FilterMembersToFlatten(ISymbol m) =>
            m.OriginalDefinition.DeclaredAccessibility == Accessibility.Public &&
            !m.OriginalDefinition.IsStatic &&
            !m.IsAbstract;

        private static bool IsNonSpecialClassLike(ITypeSymbol item) =>
            item.SpecialType == SpecialType.None &&
            IsClassLike(item);

        private static bool IsClassLike(ITypeSymbol item) =>
            item.TypeKind == TypeKind.Class ||
            item.TypeKind == TypeKind.Struct ||
            item.TypeKind == TypeKind.Structure;
    }
}
