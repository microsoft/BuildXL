using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.Monitor.Library.IcM;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public class IcmClientTests : TestBase
    {
        public IcmClientTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(Skip = "For manual testing only")]
        public async Task IcmClientTestAsync()
        {
            Debugger.Launch();

            LoadApplicationKey().ThrowIfFailure();

            var config = new App.Monitor.Configuration();

            var clock = new MemoryClock();
            clock.Increment(); 

            var keyVault = new KeyVaultClient(
                config.KeyVaultUrl,
                config.KeyVaultCredentials.TenantId,
                config.KeyVaultCredentials.AppId,
                config.KeyVaultCredentials.AppKey,
                clock,
                cacheTimeToLive: TimeSpan.FromSeconds(1));

            keyVault.IcmCallsCounter.Value.Should().Be(0);

            // Simulate that the certificate has been acquired before.
            _ = await keyVault.GetCertificateAsync(config.IcmCertificateName);
            keyVault.IcmCallsCounter.Value.Should().Be(1);

            var icmClient = new IcmClient(keyVault, config.IcmUrl, config.IcmConnectorId, config.IcmCertificateName, clock);

            var incident = new IcmIncident(
                stamp: "Test",
                environment: "PROD",
                machines: new [] { "MachineA", "MachineB" },
                correlationIds: new[] { "GuidA", "GuidB" },
                severity: 4,
                description: "This incident was created for testing the cache monitor",
                title: "Cache Monitor Test Incident",
                incidentTime: DateTime.Now,
                cacheTimeToLive: null);

            await icmClient.EmitIncidentAsync(incident);

            // Should have used cached cert.
            keyVault.IcmCallsCounter.Value.Should().Be(1);

            // Simulate that the certificate will be acquired in the future.
            clock.AddSeconds(2);
            _ = await keyVault.GetCertificateAsync(config.IcmCertificateName);
            keyVault.IcmCallsCounter.Value.Should().Be(2);
        }
    }
}
