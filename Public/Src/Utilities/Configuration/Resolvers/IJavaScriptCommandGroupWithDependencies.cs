// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The case of <see cref="IJavaScriptCommandGroup"/> when BuildXL provides the script execution semantics (e.g. Yarn, Rush)
    /// </summary>
    public interface IJavaScriptCommandGroupWithDependencies : IJavaScriptCommandGroup, IJavaScriptCommandWithDependencies
    {
    }
}
