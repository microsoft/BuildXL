// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using VSLangProj;

namespace BuildXL.VsPackage
{
    /// <summary>
    /// Manages events related to references such as adding a reference or removing a reference
    /// </summary>
    internal static class ReferencesEventsHandler
    {
        /// <summary>
        /// Gets invoked when a reference is added
        /// </summary>
        /// <param name="reference">Reference that is added</param>
        internal static void ReferenceAdded(Reference reference)
        {
            try
            {
                string specFile, value;

                // If the specification file does not exist, ignore
                if (!TryGetSpecFile(reference, out specFile, out value))
                {
                    return;
                }

                // If failed to infer the name of the reference
                string name = GetReferenceName(reference);
                if (name == null)
                {
                    SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Failed to infer the name of the reference: {0}", reference.Name));
                    return;
                }

                var projectKind = reference.ContainingProject.Kind;
                if (projectKind.Equals(Constants.CsProjGuid, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.AddCSharpReferenceElement(specFile, value, name);
                    return;
                }

                if (projectKind.Equals(Constants.VcxProjGuid, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.AddCppReferenceElement(specFile, value, name);
                    return;
                }

                SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Unknown project type: {0}", reference.ContainingProject.Name));
            }
            catch (BuildXLVsException de)
            {
                SpecUtilities.ShowException(de.Message, de.StackTrace);
            }
            catch (Exception ex)
            {
                // Log exception and rethrow it
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                SpecUtilities.ShowException(ex.Message, ex.StackTrace);
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                throw;
            }
        }

        /// <summary>
        /// Gets invoked when a reference is removed
        /// </summary>
        /// <param name="reference">Reference that is removed</param>
        internal static void ReferenceRemoved(Reference reference)
        {
            try
            {
                string specFile, value;

                // If the specification file does not exist, ignore
                if (!TryGetSpecFile(reference, out specFile, out value))
                {
                    return;
                }

                var name = GetReferenceName(reference);
                if (name == null)
                {
                    SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Failed to infer the name of the reference: {0}", reference.Name));
                    return;
                }

                var projectKind = reference.ContainingProject.Kind;
                if (projectKind.Equals(Constants.CsProjGuid, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.RemoveCSharpReferenceElement(specFile, value, name);
                    return;
                }

                if (projectKind.Equals(Constants.VcxProjGuid, StringComparison.OrdinalIgnoreCase))
                {
                    SpecUtilities.RemoveCppReferenceElement(specFile, value, name);
                    return;
                }

                SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Unknown project type: {0}", reference.ContainingProject.Name));
            }
            catch (BuildXLVsException de)
            {
                SpecUtilities.ShowException(de.Message, de.StackTrace);
            }
            catch (Exception ex)
            {
                // Log exception and rethrow it
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                SpecUtilities.ShowException(ex.Message, ex.StackTrace);
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                throw;
            }
        }

        /// <summary>
        /// Gets the name of the reference
        /// </summary>
        /// <param name="reference">The reference whose name needs to be returned</param>
        /// <returns>Returns the name of the reference</returns>
        private static string GetReferenceName(Reference reference)
        {
            string name;

            // Check whether the reference is made to some of the existing projects
            if (reference.SourceProject != null)
            {
                string otherSpecFile;
                if (!SpecUtilities.TryGetSpecFileFromProject(reference.SourceProject.FileName, out otherSpecFile, out name))
                {
                    // SpecUtilities.ShowMessage(string.Format(CultureInfo.InvariantCulture, "Could not find specification file for the project: {0}", reference.SourceProject.Name));
                    return null;
                }

                // Get the project kind. If its a CPP project, ".Object" needs to be added to the reference name
                if (reference.SourceProject.Kind.Equals(Constants.VcxProjGuid, StringComparison.OrdinalIgnoreCase))
                {
                    name = name + ".Object";
                }
            }
            else
            {
                if (reference.Path != null)
                {
                    if (Path.GetExtension(reference.Path).Equals(Constants.Lib, StringComparison.OrdinalIgnoreCase))
                    {
                        // Full file path is required for libs (TODO: need to confirm)
                        return reference.Path;
                    }

                    name = Path.GetFileName(reference.Path);
                }
                else
                {
                    name = reference.Name;
                    var prjType = (prjOutputType)reference.SourceProject.Properties.Item("OutputType").Value;
                    switch (prjType)
                    {
                        case prjOutputType.prjOutputTypeExe:
                            name += ".exe";
                            break;
                        case prjOutputType.prjOutputTypeLibrary:
                        case prjOutputType.prjOutputTypeWinExe:
                            name += ".dll";
                            break;
                        default:
                            break;
                    }
                }
            }

            return "{" + name + "}";
        }

        /// <summary>
        /// Gets specification file from reference
        /// </summary>
        /// <param name="reference">The reference object</param>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="value">The name of the value in the specification file</param>
        /// <returns>true if successful</returns>
        private static bool TryGetSpecFile(Reference reference, out string specFile, out string value)
        {
            return SpecUtilities.TryGetSpecFileFromProject(reference.ContainingProject.FileName, out specFile, out value);
        }
    }
}
