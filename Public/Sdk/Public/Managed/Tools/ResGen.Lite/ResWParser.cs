// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace ResGen.Lite
{
    /// <summary>
    /// Parser class ResW/ResX files
    /// </summary>
    public static class ResWParser
    {
        /// <summary>
        /// Parses a file from disk and 
        /// </summary>
        /// <remarks>
        /// This parser uses a very strict parser to get to a subset of supported full ResX format. The choice was made to explicitly
        /// fail fast rather than handle unknown structures and have unexpected behavior.
        ///
        /// This parser loads the ResW/ResX file into an XDocument. XmlReader would be more efficient but this is a bit easier to read.
        /// </remarks>
        /// 
        public static ResourceData Parse(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new ResGenLiteException($"{filePath}: Error: Input file does not exist.");
                }

                var document = XDocument.Load(filePath, LoadOptions.SetLineInfo);
                return Parse(document, filePath);
            }
            catch (XmlException e)
            {
                throw new ResGenLiteException(
                    $"{filePath}({e.LineNumber}, {e.LinePosition}): Error: Failed to parse Xml: {e.Message}", e);
            }
            catch (IOException e)
            {
                throw new ResGenLiteException($"{filePath}: Error: Failed to load file: {e.Message}", e);
            }
            catch (UnauthorizedAccessException e)
            {
                throw new ResGenLiteException($"{filePath}: Error: Failed to load file: {e.Message}", e);
            }
        }

        /// <summary>
        /// Parses a loaded ResW/ResX file from an XDocument.
        /// </summary>
        private static ResourceData Parse(XDocument document, string filePath)
        {
            var data = new ResourceData();

            var root = document.Root;
            if (root == null)
            {
                throw new ResGenLiteException($"{filePath}: Error: File does not contain a root element");
            }

            if (root.Name != ResWNames.Root)
            {
                throw new ResGenLiteException(
                    $"{GetErrorPrefix(filePath, root)}Expected document element to be '{ResWNames.Root}'. Encountered '{root.Name}'");
            }

            foreach (var rootChild in root.Elements())
            {
                if (rootChild.Name == ResWNames.Schema)
                {
                    // Skip over the xsd schema declaration
                    continue;
                }

                if (rootChild.Name == ResWNames.ResHeader)
                {
                    // Skip over these metadata elements
                    continue;
                }

                if (rootChild.Name != ResWNames.Data)
                {
                    throw new ResGenLiteException(
                        $"{GetErrorPrefix(filePath, rootChild)}Unsupported element in ResW file. Only '{ResWNames.ResHeader}' and '{ResWNames.Data}' allowed. Encountered '{rootChild.Name}' ");
                }

                var entry = ParseDataElement(rootChild, filePath);

                if (!data.TryAddString(entry))
                {
                    throw new ResGenLiteException(
                        $"{GetErrorPrefix(filePath, rootChild)}Duplicate resource name '{entry.Name}' encountered.");
                }
            }

            return data;
        }

        private static ResourceDataEntry ParseDataElement(XElement dataElement, string filePath)
        {
            var entry = new ResourceDataEntry();

            foreach (var attribute in dataElement.Attributes())
            {
                if (attribute.Name == ResWNames.Space)
                {
                    // xmlns space declaration is okay.
                    continue;
                }

                if (attribute.Name == ResWNames.Name)
                {
                    entry.Name = attribute.Value;
                    continue;
                }

                throw new ResGenLiteException(
                    $"{GetErrorPrefix(filePath, attribute)}Unexpected attribute encountered. Only '{ResWNames.Name}' is allowed on '{ResWNames.Data}' element. Encountered '{attribute.Name}'");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                throw new ResGenLiteException(
                    $"{GetErrorPrefix(filePath, dataElement)}Required attribute '{ResWNames.Name}' on element '{ResWNames.Data}' is missing.");
            }

            foreach (var dataChild in dataElement.Elements())
            {
                if (dataChild.Name == ResWNames.Comment)
                {
                    entry.Comment = dataChild.Value;
                    continue;
                }

                if (dataChild.Name == ResWNames.Value)
                {
                    entry.Value = dataChild.Value;
                    continue;
                }

                throw new ResGenLiteException(
                    $"{GetErrorPrefix(filePath, dataChild)}Unexpected element encountered. Only '{ResWNames.Value}' is allowed to be a child of '{ResWNames.Data}' element. Encountered '{dataChild.Name}'");
            }
            if (entry.Value == null)
            {
                throw new ResGenLiteException(
                    $"{GetErrorPrefix(filePath, dataElement)}Required element '{ResWNames.Value}' under '{ResWNames.Data}' is missing.");
            }

            return entry;
        }

        /// <summary>
        /// Helper to print a standard error prefix format.
        /// </summary>
        private static string GetErrorPrefix(string fileName, IXmlLineInfo lineInfo)
        {
            return $"{fileName}({lineInfo.LineNumber}, {lineInfo.LinePosition}): Error: ";
        }

        /// <summary>
        /// Const names for xml comparison
        /// </summary>
        private class ResWNames
        {
            /// <nodoc />
            public static readonly XName Root = XName.Get("root");

            /// <nodoc />
            public static readonly XName ResHeader = XName.Get("resheader");

            /// <nodoc />
            public static readonly XName Data = XName.Get("data");

            /// <nodoc />
            public static readonly XName Name = XName.Get("name");

            /// <nodoc />
            public static readonly XName Comment = XName.Get("comment");

            /// <nodoc />
            public static readonly XName Value = XName.Get("value");

            /// <nodoc />
            public static readonly XName Space = XName.Get("space", "http://www.w3.org/XML/1998/namespace");

            /// <nodoc />
            public static readonly XName Schema = XName.Get("schema", "http://www.w3.org/2001/XMLSchema");
        }
    }
}
