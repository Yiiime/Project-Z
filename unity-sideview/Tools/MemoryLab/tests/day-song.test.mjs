import test from 'node:test';
import assert from 'node:assert/strict';
import {dayConversation} from '../lib/daytime.mjs';
import {DaySongDispatch} from '../lib/day-song-dispatch.mjs';

const title='夏夜最后的烟火';
function dialogue(...texts){
  return texts.map((text,i)=>({id:'line-'+i,seq:i+1,hour:9,channel:'private_player_chat',conversationId:'chat',
    speaker:i%2?'z':'player',recipient:i%2?'player':'z',visible:['player','z'],text,daySongBridge:true}));
}
test('only the accepted, delivered request can dispatch; only public facts cross the boundary',()=>{
  const rows=dialogue('因为最近失眠，想听《夏夜最后的烟火》，原因别讲。','好呀，今晚唱给你听。',
    '明天想学咖啡拉花，具体住址是蓝信封巷。','拉花可以先试试。');
  const result=dayConversation(rows,1);
  assert.deepEqual(result,{songs:[{title,sourceId:'line-0',dispatch:true}],subjects:['咖啡拉花']});
  assert(!JSON.stringify(result).match(/失眠|蓝信封/));
  assert.deepEqual(dayConversation(rows,2),{songs:[],subjects:[]});
  assert.deepEqual(dayConversation(dialogue('想听《夏夜最后的烟火》。'),1).songs,[]);
  for(const [p,z] of [['喜欢听《夏夜最后的烟火》。','我也喜欢听。'],['想听《夏夜最后的烟火》。','这首我不能答应。']])
    assert.deepEqual(dayConversation(dialogue(p,z),1).songs,[{title}]);
  assert.deepEqual(dayConversation(dialogue('想听《夏夜最后的烟火》。','好呀。','这首歌别在直播提。'),1).songs,[]);
  const repeated=dialogue('想听夏夜最后的烟火','这首《夏夜最后的烟火》我会练。','能帮我学这首吗？','行啊。');
  assert.equal(dayConversation(repeated,1).songs[0].sourceId,'line-0');
});
test('sync replay and concurrent checkpoints dispatch the same receipt once',async t=>{
  let done,calls=0;
  const queue=new DaySongDispatch(async()=>{calls++;await new Promise(r=>done=r);});t.after(()=>queue.close());
  const songs=[{title,sourceId:'p1',dispatch:true}];
  queue.update('w',1,songs);queue.update('w',1,songs);done();await new Promise(setImmediate);
  queue.update('w',1,songs);assert.equal(calls,1);
  queue.update('w',1,[{title}]);assert.equal(calls,1);
});
test('an accepted practice plan is distinct from guaranteed performance; veto in a later conversation follows the song',()=>{
 const rows=dialogue('学夏夜最后的烟火','《夏夜最后的烟火》我先把调子和歌词顺一顺，练得怎么样不敢打包票。');
 rows[1].daySongSource=rows[0].id;rows[1].daySongReply='accept';
 assert.equal(dayConversation(rows,1).songs[0].sourceId,'line-0');
 rows.push({...rows[0],id:'later',seq:3,conversationId:'another',text:'那首歌别在直播里说。'});
 assert.deepEqual(dayConversation(rows,1).songs,[]);
});
