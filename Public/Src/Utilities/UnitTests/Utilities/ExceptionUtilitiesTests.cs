// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class ExceptionUtilitiesTests
    {
        [Fact]
        public void ClassifyMissingRuntimeDependencyTest()
        {
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new FileLoadException()));
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new FileNotFoundException("Could not load file or assembly")));
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new DllNotFoundException()));
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new TypeLoadException()));
        }
    }
}
