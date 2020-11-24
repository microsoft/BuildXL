using System;
using System.Diagnostics.ContractsLight;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stats;

namespace BuildXL.Cache.Monitor.Library.IcM
{
    public class KeyVaultClient
    {
        internal Counter IcmCallsCounter = new Counter("IcmCalls");

        private readonly TimeSpan _cacheTimeToLive;
        private readonly Uri _uri;
        private readonly ClientSecretCredential _azureCreds;

        // To make sure that we don't overwhelm KeyVault, cache certificates, since they shouldn't change frequently.
        private readonly VolatileMap<string, X509Certificate2> _cachedCertificates;

        public KeyVaultClient(string keyVaultUrl, string azureTenantId, string azureAppId, string azureAppKey, IClock clock, TimeSpan cacheTimeToLive)
        {
            _cachedCertificates = new VolatileMap<string, X509Certificate2>(clock);
            _uri = new Uri(keyVaultUrl);
            _azureCreds = new ClientSecretCredential(azureTenantId, azureAppId, azureAppKey);
            _cacheTimeToLive = cacheTimeToLive;
        }

        public async Task<X509Certificate2> GetCertificateAsync(string certificateName)
        {
            if (_cachedCertificates.TryGetValue(certificateName, out var certificate))
            {
                return certificate;
            }

            var client = new SecretClient(_uri, _azureCreds);

            IcmCallsCounter.Increment();

            var certResponse = await client.GetSecretAsync(certificateName);
            var bytes = Convert.FromBase64String(certResponse.Value.Value);
            var cert = new X509Certificate2(bytes);

            var added = _cachedCertificates.TryAdd(certificateName, cert, _cacheTimeToLive, replaceIfExists: true);
            Contract.Assert(added, "Failed to add certificate to cache.");

            return cert;
        }
    }
}
