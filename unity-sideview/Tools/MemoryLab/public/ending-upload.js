const $ = id => document.getElementById(id);
const token = location.hash.slice(1);
let busy=false,draft=null,current=null,shownRun='',polling=false;
function text(tag,value,className){const node=document.createElement(tag);node.textContent=value;if(className)node.className=className;return node;}
async function api(path,body){
  const response=await fetch('/api/ending-upload/'+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json','X-Ending-Upload':token},...(body?{body:JSON.stringify(body)}:{})});
  const result=await response.json();if(!response.ok||result.ok!==true)throw Error(result.error||'暂时无法完成，请重试。');return result;
}
function showDraft(){
  $('preview').hidden=false;$('preview-title').textContent=draft.scenario.title;
  const memories=draft.scenario.memories;
  $('preview-count').textContent=`${memories.length} 段原文 · ${memories.filter(m=>m.zKnows).length} 段 Z 已知`;
  $('preview-context').textContent=draft.scenario.context;
  $('preview-memories').replaceChildren(...memories.map(m=>{const card=text('article','',m.zKnows?'':'unknown');card.append(text('span',`第 ${m.day} 天 · ${m.zKnows?'Z 已知':'Z 未曾获知，作者侧保留'}`,'memory-label'),text('h3',m.title),text('p',m.text));return card;}));
}
function showRun(run){
  if(!run||shownRun===run.id)return;shownRun=run.id;$('result').hidden=false;
  $('result-title').textContent=run.decision.title;$('result-opening').textContent=run.decision.opening;
  $('recollections').replaceChildren(...run.recollections.map(m=>{const item=document.createElement('details');item.append(text('summary',`第 ${m.day} 天 · ${m.title}`),text('p',m.text));return item;}));
  $('result-decision').textContent=run.decision.reason+'\n\n'+run.decision.decision;
  $('result-outcome').textContent=run.outcome.label+'\n'+run.outcome.text;$('last-line').textContent=run.decision.lastLine;
}
async function refresh(){
  if(polling)return;polling=true;
  try{
    const status=await api('status');current=status;
    $('source-name').textContent=`${status.source.sourceName} · 共 ${status.source.memoryCount} 段知情经历，含 ${status.source.liveMemoryCount} 段本局经历`;
    if(!busy&&!draft){
      if(status.source.phase==='deciding')$('notice').textContent='Z 正在结合这些日子，形成自己的选择…';
      else if(status.source.phase==='recalling')$('notice').textContent=`正在回望经历：${status.source.completed} / ${status.source.memoryCount} 段`;
      else if(status.source.phase==='failed')$('error').textContent=status.source.error||'这次生成没有完成，可回游戏重试。';
      else if(status.source.phase==='outdated'){$('notice').textContent='记忆文件已保存。生成期间本局经历发生变化，请回游戏点“以本局经历重新演出”。';$('error').textContent=status.source.error||'';}
      else if(status.source.uploaded&&!status.source.ending){$('notice').textContent='记忆文件已保存，当前经历尚未生成结局。请回游戏点“以本局经历重新演出”。';}
      else if(status.source.ending){$('notice').textContent=status.source.newExperiencesAvailable?'结局已按本次生成开始时的记忆保存。本局之后有新变化，可回游戏重新演出。':'新的结局已保存，游戏中的第七晚会自动更新。';showRun(status.source.ending);}
    }
  }catch(error){$('error').textContent=error.message;}finally{polling=false;}
}
async function loadFile(file){
  if(!file||busy)return;
  if(!/\.(txt|md|json)$/i.test(file.name)){$('error').textContent='请选择 TXT、Markdown 或 JSON 文件。';return;}
  if(file.size>(/\.json$/i.test(file.name)?500000:200000)){$('error').textContent='文件过大，TXT/Markdown 最多200KB，JSON最多500KB。';return;}
  busy=true;draft=null;$('preview').hidden=true;$('error').textContent='';$('notice').textContent='正在在线解析经历。原文不会改写，日期与知情范围会逐段整理…';$('drop-zone').classList.add('busy');$('memory-file').disabled=true;
  try{draft=await api('import',{name:file.name,text:await file.text()});showDraft();$('notice').textContent='请检查整理结果，确认后更新这一局的结局。';}
  catch(error){$('error').textContent=error.message;$('notice').textContent='';}
  finally{busy=false;$('drop-zone').classList.remove('busy');$('memory-file').disabled=false;$('memory-file').value='';}
}
$('memory-file').addEventListener('change',e=>loadFile(e.target.files[0]));
$('drop-zone').addEventListener('dragover',e=>{e.preventDefault();$('drop-zone').classList.add('drag-over');});
$('drop-zone').addEventListener('dragleave',()=>$('drop-zone').classList.remove('drag-over'));
$('drop-zone').addEventListener('drop',e=>{e.preventDefault();$('drop-zone').classList.remove('drag-over');loadFile(e.dataTransfer.files[0]);});
$('use-file').addEventListener('click',async()=>{
  if(!draft||busy)return;busy=true;$('use-file').disabled=true;$('error').textContent='';
  try{await api('use',{draftId:draft.draftId,expectedRevision:draft.expectedRevision});draft=null;$('preview').hidden=true;$('result').hidden=true;shownRun='';$('notice').textContent='记忆已更新，正在自动生成 Z 的新结局。可以回到游戏继续观看。';}
  catch(error){$('error').textContent=error.message;}
  finally{busy=false;$('use-file').disabled=false;void refresh();}
});
$('export').addEventListener('click',async()=>{
  try{const result=await api('export'),url=URL.createObjectURL(new Blob([JSON.stringify(result.scenario,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='Z-uploaded-memories.json';a.click();URL.revokeObjectURL(url);}
  catch(error){$('error').textContent=error.message;}
});
if(!token){$('error').textContent='请从游戏内的“上传 / 更换记忆文件”按钮打开此页。';$('memory-file').disabled=true;$('export').disabled=true;}
else{void refresh();setInterval(refresh,2000);}
