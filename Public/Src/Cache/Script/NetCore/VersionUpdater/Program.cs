// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace VersionUpdater
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length <= 1) {
                Console.WriteLine(@"Usage: versionupdater <relative-path to versions file> <relative-path to nuspec file> ...");
                Environment.Exit(1);
            }
            
            var basePath = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
            XElement versionsProps = XElement.Load(args[0]);
            var version = String.Empty;

            foreach (XElement element in versionsProps.Descendants())
            {
                if (element.Name.LocalName.Equals(@"CloudStoreReleaseVersion"))
                {
                    version = element.Value as string;
                }
            }

            if (version == null) {
                throw new Exception(@"Couldn't parse version number from the Versions.props file, exiting.");
            }

            var nugetSpecFiles = args.Skip(1).Take((args.Length-1));
            foreach (var nugetSpecFile in nugetSpecFiles) {
                var path = basePath + nugetSpecFile;
                XElement nuspec = XElement.Load(path);
                foreach (XElement element in nuspec.Descendants())
                {
                    if (element.Name.LocalName.Equals(@"version"))
                    {
                        element.Value = version + "-netcore";
                    }
                    else if (element.Name.LocalName.Equals(@"dependencies"))
                    {
                        foreach (XElement dependencyElement in element.Descendants())
                        {
                            if (dependencyElement.FirstAttribute.Value.Equals("BuildXL.Cache.ContentStore.Interfaces"))
                            {
                                dependencyElement.LastAttribute.Value = version + "-netcore";
                                break;
                            }
                        }
                    }
                }

                nuspec.Save(path);
            }

            Console.WriteLine(version);
        }
    }
}
