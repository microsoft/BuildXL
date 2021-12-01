// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBOMApi.Contracts;
using Microsoft.VisualStudio.Services.Governance.ComponentDetection.BcdeModels;
using Newtonsoft.Json;

namespace SBOMConverter
{
    /// <summary>
    /// Converts from Component Detection BCDE to SPDX component format.
    /// </summary>
    public static class ComponentDetectionConverter
    {
        /// <summary>
        /// Converts from <see cref="Microsoft.VisualStudio.Services.Governance.ComponentDetection.TypedComponent"/> to <see cref="SBOMPackage"/>
        /// by reading a bcde-output.json file produced from the component detection library.
        /// </summary>
        /// <param name="bcdeOutputPath">Path to bcde-output.json file.</param>
        /// <param name="logger">Logger to log any potential warnings during conversion.</param>
        /// <param name="packages">List of <see cref="SBOMPackage"/> objects to be returned.</param>
        /// <returns>Returns false if package conversion was unsuccessful or only partially successful.</returns>
        public static bool TryConvert(string bcdeOutputPath, Action<string> logger, out IEnumerable<SBOMPackage> packages)
        {
            var result = true;
            packages = null;

            try
            {
                var json = File.ReadAllText(bcdeOutputPath);
                result = ConvertInternal(json, logger, out var generatedPackages);
                packages = generatedPackages;
            }
            catch (Exception ex)
            {
                Log(logger, $"Unable to parse bcde-output.json at path '{bcdeOutputPath}' due to the following exception: {ex}");
                result = false;
            }

            return result;
        }

        /// <summary>
        /// Same method as <see cref="TryConvert(string, Action{string}, out IEnumerable{SBOMPackage})"/>, but accepts a json string directly instead of a path to read for unit testing.
        /// </summary>
        public static bool TryConvertForUnitTest(string json, Action<string> logger, out IEnumerable<SBOMPackage> packages)
        {
            var result = true;
            packages = null;

            try
            {
                result = ConvertInternal(json, logger, out var generatedPackages);
                packages = generatedPackages;
            }
            catch (Exception ex)
            {
                Log(logger, $"Unable to parse bcde-output.json due to the following exception: {ex}");
                result = false;
            }

            return result;
        }

        private static bool ConvertInternal(string json, Action<string> logger, out IEnumerable<SBOMPackage> packages)
        {
            packages = null;
            var result = true;
            var componentDetectionScanResult = JsonConvert.DeserializeObject<ScanResult>(json);

            if (componentDetectionScanResult == null)
            {
                Log(logger, $"Parsing bcde-output.json returns null.");
                result = false;
            }
            else if (componentDetectionScanResult.ComponentsFound != null)
            {
                packages = componentDetectionScanResult.ComponentsFound.ToList()
                    .ConvertAll(component =>
                    {
                        if (SBOMPackageGenerator.TryConvertTypedComponent(component.Component, logger, out var package))
                        {
                            return package;
                        }
                        else
                        {
                                // A warning should already be logged here.
                                result = false;
                        }
                        return null;
                    })
                    // It is acceptable to return a partial list of values with null filtered out since they should be reported as failures already
                    .Where(package => package != null)
                    .Select(package => package!);
            }

            return result;
        }

        internal static void Log(Action<string> logger, string message)
        {
            logger($"[SBOMConverter.ComponentDetectionConverter] {message}");
        }
    }
}
