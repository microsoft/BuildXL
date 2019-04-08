// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import { Router, Route } from 'react-router-dom'
import * as History from 'history'


import { Header } from './controls/header';
import { Nav } from './controls/nav';
import { StatusBar } from './controls/statusBar';
import { Theme } from './controls/theme';

import { Settings } from './pages/settings';
import { Builds } from './pages/builds';
import { BuildSummary } from './pages/buildSummary';
import { Pips } from './pages/pips';
import { PipDetailsPage} from './pages/pipDetails';

export const history = History.createHashHistory();

export class App extends React.Component<{}, {}> {
  render() {
    return (
      <Router history={history}>
        <div className="app">
          <Header />
          <Nav />
          <main>
            <Route path="/" exact component={Builds} />
            <Route path="/b/:sessionId" exact component={BuildSummary} />
            <Route path="/b/:sessionId/pips" exact component={Pips} />
            <Route path="/b/:sessionId/pips/:pipId" exact component={PipDetailsPage} />
            <Route path="/settings" exact component={Settings} />
          </main>
          <StatusBar />
          <Theme />
        </div>
      </Router>
    );
  }
}
