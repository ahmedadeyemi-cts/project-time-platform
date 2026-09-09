import assert from 'node:assert/strict';
import { assertImportedTargetMarkers, resolveEffectiveBuildTargetsFromSources } from '../src/frontend/project-time-web/scripts/read-effective-build-targets.mjs';

const platform = 'src/backend/ProjectTime.Api/build/PlatformRuntime.targets';
const sow = 'src/backend/ProjectTime.Api/build/Module025SowSell.targets';
const wrapper = `<Project>\n  <Import Project="$(MSBuildProjectDirectory)/build/PlatformRuntime.targets" />\n  <Import Project="$(MSBuildProjectDirectory)/build/Module025SowSell.targets" />\n</Project>`;
const sources = {
  [platform]: 'GenerateWithPrivateTargetAsync\nExternalFactCodes: externalFactCodes\nDestinationFiles="$(CelarAiTimesheetGenerated)"',
  [sow]: 'GenerateModule025SowSellSources\nCompile Include="$(Module025SowSellGenerated)"'
};

const resolved = resolveEffectiveBuildTargetsFromSources(wrapper, sources);
assert.deepEqual(resolved.imports, [platform, sow]);
assert.match(resolved.text, /GenerateWithPrivateTargetAsync/);
assert.doesNotThrow(() => assertImportedTargetMarkers(resolved.sources, {
  [platform]: ['GenerateWithPrivateTargetAsync'],
  [sow]: ['Compile Include="$(Module025SowSellGenerated)"']
}));

assert.throws(
  () => resolveEffectiveBuildTargetsFromSources(wrapper.replace('Module025SowSell.targets', 'Missing.targets'), sources),
  /missing required direct import/
);
assert.throws(
  () => resolveEffectiveBuildTargetsFromSources(wrapper, { ...sources, 'src/backend/ProjectTime.Api/build/Disconnected.targets': 'hidden protection' }),
  /disconnected/
);
assert.throws(
  () => assertImportedTargetMarkers({ [platform]: 'GenerateWithPrivateTargetAsync', [sow]: sources[sow] }, {
    [platform]: ['missing compiler protection']
  }),
  /missing required compiler protection/
);

console.log('EFFECTIVE_BUILD_TARGETS_IMPORTS=PASSED');
console.log('EFFECTIVE_BUILD_TARGETS_NEGATIVE_CASES=PASSED');
