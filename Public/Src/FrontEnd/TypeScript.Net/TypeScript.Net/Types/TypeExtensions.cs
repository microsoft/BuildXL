// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of extension methods for <see cref="IType"/> interface.
    /// </summary>
    public static class TypeExtensions
    {
        /// <summary>
        /// Generic safe-cast method that tries to convert from <paramref name="type"/> to <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// TODO:SQ: Fix for possible union type (IUnionType)
        /// This method is important because <paramref name="type"/> could be of union type that will prevent
        /// regular conversion from it to target type.
        /// Keep in mind that this method is separate from <see cref="NodeExtensions.As{T}"/> method that takes care union types (but this one does not).
        /// </remarks>
        [DebuggerStepThrough]
        public static T As<T>(this IType type) where T : class, IType
        {
            return type as T;
        }

        /// <summary>
        /// Generic unsafe-cast method that tries to convert from <paramref name="type"/> to <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// TODO:SQ: Fix for possible union type (IUnionType)
        /// This method is important because <paramref name="type"/> could be of union type that will prevent
        /// regular conversion from it to target type.
        /// </remarks>
        [DebuggerStepThrough]
        public static T Cast<T>(this IType type) where T : class, IType
        {
            Contract.Requires(type != null);
            Contract.Ensures(Contract.Result<T>() != null);

            // TODO: switch checks. Use direct cast first and then check union case. But measure first.
            // var union = node.As<IUnionNode>();
            // if (union != null)
            // {
            //    return union.Node.Cast<T>();
            // }
            var directCastResult = type as T;
            if (directCastResult == null)
            {
                throw CreateInvalidCastException(type, typeof(T).Name);
            }

            return directCastResult;
        }

        private static InvalidCastException CreateInvalidCastException(IType sourceTypeNode, string targetType, IType actualType = null)
        {
            string actualTypeString = actualType != null ? I($" Actual type is '{actualType.GetType()}'.") : null;
            return new InvalidCastException(I($"Specified cast from node '{sourceTypeNode.GetType()}' with '{sourceTypeNode.Flags}' flags to '{targetType}' is not valid.{actualTypeString}"));
        }
    }
}
