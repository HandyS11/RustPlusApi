// Local mirror of the JavaScript rules SonarQube runs against develop (Sonar.yml), so a smell fails
// `npm run lint` here and in CI.yml instead of surfacing after the push. SonarQube's own
// `javascript:S9383` (Promises should not be left unhandled) and `S6544` (Promises should not be
// misused) are wrappers over typescript-eslint's type-aware rules, which is why a TypeScript program
// (tsconfig.json) is needed to lint plain JavaScript. The remaining Sonar way rules come from
// SonarSource's eslint-plugin-sonarjs.
import js from "@eslint/js";
import globals from "globals";
import sonarjs from "eslint-plugin-sonarjs";
import tseslint from "typescript-eslint";

export default tseslint.config(
    {
        files: ["wwwroot/**/*.js"],
        extends: [js.configs.recommended, sonarjs.configs.recommended],
        plugins: { "@typescript-eslint": tseslint.plugin },
        languageOptions: {
            parser: tseslint.parser,
            ecmaVersion: 2022,
            sourceType: "script",
            globals: globals.browser,
            parserOptions: {
                project: "./tsconfig.json",
                tsconfigRootDir: import.meta.dirname
            }
        },
        rules: {
            "@typescript-eslint/no-floating-promises": "error",
            // Sonar's S6544 runs this rule with checksVoidReturn off: passing an async function straight to
            // addEventListener is accepted, as it is here.
            "@typescript-eslint/no-misused-promises": ["error", { checksVoidReturn: false }]
        }
    }
);
