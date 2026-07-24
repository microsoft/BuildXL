// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using Grpc.Core;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Newtonsoft.Json;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Encryption options used when creating gRPC channels.
    /// </summary>
    public record ChannelEncryptionOptions(string CertificateSubjectName, string? AuthorizationTokenFile);

    /// <summary>
    /// Utility methods needed to enable encryption and authentication for gRPC-using services in CloudBuild
    /// </summary>
    public static class GrpcEncryptionUtils
    {
        /// <summary>
        /// Process-wide override for the certificate subject name used to encrypt gRPC communication, or null when not set.
        /// </summary>
        /// <remarks>
        /// Populated by the engine from the command line so that the value can take precedence over the environment
        /// variables without threading the configuration through the many in-proc cache/gRPC layers. The engine
        /// distribution layer does not read this; it plumbs its configuration value explicitly. A null value defers to
        /// the environment variables.
        /// </remarks>
        public static string? CertificateSubjectNameOverride { get; set; }

        /// <summary>
        /// Process-wide override for whether gRPC encryption is enabled, or null when not set.
        /// </summary>
        /// <remarks>
        /// Populated by the engine from the command line. A null value defers to the environment variable.
        /// </remarks>
        public static bool? EncryptionEnabledOverride { get; set; }

        /// <summary>
        /// Resolves the certificate subject name used to encrypt gRPC communication, or null when none is configured.
        /// </summary>
        /// <remarks>
        /// This is the single place where the fallback to the environment variables is resolved: the explicit
        /// <paramref name="certificateSubjectNameOverride"/> (from the command line) takes precedence, then the
        /// environment variables. The engine distribution layer passes its plumbed configuration value here directly;
        /// the parameterless overload passes the process-wide <see cref="CertificateSubjectNameOverride"/> (cache layer).
        /// </remarks>
        public static string? TryGetCertificateSubjectName(string? certificateSubjectNameOverride) =>
            certificateSubjectNameOverride
            ?? EngineEnvironmentSettings.GrpcCertificateSubjectName.Value
            ?? EngineEnvironmentSettings.CBBuildUserCertificateName.Value;

        /// <summary>
        /// Resolves the certificate subject name using the process-wide <see cref="CertificateSubjectNameOverride"/> as
        /// the override. Intended for the cache layer, which cannot plumb the configuration through its many layers.
        /// </summary>
        public static string? TryGetCertificateSubjectName() =>
            TryGetCertificateSubjectName(CertificateSubjectNameOverride);

        /// <summary>
        /// Whether gRPC encryption is enabled and a certificate subject name is available to implement it.
        /// </summary>
        /// <remarks>
        /// The explicit <paramref name="encryptionEnabledOverride"/> (from the command line) takes precedence over the
        /// environment variable; when null the environment variable (default enabled) is used. This is the single place
        /// where the fallback to the environment variable is resolved. The engine distribution layer passes its plumbed
        /// configuration values here directly; the parameterless overload passes the process-wide override properties
        /// (cache layer).
        /// </remarks>
        public static bool IsEncryptionEnabled(bool? encryptionEnabledOverride, string? certificateSubjectNameOverride) =>
            (encryptionEnabledOverride ?? EngineEnvironmentSettings.GrpcEncryptionEnabled)
            && TryGetCertificateSubjectName(certificateSubjectNameOverride) != null;

        /// <summary>
        /// Whether gRPC encryption is enabled, using the process-wide override properties. Intended for the cache layer,
        /// which cannot plumb the configuration through its many layers.
        /// </summary>
        public static bool IsEncryptionEnabled() =>
            IsEncryptionEnabled(EncryptionEnabledOverride, CertificateSubjectNameOverride);

        /// <summary>
        /// Gets channel encryption options used by gRPC.NET implementation.
        /// </summary>
        public static ChannelEncryptionOptions GetChannelEncryptionOptions()
        {
            // TODO(seokur): After the service changes are rolled-out, we will only read GrpcCertificateSubjectName and GrpcAuthorizationTokenFile.
            var encryptionCertificateName = TryGetCertificateSubjectName();
            var authorizationTokenFile = EngineEnvironmentSettings.GrpcAuthorizationTokenFile.Value ?? EngineEnvironmentSettings.CBBuildIdentityTokenPath.Value;

            if (encryptionCertificateName is null)
            {
                throw new InvalidOperationException($"EncryptionCertificateName is null. Set it via the '/grpcCertificateSubjectName' command-line argument or the '{EngineEnvironmentSettings.GrpcCertificateSubjectName.Name}' environment variable.");
            }

            return new ChannelEncryptionOptions(encryptionCertificateName, authorizationTokenFile);
        }

        /// <summary>
        /// Look up the given certificate subject name in the Windows certificate stores and return the actual certificate.
        /// </summary>
        public static X509Certificate2? TryGetEncryptionCertificate(string certSubjectName, out string error)
        {
            var cert = TryGetEncryptionCertificate(certSubjectName, StoreLocation.CurrentUser, out error);
            if (cert != null || OperatingSystemHelper.IsLinuxOS)
            {
                // For linux, LocalMachine X509Store is limited to the Root and CertificateAuthority stores.
                // We do not need to do the second lookup.
                return cert;
            }

            cert = TryGetEncryptionCertificate(certSubjectName, StoreLocation.LocalMachine, out var secondError);
            error += Environment.NewLine + secondError;
            return cert;
        }

        /// <summary>
        /// Look up the given certificate subject name in the given Windows certificate store and return the actual certificate.
        /// </summary>
        public static X509Certificate2? TryGetEncryptionCertificate(string certSubjectName, StoreLocation storeLocation, out string error)
        {
            error = $"{nameof(TryGetEncryptionCertificate)}: ";
            if (string.IsNullOrWhiteSpace(certSubjectName))
            {
                error += "Certificate Name is Null or empty. ";
                return null;
            }

            using X509Store? store = new X509Store(StoreName.My, storeLocation);

            try
            {
                store.Open(OpenFlags.ReadOnly);
            }
            catch (CryptographicException e)
            {
                // LocalMachine store cannot be opened in some platforms.
                // For example, Unix LocalMachine X509Store is limited to the Root and CertificateAuthority stores.
                error += $"Exception occurred by finding {certSubjectName} in {storeLocation}: {e}. ";
                return null;
            }

            X509Certificate2Collection certificates = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, certSubjectName, false);

            if (certificates.Count < 1)
            {
                error += $"Found zero certificates by {certSubjectName} in {storeLocation}. ";
                return null;
            }

            DateTime now = DateTime.Now;
            foreach (X509Certificate2 certificate in certificates)
            {
                // NotBefore and NotAfter are in local time!
                if (now < certificate.NotBefore)
                {
                    continue;
                }

                if (now > certificate.NotAfter)
                {
                    continue;
                }

                return certificate;
            }

            error += $"{certSubjectName} found in {storeLocation}, but not in valid timespan. ";
            return null;
        }

        /// <summary>
        /// Extract public certificate and private key in PEM format for a given certificate name in the Windows certificate store
        /// </summary>
        public static bool TryGetPublicAndPrivateKeys(
            string certificateSubject,
            out string? publicCertificate,
            out string? privateKey,
            out string? hostName,
            out string? errorMessage)
        {
            publicCertificate = null;
            privateKey = null;
            hostName = null;
            errorMessage = null;

            X509Certificate2? serverCert = TryGetEncryptionCertificate(certificateSubject, out errorMessage);

            if (serverCert == null)
            {
                return false;
            }

            hostName = serverCert.GetNameInfo(X509NameType.DnsName, false);

            publicCertificate = CertToPem(serverCert.RawData);

            var loadedRsa = serverCert.GetRSAPrivateKey();
            byte[]? loadedPrivateKey = null;
            if (loadedRsa is RSACng cng)
            {
                byte[] exportValue = new byte[] { 0x02, 0x00, 0x00, 0x00 }; // 0x02 DWORD in little endian
                cng.Key.SetProperty(new CngProperty("Export Policy", exportValue, CngPropertyOptions.None));

                //ExportPkcs8PrivateKey is not available for .net full framework so we use the following for full framework.
#if !NETCOREAPP
                loadedPrivateKey = cng.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
#endif
            }

            //ExportPkcs8PrivateKey is not available for .net full framework.
#if NETCOREAPP
            loadedPrivateKey = loadedRsa?.ExportPkcs8PrivateKey();
#endif

            if (loadedPrivateKey == null)
            {
                errorMessage = $"The certificate does not contain the private key. Cert.HasPrivateKey: {serverCert.HasPrivateKey}";
                return false;
            }

            privateKey = PrivateKeyToPem(loadedPrivateKey);
            return true;
        }

        /// <summary>
        /// Converts a binary public certificate to PEM format.
        /// </summary>
        private static string CertToPem(byte[] certContents)
        {
            return PemFormatCertContents(certContents, "CERTIFICATE");
        }

        /// <summary>
        /// Converts a binary PKCS#8-formatted private key to PEM format.
        /// </summary>
        private static string PrivateKeyToPem(byte[] certContents)
        {
            return PemFormatCertContents(certContents, "PRIVATE KEY");
        }

        private static string PemFormatCertContents(byte[] certContents, string header)
        {
            return $"-----BEGIN {header}-----" + Environment.NewLine +
                   Convert.ToBase64String(certContents, Base64FormattingOptions.InsertLineBreaks) + Environment.NewLine +
                   $"-----END {header}-----";
        }

        private static StoreLocation? ParseCertificateStoreLocation(string? value)
        {
            StoreLocation phase;
            if (Enum.TryParse(value, ignoreCase: true, result: out phase))
            {
                return phase;
            }

            return null;
        }

        public static Result<KeyCertificatePair> TryGetSecureChannelCredentials(string? encryptionCertificateName, out string? hostName)
        {
            hostName = "localhost";
            try
            {
                if (TryGetPublicAndPrivateKeys(encryptionCertificateName!,
                    out var publicCertificate,
                    out var privateKey,
                    out hostName,
                    out var errorMessage) && publicCertificate != null && privateKey != null)
                {
                    return Result.Success(new KeyCertificatePair(publicCertificate, privateKey));
                }

                return Result.FromErrorMessage<KeyCertificatePair>($"{errorMessage}");
            }
            catch (Exception e)
            {
                return Result.FromException<KeyCertificatePair>(e, "Failed to get Encryption Certificate.");
            }
        }

        /// <summary>
        /// Validate the BuildUser certificate 
        /// </summary>
        public static bool TryValidateCertificate(string certificateChainsPath, X509Chain? chain, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!File.Exists(certificateChainsPath))
            {
                errorMessage += $"File is not found: '{certificateChainsPath}'.";
                return false;
            }

            string jsonContent = File.ReadAllText(certificateChainsPath);
            var cmdSettingsFile = JsonConvert.DeserializeObject<CompliantBuildCmdAgentSettings>(jsonContent);

            if (cmdSettingsFile != null)
            {
                foreach (CertificateChainValidationElement issuerChain in cmdSettingsFile.ValidClientAuthenticationChains)
                {
                    try
                    {
                        // TODO: Validate fails if chain is null. What should we do here? Work item: 1907180
                        if (issuerChain.Validate(chain, false, out errorMessage))
                        {
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMessage += ex;
                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Return the decrypted contents of the build identity token in the given location
        /// </summary>
        public static string? TryGetAuthorizationToken(string? authorizationTokenFile)
        {
            if (!string.IsNullOrEmpty(authorizationTokenFile) && File.Exists(authorizationTokenFile))
            {
#if NETCOREAPP
                var bytes = File.ReadAllBytes(authorizationTokenFile);
                byte[] clearText = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
                var fullToken = Encoding.UTF8.GetString(clearText);
                // Only the first part of the token matches between machines in the same build.
                return fullToken.Split('.')[0];
#endif
            }

            return null;
        }
    }
}
