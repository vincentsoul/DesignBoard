import {uid} from './model.js';
export const TERRAIN_TYPES = new Set(['solid','platform','hazard','ladder']);
export function lineCells(a,b){
 const cells=[];let x=a.x,y=a.y;const dx=Math.abs(b.x-x),dy=-Math.abs(b.y-y),sx=x<b.x?1:-1,sy=y<b.y?1:-1;let error=dx+dy;
 while(true){cells.push([x,y]);if(x===b.x&&y===b.y)break;const e=2*error;if(e>=dy){error+=dy;x+=sx;}if(e<=dx){error+=dx;y+=sy;}}
 return cells;
}
// Convert occupied cells into vertically merged runs rather than thousands of tile objects.
export function packCells(cells){
 const rows=new Map();for(const key of cells){const [x,y]=key.split(',').map(Number);if(!rows.has(y))rows.set(y,new Set());rows.get(y).add(x);}
 const rectangles=[],active=new Map();let previous=null;
 for(const y of [...rows.keys()].sort((a,b)=>a-b)){
  if(previous!==null&&y!==previous+1)active.clear();const next=new Map(),xs=[...rows.get(y)].sort((a,b)=>a-b);
  for(let i=0;i<xs.length;){const x=xs[i];let end=x;while(i+1<xs.length&&xs[i+1]===end+1){end=xs[++i];}i++;
   const w=end-x+1,key=x+':'+w,old=active.get(key);
   if(old){old.h++;next.set(key,old);}else{const rect={x,y,w,h:1};rectangles.push(rect);next.set(key,rect);}
  }
  active.clear();for(const [k,v]of next)active.set(k,v);previous=y;
 }
 return rectangles;
}
const covers=(o,x,y)=>x>=o.x&&x<o.x+o.w&&y>=o.y&&y<o.y+o.h;
export function editRoomCells(room,requested,type='solid',erase=false){
 if(!erase&&!TERRAIN_TYPES.has(type))throw new Error('画笔只用于实体、平台、危险区和梯子；物件请用矩形或标记工具。');
 const touched=new Set([...requested].filter(k=>{const [x,y]=k.split(',').map(Number);return Number.isInteger(x)&&Number.isInteger(y)&&x>=0&&y>=0&&x<room.w*4&&y<room.h*4;}));
 if(!touched.size)return room.objects;
 const added=erase?new Set():new Set(touched),terrain=[],markers=[];
 const reusable=new Map(room.objects.filter(o=>o.brush===true&&o.type===type&&!o.label&&!o.flagId).map(o=>[[o.x,o.y,o.w,o.h].join(':'),o.id]));
 for(const o of room.objects){
  if(!TERRAIN_TYPES.has(o.type)){
   if(!erase||![...touched].some(k=>{const [x,y]=k.split(',').map(Number);return covers(o,x,y);}))markers.push(o);
   continue;
  }
  if(!erase&&o.type===type){
   if(o.brush===true&&!o.label&&!o.flagId){for(let y=o.y;y<o.y+o.h;y++)for(let x=o.x;x<o.x+o.w;x++)added.add(x+','+y);}
   else{terrain.push(o);for(const k of added){const [x,y]=k.split(',').map(Number);if(covers(o,x,y))added.delete(k);}}
   continue;
  }
  let hit=false;for(const k of touched){const [x,y]=k.split(',').map(Number);if(covers(o,x,y)){hit=true;break;}}
  if(!hit){terrain.push(o);continue;}
  const remaining=new Set();for(let y=o.y;y<o.y+o.h;y++)for(let x=o.x;x<o.x+o.w;x++)if(!touched.has(x+','+y))remaining.add(x+','+y);
  packCells(remaining).forEach((rect,i)=>terrain.push({...o,...rect,id:i===0?o.id:uid('object')}));
 }
 if(!erase)for(const rect of packCells(added))terrain.push({id:reusable.get([rect.x,rect.y,rect.w,rect.h].join(':'))||uid('object'),type,...rect,label:'',flagId:null,brush:true});
 const objects=[...terrain,...markers];if(objects.length>5000)throw new Error('地形过于零碎，超过单房间 5000 个图形，请使用较大的画笔或矩形。');
 return objects;
}
export function brushCells(room,from,to,size=1){
 const limit=p=>({x:Math.max(-room.w*4,Math.min(room.w*8,p.x)),y:Math.max(-room.h*4,Math.min(room.h*8,p.y))});
 const result=new Set();for(const [x,y]of lineCells(limit(from),limit(to)))for(let dy=0;dy<size;dy++)for(let dx=0;dx<size;dx++){
  const px=x+dx,py=y+dy;if(px>=0&&py>=0&&px<room.w*4&&py<room.h*4)result.add(px+','+py);
 }
 return result;
}
