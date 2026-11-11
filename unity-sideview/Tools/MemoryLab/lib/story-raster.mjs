import {deflateSync} from 'node:zlib';
const size=512;
function color(value){if(typeof value!=='string'||!/^#[0-9a-fA-F]{6}$/.test(value))throw Error('绘图颜色无效');return [1,3,5].map(i=>parseInt(value.slice(i,i+2),16));}
function number(value){if(!Number.isFinite(value)||value<0||value>512)throw Error('绘图坐标越界');return value;}
export function validateDrawing(drawing){
  if(!Array.isArray(drawing?.shapes)||drawing.shapes.length<3||drawing.shapes.length>64)throw Error('绘图需要3至64个形状');
  return drawing.shapes.map(s=>{
    const fill=color(s.fill);
    if(s.type==='ellipse'||s.type==='rect')return {type:s.type,fill,x:number(s.x),y:number(s.y),w:number(s.w),h:number(s.h)};
    if(s.type==='polygon'&&Array.isArray(s.points)&&s.points.length>=3&&s.points.length<=24)return {type:s.type,fill,points:s.points.map(p=>{if(!Array.isArray(p)||p.length!==2)throw Error('绘图点无效');return p.map(number);})};
    throw Error('不支持的绘图形状');
  });
}
function crc32(b){let crc=0xffffffff;for(const byte of b){crc^=byte;for(let n=0;n<8;n++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return (crc^0xffffffff)>>>0;}
function chunk(type,data){const t=Buffer.from(type),n=Buffer.alloc(4),c=Buffer.alloc(4);n.writeUInt32BE(data.length);c.writeUInt32BE(crc32(Buffer.concat([t,data])));return Buffer.concat([n,t,data,c]);}
export function renderDrawing(drawing){
  const shapes=validateDrawing(drawing),pixels=Buffer.alloc(size*size*4);
  for(const s of shapes){
    const bounds=s.type==='polygon'?[Math.min(...s.points.map(p=>p[0])),Math.min(...s.points.map(p=>p[1])),Math.max(...s.points.map(p=>p[0])),Math.max(...s.points.map(p=>p[1]))]:[s.x,s.y,s.x+s.w,s.y+s.h];
    for(let y=Math.max(0,Math.floor(bounds[1]));y<Math.min(size,Math.ceil(bounds[3]));y++)for(let x=Math.max(0,Math.floor(bounds[0]));x<Math.min(size,Math.ceil(bounds[2]));x++){
      let inside=s.type==='rect';
      if(s.type==='ellipse')inside=((x+.5-s.x-s.w/2)/(s.w/2))**2+((y+.5-s.y-s.h/2)/(s.h/2))**2<=1;
      if(s.type==='polygon'){inside=false;for(let i=0,j=s.points.length-1;i<s.points.length;j=i++){
        const a=s.points[i],b=s.points[j];if((a[1]>y+.5)!==(b[1]>y+.5)&&(x+.5)<(b[0]-a[0])*(y+.5-a[1])/(b[1]-a[1])+a[0])inside=!inside;
      }}
      if(inside){let i=(y*size+x)*4;pixels[i]=s.fill[0];pixels[i+1]=s.fill[1];pixels[i+2]=s.fill[2];pixels[i+3]=255;}
    }
  }
  let opaque=0;for(let i=3;i<pixels.length;i+=4)if(pixels[i])opaque++;
  if(opaque<size*size*.01||opaque>size*size*.88)throw Error('道具需要清晰主体和透明边缘');
  const rows=Buffer.alloc(size*(size*4+1));for(let y=0;y<size;y++)pixels.copy(rows,y*(size*4+1)+1,y*size*4,(y+1)*size*4);
  const header=Buffer.alloc(13);header.writeUInt32BE(size,0);header.writeUInt32BE(size,4);header[8]=8;header[9]=6;
  return Buffer.concat([Buffer.from('89504e470d0a1a0a','hex'),chunk('IHDR',header),chunk('IDAT',deflateSync(rows)),chunk('IEND',Buffer.alloc(0))]);
}
