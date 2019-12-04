// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;
using JetBrains.Annotations;
using Test.DScript.Workspaces.Utilities;

namespace Test.BuildXL.FrontEnd.Workspaces.Utilities
{
    /// <nodoc />
    public class SimpleDScriptFrontEnd : FrontEnd<SimpleWorkspaceSourceModuleResolver>
    {
        /// <inheritdoc />
        public override IReadOnlyCollection<string> SupportedResolvers => new [] { "DScript" };

        /// <inheritdoc />
        public override IResolver CreateResolver([NotNull] string kind)
        {
            throw new NotImplementedException();
        }
    }
}
