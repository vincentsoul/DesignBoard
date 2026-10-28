/** Original classroom deck. Use the Codex bundled artifact-tool runtime.
 * Usage: node build-grade1-logic-slides.mjs --modules DIR --skill DIR --python FILE
 * Source illustrations: the companion story workbook in ignored outputs/.
 */
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const args=Object.fromEntries(process.argv.slice(2).reduce((a,v,i,all)=>i%2===0?[...a,[v.replace(/^--/,''),all[i+1]]]:a,[]));
for(const k of ['modules','skill','python'])if(!args[k])throw Error(`Missing --${k}`);
process.env.RUNTIME_NODE_MODULES=args.modules;
const {Presentation,PresentationFile}=await import(pathToFileURL(path.join(args.modules,'@oai/artifact-tool/dist/artifact_tool.mjs')));
const {finalizePresentation}=await import(pathToFileURL(path.join(args.skill,'container_tools/artifact_tool_utils.mjs')));
const data=JSON.parse(await fs.readFile(path.join(root,'docs/grade1-logic-content.json'),'utf8'));
const work=path.join(root,'work/grade1-ppt'),out=path.join(root,'outputs/grade1-ppt');
await fs.mkdir(work,{recursive:true});await fs.mkdir(out,{recursive:true});await fs.mkdir(path.join(work,'previews'),{recursive:true});
const pres=Presentation.create({slideSize:{width:1280,height:720}});
const FONT='Arial Unicode MS',INK='#263E46',TEAL='#317F75',MUTED='#647B80',ANSWER='#A45B35';
const slides=[],nativeTables=[];
function text(s,t,x,y,w,h,size=36,color=INK,bold=false){
 const a=s.shapes.add({geometry:'textbox',name:`text-${s.shapes.items.length}`,position:{left:x,top:y,width:w,height:h},fill:'none',line:{fill:'none',width:0}});
 a.text=String(t);a.text.style={typeface:FONT,fontSize:size,color,bold,autoFit:'none',wrap:'square',verticalAlignment:'top',insets:0};return a;
}
function slide(title,{lesson=null,kind='讲解',notes='',bg='#FFFFFF'}={}){
 const s=pres.slides.add();s.background.fill=bg;
 text(s,lesson===null?'一年级逻辑思维':`第 ${lesson+1} 讲  ${data.lessons[lesson].title}`,64,28,1140,34,22,TEAL);
 text(s,title,64,78,1150,76,44,INK,true);
 text(s,String(slides.length+1),1180,666,45,30,20,MUTED);
 s.speakerNotes.text=notes;
 slides.push({slide:s,title,lesson,kind,number:slides.length+1});return s;
}
async function image(s,name,x,y,w,h){
 const bytes=await fs.readFile(path.join(root,'outputs/grade1-logic-story/assets',name+'.png'));
 s.images.add({blob:bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),contentType:'image/png',alt:name,fit:'contain',position:{left:x,top:y,width:w,height:h}});
}
function grid(s,values,x,y,side=264){
 const t=s.tables.add({rows:3,columns:3,left:x,top:y,width:side,height:side,values:values.map(r=>r.map(String)),columnWidths:[side/3,side/3,side/3]});
 t.styleOptions={headerRow:false,bandedRows:false};t.borders.assign({fill:'#80ADA5',width:2,style:'solid'});
 for(let y=0;y<3;y++)for(let x=0;x<3;x++){
  const c=t.getCell(y,x);c.fill='#F5FAF7';c.text.style={typeface:FONT,fontSize:42,color:INK,alignment:'center',verticalAlignment:'middle',insets:8};
 }
 nativeTables.push(slides.length);return t;
}
const prompts=[
 '每行、每列都要有 1、2、3。\n这三张空白房间卡，该填什么？',
 '直路两端都放灯，一共 5 盏。\n相邻两盏相距 2 米，这条路长多少米？',
 '7 □ 2 □ 3 ＝ 8\n每格填一个加号或减号，数字顺序不能变。',
 '铅笔 1 元 5 角，橡皮 5 角。\n买这两样共多少钱？付 5 元，应找回多少？',
 '盒里先拿走 3 个球，再放入 2 个。\n最后有 7 个，原来有几个？',
 '△ ＋ △ ＝ 8\n△ ＋ ○ ＝ 7\n△ 和 ○ 分别表示几？',
 '一张纸对折一次后有几层？\n再把整叠纸对折一次，又有几层？'
];
const exampleAnswers=[
 '三行分别是 1 2 3、2 3 1、3 1 2。\n三个空格都填 2，再逐列检查。',
 '5 盏灯之间有 4 段。\n2＋2＋2＋2＝8 米。',
 '7－2＋3＝8\n四种组合逐个试，找到以后再验算。',
 '15 角＋5 角＝20 角＝2 元。\n找回 5－2＝3 元。',
 '先撤销放入的 2 个：7－2＝5。\n再把拿走的 3 个加回：5＋3＝8。',
 '△＝4，○＝3。\n代回检查：4＋4＝8，4＋3＝7。',
 '对折一次有 2 层，再对折有 4 层。\n图中的剪口远离折痕和外边，展开得到两个洞。'
];
// Short classroom wording keeps complete mathematical conditions on screen.
const questionCopy={
 '2-4':'5  只加一对小括号，不改数字和原来的符号。\n让下面的算式成立：\n10－3＋2＝5',
 '5-1':'2  ○＋○＝10，○＋□＝8。\n同一道题中，相同图形代表相同数字。\n○ 和 □ 分别是多少？',
 '6-1':'2  画一个长方形，画出一条能让两边重合的折线。\n折一折，检验你的想法。',
 '6-2':'3  对折一次，在重叠的两层纸上剪一个三角洞。\n洞不碰折痕，也不碰纸边。\n展开后有几个洞？它们有什么关系？',
 '6-3':'4  整叠纸连续对折两次，在4层重叠处剪一个洞。\n剪口不碰任何折边和外边。\n展开后有几个洞？请预测，再验证。'
};
const answerCopy={
 '2-2':'3  5＋3－2＝6，填加号、减号。\n另外三种组合的结果分别是10、4、0。',
 '6-1':'2  过中心、平行于一组边的直线都可以。\n普通长方形有横、竖两条对称轴。\n斜着连接对角的线不能让两边重合。',
 '6-4':'5  下一次是16层。\n洞数还取决于剪口位置和形状。\n剪口碰到折痕时，展开后可能连成一个洞。'
};
const bg='#FFFFFF';
let s=slide('一年级逻辑思维',{kind:'封面',notes:'原创课堂版，非学而思官方教材。按七讲分次使用。问题与答案在相邻页面，普通翻页即可揭晓。'});
text(s,'森林探险课堂',64,250,1140,104,72,TEAL,true);text(s,'和米米、点点一起找线索',68,398,1120,60,38);text(s,'7 讲课程    42 道练习',68,510,1080,50,30,MUTED);text(s,'原创配套课件  非学而思官方教材',68,602,1080,35,22,MUTED);
s=slide('七次探险',{kind:'目录',notes:'在放映模式中输入起始页码再按回车，可直接进入相应讲次。每讲13页。'});
for(let i=0;i<7;i++)text(s,`${i+1}  ${data.lessons[i].title}    第 ${4+i*13} 页`,78,165+i*63,1110,48,32);
s=slide('课堂使用说明',{kind:'教师说明',notes:'推荐每讲30至40分钟，按孩子状态调整，挑战题选做。本课件没有设置点击动画，使用独立答案页逐步揭晓。课堂可配合白板、实物与配套教材。'});
text(s,'每讲建议分一次或两次上完',72,180,1100,60,38,TEAL,true);
text(s,'题目页：先观察，自己想，再说明理由。\n答案页：下一页一起核对，可随时返回题目。\n动手页：准备瓶盖、卡片和薄纸。',72,280,1110,230,34);
text(s,'教师备注里有提问、答案和易错点提示。\n剪纸活动由成人看护，挑战题可以选做。',72,555,1110,95,28,MUTED);
for(let i=0;i<7;i++){
 const l=data.lessons[i],source=`内容来源：docs/grade1-logic-content.json 第${i+1}讲。插图来自原创配套故事教材。`;
 s=slide(l.scene,{lesson:i,kind:'故事导入',notes:`${source}\n建议3分钟。教师分角色读对话。先请孩子观察画面，再提问：${l.mission}\n准备材料：${l.materials}`});
 await image(s,`scene-${i+1}`,64,152,1152,340);
 text(s,l.dialogue.split('\n').slice(0,2).join('\n'),72,504,1135,100,30);text(s,l.mission,72,619,1130,43,28,TEAL,true);
 s=slide('这一讲的思考方法',{lesson:i,kind:'知识讲解',notes:`${source}\n${l.explain}\n引导：${l.parent}\n易错点：${l.reminder}`});
 for(let j=0;j<3;j++)text(s,`${j+1}  ${l.steps[j]}`,76,190+j*126,1120,100,36,j===0?TEAL:INK,j===0);
 s=slide('一起想一想',{lesson:i,kind:'例题',notes:`${source}\n停留2至3分钟，让孩子先尝试。\n讲解：${l.example}\n答案在下一页。`});
 if(i===0){text(s,prompts[i],72,180,660,180,38);grid(s,[[1,'',3],['',3,1],[3,1,'']],850,185,300);text(s,'先找只缺一个数的横行。',72,450,680,100,34,TEAL);}
 else text(s,prompts[i],76,205,1120,310,i===5?52:42);
 text(s,'你先发现了哪条线索？',76,588,1120,58,32,TEAL);
 s=slide('一起核对例题',{lesson:i,kind:'例题答案',notes:`${source}\n${l.example}\n请孩子复述一步解法，并按条件检查。`});
 await image(s,`diagram-${i+1}`,64,150,1152,322);text(s,exampleAnswers[i],72,500,1130,150,34,ANSWER,true);
 s=slide('换你来试一试',{lesson:i,kind:'跟练题',notes:`${source}\n问题：${l.follow}\n参考答案：${l.followanswer}\n先独立思考，再请一位孩子解释。下一页揭晓。`});
 text(s,l.follow.replace(/^换你来：/,''),76,218,1120,265,42);text(s,'想好以后，说说你是怎样检查的。',76,565,1120,74,30,TEAL);
 s=slide('换你来的答案',{lesson:i,kind:'跟练答案',notes:`${source}\n${l.followanswer}`});text(s,i===6?'相同。\n两个洞由同一剪口剪出，在折痕两侧对应。':l.followanswer,76,218,1120,300,44,ANSWER,true);text(s,'不同的方法，只要符合条件，也可以。',76,575,1120,65,28,MUTED);
 for(const group of [[0,1],[2,3],[4]]){
  const label=group[0]===0?'基础练习':group[0]===2?'进阶练习':'挑战题';
  s=slide(label,{lesson:i,kind:'练习题',notes:`${source}\n${group.map(n=>`第${n+1}题：${l.qs[n][1]}\n答案：${l.ans[n]}`).join('\n')}\n${label==='挑战题'?'选做，不要求所有孩子完成。':'建议2至4分钟。可请孩子在纸上作答。'}\n下一页揭晓答案。`});
  if(i===0){
   if(group[0]===0){text(s,'1  按每行每列都有 1、2、3 的规则填空。',72,166,1120,70,32);grid(s,[[1,'',3],['',3,1],[3,1,'']],100,250,250);text(s,'2  第一行是 2、3、□。\n小熊说填 2，你同意吗？',470,290,700,150,36);}
   if(group[0]===2){text(s,'3  继续用每行每列不重复的规则。',72,166,1120,70,32);grid(s,[[1,'',''],['',1,2],[2,'',1]],100,250,250);text(s,'4  小猫、小狗、小兔各住一间房。\n小猫在中间，小狗不在右边。\n左、中、右分别住谁？',470,250,715,220,34);}
   if(group[0]===4){text(s,'只知道左上角是 1，能确定全部空格吗？\n请摆出两种不同答案。',72,166,1120,120,36);grid(s,[[1,'',''],['','',''],['','','']],100,335,240);text(s,'相同规则，条件够不够？',470,390,710,100,36,TEAL);}
  }else{
   for(let j=0;j<group.length;j++){
    const n=group[j];text(s,questionCopy[`${i}-${n}`]??`${n+1}  ${l.qs[n][1]}`,76,group.length===1?210:170+j*224,1120,group.length===1?300:194,group.length===1?39:34);
   }
  }
  text(s,label==='挑战题'?'挑战题可以选做。你也可以用实物来验证。':'先自己试，再和同桌说一说。',76,615,1120,42,26,TEAL);
  s=slide(label+'答案',{lesson:i,kind:'练习答案',notes:`${source}\n${group.map(n=>`${n+1}：${l.ans[n]}`).join('\n')}\n易错提示：${l.reminder}`});
  if(i===0&&group[0]===0){grid(s,[[1,2,3],[2,3,1],[3,1,2]],96,210,280);text(s,'1  三个空格都填 2。\n填好以后再检查每一列。\n\n2  不同意。缺的是 1。',440,205,750,300,38,ANSWER,true);}
  else if(i===0&&group[0]===2){grid(s,[[1,2,3],[3,1,2],[2,3,1]],96,210,280);text(s,'3  先看第二行，再看第一列。\n\n4  左边小狗，中间小猫，右边小兔。',440,210,750,300,36,ANSWER,true);}
  else if(i===0&&group[0]===4){text(s,'条件不够，答案不止一种。',72,166,1120,70,40,ANSWER,true);grid(s,[[1,2,3],[2,3,1],[3,1,2]],175,280,280);grid(s,[[1,3,2],[3,2,1],[2,1,3]],760,280,280);}
  else for(let j=0;j<group.length;j++){
   const n=group[j];text(s,answerCopy[`${i}-${n}`]??`${n+1}  ${l.ans[n]}`,76,group.length===1?210:180+j*224,1120,group.length===1?300:195,group.length===1?39:34,ANSWER,true);
  }
 }
 s=slide('这一讲我会了',{lesson:i,kind:'小结与活动',notes:`${source}\n建议5分钟。${l.game}\n教师可将“家长”换成“同桌”，孩子轮流出题、讲解。\n课堂观察：能否找到条件、表达思路、检查结果。\n${l.reminder}`});
 text(s,l.goal,76,180,1120,140,40,TEAL,true);text(s,'课堂小游戏',76,345,1120,60,34,INK,true);text(s,l.game.replaceAll('家长','同桌').replaceAll('孩子','另一位同学'),76,425,1120,155,32);text(s,'我今天学会了 ____________________',76,618,1120,45,28,MUTED);
}
s=slide('参考资料',{kind:'参考资料',notes:'参考的是公开样例与课程介绍，不是所提供冬季大纲对应的整套教材。沿用上一版教材的研究来源，本次转换未新增网络研究。原题和插图均为原创配套内容。\n'+data.references.map(r=>`${r.title}\n${r.url}\n${r.type}\n${r.use}`).join('\n\n')});
text(s,'本课件沿用故事教材的公开参考资料。\n人物、练习与插图均为原创配套内容。',72,170,1120,110,32);
text(s,'学而思秘籍公开介绍与一年级样例\nhttps://www.hikid.vip/8993/\n\n学而思轻课官方介绍\nhttps://www.xueersi.cn/shuxue',72,315,1120,235,27,MUTED);
text(s,'完整书目及参考范围见本页教师备注。\n本课件非学而思官方教材。',72,590,1120,70,24,MUTED);
if(slides.length!==95)throw Error(`Unexpected slide count ${slides.length}`);
// Render all slides and keep a structural manifest for the classroom flow.
await fs.writeFile(path.join(work,'slides.json'),JSON.stringify(slides.map(({slide,...m})=>m),null,2));
await fs.writeFile(path.join(root,'docs/grade1-logic-slide-outline.md'),'# 一年级逻辑思维课堂课件\n\n16:9 宽屏，共 95 页。7 讲，每讲 13 页。42 道练习均有独立的后续答案页。\n\n'+slides.map(m=>`${m.number}. ${m.lesson===null?'':`第${m.lesson+1}讲 `}${m.title}（${m.kind}）`).join('\n')+'\n\n采用普通翻页揭晓答案，不依赖动画。教师备注包含带学提示。\n');
for(let i=0;i<slides.length;i++){
 const blob=await pres.export({slide:slides[i].slide,format:'png',scale:1});
 await fs.writeFile(path.join(work,'previews',`slide-${String(i+1).padStart(3,'0')}.png`),new Uint8Array(await blob.arrayBuffer()));
 if(i%10===0)console.log(`Rendered ${i+1}/${slides.length}`);
}
const candidate=path.join(work,'candidate.pptx');await(await PresentationFile.exportPptx(pres)).save(candidate);
const finalPath=path.join(out,'一年级逻辑思维森林探险课堂完整版.pptx');
await finalizePresentation({workspaceDir:root,candidatePath:candidate,finalPath,pythonExecutable:args.python,integrityValidatorPath:path.join(args.skill,'container_tools/inspect_presentation_package_integrity.py'),layoutValidatorPath:path.join(args.skill,'container_tools/inspect_presentation_layout_geometry.py'),layoutArgs:['--expected-slide-size-emu','12192000,6858000','--validate-bullet-geometry','--validate-heading-fit',...Array.from(new Set(nativeTables)).flatMap(n=>['--require-native-table-slide',String(n)])],requiredNativeTableOwnerSlides:[...new Set(nativeTables)],fontPolicy:{basis:'design',families:[FONT]},verifyArtifactToolImport:true,receiptPath:path.join(work,'validation-full.json')});
console.log(finalPath);
