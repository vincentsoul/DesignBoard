"""Make the A4 classroom book from the existing lesson data and story artwork."""
import argparse
import io
import json
from pathlib import Path

from PIL import Image, ImageOps
from docx import Document
from docx.enum.section import WD_SECTION_START
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_TAB_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

ROOT = Path(__file__).resolve().parents[1]


def xml(parent, tag, **attrs):
    el = OxmlElement('w:' + tag)
    for key, value in attrs.items():
        el.set(qn('w:' + key), str(value))
    parent.append(el)
    return el


def build(output, assets, *, color=False):
    data = json.loads((ROOT / 'docs/grade1-logic-content.json').read_text())
    lessons = data['lessons']
    doc = Document()
    doc.core_properties.title = '一年级逻辑思维 森林探险'
    doc.core_properties.author = ''
    sec = doc.sections[0]
    sec.page_width, sec.page_height = Cm(21), Cm(29.7)
    sec.left_margin, sec.right_margin = Cm(2.5), Cm(1.8)
    sec.top_margin, sec.bottom_margin = Cm(1.85), Cm(1.85)
    sec.footer_distance = Cm(0.85)
    sec.different_first_page_header_footer = True
    xml(doc.settings.element, 'mirrorMargins')

    for name, size, before, after in [
        ('Normal', 14, 0, 7), ('Title', 34, 0, 12),
        ('Heading 1', 24, 0, 12), ('Heading 2', 16, 9, 5),
    ]:
        style = doc.styles[name]
        style.font.name = 'Arial Unicode MS'
        style.font.size = Pt(size)
        style.font.color.rgb = RGBColor(0, 0, 0)
        fonts = style.element.get_or_add_rPr().rFonts
        fonts.set(qn('w:eastAsia'), 'Arial Unicode MS')
        for key in ('asciiTheme', 'hAnsiTheme', 'eastAsiaTheme'):
            fonts.attrib.pop(qn('w:' + key), None)
        fmt = style.paragraph_format
        fmt.space_before, fmt.space_after = Pt(before), Pt(after)
        fmt.line_spacing = 1.1
    for el in doc.styles.element.xpath('.//w:pBdr'):
        el.getparent().remove(el)
    footer = sec.footer.paragraphs[0]
    footer.alignment = WD_ALIGN_PARAGRAPH.CENTER
    footer.add_run('森林探险    ').font.size = Pt(10)
    xml(footer._p, 'fldSimple', instr='PAGE')

    def p(text='', size=None, before=0, after=None):
        para = doc.add_paragraph()
        para.paragraph_format.space_before = Pt(before)
        if after is not None:
            para.paragraph_format.space_after = Pt(after)
        run = para.add_run(text)
        if size:
            run.font.size = Pt(size)
        return para

    def page(label, title, *, chapter=False):
        if chapter:
            section = doc.add_section(WD_SECTION_START.NEW_PAGE)
            section.different_first_page_header_footer = False
            # The section break occupies its own paragraph at the end of the previous page.
            break_format = doc.paragraphs[-1].paragraph_format
            break_format.space_before = break_format.space_after = Pt(0)
            break_format.line_spacing = Pt(1)
        marker = p(label, 14 if chapter else 11, after=7)
        marker.runs[0].bold = chapter
        if color:
            marker.runs[0].font.color.rgb = RGBColor(0x31, 0x7F, 0x75)
        marker.paragraph_format.page_break_before = not chapter
        marker.paragraph_format.keep_with_next = True
        heading = doc.add_heading(title, 1)
        if chapter:
            heading.runs[0].font.size = Pt(28)

    def h(title):
        doc.add_heading(title, 2)

    def pic(name, width=16.3, *, source=None, max_height=None):
        stream = io.BytesIO()
        source = source or assets / (name + '.png')
        if max_height:
            with Image.open(source) as im:
                width = min(width, max_height * im.width / im.height)
        if color:
            stream.write(source.read_bytes())
        else:
            with Image.open(source) as im:
                ImageOps.grayscale(im).save(stream, 'PNG')
        stream.seek(0)
        para = p(after=8)
        para.alignment = WD_ALIGN_PARAGRAPH.CENTER
        shape = para.add_run().add_picture(stream, width=Cm(width))
        shape._inline.docPr.set('descr', name)

    def ruled(count):
        for _ in range(count):
            para = p('\u00a0', 9, after=5)
            para.paragraph_format.line_spacing = Pt(25)
            borders = xml(para._p.get_or_add_pPr(), 'pBdr')
            xml(borders, 'bottom', val='single', sz=4, space=0, color='A5A5A5')
            xml(borders, 'between', val='single', sz=4, space=0, color='A5A5A5')

    def grid(rows, second=False):
        width = 1.42
        vals = [r + [''] + r if second else r for r in rows]
        count = len(vals[0])
        table = doc.add_table(rows=3, cols=count)
        table.autofit = False
        for k, col in enumerate(table.columns):
            col.width = Cm(1 if k == 3 else width)
        table_width = table._tbl.tblPr.find(qn('w:tblW'))
        table_width.set(qn('w:type'), 'dxa')
        table_width.set(qn('w:w'), str(round((6 * width + 1 if second else 3 * width) * 567)))
        for y, row in enumerate(vals):
            table.rows[y].height = Cm(width)
            xml(table.rows[y]._tr.get_or_add_trPr(), 'cantSplit')
            for x, val in enumerate(row):
                cell = table.cell(y, x)
                cell.width = Cm(1 if x == 3 else width)
                cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
                pp = cell.paragraphs[0]
                pp.alignment = WD_ALIGN_PARAGRAPH.CENTER
                pp.paragraph_format.space_after = Pt(0)
                pp.add_run(str(val)).font.size = Pt(17)
                borders = xml(cell._tc.get_or_add_tcPr(), 'tcBorders')
                for edge in ('top', 'left', 'bottom', 'right'):
                    xml(borders, edge, val='nil' if x == 3 else 'single',
                        sz=8, color='D9D9D9')

    grids = {
        1: [[1, '', 3], ['', 3, 1], [3, 1, '']],
        3: [[1, '', ''], ['', 1, 2], [2, '', 1]],
        5: [[1, '', ''], ['', '', ''], ['', '', '']],
    }

    def question(i, lesson, n):
        level, prompt = lesson['qs'][n - 1]
        h(f'{n}  {level}')
        if i == 0 and n in grids:
            p(prompt.split('\n')[0])
            grid(grids[n], second=n == 5)
        else:
            p(prompt)
            ruled(3 if n == 5 else 2)

    p('小学一年级', 15, before=16, after=10)
    doc.add_heading('一年级逻辑思维\n森林探险', 0)
    pic('三个伙伴走向森林游园会', source=ROOT / 'docs/assets/grade1-forest-cover.png',
        max_height=8)
    h('游园会的探险地图')
    p('「游园会快开场啦！」小兔米米拉上小狐点点和小龟慢慢。'
      '刚到旅馆，风就吹乱了九宫格里的房间卡。补好卡片，他们沿着花灯小路往前走。'
      '米米数灯，点点数灯与灯之间的路段，谁数得多？')
    p('小火车也在等他们。车厢间少了加号和减号，补对了才能发车。'
      '到了森林小店，三个伙伴又得看价签、付钱，还要数清找回的钱。')
    p('果盒里的果子拿了又放，慢慢想从最后一步倒着找回原来的数量。'
      '接着，他们遇见一串藏在图形里的数字密码。到了纸艺工坊，'
      '一张对折的纸剪开再展开，两边会变成什么样？')
    p('先在图里找找小火车和纸鸟，再翻开目录，和三个伙伴一起出发吧！')
    p('姓名 ____________________    班级 ____________________', 15, before=16)
    p('原创配套教材', 11, before=8)

    page('开始探险', '目录', chapter=True)
    for i, lesson in enumerate(lessons):
        row = p(f'第 {i+1} 讲  {lesson["title"]}\t{3 + i*4}', 14, after=9)
        row.paragraph_format.tab_stops.add_tab_stop(Cm(16.3), WD_TAB_ALIGNMENT.RIGHT)
    row = p('参考答案与教学提示\t31', 14, after=9)
    row.paragraph_format.tab_stops.add_tab_stop(Cm(16.3), WD_TAB_ALIGNMENT.RIGHT)
    row = p('教师备注与参考资料\t38', 14)
    row.paragraph_format.tab_stops.add_tab_stop(Cm(16.3), WD_TAB_ALIGNMENT.RIGHT)

    for i, lesson in enumerate(lessons):
        label = f'第 {i+1} 讲'
        page(label + '  故事与方法', lesson['title'], chapter=True)
        pic(f'scene-{i+1}')
        p(lesson['dialogue'], after=11)
        h('今天的任务')
        p(lesson['mission'])
        h('先玩一玩再发现')
        p(lesson['explain'])
        h('我的思考路线')
        for n, step in enumerate(lesson['steps'], 1):
            p(f'{n}  {step}', after=4)

        page(label + '  示范与跟练', '一起解开谜题')
        pic(f'diagram-{i+1}')
        h('跟着想一想')
        p(lesson['example'])
        h('换你来')
        p(lesson['follow'].removeprefix('换你来：'))
        ruled(4)
        h('说说我的办法')
        p('我先看到了什么？我是怎么检查答案的？')
        ruled(2)

        page(label + '  练习上', '我来当小探险家')
        p('先自己试一试。需要时，可以画图或用实物帮忙。', 12)
        for n in (1, 2, 3):
            question(i, lesson, n)

        page(label + '  练习下', '再往前走一步')
        for n in (4, 5):
            question(i, lesson, n)
        h('动手活动')
        p(lesson['game'])
        h('我的发现')
        p('我今天学会了什么？可以写下来，也可以画出来。', 12)
        ruled(2)

    for i, lesson in enumerate(lessons):
        page(f'教师参考  第 {i+1} 讲', lesson['title'], chapter=i == 0)
        h('换你来')
        p(lesson['followanswer'])
        h('分层练习答案')
        for n, answer in enumerate(lesson['ans'], 1):
            p(f'{n}  {answer}')
        h('备课材料')
        p(lesson['materials'])
        h('教学提示')
        p(lesson['parent'])
        p(lesson['reminder'])

    page('教师参考', '教师备注与参考资料')
    h('怎样用这本书')
    p('先读故事，再跟着例题想一想。练习可以写在书上，也可以摆实物、画图。挑战题选做。')
    p('每讲四页。老师可以先带读前两页，再让孩子独立完成练习，最后做动手活动。')
    h('参考资料')
    p('本书为原创配套教材。故事、题目与图示独立编写，教学组织参考以下公开资料。')
    for n, ref in enumerate(data['references'], 1):
        h(f'{n}  {ref["title"]}')
        p(ref['url'], 10)
    p('本书非学而思官方教材。', 11, before=14)
    output.parent.mkdir(parents=True, exist_ok=True)
    doc.save(output)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, default=ROOT / 'outputs/grade1-logic-story/assets')
    parser.add_argument('--color', action='store_true', help='Use the original color illustrations.')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    filename = '一年级逻辑思维森林探险彩色印刷版.docx' if args.color else '一年级逻辑思维森林探险印刷版.docx'
    build(args.output or ROOT / 'outputs/grade1-book' / filename, args.assets, color=args.color)
