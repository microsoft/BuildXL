// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
