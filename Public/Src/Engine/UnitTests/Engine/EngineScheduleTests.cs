// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Pips.Filter;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.EngineTests
{
    public class EngineScheduleTests
    {
        #region PipFilter Prescedence Tests
        private const string TagFilter = "tag='foo'";
        private const string NegatedTagFilter = "~(tag='foo')";

        [Fact]
        public void CommandLineFilterIsHighest()
        {
            RootFilter filter = ParseFilter(values: null, commandLineFilter: TagFilter, defaultFilter: NegatedTagFilter, out _, out _);
            XAssert.IsFalse(filter.IsEmpty);
            XAssert.IsFalse(filter.PipFilter is NegatingFilter);
        }

        [Fact]
        public void ImplicitPathTrumpsDefault()
        {
            RootFilter filter = ParseFilter(values: new string[] { "myPath" }, commandLineFilter: null, defaultFilter: NegatedTagFilter, out _, out _);
            XAssert.IsTrue(filter.PipFilter is BinaryFilter, "Main is binary filter");
 
            var binaryFilter1 = (BinaryFilter)filter.PipFilter;
            XAssert.IsTrue(binaryFilter1.Left is OutputFileFilter, "Left is OutputFile");
            XAssert.IsTrue(binaryFilter1.Right is BinaryFilter, "Right is again binary filter");
 
            var binaryFilter2 = (BinaryFilter)binaryFilter1.Right;
            XAssert.IsTrue(binaryFilter2.Left is SpecFileFilter, "Left of right binary filter is SpecFile");
            XAssert.IsTrue(binaryFilter2.Right is TagFilter, "Right  of right binary filter is Tag");
        }


        [Theory]
        [InlineData("Pip3F8F204D436AE98A")]
        [InlineData("3F8F204D436AE98A")]
        public void ImplicitWithPipIdAddsIdFilter(string implicitFilter)
        {
            RootFilter filter = ParseFilter(values: new string[] { implicitFilter }, commandLineFilter: null, defaultFilter: NegatedTagFilter, out _, out _);
            XAssert.IsTrue(filter.PipFilter is BinaryFilter, "Main is binary filter");
 
            var binaryFilter1 = (BinaryFilter)filter.PipFilter;
            XAssert.IsTrue(binaryFilter1.Left is OutputFileFilter, "Left is OutputFile");
            XAssert.IsTrue(binaryFilter1.Right is BinaryFilter, "Right is again binary filter");
 
            var binaryFilter2 = (BinaryFilter)binaryFilter1.Right;
            XAssert.IsTrue(binaryFilter2.Left is SpecFileFilter, "Left of right binary filter is SpecFile");
            XAssert.IsTrue(binaryFilter2.Right is BinaryFilter, "Right of right binary filter is Again Binary Filter");

            var binaryFilter3 = (BinaryFilter)binaryFilter2.Right;
            XAssert.IsTrue(binaryFilter3.Left is TagFilter, "Left of right,right binary filter is Tag");
            XAssert.IsTrue(binaryFilter3.Right is PipIdFilter, "Right  of right,right binary filter is Id");
        }

        [Fact]
        public void CommandLineEmptyFilter()
        {
            RootFilter filter = ParseFilter(values: null, commandLineFilter: string.Empty, defaultFilter: NegatedTagFilter, out _, out _);
            XAssert.IsTrue(filter.IsEmpty);
        }

        [Fact]
        public void DefaultFallback()
        {
            RootFilter filter = ParseFilter(values: null, commandLineFilter: null, defaultFilter: NegatedTagFilter, out _, out _);
            XAssert.IsFalse(filter.IsEmpty);
            XAssert.IsTrue(filter.PipFilter is NegatingFilter);
        }

        [Fact]
        public void DefaultFilterCannotShortCircuitValues()
        {
            string filterWithValue = "value='myValue'";
            RootFilter filterFromCommandLine = ParseFilter(values: null, commandLineFilter: filterWithValue, defaultFilter: null, out var symbolTable, out var pathTable);
            XAssert.AreEqual(1, filterFromCommandLine.GetEvaluationFilter(symbolTable, pathTable).ValueNamesToResolve.Count);

            RootFilter filterFromDefault = ParseFilter(values: null, commandLineFilter: null, defaultFilter: filterWithValue, out symbolTable, out pathTable);
            XAssert.AreEqual(0, filterFromDefault.GetEvaluationFilter(symbolTable, pathTable).ValueNamesToResolve.Count);
            XAssert.AreEqual(0, filterFromDefault.GetEvaluationFilter(symbolTable, pathTable).ValueDefinitionRootsToResolve.Count);
        }

        private RootFilter ParseFilter(IEnumerable<string> values, string commandLineFilter, string defaultFilter, out SymbolTable symbolTable, out PathTable pathTable)
        {
            var loggingContext = new LoggingContext("EngineScheduleTests.ParseFilter");
            pathTable = new PathTable();
            // EngineSchedulTest operate on the real filesystem for now
            var fileSystem = new PassThroughFileSystem(pathTable);
            BuildXLContext context = EngineContext.CreateNew(CancellationToken.None, pathTable, fileSystem);
            symbolTable = context.SymbolTable;

            var config = new CommandLineConfiguration
                         {
                             Filter = commandLineFilter,
                             Startup =
                             {
                                 ImplicitFilters = new List<string>(values ?? Enumerable.Empty<string>()),
                             },
                             Engine =
                             {
                                 DefaultFilter = defaultFilter,
                             }
                         };

            RootFilter rootFilter;
            XAssert.IsTrue(EngineSchedule.TryGetPipFilter(loggingContext, context, config, config, TryGetPathByMountName,
                rootFilter: out rootFilter));
            return rootFilter;
        }

        private bool TryGetPathByMountName(string mountName, out AbsolutePath path)
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void TryGetValuesImpactingGraphFingerprint()
        {
            // Mounts should resolve
            RunGraphFingerprintValueTest(new string[] { }, "output='Mount[DummyMount]'", new string[] { });

            // Value can come from command line filter
            RunGraphFingerprintValueTest(new string[] { "dummyValue" }, "value='dummyValue'", new string[] { });

            // Error should bubble through
            RunGraphFingerprintValueTest(new string[] { "dummyValue" }, "badFilter='error err'", new string[] { }, expectSuccess: false);
        }

        private void RunGraphFingerprintValueTest(string[] expectedValues, string commandLineFilter, string[] commandLineValues, bool expectSuccess = true)
        {
            var pathTable = new PathTable();
            // EngineSchedulTest operate on the real filesystem for now
            var fileSystem = new PassThroughFileSystem(pathTable);
            BuildXLContext context = EngineContext.CreateNew(CancellationToken.None, pathTable, fileSystem);

            var config = new CommandLineConfiguration
                         {
                             Filter = commandLineFilter,
                             Startup = { ImplicitFilters = new List<string>(commandLineValues) },
                             Engine = { DefaultFilter = null }
                         };
            EvaluationFilter evaluationFilter;

            if (!expectSuccess)
            {
                XAssert.IsFalse(EngineSchedule.TryGetEvaluationFilter(BuildXL.TestUtilities.Xunit.XunitBuildXLTest.CreateLoggingContextForTest(), context, config, config, out evaluationFilter));
            }
            else
            {
                XAssert.IsTrue(EngineSchedule.TryGetEvaluationFilter(BuildXL.TestUtilities.Xunit.XunitBuildXLTest.CreateLoggingContextForTest(), context, config, config, out evaluationFilter));

                foreach (FullSymbol symbol in evaluationFilter.ValueNamesToResolve)
                {
                    string value = symbol.ToString(context.SymbolTable);
                    XAssert.IsTrue(expectedValues.Contains(value));
                }

                XAssert.AreEqual(expectedValues.Length, evaluationFilter.ValueNamesToResolve.Count());
            }
        }

        #endregion
    }
}
