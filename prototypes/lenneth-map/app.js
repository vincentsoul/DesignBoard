import {uid,clone,newRoom,newProject,sampleProject,validateProject,PORTS,LINK_TYPES,ROOM_TYPES,OBJECT_TYPES,removeRoom,removeFlag,reachableRooms,linkPassable,audit} from './model.js';
import {Preview3D,clampPitch} from './preview3d.js';
import {ProjectStorage} from './storage.js';
const $=s=>document.querySelector(s),esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const store=new ProjectStorage();
const orbit=new Preview3D();
let view3D=false;
let doc=sampleProject(),selection=null,detailId=null,tool='select',objectType='platform',depth=null,activeDepth=0,linkSource=null,preview=false;
let cam={x:0,y:0,zoom:1},drag=null,space=false,dirty=false,saveError=false,timer=null,generation=0;
const undoStack=[],redoStack=[],enabled=new Set();
const canvas=$('#canvas'),ctx=canvas.getContext('2d');let width=800,height=600,dpr=1;
const GRID=26;
const room=id=>doc.rooms.find(r=>r.id===id),link=id=>doc.links.find(l=>l.id===id);
const selectedRoom=()=>selection?.type==='room'?room(selection.id):room(detailId);
const selectedObject=()=>selection?.type==='object'?room(detailId)?.objects.find(o=>o.id===selection.id):null;
const visibleRooms=()=>doc.rooms.filter(r=>depth===null||r.depth===depth);
function notify(message){$('#notice-text').textContent=message;$('#notice').hidden=false;}
$('#close-notice').onclick=()=>$('#notice').hidden=true;
function status(message,error=false){$('#save-status').textContent=message;$('#save-status').style.color=error?'#e6ad8b':'#7ccdb5';}
function backupId(){try{localStorage.setItem('lenneth:last',doc.id);}catch{}}
function checkpoint(){undoStack.push(JSON.stringify(doc));if(undoStack.length>100)undoStack.shift();redoStack.length=0;}
function changed(){doc.updated=Date.now();dirty=true;generation++;backupId();render();schedule();}
function mutate(fn){checkpoint();fn();changed();}
function schedule(){clearTimeout(timer);status('待保存…');timer=setTimeout(()=>save().catch(()=>{}),650);}
async function save(){
 clearTimeout(timer);timer=null;if(!dirty)return;
 const value=clone(doc),token=generation;status('正在保存…');
 try{await store.write(value);if(token===generation){dirty=false;saveError=false;status(store.remote?'已同步到 NAS':'已保存到本机');}}
 catch(e){saveError=true;status(e.status===409?'版本冲突 · 本地已保留':'保存失败 · 请重试',true);notify(e.status===409?'其他设备已修改这张地图，未覆盖 NAS。请先导出 JSON 或「另存副本」，再刷新服务器版本。':`尚未同步：${e.message} 可导出 JSON 备份。`);throw e;}
}
async function beforeSwitch(){if(!dirty)return true;try{await save();return true;}catch{return confirm('当前地图尚未同步，建议先导出 JSON。仍切换？可以从地图库的「本机备份」恢复。');}}
function setDocument(record){
 clearTimeout(timer);timer=null;doc=clone(validateProject(record.doc));store.accept(record);selection=null;detailId=null;linkSource=null;
 depth=null;activeDepth=0;enabled.clear();doc.flags.filter(f=>f.initial).forEach(f=>enabled.add(f.id));
 undoStack.length=redoStack.length=0;generation++;dirty=!!record.dirty;saveError=!!record.dirty;backupId();render();fit();status(record.dirty?'本机有未同步修改，请重试或另存副本':store.remote?'NAS 文件已读取':'本机文件已读取',!!record.dirty);
}
function modal(title,html){$('#dialog-title').textContent=title;$('#dialog-content').innerHTML=html;if(!$('#dialog').open)$('#dialog').showModal();}
$('#dialog-close').onclick=()=>$('#dialog').close();
$('#dialog').addEventListener('click',e=>{if(e.target===$('#dialog')){const r=e.target.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)e.target.close();}});
const input=(label,key,value,type='text',extra='')=>`<label class="field"><span>${label}</span><input data-field="${key}" type="${type}" value="${esc(value)}" ${extra}></label>`;
const select=(label,key,value,options)=>`<label class="field"><span>${label}</span><select data-field="${key}">${Object.entries(options).map(([v,n])=>`<option value="${esc(v)}" ${String(v)===String(value)?'selected':''}>${esc(n)}</option>`).join('')}</select></label>`;
const textarea=(label,key,value)=>`<label class="field"><span>${label}</span><textarea data-field="${key}">${esc(value)}</textarea></label>`;
function bindFields(target,hook=()=>{}){
 $('#inspector').querySelectorAll('[data-field]').forEach(el=>el.onchange=()=>{
  const key=el.dataset.field,old=clone(target);checkpoint();
  target[key]=el.type==='number'?Number(el.value):el.type==='checkbox'?el.checked:el.value;
  hook(key);try{validateProject(doc);changed();}catch(e){Object.assign(target,old);undoStack.pop();notify(e.message);render();}
 });
}
function renderInspector(){
 const host=$('#inspector'),r=selectedRoom(),o=selectedObject();
 if(selection?.type==='object'&&o){
  $('#inspector-title').textContent='室内对象';
  host.innerHTML=`<p class="tag">${esc(OBJECT_TYPES[o.type].name)} / 4× 子网格</p>${select('类型','type',o.type,Object.fromEntries(Object.entries(OBJECT_TYPES).map(([k,v])=>[k,v.name])))}${input('标签','label',o.label)}<div class="field-row">${input('X','x',o.x,'number','min="0"')}${input('Y','y',o.y,'number','min="0"')}</div><div class="field-row">${input('宽度','w',o.w,'number','min="1"')}${input('高度','h',o.h,'number','min="1"')}</div>${select('可触发的条件（设计标记）','flagId',o.flagId||'',{'':'无',...Object.fromEntries(doc.flags.map(f=>[f.id,f.name]))})}<p class="small muted">触发条件是设计标记，不会在预览中自动激活。请手动勾选左侧条件。</p><button class="danger" id="delete-selection">删除对象</button>`;
  bindFields(o,()=>{if(o.flagId==='')o.flagId=null;});
 }else if(selection?.type==='link'&&link(selection.id)){
  const l=link(selection.id);$('#inspector-title').textContent='房间连接';
  const opts=Object.fromEntries(doc.rooms.map(r=>[r.id,`${r.name} · Z${r.depth}`]));
  host.innerHTML=`<p class="tag">CONNECTION / 显式连接</p>${select('起点房间','from',l.from,opts)}${select('起点出口','fromPort',l.fromPort,PORTS)}${select('终点房间','to',l.to,opts)}${select('终点入口','toPort',l.toPort,PORTS)}${select('连接类型','type',l.type,LINK_TYPES)}<label class="field check"><input data-field="oneWay" type="checkbox" ${l.oneWay?'checked':''}><span>单向：起点 → 终点</span></label>${select('条件组合','logic',l.logic,{all:'满足全部（AND）',any:'满足任一（OR）'})}<div class="field"><span>通行条件</span>${doc.flags.map(f=>`<label class="flag"><input type="checkbox" data-requires="${f.id}" ${l.requires.includes(f.id)?'checked':''}>${esc(f.name)}</label>`).join('')||'<p class="muted small">先在左侧新增机关条件。</p>'}</div>${textarea('连接 / 机关行为说明','notes',l.notes)}<button class="danger" id="delete-selection">删除连接</button>`;
  bindFields(l,key=>{if(key==='type'&&l.type==='drop')l.oneWay=true;});
  host.querySelectorAll('[data-requires]').forEach(el=>el.onchange=()=>mutate(()=>{l.requires=el.checked?[...l.requires,el.dataset.requires]:l.requires.filter(x=>x!==el.dataset.requires);}));
 }else if(r){
  $('#inspector-title').textContent=detailId?'房间室内布局':'房间属性';
  host.innerHTML=`<p class="tag">ROOM / ${esc(ROOM_TYPES[r.kind])}</p>${input('名称','name',r.name)}${select('类型','kind',r.kind,ROOM_TYPES)}<div class="field-row">${input('景深 Z','depth',r.depth,'number','min="-20" max="20"')}${input('颜色','color',r.color,'color')}</div><div class="field-row">${input('排版 X','x',r.x,'number')}${input('排版 Y（向下为正）','y',r.y,'number')}</div><div class="field-row">${input('宽度（世界格）','w',r.w,'number','min="1" max="128"')}${input('高度（世界格）','h',r.h,'number','min="1" max="64"')}</div>${textarea('设计笔记','notes',r.notes)}<div class="button-row"><button id="open-detail" class="primary">编辑室内灰盒 ↗</button><button id="set-start">${doc.startRoomId===r.id?'✓ 起始房间':'设为起点'}</button><button class="danger" id="delete-selection">删除房间</button></div><p class="small muted">拖动可移动；拖右下角调整大小。缩小房间不能裁掉已有室内对象。</p>`;
  bindFields(r);$('#open-detail').onclick=()=>openDetail(r.id);$('#set-start').onclick=()=>mutate(()=>doc.startRoomId=r.id);
 }else{
  $('#inspector-title').textContent='工程属性';
  host.innerHTML=`<p class="inspector-intro">以横版房间组织迷宫，用<strong>前后景通道</strong>连接不同深度。<br>选择房间或连线编辑属性，双击房间布置内部地形。</p><div class="field-row">${input('章节（0–8）','chapter',doc.chapter,'number','min="0" max="8"')}${select('难度','difficulty',doc.difficulty,{Easy:'Easy',Normal:'Normal',Hard:'Hard'})}</div>${textarea('工程说明','notes',doc.notes)}<p class="small muted">章节和难度是项目元数据，不控制房间显隐。旋转机关用条件与通路表示，本版本不模拟房间旋转。</p><button id="sample">加载结构示例</button>`;
  bindFields(doc);$('#sample').onclick=async()=>{if(await beforeSwitch()){setDocument({doc:sampleProject(),revision:null});dirty=true;generation++;schedule();}};
 }
 const del=$('#delete-selection');if(del)del.onclick=deleteSelection;
}
function render(){
 $('#project-name').value=doc.name;$('#storage-mode').textContent=store.remote?'NAS · 已连接服务':'离线 · 本机存储';
 $('#refresh').hidden=!store.remote;$('#retry').hidden=!store.remote;
 $('#undo').disabled=view3D||!undoStack.length;$('#redo').disabled=view3D||!redoStack.length;$('#project-name').disabled=view3D;
 $('#room-count').textContent=doc.rooms.length;
 const layers=[...new Set([activeDepth,...doc.rooms.map(r=>r.depth)])].sort((a,b)=>a-b);
 $('#depth-list').innerHTML=`<button data-depth="all" class="${depth===null?'on':''}">全部</button>`+layers.map(z=>`<button data-depth="${z}" class="${depth===z?'on':''}">${z<0?'前景':z>0?'后景':'主景'} Z${z}</button>`).join('');
 $('#depth-list').querySelectorAll('button').forEach(b=>b.onclick=()=>{depth=b.dataset.depth==='all'?null:Number(b.dataset.depth);if(depth!==null)activeDepth=depth;render();});
 const q=$('#room-search').value.toLowerCase();
 $('#room-list').innerHTML=doc.rooms.filter(r=>r.name.toLowerCase().includes(q)).map(r=>`<button class="room-row ${selection?.id===r.id?'on':''}" data-room="${r.id}" title="${esc(r.name)}"><span class="swatch" style="background:${r.color}"></span><span>${doc.startRoomId===r.id?'⌂ ':''}${esc(r.name)}</span><small>Z${r.depth}</small></button>`).join('')||'<p class="muted small">暂无房间。按 R 拖动绘制。</p>';
 $('#room-list').querySelectorAll('[data-room]').forEach(b=>b.onclick=()=>{if(tool==='link'&&!detailId&&!view3D){connectTo(room(b.dataset.room));return;}selection={type:'room',id:b.dataset.room};if(detailId)openDetail(selection.id);else{depth=null;const r=room(selection.id);cam.x=(r.x+r.w/2)*GRID;cam.y=(r.y+r.h/2)*GRID;render();}});
 $('#flag-list').innerHTML=doc.flags.map(f=>`<div class="flag"><input id="flag-${f.id}" data-flag="${f.id}" type="checkbox" ${enabled.has(f.id)?'checked':''}><label for="flag-${f.id}">${esc(f.name)}</label><button data-edit-flag="${f.id}" title="编辑条件">⋯</button></div>`).join('')||'<p class="muted small">暂无条件。可添加钥匙、机关或事件。</p>';
 $('#flag-list').querySelectorAll('[data-flag]').forEach(el=>el.onchange=()=>{el.checked?enabled.add(el.dataset.flag):enabled.delete(el.dataset.flag);draw();});
 $('#flag-list').querySelectorAll('[data-edit-flag]').forEach(b=>{b.disabled=view3D;b.onclick=()=>editFlag(b.dataset.editFlag);});
 $('#tools').querySelectorAll('[data-tool]').forEach(b=>b.classList.toggle('on',tool===b.dataset.tool));
 $('#view-3d').classList.toggle('on',view3D);$('#view-3d').textContent=view3D?'↖ 返回二维':'◇ 3D 旋转预览';
 $('#preview-3d-controls').hidden=!view3D;
 $('#preview').classList.toggle('on',preview);$('#back').hidden=!detailId;
 $('#view-label').textContent=view3D?'3D 空间预览':detailId?'室内灰盒':'迷宫总览';$('#view-title').textContent=detailId?(room(detailId)?.name||'房间'):depth===null?'所有景深':`景深 Z${depth}`;
 $('#detail-palette').hidden=!detailId;
 $('#detail-palette').innerHTML=Object.entries(OBJECT_TYPES).map(([type,v])=>`<button data-object-type="${type}" class="${type===objectType?'on':''}">${v.glyph} ${v.name}</button>`).join('');
 $('#detail-palette').querySelectorAll('button').forEach(b=>b.onclick=()=>{objectType=b.dataset.objectType;tool='room';render();});
 $('#tools [data-tool="room"]').innerHTML=detailId?'<b>▭</b>方框 <kbd>R</kbd>':'<b>▭</b>房间 <kbd>R</kbd>';
 $('#tools').querySelectorAll('[data-tool]').forEach(b=>b.disabled=view3D);
 $('#tools [data-tool="link"]').disabled=!!detailId||view3D;
 $('#add-room').disabled=view3D;$('#add-depth').disabled=view3D;$('#add-flag').disabled=view3D;
 $('.canvas-caption').hidden=!!detailId||view3D;$('#empty').hidden=!!doc.rooms.length;
 $('#summary').textContent=`${doc.rooms.length} 个房间 · ${doc.links.length} 条连接 · ${doc.flags.length} 个条件`;
 const hints={select:detailId?'点击选择对象 · 拖动移动 / 右下角缩放 · Esc 返回总览':'点击选择 · 拖动移动 / 右下角缩放 · 双击编辑室内',room:detailId?'拖出室内方框 · 使用上方类型工具条切换':'拖出房间矩形 · 当前新增景深 Z'+activeDepth,link:linkSource?'请选择目标房间（可在左侧切换景深）':'依次点击起点和终点房间 · 可跨景深连接',erase:'点击删除对象 · Ctrl/Cmd+Z 撤销',pan:'拖动画布平移 · 滚轮缩放 · F 适应'};
 $('#hint').textContent=view3D?'拖动旋转 · 右键 / 中键 / Shift 拖动平移 · 滚轮缩放 · 双击房间进入二维':hints[tool];renderInspector();
 if(view3D){$('#inspector').querySelectorAll('input,textarea,select,button').forEach(el=>el.disabled=el.id!=='open-detail');}
 draw();
}
function editFlag(id){
 const flag=doc.flags.find(f=>f.id===id);
 modal('机关条件',`${input('名称','flag-name',flag.name)}<label class="flag"><input id="flag-initial" type="checkbox" ${flag.initial?'checked':''}>进入迷宫时默认已满足</label><p class="small muted">删除条件会移除连接上的该项要求，可能使通道开放。</p><div class="button-row"><button id="flag-save" class="primary">保存</button><button id="flag-delete" class="danger">删除条件</button></div>`);
 $('#flag-save').onclick=()=>{const name=$('[data-field="flag-name"]').value.trim();if(!name)return;mutate(()=>{flag.name=name.slice(0,200);flag.initial=$('#flag-initial').checked;flag.initial?enabled.add(id):enabled.delete(id);});$('#dialog').close();};
 $('#flag-delete').onclick=()=>{if(!confirm('删除条件后，所有连接将移除此项要求。继续？'))return;mutate(()=>{removeFlag(doc,id);enabled.delete(id);});$('#dialog').close();};
}
function deleteSelection(){
 if(!selection)return;
 mutate(()=>{if(selection.type==='room'){removeRoom(doc,selection.id);if(detailId===selection.id)detailId=null;}else if(selection.type==='link')doc.links=doc.links.filter(l=>l.id!==selection.id);else if(selectedObject())room(detailId).objects=room(detailId).objects.filter(o=>o.id!==selection.id);selection=null;});
}
function undo(redo=false){const from=redo?redoStack:undoStack,to=redo?undoStack:redoStack;if(!from.length)return;to.push(JSON.stringify(doc));doc=JSON.parse(from.pop());if(detailId&&!room(detailId))detailId=null;selection=null;enabled.forEach(f=>{if(!doc.flags.some(x=>x.id===f))enabled.delete(f);});changed();}
$('#undo').onclick=()=>undo();$('#redo').onclick=()=>undo(true);
$('#project-name').onchange=()=>mutate(()=>doc.name=$('#project-name').value);
$('#room-search').oninput=()=>render();
$('#tools').querySelectorAll('[data-tool]').forEach(b=>b.onclick=()=>{tool=b.dataset.tool;linkSource=null;render();});
$('#add-room').onclick=()=>{mutate(()=>{const r=newRoom(doc.rooms.length+1,activeDepth,Math.round(cam.x/GRID)-4,Math.round(cam.y/GRID)-2);doc.rooms.push(r);if(!doc.startRoomId)doc.startRoomId=r.id;selection={type:'room',id:r.id};detailId=null;});};
$('#add-depth').onclick=()=>{const value=prompt('选择新增房间的景深 Z（-20 到 20；越大越靠后）',String(activeDepth+1));if(value===null)return;const z=Number(value);if(!Number.isInteger(z)||z<-20||z>20){notify('景深必须为 -20 到 20 的整数。');return;}activeDepth=z;depth=z;render();};
$('#add-flag').onclick=()=>{const name=prompt('机关条件名称，例如：水闸已开启');if(!name?.trim())return;mutate(()=>doc.flags.push({id:uid('flag'),name:name.trim().slice(0,200),initial:false}));};
$('#preview').onclick=()=>{preview=!preview;render();if(preview)notify('预览按当前勾选条件计算房间拓扑可达性，不模拟室内跳跃、冰晶物理或机关时序。绿色边框表示可达。');};
$('#audit').onclick=()=>{const issues=audit(doc);modal('结构检查',`<p>按全部条件满足计算孤立房间，并检查深度连接和条件获取点。不是物理模拟或完整软锁检测。</p>${issues.length?`<ol>${issues.map(i=>`<li>${esc(i)}</li>`).join('')}</ol>`:'<p style="color:#7ccdb5">✓ 未发现基础结构问题。</p>'}`);};
$('#new').onclick=async()=>{if(await beforeSwitch()){setDocument({doc:newProject(),revision:null});dirty=true;generation++;schedule();}};
$('#copy').onclick=()=>{if(drag)return;doc.id=uid('lenneth');doc.name+='（副本）';generation++;dirty=true;saveError=false;backupId();render();schedule();};
$('#retry').onclick=()=>save().catch(()=>{});
$('#refresh').onclick=async()=>{
 if(dirty&&!confirm('当前有未同步修改。请先导出或另存副本；仍读取 NAS 版本？'))return;
 clearTimeout(timer);timer=null;await store.queue.catch(()=>{});
 try{if(dirty)await store.cache({id:doc.id,doc:clone(doc),revision:store.revisions.get(doc.id)||null,dirty:true});const records=await store.list(),record=records.find(r=>r.doc.id===doc.id);if(!record){notify('NAS 中没有当前地图，可点击重试保存，或从地图库打开其他地图。');return;}setDocument(record);$('#notice').hidden=true;}catch(e){notify(e.message);}
};
async function showLibrary(backups=false){
 try{
  const records=backups?(await store.backups()).map(r=>({...r,revision:r.revision||null})):await store.list();
  records.sort((a,b)=>(b.doc.updated||0)-(a.doc.updated||0));
  modal(backups?'本机备份':'地图库',`<div class="button-row"><button id="show-backups">${backups?'返回地图库':'查看本机备份'}</button><button id="load-sample">新增原创示例</button></div><p class="small muted">${backups?'恢复备份会创建新工程，不覆盖 NAS。':'地图与 MVMap、白板分开存储。删除会移入服务器回收站。'}</p>${records.map((r,i)=>`<div class="map-row"><div class="map-info"><strong>${esc(r.doc.name)}</strong><small>${r.doc.rooms.length} 个房间 · ${new Date(r.doc.updated).toLocaleString('zh-CN')}${r.dirty?' · 尚未同步':''}</small></div><button data-open="${i}">${backups?'恢复副本':'打开'}</button>${!backups?`<button data-delete="${i}" class="danger">删除</button>`:''}</div>`).join('')||'<p>暂无文件。</p>'}`);
  $('#show-backups').onclick=()=>showLibrary(!backups);
  $('#load-sample').onclick=async()=>{if(await beforeSwitch()){setDocument({doc:sampleProject(),revision:null});dirty=true;generation++;schedule();$('#dialog').close();}};
  $('#dialog-content').querySelectorAll('[data-open]').forEach(b=>b.onclick=async()=>{if(!(await beforeSwitch()))return;let record=records[+b.dataset.open];if(backups){record={doc:clone(record.doc),revision:null};record.doc.id=uid('lenneth');record.doc.name+='（恢复）';}setDocument(record);if(backups){dirty=true;generation++;schedule();}$('#dialog').close();});
  $('#dialog-content').querySelectorAll('[data-delete]').forEach(b=>b.onclick=async()=>{const record=records[+b.dataset.delete];if(!confirm(`删除“${record.doc.name}”？NAS 文件将移入回收站。`))return;if(record.doc.id===doc.id&&!(await beforeSwitch()))return;try{await store.remove(record);if(record.doc.id===doc.id)setDocument({doc:newProject(),revision:null});showLibrary();}catch(e){notify(e.message);}});
 }catch(e){notify(e.message);}
}
$('#library').onclick=()=>showLibrary();
function download(blob,name){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),4000);}
const filename=()=>doc.name.replace(/[<>:"/\\|?*\u0000-\u001f]+/g,'-').slice(0,100)||'横版迷宫';
$('#export').onclick=()=>download(new Blob([JSON.stringify(doc,null,2)],{type:'application/json'}),filename()+'.lenneth.json');
$('#import').onclick=()=>$('#file-input').click();
$('#file-input').onchange=async e=>{const file=e.target.files[0];e.target.value='';if(!file)return;if(file.size>30*1024*1024){notify('文件超过 30 MiB。');return;}try{const value=validateProject(JSON.parse(await file.text()));if(!(await beforeSwitch()))return;value.id=uid('lenneth');setDocument({doc:value,revision:null});dirty=true;generation++;schedule();}catch(e){notify('导入失败：'+e.message);}};
function openDetail(id){if(!room(id))return;view3D=false;detailId=id;selection={type:'room',id};linkSource=null;tool='select';render();fit();}
$('#back').onclick=()=>{detailId=null;selection=null;tool='select';render();fit();};
const screen=(x,y,c=cam,w=width,h=height)=>({x:(x*GRID-c.x)*c.zoom+w/2,y:(y*GRID-c.y)*c.zoom+h/2});
const world=(p)=>({x:((p.x-width/2)/cam.zoom+cam.x)/GRID,y:((p.y-height/2)/cam.zoom+cam.y)/GRID});
const pos=e=>{const r=canvas.getBoundingClientRect();return{x:e.clientX-r.left,y:e.clientY-r.top};};
function bounds(){if(detailId){const r=room(detailId);return{x:0,y:0,w:r.w*4,h:r.h*4};}const rooms=visibleRooms();if(!rooms.length)return{x:-10,y:-6,w:20,h:12};const x=Math.min(...rooms.map(r=>r.x)),y=Math.min(...rooms.map(r=>r.y));return{x,y,w:Math.max(...rooms.map(r=>r.x+r.w))-x,h:Math.max(...rooms.map(r=>r.y+r.h))-y};}
function fit(){if(view3D){orbit.fit(visibleRooms(),width,height);draw();return;}const b=bounds();cam.x=(b.x+b.w/2)*GRID;cam.y=(b.y+b.h/2)*GRID;cam.zoom=Math.max(.12,Math.min(2,(width-100)/((b.w+4)*GRID),(height-(detailId?190:150))/((b.h+4)*GRID)));draw();}
$('#fit').onclick=fit;
function set3D(value){
 if(drag?.backup)doc=JSON.parse(drag.backup);
 if(detailId&&value)selection={type:'room',id:detailId};
 drag=null;view3D=value;detailId=null;linkSource=null;tool='select';render();fit();
}
$('#view-3d').onclick=()=>set3D(!view3D);
$('#preview-3d-controls').querySelectorAll('[data-view]').forEach(button=>button.onclick=()=>{orbit.preset(button.dataset.view,visibleRooms(),width,height);draw();});
$('#orbit-reset').onclick=()=>{orbit.preset('iso',visibleRooms(),width,height);draw();};
$('#depth-spacing').oninput=e=>{orbit.gap=Number(e.target.value);$('#depth-spacing-value').textContent=orbit.gap;fit();};
for(const [id,key]of [['orbit-labels','labels'],['orbit-guides','guides'],['orbit-interiors','interiors']])$('#'+id).onchange=e=>{orbit[key]=e.target.checked;draw();};

function port(r,p){return p==='left'?{x:r.x,y:r.y+r.h/2}:p==='right'?{x:r.x+r.w,y:r.y+r.h/2}:p==='up'?{x:r.x+r.w/2,y:r.y}:p==='down'?{x:r.x+r.w/2,y:r.y+r.h}:p==='front'?{x:r.x+r.w*.35,y:r.y+r.h}:{x:r.x+r.w*.65,y:r.y};}
function curve(l,c=cam,w=width,h=height){const a=screen(...Object.values(port(room(l.from),l.fromPort)),c,w,h),b=screen(...Object.values(port(room(l.to),l.toPort)),c,w,h);const dx=Math.max(30,Math.abs(b.x-a.x)*.45),dy=Math.max(30,Math.abs(b.y-a.y)*.45);const control=(p,port,t)=>port==='left'?{x:p.x-dx,y:p.y}:port==='right'?{x:p.x+dx,y:p.y}:['up','back'].includes(port)?{x:p.x,y:p.y-dy}:{x:p.x,y:p.y+dy};return[a,control(a,l.fromPort),control(b,l.toPort),b];}
const bez=(points,t)=>{const[a,b,c,d]=points,u=1-t;return{x:u*u*u*a.x+3*u*u*t*b.x+3*u*t*t*c.x+t*t*t*d.x,y:u*u*u*a.y+3*u*u*t*b.y+3*u*t*t*c.y+t*t*t*d.y};};
function rounded(g,x,y,w,h,r=6){g.beginPath();g.roundRect(x,y,w,h,r);}
function text(g,str,x,y,color,size=12,align='left'){g.fillStyle=color;g.font=`${size}px system-ui,"PingFang SC","Microsoft YaHei",sans-serif`;g.textAlign=align;g.fillText(str,x,y);}
function draw(g=ctx,w=width,h=height,c=cam,exporting=false){
 if(view3D){orbit.render(g,w,h,doc,visibleRooms(),{selection,preview,enabled,exporting});if(!exporting)$('#hud').textContent=`3D · ${Math.round(orbit.camera.yaw*180/Math.PI)%360}° / ${Math.round(orbit.camera.pitch*180/Math.PI)}°`;return;}
 if(!width||!height)return;g.clearRect(0,0,w,h);if(exporting){g.fillStyle='#111923';g.fillRect(0,0,w,h);}
 const unit=GRID*c.zoom;
 if(!exporting&&unit>=5){const start=screen(0,0,c,w,h);g.lineWidth=1;for(let x=((start.x%unit)+unit)%unit;x<w;x+=unit){g.strokeStyle='#7890b011';g.beginPath();g.moveTo(x,0);g.lineTo(x,h);g.stroke();}for(let y=((start.y%unit)+unit)%unit;y<h;y+=unit){g.strokeStyle='#7890b011';g.beginPath();g.moveTo(0,y);g.lineTo(w,y);g.stroke();}}
 if(detailId){drawDetail(g,w,h,c);}
 else{
  const visible=visibleRooms(),ids=new Set(visible.map(r=>r.id)),reachable=reachableRooms(doc,enabled);
  for(const l of doc.links){if(!ids.has(l.from)||!ids.has(l.to))continue;const pts=curve(l,c,w,h),[a,b,cc,d]=pts;const locked=preview&&!linkPassable(l,enabled);const color=l.requires.length?'#d9b777':['depth','teleport'].includes(l.type)?'#b39cda':l.type==='drop'?'#dd8c8d':'#91adcc';g.save();g.strokeStyle=locked?'#6d4548':color;g.globalAlpha=locked?.5:selection?.id===l.id?1:.65;g.lineWidth=selection?.id===l.id?3:1.6;g.setLineDash(['depth','teleport'].includes(l.type)?[6,5]:[]);g.beginPath();g.moveTo(a.x,a.y);g.bezierCurveTo(b.x,b.y,cc.x,cc.y,d.x,d.y);g.stroke();g.setLineDash([]);
   if(l.oneWay){const p=bez(pts,.6),q=bez(pts,.57),angle=Math.atan2(p.y-q.y,p.x-q.x);g.fillStyle=color;g.beginPath();g.moveTo(p.x,p.y);g.lineTo(p.x-10*Math.cos(angle-.45),p.y-10*Math.sin(angle-.45));g.lineTo(p.x-10*Math.cos(angle+.45),p.y-10*Math.sin(angle+.45));g.fill();}
   if(c.zoom>.35){const p=bez(pts,.5),label=l.requires.length?'◇ '+l.requires.map(id=>doc.flags.find(f=>f.id===id)?.name).join(l.logic==='all'?' + ':' / '):['depth','teleport','drop'].includes(l.type)?LINK_TYPES[l.type]:'';if(label){g.font='10px system-ui';const tw=g.measureText(label).width;g.fillStyle='#111923';g.fillRect(p.x-tw/2-5,p.y-8,tw+10,16);text(g,label,p.x,p.y+3,color,10,'center');}}
   g.restore();
  }
  for(const r of visible){const p=screen(r.x,r.y,c,w,h),rw=r.w*unit,rh=r.h*unit,selected=selection?.type==='room'&&selection.id===r.id;
   g.save();g.globalAlpha=preview&&!reachable.has(r.id)?.28:1;g.shadowColor='#0007';g.shadowBlur=selected?16:7;rounded(g,p.x,p.y,rw,rh,5);g.fillStyle=selected?'#263d54':'#1c2c3e';g.fill();g.shadowBlur=0;g.strokeStyle=preview&&reachable.has(r.id)?'#7ccdb5':selected?'#e4c98c':r.color;g.lineWidth=selected?2.2:1.1;g.stroke();
   g.save();g.beginPath();g.rect(p.x+2,p.y+2,Math.max(0,rw-4),Math.max(0,rh-4));g.clip();g.fillStyle=r.color+'33';g.fillRect(p.x,p.y+rh-8,rw,8);g.strokeStyle=r.color+'25';g.lineWidth=1;for(let j=1;j<r.w;j++){g.beginPath();g.moveTo(p.x+j*unit,p.y);g.lineTo(p.x+j*unit,p.y+rh);g.stroke();}
   const name=(doc.startRoomId===r.id?'⌂ ':'')+r.name;
   text(g,name,p.x+rw/2,p.y+rh*.44,selected?'#f6e3b9':'#d8e5f3',Math.max(10,Math.min(13,12*c.zoom+.8)),'center');
   if(rh>40)text(g,`${ROOM_TYPES[r.kind]} · Z${r.depth}`,p.x+rw/2,p.y+rh*.44+19,r.color,10,'center');
   if(rh>65)text(g,r.objects.length?`${r.objects.length} 个室内对象`:'双击编辑室内',p.x+rw/2,p.y+rh-17,'#8298b1',9,'center');g.restore();
   if(selected){g.fillStyle='#e4c98c';g.fillRect(p.x+rw-7,p.y+rh-7,7,7);}
   for(const type of new Set(doc.links.filter(l=>l.from===r.id||l.to===r.id).map(l=>l.from===r.id?l.fromPort:l.toPort))){const q=screen(...Object.values(port(r,type)),c,w,h);g.fillStyle=['front','back'].includes(type)?'#c5a5e4':r.color;g.beginPath();g.arc(q.x,q.y,3.5,0,Math.PI*2);g.fill();if(['front','back'].includes(type)&&c.zoom>.5)text(g,type==='front'?'前':'后',q.x+7,q.y+3,'#c5a5e4',9);}
   if(linkSource===r.id){g.strokeStyle='#e4c98c';g.setLineDash([4,4]);g.strokeRect(p.x-5,p.y-5,rw+10,rh+10);g.setLineDash([]);}g.restore();
  }
 }
 if(drag?.kind==='create'){const a=screen(drag.a.x,drag.a.y,c,w,h),b=screen(drag.b.x,drag.b.y,c,w,h);g.fillStyle='#b1d4f022';g.strokeStyle='#b1d4f0';g.fillRect(Math.min(a.x,b.x),Math.min(a.y,b.y),Math.abs(a.x-b.x),Math.abs(a.y-b.y));g.strokeRect(Math.min(a.x,b.x),Math.min(a.y,b.y),Math.abs(a.x-b.x),Math.abs(a.y-b.y));}
 if(!exporting)$('#hud').textContent=`${Math.round(c.zoom*100)}% · ${detailId?'室内 4× 子网格':depth===null?'全部景深':'Z'+depth}`;
}
function drawDetail(g,w,h,c){
 const r=room(detailId);if(!r)return;const p=screen(0,0,c,w,h),unit=GRID*c.zoom,rw=r.w*4*unit,rh=r.h*4*unit;
 g.fillStyle='#152434';g.fillRect(p.x,p.y,rw,rh);g.strokeStyle=r.color;g.lineWidth=2;g.strokeRect(p.x,p.y,rw,rh);
 g.save();g.beginPath();g.rect(p.x,p.y,rw,rh);g.clip();
 if(unit>4){for(let x=0;x<=r.w*4;x++){g.strokeStyle=x%4===0?'#6d8aab40':'#6d8aab17';g.beginPath();g.moveTo(p.x+x*unit,p.y);g.lineTo(p.x+x*unit,p.y+rh);g.stroke();}for(let y=0;y<=r.h*4;y++){g.strokeStyle=y%4===0?'#6d8aab40':'#6d8aab17';g.beginPath();g.moveTo(p.x,p.y+y*unit);g.lineTo(p.x+rw,p.y+y*unit);g.stroke();}}
 for(const o of r.objects){const t=OBJECT_TYPES[o.type],q=screen(o.x,o.y,c,w,h),ow=o.w*unit,oh=o.h*unit;g.fillStyle=t.color+(o.type==='solid'?'80':'33');g.strokeStyle=selection?.id===o.id?'#f1d08c':t.color;g.lineWidth=selection?.id===o.id?2:1;rounded(g,q.x,q.y,ow,oh,Math.min(3,ow/4,oh/4));g.fill();g.stroke();if(ow>24&&oh>18){g.save();g.beginPath();g.rect(q.x,q.y,ow,oh);g.clip();text(g,`${t.glyph} ${o.label||t.name}`,q.x+5,q.y+Math.min(oh/2+4,20),t.color,Math.max(10,Math.min(12,unit*.9)));g.restore();}if(selection?.id===o.id){g.fillStyle='#f1d08c';g.fillRect(q.x+ow-7,q.y+oh-7,7,7);}}
 g.restore();
 for(const l of doc.links.filter(l=>l.from===r.id||l.to===r.id)){const po=l.from===r.id?l.fromPort:l.toPort,pr=port({x:0,y:0,w:r.w*4,h:r.h*4},po),q=screen(pr.x,pr.y,c,w,h);g.fillStyle='#d9b777';g.fillRect(q.x-5,q.y-5,10,10);text(g,PORTS[po]+' → '+(room(l.from===r.id?l.to:l.from)?.name||''),q.x+10,q.y-8,'#d9b777',11);}
}
function hitRoom(p){const a=world(p);return[...visibleRooms()].reverse().find(r=>a.x>=r.x&&a.x<=r.x+r.w&&a.y>=r.y&&a.y<=r.y+r.h);}
function hitObject(p){const a=world(p);return[...(room(detailId)?.objects||[])].reverse().find(o=>a.x>=o.x&&a.x<=o.x+o.w&&a.y>=o.y&&a.y<=o.y+o.h);}
function hitLink(p){const ids=new Set(visibleRooms().map(r=>r.id));return [...doc.links].reverse().find(l=>{if(!ids.has(l.from)||!ids.has(l.to))return false;const pts=curve(l);for(let t=0;t<=1;t+=.025){const q=bez(pts,t);if(Math.hypot(q.x-p.x,q.y-p.y)<8)return true;}return false;});}
function connectTo(r){
 if(!linkSource){linkSource=r.id;selection={type:'room',id:r.id};render();return;}
 if(linkSource===r.id){linkSource=null;render();return;}
 const a=room(linkSource),dz=r.depth-a.depth,dy=r.y-a.y,dx=r.x-a.x;
 const type=dz?'depth':Math.abs(dy)>Math.abs(dx)?'stairs':'walk',fromPort=dz?(dz>0?'back':'front'):type==='stairs'?(dy>0?'down':'up'):dx>=0?'right':'left';
 const toPort={back:'front',front:'back',down:'up',up:'down',right:'left',left:'right'}[fromPort];
 mutate(()=>{const l={id:uid('link'),from:a.id,to:r.id,fromPort,toPort,type,oneWay:false,requires:[],logic:'all',notes:''};doc.links.push(l);selection={type:'link',id:l.id};linkSource=null;});
}
canvas.addEventListener('pointerdown',e=>{
 if(view3D){
  if(![0,1,2].includes(e.button))return;canvas.focus();canvas.setPointerCapture(e.pointerId);
  drag={kind:'orbit',p:pos(e),camera:{...orbit.camera},pan:e.button!==0||e.shiftKey||space,moved:false};return;
 }
 if(e.button!==0&&e.button!==1)return;canvas.focus();canvas.setPointerCapture(e.pointerId);const p=pos(e),a=world(p);
 if(e.button===1||space||tool==='pan'){drag={kind:'pan',p,cam:{...cam}};return;}
 if(tool==='link'&&!detailId){const r=hitRoom(p);if(r)connectTo(r);else{const l=hitLink(p);if(l){selection={type:'link',id:l.id};render();}}return;}
 const hit=detailId?hitObject(p):hitRoom(p),l=!detailId&&!hit?hitLink(p):null;
 if(tool==='erase'){if(hit||l){selection={type:l?'link':detailId?'object':'room',id:(hit||l).id};deleteSelection();}return;}
 if(tool==='room'){
  if(detailId){const r=room(detailId);if(a.x<0||a.y<0||a.x>=r.w*4||a.y>=r.h*4)return;}
  drag={kind:'create',a:{x:Math.floor(a.x),y:Math.floor(a.y)},b:{x:Math.floor(a.x)+1,y:Math.floor(a.y)+1}};draw();return;
 }
 if(hit){selection={type:detailId?'object':'room',id:hit.id};const corner=screen(hit.x+hit.w,hit.y+hit.h);drag={kind:Math.hypot(corner.x-p.x,corner.y-p.y)<13?'resize':'move',target:hit,original:clone(hit),a,backup:JSON.stringify(doc)};}
 else{selection=l?{type:'link',id:l.id}:null;}
 render();
});
canvas.addEventListener('pointermove',e=>{
 if(!drag)return;const p=pos(e);
 if(drag.kind==='orbit'){const dx=p.x-drag.p.x,dy=p.y-drag.p.y;drag.moved=drag.moved||Math.hypot(dx,dy)>4;if(drag.pan){orbit.camera.panX=drag.camera.panX+dx;orbit.camera.panY=drag.camera.panY+dy;}else{orbit.camera.yaw=drag.camera.yaw+dx*.008;orbit.camera.pitch=clampPitch(drag.camera.pitch+dy*.008);}draw();return;}
 const a=world(p);
 if(drag.kind==='pan'){cam.x=drag.cam.x-(p.x-drag.p.x)/cam.zoom;cam.y=drag.cam.y-(p.y-drag.p.y)/cam.zoom;draw();return;}
 if(drag.kind==='create'){
  let x=Math.ceil(a.x),y=Math.ceil(a.y);if(detailId){const r=room(detailId);x=Math.max(0,Math.min(r.w*4,x));y=Math.max(0,Math.min(r.h*4,y));}drag.b={x,y};draw();return;
 }
 const t=drag.target,o=drag.original,dx=Math.round(a.x-drag.a.x),dy=Math.round(a.y-drag.a.y);
 if(drag.kind==='move'){t.x=o.x+dx;t.y=o.y+dy;if(detailId){const r=room(detailId);t.x=Math.max(0,Math.min(r.w*4-t.w,t.x));t.y=Math.max(0,Math.min(r.h*4-t.h,t.y));}}
 else{
  let minW=1,minH=1,maxW=128,maxH=64;
  if(detailId){maxW=room(detailId).w*4-t.x;maxH=room(detailId).h*4-t.y;}
  else if(t.objects.length){minW=Math.ceil(Math.max(...t.objects.map(v=>v.x+v.w))/4);minH=Math.ceil(Math.max(...t.objects.map(v=>v.y+v.h))/4);}
  t.w=Math.max(minW,Math.min(maxW,o.w+dx));t.h=Math.max(minH,Math.min(maxH,o.h+dy));
 }
 draw();
});
function endDrag(){
 if(!drag)return;const d=drag;drag=null;
 if(d.kind==='create'){
  const x=Math.min(d.a.x,d.b.x),y=Math.min(d.a.y,d.b.y),w=Math.max(1,Math.abs(d.b.x-d.a.x)),h=Math.max(1,Math.abs(d.b.y-d.a.y));
  mutate(()=>{if(detailId){const r=room(detailId);const o={id:uid('object'),type:objectType,x,y,w:Math.min(w,r.w*4-x),h:Math.min(h,r.h*4-y),label:'',flagId:null};if(o.w>0&&o.h>0){r.objects.push(o);selection={type:'object',id:o.id};}}else{const r={...newRoom(doc.rooms.length+1,activeDepth,x,y),w:Math.min(128,w),h:Math.min(64,h)};doc.rooms.push(r);selection={type:'room',id:r.id};if(!doc.startRoomId)doc.startRoomId=r.id;}});
 }else if(d.backup&&JSON.stringify(doc)!==d.backup){undoStack.push(d.backup);if(undoStack.length>100)undoStack.shift();redoStack.length=0;changed();}else render();
}
canvas.addEventListener('pointerup',e=>{if(drag?.kind==='orbit'){const d=drag;drag=null;if(!d.moved&&!d.pan){const id=orbit.pick(pos(e));selection=id?{type:'room',id}:null;render();}return;}endDrag();});
canvas.addEventListener('contextmenu',e=>{if(view3D)e.preventDefault();});
canvas.addEventListener('pointercancel',()=>{if(drag?.backup)doc=JSON.parse(drag.backup);drag=null;render();});
canvas.addEventListener('dblclick',e=>{if(view3D){const id=orbit.pick(pos(e));if(id)openDetail(id);return;}if(detailId||tool!=='select')return;const r=hitRoom(pos(e));if(r)openDetail(r.id);});
canvas.addEventListener('wheel',e=>{e.preventDefault();if(view3D){orbit.camera.zoom=Math.max(.15,Math.min(8,orbit.camera.zoom*Math.exp(-Math.max(-120,Math.min(120,e.deltaY))*.002)));draw();return;}const p=pos(e),a=world(p);cam.zoom=Math.max(.1,Math.min(5,cam.zoom*Math.exp(-Math.max(-120,Math.min(120,e.deltaY))*.002)));cam.x=a.x*GRID-(p.x-width/2)/cam.zoom;cam.y=a.y*GRID-(p.y-height/2)/cam.zoom;draw();},{passive:false});
window.addEventListener('keydown',e=>{
 if($('#dialog').open)return;
 if(['INPUT','TEXTAREA','SELECT'].includes(e.target.tagName))return;
 const key=e.key.toLowerCase(),mod=e.ctrlKey||e.metaKey;
 if(!mod&&key==='3'){set3D(!view3D);return;}
 if(view3D){
  if(mod&&key==='s'){e.preventDefault();$('#export').click();return;}
  if(key==='escape'){set3D(false);return;}if(key==='f'){fit();return;}
  if(key===' '){e.preventDefault();space=true;return;}
  const steps={arrowleft:[-.12,0],arrowright:[.12,0],arrowup:[0,-.1],arrowdown:[0,.1]};
  if(steps[key]){e.preventDefault();orbit.camera.yaw+=steps[key][0];orbit.camera.pitch=clampPitch(orbit.camera.pitch+steps[key][1]);draw();}return;
 }
 if(mod&&key==='z'){e.preventDefault();undo(e.shiftKey);return;}if(mod&&key==='y'){e.preventDefault();undo(true);return;}if(mod&&key==='s'){e.preventDefault();$('#export').click();return;}if(mod)return;
 if(key===' '){e.preventDefault();space=true;return;}
 if(key==='escape'){if(drag){if(drag.backup)doc=JSON.parse(drag.backup);drag=null;}else if(linkSource)linkSource=null;else if(selection?.type==='object')selection=null;else if(detailId){$('#back').click();return;}else selection=null;render();return;}
 if(key==='delete'||key==='backspace'){e.preventDefault();deleteSelection();return;}
 if(key==='enter'&&selectedRoom()&&!detailId){openDetail(selectedRoom().id);return;}
 if(key==='f'){fit();return;}const tools={v:'select',r:'room',b:'room',c:'link',e:'erase',h:'pan'};if(tools[key]&&!(detailId&&key==='c')){tool=tools[key];linkSource=null;render();}
});
window.addEventListener('keyup',e=>{if(e.key===' ')space=false;});window.addEventListener('blur',()=>space=false);
window.addEventListener('beforeunload',e=>{if(dirty||saveError||drag?.backup){e.preventDefault();e.returnValue='';}});
$('#png').onclick=()=>{
 if(view3D){const out=document.createElement('canvas');out.width=Math.round(width*2);out.height=Math.round(height*2);const g=out.getContext('2d');g.scale(2,2);orbit.render(g,width,height,doc,visibleRooms(),{selection,preview,enabled,exporting:true});out.toBlob(blob=>{if(blob)download(blob,filename()+'-3D预览.png');else notify('PNG 生成失败。');});return;}
 const b=bounds(),out=document.createElement('canvas');const margin=detailId?2:4;
 const z=Math.min(2,6000/((b.w+margin)*GRID),6000/((b.h+margin)*GRID));out.width=Math.ceil((b.w+margin)*GRID*z);out.height=Math.ceil((b.h+margin)*GRID*z);
 const c={x:(b.x+b.w/2)*GRID,y:(b.y+b.h/2)*GRID,zoom:z};draw(out.getContext('2d'),out.width,out.height,c,true);
 out.toBlob(blob=>{if(blob)download(blob,filename()+(detailId?'-室内':depth===null?'-总览':'-景深'+depth)+'.png');else notify('PNG 生成失败。');});
};
new ResizeObserver(()=>{const r=$('#canvas-wrap').getBoundingClientRect();width=r.width;height=r.height;dpr=Math.min(2,devicePixelRatio||1);canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);ctx.setTransform(dpr,0,0,dpr,0,0);draw();}).observe($('#canvas-wrap'));
(async()=>{
 await store.init();let records=[];
 try{records=await store.list();}catch(e){notify('无法读取 NAS 地图库：'+e.message);}
 let last=null;try{last=localStorage.getItem('lenneth:last');}catch{}
 let backups=[];try{backups=await store.backups();}catch(e){notify('无法读取本机备份：'+e.message);}
 const pending=backups.find(r=>r.id===last&&r.dirty);
 if(pending){setDocument({...pending,doc:pending.doc});notify('上次有未同步修改，已保留在画布。请重试保存，或另存副本。');}
 else if(records.length)setDocument(records.find(r=>r.doc.id===last)||records[0]);
 else if(store.remote){setDocument({doc:sampleProject(),revision:null});status('原创示例 · 编辑后自动保存');}
 else if(backups.length)setDocument(backups.find(r=>r.id===last)||backups[0]);
 else {setDocument({doc:sampleProject(),revision:null});status('原创示例 · 编辑后保存到本机');}
})().catch(e=>{notify(e.message);render();fit();});
