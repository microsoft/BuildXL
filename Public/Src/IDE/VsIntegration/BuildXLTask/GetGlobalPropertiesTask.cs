// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BuildXL.IDE.BuildXLTask
{
    /// <summary>
    /// Gets the list of global properties from all projects in the solution to pass the output
    /// as RemoveProperties to the project file that launches the BuildXL server.
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public class GetGlobalPropertiesTask : Task
    {
        /// <summary>
        /// Global properties associated with this project configuration
        /// </summary>
        [Output]
        public string[] GlobalProperties { get; set; }

        /// <summary>
        /// Optional list of properties (separated by ;) that need to be ignored while returning the set of global properties
        /// </summary>
        public string[] IgnoreProperties { get; set; }

        /// <summary>
        /// Main function of the task
        /// </summary>
        /// <returns>Returns the status of the task execution</returns>
        public override bool Execute()
        {
            try
            {
                // Use reflection to get the global properties dictionary object
                var propertyDictionary = GetPropertyValue(BuildEngine4, "requestEntry.RequestConfiguration.Project.GlobalPropertiesDictionary");
                if (propertyDictionary == null)
                {
                    Log.LogError(
                        string.Format(CultureInfo.InvariantCulture, "Could not get instance of '{0}' to get global properties",
                        "requestEntry.RequestConfiguration.Project.GlobalPropertiesDictionary"));
                    return false;
                }

                var typecastedDictionary = propertyDictionary as IDictionary<string, ProjectPropertyInstance>;
                if (typecastedDictionary == null)
                {
                    Log.LogError(
                       string.Format(CultureInfo.InvariantCulture, "Instance of '{0}' does not match with the type '{1}'",
                       "requestEntry.RequestConfiguration.Project.GlobalPropertiesDictionary", "IDictionary<string, ProjectPropertyInstance>"));
                    return false;
                }

                // CreateSourceFile a set of ignored properties
                HashSet<string> ignoredPropSet = null;
                if (IgnoreProperties != null && IgnoreProperties.Length > 0)
                {
                    ignoredPropSet = new HashSet<string>(
                        IgnoreProperties,
                        StringComparer.OrdinalIgnoreCase);
                }

                // Build the output string
                GlobalProperties = typecastedDictionary.Keys.Where(
                    value => ignoredPropSet == null || !ignoredPropSet.Contains(value)).ToArray();
                return true;
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex);
                throw;
            }
        }

        /// <summary>
        /// Gets the value of requested property
        /// </summary>
        private static object GetPropertyValue(object obj, string name)
        {
            foreach (var part in name.Split('.'))
            {
                if (obj == null)
                {
                    return null;
                }

                // Dealing with properties including non-public ones
                var type = obj.GetType();
                var pinfo = type.GetProperty(part, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Default);
                if (pinfo != null)
                {
                    obj = pinfo.GetValue(obj, null);
                    continue;
                }

                // Dealing with fields including non-public ones
                var finfo = type.GetField(part, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Default);
                if (finfo != null)
                {
                    obj = finfo.GetValue(obj);
                    continue;
                }

                // Dealing with renamed private fields in MsBuild14+
                finfo = type.GetField("_" + part, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Default);
                if (finfo != null)
                {
                    obj = finfo.GetValue(obj);
                    continue;
                }

                return null;
            }

            return obj;
        }
    }
}
