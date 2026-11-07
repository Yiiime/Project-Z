export function completeHostSentences(text,closed=false){
  const sentences=[];let start=0;const stack=[],pairs={'“':'”','「':'」','『':'』','（':'）','(':')','[':']'};
  for(let i=0;i<text.length;i++){
    const c=text[i];if(pairs[c])stack.push(pairs[c]);else if(stack.at(-1)===c)stack.pop();
    if(!stack.length&&/[。！？!?\n]/.test(c)){let end=i+1;while(end<text.length&&/[。！？!?]/.test(text[end]))end++;if(end===text.length&&!closed)break;sentences.push(text.slice(start,end));start=end;i=end-1;}
  }
  if(closed&&text.slice(start).trim())sentences.push(text.slice(start));
  return sentences.filter(s=>s.trim());
}
