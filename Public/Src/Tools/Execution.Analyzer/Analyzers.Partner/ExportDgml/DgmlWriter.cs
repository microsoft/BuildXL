// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Serialization;

namespace BuildXL.Execution.Analyzer.Analyzers.ExportDgml
{
    public sealed class DgmlWriter
    {
        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        public struct Graph
        {
            public Node[] Nodes;
            public Link[] Links;
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        public struct Node
        {
            [XmlAttribute]
            public string Id;
            [XmlAttribute]
            public string Label;

            public Node(string id, string label)
            {
                Id = id;
                Label = label;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        public struct Link
        {
            [XmlAttribute]
            public string Source;
            [XmlAttribute]
            public string Target;
            [XmlAttribute]
            public string Label;

            public Link(string source, string target, string label)
            {
                Source = source;
                Target = target;
                Label = label;
            }
        }

        public List<Node> Nodes { get; private set; }

        public List<Link> Links { get; private set; }

        public DgmlWriter()
        {
            Nodes = new List<Node>();
            Links = new List<Link>();
        }

        public void AddNode(Node n)
        {
            Nodes.Add(n);
        }

        public void AddLink(Link l)
        {
            Links.Add(l);
        }

        public void Serialize(string xmlpath)
        {
            Graph g = default(Graph);
            g.Nodes = Nodes.ToArray();
            g.Links = Links.ToArray();

            XmlRootAttribute root = new XmlRootAttribute("DirectedGraph");
            root.Namespace = "http://schemas.microsoft.com/vs/2009/dgml";
            XmlSerializer serializer = new XmlSerializer(typeof(Graph), root);
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            using (XmlWriter xmlWriter = XmlWriter.Create(xmlpath, settings))
            {
                serializer.Serialize(xmlWriter, g);
            }
        }
    }
}
