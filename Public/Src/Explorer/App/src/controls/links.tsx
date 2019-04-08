// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as References from '../models/references';
import { Link } from 'react-router-dom';

export interface PipRefProperty<TRef> {
    sessionId: string,
    renderIcon?: boolean,
    renderShort?: boolean
    reference: TRef
};

abstract class RefLink<TRef> extends React.Component<PipRefProperty<TRef>, {}> {

    abstract getLink(): string;
    abstract getText(): string;
    abstract getIcon(): string;

    ref() {
        return this.props.reference;
    }

    render() {
        const relativeLink = this.getLink();
        const link = relativeLink.indexOf("/") == 0
            ? relativeLink
            :  "/b/" + this.props.sessionId + "/" + relativeLink
        const icon = this.props.renderIcon !== false ? <i className={"ms-Icon ms-Icon--" + this.getIcon() }></i> : <></>
        return (
            <span className="ref-int">{icon}<Link to={link}>{this.getText()}</Link></span>
        )
    }
}

abstract class PathRefLink<TRef extends References.PathRef > extends RefLink<TRef> {
    getText() { return  this.props.renderShort ? this.ref().fileName: this.ref().filePath; }
    getLink() { return "path/" + this.ref().id; }
    getIcon() { return "TextDocument"; }
}

export class BuildLink extends RefLink<References.BuildRef> {
    getText() { return this.ref().sessionId; }
    getLink() { return "/b/" + this.ref().sessionId; }
    getIcon() { 
        switch (this.ref().kind) {
            case "local": return "HardDrive";
            case "cloudBuild": return "Cloud";
            default: throw "Unexpected build kind: " + this.ref().kind;
        }
    }
}

export class PipLink extends RefLink<References.PipRef> {
    getText() { return this.ref().semiStableHash; }
    getLink() { return `pips/${this.ref().id}`; }
    getIcon() { return "GitGraph"; }
}

export class ModuleLink extends RefLink<References.ModuleRef> {
    getText() { return this.ref().name; }
    getLink() { return "modules/" + this.ref().id; }
    getIcon() { return "BubbleChart"; }
}

export class SpecFileLink extends PathRefLink<References.SpecFileRef> {
    getLink() { return "projects/" + this.ref().id; }
}

export class FileLink extends PathRefLink<References.FileRef> {
}

export class DirectoryLink extends PathRefLink<References.DirectoryRef> {
    getIcon() { return "FolderHorizontal"; }
}

export class PathLink extends PathRefLink<References.PathRef> {
}

export class ToolLink extends RefLink<References.ToolRef> {
    getText() { return this.ref().name; }
    getLink() { return "tools/" + this.ref().id; }
    getIcon() { return "DeveloperTools"; }
}

export class ValueLink extends RefLink<References.ValueRef> {
    getText() { return this.ref().symbol; }
    getLink() { return "values/" + this.ref().id; }
    getIcon() { return "Copper"; }
}

export class TagLink extends RefLink<References.TagRef> {
    getText() { return this.ref().name; }
    getLink() { return "tags/" + this.ref().name; }
    getIcon() { return "Tag"; }
}


export class QualifierLink extends RefLink<References.QualifierRef> {
    getText() { return this.ref().shortName; }
    getLink() { return "qualifiers/" + this.ref().id; }
    getIcon() { return "SetTopStack"; }
}
