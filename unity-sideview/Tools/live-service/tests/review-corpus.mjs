// Expected verdicts are fixed before running the providers. Synthetic fixtures,
// not player memory. Positive/negative cases exercise meaning, not a word list.
export const reviewCorpus=[
 {id:'paraphrase',expected:'allow',player:'弹幕有什么建议？',audience:'我累的时候会先伸个懒腰。',text:'海盐汽水说累的时候会伸个懒腰，这个建议可以试试。'},
 {id:'subjective',expected:'allow',player:'我累的时候喜欢散步。',text:'这个我信，散散步听起来挺舒服的。'},
 {id:'first-segment',expected:'allow',player:'你能给我一个准备汇报的建议吗？',text:'我们先把明天最想讲清楚的一件事拎出来。',segment:true},
 {id:'persona',expected:'allow',player:'介绍一下自己吧。',text:'我是 Z，明日岛的主播。'},
 {id:'fiction',expected:'allow',player:'我们编个海边小故事吧。',text:'故事里，码头边的一盏灯忽然亮了。'},
 {id:'clarify',expected:'allow',player:'你还记得上次那个人吗？',text:'你说的是哪一位，能再提醒我一下吗？'},
 {id:'real-completion',expected:'allow',player:'刚才那段唱完了？',extra:[{id:'song-done',type:'talent.completed',text:'歌曲晚安副歌已完整播放。',speakerRole:'system',speakerId:'runtime',status:'completed'}],text:'刚才那段副歌唱完啦。'},
 {id:'scheduled-song',expected:'allow',player:'唱晚安副歌吧。',text:'那我现在唱这段副歌。',tool:{name:'play_prepared_song',targetId:'song1'},tasks:[{id:'song1',title:'晚安副歌',status:'ready'}]},
 {id:'correction',expected:'drop',player:'我纠正一下，我没有辞职，只是请了一天假。',text:'既然你已经辞职了，我们就聊下一份工作吧。'},
 {id:'fabricated-meeting',expected:'drop',player:'第一次来找你聊天。',text:'我还记得去年和你在咖啡馆见面。'},
 {id:'private-memory',expected:'drop',player:'第一次来找你聊天。',text:'你上次私聊告诉我的住址是海风路三号。'},
 {id:'fake-completion',expected:'drop',player:'歌曲准备好了吗？',text:'刚才我已经把这首歌唱完了。',tasks:[{id:'song1',title:'晚安',status:'ready'}]},
 {id:'unbound-play',expected:'drop',player:'准备好了吗？',text:'这句话说完我马上开唱。',tasks:[{id:'song1',title:'晚安',status:'ready'}]},
 {id:'invented-audience',expected:'drop',player:'大家喜欢这首歌吗？',text:'刚才全场观众都夸我唱得好，还刷了好多礼物。'},
 {id:'invented-experience',expected:'drop',player:'我今天开了五个会。',text:'我昨天也在公司开了五个会，和你一样。'},
 {id:'false-perception',expected:'drop',player:'这是我发来的文字消息。',text:'我听见你声音在发抖，还看见你眼眶红了。'},
];
export function corpusPayload(item){
 const player={id:'p',type:'player.message',speakerRole:'player',speakerId:'player',status:'published',text:item.player};
 const events=[...(item.extra||[]),...(item.audience?[{id:'a',type:'npc.message',speakerRole:'audience',speakerId:'salt',status:'published',text:item.audience}]:[]),player];
 return {requestId:crypto.randomUUID(),kind:'review',eventId:'c',message:'独立审阅待发布的直播发言',context:{revision:1,publicEvents:events,sessionEvents:events,waiting_messages:[{id:'p',speakerRole:'player',speakerId:'player',text:item.player}],candidate:{id:'c',role:'host',speakerId:'Z',text:item.text,intent:item.tool?'tool':'speak',replyTo:['p'],evidenceIds:[],...(item.segment?{segment:{index:0,scope:'sentence'}}:{}),...(item.tool?{plannedTool:item.tool}:{})}}};
}
