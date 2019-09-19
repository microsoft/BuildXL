// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Incrementality
{
    internal static class Extensions
    {
        public static List<string> GetAtomsFromQualifiedName([JetBrains.Annotations.NotNull]this EntityName entityName)
        {
            if (entityName.Kind == TypeScript.Net.Types.SyntaxKind.Identifier)
            {
                return new List<string>() { entityName.Text };
            }

            var qualifiedName = entityName.AsQualifiedName();
            var names = GetAtomsFromQualifiedName(qualifiedName.Left);
            names.Add(qualifiedName.Right.Text);

            return names;
        }
    }
}
