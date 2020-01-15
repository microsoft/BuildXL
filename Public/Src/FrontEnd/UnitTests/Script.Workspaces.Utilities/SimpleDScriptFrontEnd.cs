// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
