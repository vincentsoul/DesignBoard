"""Idempotent, non-colliding visual annotations for the existing three-storey mine."""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
PREFIX='CNX_'

def annotate_connections():
 for o in list(bpy.data.objects):
  if o.name.startswith(PREFIX):bpy.data.objects.remove(o,do_unlink=True)
 for c in list(bpy.data.collections):
  if c.name.startswith(PREFIX):bpy.data.collections.remove(c)
 fontpath=Path('/System/Library/Fonts/Supplemental/Arial Unicode.ttf')
 font=bpy.data.fonts.load(str(fontpath)) if fontpath.exists() else bpy.data.fonts.get('Bfont')
 mats={}
 for key,col in {'W':(.95,.38,.055),'E':(.055,.35,.90),'H':(.60,.18,.70),'V':(.025,.52,.46),'ink':(.035,.055,.075),'paper':(.97,.97,.94)}.items():
  m=bpy.data.materials.get(PREFIX+key) or bpy.data.materials.new(PREFIX+key);m.diffuse_color=(*col,1);m.use_nodes=True
  next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(*col,1);mats[key]=m
 def coll(name,parent):
  c=bpy.data.collections.new(PREFIX+name);parent.children.link(c);return c
 def prop(o,kind):
  o['annotation_only']=True;o['collision']='NONE';o['connection_kind']=kind;return o
 def path(c,name,pts,key,r=.075,dashed=False):
  segments=[]
  for a,b in zip(pts,pts[1:]):
   a,b=Vector(a),Vector(b);length=(b-a).length
   if dashed:
    for i in range(math.ceil(length/1.5)):
     lo=i*1.5;hi=min(lo+.72,length)
     if lo<length:segments.append([a+(b-a)*(lo/length),a+(b-a)*(hi/length)])
   else:segments.append([a,b])
  cv=bpy.data.curves.new(PREFIX+name,'CURVE');cv.dimensions='3D';cv.bevel_depth=r;cv.bevel_resolution=2
  for seg in segments:
   sp=cv.splines.new('POLY');sp.points.add(len(seg)-1)
   for p,v in zip(sp.points,seg):p.co=(*v,1)
  o=bpy.data.objects.new(PREFIX+name,cv);c.objects.link(o);cv.materials.append(mats[key]);prop(o,key);return o
 def cone(c,name,tip,direction,key,size=.7):
  direction=Vector(direction).normalized();base=Vector(tip)-direction*size
  ref=Vector((1,0,0)) if abs(direction.x)<.9 else Vector((0,1,0));u=direction.cross(ref).normalized();v=direction.cross(u)
  verts=[tuple(Vector(tip))]+[tuple(base+(u*math.cos(i*math.tau/12)+v*math.sin(i*math.tau/12))*size*.35) for i in range(12)]
  faces=[(0,i+1,(i+1)%12+1) for i in range(12)]+[tuple(range(12,0,-1))]
  me=bpy.data.meshes.new(PREFIX+name);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(PREFIX+name,me);c.objects.link(o);me.materials.append(mats[key]);prop(o,key);return o
 texts=[]
 def label(c,name,body,pos,key='ink',size=.55,camera=None):
  cv=bpy.data.curves.new(PREFIX+name,'FONT');cv.body=body;cv.font=font;cv.size=size;cv.align_x='CENTER';cv.align_y='CENTER';cv.space_line=1.25;cv.extrude=.006
  o=bpy.data.objects.new(PREFIX+name,cv);c.objects.link(o);o.location=pos
  if camera:
   o.rotation_euler=camera.rotation_euler
   # Orthographic display labels retain their screen position but sit in front of floors.
   o.location += camera.rotation_euler.to_quaternion() @ Vector((0,0,80))
  cv.materials.append(mats[key]);prop(o,key);o['label_text']=body;texts.append(o);return o
 def ring(c,name,x,y,z,w,d,key):
  path(c,name,[(x-w/2,-y-d/2,z),(x+w/2,-y-d/2,z),(x+w/2,-y+d/2,z),(x-w/2,-y+d/2,z),(x-w/2,-y-d/2,z)],key,.09)
 levels={lv:bpy.data.collections[lv+'_Z%+d'%z] for lv,z in [('L2',4),('L3',0),('L4',-4)]}
 for lv,z in [('L2',4),('L3',0),('L4',-4)]:
  c=coll('Model_'+lv,levels[lv])
  for key,x,y,w,d in [('W',18,33,8,10),('E',62,28,8,12)]:
   ring(c,lv+'_'+key+'_Stair_Outline',x,y,z+.07,w-.35,d-.35,key)
   path(c,lv+'_'+key+'_Pin',[(x,-y,z+.1),(x,-y,z+3.8)],key,.10)
   cone(c,lv+'_'+key+'_PinTip',(x,-y,z+4.1),(0,0,1),key,.65)
   body=({'L2':'W 下至 L3｜维修钥匙','L3':'W 上至L2：钥匙\n下至L4：保险丝','L4':'W 上至 L3｜保险丝'}[lv] if key=='W' else {'L2':'E 下至 L3｜供电后','L3':'E 上至L2／下至L4\n恢复供电后开放','L4':'E 上至 L3｜供电后'}[lv])
   # Labels on landing approach floor, not inside the stair void.
   label(c,lv+'_'+key+'_Destinations',body,(x,-(39.4 if key=='W' else 35.5),z+.13),key,.44)
  ring(c,lv+'_Hatch_Outline',5,35,z+.25,2.3,2.3,'H')
  label(c,lv+'_Hatch_Status','H 检修接口｜未启用',(5,-37.6,z+.15),'H',.36)
  if lv!='L4':ring(c,lv+'_Atrium_Visual',38,28,z+.12,16,4,'V')
  label(c,lv+'_Atrium_Use','中庭挑空｜仅视觉联系' if lv!='L4' else '中庭底层大厅',(38,-24,z+.15),'V',.5)
 # Explicit connections in the exploded display are schematic visual leaders, never bridges.
 for scene_name,kind in [('03_Vertical_Exploded','Vertical'),('01_Assembled_Three_Storeys','Assembled')]:
  sc=bpy.data.scenes[scene_name];c=coll(kind+'_Connections',sc.collection);camera=sc.camera
  zs={'L2':46,'L3':0,'L4':-46} if kind=='Vertical' else {'L2':4,'L3':0,'L4':-4}
  for key,x,y,outer in [('W',18,33,-7),('E',62,28,89)]:
   for lv,z in zs.items():path(c,kind+'_'+key+'_'+lv+'_Leader',[(x,-y,z+4.3),(outer,-y,z+4.3)],key,.10,True)
   for hi,lo,condition in [('L2','L3','维修钥匙' if key=='W' else '恢复供电'),('L3','L4','保险丝' if key=='W' else '恢复供电')]:
    za,zb=zs[hi]+4.3,zs[lo]+4.3
    path(c,kind+'_'+key+'_'+hi+'_'+lo,[(outer,-y,za),(outer,-y,zb)],key,.16)
    cone(c,kind+'_'+key+'_'+hi+'_Up',(outer,-y,za),(0,0,1),key,1.3 if kind=='Vertical' else .5)
    cone(c,kind+'_'+key+'_'+lo+'_Down',(outer,-y,zb),(0,0,-1),key,1.3 if kind=='Vertical' else .5)
    o=bpy.data.objects[PREFIX+kind+'_'+key+'_'+hi+'_'+lo];o['from_floor']=hi;o['to_floor']=lo;o['requires']=condition
    if kind=='Vertical':
     pos=(outer+(-8 if key=='W' else 8),-y-2,(za+zb)/2)
     label(c,kind+'_'+key+'_'+hi+'_'+lo+'_Caption',f'{key} {hi} ↔ {lo}\n{condition}',pos,'ink',2.9,camera)
  if kind=='Vertical':
   for hi,lo in [('L2','L3'),('L3','L4')]:
    za,zb=zs[hi]+4.3,zs[lo]+4.3
    path(c,kind+'_Atrium_'+hi+'_'+lo,[(38,-28,za),(38,-28,zb)],'V',.085,True)
    label(c,kind+'_Atrium_'+hi+'_Note','中庭挑空\n仅视觉关联',(38,-27,(za+zb)/2),'ink',2.1,camera)
    path(c,kind+'_H_'+hi+'_'+lo,[(-24,-15,za),(-24,-15,zb)],'H',.10,True)
    label(c,kind+'_H_'+hi+'_Note','H 检修梯\n未启用',(-30,-15,(za+zb)/2),'ink',2.1,camera)
   for lv,z in zs.items():
    path(c,kind+'_H_'+lv+'_Leader',[(5,-35,z+.3),(-24,-15,z+.3)],'H',.07,True)
    label(c,kind+'_'+lv+'_FloorTitle',{'L2':'L2 管理层｜实际 +4m','L3':'L3 救援层｜实际 0m','L4':'L4 动力层｜实际 −4m'}[lv],(39,3,z+5),'ink',2.6,camera)
   label(c,'Legend','橙 W 主线楼梯  ·  蓝 E 供电捷径\n紫 H 未启用接口  ·  青 中庭视觉联系\n连线仅为标注，真实层间距4m',(39,-60,-59),'ink',2.3,camera)
   camera.data.ortho_scale=169
  else:
   label(c,'Assembled_Key','真实标高 +4 / 0 / −4m\nW：钥匙 / 保险丝\nE：恢复供电后开放',(-14,-20,10),'ink',1.05,camera)
 # Convert CJK glyphs to meshes so the .blend needs no external font installation.
 bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
 for o in texts:
  # Scene-local labels need their source scene active for reliable evaluated geometry.
  owners=[sc for sc in bpy.data.scenes if o.name in sc.objects]
  if owners:bpy.context.window.scene=owners[0]
  bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
  me=bpy.data.meshes.new_from_object(o.evaluated_get(dg));new=bpy.data.objects.new(o.name+'_GlyphMesh',me)
  new.matrix_world=o.matrix_world.copy()
  for col in list(o.users_collection):col.objects.link(new)
  for key in o.keys():new[key]=o[key]
  bpy.data.objects.remove(o,do_unlink=True)
 bpy.context.window.scene=bpy.data.scenes['03_Vertical_Exploded']
 # Display material colours in existing live views as well as Workbench renders.
 for screen in bpy.data.screens:
  for area in screen.areas:
   if area.type=='VIEW_3D':area.spaces.active.shading.color_type='MATERIAL'
 objects=[o for o in bpy.data.objects if o.name.startswith(PREFIX)]
 assert all(o.get('annotation_only') for o in objects)
 assert len([o for o in objects if o.get('from_floor')])==8
 print('CONNECTION_ANNOTATIONS',len(objects),'objects; 4 actual + 4 exploded vertical links; no base geometry changed')
 return len(objects)

if __name__=='__main__':
 annotate_connections()
 bpy.context.preferences.filepaths.save_version=0
 bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'assets/levels/mine-complex/mine-complex-three-storeys.blend'))
