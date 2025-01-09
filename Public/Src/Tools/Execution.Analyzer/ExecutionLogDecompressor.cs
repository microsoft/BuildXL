// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using ZstdSharp;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Decompress the Zstd-compressed execution log file.
    /// </summary>
    public class ExecutionLogDecompressor
    {
        /// <summary>
        /// Create the decompressed execution log file.
        /// </summary>
        public static string GetDecompressedExecutionLogFile(string executionLogFilePath)
        {
            if (executionLogFilePath == null || !File.Exists(executionLogFilePath))
            {
                throw new Exception(ExecutionLogDecompressorConstants.ExecutionLogFilePathErrorMessage);
            }

            // Check if the file is Zstd-compressed.
            // If it is, verify whether a decompressed version already exists.
            // If a decompressed file exists, reuse it; otherwise, proceed with decompression.
            if (IsZstdCompressed(executionLogFilePath))
            {
                return RetrieveOrDecompressExecutionLogFile(executionLogFilePath);
            }

            return executionLogFilePath;
        }

        /// <summary>
        /// Verifies whether the execution log file has been compressed using the Zstd compression stream.
        /// This is determined by checking for the presence of the Zstd magic number at the beginning of the file.
        /// If the magic number is found, it confirms that the file is Zstd-compressed and the file needs to decompressed.
        /// </summary>
        private static bool IsZstdCompressed(string executionLogFilePath)
        {
            try
            {
                using (var fileStream = File.Open(executionLogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] header = new byte[4];
                    int bytesRead = fileStream.Read(header, 0, 4);

                    if (bytesRead == 4 && header[0] == ExecutionLogDecompressorConstants.ZstdMagicNumber[0] &&
                        header[1] == ExecutionLogDecompressorConstants.ZstdMagicNumber[1] &&
                        header[2] == ExecutionLogDecompressorConstants.ZstdMagicNumber[2] &&
                        header[3] == ExecutionLogDecompressorConstants.ZstdMagicNumber[3])
                    {
                        Console.WriteLine(ExecutionLogDecompressorConstants.IsZstdCompressedMessage);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"{ExecutionLogDecompressorConstants.FailedToVerifyIfTheExecutionLogFileIsZstdCompressed} {executionLogFilePath} : {ex}");
            }

            Console.WriteLine($"{executionLogFilePath}{ExecutionLogDecompressorConstants.ExecutionLogFileIsNotZstdCompressedMessage}");
            return false;
        }

        /// <summary>
        /// Checks for the existence of decompressed execution log file.
        /// </summary>
        private static string RetrieveOrDecompressExecutionLogFile(string executionLogFilePath)
        {
            var decompressFilePath = executionLogFilePath.Replace(".xlg", ExecutionLogDecompressorConstants.DecompressedExecutionLogFileSuffixWithExtension);

            // Check for the existence of a file of the format BuildXL_decompressed.xlg.
            // If present we do not proceed with the decompression, we reuse the same file.
            if (File.Exists(decompressFilePath))
            {
                Console.WriteLine($"{ExecutionLogDecompressorConstants.DecompressedExecutionLogFileAlreadyExists}{decompressFilePath}");
                return decompressFilePath;
            }
            return DecompressExecutionLogFile(executionLogFilePath);
        }

        /// <summary>
        /// Decompress file execution log file.
        /// </summary>
        private static string DecompressExecutionLogFile(string compressedFilePath)
        {
            try
            {
                var decompressedFilePath = GetDecompressedFilePath(compressedFilePath);

                using (var compressedFile = File.Open(compressedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var decompressedFile = File.Create(decompressedFilePath))
                using (var decompressionStream = new DecompressionStream(compressedFile, leaveOpen: false))
                {
                    decompressionStream.CopyTo(decompressedFile);
                }

                Console.WriteLine($"{ExecutionLogDecompressorConstants.DecompressedFileSuccessMessage}{decompressedFilePath}");
                return decompressedFilePath;
            }
            catch (Exception ex)
            {
                throw new Exception($"{ExecutionLogDecompressorConstants.FailedToDecompressExecutionFileMessage}", ex);
            }
        }

        /// <summary>
        /// Construct the decompressed execution log file path.
        /// </summary>
        /// <remarks>
        /// Ex: If the compressed file path is /Out/Logs/BuildXL.xlg,
        /// the decompressed file path is /Out/Logs/BuildXL_decompressed.xlg
        /// </remarks>
        private static string GetDecompressedFilePath(string compressedFilePath)
        {
            return Path.Combine(
                    Path.GetDirectoryName(compressedFilePath),
                    $"{Path.GetFileNameWithoutExtension(compressedFilePath)}{ExecutionLogDecompressorConstants.DecompressedExecutionLogFileSuffixWithExtension}");
        }

        /// <summary>
        /// Provides constants for ExecutionLogDecompressor.
        /// </summary>
        public static class ExecutionLogDecompressorConstants
        {
            /// <summary>
            /// The magic number used to identify files compressed with the Zstd compression algorithm.
            /// This sequence of bytes appears at the beginning of a Zstd-compressed file.
            /// </summary>
            public static readonly byte[] ZstdMagicNumber = { 0x28, 0xB5, 0x2F, 0xFD };

            /// <summary>
            /// Suffix for the decompressed execution log file.
            /// </summary>
            public const string DecompressedExecutionLogFileSuffixWithExtension = "_decompressed.xlg";

            /// <nodoc />
            public const string IsZstdCompressedMessage = "The execution log file is Zstd-compressed.";

            /// <nodoc />
            public const string DecompressedExecutionLogFileAlreadyExists = "The decompressed execution log file already exists: ";

            /// <nodoc />
            public const string ExecutionLogFileIsNotZstdCompressedMessage = "- is not Zstd-compressed. Decompression is not required.";

            /// <nodoc />
            public const string ExecutionLogFilePathErrorMessage = "The execution log file path is either null or the file does not exist.";

            /// <nodoc />
            public const string DecompressedFileSuccessMessage = "Successfully created the decompressed execution log file: ";

            /// <nodoc />
            public const string FailedToDecompressExecutionFileMessage = "Failed to create decompressed execution log file: ";

            /// <nodoc />
            public const string FailedToVerifyIfTheExecutionLogFileIsZstdCompressed = "Failed to verify if the execution log file is Zstd compressed";
        }
    }
}