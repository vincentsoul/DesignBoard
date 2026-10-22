"""Three-storey mine complex. Run in Blender background; preserves the V1 blend."""
import bpy, json, math
from pathlib import Path
from collections import defaultdict, deque
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'assets/levels/mine-complex'; OUT.mkdir(parents=True,exist_ok=True)
BASE=ROOT/'assets/levels/mine-floor-3/mine-floor-3-whitebox.blend'
bpy.ops.wm.open_mainfile(filepath=str(BASE))
s=bpy.context.scene;s.name='01_Assembled_Three_Storeys'
# Preserve only the rescue mechanisms and scale references, rebuild circulation and shaft.
keep=set()
for c in ['03_Mechanisms','05_ScaleReferences']:
 keep.update(bpy.data.collections[c].objects)
for o in list(bpy.data.objects):
 if o not in keep:bpy.data.objects.remove(o,do_unlink=True)
for c in list(bpy.data.collections):
 if not c.objects and not c.children:bpy.data.collections.remove(c)
levels={}
for key,z in [('L2',4),('L3',0),('L4',-4)]:
 c=bpy.data.collections.new(key+'_Z%+d'%z);s.collection.children.link(c);levels[key]=c
for c in ['03_Mechanisms','05_ScaleReferences']:
 col=bpy.data.collections.get(c)
 for parent in list(s.collection.children):
  if parent==col:s.collection.children.unlink(col)
 levels['L3'].children.link(col)
# Existing G1 and cage remain; no original routes are assumed valid in V2.
M={name:bpy.data.materials.get(name) for name in ['Whitebox_Plaster','Walkable_Floor','Mechanism_Grey','Annotation']}
white=M['Whitebox_Plaster'];floor=M['Walkable_Floor'];dark=M['Mechanism_Grey'];ink=M['Annotation']

def box(c,name,x,y,z,w,d,h,mat=white):
 verts=[(sx*w/2,sy*d/2,sz*h/2) for sx,sy,sz in [(-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),(1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1)]]
 faces=[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)]
 m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update()
 o=bpy.data.objects.new(name,m);c.objects.link(o);o.location=(x,-y,z);m.materials.append(mat);return o

def text(c,name,body,x,y,z,size=.65):
 d=bpy.data.curves.new(name,'FONT');d.body=body;d.align_x='CENTER';d.size=size;d.extrude=.001
 o=bpy.data.objects.new(name,d);c.objects.link(o);o.location=(x,-y,z);d.materials.append(ink);return o

def meshfloor(c,name,cells,z):
 verts=[];faces=[]
 for x,y in sorted(cells):
  n=len(verts);verts += [(x,-y,z),(x+1,-y,z),(x+1,-y-1,z),(x,-y-1,z)];faces.append((n+3,n+2,n+1,n))
 m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update();o=bpy.data.objects.new(name,m);c.objects.link(o);m.materials.append(floor)
 mod=o.modifiers.new('Floor_0.25m','SOLIDIFY');mod.thickness=.25
 return o

names2=['Records','Dormitory','Infirmary','Chief_Office','Survey_Lab','Security', 'West_Storage','West_Safe_Room','East_Archives','East_Exhibit','Meeting','Dispatch','Observation_Control','West_Inner_Office','Radio','Key_Vault']
names4=['Water_Intake','Filter_Plant','Pump_Control','Generator','Switchgear','Workshop','West_Cistern','Drainage','East_Stores','Service_Exit','Boiler','Turbine','Valve_Room','Tool_Room','Power_Console','Emergency_Store']
common=[(2,2,12,12),(14,2,12,12),(26,2,12,12),(38,2,12,12),(50,2,16,12),(66,2,16,12),(2,18,8,12),(2,30,8,12),(70,18,12,12),(70,30,12,12),(22,34,12,8),(34,34,12,8),(46,34,12,8),(14,18,12,6),(34,18,16,4),(50,18,8,8)]
# Shared stair footprints align exactly on all floors.
stairs={'W':(14,28,8,10),'E':(58,22,8,12)}
CIRC=[(2,14,80,4),(10,18,4,24),(66,18,4,28),(14,42,56,4),(14,38,8,4),(58,34,8,8),(14,24,12,4),(50,26,8,4),(30,18,4,4),(26,22,24,4),(26,26,4,8),(46,26,4,8),(30,30,16,4)]
original=[(2,2,12,10),(14,6,6,4),(20,2,6,8),(20,10,4,18),(8,24,16,4),(8,28,4,10),(8,38,26,4),(24,14,6,4),(30,2,26,20),(34,30,16,16),(50,36,14,4),(64,34,12,10)]
annex=[('West_Archive',(0,16,8,12)),('West_Maintenance',(0,28,8,10)),('East_Vault',(56,2,10,12)),('Fuse_Store',(56,14,10,4)),('South_Store',(24,42,10,8)),('South_Safe_Room',(50,42,14,8))]
data={};portals=[];edge_graph=defaultdict(list);room_specs={};door_objects={};floor_holes={}

def fill(grid,tag,r):
 x,y,w,h=r
 for i in range(x,x+w):
  for j in range(y,y+h):grid[i,j]=tag

def addgraph(a,b,gate=None):edge_graph[a].append((b,gate));edge_graph[b].append((a,gate))

for level,z in [('L2',4),('L3',0),('L4',-4)]:
 c=levels[level];g={};spec=[]
 if level=='L3':
  for r in original:fill(g,'Core',r)
  for r in [(24,22,6,4),(26,22,24,4),(26,26,4,8),(46,26,4,8),(30,30,16,4),(12,32,2,4),(52,18,10,4),(60,34,4,4),(50,26,8,4)]:fill(g,'Core',r)
  for name,r in annex:fill(g,name,r);spec.append((name,r))
 else:
  names=names2 if level=='L2' else names4
  lower_rooms=[('Water_Intake',(2,2,24,12)),('Pump_Control',(26,2,12,12)),('Generator',(38,2,12,12)),('Workshop',(50,2,32,12)),('West_Cistern',(2,18,8,24)),('Switchgear',(70,18,12,12)),('Service_Exit',(70,30,12,12)),('Boiler',(22,34,24,8)),('Valve_Room',(46,34,12,8)),('Tool_Room',(14,18,12,6)),('Power_Console',(34,18,16,4)),('Emergency_Store',(50,18,8,8))]
  selected=list(zip(names,common)) if level=='L2' else lower_rooms
  for name,r in selected:fill(g,name,r);spec.append((name,r))
  for r in CIRC:fill(g,'Circulation',r)
  if level=='L4':fill(g,'Atrium_Hall',(26,22,24,12));spec.append(('Atrium_Hall',(26,22,24,12)))
 for name,r in stairs.items():fill(g,name+'_Stair',r);spec.append((name+'_Stair',r))
 data[level]=g;room_specs[level]=spec
 # Floor holes over both flights and half-height landing; bottom floor solid.
 holes=set()
 if level!='L4':
  for name,(x,y,w,h) in stairs.items():
   for i in range(x+1,x+7):
    for j in range(y+h-8,y+h-2):holes.add((i,j))
 # L2 drop hatch is a closed grate; original observation chasm never filled on L3.
 floor_holes[level]=holes
 meshfloor(c,level+'_Floor',set(g)-holes,z)
 # Build wall runs grouped by orientation, coordinate and region pair.
 runs=defaultdict(list)
 for (x,y),tag in g.items():
  for dx,dy in [(1,0),(-1,0),(0,1),(0,-1)]:
   other=g.get((x+dx,y+dy))
   if other==tag:continue
   if other is not None and (dx,dy) not in [(1,0),(0,1)]:continue
   axis='X' if dx else 'Y';fixed=x+(dx==1) if dx else y+(dy==1);t=y if dx else x
   a,b=tag,other
   runs[(axis,fixed,a,b,dx,dy)].append(t)
 for (axis,fixed,a,b,dx,dy),ts in runs.items():
  ts=sorted(ts);groups=[]
  for t in ts:
   if not groups or t!=groups[-1][-1]+1:groups.append([t])
   else:groups[-1].append(t)
  for run in groups:
   start,end=run[0],run[-1]+1;opening=None;gate=None
   if b is not None and end-start>=2:
    # Every physical adjacency gets a 2m door; only circulation/circulation remains unwalled.
    opening=((start+end)/2-1,(start+end)/2+1)
    # Optional rooms locked until power; the core loop never depends on these rewards.
    locked={'Key_Vault','East_Vault','Emergency_Store','East_Archives'}
    if a in locked or b in locked:gate='POWER'
    aid=level+':'+a;bid=level+':'+b
    addgraph(aid,bid,gate)
    mid=sum(opening)/2
    px,py=(fixed,mid) if axis=='X' else (mid,fixed)
    pid=f'{level}_D{len(portals):03d}'
    portals.append({'id':pid,'level':level,'a':a,'b':b,'axis':axis,'fixed':fixed,'span':list(opening),'gate':gate})
    if gate:
     o=box(c,pid+'_POWER_LOCK',px,py,z+1.2,.12 if axis=='X' else 2,2 if axis=='X' else .12,2.4,dark);o['requires']=gate;door_objects[pid]=o
    else:
     # Header locates doors without filling the opening below.
     box(c,pid+'_Lintel',px,py,z+2.85,.25 if axis=='X' else 2,2 if axis=='X' else .25,.3)
   for t in run:
    # Only split segments around actual opening; doors can be half-grid centred.
    spans=[(t,t+1)]
    if opening:
     spans=[]
     if t<opening[0]:spans.append((t,min(t+1,opening[0])))
     if t+1>opening[1]:spans.append((max(t,opening[1]),t+1))
    for lo,hi in spans:
     if hi-lo<.001:continue
     window=level=='L3' and axis=='X' and fixed in (26,30) and 4<=t<8 and b is None
     # Exit opens towards continuation, other exterior boundaries remain closed.
     if level=='L3' and axis=='X' and fixed==76 and 36<=t<40:continue
     # Shift partitions into rooms to keep circulation widths intact.
     offset=.125 if b is None else ( .125 if a in ['Core','Circulation'] else -.125 if b in ['Core','Circulation'] else 0 )
     px=fixed+dx*offset if axis=='X' else (lo+hi)/2
     py=(lo+hi)/2 if axis=='X' else fixed+dy*offset
     for height,cz in ([(.8,.4),(.4,2.8)] if window else [(3,1.5)]):
      box(c,level+'_Wall',px,py,z+cz,.25 if axis=='X' else hi-lo,hi-lo if axis=='X' else .25,height)
 # Atrium safety rails are external boundaries and initially full walls; replace facing centre with 1.1m guards.
 if level in ('L2','L3'):
  for o in list(c.objects):
   if '_Wall' not in o.name:continue
   x,y=o.location.x,-o.location.y
   if (abs(x-30)<.2 or abs(x-46)<.2) and 26<=y<=30 or (abs(y-26)<.2 or abs(y-30)<.2) and 30<x<46:
    o.dimensions.z=1.1;o.location.z=z+.55;o.name=level+'_Atrium_Guard'
 for name,(x,y,w,h) in spec:
  if 'Stair' in name:continue
  text(c,level+'_'+name+'_Label',name.replace('_',' ').upper(),x+w/2,y+h/2,z+.04,min(.65,w/16))
 if level!='L4':text(c,level+'_Atrium_Label','OPEN TO ATRIUM BELOW',38,28,z+.02,.55)
 text(c,level+'_Title',{'L2':'L2  ADMINISTRATION  /  Z +4 m','L3':'L3  RESCUE & MINING  /  Z 0 m','L4':'L4  UTILITIES  /  Z -4 m'}[level],39,-2,z+.05,1.0)
 # North and scale annotations.
 text(c,level+'_North','N',79,0,z+.05,.8)
 box(c,level+'_10m_Bar',7,49,z+.04,10,.1,.08,ink)
 text(c,level+'_Scale','10 m',7,50,z+.05,.7)

for body,x,y in [('ENTRY',8,4),('OVERLOOK',23,3),('MINE',43,4),('RESCUE',42,44),('EXIT',70,43),('OLD MINE',17,25)]:
 text(levels['L3'],'L3_'+body+'_Label',body,x,y,.04,.65)

# Rail the upper stair void on all sides except its controlled top-flight entrance.
for level,z in [('L2',4),('L3',0)]:
 c=levels[level]
 for name,(x,y,w,h) in stairs.items():
  front=y+h-2
  for xx in (x+.95,x+7.05):box(c,f'{name}_{level}_Void_Side_Guard',xx,front-3,z+.55,.1,6,1.1)
  box(c,f'{name}_{level}_Void_Back_Guard',x+4,front-6.05,z+.55,6,.1,1.1)
  box(c,f'{name}_{level}_Void_Front_Guard',x+3,front+.05,z+.55,4,.1,1.1)

# U-stairs: 24 risers x 1/6m, 0.3m tread, 2m clear flights, 2m deep central landing.
for low,high,z in [('L4','L3',-4),('L3','L2',0)]:
 c=levels[low]
 for name,(x,y,w,h) in stairs.items():
  front=y+h-2
  for i in range(12):
   rise=(i+1)/6
   box(c,f'{name}_{low}_{high}_FlightA_{i:02d}',x+2,front-(i+.5)*.3,z+rise/2,2,.3,rise)
  box(c,f'{name}_{low}_{high}_MidLanding',x+4,front-4.6,z+1.875,6,2,.25)
  for i in range(12):
   rise=2+(i+1)/6
   box(c,f'{name}_{low}_{high}_FlightB_{i:02d}',x+6,front-3.6+(i+.5)*.3,z+rise/2,2,.3,rise)
  # Rail along centre split, not across entrances.
  box(c,f'{name}_{low}_{high}_Rail',x+4,front-1.8,z+2.6,.12,3.6,1.1,dark)
  requirement='SERVICE_KEY' if name=='W' and low=='L3' else 'FUSE' if name=='W' else 'POWER'
  addgraph(low+':'+name+'_Stair',high+':'+name+'_Stair',requirement)
  o=box(levels[high],f'{name}_{low}_{high}_Gate_{requirement}',x+6,front,z+5,2,.12,2,dark)
  o['requires']=requirement;o['connection']=low+'<->'+high
  text(levels[high],f'{name}_{high}_StairLabel',f'{name} STAIR / {requirement}',x+4,y+h-.6,z+4.04,.40)
# Maintenance ladder: optional powered/earth shortcut, no initial bypass of progression.
for level,z in [('L2',4),('L3',0),('L4',-4)]:
 c=levels[level]
 x,y=5,35
 # Closed hatches as whitebox markers; ladder volume in west service rooms.
 hatch=box(c,level+'_Maintenance_Hatch_EARTH',x,y,z+.1,2,2,.2,dark);hatch['requires']='EARTH';hatch['scope']='Shortcut placeholder, no climbing controller'
for x in [4.7,5.3]:box(levels['L4'],'Maintenance_Ladder_Rail',x,35,0,.08,.10,8,dark)
for i in range(27):box(levels['L4'],'Maintenance_Ladder_Rung',5,35,-4+i*.3,.6,.12,.07,dark)
# Separate hatch annotation: not counted in mandatory walkable graph until ladder gameplay exists.
# Pickup and objective markers on reachable floor outside stair holes.
objectives=[('SERVICE_KEY','L2','Records',8,8),('FUSE','L3','Core',43,16),('POWER','L4','Power_Console',42,20),('EARTH','L3','Core',40,39)]
for token,lv,room,x,y in objectives:
 z={'L2':4,'L3':0,'L4':-4}[lv]
 o=box(levels[lv],'OBJECTIVE_'+token,x,y,z+.65,.65,.65,1.3,dark);o['grants']=token
 text(levels[lv],token+'_Text',token,x,y+1.3,z+.05,.50)
# Utility volumes make lower-storey orientation different from the office layer.
for name,x,y,w,d,h in [('Intake_Tank',10,8,8,5,2),('Generator_Block',44,8,6,4,2),('Workshop_Bench',64,8,10,2,.9),('Boiler_Block',34,38,7,3,2.5)]:
 box(levels['L4'],name,x,y,-4+h/2,w,d,h,dark)
# One side-latched shortcut: safe room -> original core, later usable in reverse.
for portal in portals:
 if portal['level']=='L3' and 'South_Safe_Room' in (portal['a'],portal['b']) and portal['axis']=='X' and portal['fixed']==64:
  mid=sum(portal['span'])/2
  o=box(levels['L3'],'Shortcut_SafeRoom_SideLatch',64,mid,1.2,.12,2,2.4,dark)
  o['unlock_from']='South_Safe_Room';o['after_unlock']='bidirectional';portal['side_latch_from']='South_Safe_Room'

# Update original cage and exit annotations to prevent early bypass of progression.
bpy.data.objects['G2_CageDoor_HIDE_TO_OPEN']['requires']='POWER + SOUND + FIRE; grants EARTH'
bpy.data.objects['G3_Blocker_HIDE_TO_OPEN']['requires']='EARTH'
# Connected physical portal graph, iterative progression validation.
start='L2:Circulation'; tokens=set();history=[]
def flood(tokens):
 visited={start};q=deque([start])
 while q:
  a=q.popleft()
  for b,gate in edge_graph[a]:
   if b not in visited and (gate is None or gate in tokens):visited.add(b);q.append(b)
 return visited
for token,lv,room,x,y in objectives:
 reachable=flood(tokens)
 assert lv+':'+room in reachable,('unreachable objective',token,lv,room)
 # Each mandatory grant has an extra intended condition, not merely a region placeholder.
 assert token!='EARTH' or 'POWER' in tokens
 tokens.add(token);history.append({'grant':token,'reachable_regions_before':len(reachable)})
assert all(lv+':W_Stair' in flood(tokens) for lv in levels)
assert all(lv+':E_Stair' in flood(tokens) for lv in levels)
assert not any('L3:' in r or 'L4:' in r for r in flood(set()))
assert not any(r.startswith('L4:') for r in flood({'SERVICE_KEY'}))
assert len(flood(tokens))==len(edge_graph),('disconnected regions',set(edge_graph)-flood(tokens))
assert all((x,6) not in data['L3'] for x in range(26,30))
for lv in ('L2','L3'):
 assert not any((x,y) in set(data[lv])-floor_holes[lv] for x in range(30,46) for y in range(26,30))
# Cameras and exploded inspection scene use linked collection instances, actual geometry remains stacked.
pres=bpy.data.collections.new('Presentation');s.collection.children.link(pres)
def cam(scene,name,pos,target,scale):
 d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();d.type='ORTHO';d.ortho_scale=scale;d.clip_end=1000;return o
maincam=cam(s,'Camera_Assembled',(115,-115,95),(40,-24,0),112);s.camera=maincam
expl=bpy.data.scenes.new('02_Exploded_Overview')
for lv,offset in [('L2',-92),('L3',0),('L4',92)]:
 o=bpy.data.objects.new('Display_'+lv,None);o.instance_type='COLLECTION';o.instance_collection=levels[lv];expl.collection.objects.link(o);o.location=(offset,0,0)
expl.camera=cam(expl,'Camera_Exploded',(40,-180,205),(40,-23,0),282)
vertical=bpy.data.scenes.new('03_Vertical_Exploded')
for lv,offset in [('L2',42),('L3',0),('L4',-42)]:
 o=bpy.data.objects.new('Vertical_Display_'+lv,None);o.instance_type='COLLECTION';o.instance_collection=levels[lv];vertical.collection.objects.link(o);o.location=(0,0,offset)
vertical.camera=cam(vertical,'Camera_Vertical_Exploded',(130,-135,100),(40,-24,0),150)
plans=[]
for lv,z in [('L2',4),('L3',0),('L4',-4)]:
 sc=bpy.data.scenes.new('Plan_'+lv);sc.collection.children.link(levels[lv]);sc.camera=cam(sc,'Camera_'+lv+'_Plan',(41,-24,z+100),(41,-24,z),94);plans.append((lv,sc))
for sc in [s,expl,vertical]+[sc for _,sc in plans]:
 sc.unit_settings.system='METRIC';sc.unit_settings.scale_length=1
 try:sc.render.engine='BLENDER_WORKBENCH'
 except TypeError:pass
 sc.world=s.world;sh=sc.display.shading;sh.light='STUDIO';sh.color_type='MATERIAL';sh.show_shadows=False;sh.show_cavity=True;sh.background_type='VIEWPORT';sh.background_color=(.88,.88,.88)
 sc.render.image_settings.file_format='PNG';sc.render.resolution_percentage=100;sc.render.resolution_x=1800;sc.render.resolution_y=1200
 if 'Standard' in [i.identifier for i in sc.view_settings.bl_rna.properties['view_transform'].enum_items]:sc.view_settings.view_transform='Standard'
 sc['scope']='Static three-storey whitebox: portal topology validated, gameplay not implemented'
expl.render.resolution_x=2800;expl.render.resolution_y=1100
vertical.render.resolution_x=1800;vertical.render.resolution_y=1800
# Add linked, non-colliding model annotations and exploded connection leaders.
import runpy
runpy.run_path(str(ROOT/'tools/blender/annotate_mine_connections.py'))['annotate_connections']()
runpy.run_path(str(ROOT/'tools/blender/localize_mine_rooms.py'))['localize_rooms']({'levels':{lv:{'rooms':spec} for lv,spec in room_specs.items()}})
# Default to readable exploded view, change scene to 01_Assembled to inspect actual stacking.
bpy.context.window.scene=vertical
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':area.spaces.active.region_3d.view_perspective='CAMERA';area.spaces.active.clip_end=1000
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'mine-complex-three-storeys.blend'))
for sc,name in [(expl,'exploded-overview'),(vertical,'vertical-exploded'),(s,'assembled-overview')]+[(sc,lv.lower()+'-plan') for lv,sc in plans]:
 sc.render.filepath=str(OUT/(name+'.png'));bpy.context.window.scene=sc;bpy.ops.render.render(write_still=True)
report={'regions_per_level':{lv:len(set(g.values())) for lv,g in data.items()},'doorways':len(portals),'vertical_connections':4,'reachable_nodes':len(flood(tokens)),'progression':history,'checks':['objective acquisition order reachable','no lower-floor access without service key','no L4 before fuse','all portal graph nodes reachable after unlock','L3 observation chasm retained','L2/L3 atrium voids retained'],'not_tested':['capsule traversal','physics','ladder climbing','trigger state implementation']}
check=ROOT/'outputs/mine-complex';check.mkdir(parents=True,exist_ok=True);(check/'checks.json').write_text(json.dumps(report,indent=2))
(OUT/'layout.json').write_text(json.dumps({'levels':{lv:{'z':{'L2':4,'L3':0,'L4':-4}[lv],'rooms':spec} for lv,spec in room_specs.items()},'portals':portals,'stairs':stairs,'objectives':objectives},indent=2))
print('COMPLEX_COMPLETE',json.dumps(report))
