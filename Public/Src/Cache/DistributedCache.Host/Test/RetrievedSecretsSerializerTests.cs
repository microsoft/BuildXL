// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.Internal;
using Xunit;

namespace BuildXL.Cache.Host.Test;

public class RetrievedSecretsSerializerTests
{
    [Fact]
    public void TestSerialization()
    {
        var secretsMap = new Dictionary<string, Secret>
                               {
                                   ["cbcache-test-redis-dm_s1"] =
                                       new PlainTextSecret("Fake secret that is quite long to emulate the size of the serialized entry."),
                                   ["cbcache-test-redis-secondary-dm_s1"] =
                                       new PlainTextSecret("Fake secret that is quite long to emulate the size of the serialized entry."),
                                   ["cbcache-test-event-hub-dm_s1"] =
                                       new PlainTextSecret("Fake secret that is quite long to emulate the size of the serialized entry."),
                                   ["cbcacheteststorage-dm_s1-sas"] =
                                       new UpdatingSasToken(new SasToken("token_name", "storage_account", "resource_path")),
                                   ["ContentMetadataBlobSecretName-dm_s1"] = new PlainTextSecret(
                                       "Fake secret that is quite long to emulate the size of the serialized entry.")
                               };
        var secrets = new RetrievedSecrets(secretsMap);

        var text = RetrievedSecretsSerializer.Serialize(secrets);

        var deserializedSecretsMap = RetrievedSecretsSerializer.Deserialize(text).ShouldBeSuccess().Value.Secrets;

        Assert.Equal(secretsMap.Count, deserializedSecretsMap.Count);

        foreach (var kvp in secretsMap)
        {
            Assert.Equal(kvp.Value, deserializedSecretsMap[kvp.Key]);
            Assert.Equal(kvp.Value, deserializedSecretsMap[kvp.Key]);
        }
    }
}
