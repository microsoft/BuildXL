// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;

namespace BuildXL.Utilities.VmCommandProxy
{
    /// <summary>
    /// Utilities for low privilege account.
    /// </summary>
    public static class LowPrivilegeAccountUtils
    {
        private static SecureString ConvertToSecureString([NotNull] this string text)
        {
            Contract.Requires(text != null);
            unsafe
            {
                fixed (char* textChars = text)
                {
                    SecureString secure = null;

                    try
                    {
                        secure = new SecureString(textChars, text.Length);
                        secure.MakeReadOnly();
                        return secure;
                    }
                    catch (Exception)
                    {
                        if (secure != null)
                        {
                            secure.Dispose();
                        }

                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Converts an instance of <see cref="SecureString"/> to an instance of <see cref="string"/>.
        /// </summary>
        public static string GetUnsecuredString(this SecureString secured)
        {
            if (secured == null)
            {
                return null;
            }

            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secured);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }

        /// <summary>
        /// Gets the low privilege build account from environment variable.
        /// </summary>
        public static string GetLowPrivilegeBuildAccount()
        {
            return Environment.GetEnvironmentVariable("LowPrivilegeBuildAccount");
        }

        /// <summary>
        /// Gets the low privilege build password from environment variable and decrypt it.
        /// </summary>
        public static SecureString GetLowPrivilegeBuildPassword()
        {
            var encryptedSecret = Environment.GetEnvironmentVariable("LowPrivilegeBuildPassword");

            if (string.IsNullOrEmpty(encryptedSecret))
            {
                return null;
            }
            else
            {
                byte[] clearText = ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedSecret),
                    null,
                    DataProtectionScope.LocalMachine);

                return ConvertToSecureString(Encoding.UTF8.GetString(clearText));
            }
        }              
    }
}
