import test from 'node:test';
import assert from 'node:assert/strict';
import {sampleProject} from '../prototypes/lenneth-map/model.js';
import {DEFAULT_CAMERA,Preview3D,roomCorners,rotatePoint,projectPoint,sceneBounds,fitScale,port3D,pointInPolygon,clampPitch} from '../prototypes/lenneth-map/preview3d.js';
const close=(a,b)=>assert.ok(Math.abs(a-b)<1e-8,`${a} != ${b}`);
test('3D uses independent depth while preserving side-view X and upward floor height',()=>{
 const room={x:3,y:7,w:8,h:4,depth:2};const c=roomCorners(room,12);
 assert.deepEqual(c[0],[3,-7,23]);assert.deepEqual(c[6],[11,-11,25]);
 assert.deepEqual(port3D(room,'back',12),[7,-9,25]);assert.deepEqual(port3D(room,'front',12),[7,-9,23]);
 const front={...DEFAULT_CAMERA,yaw:0,pitch:0};const p=rotatePoint([3,5,20],front);assert.equal(p.x,3);assert.equal(p.y,-5);
 const a=rotatePoint([0,0,0],DEFAULT_CAMERA),b=rotatePoint([0,0,12],DEFAULT_CAMERA);assert.notEqual(a.x,b.x);assert.notEqual(a.y,b.y);
});
test('orbit wraps through full rotation, stays finite at extreme pitch and camera fit contains every room',()=>{
 const rooms=sampleProject().rooms,b=sceneBounds(rooms,16);
 for(const yaw of [0,Math.PI/2,Math.PI,Math.PI*2,9])for(const pitch of [-1.48,0,.42,1.48]){
  const cam={...DEFAULT_CAMERA,yaw,pitch};const scale=fitScale(b.points,cam,b.target,900,650);assert.ok(Number.isFinite(scale)&&scale>0);
  for(const point of b.points){const p=projectPoint(point,cam,b.target,900,650,scale);assert.ok(p.x>=50&&p.x<=850);assert.ok(p.y>=70&&p.y<=580);}
 }
 const a=rotatePoint([8,3,-4],DEFAULT_CAMERA),c=rotatePoint([8,3,-4],{...DEFAULT_CAMERA,yaw:DEFAULT_CAMERA.yaw+Math.PI*2});close(a.x,c.x);close(a.y,c.y);close(a.depth,c.depth);
 assert.equal(clampPitch(100),1.48);assert.equal(clampPitch(-100),-1.48);
});
test('camera presets, spacing and preview projection never mutate the saved document',()=>{
 const d=sampleProject(),before=JSON.stringify(d),view=new Preview3D();
 for(const mode of ['iso','front','top','side']){view.gap=24;view.preset(mode,d.rooms,900,650);assert.ok(Number.isFinite(view.scale));}
 assert.equal(JSON.stringify(d),before);view.fit([],900,650);assert.ok(view.scale>0);
});
test('room picking works on rotated projected faces and chooses the front-most overlap',()=>{
 const quad=[{x:0,y:10},{x:10,y:0},{x:20,y:10},{x:10,y:20}];assert.equal(pointInPolygon({x:10,y:10},quad),true);assert.equal(pointInPolygon({x:30,y:10},quad),false);
 const v=new Preview3D();v.hitFaces=[{roomId:'far',points:quad},{roomId:'near',points:quad}];assert.equal(v.pick({x:10,y:10}),'near');assert.equal(v.pick({x:30,y:30}),null);
});
