using System;
using System.Diagnostics;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.SandboxExec
{
    internal static class Telemetry
    {
        internal static void TelemetryStartup(bool enableRemoteTelemetry)
        {
            if (!Debugger.IsAttached && enableRemoteTelemetry)
            {
                AriaV2StaticState.Enable(global::BuildXL.Tracing.AriaTenantToken.Key);
            }
        }

        internal static void TelemetryShutdown()
        {
            if (AriaV2StaticState.IsEnabled)
            {
                AriaV2StaticState.TryShutDown(TimeSpan.FromSeconds(10), out _);
            }
        }
    }
}
