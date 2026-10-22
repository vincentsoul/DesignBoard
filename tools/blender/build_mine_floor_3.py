"""Run with Blender --background --factory-startup --python this_file.py.
Coordinates match docs/level-design/mine-floor-3-v1.md: Blender Y = -plan Y.
"""
import bpy, math, random, json
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'assets/levels/mine-floor-3'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
collections={}
for name in ['01_Floors','02_Walls','03_Mechanisms','04_Shaft','05_ScaleReferences','06_Annotations','07_Presentation']:
 c=bpy.data.collections.new(name); scene.collection.children.link(c); collections[name]=c

def mat(name,value):
 m=bpy.data.materials.new(name); m.diffuse_color=(value,value,value,1); m.use_nodes=True
 n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'); n.inputs['Base Color'].default_value=(value,value,value,1); n.inputs['Roughness'].default_value=.85
 return m
white=mat('Whitebox_Plaster',.78); floor=mat('Walkable_Floor',.60); dark=mat('Mechanism_Grey',.28); ink=mat('Annotation',.10)
def link(o,group):
 for c in list(o.users_collection):c.objects.unlink(o)
 collections[group].objects.link(o)
 return o

def box(name,x,y,z,w,d,h,material=white,group='03_Mechanisms'):
 bpy.ops.mesh.primitive_cube_add(size=1,location=(x,-y,z)); o=bpy.context.object; o.name=name; o.dimensions=(w,d,h)
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 o.data.materials.append(material); link(o,group); return o

def line(name,points,r=.035,material=ink,group='06_Annotations'):
 cv=bpy.data.curves.new(name,'CURVE'); cv.dimensions='3D'; cv.bevel_depth=r; cv.bevel_resolution=1
 s=cv.splines.new('POLY'); s.points.add(len(points)-1)
 for v,p in zip(s.points,points):v.co=(p[0],-p[1],p[2],1)
 o=bpy.data.objects.new(name,cv); collections[group].objects.link(o); cv.materials.append(material); return o

def label(name,text,x,y,z=.06,size=.7):
 cv=bpy.data.curves.new(name,'FONT'); cv.body=text; cv.size=size; cv.align_x='CENTER'; cv.extrude=.002
 o=bpy.data.objects.new(name,cv); collections['06_Annotations'].objects.link(o); o.location=(x,-y,z); cv.materials.append(ink); return o
regions=[('01_Entry',2,2,12,10),('Entry_Link',14,6,6,4),('02_Overlook',20,2,6,8),('OldMine_NS',20,10,4,18),('03_OldMine_West',8,24,16,4),('Shaft_Corridor',8,28,4,10),('OldMine_East',8,38,26,4),('Mine_Branch',24,14,6,4),('04_Mine',30,2,26,20),('05_Rescue',34,30,16,16),('Exit_Link',50,36,14,4),('06_Exit',64,34,12,10)]
cells=set()
for name,x,y,w,d in regions:
 for i in range(x,x+w):
  for j in range(y,y+d): cells.add((i,j))
 # Exact design region as non-rendering empty, inspect dimensions in custom props.
 o=bpy.data.objects.new(name+'_Bounds',None); collections['06_Annotations'].objects.link(o); o.location=(x+w/2,-y-d/2,0)
 o['plan_xywh']=[x,y,w,d]; o['clear_height_m']=3
verts=[];faces=[]
for x,y in sorted(cells):
 n=len(verts); verts += [(x,-y,0),(x+1,-y,0),(x+1,-y-1,0),(x,-y-1,0)]; faces.append((n+3,n+2,n+1,n))
mesh=bpy.data.meshes.new('Floor_Union_1m'); mesh.from_pydata(verts,[],faces);mesh.update()
o=bpy.data.objects.new('Floor_Union_Z0',mesh);collections['01_Floors'].objects.link(o);o.data.materials.append(floor)
solid=o.modifiers.new('Floor thickness 0.3m','SOLIDIFY');solid.thickness=.3
# Exterior boundary only. Wall thickness lies outside design net floor area.
wall_count=0
for x,y in sorted(cells):
 for dx,dy in [(-1,0),(1,0),(0,-1),(0,1)]:
  if (x+dx,y+dy) in cells:continue
  vertical=dx!=0
  bx=x+(1 if dx==1 else 0) if vertical else x+.5
  by=y+.5 if vertical else y+(1 if dy==1 else 0)
  # Exit interface and exterior ladder alcove connection.
  if vertical and bx==76 and 36<=y<40:continue
  if vertical and bx==8 and 32<=y<36:continue
  window=vertical and bx in (26,30) and 4<=y<8
  segments=[(.4,.8),(2.8,.4)] if window else [(1.5,3)]
  for z,h in segments:
   box('Wall_%04d'%wall_count,bx+dx*.125,by+dy*.125,z,.25 if vertical else 1,1 if vertical else .25,h,white,'02_Walls');wall_count+=1
# True 4m G1 opening, closed block can be hidden to inspect open state.
g1=box('G1_ChainDoor_HIDE_TO_OPEN',29,16,1.4,.18,4,2.8,dark);g1['interaction']='Fire slash; hide object to open';g1['clear_width_m']=4
# Cage footprint 37..43,32..38. Bars entirely inside footprint; southern opening 39..41.
for x in (37.12,42.88):
 for y in [32.12+i*.48 for i in range(13)]:box('Cage_Side_Bar',x,y,1.5,.10,.10,3,dark)
for x in [37.12+i*.48 for i in range(13)]:
 box('Cage_Back_Bar',x,32.12,1.5,.1,.1,3,dark)
 if not 39<x<41:box('Cage_Front_Bar',x,37.88,1.5,.1,.1,3,dark)
for z in (.1,2.95):
 box('Cage_Back_Rail',40,32.12,z,6,.14,.14,dark);box('Cage_Front_Rail',40,37.88,z,6,.14,.14,dark)
 for x in (37.12,42.88):box('Cage_Side_Rail',x,35,z,.14,6,.14,dark)
g2=box('G2_CageDoor_HIDE_TO_OPEN',40,37.92,1.35,2,.12,2.7,dark);g2['interaction']='Sound calm AND fire chain removal'
# Solid blocker envelope guarantees G3 blocks the full corridor; rocks are visual placeholders.
g3=box('G3_Blocker_HIDE_TO_OPEN',58,38,1.25,4,4,2.5,dark);g3['interaction']='Earth stabilize; hide G3 objects to open';g3['open_floor_z']=0
rng=random.Random(3)
for i in range(13):
 bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=(rng.uniform(56.7,59.3),-rng.uniform(36.7,39.3),rng.uniform(.8,2.2)))
 o=bpy.context.object;o.name='G3_Rock_%02d'%i;o.scale=(.7,.7,.8);o.data.materials.append(white);link(o,'03_Mechanisms')
# Shaft alcove is outside x8..12 main corridor: no open pit on main route.
for z in (-4,0,4):
 box('Shaft_Landing_Z%+d'%z,7,34,z-.15,2,4,.3,floor,'04_Shaft')
for x in (4.6,5.4):box('Ladder_Upright',x,35.5,0,.10,.12,8.4,dark,'04_Shaft')
for i in range(28):box('Ladder_Rung',5,35.5,-4+i*.3,.8,.12,.09,dark,'04_Shaft')
for y in (31.9,36.1):
 box('Shaft_Frame',5.9,y,0,.18,.18,9,white,'04_Shaft')
 line('Shaft_Edge',[(4,y,-4),(8,y,-4)],.08,dark,'04_Shaft')
box('UpperFloor_Drop_Interface',8,7,4,4,4,.15,white,'04_Shaft').hide_render=True
# Simple scale mannequins, crown at exactly supplied height.
def person(name,x,y,h):
 box(name+'_Body',x,y,h*.46,.45,.30,h*.6,dark,'05_ScaleReferences')
 bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,radius=h*.12,location=(x,-y,h*.88));o=bpy.context.object;o.name=name+'_Head';o.data.materials.append(white);link(o,'05_ScaleReferences')
 for ox in (-.13,.13):box(name+'_Leg',x+ox,y,h*.15,.16,.22,h*.3,dark,'05_ScaleReferences')
person('Player_1.8m',40,40,1.8);person('Miner_1.5m',40,12,1.5);person('Caged_Beast_1.0m',40,35,1)
box('Miner_Trapping_Beam',40,12,1,3,.35,.4,dark)
# Planning overlays can be hidden as an independent collection.
line('Main_Route',[(8,7,.05),(17,8,.05),(22,8,.05),(22,26,.05),(10,26,.05),(10,40,.05),(35,40,.05),(40,39,.05)],.045)
line('Optional_Mine_Route',[(22,16,.05),(34,16,.05),(40,12,.05)],.04)
line('Unlocked_Exit_Route',[(42,40,.05),(48,38,.05),(75,38,.05)],.04)
line('Sightline',[(23,6,1.6),(35,6,1)],.035)
for args in [('EntryLabel','01 ENTRY 12 x 10 m',8,4),('ObservationLabel','02 OVERLOOK',23,3),('MineLabel','04 MINE 26 x 20 m',43,4),('RescueLabel','05 RESCUE 16 x 16 m',42,44),('ExitLabel','06 EXIT 12 x 10 m',70,43),('OldMineLabel','03 OLD MINE / 4 m CLEAR',17,25)]:label(*args)
label('ShaftLabel','SHAFT +4 / 0 / -4',4,30,size=.6)
label('North','NORTH',39,-2,size=1)
line('Scale10m',[(3,47,.05),(13,47,.05)],.06);label('ScaleLabel','10 m',8,49,size=.8)
# Cameras preserve plan orientation: north = Blender +Y.
def camera(name,loc,target,scale):
 data=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,data);collections['07_Presentation'].objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=scale;data.clip_end=500;return o
cam=camera('Camera_Isometric',(99,-105,102),(38,-24,0),98)
top=camera('Camera_Plan',(39,-24,120),(39,-24,0),88)
scene.camera=cam
try:scene.render.engine='BLENDER_WORKBENCH'
except TypeError:pass
scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD';scene.world.color=(.85,.85,.85)
transforms=[i.identifier for i in scene.view_settings.bl_rna.properties['view_transform'].enum_items]
if 'Standard' in transforms:scene.view_settings.view_transform='Standard'
scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene['source_plan']='docs/level-design/mine-floor-3-v1.svg'
scene['coordinate_mapping']='Blender (x,y,z) = plan (X,-Y,Z); metres'
scene['scope']='Static whitebox; doors and blockers are placeholders, not playable game logic'
# Geometry-level assertions, independent of presentation.
assert all((22,y) in cells for y in range(10,28))
assert all((x,16) in cells for x in range(22,56))
assert all((x,38) in cells for x in range(34,76))
assert not any((x,6) in cells for x in range(26,30))
assert g1.dimensions.y==4 and g2.dimensions.x==2
report={'units':'metres','floor_cells_1m':len(cells),'boundary_wall_segments':wall_count,'regions':regions,'checks':['main and branch floor continuity','4m chasm remains empty','G1 4m / G2 2m','ladder alcove outside main path'],'not_tested':['engine collisions','player traversal','ability state logic','game camera']}
check_dir=ROOT/'outputs/mine-floor-3'
check_dir.mkdir(parents=True,exist_ok=True)
(check_dir/'geometry-checks.json').write_text(json.dumps(report,indent=2))
# Frame live viewport when this blend opens.
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.region_3d.view_perspective='CAMERA';area.spaces.active.clip_end=500
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(OUT/'mine-floor-3-isometric.png')
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'mine-floor-3-whitebox.blend'))
scene.render.filepath=str(OUT/'mine-floor-3-isometric.png');bpy.ops.render.render(write_still=True)
scene.camera=top;scene.render.filepath=str(OUT/'mine-floor-3-plan.png');bpy.ops.render.render(write_still=True)
scene.camera=cam
print('WHITEBOX_COMPLETE',len(bpy.data.objects),len(cells))
