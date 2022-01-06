using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.Host.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildXL.Launcher.Server
{
    public class KeyVaultSecretsProvider : ISecretsProvider
    {
        private SecretClient _client;

        public KeyVaultSecretsProvider(string keyVaultUri)
        {
            _client = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        }

        public async Task<RetrievedSecrets> RetrieveSecretsAsync(
            List<RetrieveSecretsRequest> requests, 
            CancellationToken token)
        {
            var secrets = new Dictionary<string, Secret>();

            foreach (var request in requests)
            {
                var secretResponse = await _client.GetSecretAsync(request.Name, cancellationToken: token);
                var secret = secretResponse.Value.Value;
                Contract.Assert(request.Kind == SecretKind.PlainText);
                secrets[request.Name] = new PlainTextSecret(secret);
            }

            return new RetrievedSecrets(secrets);
        }
    }
}
