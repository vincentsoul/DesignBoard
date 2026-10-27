import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source=await readFile(new URL('../prototypes/mvmap-zh/sync.js',import.meta.url),'utf8');
test('client keeps edit revision when library refreshes; conflict preserves local backup and retry queue',async()=>{
 const backups=[],puts=[],nodes=new Map();let current='r1';let fail=false;
 const local={mode:'idb',init:async()=>{},put:async d=>backups.push(d),all:async()=>[],del:async()=>{}};
 const context={window:{},location:{pathname:'/mvmap/'},document:{querySelector:s=>{if(!nodes.has(s))nodes.set(s,{});return nodes.get(s);}},fetch:async(url,options={})=>{
   if(!options.method)return{ok:true,json:async()=>({maps:[{doc:{id:'map_1',name:'test'},revision:current}]})};
   const data=JSON.parse(options.body);puts.push(data);
   if(fail)return{ok:false,status:409,json:async()=>({error:'conflict'})};
   return{ok:true,json:async()=>({revision:'saved-'+puts.length})};
 }};
 vm.runInNewContext(source,context);const store=context.window.createMVMapNASStore(local);await store.init();
 const [doc]=await store.all();store.accept(doc);current='r2';await store.all();fail=true;
 await assert.rejects(store.put(doc),/conflict/);assert.equal(puts[0].baseRevision,'r1');assert.equal(backups.length,1);assert.equal(store.failed,true);
 fail=false;const [fresh]=await store.all();store.accept(fresh);await store.put(fresh);
 assert.equal(puts[1].baseRevision,'r2');assert.equal(store.failed,false);
 await Promise.all([store.put({...doc,name:'first'}),store.put({...doc,name:'second'})]);
 assert.equal(puts[3].baseRevision,'saved-3');assert.equal(puts[3].doc.name,'second');
});
