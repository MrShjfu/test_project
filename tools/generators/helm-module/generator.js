"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.default = helmModuleGenerator;
const devkit_1 = require("@nx/devkit");
const child_process_1 = require("child_process");
const path = __importStar(require("path"));
const PROGRAM_CS = 'apps/api/Helm.Host/Program.cs';
const HOST_CSPROJ = 'apps/api/Helm.Host/Helm.Host.csproj';
const SERVICES_MARKER = '// <helm-modules-services>';
const ENDPOINTS_MARKER = '// <helm-modules-endpoints>';
async function helmModuleGenerator(tree, options) {
    const name = (0, devkit_1.names)(options.name).className; // PascalCase
    const schema = options.schema.toLowerCase();
    const projects = [
        { impl: `Helm.${name}`, sln: `libs/api/Helm.${name}/Helm.${name}.csproj` },
        { impl: `Helm.${name}.Contracts`, sln: `libs/api/Helm.${name}.Contracts/Helm.${name}.Contracts.csproj` },
        { impl: `Helm.${name}.Tests`, sln: `libs/api/Helm.${name}.Tests/Helm.${name}.Tests.csproj` },
    ];
    // Refuse to clobber an existing module.
    for (const p of projects) {
        if (tree.exists(`libs/api/${p.impl}`)) {
            throw new Error(`libs/api/${p.impl} already exists — refusing to overwrite. Delete it first.`);
        }
    }
    // Emit templates. The directory placeholders (__moduleImpl__ etc.) map to the real project
    // directory names; __name__ in file names becomes the PascalCase module name.
    (0, devkit_1.generateFiles)(tree, path.join(__dirname, 'files'), '.', {
        name,
        schema,
        moduleImpl: `Helm.${name}`,
        moduleContracts: `Helm.${name}.Contracts`,
        moduleTests: `Helm.${name}.Tests`,
        tmpl: '', // strips the .template suffix
        template: '',
    });
    // generateFiles drops the ".template" suffix only if we tell it — we use a two-pronged approach:
    // template files end in `.template`, which we rename below (devkit strips `__tmpl__`/`.tmpl`
    // conventions, not arbitrary suffixes).
    stripTemplateSuffix(tree);
    // Ensure the Contracts .gitkeep files exist (empty files can be dropped by some pipelines).
    for (const folder of ['Dtos', 'Events', 'Interfaces']) {
        const gk = `libs/api/Helm.${name}.Contracts/${folder}/.gitkeep`;
        if (!tree.exists(gk))
            tree.write(gk, '');
    }
    // Host must reference the new module implementation (only Helm.Host references impls — ADR-001).
    addHostProjectReference(tree, name);
    // Register in Host/Program.cs: `using`, then service + endpoint calls before the markers.
    addUsing(tree, PROGRAM_CS, `using Helm.${name};`);
    insertBeforeMarker(tree, PROGRAM_CS, SERVICES_MARKER, `builder.Services.Add${name}Module(builder.Configuration);`);
    insertBeforeMarker(tree, PROGRAM_CS, ENDPOINTS_MARKER, `app.Map${name}Endpoints();`);
    await (0, devkit_1.formatFiles)(tree);
    // dotnet sln add + migration instruction run after the tree is flushed to disk.
    return () => {
        const root = tree.root;
        for (const p of projects) {
            try {
                (0, child_process_1.execSync)(`dotnet sln "${(0, devkit_1.joinPathFragments)(root, 'Helm.sln')}" add "${(0, devkit_1.joinPathFragments)(root, p.sln)}"`, {
                    stdio: 'inherit',
                    cwd: root,
                });
            }
            catch (e) {
                devkit_1.logger.warn(`dotnet sln add failed for ${p.sln} — add it manually: dotnet sln Helm.sln add ${p.sln}`);
            }
        }
        devkit_1.logger.info('');
        devkit_1.logger.info(`Helm.${name} scaffolded. Next step — create its initial EF migration:`);
        devkit_1.logger.info('');
        devkit_1.logger.info(`  dotnet ef migrations add Init${name} \\\n` +
            `    --project libs/api/Helm.${name}/Helm.${name}.csproj \\\n` +
            `    --startup-project apps/api/Helm.Host/Helm.Host.csproj \\\n` +
            `    --output-dir Migrations`);
        devkit_1.logger.info('');
    };
}
/** Renames every generated `*.template` file to drop the suffix (devkit copies them verbatim). */
function stripTemplateSuffix(tree) {
    const walk = (dir) => {
        for (const child of tree.children(dir)) {
            const full = (0, devkit_1.joinPathFragments)(dir, child);
            if (!tree.isFile(full)) {
                walk(full);
            }
            else if (full.endsWith('.template')) {
                const content = tree.read(full);
                tree.write(full.slice(0, -'.template'.length), content ?? Buffer.from(''));
                tree.delete(full);
            }
        }
    };
    walk('libs/api');
}
/** Inserts a `using` directive after the last existing top-of-file `using` in Program.cs. */
function addUsing(tree, file, using) {
    const content = tree.read(file, 'utf-8');
    if (content === null)
        throw new Error(`${file} not found`);
    if (content.includes(using))
        return; // idempotent
    const lines = content.split('\n');
    let lastUsing = -1;
    for (let i = 0; i < lines.length; i++) {
        if (lines[i].startsWith('using '))
            lastUsing = i;
    }
    lines.splice(lastUsing + 1, 0, using);
    tree.write(file, lines.join('\n'));
}
/** Adds a ProjectReference to the new module impl into Helm.Host.csproj (idempotent). */
function addHostProjectReference(tree, name) {
    const content = tree.read(HOST_CSPROJ, 'utf-8');
    if (content === null)
        throw new Error(`${HOST_CSPROJ} not found`);
    const ref = `<ProjectReference Include="..\\..\\..\\libs\\api\\Helm.${name}\\Helm.${name}.csproj" />`;
    if (content.includes(`Helm.${name}\\Helm.${name}.csproj`))
        return; // idempotent
    const anchor = `<ProjectReference Include="..\\..\\..\\libs\\api\\Helm.Crm\\Helm.Crm.csproj" />`;
    if (!content.includes(anchor))
        throw new Error(`Anchor project reference not found in ${HOST_CSPROJ}`);
    tree.write(HOST_CSPROJ, content.replace(anchor, `${anchor}\n    ${ref}`));
}
function insertBeforeMarker(tree, file, marker, line) {
    const content = tree.read(file, 'utf-8');
    if (content === null)
        throw new Error(`${file} not found`);
    if (!content.includes(marker))
        throw new Error(`Marker '${marker}' not found in ${file}`);
    if (content.includes(line))
        return; // idempotent
    const updated = content.replace(marker, `${line}\n${marker}`);
    tree.write(file, updated);
}
