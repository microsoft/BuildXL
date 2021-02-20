// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using BuildXL.Utilities;
using NLog;
using NLog.Layouts;

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    /// A more optimal version of <see cref="SimpleLayout"/> that uses a pooled <see cref="StringBuilder"/> to avoid excessive allocations from getting messages.
    /// </summary>
    internal class LowAllocationSimpleLayout : SimpleLayout
    {
        // Intentionally using a different pool because we know the max length of the output string and because reusing an existing StringBuilder's pool would introduce unnecessary coupling.
        private static ObjectPool<StringBuilder> StringBuilderPool { get; } = new ObjectPool<StringBuilder>(
            () => new StringBuilder(),
            // Use Func instead of Action to avoid redundant delegate reconstruction.
            sb => { sb.Clear(); return sb; });

        public LowAllocationSimpleLayout(string layout)
            : base(layout)
        {
        }

        /// <inheritdoc />
        protected override string GetFormattedMessage(LogEventInfo logEvent)
        {
            using var wrapper = StringBuilderPool.GetInstance();
            var sb = wrapper.Instance;
            base.RenderFormattedMessage(logEvent, sb);
            var result = sb.ToString();
            return result;
        }
    }
}
