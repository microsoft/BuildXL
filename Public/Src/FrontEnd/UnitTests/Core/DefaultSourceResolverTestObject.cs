// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using BuildXL.FrontEnd.Workspaces.Core;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Test object for default source resolver.
    /// </summary>
    public sealed class DefaultSourceResolverTestObject : ResolverTestObject
    {
        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.Append("{ ");
            builder.Append($"kind: \"{KnownResolverKind.DefaultSourceResolverKind}\"");
            builder.Append(" }");

            return builder.ToString();
        }
    }
}
