# Importing and Exporting
Build modules are separate logical units that can export values. Build projects can import values from one or more build modules. Only values with 'public' visibility can be exported/imported.

# Import
There are two ways to import values from a module:

- in a declaration (`import { X } from "MyModule"`), and
- within an expression (`importFrom("MyModule").X`).

## The `import` declaration
Values may be imported from one or more modules at the top of a project definition.  This is recommended when the imported value is used multiple times in the same project.

### Standard import declaration
```ts
import * as Compression from "compression";
const zipArchive = Compression.Zip.pack([ f`file.txt` ]]);
```

### Selective import declaration
```ts
import {Zip} from "compression";
const zipArchive = Zip.pack([ f`file.txt` ]);
```

### Multiple selective import declaration
```ts
import {Zip, Gzip} from "compression";
const zipArchive = Zip.pack([ f`file.txt` ]);
const gzipArchive = Gzip.pack([ f`file.txt` ]);
```

### Aliased selective import declaration
```ts
import {Zip as FastZip} from "compression";
const zipArchive = FastZip.pack([ f`file.txt` ]);
```

### Aliased multiple selective import declaration
```ts
import {Zip as FastZip, GZip as GZ} from "compression";
const zipArchive = FastZip.pack([ f`file.txt` ]);
const gzip = GZ.pack([ f`file.txt` ]);
```

## The `importFrom` expression
Elements from other modules may be imported within an expression using the `importFrom` function.  This function takes the module's name (as a string value) to import and returns an object with all values exported by the selected module.  

```ts
const zipArchive = importFrom("compression").Zip.pack([ f`file.txt` ]);
```

*Note*: Consider using a single `import` declaration at the file's top if you find yourself overusing `importFrom` with the same module in a single file.

# Export
Declarations in a build project file are not visible to other projects unless they are explicitly exported with an `export` declaration.  The declaration must also be marked as public with the `@@public` decorator to be visible to other modules.

### Re-export a whole module
```ts
export * from "compression";
```

### Selective re-export
```ts
export { Zip } from "compression";
```

### Multiple selective re-export
```ts
export { Zip, Gzip } from "compression";
```

### Aliased selective re-export
```ts
export { Zip as FastZip } from "compression";
```

### Aliased multiple selective re-export
```ts
export { Zip as FastZip, Gzip as GZ } from "compression";
```

### Exporting values
```ts
export const myValue = 42;

const myObject = { "foo": "bar" };
export { myObject };
```

### Exporting imported values
```ts
import * as Compression from "compression";
import { Zip, Gzip as GZ } from "compression";

export { Compression, Zip, GZ as MyGZ };
export const gzipArchive = GZ.pack([ f`file.txt` ]);
```