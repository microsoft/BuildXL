# Object Model

This documents only highlights certain commonly used types in the object model presented by the XLG debugger.  

## Root Object 

```javascript
{
    Pips: [],
    Files: [],
    Directories: [],
    DirMembership: [],
    GroupedBy: []
}
```

## All Pips

```javascript
{
    Tags: [],
    Provenance: {},
    PipType: "CopyFile" | "HashSourceFile" | "Module" | "Process" | "SealDirectory" | "SpecFile" | "Value" | "WriteFile",
    PipId: {},
    SemiStableHash: {},
    FormattedSemiStableHash: {},
    DownstreamPips: [],
    UpstreamPips: [],
  }
```

## Process Pips

```javascript
{
    Description: {},
    ExecutionLevel: {},
    EXE: {},
    CMD: {},
    Inputs: [],
    Outputs: [],
    ExecutionPerformance: [],
    MonitoringData: [],
}
```

## File Artifacts

```javascript
{
    Path: {},
    Kind: "input" | "output",
    RewriteCount: {},
    FileContentInfo: {},
    Producer: {},
    Consumers: []
}
```

## Directory Artifacts

```javascript
{
    Path: {},
    PartialSealId: {},
    Kind: "source" | "exclusive opaque" | "shared opaque",
    Consumers: [],
    Members: []
}
```