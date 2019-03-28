// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Special syntax factory that can actually use tuples.
    /// </summary>
    /// <remarks>
    /// TODO: the type should be merged with SyntaxFactory in the next iteration.
    /// </remarks>
    public static class SyntaxFactoryEx
    {
        /// <summary>
        /// Creates an object literal with a given set of name-expression pairs.
        /// </summary>
        public static IObjectLiteralExpression ObjectLiteral(params (string name, IExpression expression)[] elements)
        {
            var objectLiteralElements = elements
                .Where(tpl => tpl.expression != null)
                .Select(tpl => new PropertyAssignment(tpl.name, tpl.expression))
                .ToArray();

            return new ObjectLiteralExpression(objectLiteralElements);
        }
    }
}
