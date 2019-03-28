// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as Xml from "Sdk.Xml";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function RootElement(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root")
            )
        );
    }

    @@Testing.unitTest()
    export function RootWithAttributes(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root", Xml.attr("a1", "v1"), Xml.attr("a2", "v2"))
            )
        );
    }

    @@Testing.unitTest()
    export function RootWithElementaAndAttributes(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root", 
                    Xml.elem("c1"), 
                    Xml.attr("a1", "v1"), 
                    Xml.elem("c2", 
                        Xml.attr("a1c", "v1c")
                    ), 
                    Xml.attr("a2", "v2")
                )
            )
        );
    }

    @@Testing.unitTest()
    export function RootWithText(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root", "content")
            )
        );
    }
    
    @@Testing.unitTest()
    export function RootWithAllNodes(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root", 
                    Xml.elem("elem"),
                    Xml.comment("comment"),
                    "text",
                    Xml.text("text2"),
                    Xml.comment("comment"),
                    Xml.cdata("cdata"),
                    Xml.processingInstruction("n", "v")
                )
            )
        );
    }

    @@Testing.unitTest()
    export function ElementsWithAllPrimitives(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root", 
                    Xml.elem("s", "str"),
                    Xml.elem("a", a`atom`),
                    Xml.elem("r", r`f/rel`),
                    Xml.elem("b", true),
                    Xml.elem("n", 1),
                    Xml.elem("p", p`path`),
                    Xml.elem("d", d`dir`),
                    Xml.elem("f", f`file`),
                    Xml.elem("mixed", ["str", 1, p`path`])
                )
            )
        );
    }

    @@Testing.unitTest()
    export function AttrWithAllPrimities(){
        Xml.write(
            p`out/out.xml`,
            Xml.doc(
                Xml.elem("root", 
                    Xml.attr("s", "str"),
                    Xml.attr("a", a`atom`),
                    Xml.attr("r", r`f/rel`),
                    Xml.attr("b", true),
                    Xml.attr("n", 1),
                    Xml.attr("p", p`path`),
                    Xml.attr("d", d`dir`),
                    Xml.attr("f", f`file`),
                    Xml.attr("mixed", ["str", 1, p`path`])
                )
            )
        );
    }
}
