## Concept of weight
BuildXL has a limited number of process slots that can be used for processes execution. This number is indicated by the argument /maxproc when running BuildXL. "Weight" is a concept that describes how resource-heavy a process is. It specifies how many process slots it requires to execute. The total "weight" of all processes running concurrently must be less than the number of available process slots. "Weight" defaults to 1, converts values < 1 to 1, and considers values >= available process slots to mean the process should run alone. 

Weight can be statically configured as the examples below describe or use a dynamic value based on historical performance information. This historic information looks at the number of CPU clock cycles used compared to a process pip's wall clock runtime. If it utilized 3 CPU cores over its wall clock runtime window, a value of 3 will be used. Accuracy is impacted by how the operating allocates CPU time for the process.

Historic metadata is toggled with the `/UseHistoricalCpuUsageInfo` command line flag. It defaults to enabled in CloudBuild but defaults to disabled otherwise. When historic weight and statically configured weight both exist, the larger of the two values are used.

## Language support & API
`weight` is a field in the argument of `Transformer.execute` in DScript that allows user to specify the number.
```ts
Transformer.execute({
        tool: <your tool name>,
        workingDirectory: d`.`,
        arguments: [ /*other args*/ ],
        weight: <integer number representing the weight of this pip>,
        disableCacheLookup: true,
        dependencies: [ /* dependencies list*/],
    });
```

In addition, in the `QTest.SDK`, `weight` is also a field in the `QTestArguments`. The `weight` in `QTestArguments` will be passed through when calling `Transformer.execute` to create a QTest pip in `QTest.SDK`. So the user of `QTest.SDK` can easily set the weight of the QTest pip.
```ts
QTest.runQTest({
    testAssembly: <your testAssembly path>,
    qTestRetryOnFailure : undefined,
    qTestTimeoutSec : undefined,
    qTestIgnoreQTestSkip : undefined,
    qTestRawArgFile : undefined,
    qTestAdapterPath: <your testAdapter path>,
    invocation: invocation_887,
    project: __projectSlice,
    qTestInputs: [ /* input file list*/ ],
    weight: <integer number representing the weight of this pip>,
    });
```


