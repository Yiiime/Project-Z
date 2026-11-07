// Build-time only. Keep the original imported specs intact and out of browser JS.
import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
export async function loadAgentBundle(root) {
  const read = relative => fs.readFile(path.join(root, relative), 'utf8');
  const provenance = JSON.parse(await read('main-agent/import-manifest.json'));
  for (const file of provenance.files) {
    if (!/^(?:main-agent|skills)\/[\w./-]+$/.test(file.path) || file.path.includes('..')) throw Error('Invalid agent source path');
    const hash = createHash('sha256').update(await fs.readFile(path.join(root, file.path))).digest('hex');
    if (hash !== file.sha256) throw Error(`Imported agent source changed: ${file.path}`);
  }
  const sourcePolicy = JSON.parse(await read('main-agent/config/skills-scheduling.json'));
  // Product runtime adaptation: keep the imported source and its hashes intact.
  // NPC presence now continues throughout the live session on one shared cadence.
  const {phases: _sourceAudiencePhases, ...audiencePolicy} = sourcePolicy.audience;
  const policy = {...sourcePolicy,
    host: {...sourcePolicy.host, toolPlanningTimeoutMs: 20000},
    scheduling: {...sourcePolicy.scheduling, pauseAudienceWhenPlayerWaiting: false, maxAudienceRequests: 2},
    audience: {...audiencePolicy, mode: 'continuous', intervalMs: 1500, generationDeadlineMs: 3500, candidateTtlMs: 12000, publishGapMs: 2500, maxConcurrent: 2, maxPoolSize: 3, handoff: true, maxItems: 1, maxAdvances: 1}};
  const registry = JSON.parse(await read('main-agent/config/skill-registry.json'));
  const releasePrompt = '\n'+await read('main-agent/prompts/singing-only.txt');
  const hostPrompt = await read('main-agent/prompts/main-agent-skills.txt')+releasePrompt;
  const skills = {};
  for (const entry of registry.skills) {
    skills[entry.id] = {instructions: await read(entry.entry), contract: await read(entry.contract)};
  }
  const conversationPrompt = await read('main-agent/prompts/conversation-host.txt')+releasePrompt;
  const callFiles=['SKILL.md','references/companionship.md','references/audience.md','references/talents.md','references/story.md'];
  const callPrompt=(await Promise.all(callFiles.map(p=>read('skills/call-companion/'+p)))).join('\n')+releasePrompt;
  const topicPrompt = await read('main-agent/prompts/topic-progress.txt');
  const reviewPrompt = await read('skills/audience-reaction/references/dialogue-review.md');
  const review = {prompt:reviewPrompt, version:createHash('sha256').update(reviewPrompt+skills['audience-reaction'].instructions+skills['audience-reaction'].contract+conversationPrompt+await read('worker/agent-director.js')).digest('hex')};
  return {policy, registry, hostPrompt, conversationPrompt, callPrompt, topicPrompt, skills, review, version: provenance.bundle};
}
export async function writeAgentBundle(root) {
  const bundle = await loadAgentBundle(root);
  await fs.writeFile(path.join(root, 'worker/agent-bundle-data.js'), '// Generated from hash-verified imported specifications. Server only.\nexport const AGENT_BUNDLE = '+JSON.stringify(bundle)+';\n');
  return bundle;
}
