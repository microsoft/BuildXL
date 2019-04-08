# vscode-test

This module helps you test VS Code extensions.

Supported:

- Node > 8.x
- Windows > Windows Server 2012+ / Win10+ (anything with Powershell > 5.0)
- macOS
- Linux

## Usage

See https://github.com/octref/vscode-test-sample for a runnable sample, with [Azure Devops](https://github.com/octref/vscode-test-sample/blob/master/azure-pipelines.yml) and [Travis](https://github.com/octref/vscode-test-sample/blob/master/.travis.yml) configurations.

```ts
import * as path from 'path'

import { runTests, downloadAndUnzipVSCode } from 'vscode-test'

async function go() {

  const extensionPath = path.resolve(__dirname, '../../')
  const testRunnerPath = path.resolve(__dirname, './suite')
  const testWorkspace = path.resolve(__dirname, '../../test-fixtures/fixture1')

  /**
   * Basic usage
   */
  await runTests({
    extensionPath,
    testRunnerPath,
    testWorkspace
  })

  const testRunnerPath2 = path.resolve(__dirname, './suite2')
  const testWorkspace2 = path.resolve(__dirname, '../../test-fixtures/fixture2')

  /**
   * Running a second test suite
   */
  await runTests({
    extensionPath,
    testRunnerPath: testRunnerPath2,
    testWorkspace: testWorkspace2
  })

  /**
   * Use 1.31.0 release for testing
   */
  await runTests({
    version: '1.31.0',
    extensionPath,
    testRunnerPath,
    testWorkspace
  })

  /**
   * Noop, since 1.31.0 already downloaded to .vscode-test/vscode-1.31.0
   */
  await downloadAndUnzipVSCode('1.31.0')

  /**
   * Manually download VS Code 1.30.0 release for testing.
   */
  const vscodeExecutablePath = await downloadAndUnzipVSCode('1.30.0')
  await runTests({
    vscodeExecutablePath,
    extensionPath,
    testRunnerPath,
    testWorkspace
  })
}

go()
```

## License

[MIT](LICENSE)

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
