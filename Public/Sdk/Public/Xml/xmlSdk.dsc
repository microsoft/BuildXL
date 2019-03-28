// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

@@public
/** Writes an xml */
export function read(xmlFile: SourceFile): Document
{
    return <Document>_PreludeAmbientHack_Xml.read(xmlFile);
}

@@public
/** Writes an xml Dom */
export function write(destinationFile: Path, doc: Document, options?: WriteOptions, tags?: string[], description?: string): File
{
    return _PreludeAmbientHack_Xml.write(destinationFile, doc, options, tags, description);
}

/**
 * When writing placeholder for options
 */
@@public
export interface WriteOptions {
}

// Document

@@public
export interface Document {
    kind: "document",
    nodes: Node[]
}

@@public
export function doc(root: Element) : Document {
    return {
        kind: "document",
        nodes: [root],
    };
}

@@public
export function isDocument(item: Node) : item is Document {
    return item.kind === "document"; 
}

// Element

@@public
export interface Element extends Node {
    kind: "element",
    name: Name,
    attributes: Attribute[],
    nodes: Node[]
}

@@public
export function elem(name: string | Name, ...contents: NodeContent[]) : Element {
    let attributes : Attribute[] = [];
    let content : Node[] = [];

    for (let item of contents) {
        if (item === undefined) {
            // Skip
            continue;
        }
        
        if (isNode(item)) {
            if (isAttribute(item)) {
                attributes = attributes.push(item);
            }
            else {
                content = content.push(item);
            }
        }
        else {
            content = content.push(text(item));
        }
    }

    return {
        kind: "element",
        name: toName(name),
        attributes: attributes,
        nodes: content,
    };
}

@@public
export function isElement(item: Node) : item is Element {
    return item.kind === "element"; 
}

// Attribute

@@public
export interface Attribute extends Node {
    kind: "attribute",
    name: Name,
    value: TextContent
}

@@public
export function attr(name: string | Name, value: TextContent) : Attribute {
    return {
        kind: "attribute",
        name: toName(name),
        value: value,
    };
}

@@public
export function isAttribute(item: Node) : item is Attribute {
    return item.kind === "attribute"; 
}


// Text

@@public
export interface Text extends Node {
    kind: "text",
    text: TextContent
}

@@public
export function text(content: TextContent) : Text {
    return {
        kind: "text",
        text: content,
    };
}

@@public
export function isText(item: Node) : item is Text {
    return item.kind === "text"; 
}

// CData
@@public
export interface CData extends Node {
    kind: "cdata",
    text: TextContent
}

@@public
export function cdata(content: TextContent) : CData {
    return {
        kind: "cdata",
        text: content,
    };
}

@@public
export function isCData(item: Node) : item is CData {
    return item.kind === "cdata"; 
}

// Comment

export interface Comment extends Node {
    kind: "comment",
    value: TextContent
}

@@public 
export function comment(value: TextContent) : Comment {
    return {
        kind: "comment",
        value: value,
    };
}

@@public
export function isComment(item: Node) : item is Comment {
    return item.kind === "comment"; 
}

// Processing Instruction

export interface ProcessingInstruction extends Node {
    kind: "processing-instruction",
    name: string,
    value: string,
}

@@public 
export function processingInstruction(name: string, value: string) : ProcessingInstruction {
    return {
        kind: "processing-instruction",
        name: name,
        value: value,
    };
}

@@public
export function isProcessingInstruction(item: Node) : item is ProcessingInstruction {
    return item.kind === "processing-instruction"; 
}


// Node

@@public
export interface Node {
    kind: "document" | "element" | "attribute" | "text" | "cdata" | "comment" | "processing-instruction",
}

@@public
export function isNode(item: Object) : item is Node {
    return item["kind"] !== undefined;
}


// Element manipulation

/**
 * Returns a copy of the given element where the elements with the given name are updated using the update function. 
 * When no child elements are found to match the name, a new one is added.
 */
@@public
export function updateOrAddChildElement(
    element: Element, 
    name: string | Name,
    update: (Element) => Element
    ) : Element
{
    let updatedNodes :Node[] = [];
    let matched = false;
    
    for (let child of element.nodes) {
        if (isElement(child) && nameEquals(child.name, name)) {
            matched = true;
            updatedNodes = updatedNodes.push(update(child));
        }
        else {
            updatedNodes = updatedNodes.push(child);
        }
    }

    if (!matched) {
        let newElement = elem(name);
        updatedNodes = updatedNodes.push(update(newElement));
    }

    return {
        kind: "element",
        name: element.name,
        attributes: element.attributes,
        nodes: updatedNodes
    };
}

/**
 * Adds the given nodes to the element
 */
@@public
export function addNodes(element: Element, nodes: Node[]) : Element
{
    return {
        kind: "element",
        name: element.name,
        attributes: element.attributes,
        nodes: [
            ...element.nodes,
            ...nodes
        ]
    };
}

// Name

@@public
export interface  Name {
    prefix?: string,
    local: string, 
    namespace?: string
}

@@public
export function name(local: string, ns?: string, prefix?: string) : Name {
    return {
        local: local,
        namespace: ns || "",
        prefix: prefix || "",
    };
}

function toName(name: string | Name) : Name {
    if (typeof name === "string") {
        return {local: name};
    }

    return <Name>name;
}

@@public
export function nameEquals(left: string | Name, right: string | Name) {
    if (typeof left === "string") {
        if (typeof right === "string") {
            return left === right;
        } else {
            return (!right.prefix || right.prefix === "") && (!right.namespace || right.namespace === "") && left === right.local;
        }
    } else {
        if (typeof right === "string") {
            return (!left.prefix || left.prefix === "") && (!left.namespace || left.namespace === "") && right === left.local;
        } else {
            return left.prefix === right.prefix && left.namespace === right.namespace && left.local === right.local;
        }
    }
}

type NodeType = "element" | "attribute" | "text";
type Literal = string | number | boolean | PathAtom | RelativePath | Path | File | Directory;
type TextContent = Literal | Literal[];
type NodeContent = Literal | Literal[] | Node;
