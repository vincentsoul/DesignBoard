// Dependency-free orthographic 3D scene. Camera state never changes project data.
import {OBJECT_TYPES,linkPassable,reachableRooms} from './model.js';
export const DEFAULT_CAMERA = {yaw:.62,pitch:.42,zoom:1,panX:0,panY:0};
export const clampPitch = value => Math.max(-1.48,Math.min(1.48,value));
export function roomCorners(room,gap=12,thickness=2){
 const x=room.x,y=-room.y,z=room.depth*gap,t=thickness/2;
 return [[x,y,z-t],[x+room.w,y,z-t],[x+room.w,y-room.h,z-t],[x,y-room.h,z-t],
         [x,y,z+t],[x+room.w,y,z+t],[x+room.w,y-room.h,z+t],[x,y-room.h,z+t]];
}
export function rotatePoint(point,camera,target=[0,0,0]){
 const [x,y,z]=point.map((v,i)=>v-target[i]),cy=Math.cos(camera.yaw),sy=Math.sin(camera.yaw),cp=Math.cos(camera.pitch),sp=Math.sin(camera.pitch);
 const rx=x*cy+z*sy,rz=-x*sy+z*cy;
 return {x:rx,y:-y*cp-rz*sp,depth:y*sp-rz*cp};
}
export function projectPoint(point,camera,target,width,height,scale){
 const p=rotatePoint(point,camera,target);
 return {x:width/2+camera.panX+p.x*scale*camera.zoom,y:height/2+camera.panY+p.y*scale*camera.zoom,depth:p.depth};
}
export function sceneBounds(rooms,gap=12){
 if(!rooms.length)return {target:[0,0,0],points:[],min:[-4,-4,-4],max:[4,4,4]};
 const points=rooms.flatMap(r=>roomCorners(r,gap)),min=[0,1,2].map(i=>Math.min(...points.map(p=>p[i]))),max=[0,1,2].map(i=>Math.max(...points.map(p=>p[i])));
 return {points,min,max,target:min.map((v,i)=>(v+max[i])/2)};
}
export function fitScale(points,camera,target,width,height){
 if(!points.length)return 20;
 const rotated=points.map(p=>rotatePoint(p,camera,target));
 const extentX=Math.max(1,...rotated.map(p=>Math.abs(p.x)))*2,extentY=Math.max(1,...rotated.map(p=>Math.abs(p.y)))*2;
 return Math.max(.00001,Math.min(60,Math.max(40,width-150)/extentX,Math.max(40,height-210)/extentY));
}
export function port3D(room,port,gap=12){
 const x=room.x,y=-room.y,z=room.depth*gap;
 if(port==='left')return[x,y-room.h/2,z];
 if(port==='right')return[x+room.w,y-room.h/2,z];
 if(port==='up')return[x+room.w/2,y,z];
 if(port==='down')return[x+room.w/2,y-room.h,z];
 return[x+room.w/2,y-room.h/2,z+(port==='back'?1:-1)];
}
export function pointInPolygon(point,polygon){
 let inside=false;
 for(let i=0,j=polygon.length-1;i<polygon.length;j=i++){
  const a=polygon[i],b=polygon[j];
  if((a.y>point.y)!==(b.y>point.y)&&point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
 }
 return inside;
}
function tint(hex,factor){const n=parseInt(hex.slice(1),16);return `rgb(${[16,8,0].map(b=>Math.round(((n>>b)&255)*factor)).join(',')})`;}
const faces=[[0,1,2,3],[5,4,7,6],[4,0,3,7],[1,5,6,2],[4,5,1,0],[3,2,6,7]];
function path(ctx,points){ctx.beginPath();points.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.closePath();}
function label(ctx,value,x,y,color,size=11){ctx.font=`${size}px system-ui,"PingFang SC","Microsoft YaHei",sans-serif`;ctx.textAlign='center';const w=ctx.measureText(value).width;ctx.fillStyle='#111c29e8';ctx.fillRect(x-w/2-5,y-size,w+10,size+6);ctx.fillStyle=color;ctx.fillText(value,x,y);}
export class Preview3D {
 constructor(){this.camera={...DEFAULT_CAMERA};this.gap=18;this.labels=true;this.interiors=false;this.guides=true;this.scale=20;this.target=[0,0,0];this.hitFaces=[];}
 fit(rooms,width,height){const b=sceneBounds(rooms,this.gap);this.target=b.target;this.camera.zoom=1;this.camera.panX=this.camera.panY=0;this.scale=fitScale(b.points,this.camera,this.target,width,height);}
 preset(name,rooms,width,height){const views={iso:[.62,.42],front:[0,0],top:[0,1.48],side:[Math.PI/2,0]};const angles=views[name]||views.iso;this.camera.yaw=angles[0];this.camera.pitch=angles[1];this.fit(rooms,width,height);}
 pick(p){return [...this.hitFaces].reverse().find(f=>pointInPolygon(p,f.points))?.roomId||null;}
 render(ctx,width,height,doc,rooms,{selection=null,preview=false,enabled=new Set(),exporting=false}={}){
  const proj=p=>projectPoint(p,this.camera,this.target,width,height,this.scale),items=[],labels=[],ids=new Set(rooms.map(r=>r.id)),byId=new Map(rooms.map(r=>[r.id,r]));
  const reachable=preview?reachableRooms(doc,enabled):null;
  ctx.clearRect(0,0,width,height);ctx.fillStyle='#101923';ctx.fillRect(0,0,width,height);this.hitFaces=[];
  const bounds=sceneBounds(rooms,this.gap),floor=bounds.min[1]-2;
  // Reference ground plane, deliberately sparse even on large imported maps.
  const spanX=Math.max(12,bounds.max[0]-bounds.min[0]+8),spanZ=Math.max(16,bounds.max[2]-bounds.min[2]+12),step=Math.max(4,Math.ceil(Math.max(spanX,spanZ)/35/4)*4);
  ctx.strokeStyle='#6888a51b';ctx.lineWidth=1;
  for(let x=bounds.min[0]-4;x<=bounds.max[0]+4;x+=step){const a=proj([x,floor,bounds.min[2]-6]),b=proj([x,floor,bounds.max[2]+6]);ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke();}
  for(let z=bounds.min[2]-6;z<=bounds.max[2]+6;z+=step){const a=proj([bounds.min[0]-4,floor,z]),b=proj([bounds.max[0]+4,floor,z]);ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke();}
  if(this.guides)for(const depth of new Set(rooms.map(r=>r.depth))){
   const group=rooms.filter(r=>r.depth===depth),x0=Math.min(...group.map(r=>r.x))-1,x1=Math.max(...group.map(r=>r.x+r.w))+1,y0=Math.max(...group.map(r=>-r.y))+1,y1=Math.min(...group.map(r=>-r.y-r.h))-1;
   const points=[[x0,y0,depth*this.gap],[x1,y0,depth*this.gap],[x1,y1,depth*this.gap],[x0,y1,depth*this.gap]].map(proj);
   ctx.strokeStyle='#9d9bb742';ctx.setLineDash([4,5]);path(ctx,points);ctx.stroke();ctx.setLineDash([]);
   if(this.labels)label(ctx,`Z ${depth} · ${depth<0?'前景':depth>0?'后景':'主景'}`,points[0].x,points[0].y-12,'#9eafc7',10);
  }
  for(const room of rooms){
   const corners=roomCorners(room,this.gap),selected=selection?.type==='room'&&selection.id===room.id,dim=preview&&!reachable.has(room.id);
   for(let i=0;i<faces.length;i++){
    const points=faces[i].map(n=>proj(corners[n]));
    const signed=points.reduce((sum,p,j)=>{const q=points[(j+1)%points.length];return sum+p.x*q.y-q.x*p.y;},0);
    if(Math.abs(signed)<.01)continue;
    // Both sides render; painter ordering hides back faces and supports a full orbit.
    items.push({kind:'face',points,roomId:room.id,depth:points.reduce((s,p)=>s+p.depth,0)/4,color:tint(room.color,[.28,.23,.2,.35,.48,.16][i]),stroke:selected?'#edd398':preview&&reachable.has(room.id)?'#7ccdb5':room.color,selected,dim});
   }
   const nearZ=room.depth*this.gap+(Math.cos(this.camera.yaw)>=0?-1.015:1.015);
   if(this.interiors)for(const object of room.objects){
    const v=OBJECT_TYPES[object.type],x=room.x+object.x/4,y=-room.y-object.y/4;
    const points=[[x,y,nearZ],[x+object.w/4,y,nearZ],[x+object.w/4,y-object.h/4,nearZ],[x,y-object.h/4,nearZ]].map(proj);
    items.push({kind:'object',points,roomId:room.id,depth:points.reduce((s,p)=>s+p.depth,0)/4+.001,color:v.color,dim});
   }
   if(this.labels){const p=proj([room.x+room.w/2,-room.y-room.h*.42,nearZ]);labels.push({kind:'label',...p,roomId:room.id,name:(doc.startRoomId===room.id?'⌂ ':'')+room.name,z:room.depth,selected,dim});}
  }
  // Split paths into short depth-sorted segments so lines disappear behind rooms.
  for(const link of doc.links){
   if(!ids.has(link.from)||!ids.has(link.to))continue;
   const a=port3D(byId.get(link.from),link.fromPort,this.gap),b=port3D(byId.get(link.to),link.toPort,this.gap),points=[];
   for(let i=0;i<=24;i++)points.push(proj(a.map((v,j)=>v+(b[j]-v)*i/24)));
   const color=preview&&!linkPassable(link,enabled)?'#75494f':link.requires.length?'#d9b777':['depth','teleport'].includes(link.type)?'#b39cda':link.type==='drop'?'#dd8c8d':'#91adcc';
   for(let i=1;i<points.length;i++)items.push({kind:'line',points:[points[i-1],points[i]],depth:(points[i-1].depth+points[i].depth)/2,color,dashed:['depth','teleport'].includes(link.type),arrow:link.oneWay&&i===14});
  }
  items.sort((a,b)=>a.depth-b.depth);
  for(const item of items){
   ctx.save();ctx.globalAlpha=item.dim?.27:1;
   if(item.kind==='face'){
    path(ctx,item.points);ctx.fillStyle=item.color;ctx.fill();ctx.strokeStyle=item.stroke;ctx.lineWidth=item.selected?2:1;ctx.stroke();this.hitFaces.push(item);
   }else if(item.kind==='object'){
    path(ctx,item.points);ctx.fillStyle=item.color+'88';ctx.fill();ctx.strokeStyle=item.color;ctx.lineWidth=.8;ctx.stroke();
   }else{
    const[a,b]=item.points;ctx.strokeStyle=item.color;ctx.lineWidth=2;ctx.setLineDash(item.dashed?[3,3]:[]);ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke();
    if(item.arrow){ctx.setLineDash([]);const angle=Math.atan2(b.y-a.y,b.x-a.x);ctx.fillStyle=item.color;ctx.beginPath();ctx.moveTo(b.x,b.y);ctx.lineTo(b.x-10*Math.cos(angle-.45),b.y-10*Math.sin(angle-.45));ctx.lineTo(b.x-10*Math.cos(angle+.45),b.y-10*Math.sin(angle+.45));ctx.fill();}
   }
   ctx.restore();
  }
  // Names are screen-facing annotations so they remain legible from behind.
  for(const item of labels.sort((a,b)=>a.depth-b.depth)){ctx.save();ctx.globalAlpha=item.dim?.4:1;label(ctx,item.name,item.x,item.y,item.selected?'#f2d69a':'#dfebf8');label(ctx,`Z ${item.z}`,item.x,item.y+17,'#8da8bf',9);ctx.restore();}
  // Orientation triad follows orbit but is independent of pan and zoom.
  const origin={x:65,y:height-85};ctx.lineWidth=2;
  for(const[p,name,color]of [[[1,0,0],'横向 X','#dfb27c'],[[0,1,0],'楼层 ↑','#88c5a8'],[[0,0,1],'景深 Z','#ad9bd7']]){
   const a=rotatePoint(p,this.camera),end={x:origin.x+a.x*32,y:origin.y+a.y*32};ctx.strokeStyle=color;ctx.beginPath();ctx.moveTo(origin.x,origin.y);ctx.lineTo(end.x,end.y);ctx.stroke();label(ctx,name,end.x+(end.x-origin.x)*.7,end.y+(end.y-origin.y)*.5,color,9);
  }
  if(!exporting){ctx.font='10px system-ui';ctx.textAlign='right';ctx.fillStyle='#7c94ad';ctx.fillText('正交 3D · 景深间距为示意，不是原作测绘',width-16,height-65);}
 }
}
