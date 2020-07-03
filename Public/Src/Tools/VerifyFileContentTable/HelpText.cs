// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.ToolSupport;

namespace Tool.VerifyFileContentTable
{
    internal static class HelpText
    {
        public static void DisplayLogo()
        {
            var hw = new HelpWriter();
            hw.WriteLine(Resources.Help_Logo_VersionInfo);
            hw.WriteLine(Resources.Help_Logo_Copyright);
            hw.WriteLine();
        }

        public static void DisplayHelp()
        {
            var hw = new HelpWriter();

            hw.WriteLine(Resources.Help_DisplayHelp_Usage);
            hw.WriteOption("/help", Resources.Help_DisplayHelp_Help);
            hw.WriteOption("/noLogo", Resources.Help_DisplayHelp_Usage);
        }
    }
}
