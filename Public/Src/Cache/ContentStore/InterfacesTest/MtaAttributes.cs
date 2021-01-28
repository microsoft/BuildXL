// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace BuildXL.Cache.ContentStore.Interfaces.Test
{
    /// <summary>
    /// Ensures a fact is run using an MTA thread. Based on sample: https://github.com/xunit/samples.xunit/tree/main/STAExamples
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("BuildXL.Cache.ContentStore.Interfaces.Test.MtaFactDiscoverer", "BuildXL.Cache.ContentStore.Interfaces.Test")]
    public class MtaFactAttribute : FactAttribute { }

    /// <nodoc />
    public class MtaFactDiscoverer : IXunitTestCaseDiscoverer
    {
        private readonly FactDiscoverer _factDiscoverer;

        /// <nodoc />
        public MtaFactDiscoverer(IMessageSink diagnosticMessageSink) => _factDiscoverer = new FactDiscoverer(diagnosticMessageSink);

        /// <nodoc />
        public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
        {
            return _factDiscoverer
                .Discover(discoveryOptions, testMethod, factAttribute)
                .Select(testCase => new MtaTestCase(testCase));
        }
    }

    /// <summary>
    /// Ensures a theory is run using an MTA thread. Based on sample: https://github.com/xunit/samples.xunit/tree/main/STAExamples
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("BuildXL.Cache.ContentStore.Interfaces.Test.MtaTheoryDiscoverer", "BuildXL.Cache.ContentStore.Interfaces.Test")]
    public class MtaTheoryAttribute : TheoryAttribute { }

    /// <nodoc />
    public class MtaTheoryDiscoverer : IXunitTestCaseDiscoverer
    {
        private readonly TheoryDiscoverer _theoryDiscoverer;

        /// <nodoc />
        public MtaTheoryDiscoverer(IMessageSink diagnosticMessageSink) => _theoryDiscoverer = new TheoryDiscoverer(diagnosticMessageSink);

        /// <nodoc />
        public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
        {
            return _theoryDiscoverer
                .Discover(discoveryOptions, testMethod, factAttribute)
                .Select(testCase => new MtaTestCase(testCase));
        }
    }

    /// <nodoc />
    [DebuggerDisplay(@"\{ class = {TestMethod.TestClass.Class.Name}, method = {TestMethod.Method.Name}, display = {DisplayName}, skip = {SkipReason} \}")]
    public class MtaTestCase : IXunitTestCase
    {
        private IXunitTestCase _testCase;

        /// <nodoc />
        public MtaTestCase(IXunitTestCase testCase) => _testCase = testCase;

        /// <summary/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer", error: true)]
        public MtaTestCase() { }

        /// <nodoc />
        public IMethodInfo Method => _testCase.Method;

        /// <nodoc />
        public Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return _testCase.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource);
            }

            var tcs = new TaskCompletionSource<RunSummary>();
            var thread = new Thread(() =>
            {
                Assert.Equal(Thread.CurrentThread.GetApartmentState(), ApartmentState.MTA);

                try
                {
                    var result = _testCase.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource).GetAwaiter().GetResult();
                    tcs.SetResult(result);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return tcs.Task;
        }

        /// <nodoc />
        public string DisplayName => _testCase.DisplayName;

        /// <nodoc />
        public string SkipReason => _testCase.SkipReason;

        /// <nodoc />
        public ISourceInformation SourceInformation
        {
            get => _testCase.SourceInformation;
            set => _testCase.SourceInformation = value;
        }

        /// <nodoc />
        public ITestMethod TestMethod => _testCase.TestMethod;

        /// <nodoc />
        public object[] TestMethodArguments => _testCase.TestMethodArguments;

        /// <nodoc />
        public Dictionary<string, List<string>> Traits => _testCase.Traits;

        /// <nodoc />
        public string UniqueID => _testCase.UniqueID;

        /// <nodoc />
        public Exception InitializationException => _testCase.InitializationException;

        /// <nodoc />
        public int Timeout => _testCase.Timeout;

        /// <nodoc />
        public void Deserialize(IXunitSerializationInfo info) => _testCase = info.GetValue<IXunitTestCase>("InnerTestCase");

        /// <nodoc />
        public void Serialize(IXunitSerializationInfo info) => info.AddValue("InnerTestCase", _testCase);
    }
}
