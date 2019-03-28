// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;

namespace Tool.ArbitraryFileGenerator
{
    /// <summary>
    /// Simple process used by TestProjectGenerator-generated projects to product arbitrarily large outputs.
    /// </summary>
    public class Program
    {
        /// <param name="args">
        /// args[0]: Number of bytes to write.
        /// args[1]: Header string to ensure uniqueness of the relatively-empty content file.
        /// args[2]: Output file path.
        /// </param>
        public static void Main(string[] args)
        {
            Contract.Requires(args.Length == 3);

            string pathToWrite = args[2];
            string header = args[1];
            long bytes;
            if (long.TryParse(args[0], out bytes))
            {
                File.WriteAllText(pathToWrite, header);

                using (var fileStream = new FileStream(pathToWrite, FileMode.Create))
                {
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                    fileStream.Seek(bytes, SeekOrigin.Begin);
                    fileStream.Write(headerBytes, 0, headerBytes.Length);
                }
            }
            else
            {
                throw new ArgumentException(string.Format("Unable to parse first argument '{0}', which should be the number of bytes.", args[0]));
            }
        }
    }
}
