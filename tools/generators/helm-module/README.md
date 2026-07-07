# helm-module generator

Nx generator that scaffolds a compliant-by-construction Helm domain module (impl + `.Contracts` + `.Tests`), distilled from the CRM exemplar. See `generator.ts` for behavior and `schema.json` for inputs.

Usage:

```
npx nx g helm-module <Name> --schema <schema>
```

`generator.js` is compiled output (from `generator.ts` via `tools/generators/helm-module/tsconfig.json`) and must be regenerated via `npm run build:generators` after editing `generator.ts` — do not hand-edit `generator.js`.
