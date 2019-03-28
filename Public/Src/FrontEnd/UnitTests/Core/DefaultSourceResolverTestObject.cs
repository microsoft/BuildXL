// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
