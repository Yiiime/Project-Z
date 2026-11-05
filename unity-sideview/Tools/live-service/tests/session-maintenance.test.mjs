import {test} from 'node:test';
import assert from 'node:assert/strict';
import {startNeteaseSessionMaintenance} from '../server/song-library/netease-auth.mjs';
test('session maintenance runs on start and shutdown waits for ongoing work',async()=>{let done,start;const begun=new Promise(r=>start=r),wait=new Promise(r=>done=r);const events=[];const h=startNeteaseSessionMaintenance({maintain:async()=>{start();await wait;return {status:'authenticated'};},onStatus:s=>events.push(s.status)});await begun;const stop=h.stop();done();await stop;assert.deepEqual(events,['authenticated']);});
test('maintenance failures do not crash host or claim login expiry',async()=>{const events=[];const h=startNeteaseSessionMaintenance({maintain:async()=>{throw Error('network');},onStatus:s=>events.push(s)});await h.stop();assert.equal(events[0].status,'check_failed');assert.equal(events[0].requiresLogin,false);});
