// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using TypeScript.Net.Types;
using static BuildXL.Utilities.Collections.HashSetExtensions;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <nodoc />
    public static class AutoCompleteHelpers
    {
        /// <summary>
        /// Given a type, creates an IEnumerable of all the base <see cref="IInterfaceType"/> and all <see cref="IUnionType"/> types.
        /// </summary>
        /// <remarks>
        /// If IType is not a union or interface type, the function returns an enumerable of just that type.
        /// </remarks>
        private static HashSet<IType> ExpandUnionAndBaseInterfaceTypes(IType type, HashSet<IType> expandedTypes)
        {
            if ((type.Flags & (TypeFlags.Interface | TypeFlags.Union)) == TypeFlags.None)
            {
                return new HashSet<IType> { type };
            }

            if ((type.Flags & TypeFlags.Interface) != TypeFlags.None)
            {
                // Track the interface type as well, as it can contain symbols and properties in
                // addition to the base types
                expandedTypes.Add(type);

                var interfaceTypeWithMembers = type.As<IInterfaceType>();
                if (interfaceTypeWithMembers.ResolvedBaseTypes?.Any() == true)
                {
                    foreach (var baseType in interfaceTypeWithMembers.ResolvedBaseTypes)
                    {
                        expandedTypes.AddRange(ExpandUnionAndBaseInterfaceTypes(baseType, expandedTypes));
                    }
                }
            }
            else
            {
                Contract.Assert((type.Flags & TypeFlags.Union) != TypeFlags.None);

                var unionType = type.As<IUnionType>();
                if (unionType.Types?.Any() == true)
                {
                    foreach (var unionTypesType in unionType.Types)
                    {
                        expandedTypes.AddRange(ExpandUnionAndBaseInterfaceTypes(unionTypesType, expandedTypes));
                    }
                }
            }

            return expandedTypes;
        }

        /// <summary>
        /// Given a type, creates an IEnumerable of all the base interface types and all union types.
        /// </summary>
        /// <remarks>
        /// If IType is not a union or interface type, the functin returns an enumerable of just that type.
        /// </remarks>
        public static IEnumerable<IType> ExpandUnionAndBaseInterfaceTypes(IType type)
        {
            var expandedTypes = new HashSet<IType>();

            // Since we are collecting types recursively, we could end up with duplicate types.
            // We should only return distinct types so we do not get duplicate symbols.
            return ExpandUnionAndBaseInterfaceTypes(type, expandedTypes);
        }

        /// <summary>
        /// Returns an enumeration of symbols from the specified types.
        /// </summary>
        public static IEnumerable<ISymbol> GetSymbolsFromTypes(ITypeChecker typeChecker, IEnumerable<IType> types)
        {
            HashSet<ISymbol> symbols = new HashSet<ISymbol>();
            foreach (var type in types)
            {
                symbols.AddRange(typeChecker.GetPropertiesOfType(type));
            }

            return symbols;
        }
    }
}
