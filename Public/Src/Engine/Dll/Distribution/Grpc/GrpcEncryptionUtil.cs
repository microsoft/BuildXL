// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Security.Cryptography;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Utility methods needed to enable encryption and authentication for gRPC-using services in CloudBuild
    /// </summary>
    public static class GrpcEncryptionUtil
    {
        /// <summary>
        /// Look up the given certificate subject name in the Windows certificate store and return the actual certificate.
        /// </summary>
        public static X509Certificate2 TryGetBuildUserCertificate(string certSubjectName)
        {
            if (string.IsNullOrWhiteSpace(certSubjectName))
            {
                return null;
            }

            X509Store store = null;
            try
            {
                store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.OpenExistingOnly);

                X509Certificate2Collection certificates =
                    store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, certSubjectName, false);
                if (certificates.Count < 1)
                {
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
            }
            finally
            {
                store?.Close();
            }

            return null;
        }

        /// <summary>
        /// Return the decrypted contents of the build identity token in the given location
        /// </summary>
        public static string TryGetTokenBuildIdentityToken(string buildIdentityTokenLocation)
        {
            if (File.Exists(buildIdentityTokenLocation))
            {
#if NET_COREAPP_31
                var bytes = File.ReadAllBytes(buildIdentityTokenLocation);
                byte[] clearText = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
                var fullToken = Encoding.UTF8.GetString(clearText);
                // Only the first part of the token matches between machines in the same build.
                return fullToken.Split('.')[0];
#endif
            }

            return null;
        }

        /// <summary>
        /// Extract public certificate and private key in PEM format for a given certificate name in the Windows certificate store
        /// </summary>
        public static bool TryGetPublicAndPrivateKeys(
            string certSubjectName,
            out string publicCertificate,
            out string privateKey,
            out string hostName)
        {
            publicCertificate = null;
            privateKey = null;
            hostName = null;

            X509Certificate2 serverCert = TryGetBuildUserCertificate(certSubjectName);
            if (serverCert == null)
            {
                return false;
            }

            hostName = serverCert.GetNameInfo(X509NameType.DnsName, false);

            publicCertificate = CertToPem(serverCert.RawData);

            var loadedRsa = serverCert.GetRSAPrivateKey();
            if (loadedRsa is RSACng cng)
            {
                byte[] exportValue = new byte[] { 0x02, 0x00, 0x00, 0x00 }; // 0x02 DWORD in little endian
                cng.Key.SetProperty(new CngProperty("Export Policy", exportValue, CngPropertyOptions.None));
            }

            byte[] loadedPrivateKey = null;
#if NET_COREAPP_31
            loadedPrivateKey = loadedRsa.ExportPkcs8PrivateKey();
#endif

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

        /// <summary>
        /// Validate the BuildUser certificate 
        /// </summary>
        public static bool ValidateBuildUserCertificate(
            object request,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // WILL BE IMPLEMENTED IN CLOUDBUILD CODEBASE
            return true;
        }
    }
}
