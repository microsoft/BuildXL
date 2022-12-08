// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;
using System.Linq;
using BuildXL.Pips.Graph;
using BuildXL.Utilities.Tracing;
#if MICROSOFT_INTERNAL
using Microsoft.Security.CredScan.ClientLib;
using Microsoft.Security.CredScan.KnowledgeBase.Client;
#endif

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// This class is used to detect credentials in environment variables.
    /// </summary>
    /// <remarks>
    /// For now the implementation of the methods in the class are not yet finalized until sufficient information is collected from logging of the warnings on detection of credentials.
    /// </remarks>
    public sealed class CredentialScanner
    {
        // The dictionary stores the environmentVariable and it also stores the result of the CredScan.
        private ConcurrentDictionary<string, bool> m_scannedEnvVars = new ConcurrentDictionary<string, bool>();

        /// <summary>
        /// This property is used to avoid creating CredentialScannerFactory instance for un-related unit tests.
        /// </summary>
        public readonly bool EnableCredScan = false;

        /// <summary>
        /// Counter used to evaluate the time take for credscan.
        /// </summary>
        public CounterCollection<CredScanCounter> Counters;

#if MICROSOFT_INTERNAL
        /// <summary>
        /// CredentialScannerFactory object
        /// </summary>
        private readonly ICredentialScanner m_credScan;
#endif

        /// <summary>
        /// CredScan
        /// </summary>
        /// <param name="enableCredScan"></param>
        public CredentialScanner(bool enableCredScan)
        {
#if MICROSOFT_INTERNAL
            EnableCredScan = enableCredScan;
            if (enableCredScan)
            {
                m_credScan = CredentialScannerFactory.Create();
                Counters = new CounterCollection<CredScanCounter>();
            }
#endif
        }

        /// <summary>
        /// This method is used to scan env variables for credentials.
        /// </summary>
        public bool CredentialsDetected(string envVarKey, string envVarValue)
        {
#if MICROSOFT_INTERNAL
            // Converting the env variable into the below pattern.
            // Ex: string input = "password: Cr3d5c@n_D3m0_P@55w0rd";
            // The above example is one of the suggested patterns to represent the input string which is to be passed to the CredScan method.
            string environmentVariable = envVarKey + ":" + envVarValue;

            // This has been added to deal with the scenario when the environment variable is present and also detected as a credential previously.
            if (m_scannedEnvVars.TryGetValue(environmentVariable, out var isCredential))
            {
               return isCredential;
            }
            var result = m_credScan.Scan(environmentVariable);
            var secretsDetected = result?.Any() == true;
            m_scannedEnvVars.TryAdd(environmentVariable, secretsDetected);
            return secretsDetected;
#else
            return false;
#endif
        }
    }
}
