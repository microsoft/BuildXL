// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Result of the Calibrate call.
    /// </summary>
    public class CalibrateResult : Result<long>
    {
        /// <summary>
        ///     Calibrate result indicating quota cannot be calibrated.
        /// </summary>
        public static readonly CalibrateResult CannotCalibrate = new CalibrateResult("Quota cannot be calibrated");

        /// <summary>
        ///     Initializes a new instance of the <see cref="CalibrateResult"/> class.
        /// </summary>
        public CalibrateResult(long size)
            : base(size)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CalibrateResult"/> class.
        /// </summary>
        public CalibrateResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <nodoc />
        public CalibrateResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <summary>
        ///     Gets size.
        /// </summary>
        public long Size => Value;

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"New hard limit=[{Size}]"
                : GetErrorString();
        }
    }
}
