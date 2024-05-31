            var obj = {};
            Object.defineProperty(obj, "accProperty", <PropertyDescriptor>({
                get: function () {
                    // CodeQL [SM04509] Intentional usage of eval in a test. The executed code is static.
                    eval("public = 1;");
                    return 11;
                },
                set: function (v) {
                }
            }))
