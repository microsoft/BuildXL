// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Return type for IFileCopier{T} />
    /// </summary>
    /// <remarks>
    /// For file copies, a error represents the source file being missing or unavailable. This is opaque to any file system
    /// and could be representing scenarios where the file is missing, or the machine is down or the network is unreachable etc.
    /// </remarks>
    public class CopyFileResult : ResultBase, IEquatable<CopyFileResult>
    {
        /// <summary>
        ///     Success singleton.
        /// </summary>
        public static readonly CopyFileResult Success = new CopyFileResult();

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        /// <param name="code">Whether the exception came from a remote or local path.</param>
        public CopyFileResult(CopyResultCode code = CopyResultCode.Success)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, string message, string? diagnostics = null)
            : base(message, diagnostics)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, Exception innerException, string? message = null)
            : base(innerException, message)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, ResultBase other, string? message = null)
            : base(other, message)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(ResultBase other, string? message = null)
            : this(CopyResultCode.Unknown, other, message)
        {
        }

        /// <inheritdoc />
        public override bool Succeeded => Code == CopyResultCode.Success;

        /// <summary>
        /// Successful copy with the actual size of the copied file.
        /// </summary>
        /// <param name="size">Actual size of the copied file.</param>
        public static CopyFileResult SuccessWithSize(long size) => new CopyFileResult(CopyResultCode.Success) { Size = size };

        /// <summary>
        /// Optional size of the copied file.
        /// </summary>
        public long? Size { get; private set; }

        /// <summary>
        /// Optional timespan describing the time spent hashing.
        /// </summary>
        public TimeSpan? TimeSpentHashing { get; set; }

        /// <summary>
        /// Optional timespan describing the time spent writing file to disk.
        /// </summary>
        public TimeSpan? TimeSpentWritingToDisk { get; set; }

        /// <summary>
        /// Gets the source of the exception for the file copy (whether it was local or remote).
        /// </summary>
        public readonly CopyResultCode Code;

        /// <summary>
        /// Optional byte array with the bytes that were copied during a trusted copy.
        /// </summary>
        public byte[]? BytesFromTrustedCopy { get; set; }

        /// <summary>
        /// Minimum bandwidth speed for a copy operation in MbPerSec
        /// </summary>
        public double? MinimumSpeedInMbPerSec { get; set; }

        /// <summary>
        /// Implicit conversion operator from <see cref="CopyFileResult"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(CopyFileResult result) => result.Succeeded;

        /// <inheritdoc />
        public bool Equals(CopyFileResult other)
        {
            return EqualsBase(other) && other != null && Code == other.Code;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is CopyFileResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Code.GetHashCode() ^ (ErrorMessage?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            switch (Code)
            {
                case CopyResultCode.Success:
                    return $"{Code}";
                default:
                    return $"{Code} {GetErrorString()}";
            }
        }
    }
}
