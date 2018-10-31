using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace IOSimulator
{
    public static class Hashing
    {
        public static bool HashFileWithPath(string filePath, out string md5Hash, bool verbose = false)
        {
            try
            {
                const FileOptions FileFlagNoBuffering = (FileOptions)0x20000000;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileFlagNoBuffering))
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(stream);
                    md5Hash = HexValue(ref hash);
                }
            }
            catch (Exception ex)
            {
                if (verbose) Console.WriteLine(ex.ToString());
                md5Hash = String.Empty;
                return false;
            }

            // Successfully hashed
            return true;
        }

        /// <summary>
        /// Creates a MD5 hash from a byte array, returns if the hashing was successful and the hexadecimal representation
        /// of the MD5 hash on success
        /// </summary>
        public static bool HashByteArray(ref byte[] bytes, out string md5Hash, bool verbose = false)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(bytes);
                    md5Hash = HexValue(ref hash);
                }
            }
            catch (Exception ex)
            {
                if (verbose) Console.WriteLine(ex.ToString());
                md5Hash = String.Empty;
                return false;
            }

            // Successfully hashed
            return true;
        }

        private static string HexValue(ref byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                result.Append(bytes[i].ToString("X2"));
            }

            return result.ToString();
        }
    }
}