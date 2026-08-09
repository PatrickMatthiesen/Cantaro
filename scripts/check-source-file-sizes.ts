import { readdir, readFile } from 'node:fs/promises';
import { extname, join, relative } from 'node:path';

const MAX_SOURCE_LINES = 800;
const SOURCE_ROOTS = [
  'src/Cantaro.ClientShared',
  'src/Cantaro.Web',
  'src/Cantaro.BrowserExtension',
];
const IGNORED_DIRECTORIES = new Set(['node_modules', 'dist', '.output']);
const SOURCE_EXTENSIONS = new Set(['.ts', '.tsx']);

async function collectSourceFiles(directory: string): Promise<string[]> {
  const entries = await readdir(directory, { withFileTypes: true });
  const nestedFiles = await Promise.all(entries.map(async (entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return IGNORED_DIRECTORIES.has(entry.name) ? [] : collectSourceFiles(path);
    }

    const isGenerated = entry.name.endsWith('.gen.ts') || entry.name.endsWith('.gen.tsx');
    return SOURCE_EXTENSIONS.has(extname(entry.name)) && !isGenerated ? [path] : [];
  }));

  return nestedFiles.flat();
}

function countLines(source: string): number {
  const content = source.replace(/\s+$/, '');
  return content ? content.split(/\r?\n/).length : 0;
}

const files = (await Promise.all(SOURCE_ROOTS.map(collectSourceFiles))).flat();
const oversizedFiles = (
  await Promise.all(files.map(async (path) => ({
    path: relative(process.cwd(), path),
    lines: countLines(await readFile(path, 'utf8')),
  })))
)
  .filter(({ lines }) => lines > MAX_SOURCE_LINES)
  .sort((left, right) => right.lines - left.lines);

if (oversizedFiles.length > 0) {
  console.error(`Handwritten source files must stay at or below ${MAX_SOURCE_LINES} lines:`);
  for (const file of oversizedFiles) {
    console.error(`- ${file.path}: ${file.lines} lines`);
  }
  process.exit(1);
}

console.log(`Source file size check passed (${files.length} files, maximum ${MAX_SOURCE_LINES} lines).`);
