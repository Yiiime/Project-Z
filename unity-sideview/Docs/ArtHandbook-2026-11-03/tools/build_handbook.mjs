import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {marked} from '/Users/bytedance/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/marked/lib/marked.esm.js';

const base=path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const project=path.dirname(path.dirname(base));
const audit=JSON.parse(fs.readFileSync(path.join(base,'evidence/project-audit.json'),'utf8'));
const dataUrl=(p,mime='image/png')=>'data:'+mime+';base64,'+fs.readFileSync(p).toString('base64');
const refDetails=[
  [8,'晴日卧室','蓝色布料、奶白墙面、大窗与干净斜射光'],
  [10,'雨夜卧室','蓝灰大环境与局部桌灯；雨景不等于简单调暗'],
  [7,'黄昏卧室','紫橙过渡、长投影与两人交谈的生活空间'],
  [9,'雨天海堤','湿地反射、低对比远海与安静的阴天层次'],
  [6,'功能工作间','货架、工具与纸箱；风格也能覆盖功能空间'],
  [5,'黄昏灯塔','主地标、横向步道、海天留白与暖色焦点']
];
const images={};
for(const a of audit.maps)images[a.file]=dataUrl(path.join(project,'Assets/Seabreeze/Resources/Art',a.file));
const refs=refDetails.map(([n,title,caption])=>({n,title,caption,url:dataUrl('/Users/bytedance/Desktop/image '+n+'.png')}));
const docs=[
  ['style','01_美术风格指南.md','美术风格指南'],
  ['contract','02_Unity地图资产规范.md','Unity 地图资产规范'],
  ['production','03_批量制作单与提示词.md','批量制作单与提示词']
];
let html=fs.readFileSync(path.join(base,'tools/handbook.template.html'),'utf8');
const sections=docs.map(([id,file,title])=>'<section id="'+id+'" class="document"><div class="section-header"><span>完整规范</span><a href="'+file+'">打开 Markdown</a></div>'+marked.parse(fs.readFileSync(path.join(base,file),'utf8'))+'</section>').join('\n');
const gallery=refs.map(r=>'<figure><img loading="lazy" src="'+r.url+'" alt="参考图 image '+r.n+'：'+r.title+'"><figcaption><strong>'+r.title+'</strong><span>image '+r.n+' · '+r.caption+'</span></figcaption></figure>').join('\n');
const inventory=audit.maps.map(a=>'<tr><td>'+a.file+'</td><td>'+a.width+' × '+a.height+'</td><td>RGB · 无 alpha</td><td>'+(a.file.endsWith('_day.png')?'上午 / 午后':a.file==='cafe.png'?'四时段共用':'傍晚 / 夜间')+'</td></tr>').join('');
const download='data:text/csv;charset=utf-8;base64,'+fs.readFileSync(path.join(base,'地图锚点.csv')).toString('base64');
const payload=JSON.stringify({audit,images}).replaceAll('<','\\u003c');
const values={'__DOCUMENTS__':sections,'__GALLERY__':gallery,'__HERO__':refs[5].url,'__INVENTORY__':inventory,'__PAYLOAD__':payload,'__CSV__':download};
for(const [token,value] of Object.entries(values))html=html.replace(token,()=>value);
fs.writeFileSync(path.join(base,'index.html'),html);
console.log('Built offline handbook: '+(Buffer.byteLength(html)/1024/1024).toFixed(1)+' MiB; '+audit.scenes.reduce((n,s)=>n+s.anchors.length,0)+' anchors.');
