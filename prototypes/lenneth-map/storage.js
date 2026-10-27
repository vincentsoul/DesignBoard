export class ProjectStorage {
 constructor(){this.remote=location.pathname.startsWith('/lenneth-map/');this.revisions=new Map();this.queue=Promise.resolve();this.db=null;}
 async init(){
  try{this.db=await new Promise((resolve,reject)=>{const r=indexedDB.open('designboard-lenneth',1);r.onupgradeneeded=()=>r.result.createObjectStore('projects',{keyPath:'id'});r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);r.onblocked=()=>reject(new Error('本地数据库被阻塞'));});}catch{this.db=null;}
 }
 async cache(record){
  if(!this.db){localStorage.setItem('lenneth:backup:'+record.id,JSON.stringify(record));return;}
  await new Promise((resolve,reject)=>{const tx=this.db.transaction('projects','readwrite');tx.objectStore('projects').put(record);tx.oncomplete=resolve;tx.onerror=tx.onabort=()=>reject(tx.error||new Error('本机备份失败'));});
 }
 async backups(){
  if(!this.db)return Object.keys(localStorage).filter(k=>k.startsWith('lenneth:backup:')).map(k=>JSON.parse(localStorage.getItem(k)));
  return new Promise((resolve,reject)=>{const r=this.db.transaction('projects').objectStore('projects').getAll();r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});
 }
 async api(url,options={}){
  const r=await fetch(url,{cache:'no-store',...options});const data=await r.json().catch(()=>({}));
  if(!r.ok)throw Object.assign(new Error(r.status===401?'登录已过期，请在首页重新登录后重试。':data.error||`服务器错误 ${r.status}`),{status:r.status});return data;
 }
 async list(){if(this.remote)return(await this.api('/api/lennethmaps')).maps;return(await this.backups()).map(r=>({doc:r.doc,revision:null}));}
 accept(record){this.revisions.set(record.doc.id,record.revision||null);}
 write(doc){
  const snapshot=JSON.parse(JSON.stringify(doc));
  const next=this.queue.catch(()=>{}).then(async()=>{
   const revision=this.revisions.get(snapshot.id)||null;
   await this.cache({id:snapshot.id,doc:snapshot,revision,dirty:true});
   if(!this.remote){await this.cache({id:snapshot.id,doc:snapshot,revision:null,dirty:false});return;}
   const result=await this.api('/api/lennethmaps/'+encodeURIComponent(snapshot.id),{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({doc:snapshot,baseRevision:revision})});
   this.revisions.set(snapshot.id,result.revision);
   await this.cache({id:snapshot.id,doc:snapshot,revision:result.revision,dirty:false});
  });this.queue=next;return next;
 }
 async remove(record){
  await this.queue.catch(()=>{});
  if(this.remote)await this.api('/api/lennethmaps/'+encodeURIComponent(record.doc.id),{method:'DELETE',headers:{'If-Match':record.revision||''}});
  if(this.db)await new Promise((resolve,reject)=>{const t=this.db.transaction('projects','readwrite');t.objectStore('projects').delete(record.doc.id);t.oncomplete=resolve;t.onerror=()=>reject(t.error);});
  else localStorage.removeItem('lenneth:backup:'+record.doc.id);
  this.revisions.delete(record.doc.id);
 }
}
