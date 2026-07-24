// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Utilities.Configuration;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Grpc
{
    [Collection("GrpcEncryptionUtils")] // Serialize: these tests mutate process-wide environment settings and static overrides.
    public class GrpcEncryptionUtilsTests
    {
        /// <summary>
        /// When neither command-line override is set, encryption still resolves to enabled: the encryption toggle
        /// defaults to true (<see cref="EngineEnvironmentSettings.GrpcEncryptionEnabled"/>) and the certificate subject
        /// name falls back to the CB_BUILDUSERCERTIFICATE_NAME environment variable that the infra always populates.
        /// </summary>
        [Fact]
        public void EncryptionEnabledWhenOverridesNullAndInfraCertificatePresent()
        {
            RunWithCleanEnvironment(() =>
            {
                // Command-line arguments were not passed, so both overrides are null.
                GrpcEncryptionUtils.CertificateSubjectNameOverride = null;
                GrpcEncryptionUtils.EncryptionEnabledOverride = null;

                // Simulate the infra always populating the build user certificate name.
                Environment.SetEnvironmentVariable(EngineEnvironmentSettings.CBBuildUserCertificateName.Name, "CN=Test.Infra.Certificate");
                EngineEnvironmentSettings.Reset();

                GrpcEncryptionUtils.TryGetCertificateSubjectName().Should().Be("CN=Test.Infra.Certificate");
                GrpcEncryptionUtils.IsEncryptionEnabled().Should().BeTrue();
            });
        }

        /// <summary>
        /// When neither command-line override is set and no certificate is available from any source, encryption resolves
        /// to disabled even though the toggle defaults to true, because a certificate subject name is required.
        /// </summary>
        [Fact]
        public void EncryptionDisabledWhenOverridesNullAndNoCertificate()
        {
            RunWithCleanEnvironment(() =>
            {
                GrpcEncryptionUtils.CertificateSubjectNameOverride = null;
                GrpcEncryptionUtils.EncryptionEnabledOverride = null;

                GrpcEncryptionUtils.TryGetCertificateSubjectName().Should().BeNull();
                GrpcEncryptionUtils.IsEncryptionEnabled().Should().BeFalse();
            });
        }

        /// <summary>
        /// Runs the given test body with the relevant environment variables and static overrides cleared, restoring the
        /// original process-wide state afterwards.
        /// </summary>
        private static void RunWithCleanEnvironment(Action body)
        {
            string certEnvName = EngineEnvironmentSettings.GrpcCertificateSubjectName.Name;
            string cbCertEnvName = EngineEnvironmentSettings.CBBuildUserCertificateName.Name;
            string encryptionEnabledEnvName = EngineEnvironmentSettings.GrpcEncryptionEnabled.Name;

            string originalCert = Environment.GetEnvironmentVariable(certEnvName);
            string originalCbCert = Environment.GetEnvironmentVariable(cbCertEnvName);
            string originalEncryptionEnabled = Environment.GetEnvironmentVariable(encryptionEnabledEnvName);
            string savedCertOverride = GrpcEncryptionUtils.CertificateSubjectNameOverride;
            bool? savedEnabledOverride = GrpcEncryptionUtils.EncryptionEnabledOverride;

            try
            {
                // Start from a clean slate so the fallback chain is deterministic regardless of the host environment.
                Environment.SetEnvironmentVariable(certEnvName, null);
                Environment.SetEnvironmentVariable(cbCertEnvName, null);
                Environment.SetEnvironmentVariable(encryptionEnabledEnvName, null);
                EngineEnvironmentSettings.Reset();

                body();
            }
            finally
            {
                Environment.SetEnvironmentVariable(certEnvName, originalCert);
                Environment.SetEnvironmentVariable(cbCertEnvName, originalCbCert);
                Environment.SetEnvironmentVariable(encryptionEnabledEnvName, originalEncryptionEnabled);
                GrpcEncryptionUtils.CertificateSubjectNameOverride = savedCertOverride;
                GrpcEncryptionUtils.EncryptionEnabledOverride = savedEnabledOverride;
                EngineEnvironmentSettings.Reset();
            }
        }
    }
}
