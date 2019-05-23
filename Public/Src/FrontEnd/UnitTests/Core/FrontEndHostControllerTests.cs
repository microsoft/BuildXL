// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Logger = BuildXL.FrontEnd.Core.Tracing.Logger;

namespace Test.BuildXL.FrontEnd.Core
{
    public class FrontEndHostControllerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {

        private static readonly IReadOnlyDictionary<string, string> emptyQualifier = new Dictionary<string, string>(0);
        private static readonly IReadOnlyDictionary<string, string> badKeyQualifier = new Dictionary<string, string>() { { "a;=c", "b" } };
        private static readonly IReadOnlyDictionary<string, string> badValueQualifier = new Dictionary<string, string>() { { "a", "b;=c" } };

        public FrontEndHostControllerTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void DownloadFileTestWithHash()
        {
            var host = CreateHost();

            var testData = new TestData(host.FrontEndContext.PathTable, TestOutputDirectory);

            var result = host.DownloadFile(testData.SourceFile, testData.TargetPath, testData.SourceHash, "testFile").Result;
            VerifySuccessfullDownload(result, testData);

            // hack the cache in the host to ensure the cache is not used on second call
            var lastWrite = File.GetLastWriteTimeUtc(testData.TargetFile);
            host.InitializeInternalForTesting(Task.FromResult(default(Possible<EngineCache>)), testData.TargetPath);

            result = host.DownloadFile(testData.SourceFile, testData.TargetPath, testData.SourceHash, "testFile").Result;
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(testData.TargetFile)); // File shouldn't have been touched
            VerifySuccessfullDownload(result, testData);
        }

        [Fact]
        public void DownloadFileThenFromCacheWithHash()
        {
            var host = CreateHost();

            var testData = new TestData(host.FrontEndContext.PathTable, TestOutputDirectory);

            var result = host.DownloadFile(testData.SourceFile, testData.TargetPath, testData.SourceHash, "testFile").Result;
            VerifySuccessfullDownload(result, testData);

            // Delete the target file to force redownload
            FileUtilities.DeleteFile(testData.TargetFile);

            // Detele the sorucefile to make the download fail if it was used since it should be pulled from the cache
            FileUtilities.DeleteFile(testData.SourceFile);

            result = host.DownloadFile(testData.SourceFile, testData.TargetPath, testData.SourceHash, "testFile").Result;
            VerifySuccessfullDownload(result, testData);
        }

        [Fact]
        public void DownloadFileThenFromCacheWithoutHash()
        {
            var host = CreateHost();

            var testData = new TestData(host.FrontEndContext.PathTable, TestOutputDirectory);

            var result = host.DownloadFile(testData.SourceFile, testData.TargetPath, null, "testFile").Result;
            VerifySuccessfullDownload(result, testData);

            // Delete the target file to force redownload
            File.Delete(testData.TargetFile);

            // Detele the sorucefile to make the download fail if it was used since it should be pulled from the cache
            File.Delete(testData.SourceFile);

            result = host.DownloadFile(testData.SourceFile, testData.TargetPath, null, "testFile").Result;
            VerifySuccessfullDownload(result, testData);
        }

        [Fact]
        public void ReDownloadWhenNoMatchWithHash()
        {
            var host = CreateHost();

            var testData = new TestData(host.FrontEndContext.PathTable, TestOutputDirectory);

            FileUtilities.CreateDirectory(Path.GetDirectoryName(testData.TargetFile));
            File.WriteAllText(testData.TargetFile, "IncorrectContent");

            var result = host.DownloadFile(testData.SourceFile, testData.TargetPath, testData.SourceHash, "testFile").Result;
            VerifySuccessfullDownload(result, testData);
        }

        [Fact]
        public void ReDownloadWhenNoMatchWithoutHash()
        {
            var host = CreateHost();

            var testData = new TestData(host.FrontEndContext.PathTable, TestOutputDirectory);

            FileUtilities.CreateDirectory(Path.GetDirectoryName(testData.TargetFile));
            File.WriteAllText(testData.TargetFile, "IncorrectContent");

            var result = host.DownloadFile(testData.SourceFile, testData.TargetPath, null, "testFile").Result;
            VerifySuccessfullDownload(result, testData);
        }

        [Fact]
        public void HandleDownloadFailure()
        {
            var host = CreateHost();

            var testData = new TestData(host.FrontEndContext.PathTable, TestOutputDirectory);

            // Delete source location to ensure download fails.
            FileUtilities.DeleteFile(testData.SourceFile);

            var result = host.DownloadFile(testData.SourceFile, testData.TargetPath, null, "testFile").Result;
            Assert.False(result.Succeeded);
            var failure = ((FileDownloadFailure) result.Failure);
            Assert.Equal(FailureType.CopyFile, failure.FailureType);
            Assert.Equal(testData.SourceFile, failure.Url);
            Assert.Equal(testData.TargetFile, failure.TargetLocation);
            Assert.NotNull(failure.Exception);
        }

        private static void VerifySuccessfullDownload(Possible<ContentHash> result, TestData testData)
        {
            if (!result.Succeeded)
            {
                Assert.True(result.Succeeded, result.Failure.DescribeIncludingInnerFailures());
            }
            Assert.Equal(testData.SourceHash, result.Result);
            Assert.True(File.Exists(testData.TargetFile));
            Assert.Equal(testData.TestContent, File.ReadAllText(testData.TargetFile));
        }

        [Theory]
        // Named cases
        [InlineData("named",        "n1-v1+n2-v2",      true,  false)]
        [InlineData("Other",        "n1-o1",            true,  false)]
        [InlineData("other",        "",                 true,  false, "'other'", "Other", "Available named qualifiers are")]
        [InlineData("named",        "",                 false, false, "no named qualifiers exist in the configuration")]
        // Literal cases
        [InlineData("a=b",          "a-b",              true,  false)]
        [InlineData("a=b;c=d",      "a-b+c-d",          false, false)]
        [InlineData("a=b;a=d",      "a-d",              false, false)]
        [InlineData("a=b;b=c;a=",   "b-c",              false, false)]
        [InlineData("a=b;a=",       "",                 false, false, "the qualifier has no values")]
        // Combine with default values
        [InlineData("a=b",          "d1-v1+d2-v2+a-b",  true,  true)]
        [InlineData("d1=v3",        "d1-v3+d2-v2",      true,  true)]
        [InlineData("d2=;d1=v3",    "d1-v3",            true,  true)]
        // Whitespace eating tests
        [InlineData("  a  =  b  ;  c  =  d", "a-b+c-d", false, false)]
        [InlineData("  a  =  b  ;  a  =  d", "a-d",     false, false)]
        // Invalid literal cases
        [InlineData("xxx;a=d",      "",                 false, false, "'xxx' is ill-formed")]
        [InlineData("x=x=x",        "",                 false, false, "'x=x=x' is ill-formed")]
        [InlineData("a=b;;a=d",     "",                 false, false, "it contains an empty key value pair")]
        [InlineData("  ;  ",        "",                 false, false, "it contains an empty key value pair")]
        [InlineData(";",            "",                 false, false, "it contains an empty key value pair")]
        [InlineData("=x;a=b",       "",                 false, false, "'=x' is ill-formed")]
        [InlineData("=;a=b",        "",                 false, false, "'=' is ill-formed")]
        // Removal
        public void TestParseQualifier(string qualifierExpression, string expectedQualifier, bool useNamedQualifiers, bool useDefaultQualifier, params string[] expectedErrorContents)
        {
            var namedQualifiers = useNamedQualifiers ? new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                { "named", new Dictionary<string,string> {  {"n1", "v1"}, { "n2", "v2"} } },
                { "Other", new Dictionary<string,string> {  {"n1", "o1"} } },
            } : null;
            var defaultQualifier = useDefaultQualifier ? new Dictionary<string, string>
            {
                { "d1", "v1"},
                { "d2", "v2"},
            } : null;

            var logger = Logger.CreateLogger(true);
            var loggingContext = new LoggingContext("Test");
            IReadOnlyDictionary<string, string> actualQualifier;

            var result = FrontEndHostController.TryParseQualifiers(logger, loggingContext, qualifierExpression, defaultQualifier, namedQualifiers, out actualQualifier);

            if (expectedErrorContents == null || expectedErrorContents.Length == 0)
            {
                Assert.True(result, "Expected sucessfull parse");
                Assert.True(actualQualifier != null, "Expected non null out value");
                Assert.Equal(0, logger.CapturedDiagnostics.Count);

                var actualQualifierPrint = string.Join("+", actualQualifier.Select(kv => $"{kv.Key}-{kv.Value}"));
                Assert.Equal(expectedQualifier, actualQualifierPrint);
            }
            else
            {
                Assert.False(result, "Expected failed parse");
                Assert.True(actualQualifier == null, "Expected null as out variable");
                Assert.Equal(1, logger.CapturedDiagnostics.Count);
                var errorMessage = logger.CapturedDiagnostics[0].Message;
                foreach (var expectedErrorContent in expectedErrorContents)
                {
                    Assert.True(errorMessage.Contains(expectedErrorContent), $"Message:\n{errorMessage}\nDoes not contain:\n{expectedErrorContent}");
                }
            }
        }

        [Fact]
        public void ValidateDefaultConfig()
        {
            TestQualifierConfigValidation(badKeyQualifier, null, "'a;=c'", "has an invalid key");
            TestQualifierConfigValidation(badValueQualifier, null, "'b;=c'", "has an invalid value");
        }

        [Fact]
        public void ValidateNamedConfig()
        {
            TestQualifierConfigValidation(null, new Dictionary<string, IReadOnlyDictionary<string, string>> { { "name", emptyQualifier } }, "'name'", "has no fields defined");
            TestQualifierConfigValidation(null, new Dictionary<string, IReadOnlyDictionary<string, string>> { { "name", badKeyQualifier } }, "'name'", "'a;=c'", "has an invalid key");
            TestQualifierConfigValidation(null, new Dictionary<string, IReadOnlyDictionary<string, string>> { { "name", badValueQualifier } }, "'name'", "'b;=c'", "has an invalid value");
        }
        

        private void TestQualifierConfigValidation(
            IReadOnlyDictionary<string, string> defaultQualifier,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> namedQualifiers,
            params string[] expectedErrorContents)
        {
            var logger = Logger.CreateLogger(true);
            var loggingContext = new LoggingContext("Test");
            var pathTable = new PathTable();
            var qualifierTable = new QualifierTable(pathTable.StringTable);

            var result = FrontEndHostController.ValidateAndRegisterConfigurationQualifiers(logger, loggingContext, qualifierTable, defaultQualifier, namedQualifiers);

            Assert.False(result, "Expected failed parse");
            Assert.Equal(1, logger.CapturedDiagnostics.Count);
            var errorMessage = logger.CapturedDiagnostics[0].Message;
            foreach (var expectedErrorContent in expectedErrorContents)
            {
                Assert.True(errorMessage.Contains(expectedErrorContent), $"Message:\n{errorMessage}\nDoes not contain:\n{expectedErrorContent}");
            }
        }

        private FrontEndHostController CreateHost()
        {
            var factory = new FrontEndFactory();
            factory.AddFrontEnd(new DummyFrontEnd1());
            factory.TrySeal(new LoggingContext("UnitTest"));

            var context = BuildXLContext.CreateInstanceForTesting();

            var moduleRegistry = new ModuleRegistry(context.SymbolTable);

            var controller = new FrontEndHostController(
                factory, 
                new DScriptWorkspaceResolverFactory(), 
                new EvaluationScheduler(degreeOfParallelism: 8),
                moduleRegistry,
                new FrontEndStatistics(),
                Logger.CreateLogger(),
                collector: null,
                collectMemoryAsSoonAsPossible: false);


            var fileSystem = new InMemoryFileSystem(context.PathTable);

            ((IFrontEndController)controller).InitializeHost(
                new FrontEndContext(context,new LoggingContext("UnitTest"), fileSystem),
                new ConfigurationImpl()
                {
                    FrontEnd = new FrontEndConfiguration()
                    {
                        MaxFrontEndConcurrency = 1,
                    }
                });

            var inMemoryCache = new EngineCache(
                new InMemoryArtifactContentCache(),
                new InMemoryTwoPhaseFingerprintStore());
            controller.InitializeInternalForTesting(
                Task.FromResult(new Possible<EngineCache>(inMemoryCache)),
                AbsolutePath.Create(context.PathTable, TestOutputDirectory));

            return controller;
        }

        private class DummyFrontEnd1 : global::BuildXL.FrontEnd.Sdk.IFrontEnd
        {
            public IReadOnlyCollection<string> SupportedResolvers => new[] { "UnitTest1" };

            public void InitializeFrontEnd(FrontEndHost host, FrontEndContext context, IConfiguration frontEndConfiguration)
            {
                throw new NotImplementedException();
            }

            public IResolver CreateResolver(string kind)
            {
                throw new NotImplementedException();
            }

            public void LogStatistics(Dictionary<string, long> statistics)
            {
                throw new NotImplementedException();
            }
        }

        private class TestData
        {
            public readonly string TestContent;

            public readonly string SourceFile;
            public readonly ContentHash SourceHash;

            public readonly string TargetFile;
            public readonly AbsolutePath TargetPath;

            public TestData(PathTable pathTable, string testFolder)
            {
                TestContent = "ABCDEFG";

                var uniqueFolder = Path.Combine(testFolder, Guid.NewGuid().ToString());
                var sourceFolder = Path.Combine(uniqueFolder, "server");
                Directory.CreateDirectory(sourceFolder);
                SourceFile = Path.Combine(sourceFolder, "file.txt");
                File.WriteAllText(SourceFile, TestContent);
                SourceHash = ContentHashingUtilities.HashFileAsync(SourceFile).Result;

                var targetFolder = Path.Combine(uniqueFolder, "test");
                TargetFile = Path.Combine(targetFolder, "file.txt");
                TargetPath = AbsolutePath.Create(pathTable, TargetFile);
            }
        }
    }
}
