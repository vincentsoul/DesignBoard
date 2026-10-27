import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, readdir } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { scryptSync } from 'node:crypto';
import { createCanvasroomServer } from '../server.mjs';
const doc = (id='map_test') => ({ id, version:1, name:'矿井', areas:[{id:'area_1',name:'入口',color:'#fff'}], rooms:[], keys:[], locks:[], doors:[], detail:{} });
async function setup(t, authConfig=null) {
  const dir=await mkdtemp(path.join(os.tmpdir(),'mvmap-'));
  const server=createCanvasroomServer({ boardsDir:dir, authConfig, secureCookie:false });
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  t.after(async()=>{server.closeAllConnections();await new Promise(r=>server.close(r));await rm(dir,{recursive:true,force:true});});
  const url=`http://127.0.0.1:${server.address().port}`;
  const put=(d,rev=null,headers={})=>fetch(`${url}/api/mvmaps/${d.id}`,{method:'PUT',headers:{'content-type':'application/json',...headers},body:JSON.stringify({doc:d,baseRevision:rev})});
  return {dir,url,put};
}
test('NAS map files survive server restart and stale concurrent writes never overwrite',async t=>{
 const {dir,url,put}=await setup(t);
 const first=await put(doc());assert.equal(first.status,200);const {revision}=await first.json();
 const results=await Promise.all([put({...doc(),name:'A'},revision),put({...doc(),name:'B'},revision)]);
 assert.deepEqual(results.map(r=>r.status).sort(),[200,409]);
 const list=await (await fetch(url+'/api/mvmaps')).json();assert.equal(list.maps.length,1);
 const {createMVMapStore}=await import('../src/mvmap-store.mjs');
 const reopened=createMVMapStore(path.join(dir,'mvmap'));assert.equal((await reopened.read('map_test')).doc.name,list.maps[0].doc.name);
 const current=list.maps[0];
 assert.equal((await fetch(url+'/api/mvmaps/map_test',{method:'DELETE',headers:{'if-match':revision}})).status,409);
 assert.equal((await fetch(url+'/api/mvmaps/map_test',{method:'DELETE',headers:{'if-match':current.revision}})).status,200);
 assert.equal((await readdir(path.join(dir,'mvmap','.trash'))).length,1);
 assert.equal((await (await fetch(url+'/api/mvmaps')).json()).maps.length,0);
});
test('NAS rejects malformed files, path traversal, cross-site writes and wrong content type',async t=>{
 const {url,put}=await setup(t);
 assert.equal((await put({...doc(),rooms:[{id:'room_bad',name:'x',cells:['oops']}]})).status,400);
 assert.equal((await put(doc('../escape'))).status,404);
 assert.equal((await put(doc(),null,{origin:'https://evil.example'})).status,403);
 assert.equal((await put(doc(),null,{'content-type':'text/plain'})).status,415);
 assert.equal((await fetch(url+'/mvmap/')).status,200);
 assert.equal((await fetch(url+'/mvmap/sync.js')).status,200);
});
test('NAS APIs and editor reuse login protection',async t=>{
 const salt='a'.repeat(64), passwordHash=scryptSync('test-only',salt,64).toString('hex');
 const {url,put}=await setup(t,{username:'tester',salt,passwordHash});
 assert.equal((await fetch(url+'/api/mvmaps')).status,401);
 assert.equal((await put(doc())).status,401);
 const editor=await fetch(url+'/mvmap/',{redirect:'manual'});assert.equal(editor.status,302);
 const login=await fetch(url+'/api/auth/login',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({username:'tester',password:'test-only'})});
 assert.equal(login.status,200);const cookie=login.headers.get('set-cookie').split(';')[0];
 assert.equal((await put(doc(),null,{cookie})).status,200);
 assert.equal((await fetch(url+'/api/mvmaps',{headers:{cookie}})).status,200);
});
