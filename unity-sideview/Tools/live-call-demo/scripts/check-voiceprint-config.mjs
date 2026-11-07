import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadVoiceprintConfig, publicVoiceprintConfig } from '../server/voiceprint/config.mjs';

const root = process.env.Z_DEMO_CONFIG_DIR ? path.resolve(process.env.Z_DEMO_CONFIG_DIR) : fileURLToPath(new URL('../', import.meta.url));
try {
  const status = publicVoiceprintConfig(await loadVoiceprintConfig(root));
  console.log(JSON.stringify({ ...status, message: '仅检查本地配置，不代表云端权限或当前会话已生效；needsEnrollment 表示首次连麦还需登记真实玩家声音。' }, null, 2));
  // Missing a player profile is the normal first-use enrollment state.
  if (!status.credentialsConfigured) process.exitCode = 1;
} catch {
  console.error('无法读取声纹配置文件；未输出配置内容。'); process.exitCode = 1;
}
