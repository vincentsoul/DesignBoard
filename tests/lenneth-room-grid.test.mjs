import test from 'node:test';
import assert from 'node:assert/strict';
import {newRoom,newProject,validateProject,audit} from '../prototypes/lenneth-map/model.js';
import {packCells,editRoomCells,brushCells} from '../prototypes/lenneth-map/room-grid.js';
const occupied=(room,type)=>{const cells=new Set();for(const o of room.objects.filter(o=>o.type===type))for(let y=o.y;y<o.y+o.h;y++)for(let x=o.x;x<o.x+o.w;x++)cells.add(x+','+y);return cells;};
test('a fast drag fills every crossed grid cell, clips brush at room boundary, and packs long platforms',()=>{
 const r=newRoom();const cells=brushCells(r,{x:0,y:3},{x:31,y:3},1);assert.equal(cells.size,32);
 r.objects=editRoomCells(r,cells,'solid');assert.equal(r.objects.length,1);assert.deepEqual([...occupied(r,'solid')],[...cells]);
 const edge=brushCells(r,{x:30,y:14},{x:33,y:18},4);assert.ok(edge.size>0);assert.ok([...edge].every(k=>{const[x,y]=k.split(',').map(Number);return x<32&&y<16;}));
 const before=JSON.stringify(r.objects);r.objects=editRoomCells(r,cells,'solid');assert.equal(JSON.stringify(r.objects),before);
});
test('local erasing cuts a hole without deleting the surrounding ground or its condition metadata',()=>{
 const r=newRoom();r.objects=[{id:'ground',type:'solid',x:0,y:0,w:8,h:4,label:'地面',flagId:'flag_1'}];
 const previous=occupied(r,'solid');r.objects=editRoomCells(r,new Set(['3,1','4,1','3,2','4,2']),'solid',true);
 assert.equal(occupied(r,'solid').size,previous.size-4);assert.equal(occupied(r,'solid').has('3,1'),false);assert.equal(occupied(r,'solid').has('2,1'),true);
 assert.ok(r.objects.every(o=>o.label==='地面'&&o.flagId==='flag_1'));assert.equal(new Set(r.objects.map(o=>o.id)).size,r.objects.length);
});
test('painting replaces terrain material but preserves semantic objects and erase can remove a marker',()=>{
 const r=newRoom();r.objects=[{id:'floor',type:'solid',x:0,y:0,w:8,h:2,label:'',flagId:null},{id:'chest',type:'chest',x:3,y:0,w:2,h:2,label:'奖励',flagId:null}];
 r.objects=editRoomCells(r,new Set(['3,0','4,0']),'platform');assert.equal(occupied(r,'solid').has('3,0'),false);assert.equal(occupied(r,'platform').size,2);assert.ok(r.objects.some(o=>o.id==='chest'));
 r.objects=editRoomCells(r,new Set(['3,0']),'solid',true);assert.equal(r.objects.some(o=>o.id==='chest'),false);assert.equal(occupied(r,'platform').has('4,0'),true);
});
test('grid edits and new doorway/key markers round-trip through the existing NAS schema',()=>{
 const d=newProject(),r=newRoom();d.rooms=[r];d.startRoomId=r.id;
 r.objects=editRoomCells(r,brushCells(r,{x:1,y:1},{x:20,y:9},2),'hazard');
 for(const type of ['door','key'])r.objects.push({id:type,type,x:2,y:2,w:2,h:2,label:type,flagId:null});
 assert.deepEqual(validateProject(JSON.parse(JSON.stringify(d))),d);
 assert.throws(()=>editRoomCells(r,new Set(['1,1']),'chest'),/画笔/);
});
test('packing an L shape preserves exactly its occupied cells',()=>{
 const cells=new Set(['0,0','1,0','0,1','0,2']);const rects=packCells(cells);const r={objects:rects.map(o=>({...o,type:'solid'}))};assert.deepEqual(occupied(r,'solid'),cells);
});

test('doorway condition references are not mistaken for condition acquisition points',()=>{
 const d=newProject(),a=newRoom(),b=newRoom();d.rooms=[a,b];d.startRoomId=a.id;d.flags=[{id:'flag_gate',name:'封印钥匙',initial:false}];
 d.links=[{id:'gate_link',from:a.id,to:b.id,fromPort:'right',toPort:'left',type:'mechanism',oneWay:false,requires:['flag_gate'],logic:'all',notes:''}];
 a.objects=[{id:'door_marker',type:'door',x:1,y:1,w:2,h:4,label:'出口',flagId:'flag_gate'}];
 assert.ok(audit(d).some(s=>s.includes('没有获取点')));
 a.objects.push({id:'key_marker',type:'key',x:4,y:1,w:2,h:2,label:'钥匙',flagId:'flag_gate'});
 assert.equal(audit(d).some(s=>s.includes('没有获取点')),false);validateProject(d);
});
