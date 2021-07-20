// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Return type for IFileCopier{T} />
    /// </summary>
    /// <remarks>
    /// For file copies, a error represents the source file being missing or unavailable. This is opaque to any file system
    /// and could be representing scenarios where the file is missing, or the machine is down or the network is unreachable etc.
    /// </remarks>
    public class CopyFileResult : ResultBase, ICopyResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        private CopyFileResult(CopyResultCode code)
        {
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, string message, string? diagnostics = null)
            : base(Error.FromErrorMessage(message, diagnostics))
        {
            Contract.Requires(code != CopyResultCode.Success);
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, Error error)
            : base(error)
        {
            Contract.Requires(code != CopyResultCode.Success);
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, Exception innerException, string? message = null)
            : base(innerException, message)
        {
            Contract.Requires(code != CopyResultCode.Success);
            Code = code;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFileResult"/> class.
        /// </summary>
        public CopyFileResult(CopyResultCode code, ResultBase other, string? message = null)
            : base(other, message)
        {
            Contract.Requires(code != CopyResultCode.Success);
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
        public override Error? Error
        {
            get
            {
                return Code == CopyResultCode.Success
                    ? null
                    : (base.Error ?? Error.FromErrorMessage(Code.ToString()));
            }
        }

        /// <summary>
        ///     Success singleton.
        /// </summary>
        public static readonly CopyFileResult Success = new CopyFileResult(CopyResultCode.Success);

        /// <summary>
        /// Successful copy with the actual size of the copied file.
        /// </summary>
        /// <param name="size">Actual size of the copied file.</param>
        public static CopyFileResult SuccessWithSize(long size) => new CopyFileResult(CopyResultCode.Success) { Size = size };

        /// <nodoc />
        public static CopyFileResult FromResultCode(CopyResultCode code)
        {
            Contract.Requires(code != CopyResultCode.Success);
            return new CopyFileResult(code);
        }

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

        /// <nodoc />
        public TimeSpan? HeaderResponseTime { get; set; }

        /// <summary>
        /// Implicit conversion operator from <see cref="CopyFileResult"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(CopyFileResult result) => result.Succeeded;

        /// <nodoc />
        protected override bool SuccessEquals(ResultBase other)
        {
            return Code == ((CopyFileResult)other).Code;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (base.GetHashCode(), Code).GetHashCode();
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => Code.ToString();

        /// <inheritdoc />
        protected override string GetErrorString() => $"{Code} {base.GetErrorString()}";
    }
}
