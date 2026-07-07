import {
  Tree,
  formatFiles,
  generateFiles,
  names,
  joinPathFragments,
  logger,
} from '@nx/devkit';
import { execSync } from 'child_process';
import * as path from 'path';

interface HelmModuleSchema {
  name: string;
  schema: string;
}

const PROGRAM_CS = 'apps/api/Helm.Host/Program.cs';
const HOST_CSPROJ = 'apps/api/Helm.Host/Helm.Host.csproj';
const SERVICES_MARKER = '// <helm-modules-services>';
const ENDPOINTS_MARKER = '// <helm-modules-endpoints>';

export default async function helmModuleGenerator(tree: Tree, options: HelmModuleSchema) {
  const name = names(options.name).className; // PascalCase
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
  generateFiles(tree, path.join(__dirname, 'files'), '.', {
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
    if (!tree.exists(gk)) tree.write(gk, '');
  }

  // Host must reference the new module implementation (only Helm.Host references impls — ADR-001).
  addHostProjectReference(tree, name);

  // Register in Host/Program.cs: `using`, then service + endpoint calls before the markers.
  addUsing(tree, PROGRAM_CS, `using Helm.${name};`);
  insertBeforeMarker(
    tree,
    PROGRAM_CS,
    SERVICES_MARKER,
    `builder.Services.Add${name}Module(builder.Configuration);`,
  );
  insertBeforeMarker(
    tree,
    PROGRAM_CS,
    ENDPOINTS_MARKER,
    `app.Map${name}Endpoints();`,
  );

  await formatFiles(tree);

  // dotnet sln add + migration instruction run after the tree is flushed to disk.
  return () => {
    const root = tree.root;
    for (const p of projects) {
      try {
        execSync(`dotnet sln "${joinPathFragments(root, 'Helm.sln')}" add "${joinPathFragments(root, p.sln)}"`, {
          stdio: 'inherit',
          cwd: root,
        });
      } catch (e) {
        logger.warn(`dotnet sln add failed for ${p.sln} — add it manually: dotnet sln Helm.sln add ${p.sln}`);
      }
    }

    logger.info('');
    logger.info(`Helm.${name} scaffolded. Next step — create its initial EF migration:`);
    logger.info('');
    logger.info(
      `  dotnet ef migrations add Init${name} \\\n` +
        `    --project libs/api/Helm.${name}/Helm.${name}.csproj \\\n` +
        `    --startup-project apps/api/Helm.Host/Helm.Host.csproj \\\n` +
        `    --output-dir Migrations`,
    );
    logger.info('');
  };
}

/** Renames every generated `*.template` file to drop the suffix (devkit copies them verbatim). */
function stripTemplateSuffix(tree: Tree) {
  const walk = (dir: string) => {
    for (const child of tree.children(dir)) {
      const full = joinPathFragments(dir, child);
      if (!tree.isFile(full)) {
        walk(full);
      } else if (full.endsWith('.template')) {
        const content = tree.read(full);
        tree.write(full.slice(0, -'.template'.length), content ?? Buffer.from(''));
        tree.delete(full);
      }
    }
  };
  walk('libs/api');
}

/** Inserts a `using` directive after the last existing top-of-file `using` in Program.cs. */
function addUsing(tree: Tree, file: string, using: string) {
  const content = tree.read(file, 'utf-8');
  if (content === null) throw new Error(`${file} not found`);
  if (content.includes(using)) return; // idempotent
  const lines = content.split('\n');
  let lastUsing = -1;
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].startsWith('using ')) lastUsing = i;
  }
  lines.splice(lastUsing + 1, 0, using);
  tree.write(file, lines.join('\n'));
}

/** Adds a ProjectReference to the new module impl into Helm.Host.csproj (idempotent). */
function addHostProjectReference(tree: Tree, name: string) {
  const content = tree.read(HOST_CSPROJ, 'utf-8');
  if (content === null) throw new Error(`${HOST_CSPROJ} not found`);
  const ref = `<ProjectReference Include="..\\..\\..\\libs\\api\\Helm.${name}\\Helm.${name}.csproj" />`;
  if (content.includes(`Helm.${name}\\Helm.${name}.csproj`)) return; // idempotent
  const anchor = `<ProjectReference Include="..\\..\\..\\libs\\api\\Helm.Crm\\Helm.Crm.csproj" />`;
  if (!content.includes(anchor)) throw new Error(`Anchor project reference not found in ${HOST_CSPROJ}`);
  tree.write(HOST_CSPROJ, content.replace(anchor, `${anchor}\n    ${ref}`));
}

function insertBeforeMarker(tree: Tree, file: string, marker: string, line: string) {
  const content = tree.read(file, 'utf-8');
  if (content === null) throw new Error(`${file} not found`);
  if (!content.includes(marker)) throw new Error(`Marker '${marker}' not found in ${file}`);
  if (content.includes(line)) return; // idempotent
  const updated = content.replace(marker, `${line}\n${marker}`);
  tree.write(file, updated);
}
