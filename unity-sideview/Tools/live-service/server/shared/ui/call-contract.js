// Only these small, optional scenes may advance. They do not alter Z's main story.
export const CALL_STORY={id:'tonight-postcard',title:'留一张今晚的明信片',steps:[
 {question:'今晚这张明信片，你想留住什么？',choices:[{id:'rest',label:'一小段安静',result:'为今晚的明信片选了安静的主题。'},{id:'spark',label:'一点新鲜事',result:'为今晚的明信片选了探索的主题。'},{id:'skip',label:'先不玩，继续聊',result:'选择先聊天，不继续明信片。'}]},
 {question:'给这张只属于今晚的明信片取个名字吧。',choices:[{id:'moon',label:'月光暂停键',result:'将今晚的口头明信片命名为「月光暂停键」。'},{id:'wind',label:'晚风的新发现',result:'将今晚的口头明信片命名为「晚风的新发现」。'},{id:'skip',label:'留白也很好',result:'将今晚的口头明信片留白。'}]}
]};
