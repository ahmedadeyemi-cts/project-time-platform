import fs from 'node:fs';
import path from 'node:path';

const backendProject = 'src/backend/ProjectTime.Api';
export const DEFAULT_TARGET_IMPORTS = Object.freeze([
  `${backendProject}/build/PlatformRuntime.targets`,
  `${backendProject}/build/Module025SowSell.targets`
]);

const importPattern = /<Import\s+Project="\$\(MSBuildProjectDirectory\)\/(build\/[^"']+\.targets)"\s*\/?>/g;

function normalize(value) {
  return value.replaceAll('\\', '/');
}

export function resolveEffectiveBuildTargetsFromSources(directoryTargets, importedSources, expectedImports = DEFAULT_TARGET_IMPORTS) {
  const imports = [...directoryTargets.matchAll(importPattern)].map((match) => `${backendProject}/${match[1]}`);
  const expected = expectedImports.map(normalize);
  const actual = imports.map(normalize);

  for (const required of expected) {
    if (!actual.includes(required)) {
      throw new Error(`Directory.Build.targets is missing required direct import ${required}`);
    }
  }

  const sourceKeys = Object.keys(importedSources).map(normalize);
  for (const imported of actual) {
    if (!sourceKeys.includes(imported)) {
      throw new Error(`Directory.Build.targets imports ${imported}, but its target source is unavailable`);
    }
  }
  for (const source of sourceKeys) {
    if (!actual.includes(source)) {
      throw new Error(`Target source ${source} is disconnected; it is not directly imported by Directory.Build.targets`);
    }
  }

  return {
    imports: actual,
    sources: Object.fromEntries(Object.entries(importedSources).map(([key, value]) => [normalize(key), value])),
    text: [directoryTargets, ...actual.map((key) => importedSources[key] ?? importedSources[Object.keys(importedSources).find((candidate) => normalize(candidate) === key)])].join('\n')
  };
}

export function assertImportedTargetMarkers(sources, requirements) {
  for (const [target, markers] of Object.entries(requirements)) {
    const source = sources[normalize(target)] ?? sources[target];
    if (typeof source !== 'string') throw new Error(`Required target source ${target} was not resolved`);
    for (const marker of markers) {
      if (!source.includes(marker)) throw new Error(`${target} is missing required compiler protection ${marker}`);
    }
  }
}

export function readEffectiveBuildTargets(repositoryRoot, expectedImports = DEFAULT_TARGET_IMPORTS) {
  const read = (relative) => fs.readFileSync(path.join(repositoryRoot, relative), 'utf8');
  const directoryTargets = read(`${backendProject}/Directory.Build.targets`);
  const importedSources = Object.fromEntries(expectedImports.map((relative) => [relative, read(relative)]));
  const resolved = resolveEffectiveBuildTargetsFromSources(directoryTargets, importedSources, expectedImports);
  assertImportedTargetMarkers(resolved.sources, {
    [`${backendProject}/build/PlatformRuntime.targets`]: [
      'GenerateWithPrivateTargetAsync',
      'ExternalFactCodes: externalFactCodes',
      'DestinationFiles="$(CelarAiTimesheetGenerated)"'
    ],
    [`${backendProject}/build/Module025SowSell.targets`]: [
      'GenerateModule025SowSellSources',
      'Compile Include="$(Module025SowSellGenerated)"'
    ]
  });
  return resolved;
}
