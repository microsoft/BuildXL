// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// gRPC encryption settings for the engine distribution layer, resolved from the build configuration.
    /// </summary>
    /// <remarks>
    /// Unlike the cache layer (which reads the process-wide override properties on <see cref="GrpcEncryptionUtils"/>), the
    /// engine plumbs these values explicitly from <see cref="IDistributionConfiguration"/> down to the gRPC server and
    /// client objects. The command-line-vs-environment fallback is resolved once, here, via <see cref="GrpcEncryptionUtils"/>.
    /// </remarks>
    internal readonly record struct GrpcEncryptionSettings(bool EncryptionEnabled, string CertificateSubjectName, bool AuthenticationEnabled)
    {
        /// <summary>
        /// Resolves the encryption settings from the build configuration, falling back to the environment variables when
        /// the corresponding command-line arguments are not set.
        /// </summary>
        public static GrpcEncryptionSettings Create(IConfiguration configuration)
        {
            bool? encryptionEnabledOverride = configuration.Distribution.GrpcEncryptionEnabled;
            string certificateSubjectNameOverride = configuration.Distribution.GrpcCertificateSubjectName;

            bool encryptionEnabled = GrpcEncryptionUtils.IsEncryptionEnabled(encryptionEnabledOverride, certificateSubjectNameOverride);
            string certificateSubjectName = GrpcEncryptionUtils.TryGetCertificateSubjectName(certificateSubjectNameOverride);

            // Authentication additionally requires the build identity token (which remains environment-based).
            bool authenticationEnabled = encryptionEnabled && EngineEnvironmentSettings.CBBuildIdentityTokenPath.Value != null;

            return new GrpcEncryptionSettings(encryptionEnabled, certificateSubjectName, authenticationEnabled);
        }
    }
}
