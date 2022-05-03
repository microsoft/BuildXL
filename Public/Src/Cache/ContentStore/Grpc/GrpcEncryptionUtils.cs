// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Diagnostics.ContractsLight;
using Grpc.Core;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Utility methods needed to enable encryption and authentication for gRPC-using services in CloudBuild
    /// Duplicated from BXL.Engine, avoiding adding a commong dependency between BXL Engine and Cache.
    /// Also, this should work with fullframework as well.
    /// </summary>
    public static class GrpcEncryptionUtils
    {
        /// <summary>
        /// Look up the given certificate subject name in the Windows certificate store and return the actual certificate.
        /// </summary>
        public static X509Certificate2? TryGetEncryptionCertificate(string certSubjectName, out string error)
        {
            error = $"{nameof(TryGetEncryptionCertificate)}: ";
            if (string.IsNullOrWhiteSpace(certSubjectName))
            {
                error += "Certificate Name is Null or empty. ";
                return null;
            }

            using X509Store? store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly);

            X509Certificate2Collection certificates = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, certSubjectName, false);

            if (certificates.Count < 1)
            {
                error += $"Found Zero certificates by Certificate Name: {certSubjectName}";
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

            error += "Certificate not in valid timespan. ";


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

            Contract.Assert(loadedPrivateKey is not null, "loadedPrivateKey variable should be populated at this point.");

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
    }
}
