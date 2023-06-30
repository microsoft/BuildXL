// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Contains OS helpers.
 *
 * One purpose of this OS helpers is to hide the use of the Context object, which is one of the recommendations
 * from customers who want to onboard to DScript. Ideally, these helpers should be moved to a common location
 * because it is independent of the workflow SDK. However, for now, it is here to make the SDK self-contained, hence
 * for quick turn around.
 * 
 * TODO: Move this to a common location, or common SDK.
 */
namespace OS
{
    /** True if the OS is Windows. */
    @@public
    export const isWindows = Context.isWindowsOS();

    /** True if the OS is Linux. */
    @@public
    export const isLinux = Context.getCurrentHost().os === "unix";
}