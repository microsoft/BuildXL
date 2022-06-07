// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Global configuration for compliant builds on CmdAgents.
    /// </summary>
    [Serializable]
    public sealed class CompliantBuildCmdAgentSettings
    {
        /// <summary>
        /// The certificate being used to authenticate to CloudSign.
        /// </summary>
        public string AuthenticationCertificateDistinguishedName { get; set; }

        public string ManifestSignAuthenticationCertificateDistinguishedName { get; set; }

        public List<CertificateChainValidationElement> ValidClientAuthenticationChains { get; set; }
    }
}
