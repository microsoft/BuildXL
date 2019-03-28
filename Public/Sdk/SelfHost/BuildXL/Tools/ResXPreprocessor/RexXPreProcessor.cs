// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace BuildXLSdk
{
    internal class ResXPreProcessor
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Invalid arguments count.");
                    WriteUsage();
                    return 1;
                }

                var inFile = args[0];
                if (!File.Exists(inFile))
                {
                    Console.Error.WriteLine($"File {inFile} does not exist.");
                }

                var outFile = args[1];
                Directory.CreateDirectory(Path.GetDirectoryName(outFile));

                var replacements = new Dictionary<string, string>();
                for (var i = 2; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (!arg.StartsWith("/d:"))
                    {
                        Console.Error.WriteLine($"Invalid argument: {arg}. It does not start with /d:");
                        WriteUsage();
                        return 1;
                    }

                    // Strip /d:
                    var keyValue = arg.Substring(3);

                    var parts = keyValue.Split(new[] {'='}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                    {
                        Console.Error.WriteLine($"Invalid argument: {arg}. It does hot have a=b pattern.");
                        WriteUsage();
                        return 1;
                    }

                    replacements.Add("{" + parts[0] + "}", parts[1]);
                }

                var xdoc = XDocument.Load(inFile);

                var dataElements = xdoc.Elements("root").Elements("data");
                foreach (var data in dataElements)
                {
                    var typeAttr = data.Attribute("type");
                    if (typeAttr != null && string.Equals(typeAttr.Value, "System.Resources.ResXFileRef, System.Windows.Forms"))
                    {
                        // This is a field with a path. Set this elements path to the original location.
                        var typeValueElement = data.Element("value");
                        if (typeValueElement != null)
                        {
                            typeValueElement.Value = Path.GetDirectoryName(inFile) + Path.DirectorySeparatorChar + typeValueElement.Value;
                        }

                        continue;
                    }

                    var valueElement = data.Element("value");
                    if (valueElement != null)
                    {
                        var newValue = valueElement.Value;
                        foreach (var kv in replacements)
                        {
                            newValue = newValue.Replace(kv.Key, kv.Value);
                        }

                        valueElement.Value = newValue;
                    }
                }

                xdoc.Save(outFile);

                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);

                return 2;
            }
        }


        public static void WriteUsage()
        {
            Console.Error.WriteLine("Usage: <InputResX> <OutputResX> [/d:<key>=<value>]");
        }
    }
}