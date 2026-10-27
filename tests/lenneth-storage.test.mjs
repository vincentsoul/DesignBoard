import test from 'node:test';
import assert from 'node:assert/strict';
import {ProjectStorage} from '../prototypes/lenneth-map/storage.js';
import {sampleProject} from '../prototypes/lenneth-map/model.js';
test('autosave serializes revisions, keeps dirty backup on conflict, and restarts with original baseline',async t=>{
 const old={location:globalThis.location,localStorage:globalThis.localStorage,fetch:globalThis.fetch};
 const cache=new Map(),writes=[];let conflict=false,revision='r0';
 globalThis.location={pathname:'/lenneth-map/'};
 globalThis.localStorage={setItem:(k,v)=>{cache.set(k,v);},getItem:k=>cache.get(k),removeItem:k=>cache.delete(k)};
 globalThis.fetch=async(url,options={})=>{
  if(!options.method)return {ok:true,json:async()=>({maps:[{doc:sampleProject(),revision:'someone-else'}]})};
  const value=JSON.parse(options.body);writes.push(value);
  if(conflict)return {ok:false,status:409,json:async()=>({error:'conflict'})};
  revision='r'+writes.length;return {ok:true,json:async()=>({revision})};
 };
 t.after(()=>{for(const[k,v]of Object.entries(old)){if(v===undefined)delete globalThis[k];else globalThis[k]=v;}});
 const d=sampleProject(),s=new ProjectStorage();await s.init();s.accept({doc:d,revision:'r0'});
 await Promise.all([s.write({...d,name:'first'}),s.write({...d,name:'second'})]);
 assert.equal(writes[0].baseRevision,'r0');assert.equal(writes[1].baseRevision,'r1');
 await s.list();conflict=true;await assert.rejects(s.write({...d,name:'unsynced'}),e=>e.status===409);
 const record=JSON.parse(cache.get('lenneth:backup:'+d.id));assert.equal(record.dirty,true);assert.equal(record.doc.name,'unsynced');assert.equal(record.revision,'r2');
 const restored=new ProjectStorage();restored.accept(record);conflict=false;await restored.write(record.doc);assert.equal(writes.at(-1).baseRevision,'r2');assert.equal(JSON.parse(cache.get('lenneth:backup:'+d.id)).dirty,false);
});
