// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.BlobDaemon;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.BlobDaemon
{
    public sealed class UploadLocationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public UploadLocationTests(ITestOutputHelper output)
           : base(output) { }

        [Theory]
        [InlineData("#uri#https://example.com/upload#", true, "UriBased", "https://example.com/upload", null, null, null)]
        [InlineData("#uri#file:///local/path#", true, "UriBased", "file:///local/path", null, null, null)]
        [InlineData("#container#myaccount#mycontainer#path/to/file.txt#", true, "ContainerBased", null, "myaccount", "mycontainer", "path/to/file.txt")]
        [InlineData("#container#account123#container456#folder/subfolder/file.bin#", true, "ContainerBased", null, "account123", "container456", "folder/subfolder/file.bin")]
        public void TryParseValidInputsShouldSucceed(string input, bool expectedSuccess, string expectedKindString, string expectedUri, string expectedAccount, string expectedContainer, string expectedRelativePath)
        {
            var result = UploadLocation.TryParse(input);
            // Compiler is unhappy that UploadLocationKind is marked as internal and that it's an argument of public method. Passing it as string instead to make the compiler happy.
            var expectedKind = Enum.Parse<UploadLocationKind>(expectedKindString, true);

            XAssert.AreEqual(expectedSuccess, result.Succeeded);
            if (expectedSuccess)
            {
                var uploadLocation = result.Result;
                XAssert.AreEqual(expectedKind, uploadLocation.LocationKind);
                XAssert.AreEqual(expectedUri, uploadLocation.Uri);
                XAssert.AreEqual(expectedAccount, uploadLocation.Account);
                XAssert.AreEqual(expectedContainer, uploadLocation.Container);
                XAssert.AreEqual(expectedRelativePath, uploadLocation.RelativePath);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("invalid")]
        [InlineData("#uri#")]
        [InlineData("#uri##")]
        [InlineData("#uri#   #")]
        [InlineData("#container#")]
        [InlineData("#container#account#")]
        [InlineData("#container#account#container#")]
        [InlineData("#container####")]
        [InlineData("#container#account##path#")]
        [InlineData("#container##container#path#")]
        [InlineData("#container#account#container##")]
        [InlineData("#unknown#value#")]
        [InlineData("#invalid#format#test#")]
        [InlineData("#uri#https://example.com#extra#")]
        [InlineData("#container#account#container#path#extra#")]
        public void TryParseInvalidInputsShouldFail(string input)
        {
            var result = UploadLocation.TryParse(input);

            XAssert.IsFalse(result.Succeeded);
        }

        [Fact]
        public void TryParseUriBasedLocationShouldCreateCorrectObject()
        {
            var input = "#uri#https://myaccount.blob.core.windows.net/container/path#";
            var result = UploadLocation.TryParse(input);

            XAssert.IsTrue(result.Succeeded);
            var location = result.Result;
            XAssert.AreEqual(UploadLocationKind.UriBased, location.LocationKind);
            XAssert.AreEqual("https://myaccount.blob.core.windows.net/container/path", location.Uri);
            XAssert.IsNull(location.Account);
            XAssert.IsNull(location.Container);
            XAssert.IsNull(location.RelativePath);
        }

        [Fact]
        public void TryParseContainerBasedLocationShouldCreateCorrectObject()
        {
            var input = "#container#storageaccount#blobcontainer#uploads/file.txt#";
            var result = UploadLocation.TryParse(input);

            XAssert.IsTrue(result.Succeeded);
            var location = result.Result;
            XAssert.AreEqual(UploadLocationKind.ContainerBased, location.LocationKind);
            XAssert.IsNull(location.Uri);
            XAssert.AreEqual("storageaccount", location.Account);
            XAssert.AreEqual("blobcontainer", location.Container);
            XAssert.AreEqual("uploads/file.txt", location.RelativePath);
        }

        [Fact]
        public void TryParseNullInputShouldFail()
        {
            var result = UploadLocation.TryParse(null);

            XAssert.IsFalse(result.Succeeded);
        }

        [Theory]
        [InlineData("#uri# #")]
        [InlineData("#container# #container#path#")]
        [InlineData("#container#account# #path#")]
        [InlineData("#container#account#container# #")]
        public void TryParseWhitespaceInFieldsShouldFail(string input)
        {
            var result = UploadLocation.TryParse(input);

            XAssert.IsFalse(result.Succeeded);
        }
    }
}