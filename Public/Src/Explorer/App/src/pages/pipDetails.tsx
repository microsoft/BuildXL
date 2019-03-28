// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';

import { PipDetails } from "../models/pipDetails"
import { isProcess } from "../models/processDetails"
import { AsyncBuildPage, BuildRouteParams } from "./asyncBuildPage"
import * as Links from "../controls/links"
import { PipData } from "../controls/pipData";
import { PipTable } from "../controls/pipTable"

export interface PipDetailsParams extends BuildRouteParams {
    pipId: string,
}

export class PipDetailsPage extends AsyncBuildPage<PipDetailsParams, PipDetails> {

    getRequestUrl(props: PipDetailsParams): string {
        return `/b/${props.sessionId}/pips/${props.pipId}`;
    }

    renderData(pip: PipDetails) {
        return (
            <div>
                <div className="hub-header">
                    <h1>{pip.shortDescription}</h1>
                </div>
                <div className="hub-body">
                    <section className="hub-box">
                        <h3>Pip Summary</h3>
                        <table className="hub-details">
                            <tbody>
                                <tr>
                                    <th>Description:</th>
                                    <td>{pip.longDescription || pip.shortDescription}</td>
                                </tr>
                                <tr>
                                    <th>Definition:</th>
                                    <td>
                                        <span className="ref-int"><Links.ModuleLink reference={pip.module} sessionId={this.getSessionId()} /></span>
                                        <span className="ref-sep">/</span>
                                        <span className="ref-int"><Links.SpecFileLink reference={pip.specFile} sessionId={this.getSessionId()} /></span>
                                        <span className="ref-sep">/</span>
                                        <span className="ref-int"><Links.ValueLink reference={pip.value} sessionId={this.getSessionId()} /></span>
                                        { pip.tool ? <span className="ref-sep">/</span> : undefined }
                                        { pip.tool ? <span className="ref-int"><Links.ToolLink reference={pip.tool} sessionId={this.getSessionId()} /></span> : undefined }
                                    </td>
                                </tr>
                                <tr>
                                    <th>Qualifier:</th>
                                    <td>
                                        <span className="ref-int"><Links.QualifierLink reference={pip.qualifier} sessionId={this.getSessionId()} /></span>
                                    </td>
                                </tr>
                                <tr>
                                    <th>Tags:</th>
                                    <td>
                                        <ul className="detail-tag">
                                            {
                                                (pip.tags || []).map(tag => (<li key={tag.name}><span className="ref-int"><Links.TagLink reference={tag} sessionId={this.getSessionId()} /></span></li>))
                                            }
                                        </ul>
                                    </td>
                                </tr>
                                <tr>
                                    <th>Pip:</th>
                                    <td><span className="pipid">Pip2B9775D47DC600FC<button className="copy-button"><i className="ms-Icon ms-Icon--Copy"></i></button></span></td>
                                </tr>
                            </tbody>
                        </table>
                    </section>
                    {
                        !isProcess(pip) ? undefined :
                            (
                                <section className="hub-box">
                                    <h3>Process details</h3>
                                    <div className="group-controls">
                                        <div className="control">
                                            <div className="control-label">Tool:</div>
                                            <div className="control-item">{ pip.tool ? <Links.ToolLink reference={pip.tool} sessionId={this.getSessionId()} /> : undefined }</div>
                                        </div>
                                        <div className="control">
                                            <div className="control-label">Commandline:</div>
                                            <div className="control-item">
                                                <div className="process-executable"><Links.FileLink reference={pip.executable} renderIcon={false} sessionId={this.getSessionId()} /></div>
                                                <div className="process-arguments">
                                                    <PipData data={pip.arguments} style="linePerArg" sessionId={this.getSessionId()} />
                                                </div>
                                            </div>
                                        </div>
                                        <div className="control">
                                            <div className="control-label">Commandline:</div>
                                            <div className="control-item">
                                                <div className="process-executable"><Links.FileLink reference={pip.executable} renderIcon={false} sessionId={this.getSessionId()} /></div>
                                                <div className="process-arguments">
                                                    <PipData data={pip.arguments} style="linePerArg" sessionId={this.getSessionId()} />
                                                </div>
                                            </div>
                                        </div>
                                        <div className="control">
                                            <div className="control-label">Working directory:</div>
                                            <div className="control-item"><Links.DirectoryLink reference={pip.workingDirectory} sessionId={this.getSessionId()} /></div>
                                        </div>
                                        <div className="control">
                                            <div className="control-label">Environment variables:</div>
                                            <div className="control-item">
                                                <table className="hub-details">
                                                    <tbody>
                                                        {
                                                            (pip.environmentVariables || []).map(envVar =>
                                                                <tr key={envVar.name}>
                                                                    <th>{envVar.name}</th>
                                                                    <td>{envVar.isPassthrough ? 
                                                                        <span className="passthrough-env-var">[Pass-through variable]</span> : 
                                                                        <PipData data={envVar.value} sessionId={this.getSessionId()} />
                                                                    }</td>
                                                                </tr>)
                                                        }
                                                    </tbody>
                                                </table>
                                            </div>
                                        </div>
                                        <div className="control">
                                            <div className="control-label">Untracked Scopes (unsafe):</div>
                                            <div className="control-item">
                                                    {
                                                        (pip.untrackedScopes || []).map(scope =>
                                                            <div key={scope.id}><Links.DirectoryLink reference={scope} sessionId={this.getSessionId()} /></div>
                                                        )
                                                    }
                                            </div>
                                        </div>
                                        <div className="control">
                                            <div className="control-label">Untracked Files (unsafe):</div>
                                            <div className="control-item">
                                                <ul>
                                                    {
                                                        (pip.untrackedFiles || []).map(file =>
                                                            <div key={file.id}><Links.FileLink reference={file} sessionId={this.getSessionId()} /></div>
                                                        )
                                                    }
                                                </ul>
                                            </div>
                                        </div>
                                    </div>
                                </section>
                            )
                    }

                    <section className="hub-box">
                        <h3>Connections</h3>

                        <h4>Pip Dependencies (pips that produce files that this pip consumes)</h4>
                        <PipTable
                            items={pip.dependencies || []}
                            sessionId={this.getSessionId()}
                        />

                        <h4>Pip Dependent (pips that consumes outputs that this pip produces)</h4>
                        <PipTable
                            items={pip.dependents || []}
                            sessionId={this.getSessionId()}
                        />

                        
                    </section>
                </div>
            </div>
        );
    }
}
