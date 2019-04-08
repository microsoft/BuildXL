// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BuildXL.VsPackage.Resources;
using EnvDTE;
using Microsoft.VisualStudio;

namespace BuildXL.VsPackage
{
    /// <summary>
    /// Includes methods for manipulating BuildXL specification files
    /// </summary>
    public static class SpecUtilities
    {
        /// <summary>
        /// Name of XML element Sources in BuildXL specification file
        /// </summary>
        public const string SourcesElement = "Sources";

        /// <summary>
        /// Name of XML element Item in BuildXL specification file
        /// </summary>
        public const string ItemElement = "Item";

        /// <summary>
        /// Name of XML element Name in BuildXL specification file
        /// </summary>
        public const string NameAttribute = "Name";

        /// <summary>
        /// Name of Output XML element Name in BuildXL specification file
        /// </summary>
        public const string OutputAttribute = "Output";

        /// <summary>
        /// Name of XML element References in BuildXL specification file
        /// </summary>
        public const string ReferencesElement = "References";

        /// <summary>
        /// Name of XML element AppConfig in BuildXL specification file
        /// </summary>
        public const string AppConfigElement = "AppConfig";

        /// <summary>
        /// Name of XML element EmbeddedResources in BuildXL specification file
        /// </summary>
        public const string EmbeddedResourcesElement = "EmbeddedResources";

        /// <summary>
        /// Additional dependences element
        /// </summary>
        public const string AdditionalDependenciesElement = "AdditionalDependencies";

        /// <summary>
        /// ResX element
        /// </summary>
        public const string ResXElement = "ResX";

        /// <summary>
        /// Linked content element
        /// </summary>
        public const string LinkedContent = "LinkedContent";

        /// <summary>
        /// Stores the predefined value for physical files
        /// </summary>
        internal static readonly string PhysicalFileKind = "{" + VSConstants.GUID_ItemType_PhysicalFile + "}";

        /// <summary>
        /// Stores the predefined value for physical folders
        /// </summary>
        internal static readonly string PhysicalFolderKind = "{" + VSConstants.GUID_ItemType_PhysicalFolder + "}";

        private static readonly HashSet<string> s_allowedFileExtensions = new HashSet<string>(
            new[]
            {
                Constants.CPP,
                Constants.CSharp,
                Constants.Resource,
                Constants.DominoSpec,
                Constants.Config,
                Constants.Lib,
                Constants.Png,
                Constants.Js,
                Constants.Css,
                Constants.Htm,
                Constants.Html,
                Constants.Ico,
            },
            StringComparer.OrdinalIgnoreCase);

        internal static string GenerateSpecFilter(string specFile)
        {
            return $"spec='Mount[SourceRoot]\\{specFile}'";
        }

        /// <summary>
        /// Gets BuildXL specification file from an MSBuild Project along with the value name corresponding to the project
        /// </summary>
        /// <param name="project">The MSBuild project file name</param>
        /// <param name="specFile">Full path of the BuildXL specification file</param>
        /// <param name="valueName">Name of the value corresponding to this project</param>
        /// <returns>true if it finds the specification file and the value name name</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "DominoSpecFile")]
        internal static bool TryGetSpecFileFromProject(string project, out string specFile, out string valueName)
        {
            Contract.Assume(project != null);
            specFile = null;
            valueName = null;

            return false;
        }

        /// <summary>
        /// Loads specification file and also identifies the root element. There should be only one root element
        /// and should match with "CSharpAssemblyBuilder", "CSharpUnitTestBuilder", or "CSharpLogicBuilder"
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">Name of the interested value</param>
        /// <param name="mainElement">The core element corresponding to given value name</param>
        /// <param name="defaultNamespace">The default namespace of the root element</param>
        /// <returns>true if successfully loaded the mainElement and the root element</returns>
        private static bool TryLoadSpecFile(string specFile, string valueName, out XContainer mainElement, out XNamespace defaultNamespace)
        {
            var document = XDocument.Load(specFile);
            if (document == null || document.Root == null)
            {
                throw new BuildXLVsException(string.Format(CultureInfo.InvariantCulture, "Failed to load main element from specification file: '{0}'", specFile));
            }

            defaultNamespace = document.Root.GetDefaultNamespace();
            mainElement = GetElementByAttribute(document.Root, defaultNamespace, OutputAttribute, valueName, null);
            if (mainElement == null)
            {
                throw new BuildXLVsException(string.Format(CultureInfo.InvariantCulture, "Could not find valueName '{0}' in the specification file '{1}'", valueName, specFile));
            }

            return true;
        }

        /// <summary>
        /// Writes the specification file from mainElement
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="document">Document object</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        internal static void WriteSpecFile(string specFile, XDocument document)
        {
            using (var fileStream = new FileStream(specFile, FileMode.Create))
            {
                using (var writer = new StreamWriter(fileStream))
                {
                    writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                    writer.Write(document);
                    writer.WriteLine(string.Empty);
                }
            }
        }

        #region source files

        /// <summary>
        /// Adds a source file to BuildXL specification file
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="sourceFileToAdd">Source file that needs to be added</param>
        public static void AddSourceElement(string specFile, string valueName, string sourceFileToAdd)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, ItemElement, sourceFileToAdd, SourcesElement);
            if (element != null)
            {
                // Element already exists, just ignore with the addition
                return;
            }

            // Add the element to the XDocument
            var srcElement = GetOrCreateElement(mainElement, defaultNamespace, SourcesElement, null);
            srcElement.Add(new XElement(defaultNamespace + ItemElement, sourceFileToAdd));
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Removes a source file from BuildXL specification file
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="sourceFileToRemove">Source file that needs to be removed</param>
        public static void RemoveSourceElement(string specFile, string valueName, string sourceFileToRemove)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, ItemElement, sourceFileToRemove, SourcesElement);

            // Element does not exist in the specification file
            if (element == null)
            {
                Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Element '{0}' not found in the specification file", sourceFileToRemove));
                return;
            }

            element.Remove();
            WriteSpecFile(specFile, mainElement.Document);
        }

        #endregion source files

        #region embedded resources

        /// <summary>
        /// Adds an embedded resource
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="resourceToAdd">Resource file that needs to be added</param>
        public static void AddEmbeddedResource(string specFile, string valueName, string resourceToAdd)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, ResXElement, resourceToAdd, EmbeddedResourcesElement);
            if (element != null)
            {
                // Element already exists, just ignore with the addition
                return;
            }

            // Add the element to the XDocument
            var resourcesRoot = GetOrCreateElement(mainElement, defaultNamespace, EmbeddedResourcesElement, null);
            resourcesRoot.Add(
                new XElement(
                    defaultNamespace + ItemElement,
                    new XElement(defaultNamespace + ResXElement, resourceToAdd)));
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Adds an embedded resource with a linked content
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="resourceToAdd">Resource file that needs to be added</param>
        public static void AddEmbeddedResourceLinkedContent(string specFile, string valueName, string resourceToAdd)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, LinkedContent, resourceToAdd, EmbeddedResourcesElement);
            if (element != null)
            {
                // Element already exists, just ignore with the addition
                return;
            }

            // Add the element to the XDocument
            var resourcesRoot = GetOrCreateElement(mainElement, defaultNamespace, EmbeddedResourcesElement, null);
            resourcesRoot.Add(
                new XElement(
                    defaultNamespace + ItemElement,
                    new XElement(
                        defaultNamespace + LinkedContent,
                        new XElement(
                            defaultNamespace + ItemElement, resourceToAdd))));
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Removes an embedded resource file from the specification
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="resourceToRemove">Resource file that needs to be removed</param>
        public static void RemoveEmbeddedResource(string specFile, string valueName, string resourceToRemove)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, ResXElement, resourceToRemove, EmbeddedResourcesElement);
            if (element == null)
            {
                // Element does not exist, just ignore with the removal
                return;
            }

            // Remove the element from its parent
            Contract.Assume(element.Parent != null);
            var parentItemElement = element.Parent.Parent;
            element.Parent.Remove();

            // Remove empty parent item as well
            if (parentItemElement != null && !parentItemElement.HasElements)
            {
                parentItemElement.Remove();
            }

            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Removes an embedded resource file from the specification
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="resourceToRemove">Resource file that needs to be removed</param>
        public static void RemoveEmbeddedResourceLinkedContent(string specFile, string valueName, string resourceToRemove)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, LinkedContent, resourceToRemove, EmbeddedResourcesElement);
            if (element == null)
            {
                // Element does not exist, just ignore with the removal
                return;
            }

            // Remove the element from its parent
            Contract.Assume(element.Parent != null);
            var parentItemElement = element.Parent.Parent;
            element.Parent.Remove();

            // Remove empty parent item as well
            if (parentItemElement != null && !parentItemElement.HasElements)
            {
                parentItemElement.Remove();
            }

            WriteSpecFile(specFile, mainElement.Document);
        }
        #endregion embeded resources

        /// <summary>
        /// Adds config file to the specification
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="configFile">Config file that needs to be added</param>
        public static void AddAppConfig(string specFile, string valueName, string configFile)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, AppConfigElement, configFile, null);
            if (element != null)
            {
                // Element already exists, just ignore with the addition
                return;
            }

            // Add the element to the XDocument
            mainElement.Add(new XElement(defaultNamespace + AppConfigElement, configFile));
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Removes a config file from the specification
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="configFile">Config file that needs to be removed</param>
        public static void RemoveAppConfig(string specFile, string valueName, string configFile)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            XContainer element = GetElementByValue(mainElement, defaultNamespace, AppConfigElement, configFile, null);
            if (element == null)
            {
                // Element does not exist in the file
                return;
            }

            element.Remove();
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Adds a reference to the specification file
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="name">Refernce to be added</param>
        public static void AddCSharpReferenceElement(string specFile, string valueName, string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            // Check whether element already exists
            XContainer element = GetElementByValue(mainElement, defaultNamespace, ItemElement, name, ReferencesElement);
            if (element != null)
            {
                return;
            }

            element = GetOrCreateElement(mainElement, defaultNamespace, ReferencesElement, null);
            element.Add(new XElement(defaultNamespace + ItemElement, name));
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Removes a reference element from the specification file
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="name">Reference to be removed</param>
        public static void RemoveCSharpReferenceElement(string specFile, string valueName, string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            var element = GetElementByValue(mainElement, defaultNamespace, ItemElement, name, ReferencesElement);

            // Element does not exist in the specification file
            if (element == null)
            {
                Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Reference '{0}' not found in the specification file", name));
                return;
            }

            element.Remove();
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Adds a reference to the specification file for CPP
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="name">Refernce to be added</param>
        public static void AddCppReferenceElement(string specFile, string valueName, string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            // Check whether element already exists
            XContainer element = GetElementByValue(mainElement, defaultNamespace, ItemElement, name, AdditionalDependenciesElement);
            if (element != null)
            {
                return;
            }

            element = GetOrCreateElement(mainElement, defaultNamespace, AdditionalDependenciesElement, null);
            element.Add(new XElement(defaultNamespace + ItemElement, name));
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Removes a reference element from the specification file for CPP
        /// </summary>
        /// <param name="specFile">Full path of the specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <param name="name">Reference to be removed</param>
        public static void RemoveCppReferenceElement(string specFile, string valueName, string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(specFile));
            Contract.Requires(!string.IsNullOrEmpty(valueName));

            XContainer mainElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out mainElement, out defaultNamespace))
            {
                return;
            }

            var element = GetElementByValue(mainElement, defaultNamespace, ItemElement, name, AdditionalDependenciesElement);

            // Element does not exist in the specification file
            if (element == null)
            {
                Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Reference '{0}' not found in the specification file", name));
                return;
            }

            element.Remove();
            WriteSpecFile(specFile, mainElement.Document);
        }

        /// <summary>
        /// Gets an element in the specification that matches with the given value
        /// </summary>
        /// <param name="mainElement">The mainElement element</param>
        /// <param name="defaultNamespace">The default namespace of the root element</param>
        /// <param name="element">The element that needs to be searched</param>
        /// <param name="value">The value of the element that needs to be searched</param>
        /// <param name="optionalParent">Optional parent element to limit the search</param>
        /// <returns>Either XContainer or null</returns>
        public static XContainer GetElementByValue(XContainer mainElement, XNamespace defaultNamespace, string element, string value, string optionalParent)
        {
            Contract.Requires(mainElement != null);
            Contract.Requires(defaultNamespace != null);
            Contract.Requires(!string.IsNullOrEmpty(element));
            Contract.Requires(!string.IsNullOrEmpty(value));

            if (optionalParent != null)
            {
                return mainElement.Descendants(defaultNamespace + optionalParent)
                    .Descendants(defaultNamespace + element)
                    .FirstOrDefault(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase));
            }

            return mainElement.Descendants(defaultNamespace + element)
                .FirstOrDefault(e => e.Value == value);
        }

        /// <summary>
        /// Gets the element with a given attribute and with the specified value
        /// </summary>
        /// <param name="mainElement">The main element of value name</param>
        /// <param name="defaultNamespace">The default namespace of the root element</param>
        /// <param name="attribute">The name of the attribute</param>
        /// <param name="value">The value being searched</param>
        /// <param name="optionalParent">Optional parent to limit the search</param>
        /// <returns>Returns the XContainer instance</returns>
        public static XContainer GetElementByAttribute(XContainer mainElement, XNamespace defaultNamespace, string attribute, string value, string optionalParent)
        {
            Contract.Requires(mainElement != null);
            Contract.Requires(defaultNamespace != null);
            Contract.Requires(!string.IsNullOrEmpty(attribute));
            Contract.Requires(!string.IsNullOrEmpty(value));

            if (optionalParent != null)
            {
                return mainElement.Descendants(defaultNamespace + optionalParent)
                    .Descendants()
                    .FirstOrDefault(e => string.Equals((string)e.Attribute(attribute), value, StringComparison.OrdinalIgnoreCase));
            }

            return mainElement.Descendants()
                    .FirstOrDefault(e => string.Equals((string)e.Attribute(attribute), value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Retrieves the value specified by elementName. If it does not exist, it automatically
        /// adds the value as well
        /// </summary>
        /// <param name="mainElement">The mainElement element</param>
        /// <param name="defaultNamespace">The default namespace of the root element</param>
        /// <param name="elementName">Element name that need to be searched or created</param>
        /// <param name="parentElement">Parent element under which the element needs to be created (in case required)</param>
        /// <returns>Returns the element</returns>
        internal static XContainer GetOrCreateElement(XContainer mainElement, XNamespace defaultNamespace, string elementName, string parentElement)
        {
            Contract.Assume(mainElement != null);

            var sourcesElement = mainElement.Descendants(defaultNamespace + elementName).FirstOrDefault();
            if (sourcesElement != null)
            {
                return sourcesElement;
            }

            // Create a new source value and return
            XContainer builder = parentElement != null
                ? mainElement.Descendants(defaultNamespace + parentElement).FirstOrDefault()
                : mainElement;

            Contract.Assume(builder != null); // Builder cannot be null
            sourcesElement = new XElement(defaultNamespace + elementName);
            builder.Add(sourcesElement);
            return sourcesElement;
        }

        /// <summary>
        /// Fetches the name of the project
        /// </summary>
        /// <param name="specFile">The specification file</param>
        /// <param name="valueName">The name of the value name</param>
        /// <returns>Name of the project</returns>
        internal static string GetProjectName(string specFile, string valueName)
        {
            XContainer rootElement;
            XNamespace defaultNamespace;
            if (!TryLoadSpecFile(specFile, valueName, out rootElement, out defaultNamespace))
            {
                return null;
            }

            return (string)((XElement)rootElement).Attribute(defaultNamespace + OutputAttribute);
        }

        /// <summary>
        /// Checks whether to consider this project item for BuildXL specification or not
        /// </summary>
        /// <param name="item">Item that needs to be verified</param>
        /// <returns>true if the item type is handled, otherwise returns false</returns>
        internal static bool IsAValidProjectItem(ProjectItem item)
        {
            // Allow all physical folders
            if (string.Equals(PhysicalFolderKind, item.Kind, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Allow files with a certain extensions
            // TODO: Need to discuss what are the other extensions that can be allowed
            if (string.Equals(PhysicalFileKind, item.Kind, StringComparison.OrdinalIgnoreCase))
            {
                // Only few file extensions are allowed currently
                var extension = Path.GetExtension(item.FileNames[0]);
                return s_allowedFileExtensions.Contains(extension);
            }

            return false;
        }

        /// <summary>
        /// Displays message to user
        /// </summary>
        /// <param name="message">Message that needs to be displayed</param>
        [SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions")]
        internal static void ShowMessage(string message)
        {
            using (var dialog = new MessageDialog(message))
            {
                dialog.ShowDialog();
            }
        }

        /// <summary>
        /// Shows exception message to user
        /// </summary>
        /// <param name="message">Message that needs to be displayed</param>
        /// <param name="trace">Stack trace</param>
        [SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions")]
        internal static void ShowException(string message, string trace)
        {
            using (var dialog = new MessageDialog(message, trace))
            {
                dialog.ShowDialog();
            }
        }
    }
}
