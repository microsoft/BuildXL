// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

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
                extractedValueName: 'theExtracted'
            }}]
        }},
        {{
            kind: 'DScript', 
        }}
    ],
}});")
               .AddSpec("package.dsc", @"
import * as downloads from 'download';
const d = downloads.theDownloaded;
const e = downloads.theExtracted;
")
               .RootSpec("package.dsc")
               .EvaluateWithNoErrors();

                listener.Stop();
                listener.Close();
            }
        }
    }
}