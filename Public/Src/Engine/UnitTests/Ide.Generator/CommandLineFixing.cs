// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ide.Generator;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Ide.Generator
{
    public class CommandLineFixing
    {
        [Fact]
        public void Empty()
        {
            var result = IdeGenerator.FixCommandLine(string.Empty);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void NoArgumentsJustTool()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName}");
            Assert.Equal($@"x:\{Branding.ProductExecutableName}", result);
        }

        [Fact]
        public void NoArgumentsJustToolWithSpace()
        {
            var result = IdeGenerator.FixCommandLine($@"""x:\fo lder\{Branding.ProductExecutableName}""");
            Assert.Equal($@"""x:\fo lder\{Branding.ProductExecutableName}""", result);
        }

        [Fact]
        public void SingleOptionAndSomeOptions()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName} /opt1 /opt2:value");
            Assert.Equal($@"x:\{Branding.ProductExecutableName} /opt1 /opt2:value", result);
        }

        [Fact]
        public void SingleOptionWithSpace()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName} /opt:""val ue""");
            Assert.Equal($@"x:\{Branding.ProductExecutableName} /opt:""val ue""", result);
        }

        [Fact]
        public void RemoveFAtStartMiddleAndEnd()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName} /f:filter1 /opt1 /f:filter2 /opt2:value /f:filter3");
            Assert.Equal($@"x:\{Branding.ProductExecutableName} /opt1 /opt2:value", result);
        }

        [Fact]
        public void RemoveFilterAtStartMiddleAndEnd()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName} /filter:filter1 /opt1 /filter:filter2 /opt2:value /filter:filter3");
            Assert.Equal($@"x:\{Branding.ProductExecutableName} /opt1 /opt2:value", result);
        }

        [Fact]
        public void RemoveDefaultArgAtStartMiddleAndEnd()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName} filter1 /opt1 filter2 /opt2:value filter3");
            Assert.Equal($@"x:\{Branding.ProductExecutableName} /opt1 /opt2:value", result);
        }

        [Fact]
        public void RemoveVs()
        {
            var result = IdeGenerator.FixCommandLine($@"x:\{Branding.ProductExecutableName} /vs");
            Assert.Equal($@"x:\{Branding.ProductExecutableName}", result);
        }
    }
}
