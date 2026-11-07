/** Actual Markdown compiler tests; offline, no provider or credential access. */
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import { createHash } from 'node:crypto';
import { compileZSkill, renderZPersona } from '../scripts/z-skill.mjs';
import { Z_PERSONA, Z_PERSONA_META, Z_CURRENT_WORLD } from '../worker/z-persona.js';

const root = new URL('../', import.meta.url);
const hash = text => createHash('sha256').update(text, 'utf8').digest('hex');
let checks = 0;
async function check(name, fn) { await fn(); checks++; console.log('PASS ' + name); }

await check('built persona is derived from the original five Markdown files and carries exact provenance', async () => {
  const compiled = await compileZSkill();
  assert.equal(compiled.prompt, Z_PERSONA, 'Run npm run build after changing a selected skill source');
  assert.deepEqual(compiled.meta, Z_PERSONA_META); assert.deepEqual(compiled.world, Z_CURRENT_WORLD);
  assert.deepEqual(compiled.world, { day: 'opening', time: 'night', scene: 'live_room' });
  assert.equal(compiled.meta.sources.length, 5);
  assert.equal(compiled.meta.projectionSha256, hash(compiled.prompt));
  assert.equal(await fs.readFile(new URL('../worker/z-persona.js', import.meta.url), 'utf8'), renderZPersona(compiled));
  for (const source of compiled.meta.sources) {
    assert.equal(source.sha256, hash(await fs.readFile(new URL(source.file, root), 'utf8')));
    assert(source.selections.length > 0); source.selections.forEach(selection => assert.match(selection.sha256, /^[0-9a-f]{64}$/));
  }
});

await check('projection includes core personality, boundaries, complete voice rules and opening reaction only', async () => {
  const compiled = await compileZSkill();
  const voice = (await fs.readFile(new URL('../skills/moon-z-life/references/z-voice.md', import.meta.url), 'utf8')).trim();
  assert(compiled.prompt.includes(voice)); assert(compiled.prompt.includes('不羞辱、不冷暴力、不用发火索取顺从'));
  assert(compiled.prompt.includes('对没弄懂的事刨根问底')); assert(compiled.prompt.includes('遇到明确反证会认错'));
  assert(compiled.prompt.includes('**明日岛**：Z 生活的小岛')); assert(compiled.prompt.includes('他发来的话直接接意思'));
  for (const excluded of [
    '公司制造', '新一代 AI', '撤销', '替代计划', '第0层', '第2层',
    'day_1', 'day_2', 'day_3', 'day_4', 'day_5', 'day_6', 'day_7', 'Day 1',
    '行动点', '任务链', '剧情标志', 'z_state_update', 'suspicion_level',
    '他收到了上岛邀请', '邀请是谁安排的', '掌握后台信息', '反对离开',
  ]) assert(!compiled.prompt.includes(excluded), `Protected source content leaked: ${excluded}`);
});

const fixture = await fs.mkdtemp(path.join(os.tmpdir(), 'z-persona-'));
try {
  for (const source of Z_PERSONA_META.sources) {
    const filename = path.join(fixture, source.file); await fs.mkdir(path.dirname(filename), { recursive: true });
    await fs.copyFile(new URL(source.file, root), filename);
  }
  await check('unselected future sections never enter the projection, even if the original source changes', async () => {
    const filename = path.join(fixture, 'skills/moon-z-life/SKILL.md');
    const original = await fs.readFile(filename, 'utf8');
    await fs.writeFile(filename, original.replace('你**不知道**：', 'UNREVEALED_STORY_MARKER\n\n你**不知道**：'));
    const compiled = await compileZSkill(fixture);
    assert.equal(compiled.prompt, Z_PERSONA); assert(!compiled.prompt.includes('UNREVEALED_STORY_MARKER'));
    assert.notEqual(compiled.meta.sources[0].sha256, Z_PERSONA_META.sources[0].sha256);
    assert.equal(compiled.meta.projectionSha256, Z_PERSONA_META.projectionSha256);
    await fs.writeFile(filename, original);
  });
  await check('editing an allowed character passage changes the runtime projection and its hash', async () => {
    const filename = path.join(fixture, 'skills/moon-z-life/references/z-profile.md');
    const original = await fs.readFile(filename, 'utf8');
    await fs.writeFile(filename, original.replace('能接受对方觉得无聊。', '能接受对方觉得无聊。也愿意先听别人说完。'));
    const compiled = await compileZSkill(fixture);
    assert(compiled.prompt.includes('也愿意先听别人说完。')); assert.notEqual(compiled.meta.projectionSha256, Z_PERSONA_META.projectionSha256);
    await fs.writeFile(filename, original);
  });
  await check('missing, duplicate or expanded protected sections fail closed instead of bundling the whole skill', async () => {
    const filename = path.join(fixture, 'skills/moon-z-life/references/world.md');
    const original = await fs.readFile(filename, 'utf8');
    await fs.writeFile(filename, original.replace('## 第1层 小岛世界（Z 的认知层）', '## renamed'));
    await assert.rejects(compileZSkill(fixture), /expected one reviewed section/);
    await fs.writeFile(filename, original + '\n## 第1层 小岛世界（Z 的认知层）\n重复\n');
    await assert.rejects(compileZSkill(fixture), /expected one reviewed section/);
    await fs.writeFile(filename, original.replace('Z 生活的小岛。', 'Z 生活的小岛。day_7 的未发生内容。'));
    await assert.rejects(compileZSkill(fixture), /protected content in reviewed excerpt/);
    await fs.writeFile(filename, original);
  });
} finally { await fs.rm(fixture, { recursive: true, force: true }); }
console.log(`\n${checks} persona regression scenarios passed (original Markdown, no paid API calls).`);
