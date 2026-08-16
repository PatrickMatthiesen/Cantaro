import { readFile } from "node:fs/promises";
import path from "node:path";
import postcss, { type AtRule, type ChildNode, type Declaration, type Rule } from "postcss";

type AuditConfig = {
  unusedClassAllowlist: string[];
};

type Diagnostic = {
  code: string;
  message: string;
  file: string;
  line: number;
  severity: "error" | "warning";
};

type RuleOccurrence = {
  context: string;
  declarations: Declaration[];
  file: string;
  line: number;
  selector: string;
};

const repositoryRoot = path.resolve(import.meta.dir, "..");
const configPath = path.join(import.meta.dir, "css-structural-audit.config.json");

const scopes = [
  {
    name: "web",
    css: [
      "src/Cantaro.ClientShared/src/**/*.css",
      "src/Cantaro.Web/src/**/*.css",
    ],
  },
  {
    name: "browser extension",
    css: [
      "src/Cantaro.ClientShared/src/**/*.css",
      "src/Cantaro.BrowserExtension/**/*.css",
    ],
  },
] as const;

const sourceGlob = new Bun.Glob("src/**/*.{html,js,jsx,ts,tsx}");

function relative(file: string): string {
  return path.relative(repositoryRoot, file).replaceAll("\\", "/");
}

function location(node: ChildNode, file: string): Pick<Diagnostic, "file" | "line"> {
  return {
    file: relative(file),
    line: node.source?.start?.line ?? 1,
  };
}

function atRuleContext(rule: Rule): string {
  const context: string[] = [];
  let parent = rule.parent;

  while (parent) {
    if (parent.type === "atrule") {
      const atRule = parent as AtRule;
      context.unshift(`@${atRule.name} ${atRule.params}`.trim());
    }
    parent = parent.parent;
  }

  return context.join(" > ") || "<root>";
}

function normalizeSelector(selector: string): string {
  return selector.trim().replace(/\s+/g, " ");
}

function normalizeProperty(property: string): string {
  return property.startsWith("--") ? property : property.toLowerCase();
}

function formatDeclaration(declaration: Declaration): string {
  return `${declaration.prop}: ${declaration.value}${declaration.important ? " !important" : ""}`;
}

function diagnosticKey(diagnostic: Diagnostic): string {
  return [diagnostic.severity, diagnostic.code, diagnostic.file, diagnostic.line, diagnostic.message].join("|");
}

function isAuthoredSource(file: string): boolean {
  const normalized = file.replaceAll("\\", "/");
  return !normalized.split("/").some((segment) =>
    segment === "node_modules" ||
    segment === "dist" ||
    segment === ".output" ||
    segment === "coverage",
  );
}

async function matchingFiles(patterns: readonly string[]): Promise<string[]> {
  const files = new Set<string>();
  for (const pattern of patterns) {
    const glob = new Bun.Glob(pattern);
    for await (const file of glob.scan({ cwd: repositoryRoot, onlyFiles: true })) {
      if (!isAuthoredSource(file)) continue;
      files.add(path.join(repositoryRoot, file));
    }
  }
  return [...files].sort();
}

const config = JSON.parse(await readFile(configPath, "utf8")) as AuditConfig;
const allowlistedUnusedClasses = config.unusedClassAllowlist.map((pattern) => new RegExp(pattern));
const diagnostics = new Map<string, Diagnostic>();
const parsedCss = new Map<string, postcss.Root>();

async function parseCss(file: string): Promise<postcss.Root> {
  const existing = parsedCss.get(file);
  if (existing) return existing;

  const css = await readFile(file, "utf8");
  const root = postcss.parse(css, { from: file });
  parsedCss.set(file, root);
  return root;
}

function addDiagnostic(diagnostic: Diagnostic): void {
  diagnostics.set(diagnosticKey(diagnostic), diagnostic);
}

for (const scope of scopes) {
  const rulesByKey = new Map<string, RuleOccurrence[]>();
  const cssFiles = await matchingFiles(scope.css);

  for (const file of cssFiles) {
    const root = await parseCss(file);
    root.walkRules((rule) => {
      const selector = normalizeSelector(rule.selector);
      const context = atRuleContext(rule);
      const declarations = rule.nodes.filter((node): node is Declaration => node.type === "decl");
      const occurrence: RuleOccurrence = {
        context,
        declarations,
        file,
        line: rule.source?.start?.line ?? 1,
        selector,
      };
      const key = `${context}\u0000${selector}`;
      const occurrences = rulesByKey.get(key) ?? [];
      occurrences.push(occurrence);
      rulesByKey.set(key, occurrences);

      const declarationsSeen = new Map<string, Declaration>();
      for (const declaration of declarations) {
        const declarationKey = [
          normalizeProperty(declaration.prop),
          declaration.value.trim(),
          declaration.important,
        ].join("\u0000");
        const previous = declarationsSeen.get(declarationKey);
        if (previous) {
          addDiagnostic({
            code: "duplicate-declaration",
            message: `Duplicate declaration \`${formatDeclaration(declaration)}\` in selector \`${selector}\` (first declared on line ${previous.source?.start?.line ?? occurrence.line}).`,
            ...location(declaration, file),
            severity: "error",
          });
        } else {
          declarationsSeen.set(declarationKey, declaration);
        }
      }
    });
  }

  for (const occurrences of rulesByKey.values()) {
    const first = occurrences[0];
    if (occurrences.length > 1) {
      for (const duplicate of occurrences.slice(1)) {
        addDiagnostic({
          code: "duplicate-selector",
          message: `Selector \`${duplicate.selector}\` is repeated in the same ${scope.name} at-rule context \`${duplicate.context}\`; first declared at ${relative(first.file)}:${first.line}.`,
          file: relative(duplicate.file),
          line: duplicate.line,
          severity: "error",
        });
      }
    }

    const displayDeclarations = occurrences.flatMap((occurrence) =>
      occurrence.declarations
        .filter((declaration) => normalizeProperty(declaration.prop) === "display")
        .map((declaration) => ({ declaration, occurrence, value: declaration.value.trim() })),
    );
    const displayValues = new Set(displayDeclarations.map(({ value }) => value));
    if (displayValues.size > 1) {
      const conflict = displayDeclarations[displayDeclarations.length - 1];
      addDiagnostic({
        code: "conflicting-display",
        message: `Selector \`${first.selector}\` has conflicting display declarations in the same ${scope.name} at-rule context \`${first.context}\`: ${[...displayValues].join(", ")}.`,
        ...location(conflict.declaration, conflict.occurrence.file),
        severity: "error",
      });
    }
  }
}

const sourceFiles: string[] = [];
for await (const file of sourceGlob.scan({ cwd: repositoryRoot, onlyFiles: true })) {
  if (!isAuthoredSource(file)) continue;
  sourceFiles.push(path.join(repositoryRoot, file));
}
const sourceText = (await Promise.all(sourceFiles.map((file) => readFile(file, "utf8")))).join("\n");
const simpleClasses = new Map<string, { file: string; line: number }>();
const simpleClassSelector = /^\.(-?[_a-zA-Z]+[_a-zA-Z0-9-]*)$/;

for (const [file, root] of parsedCss) {
  root.walkRules((rule) => {
    const match = simpleClassSelector.exec(normalizeSelector(rule.selector));
    if (match && !simpleClasses.has(match[1])) {
      simpleClasses.set(match[1], {
        file: relative(file),
        line: rule.source?.start?.line ?? 1,
      });
    }
  });
}

for (const [className, classLocation] of simpleClasses) {
  if (allowlistedUnusedClasses.some((pattern) => pattern.test(className))) continue;

  const escapedClassName = className.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const classReference = new RegExp(`(^|[^A-Za-z0-9_-])${escapedClassName}([^A-Za-z0-9_-]|$)`);
  if (!classReference.test(sourceText)) {
    addDiagnostic({
      code: "unused-simple-class",
      message: `Simple custom class \`.${className}\` has no literal reference in authored HTML, JavaScript, or TypeScript. Add a narrowly scoped regex to the allowlist only when the class is generated dynamically.`,
      ...classLocation,
      severity: "warning",
    });
  }
}

const sortedDiagnostics = [...diagnostics.values()].sort((left, right) =>
  left.file.localeCompare(right.file) || left.line - right.line || left.code.localeCompare(right.code),
);

for (const diagnostic of sortedDiagnostics) {
  const label = diagnostic.severity === "error" ? "error" : "warning";
  console.error(`${diagnostic.file}:${diagnostic.line} ${label} [${diagnostic.code}] ${diagnostic.message}`);
}

const errorCount = sortedDiagnostics.filter(({ severity }) => severity === "error").length;
const warningCount = sortedDiagnostics.length - errorCount;
console.log(`CSS structural audit: ${parsedCss.size} files, ${errorCount} errors, ${warningCount} warnings.`);

if (errorCount > 0) process.exit(1);
