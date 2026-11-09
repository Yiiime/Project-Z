import {test} from 'node:test';
import assert from 'node:assert/strict';
import {gameReturnUrl,mountGameReturn} from '../ui/game-return.js';
test('only fixed protocol with valid bounded session',()=>{
 assert.equal(gameReturnUrl('?from=unity&returnSession='+'a'.repeat(32)),'projectz-live://return?session='+'a'.repeat(32));
 for(const q of ['', '?returnUrl=https://evil.example', '?from=unity&returnSession=bad', '?from=else&returnSession='+'a'.repeat(32)])assert.equal(gameReturnUrl(q),'projectz-live://return');
});
test('return shuts down media before navigating and retry does not duplicate cleanup',()=>{
 const actions=[],label={};let click;
 const button={setAttribute(){},querySelector:()=>label,set onclick(value){click=value;}};
 const location={search:'?from=unity&returnSession='+'b'.repeat(32),set href(value){actions.push(value);}};
 let retry;mountGameReturn({window:{location},button,shutdown:()=>actions.push('stop'),showNotice:fn=>{retry=fn;actions.push('notice');}});
 assert.equal(actions.length,0);click();assert.deepEqual(actions.slice(0,2),['stop','notice']);retry();
 assert.equal(actions.filter(x=>x==='stop').length,1);assert.equal(actions.at(-1),'projectz-live://return?session='+'b'.repeat(32));assert.equal(label.textContent,'返回游戏');
});
test('blocked navigation leaves retry notice available',()=>{
 let notice=false;const button={setAttribute(){},querySelector(){}};
 const window={location:{search:'',set href(v){throw Error('blocked');}}};
 const bridge=mountGameReturn({window,button,shutdown(){},showNotice(){notice=true;}});
 assert.doesNotThrow(()=>bridge.requestReturn());assert.ok(notice);
});
