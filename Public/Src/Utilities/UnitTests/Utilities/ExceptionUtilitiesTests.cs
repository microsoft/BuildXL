// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        [Fact]
        public void ClassifyOutOfDiskSpace()
        {
            Assert.Equal(ExceptionRootCause.OutOfDiskSpace, ExceptionUtilities.AnalyzeExceptionRootCause(new IOException("No space left on device")));
        }
    }
}
