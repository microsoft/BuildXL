// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable 1591

namespace Test.DScript.Ast.DScriptV2
{
    public abstract class SemanticBasedTests : DScriptV2Test
    {
        protected SemanticBasedTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            // We turn on all BuildXL Script V2 related options
            return new FrontEndConfiguration
            {
                MaxFrontEndConcurrency = 1,
                DebugScript = isDebugged,
                PreserveFullNames = true,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                UseSpecPublicFacadeAndAstWhenAvailable = false,
                EnableIncrementalFrontEnd = true,
            };
        }

        protected static class QConstants
        {
            public const string Platform = "platform";
            public const string Configuration = "configuration";
            public const string Os = "os";
            public const string Net = "net";
            public const string X86 = "x86";
            public const string X64 = "x64";
            public const string Debug = "debug";
            public const string Release = "release";
            public const string Win = "win";
            public const string Unix = "unix";
            public const string Net45 = "net45";
        }
    }
}
