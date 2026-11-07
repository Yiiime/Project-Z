// Adapted from volcengine/rtc-aigc-embedded-demo/server/src/AccessToken.py.
// Copyright (2025) Beijing Volcano Engine Technology Ltd. SPDX-License-Identifier: MIT
// License retained in docs/licenses/rtc-token-MIT.txt.
import { createHmac, randomInt } from 'node:crypto';
const u16 = value => { const b = Buffer.alloc(2); b.writeUInt16LE(value); return b; };
const u32 = value => { const b = Buffer.alloc(4); b.writeUInt32LE(value); return b; };
const bytes = value => { const b = Buffer.from(value); return Buffer.concat([u16(b.length), b]); };
export function issueRtcToken({ appId, appKey, roomId, userId, issuedAt = Math.floor(Date.now() / 1000), ttl = 3600, nonce = randomInt(1, 99999999) }) {
  if (!appId || !appKey || !/^[a-zA-Z0-9_@.\-]{1,128}$/.test(roomId) || !/^[a-zA-Z0-9_@.\-]{1,128}$/.test(userId) || ttl < 1 || ttl > 3600) throw Error('invalid_token_parameters');
  const expires = issuedAt + ttl;
  // Publish audio/video/data and subscribe; no account credentials in the client token.
  const privileges = [0,1,2,3,4];
  const message = Buffer.concat([u32(nonce), u32(issuedAt), u32(expires), bytes(roomId), bytes(userId), u16(privileges.length), ...privileges.flatMap(id => [u16(id),u32(expires)])]);
  const signature = createHmac('sha256', appKey).update(message).digest();
  return '001' + appId + Buffer.concat([bytes(message), bytes(signature)]).toString('base64');
}
