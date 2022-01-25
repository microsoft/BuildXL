// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.Internal;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Host.Test
{
    public class InterProcessSecretsCommunicatorTests : TestBase
    {
        public InterProcessSecretsCommunicatorTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public void TestMemoryMappedFileHelper()
        {
            string fileName = "MyCustomFileName";
            using var file1 = MemoryMappedFileHelper.CreateMemoryMappedFileWithContent(fileName, "Content 1");

            var content = MemoryMappedFileHelper.ReadContent(fileName);
            Logger.Debug("Content1: " + content);
            Assert.Equal("Content 1", content);

            MemoryMappedFileHelper.UpdateContent(fileName, "Content 2");
            content = MemoryMappedFileHelper.ReadContent(fileName);
            Logger.Debug("Content2: " + content);
            Assert.Equal("Content 2", content);
        }

        [Fact]
        public void TestUpdatableTokens()
        {
            var updatingToken = new UpdatingSasToken(new SasToken(token: "Token 1", "Storage Account 1", "Resource Path 1"));
            var originalSecrets = new RetrievedSecrets(
                new Dictionary<string, Secret>()
                {
                    ["Secret 1"] = new PlainTextSecret("Secret Value 1"),
                    ["Secret 2"] = updatingToken
                });

            var context = new OperationContext(new Context(Logger));

            using var secretsExposer = InterProcessSecretsCommunicator.Expose(context, originalSecrets);

            using var readSecrets = InterProcessSecretsCommunicator.ReadExposedSecrets(context, pollingIntervalInSeconds: 10_000);

            AssertSecretsAreEqual(originalSecrets, readSecrets);

            int tokenUpdated = 0;
            ((UpdatingSasToken)readSecrets.Secrets["Secret 2"]).TokenUpdated += (sender, token) =>
                                                                                 {
                                                                                     tokenUpdated++;
                                                                                 };

            // Updating the token
            updatingToken.UpdateToken(new SasToken("1", "2", "3"));

            readSecrets.RefreshSecrets(context);
            AssertSecretsAreEqual(originalSecrets, readSecrets);

            Assert.Equal(1, tokenUpdated); // An event should be raised

            // Updating token once again
            updatingToken.UpdateToken(new SasToken("2", "2", "3"));

            readSecrets.RefreshSecrets(context);

            AssertSecretsAreEqual(originalSecrets, readSecrets);

            Assert.Equal(2, tokenUpdated); // An event should be raised
        }

        private static void AssertSecretsAreEqual(RetrievedSecrets left, RetrievedSecrets right)
        {
            Assert.Equal(left.Secrets.Count, right.Secrets.Count);

            foreach (var (name, secret) in left.Secrets)
            {
                if (right.Secrets.TryGetValue(name, out var rightSecret))
                {
                    Assert.Equal(secret, rightSecret);
                }
                else
                {
                    Assert.True(false, $"Can't find a secret with name '{name}' in the 'right' variable.");
                }
            }
        }
    }
}
