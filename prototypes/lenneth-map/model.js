// A topology model inspired by side-view dungeons, not game-extracted data.
export const uid = (prefix='id') => `${prefix}_${globalThis.crypto?.randomUUID?.() || Math.random().toString(36).slice(2)+Date.now().toString(36)}`;
export const clone = value => JSON.parse(JSON.stringify(value));
export const PORTS = {left:'左侧',right:'右侧',up:'上层',down:'下层',front:'前景',back:'后景'};
export const LINK_TYPES = {walk:'横向通路',depth:'前后景通道',stairs:'楼梯 / 竖井',drop:'单向落下',teleport:'传送',mechanism:'机关门'};
export const ROOM_TYPES = {normal:'普通房间',entrance:'入口',save:'存档点',boss:'首领房',treasure:'宝物房',puzzle:'机关房'};
export const OBJECT_TYPES = {
 solid:{name:'实体地形',color:'#7e8b9f',glyph:'▰'}, platform:{name:'平台',color:'#6dc4a2',glyph:'▬'},
 crystal:{name:'水晶造阶点',color:'#75dced',glyph:'◇'}, frozen:{name:'冻结敌人',color:'#a6ccff',glyph:'❄'},
 chest:{name:'宝箱',color:'#d7b36c',glyph:'▣'}, enemy:{name:'敌人',color:'#d88088',glyph:'◆'},
 switch:{name:'机关',color:'#d49de7',glyph:'⚙'}, ladder:{name:'梯子',color:'#dbb98a',glyph:'↕'},
 save:{name:'存档点',color:'#87dbc5',glyph:'✧'}, hazard:{name:'危险区',color:'#e37979',glyph:'△'},
 block:{name:'可推拉物',color:'#bfa281',glyph:'□'}, note:{name:'注释',color:'#a1a9ba',glyph:'✎'},
};
export function newRoom(index=1, depth=0, x=0, y=0) {
 return {id:uid('room'),name:`房间 ${index}`,x,y,w:8,h:4,depth,kind:'normal',color:'#75a8d7',notes:'',objects:[]};
}
export function newProject() {
 return {id:uid('lenneth'),version:1,editor:'lenneth',name:'未命名横版迷宫',updated:Date.now(),chapter:1,difficulty:'Normal',notes:'',startRoomId:null,rooms:[],links:[],flags:[]};
}
export function sampleProject() {
 const doc=newProject();doc.name='苍霜遗迹 · 结构示例';doc.notes='原创教学示例，非《北欧女神》原作关卡复刻。景深值越大表示越靠后，楼层由画布 Y 表示。';
 const defs=[['遗迹入口',0,0,0,'entrance'],['水晶回廊',12,0,0,'normal'],['封印大厅',24,0,0,'puzzle'],['守卫之间',36,0,0,'boss'],['后景水渠',12,8,1,'normal'],['古代机关',24,8,1,'puzzle'],['高处宝库',24,-8,0,'treasure'],['前景小径',0,8,-1,'save']];
 doc.rooms=defs.map(([name,x,y,depth,kind],i)=>({...newRoom(i+1,depth,x,y),name,kind,color:depth===1?'#9b91d4':depth===-1?'#77b9ab':'#75a8d7'}));
 const [a,b,c,d,e,f,g,h]=doc.rooms;doc.startRoomId=a.id;
 const flag={id:uid('flag'),name:'水闸已开启',initial:false};doc.flags=[flag];
 const link=(from,to,type='walk',fromPort='right',toPort='left',oneWay=false,requires=[])=>({id:uid('link'),from:from.id,to:to.id,fromPort,toPort,type,oneWay,requires,logic:'all',notes:''});
 doc.links=[link(a,b),link(b,c),link(c,d,'mechanism','right','left',false,[flag.id]),link(b,e,'depth','back','front'),link(e,f),link(b,g,'stairs','up','left'),link(g,c,'drop','down','up',true),link(a,h,'depth','front','back')];
 for(const r of doc.rooms)r.objects.push({id:uid('object'),type:'solid',x:0,y:r.h*4-2,w:r.w*4,h:2,label:'地面',flagId:null});
 b.objects.push({id:uid('object'),type:'crystal',x:10,y:9,w:2,h:3,label:'水晶造阶',flagId:null},{id:uid('object'),type:'platform',x:18,y:6,w:10,h:1,label:'高台',flagId:null});
 f.objects.push({id:uid('object'),type:'switch',x:16,y:10,w:3,h:3,label:'开启水闸',flagId:flag.id});
 g.objects.push({id:uid('object'),type:'chest',x:18,y:11,w:3,h:2,label:'高处奖励',flagId:null});
 d.objects.push({id:uid('object'),type:'enemy',x:14,y:6,w:7,h:8,label:'遗迹守卫',flagId:null});
 h.objects.push({id:uid('object'),type:'save',x:14,y:10,w:3,h:4,label:'休整',flagId:null});
 return doc;
}
export function validateProject(doc, id=doc?.id) {
 const bad=message=>{throw Object.assign(new Error(message),{status:400});};
 const ident=v=>typeof v==='string'&&/^[a-zA-Z0-9_-]{1,100}$/.test(v);
 const text=(v,max=20000)=>typeof v==='string'&&v.length<=max;
 const num=(v,min=-100000,max=100000)=>Number.isInteger(v)&&v>=min&&v<=max;
 if(!doc||!ident(doc.id)||doc.id!==id||doc.version!==1||doc.editor!=='lenneth'||!text(doc.name,1000)||!text(doc.notes)||!num(doc.chapter,0,8)||!['Easy','Normal','Hard'].includes(doc.difficulty))bad('不是有效的 LennethMap 工程（与 MVMap JSON 分开存储）');
 for(const k of ['rooms','links','flags'])if(!Array.isArray(doc[k])||doc[k].length>5000)bad(`字段 ${k} 无效或超过 5000 项`);
 const allIds=new Set();
 const take=v=>{if(!ident(v)||allIds.has(v))bad('对象 ID 无效或重复');allIds.add(v);};
 const roomIds=new Set(doc.rooms.map(r=>r?.id));const flagIds=new Set(doc.flags.map(f=>f?.id));
 for(const f of doc.flags){take(f?.id);if(!text(f.name,200)||typeof f.initial!=='boolean')bad('机关条件无效');}
 for(const r of doc.rooms){
  take(r?.id);
  if(!text(r.name,500)||!text(r.notes)||!num(r.x)||!num(r.y)||!num(r.w,1,128)||!num(r.h,1,64)||!num(r.depth,-20,20)||!Object.hasOwn(ROOM_TYPES,r.kind)||!/^#[0-9a-fA-F]{6}$/.test(r.color)||!Array.isArray(r.objects)||r.objects.length>5000)bad('房间数据无效');
  for(const o of r.objects){take(o?.id);if(!Object.hasOwn(OBJECT_TYPES,o.type)||!num(o.x,0,r.w*4-1)||!num(o.y,0,r.h*4-1)||!num(o.w,1,r.w*4)||!num(o.h,1,r.h*4)||o.x+o.w>r.w*4||o.y+o.h>r.h*4||!text(o.label,1000)||(o.flagId!==null&&!flagIds.has(o.flagId)))bad('室内对象超出房间或引用无效');}
 }
 for(const l of doc.links){
  take(l?.id);
  if(!roomIds.has(l.from)||!roomIds.has(l.to)||l.from===l.to||!Object.hasOwn(PORTS,l.fromPort)||!Object.hasOwn(PORTS,l.toPort)||!Object.hasOwn(LINK_TYPES,l.type)||typeof l.oneWay!=='boolean'||!['all','any'].includes(l.logic)||!Array.isArray(l.requires)||l.requires.some(f=>!flagIds.has(f))||!text(l.notes))bad('连接端点或条件无效');
 }
 if(doc.startRoomId!==null&&!roomIds.has(doc.startRoomId))bad('起始房间不存在');
 return doc;
}
export function removeRoom(doc,id){doc.rooms=doc.rooms.filter(r=>r.id!==id);doc.links=doc.links.filter(l=>l.from!==id&&l.to!==id);if(doc.startRoomId===id)doc.startRoomId=doc.rooms[0]?.id||null;}
export function removeFlag(doc,id){doc.flags=doc.flags.filter(f=>f.id!==id);for(const l of doc.links)l.requires=l.requires.filter(f=>f!==id);for(const r of doc.rooms)for(const o of r.objects)if(o.flagId===id)o.flagId=null;}
export function linkPassable(link,enabled){return !link.requires.length||(link.logic==='all'?link.requires.every(id=>enabled.has(id)):link.requires.some(id=>enabled.has(id)));}
export function reachableRooms(doc,enabled=new Set(doc.flags.filter(f=>f.initial).map(f=>f.id))){
 const seen=new Set(doc.startRoomId?[doc.startRoomId]:[]),queue=[...seen];
 for(let i=0;i<queue.length;i++)for(const link of doc.links){
  if(!linkPassable(link,enabled))continue;
  const next=link.from===queue[i]?link.to:!link.oneWay&&link.to===queue[i]?link.from:null;
  if(next&&!seen.has(next)){seen.add(next);queue.push(next);}
 }
 return seen;
}
export function audit(doc){
 const issues=[];const all=reachableRooms(doc,new Set(doc.flags.map(f=>f.id)));
 if(!doc.startRoomId)issues.push('尚未设置起始房间。');
 for(const room of doc.rooms)if(!all.has(room.id))issues.push(`孤立或单向不可达：${room.name}`);
 for(const l of doc.links){
  const a=doc.rooms.find(r=>r.id===l.from),b=doc.rooms.find(r=>r.id===l.to);
  if(l.type==='depth'&&a.depth===b.depth)issues.push(`前后景连接位于相同景深：${a.name} → ${b.name}`);
  if(l.type==='depth'&&!['front','back'].includes(l.fromPort))issues.push(`前后景连接建议使用前景/后景端口：${a.name}`);
  if(l.type==='drop'&&!l.oneWay)issues.push(`落下通路尚未设置为单向：${a.name} → ${b.name}`);
 }
 for(const f of doc.flags)if(!f.initial&&doc.links.some(l=>l.requires.includes(f.id))&&!doc.rooms.some(r=>r.objects.some(o=>o.flagId===f.id)))issues.push(`条件没有获取点：${f.name}`);
 return issues;
}
