import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,rm,readdir} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {scryptSync} from 'node:crypto';
import {sampleProject,newProject,clone,validateProject,reachableRooms,removeRoom,removeFlag,audit} from '../prototypes/lenneth-map/model.js';
import {createCanvasroomServer} from '../server.mjs';

test('depth dungeon sample validates and gate condition changes reachability without auto-triggering switches',()=>{
 const d=sampleProject();validateProject(d);assert.equal(d.rooms.length,8);assert.equal(reachableRooms(d).size,7);
 const boss=d.rooms.find(r=>r.kind==='boss');assert.equal(reachableRooms(d).has(boss.id),false);
 assert.equal(reachableRooms(d,new Set(d.flags.map(f=>f.id))).size,8);assert.deepEqual(audit(d),[]);
 const roundTrip=JSON.parse(JSON.stringify(d));assert.deepEqual(validateProject(roundTrip),d);
});
test('AND/OR and one-way edges remain directional even when room layout is reversed',()=>{
 const d=sampleProject(),[a,b]=d.rooms;d.rooms=[a,b];d.startRoomId=b.id;
 d.flags.push({id:'flag_two',name:'钥匙',initial:false});
 d.links=[{id:'link_test',from:a.id,to:b.id,fromPort:'right',toPort:'left',type:'mechanism',oneWay:true,requires:d.flags.map(f=>f.id),logic:'all',notes:''}];
 assert.equal(reachableRooms(d,new Set(d.flags.map(f=>f.id))).has(a.id),false);
 d.startRoomId=a.id;assert.equal(reachableRooms(d,new Set([d.flags[0].id])).has(b.id),false);
 d.links[0].logic='any';assert.equal(reachableRooms(d,new Set([d.flags[0].id])).has(b.id),true);
 b.x=-100;b.y=-80;assert.equal(reachableRooms(d,new Set([d.flags[0].id])).has(b.id),true);
});
test('deleting room or condition cleans connections, markers and start references',()=>{
 const d=sampleProject(),id=d.flags[0].id;removeFlag(d,id);validateProject(d);
 assert.ok(d.links.every(l=>!l.requires.includes(id)));assert.ok(d.rooms.every(r=>r.objects.every(o=>o.flagId!==id)));
 const start=d.startRoomId;removeRoom(d,start);validateProject(d);assert.notEqual(d.startRoomId,start);assert.ok(d.links.every(l=>l.from!==start&&l.to!==start));
});
test('import rejects dangling exits, duplicate IDs, unsafe sizes and out-of-bounds interior geometry',()=>{
 for(const mutate of [d=>d.links[0].to='missing',d=>d.rooms[1].id=d.rooms[0].id,d=>d.rooms[0].w=0,d=>d.rooms[0].depth=21,d=>d.rooms[0].objects[0].w=999,d=>d.links[0].requires=['missing'],d=>d.editor='mvmap']){
  const d=sampleProject();mutate(d);assert.throws(()=>validateProject(d),e=>e.status===400);
 }
});
test('structure audit reports disconnected rooms and conditions without acquisition markers',()=>{
 const d=sampleProject();d.rooms[5].objects=d.rooms[5].objects.filter(o=>o.type!=='switch');d.links=d.links.filter(l=>l.to!==d.rooms[3].id&&l.from!==d.rooms[3].id);
 assert.ok(audit(d).some(s=>s.includes('没有获取点'))===false); // The removed gate no longer requires this condition.
 d.links.push({id:'newgate',from:d.rooms[0].id,to:d.rooms[1].id,fromPort:'right',toPort:'left',type:'mechanism',oneWay:false,requires:[d.flags[0].id],logic:'all',notes:''});
 assert.ok(audit(d).some(s=>s.includes('没有获取点')));assert.ok(audit(d).some(s=>s.includes('守卫之间')));
});
async function setup(t,authConfig=null){
 const dir=await mkdtemp(path.join(os.tmpdir(),'lenneth-')),s=createCanvasroomServer({boardsDir:dir,authConfig,secureCookie:false});await new Promise(r=>s.listen(0,'127.0.0.1',r));
 t.after(async()=>{s.closeAllConnections();await new Promise(r=>s.close(r));await rm(dir,{recursive:true,force:true});});const url=`http://127.0.0.1:${s.address().port}`;
 const put=(doc,baseRevision=null,headers={})=>fetch(url+'/api/lennethmaps/'+doc.id,{method:'PUT',headers:{'content-type':'application/json',...headers},body:JSON.stringify({doc,baseRevision})});return {dir,url,put};
}
test('NAS round-trip is isolated from MVMap and boards; concurrent saves and stale deletes conflict',async t=>{
 const {dir,url,put}=await setup(t),d=sampleProject();const first=await put(d);assert.equal(first.status,200);const {revision}=await first.json();
 const responses=await Promise.all([put({...d,name:'甲'},revision),put({...d,name:'乙'},revision)]);assert.deepEqual(responses.map(r=>r.status).sort(),[200,409]);
 const maps=await(await fetch(url+'/api/lennethmaps')).json();assert.equal(maps.maps.length,1);
 assert.equal((await(await fetch(url+'/api/mvmaps')).json()).maps.length,0);assert.equal((await(await fetch(url+'/api/boards')).json()).boards.length,0);
 assert.equal((await fetch(url+'/api/lennethmaps/'+d.id,{method:'DELETE',headers:{'if-match':revision}})).status,409);
 assert.equal((await fetch(url+'/api/lennethmaps/'+d.id,{method:'DELETE',headers:{'if-match':maps.maps[0].revision}})).status,200);
 assert.equal((await readdir(path.join(dir,'lennethmap','.trash'))).length,1);
});
test('NAS editor and API inherit authentication; all module and manual routes resolve',async t=>{
 const salt='b'.repeat(64),auth={username:'test',salt,passwordHash:scryptSync('test-only',salt,64).toString('hex')};const {url,put}=await setup(t,auth);
 assert.equal((await fetch(url+'/api/lennethmaps')).status,401);assert.equal((await put(sampleProject())).status,401);
 assert.equal((await fetch(url+'/lenneth-map/',{redirect:'manual'})).status,302);
 const login=await fetch(url+'/api/auth/login',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({username:'test',password:'test-only'})});const cookie=login.headers.get('set-cookie').split(';')[0];
 for(const route of ['/lenneth-map/','/lenneth-map/app.js','/lenneth-map/model.js','/lenneth-map/storage.js','/lenneth-map/style.css','/lenneth-map/preview3d.js','/lenneth-map/room-grid.js','/lenneth-map/manual.html'])assert.equal((await fetch(url+route,{headers:{cookie}})).status,200,route);
 assert.equal((await put(sampleProject(),null,{cookie})).status,200);
 assert.equal((await put(sampleProject(),null,{cookie,origin:'https://evil.example'})).status,403);
});
