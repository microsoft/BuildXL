declare module 'vscode/lib/testrunner' {
    export function configure(options: MochaSetupOptions): void;

    interface MochaSetupOptions {

        /**
         * Milliseconds to wait before considering a test slow
         *
         * @type {Number}
         */
        slow?: number;

        /**
         * Enable timeouts
         *
         * @type {Boolean}
         */
        enableTimeouts?: boolean;

        /**
         * Timeout in milliseconds
         *
         * @type {Number}
         */
        timeout?: number;

        /**
         * UI name "bdd", "tdd", "exports" etc
         *
         * @type {String}
         */
        ui?: string;

        /**
         * Array of accepted globals
         *
         * @type {Array} globals
         */
        globals?: any[];

        /**
         * Reporter instance (function or string), defaults to `mocha.reporters.spec`
         *
         * @type {String | Function}
         */
        reporter?: any;

        /**
         * Reporter settings object
         *
         * @type {Object}
         */
        reporterOptions?: any;

        /**
         * Bail on the first test failure
         *
         * @type {Boolean}
         */
        bail?: boolean;

        /**
         * Ignore global leaks
         *
         * @type {Boolean}
         */
        ignoreLeaks?: boolean;

        /**
         * grep string or regexp to filter tests with, if a string it is escaped
         *
         * @type {RegExp | String}
         */
        grep?: any;

        /**
         * Use colored output from test results
         *
         * @type {Boolean}
         */
        useColors?: boolean;

        /**
         * Tests marked only fail the suite
         *
         * @type {Boolean}
         */
        forbidOnly?: boolean;

        /**
         * Pending tests and tests marked skip fail the suite
         *
         * @type {Boolean}
         */
        forbidPending?: boolean;

        /**
         * Number of times to retry failed tests
         *
         * @type {Number}
         */
        retries?: number;

        /**
         * Display long stack-trace on failing
         *
         * @type {Boolean}
         */
        fullStackTrace?: boolean;

        /**
         * Delay root suite execution
         *
         * @type {Boolean}
         */
        delay?: boolean;

        /**
         * Use inline diffs rather than +/-
         *
         * @type {Boolean}
         */
        useInlineDiffs?: boolean;
    }
}
