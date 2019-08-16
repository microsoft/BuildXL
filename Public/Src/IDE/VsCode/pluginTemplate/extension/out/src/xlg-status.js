// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
const vscode = require("vscode");
exports.BxlStatusCsvFileName = "BuildXL.status.csv";
exports.CmdRenderStatus = "XLG.Render.Status";
exports.DefaultColumnsOfInterest = ["*"];
exports.DefaultColumnsToRender = ["Cpu Percent", "Mem Percent", "Process Running"];
function activate(context) {
    context.subscriptions.push(vscode.commands.registerCommand(exports.CmdRenderStatus, () => {
        return renderActiveBxlStatusCsv();
    }));
}
exports.activate = activate;
function range(start, count) {
    return Array(count).fill(0).map((elem, idx) => start + idx);
}
function splitColumns(line) {
    return line.split(",").map(s => s.trim());
}
function convertBxlTimeToSeconds(bxlTimeValue) {
    if (bxlTimeValue.startsWith("["))
        bxlTimeValue = bxlTimeValue.substring(1);
    if (bxlTimeValue.endsWith("]"))
        bxlTimeValue = bxlTimeValue.substring(0, bxlTimeValue.length - 1);
    return bxlTimeValue
        .split(":")
        .map(s => Number(s))
        .reduce((acc, elem) => acc * 60 + elem, 0);
}
function error(msg) {
    vscode.window.showErrorMessage(msg);
}
function renderActiveBxlStatusCsv() {
    const activeEditor = vscode.window.activeTextEditor;
    if (activeEditor === undefined)
        return error("No active editor.");
    if (!activeEditor.document.fileName.endsWith(exports.BxlStatusCsvFileName))
        return error(`This command can only run against a '${exports.BxlStatusCsvFileName}' file; instead, active editor is '${activeEditor.document.fileName}'`);
    vscode.window.showInputBox({
        prompt: "Enter columns of interest",
        value: exports.DefaultColumnsOfInterest.join(", "),
        ignoreFocusOut: true
    }).then(columnsOfInterestUserInput => {
        const columnsOfInterest = splitColumns(columnsOfInterestUserInput);
        vscode.window.showInputBox({
            prompt: "Enter columns to render",
            value: exports.DefaultColumnsToRender.join(", "),
            ignoreFocusOut: true
        }).then(columnsToRenderUserInput => {
            return renderCsvDocument(activeEditor.document, columnsOfInterest, splitColumns(columnsToRenderUserInput));
        });
    });
}
exports.renderActiveBxlStatusCsv = renderActiveBxlStatusCsv;
function findInvalidColumns(availableColumns, specifiedColumns) {
    return specifiedColumns.filter(col => !availableColumns.includes(col));
}
function renderCsvDocument(document, columnsOfInterest, visibleColumns) {
    const fullHeader = splitColumns(document.lineAt(0).text);
    // validate columns of interest
    const columnsOfInterestsAreSpecifies = columnsOfInterest !== undefined && columnsOfInterest.join(",") !== "*";
    if (columnsOfInterestsAreSpecifies) {
        const invalidColumns = findInvalidColumns(fullHeader, columnsOfInterest);
        if (invalidColumns.length > 0) {
            return error("The following columns are not found in the CSV file: " + invalidColumns.join(", "));
        }
    }
    const headerIndices = columnsOfInterestsAreSpecifies
        ? columnsOfInterest.map(col => fullHeader.indexOf(col))
        : range(1, fullHeader.length);
    // validate visible columns
    const invalidColumnsToRender = findInvalidColumns(columnsOfInterestsAreSpecifies ? columnsOfInterest : fullHeader, visibleColumns);
    if (invalidColumnsToRender.length > 0) {
        return error("The following columns are not found in the specified header: " + invalidColumnsToRender.join(", "));
    }
    // construct JSON data
    const data = {
        timeAxis: [],
        columns: headerIndices.map(hIdx => ({ name: fullHeader[hIdx], visible: "legendonly", values: [] }))
    };
    for (let i = 1; i < document.lineCount; i++) {
        const line = document.lineAt(i).text;
        if (line.length === 0)
            continue;
        const row = splitColumns(line);
        if (row.length != fullHeader.length) {
            error(`CSV row has different number of columns (${row.length}) from the CSV header (${fullHeader.length})`);
            continue;
        }
        data.timeAxis.push(convertBxlTimeToSeconds(row[0]));
        for (let j = 0; j < headerIndices.length; j++) {
            const col = data.columns[j];
            col.values.push(row[headerIndices[j]]);
            if (visibleColumns.indexOf(col.name) > -1) {
                col.visible = true;
            }
        }
    }
    // create a panel
    const panel = vscode.window.createWebviewPanel("bxl-status", "BuildXL Graphs", vscode.ViewColumn.Active, {
        enableScripts: true
    });
    panel.webview.html =
        `
<head>
  <!-- Plotly.js -->
  <script src="https://cdn.plot.ly/plotly-latest.min.js"></script>
</head>
<body>
<!-- Plotly chart will be drawn inside this DIV -->
<div id="myDiv"></div>
  <script>
    var metaData = ${JSON.stringify(data)};

    var data = metaData.columns.map(function(col) {
        return {
            type: "scatter",
            mode: "lines",
            name: col.name,
            visible: col.visible,
            x: metaData.timeAxis,
            y: col.values,
        };
    });

    var layout = {
        title: 'BuildXL Time Series',
    };

    Plotly.newPlot('myDiv', data, layout, {showSendToCloud: false});
  </script>
</body>
</html>
`;
}
//# sourceMappingURL=xlg-status.js.map