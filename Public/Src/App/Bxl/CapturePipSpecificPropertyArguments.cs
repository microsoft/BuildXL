// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Pips.Filter;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using Strings = bxl.Strings;

namespace BuildXL
{
    /// <summary>
    /// Functionality to enforce specific properties on specific pips.
    /// </summary>
    public static class CapturePipSpecificPropertyArguments
    {
        /// <summary>
        /// Parse pipProperty argument and map pipIds with the respective pipProperties.
        /// </summary>
        /// <remarks>
        /// /pipProperty:Pip232325435435[PipFingerprintingSalt=TooSalty,ForcedCacheMiss,Debug_EnableVerboseProcessLogging]
        /// </remarks>
        public static void ParsePipPropertyArg(CommandLineUtilities.Option opt, EngineConfiguration engineConfiguration)
        {
            string[] pipPropertyArg = opt.Value.Split(new[] { '[', ']' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (pipPropertyArg.Length != 2)
            {
                throw CommandLineUtilities.Error(Strings.Args_PipProperty_FailedToParsePipProperty);
            }

            // Ensure the parsed string has a valid PipId.
            if (!FilterParser.TryParsePipId(pipPropertyArg[0], out var semistableHash))
            {
                throw CommandLineUtilities.Error(Strings.Args_InvalidPipId, pipPropertyArg[0]);
            }

            // Split the rest of the string for extracting pipProperties.
            string[] pipProperties = pipPropertyArg[1].Split(new[] {','}, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            // Throw an error when pipProperties are not found.
            if (pipProperties.Length < 0)
            {
                throw CommandLineUtilities.Error(Strings.Args_PipProperty_PipSpecificPropertiesNotFound);
            }

            AddPipSpecificProperties(engineConfiguration, pipProperties, semistableHash);
        }

        /// <summary>
        /// Parse pipProperties further for propertyValues and map pipPropertyName's with their respective SemistableHashes and PropertyValues.
        /// </summary>
        public static void AddPipSpecificProperties(EngineConfiguration engineConfiguration, string[] pipProperties, long semistableHash)
        {
            foreach (var pipProperty in pipProperties)
            {
                // PipProperties can be of the format propertyKey=Value. Example: fingerprintSalt=tooSalty.
                // Using propertyKey and propertyValue to obtain that value
                // In such cases propertyKey = fingerprintSalt and propertyValue = tooSalty.
                // If it is not in the form of (K,V) pair, the property value is null.
                // Ex: forcedCacheMiss, has no value hence we just store the property name and the corresponding pipId.
                string propertyValue = null;
                var pipPropertyToBeChecked = pipProperty;

                // The property can contain "=" in this case we need to extract the propertyKey value.
                if (pipProperty.Contains("="))
                {
                    string[] property = pipProperty.Split(new[] { '=' }, StringSplitOptions.TrimEntries)
                                        .ToArray();
                    if (property.Length != 2)
                    {
                        throw CommandLineUtilities.Error(Strings.Args_PipProperty_InvalidProperty, pipProperty);
                    }
                    // Ex: /pipFingerprintingSalt=tooSalty
                    // propertyValue captures the tooSalty and pipPropertyToBeChecked captures propertyName
                    pipPropertyToBeChecked = property[0];
                    propertyValue = property[1];
                }

                // Only the properties listed in the enum are allowed to be passed by the flag.
                if (Enum.TryParse(pipPropertyToBeChecked, true, out PipSpecificPropertiesConfig.PipSpecificProperty propertyFound))
                {
                    // Populate the list of PipPropertyAndValue objects and add them to the PipPropertyAndValues list.
                    var pipPropertyAndValue = new PipSpecificPropertyAndValue(propertyFound, semistableHash, propertyValue);
                    engineConfiguration.PipSpecificPropertyAndValues.Add(pipPropertyAndValue);
                }
                else
                {
                    // throws an error when the property is not in the allowlist. 
                    throw CommandLineUtilities.Error(Strings.Args_PipProperty_InvalidProperty, pipProperty);
                }
            }
        }
    
    }
}
