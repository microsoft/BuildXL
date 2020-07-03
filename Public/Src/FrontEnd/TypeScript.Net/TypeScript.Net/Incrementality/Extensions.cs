// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Incrementality
{
    internal static class Extensions
    {
        public static List<string> GetAtomsFromQualifiedName([NotNull]this EntityName entityName)
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
