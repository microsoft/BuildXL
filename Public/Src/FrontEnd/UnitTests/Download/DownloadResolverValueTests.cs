// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Test.DScript.Ast.DScriptV2;
using Xunit;

namespace Test.BuildXL.FrontEnd.Download
{
    public class DownloadResolverValueTests : DScriptV2Test
    {
        public DownloadResolverValueTests(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.FrontEnd.Download.ETWLogger.Log);
        }

        [Fact]
        public void NamedValuesAreExposed()
        {
            const string TestServer = "http://localhost:9754/";

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(TestServer);
                listener.Start();

                TestRequestHandler.StartRequestHandler(listener, new AlternativeDataIndicator(), new RequestCount());

                Build().Configuration($@"
config({{
    resolvers: [
        {{
            kind: 'Download',
            downloads: [{{
                moduleName: 'download',
                url: '{TestServer}value-test.zip',
                downloadedValueName: 'theDownloaded',
                archiveType: 'zip',
                extractedValueName: 'theExtracted',
                requiredFiles: [r`multi/universe`, r`world`],
            }}]
        }},
        {{
            kind: 'DScript', 
        }}
    ],
    mounts: [
        {{
            name: a`LocalLow`,
            path: p`Out/Ignore`,
            trackSourceFileChanges: false,
            isWritable: true,
            isReadable: true,
        }},
    ],
}});")
               .AddSpec("package.dsc", @"
import * as downloads from 'download';
const d = downloads.theDownloaded;
const e = downloads.theExtracted;
const files: Map<RelativePath, File> = downloads.theExtractedFiles;
const checkCount = Contract.assert(files.count() === 2);
const checkKey = Contract.assert(files.containsKey(r`multi/universe`));
const checkWorld = Contract.assert(files.get(r`world`).path === e.root.combine(r`world`));
const checkUniverse = Contract.assert(files.get(r`multi/universe`).path === e.root.combine(r`multi/universe`));
const values: File[] = files.values();
const checkValuesCount = Contract.assert(values.length === 2);
const checkWorldValue = Contract.assert(values.some(file => file === files.get(r`world`)));
const checkUniverseValue = Contract.assert(values.some(file => file === files.get(r`multi/universe`)));
")
               .RootSpec("package.dsc")
               .EvaluateWithNoErrors();

                listener.Stop();
                listener.Close();
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RequiredFileMapIsNotExportedForOmittedOrEmptyList(bool emptyList)
        {
            var diagnostic = Build().Configuration($@"
config({{
    resolvers: [
        {{
            kind: 'Download',
            downloads: [{{
                moduleName: 'download',
                url: 'http://localhost:9754/value-test.zip',
                archiveType: 'zip',
                {(emptyList ? "requiredFiles: []," : string.Empty)}
            }}]
        }},
        {{ kind: 'DScript' }}
    ]
}});")
                .AddSpec("package.dsc", @"
const files = importFrom('download').extractedFiles;
")
                .RootSpec("package.dsc")
                .EvaluateWithFirstError();

            Assert.Contains("extractedFiles", diagnostic.FullMessage);
            Assert.Contains("does not exist", diagnostic.FullMessage);
        }
    }
}