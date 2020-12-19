// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Xunit;

namespace Test.BuildXL.FrontEnd.Download
{
    /// <nodoc/>
    public class TestRequestHandler
    {
        /// <summary>
        /// Starts a mock http server for tests
        /// </summary>
        public static void StartRequestHandler(HttpListener listener, AlternativeDataIndicator alternativeDataIndicator, RequestCount webRequestCount)
        {
#pragma warning disable EPC13 // Suspiciously unobserved result.
            Task.Run(
                () =>
                {
                    while (listener.IsListening)
                    {
                        // Note: The GetContext method blocks while waiting for a request. 
                        var context = listener.GetContext();
                        var fileName = Path.GetFileName(context.Request.Url.LocalPath);
                        var response = context.Response;

                        byte[] worldBuffer = System.Text.Encoding.UTF8.GetBytes("Hello World");
                        byte[] galaxyBuffer = System.Text.Encoding.UTF8.GetBytes("Hello Galaxy");
                        byte[] universeBuffer = System.Text.Encoding.UTF8.GetBytes("Hello Universe");

                        switch (Path.GetExtension(fileName))
                        {
                            case ".zip":
                                MemoryStream outputMemStream = new MemoryStream();
                                ZipOutputStream zipStream = new ZipOutputStream(outputMemStream);

                                zipStream.SetLevel(5);

                                AddFile(zipStream, "world", worldBuffer);
                                AddFile(zipStream, "galaxy", galaxyBuffer);
                                AddFile(zipStream, "multi/universe", universeBuffer);

                                zipStream.IsStreamOwner = false;
                                zipStream.Close();

                                outputMemStream.Position = 0;
                                response.ContentLength64 = outputMemStream.Length;
                                StreamUtils.Copy(outputMemStream, response.OutputStream, new byte[4096]);
                                break;
                            case ".404":
                                response.StatusCode = 404;
                                response.ContentLength64 = worldBuffer.Length;
                                response.OutputStream.Write(worldBuffer, 0, worldBuffer.Length);
                                break;
                            case ".txt":
                                var buffer = alternativeDataIndicator.UseAlternativeData
                                    ? galaxyBuffer
                                    : worldBuffer;
                                response.ContentLength64 = buffer.Length;
                                response.OutputStream.Write(buffer, 0, buffer.Length);
                                break;
                            default:
                                Assert.True(false, "Unexpected http request..");
                                break;
                        }


                        // Write buffer and close request
                        response.Headers.Add("Content-type: application/octet-stream");
                        response.Headers.Add("Content-Description: File Transfer");
                        response.Headers.Add($"Content-Disposition: attachment; filename=\"{fileName}\")");
                        response.OutputStream.Close();

                        webRequestCount.Count += 1;
                    }
                });
#pragma warning restore EPC13 // Suspiciously unobserved result.
        }

        private static void AddFile(ZipOutputStream zipStream, string name, byte[] buffer)
        {
            var newEntry = new ZipEntry(name);
            newEntry.DateTime = DateTime.Now;
            zipStream.PutNextEntry(newEntry);

            zipStream.Write(buffer, 0, buffer.Length);
            zipStream.CloseEntry();
        }
    }
}
