"""Chinese room labels matching the annotated plans; preserves whitebox geometry."""
import bpy, ast, json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]

def localize_rooms(layout_override=None):
 tree=ast.parse((ROOT/'tools/blender/draw_mine_complex_plans.py').read_text())
 names=None
 for n in tree.body:
  if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='N' for t in n.targets):names=ast.literal_eval(n.value)
 assert names
 layout=layout_override or json.loads((ROOT/'assets/levels/mine-complex/layout.json').read_text())
 translations={};room_sources=set()
 for lv,info in layout['levels'].items():
  for name,r in info['rooms']:
   if name.endswith('_Stair'):continue
   source=lv+'_'+name+'_Label';translations[source]=(names[name][0],1.4);room_sources.add(source)
 for name,cn in {'ENTRY':'坠落入口','OVERLOOK':'观察台','MINE':'采矿区','RESCUE':'囚笼救援区','EXIT':'塌方出口区','OLD MINE':'旧脉通道'}.items():
  source='L3_'+name+'_Label';translations[source]=(cn,1.4);room_sources.add(source)
 for lv,title in [('L2','L2 管理层｜标高 +4m'),('L3','L3 救援采矿层｜标高 0m'),('L4','L4 动力层｜标高 −4m')]:
  translations[lv+'_Title']=(title,1.6);translations[lv+'_North']=('北',1.25)
  if lv!='L4':translations[lv+'_Atrium_Label']=('中庭挑空｜不可通行',1.05)
  for key,gate in [('W','维修钥匙' if lv=='L2' else '保险丝'),('E','恢复供电')]:
   source=key+'_'+lv+'_StairLabel'
   if source in bpy.data.objects or any(o.get('room_label_source')==source for o in bpy.data.objects):translations[source]=(f'{key} '+('西楼梯' if key=='W' else '东楼梯')+'｜'+gate,.65)
 for key,cn in [('SERVICE_KEY','维修钥匙'),('FUSE','保险丝'),('POWER','恢复供电'),('EARTH','厚土能力')]:translations[key+'_Text']=(cn,.85)
 path=Path('/System/Library/Fonts/Supplemental/Arial Unicode.ttf')
 assert path.exists(),'Chinese font unavailable'
 font=bpy.data.fonts.load(str(path))
 original_scene=bpy.context.window.scene
 changed=[]
 for source,(cn,size) in translations.items():
  old=bpy.data.objects.get(source)
  if old is None:old=next((o for o in bpy.data.objects if o.get('room_label_source')==source),None)
  if old is None:continue
  owners=[sc for sc in bpy.data.scenes if old.name in sc.objects]
  assert owners,source
  bpy.context.window.scene=owners[0]
  groups=list(old.users_collection);matrix=old.matrix_world.copy()
  cv=bpy.data.curves.new('ZH_'+source,'FONT');cv.body=cn;cv.font=font;cv.size=size;cv.align_x='CENTER';cv.align_y='CENTER';cv.extrude=.003
  # Use existing dark annotation material, without embedding the system font in the file.
  cv.materials.append(bpy.data.materials['Annotation'])
  tmp=bpy.data.objects.new('ZH_TMP_'+source,cv)
  for c in groups:c.objects.link(tmp)
  tmp.matrix_world=matrix
  if source=='L2_Records_Label':tmp.location.y=-6
  if source=='L2_West_Safe_Room_Label':tmp.location.y=-32.5
  if source=='L4_Power_Console_Label':tmp.location.y=-19.1
  bpy.context.view_layer.update()
  me=bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
  o=bpy.data.objects.new('ZH_'+source,me)
  for c in groups:c.objects.link(o)
  o.matrix_world=tmp.matrix_world.copy();o['room_label_source']=source;o['label_text']=cn;o['annotation_only']=True;o['collision']='NONE';o['language']='zh-CN'
  bpy.data.objects.remove(tmp,do_unlink=True);bpy.data.objects.remove(old,do_unlink=True)
  o.name='ZH_'+source
  changed.append(source)
 assert room_sources.issubset(changed),room_sources-set(changed)
 assert all(any('\u4e00'<=c<='\u9fff' for c in o['label_text']) for o in bpy.data.objects if o.get('room_label_source'))
 bpy.context.window.scene=original_scene
 print('CHINESE_LABELS',len(changed),'labels;',len(room_sources),'room/area names; geometry unchanged')
 return len(changed)

if __name__=='__main__':localize_rooms()
