// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <nodoc />
    public class GetBlobResult : BoolResult
    {
        /// <nodoc />
        private readonly ShortHash? _hash;

        /// <nodoc />
        public byte[]? Blob { get; }

        /// <nodoc />
        public GetBlobResult(ShortHash? hash, byte[]? blob)
            : base(succeeded: true)
        {
            _hash = hash;
            Blob = blob;
        }

        /// <nodoc />
        public GetBlobResult(string errorMessage, string? diagnostics = null, ShortHash? hash = null)
            : base(errorMessage, diagnostics)
        {
            _hash = hash;
        }

        /// <nodoc />
        public GetBlobResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <nodoc />
        public GetBlobResult(ResultBase other, string message, ShortHash hash)
            : base(other, message)
        {
            _hash = hash;
        }

        /// <nodoc />
        public GetBlobResult(ResultBase other, ShortHash hash)
            : base(other)
        {
            _hash = hash;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (Succeeded)
            {
                return $"Hash=[{_hash.ToString() ?? "Unknown"}] Size=[{Blob?.Length ?? -1}]";
            }

            return $"Hash=[{_hash.ToString() ?? "Unknown"}]. Error=[{ErrorMessage}]";
        }
    }
}
