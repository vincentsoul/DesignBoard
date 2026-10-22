"""Draw annotated plans from the V2 whitebox manifest and builder geometry constants."""
from pathlib import Path
import ast,json,html,math
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'docs/level-design/mine-complex-plans';OUT.mkdir(parents=True,exist_ok=True)
layout=json.loads((ROOT/'assets/levels/mine-complex/layout.json').read_text())
tree=ast.parse((ROOT/'tools/blender/build_mine_complex.py').read_text())
def literal(name):
 for node in ast.walk(tree):
  if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id==name for t in node.targets):
   return ast.literal_eval(node.value)
 raise ValueError(name)
CIRC=literal('CIRC');original=literal('original')
extensions=[]
for node in ast.walk(tree):
 if isinstance(node,ast.For) and isinstance(node.target,ast.Name) and node.target.id=='r' and isinstance(node.iter,ast.List):
  if "fill(g, 'Core', r)" in ast.unparse(node):extensions=ast.literal_eval(node.iter)
assert extensions
N={
'Records':('档案室','收集管理资料；取得维修钥匙','objective'),
'Dormitory':('矿工宿舍','居住痕迹与背景线索','admin'),
'Infirmary':('医务室','治疗物资与伤员信息','safe'),
'Chief_Office':('主管办公室','主管日志与区域线索','admin'),
'Survey_Lab':('勘探实验室','矿脉资料与勘探设备','admin'),
'Security':('保安室','门禁记录与保安线索','admin'),
'West_Storage':('西侧储物室','可选补给与探索奖励','storage'),
'West_Safe_Room':('西侧安全屋','拟作休整与存档点','safe'),
'East_Archives':('东侧档案库','供电后开放的资料支线','locked'),
'East_Exhibit':('东侧展室','矿史陈列与世界观线索','admin'),
'Meeting':('会议室','事件纪要与叙事探索','admin'),
'Dispatch':('调度室','运输记录与路径提示','admin'),
'Observation_Control':('监测控制室','矿区监测信息与导航提示','admin'),
'West_Inner_Office':('内勤办公室','西翼档案及线索支线','admin'),
'Radio':('通信室','无线电记录与联络线索','admin'),
'Key_Vault':('钥匙保管库','供电后开放的可选奖励','locked'),
'W_Stair':('西楼梯间','主线跨层；钥匙／保险丝门控','vertical'),
'E_Stair':('东楼梯间','供电后开放的跨层捷径','vertical'),
'West_Archive':('旧档案间','旧矿记录与背景支线','admin'),
'West_Maintenance':('西侧维修间','检修盖与维护梯接口','utility'),
'East_Vault':('东侧保险库','供电后开放的奖励房','locked'),
'Fuse_Store':('备件库','维修器材与可选补给','storage'),
'South_Store':('南侧储藏室','连接旧脉与救援区的支线','storage'),
'South_Safe_Room':('南侧安全屋','休整；从室内解开出口捷径','safe'),
'Water_Intake':('取水设施间','取水池占位与供水设施','utility'),
'Pump_Control':('泵控制室','水泵操作与排水线索','utility'),
'Generator':('发电机房','发电设备与动力来源','utility'),
'Workshop':('机修车间','工作台及维修设施','utility'),
'West_Cistern':('西侧蓄水区','蓄水设施；维护梯接口','utility'),
'Switchgear':('配电室','配电设施与电路线索','utility'),
'Service_Exit':('设备服务间','设备检修与后续接口预留','utility'),
'Boiler':('锅炉房','锅炉体量与大型设备空间','utility'),
'Valve_Room':('阀门室','管路阀组与设施线索','utility'),
'Tool_Room':('工具间','维修工具与补给支线','storage'),
'Power_Console':('电力控制室','安装保险丝；恢复供电','objective'),
'Emergency_Store':('应急物资库','供电后开放的物资支线','locked'),
'Atrium_Hall':('中庭大厅','三层共享中枢的底部大厅','hub')}
EXTRA=[('Entry',(2,2,12,10),'坠落入口','保留原入口；上层坠落通路未启用','hub'),('Overlook',(20,2,6,8),'观察台','隔4m裂隙观察矿区，不可直接跨越','hub'),('Mine',(30,2,26,20),'采矿区','保险丝目标；矿工救援支线','objective'),('Rescue',(34,30,16,16),'囚笼救援区','供电后安抚、拆链；获得小兽','objective'),('Exit',(64,34,12,10),'塌方出口区','厚土开路后进入后续区域','hub')]
colors={'admin':'#deebf5','safe':'#dcefe0','objective':'#ebddf4','locked':'#f7dddd','vertical':'#f9e4bf','storage':'#ebe7d9','utility':'#dfe8e8','hub':'#d9eceb'}
records={};grids={}
for lv,info in layout['levels'].items():
 rooms=list(info['rooms']);g={}
 def fill(tag,r):
  x,y,w,h=r
  for i in range(x,x+w):
   for j in range(y,y+h):g[i,j]=tag
 if lv=='L3':
  for r in original+extensions:fill('Core',r)
 else:
  for r in CIRC:fill('Circulation',r)
 for name,r in rooms:
  fill(name,r)
 # Match builder ordering: circulation overrides rooms; atrium lower hall then stairs override.
 if lv!='L3':
  for r in CIRC:fill('Circulation',r)
  if lv=='L4':fill('Atrium_Hall',(26,22,24,12))
 for name,r in rooms:
  if name.endswith('_Stair'):fill(name,r)
 grids[lv]=g;rec=[]
 for name,r in rooms:
  cn,func,kind=N[name];rec.append({'name':name,'rect':r,'cn':cn,'func':func,'kind':kind})
 if lv=='L3':
  for name,r,cn,func,kind in EXTRA:rec.insert(0,{'name':name,'rect':r,'cn':cn,'func':func,'kind':kind})
  rec.sort(key=lambda q: (0 if q['name'] in [e[0] for e in EXTRA] else 1, [e[0] for e in EXTRA].index(q['name']) if q['name'] in [e[0] for e in EXTRA] else rooms.index([q['name'],q['rect']])))
 if lv in ('L2','L3'):rec.append({'name':'Atrium_Gallery','rect':[26,22,24,12],'cn':'中庭回廊','func':'围绕挑空的导航中枢；不是封闭房间','kind':'hub','area':'224','size':'外围24×12；开口16×4'})
 for i,r in enumerate(rec,1):r['id']=lv+'-'+str(i).zfill(2)
 records[lv]=rec

def esc(s):return html.escape(str(s),quote=True)
class Sheet:
 def __init__(self,title,sub):
  self.a=['<svg xmlns="http://www.w3.org/2000/svg" width="2400" height="2400" viewBox="0 0 2400 2400"><defs><pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse"><path d="M40 0H0V40" fill="none" stroke="#bfcbd0" stroke-width=".6"/></pattern></defs><rect width="2400" height="2400" fill="#f7f6f1"/><style>text{font-family:"PingFang SC","Microsoft YaHei",Arial,sans-serif;fill:#263640} .title{font-size:46px;font-weight:700}.subtitle{font-size:23px}.heading{font-size:27px;font-weight:700}.body{font-size:21px}.small{font-size:18px}.tiny{font-size:16px}</style>']
  self.text(70,82,title,'title');self.text(70,126,sub,'subtitle')
 def text(self,x,y,s,cls='body',anchor='start',fill=None):self.a.append(f'<text x="{x}" y="{y}" class="{cls}" text-anchor="{anchor}"'+(f' style="fill:{fill}"' if fill else '')+f'>{esc(s)}</text>')
 def rect(self,x,y,w,h,fill,stroke=None,dash=None):self.a.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}"'+(f' stroke="{stroke}" stroke-width="2"' if stroke else '')+(f' stroke-dasharray="{dash}"' if dash else '')+'/>')
 def line(self,x1,y1,x2,y2,color='#465660',width=3,dash=None):self.a.append(f'<path d="M{x1} {y1}L{x2} {y2}" fill="none" stroke="{color}" stroke-width="{width}"'+(f' stroke-dasharray="{dash}"' if dash else '')+'/>')
 def save(self,name):self.a.append('</svg>');(OUT/name).write_text(''.join(self.a))

for lv in ['L2','L3','L4']:
 z=layout['levels'][lv]['z'];name={'L2':'管理层','L3':'救援采矿层','L4':'动力层'}[lv]
 sh=Sheet(f'{lv} {name}｜房间功能与尺寸平面图',f'标高 Z={z:+d}m · 1格=2m · 北向上 · 布局坐标与当前Blender白模一致 · 尺寸为布局基准，非最终装修净尺寸')
 ox,oy,k=85,225,20
 def P(x,y):return ox+x*k,oy+y*k
 g=grids[lv]
 for x,y in sorted(g):sh.rect(*P(x,y),k,k,'#e9edef')
 # Tinted room footprints are functional overlays, not additional partitions.
 for r in records[lv]:
  x,y,w,h=r['rect'];sh.rect(*P(x,y),w*k,h*k,colors[r['kind']])
 if lv in ('L2','L3'):
  sh.rect(*P(30,26),16*k,4*k,'#f7f6f1','#8da3aa','9 6')
  sh.text(*P(38,28.0),'中庭挑空 16×4m','small','middle');sh.text(*P(38,29.1),'仅视觉关联，不可步行','tiny','middle')
 # Restore actual union gaps and annotate internal basis overlay conditions.
 sh.a.append(f'<path d="'+''.join(f'M{ox+x*k},{oy+y*k}h{k}v{k}h-{k}z' for x,y in g)+'" fill="url(#grid)"/>')
 for (x,y),tag in sorted(g.items()):
  for dx,dy in [(1,0),(-1,0),(0,1),(0,-1)]:
   other=g.get((x+dx,y+dy))
   if other==tag:continue
   if other is not None and (dx,dy) not in [(1,0),(0,1)]:continue
   if dx:
    fx=x+(dx==1);a=P(fx,y);b=P(fx,y+1)
    if lv=='L3' and fx==76 and 36<=y<40:continue
    if lv=='L3' and fx in (26,30) and 4<=y<8 and other is None:
     sh.line(*a,*b,'#2f9abc',4);continue
   else:
    fy=y+(dy==1);a=P(x,fy);b=P(x+1,fy)
   sh.line(*a,*b,width=5)
 for p in layout['portals']:
  if p['level']!=lv:continue
  axis,fix,span=p['axis'],p['fixed'],p['span'];a=P(fix,span[0]) if axis=='X' else P(span[0],fix);b=P(fix,span[1]) if axis=='X' else P(span[1],fix)
  sh.line(*a,*b,'#f7f6f1',12)
  sh.line(*a,*b,'#bb5a5a' if p.get('gate') else '#548a71',3)
 # Actual stair openings, with top-flight direction and label outside the hole.
 for key,(x,y,w,h) in layout['stairs'].items():
  front=y+h-2
  if lv!='L4':sh.rect(*P(x+1,front-6),120,120,'#f7f6f1','#89939a','6 4')
  for sx in [x+1,x+5]:
   for i in range(12):
    ystep=front-i*.3;sh.line(*P(sx,ystep),*P(sx+2,ystep),'#6f7b83',1)
  for sx,y1,y2 in [(x+2,front-.2,front-3.4),(x+6,front-3.4,front-.2)]:
   sh.line(*P(sx,y1),*P(sx,y2),'#a96c1e',3)
   px,py=P(sx,y2);sh.text(px,py,'↑' if y2<y1 else '↓','heading','middle',fill='#a96c1e')
 # Room labels; tiny spaces only ID+name, full dimensions appear in schedule.
 for r in records[lv]:
  x,y,w,h=r['rect'];cx=x+w/2;cy=y+h/2
  if r['name'].endswith('_Stair'):cy=y+h-.8
  if r['name']=='West_Safe_Room':cy=32.5
  if r['name']=='Rescue':cy=42
  if r['name']=='Atrium_Gallery':cx=38;cy=24
  sh.text(*P(cx,cy-.7),r['id']+' '+r['cn'],'small','middle')
  if r['name']!='Atrium_Gallery':sh.text(*P(cx,cy+.7),f'{w}×{h}m · {w*h}㎡','small','middle')
 # Main level-specific mechanisms and goals, each callout away from room title.
 if lv=='L3':
  sh.rect(*P(37,32),120,120,'none','#8e7898','5 4')
  sh.line(*P(39,38),*P(41,38),'#bb5a5a',5)
  sh.rect(*P(56,36),80,80,'#d3c9bc','#ad8a62')
  sh.line(*P(29,14),*P(29,18),'#bb5a5a',5)
  for txt,x,y in [('G1 拆链',29,16),('G2 囚笼6×6m',40,35),('G3 塌方4×4m',58,38)]:
   xx,yy=P(x,y);sh.rect(xx-72,yy-16,144,30,'#fff4e4','#c68d42');sh.text(xx,yy+5,txt,'tiny','middle')
  sh.line(*P(23,6),*P(35,6),'#c87830',3,'9 7')
  sh.text(*P(28,10),'4m裂隙','small','middle')
 # Service hatch marker.
 hx,hy=P(5,35);sh.rect(hx-20,hy-20,40,40,'#f7f6f1','#9d648a','5 3');sh.text(hx,hy+6,'H','small','middle')
 # Exterior dimensions and scale.
 xmin=min(x for x,y in g);xmax=max(x for x,y in g)+1;ymin=min(y for x,y in g);ymax=max(y for x,y in g)+1
 sh.line(*P(xmin,-.1),*P(xmax,-.1),'#82919a',2);sh.text(*P((xmin+xmax)/2,-.6),f'布局总跨度 {xmax-xmin}m','small','middle')
 sh.line(*P(-1.3,ymin),*P(-1.3,ymax),'#82919a',2);sh.text(65,oy+((ymin+ymax)/2)*k,f'{ymax-ymin}m','small')
 sh.line(120,1300,320,1300,width=5);sh.text(120,1332,'0');sh.text(320,1332,'10m','body','middle');sh.text(1580,1330,'北 N ↑','heading')
 # Side notes and local vertical connections.
 sh.text(1830,230,'楼层联系','heading')
 side=[('西楼梯 W','8×10m；梯段宽2m'),('L2 ↔ L3','维修钥匙开启'),('L3 ↔ L4','保险丝开启'),('东楼梯 E','8×12m；梯段宽2m'),('L2 ↔ L3 ↔ L4','恢复供电后开放'),('检修梯 H','当前封闭；计划厚土后启用'),('中庭挑空','三层视觉联系，不是通路')]
 for i,(a,b) in enumerate(side):
  sh.text(1830,280+i*78,a);sh.text(1830,310+i*78,b,'small')
 sh.text(1830,900,'图例','heading')
 for i,(lab,kind) in enumerate([('管理／线索','admin'),('主线目标','objective'),('安全休整','safe'),('设备设施','utility'),('供电锁房','locked'),('竖向交通','vertical')]):
  sh.rect(1830,930+i*42,25,25,colors[kind],'#87969d');sh.text(1870,951+i*42,lab,'small')
 sh.text(1830,1220,'绿线：普通2m门洞','small');sh.text(1830,1253,'红线：供电锁门','small');sh.text(1830,1286,'蓝线：观察窗','small')
 sh.text(1830,1319,'H虚框：检修盖（关闭）','small')
 sh.line(70,1380,2330,1380,'#b9c5cb',2);sh.text(70,1425,'房间功能与尺寸表（面积按布局矩形计算；功能为设计用途，尚未实现交互）','heading')
 rr=records[lv];n=math.ceil(len(rr)/2)
 for idx,r in enumerate(rr):
  col=idx//n;row=idx%n;x0=70+col*1155;y0=1470+row*67
  x,y,w,h=r['rect'];size=r.get('size',f'{w}×{h}m');area=r.get('area',str(w*h))
  sh.rect(x0,y0-22,1125,62,'#ffffff' if row%2==0 else '#edf0ee')
  sh.text(x0+10,y0,r['id']+' '+r['cn'],'small');sh.text(x0+600,y0,f'{size} / {area}㎡','small')
  sh.text(x0+10,y0+27,r['func'],'small')
 # Circulation schedule separately avoids misrepresenting corridors as closed rooms.
 y0=1470+n*67+30
 sh.text(70,y0,'交通与制图说明','heading')
 notes=['主回廊净宽4m；普通门洞2m；层高4m、墙高3m；坐标原点与白模相同，图上向下为计划Y正方向。',
 '房间尺寸标注为布局矩形；隔墙厚0.25m会占用居室边缘，楼梯开口及设备占位未从标注面积中扣除。',
 '功能分区底色不代表新增隔墙；实际房间边界按深色墙线与门洞读取。安全屋存档、物资和机关均为拟定用途。']
 if lv=='L3':notes+=['旧脉通道：南北段4×18m、横廊16×4m、竖井廊4×10m、东西段26×4m；矿区支路6×4m，出口廊14×4m。','中庭回廊与救援区覆盖范围局部重叠；两者之间没有额外墙体。新增通道开口以本图深色墙线为准。']
 else:notes+=['北回廊80×4m；西回廊4×24m；东回廊4×28m；南回廊56×4m；连接段按门洞与平面位置读取。']
 for i,t in enumerate(notes):sh.text(70,y0+40+i*32,t,'small')
 sh.text(70,2352,'源：assets/levels/mine-complex/layout.json + build_mine_complex.py ｜ 结构图V1 · 与白模V2配套','small')
 sh.save(lv.lower()+'-annotated.svg')

# Explicit floor relationship sheet: diagrams represent links, not scale drawings.
s=Sheet('三层矿洞｜楼层关联与门控结构','楼梯位置三层对齐 · 真实标高为+4 / 0 / −4m · 本页为关联示意，不按比例量尺')
xs=[250,860,1470,2040]
for x,title in zip(xs,['西楼梯 W','核心空间','东楼梯 E','检修梯 H']):s.text(x,235,title,'heading','middle')
for lv,y,z,title in [('L2',360,4,'管理与档案层'),('L3',930,0,'救援采矿层'),('L4',1500,-4,'动力设施层')]:
 s.rect(70,y-45,2260,360,'#e9eeef','#bccbd0')
 s.text(90,y,lv+' · '+title+' · Z='+f'{z:+d}m','heading')
 for x,label in [(250,'西楼梯间 8×10m'),(860,'中庭回廊' if lv!='L4' else '中庭大厅 24×12m'),(1470,'东楼梯间 8×12m'),(2040,'封闭检修盖')]:
  s.rect(x-155,y+75,310,105,'#ffffff','#768b96','8 5' if x==2040 else None);s.text(x,y+135,label,'body','middle')
 s.line(405,y+128,705,y+128,'#4c7c68',4);s.line(1015,y+128,1315,y+128,'#4c7c68',4)
 objective={'L2':'档案室 → 维修钥匙','L3':'矿区 → 保险丝；囚笼救援 → 厚土能力','L4':'电力控制室 → 安装保险丝 → 全区供电'}[lv]
 s.text(860,y+250,objective,'body','middle')
for y1,y2,label in [(540,1005,'维修钥匙'),(1110,1575,'保险丝')]:
 s.line(250,y1,250,y2,'#b57927',6);s.text(250,(y1+y2)/2-15,'↕','heading','middle');s.text(295,(y1+y2)/2+20,label)
for y1,y2 in [(540,1005),(1110,1575)]:
 s.line(1470,y1,1470,y2,'#537ca5',6);s.text(1470,(y1+y2)/2-15,'↕','heading','middle');s.text(1510,(y1+y2)/2+20,'供电后开放')
 s.line(2040,y1,2040,y2,'#9d648a',4,'12 9');s.text(2080,(y1+y2)/2,'预留接口','small')
 s.line(860,y1,860,y2,'#94a7ad',3,'5 8');s.text(885,(y1+y2)/2,'挑空视觉联系','small')
s.text(70,1960,'路线顺序','heading')
for i,t in enumerate(['① L2探索管理区 → 档案室取维修钥匙 → 西楼梯下降至L3。','② L3矿区取保险丝 → 西楼梯继续下降至L4。','③ L4电力控制室安装保险丝 → 东楼梯与供电锁房开放。','④ 经东楼梯返回L3 → 囚笼安抚＋拆链救小兽 → 厚土稳固塌方 → 离开。']):s.text(90,2010+i*48,t)
s.text(70,2240,'接口说明','heading')
s.text(90,2280,'W固定范围(14,28,8,10)，E固定范围(58,22,8,12)；每层差4m，24级台阶，踏步深0.3m，梯段宽2m。','small')
s.text(90,2313,'H位于西侧维护空间，当前检修盖关闭，未形成可游玩路线；中庭是挑空，不允许当作上下楼通路。','small')
s.text(90,2346,'原入口上方的坠落接口未在三层白模启用；房间设备、门禁与流程均为静态设计标注。','small')
s.save('floor-connections.svg')

# Readable complete schedule, with exact coordinates and circulation dimensions.
md=['# 三层矿洞：房间功能、尺寸与楼层关联\n','依据当前白模V2及其布局清单生成。编号与逐层图一致。尺寸为布局基准；面积为矩形面积，非扣除隔墙、井口、设备后的净可用面积。\n']
for lv in records:
 md += [f'## {lv} · Z={layout["levels"][lv]["z"]:+d}m\n','|编号|房间／区域|布局尺寸 m|面积 ㎡|左上坐标 X,Y|功能|','|---|---|---|---|---|---|']
 for r in records[lv]:
  x,y,w,h=r['rect'];md.append(f'|{r["id"]}|{r["cn"]}|{r.get("size",f"{w}×{h}")}|{r.get("area",w*h)}|{x},{y}|{r["func"]}|')
 md.append('')
md += ['## 楼层关联\n','- 西楼梯W：L2↔L3需维修钥匙；L3↔L4需保险丝。','- 东楼梯E：两段均需恢复供电。','- 检修梯H：目前关闭的预留接口；不计入可达路线。','- 中庭：上、中层16×4m挑空，下层24×12m大厅；仅有视觉联系。','- 原入口上方的坠落通路未在三层白模启用。','- 供电锁房：L2东侧档案库、钥匙保管库；L3东侧保险库；L4应急物资库。','- L3安全屋至出口侧设室内门闩捷径；囚笼G2与塌方G3分别为救援、厚土门控。\n','## 图面范围说明\n','L3旧脉交通由4×18m南北段、16×4m横廊、4×10m竖井廊、26×4m东西段组成；矿区支路6×4m，出口廊14×4m。转角矩形局部重叠，不能直接将分段面积相加当作总建筑面积。中庭回廊与救援区的功能覆盖也存在局部重叠。\n','房间功能细化用于后续内容制作，不表示白模已包含存档、物资、调查文本或机关脚本。']
(OUT/'room-schedule.md').write_text('\n'.join(md))
page=['<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>三层矿洞平面结构图</title><style>body{margin:0;background:#e7e9e8;font:16px system-ui;color:#263640}header{padding:24px;background:white;position:sticky;top:0}a{margin-right:24px;color:#345c80}main{max-width:1600px;margin:auto}img{width:100%;margin:20px 0;background:white}h2{padding:16px} @media print{header{position:static}section{break-after:page}img{margin:0}}</style><header><b>三层矿洞平面结构图</b><p><a href="#L2">上层管理区</a><a href="#L3">中层采矿区</a><a href="#L4">下层动力区</a><a href="#links">楼层关联</a><a href="room-schedule.md">房间明细表</a></p></header><main>']
for anchor,name,title in [('L2','l2-annotated','L2 管理层'),('L3','l3-annotated','L3 救援采矿层'),('L4','l4-annotated','L4 动力层'),('links','floor-connections','楼层关联')]:
 page.append(f'<section id="{anchor}"><h2>{title} · <a href="{name}.svg">查看矢量原图</a></h2><img src="{name}.svg" alt="{title}"></section>')
page.append('</main></html>');(OUT/'index.html').write_text(''.join(page))
for lv,rr in records.items():assert len({r['id'] for r in rr})==len(rr)
assert all(len(p['span'])==2 for p in layout['portals'])
print('Generated 4 SVG sheets, atlas and room schedule:', {lv:len(rr) for lv,rr in records.items()})
