"""Run in Blender's Text Editor. N sidebar > Three Echo > Play."""
import bpy
import math
import time
import blf
import os
from mathutils import Vector

SCENE = 'Three Echo'
COLORS = {'blue': (0.08, .32, .85, 1), 'red': (.85, .12, .10, 1),
          'green': (.10, .65, .28, 1), 'none': (.78, .81, .84, 1)}


def solve(green_on_a, null_on_b):
    colors = ['green', 'red', 'blue']  # C, B, A
    heights = [0, 0, 0]
    symbols = {'red': 2, 'green': 2 if green_on_a else 1,
               'none': 1 if null_on_b else -1}
    queue = ['red']
    scheduled = {'red'}
    waves = []
    while queue:
        symbol = queue.pop(0)
        cell = symbols.pop(symbol)
        source = colors[cell]
        if source == 'none' or source == symbol:
            continue
        affected = [i for i, color in enumerate(colors) if color == source]
        for i in affected:
            colors[i] = symbol
            heights[i] += 1
        for other, index in list(symbols.items()):
            if index in affected and other not in scheduled:
                scheduled.add(other)
                queue.append(other)
        waves.append((symbol, affected, colors.copy(), heights.copy()))
    return waves


def material(name, color):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = color
    node.inputs['Roughness'].default_value = .7
    return mat


def box(name, location, size, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new('Soft edges', 'BEVEL')
    bevel.width = .035
    bevel.segments = 2
    return obj


def label(body, location, size=.3, rotation=(math.pi / 2, 0, 0)):
    curve = bpy.data.curves.new('Echo lettering', 'FONT')
    curve.body = body
    curve.size = size
    obj = bpy.data.objects.new('Sign ' + body, curve)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = rotation
    obj.data.materials.append(bpy.data.materials['Echo white'])
    return obj


def socket_position(slot):
    scene = bpy.data.scenes[SCENE]
    if slot == 'stand':
        return Vector((-2.1, -2, .32))
    index = 2 if slot == 'A' else 1
    return Vector((.65, index * 3 + 1.5, scene.get('height_' + str(index), 0) + .12))


def set_symbol_positions():
    scene = bpy.data.scenes[SCENE]
    slots = {'green': 'A' if scene.echo_green else 'B',
             'none': 'B' if scene.echo_null else 'stand'}
    for obj in scene.objects:
        if 'echo_slot' in obj:
            obj.location = socket_position(obj['echo_slot'])
            if obj.get('echo_accept') == 'none' and obj['echo_slot'] == 'B':
                obj.location.x = -.65
    for key in ['red', 'green', 'none']:
        obj = bpy.data.objects['Echo crystal ' + key]
        if scene.get('held', '') == key:
            camera = scene.camera
            obj.location = camera.location + camera.rotation_euler.to_quaternion() @ Vector((.48, -.35, -.85))
        elif key == 'red':
            obj.location = (-.65, 7.5, scene.get('height_2', 0) + .65)
        else:
            obj.location = socket_position(slots[key]) + Vector((0, 0, .5))
            if key == 'none' and slots[key] == 'B':
                obj.location.x = -.65
        obj.hide_viewport = key in scene.get('consumed', '').split(',')


def aim(context, reach=30):
    camera = context.scene.camera
    context.view_layer.update()
    direction = camera.rotation_euler.to_quaternion() @ Vector((0, 0, -1))
    hit, position, normal, face, obj, matrix = context.scene.ray_cast(
        context.evaluated_depsgraph_get(), camera.location, direction, distance=reach)
    return obj if hit else None


def interaction_hint(context):
    scene = context.scene
    if scene.get('fired'):
        return None, '', False
    obj = aim(context)
    if not obj:
        return None, '', False
    close = aim(context, 2.4) == obj
    held = scene.get('held', '')
    key = obj.get('echo_crystal', '')
    if not held and key in {'green', 'none'}:
        if close:
            return obj, '按 E 拿起', True
        return None, '走近晶体后按 E 拿起', False
    if held and 'echo_slot' in obj:
        if obj.get('echo_accept') != held:
            return None, '基座不匹配，请寻找同色基座', False
        if not close:
            return None, '靠近基座后按 E 放置', False
        return obj, '按 E 放置', True
    if key == 'red':
        if held:
            return None, '先放下手中的晶体', False
        return obj, '鼠标左键射击，触发连锁', True
    return None, '', False


def interact(context):
    scene = context.scene
    if scene.get('fired'):
        scene['message'] = 'R to reset before arranging crystals.'
        return False
    obj = aim(context, 2.4)
    held = scene.get('held', '')
    if not obj:
        scene['message'] = 'Move closer and aim at a crystal or socket.'
        return False
    key = obj.get('echo_crystal', '')
    if not held and key in {'green', 'none'}:
        scene['held'] = key
        scene['message'] = 'Carrying ' + key.upper() + '. Aim at an empty socket and press E.'
        set_symbol_positions()
        return True
    if held and 'echo_slot' in obj:
        slot = obj['echo_slot']
        valid = {'green': {'A', 'B'}, 'none': {'stand', 'B'}}
        # B has separate sockets for green and null, so both may share its color.
        if obj.get('echo_accept') != held or slot not in valid[held]:
            scene['message'] = 'This socket is for ' + obj.get('echo_accept', '').upper() + '.'
            return False
        if held == 'green':
            scene.echo_green = slot == 'A'
        else:
            scene.echo_null = slot == 'B'
        scene['held'] = ''
        scene['message'] = 'Placed ' + held.upper() + ' on ' + slot + '.'
        set_symbol_positions()
        return True
    scene['message'] = 'E picks up crystals or places them into matching sockets.'
    return False


def reset():
    scene = bpy.data.scenes[SCENE]
    scene['fired'] = False
    scene['held'] = ''
    scene.echo_green = False
    scene.echo_null = False
    scene['consumed'] = ''
    scene['message'] = 'Arrange crystals, then fire. Each color change lifts a platform by 1m.'
    for i, color in enumerate(['green', 'red', 'blue']):
        scene['height_' + str(i)] = 0
        obj = bpy.data.objects['Echo platform ' + str(i)]
        obj.location.z = -.22
        obj.data.materials[0] = bpy.data.materials['Echo ' + color]
    set_symbol_positions()


def build():
    old = bpy.data.scenes.get(SCENE)
    if old:
        bpy.context.window.scene = old
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
    else:
        bpy.context.window.scene = bpy.data.scenes.new(SCENE)
    scene = bpy.context.scene
    scene.world = bpy.data.worlds.new('Echo world')
    scene.world.color = (.35, .35, .35)
    mats = {key: material('Echo ' + key, color) for key, color in COLORS.items()}
    white = material('Echo white', (.92, .93, .94, 1))
    material('Echo interaction highlight', (1.0, .7, .05, 1))
    gray = material('Echo gray', (.32, .36, .40, 1))
    box('Echo entrance', (0, -2, -.3), (5, 4, .6), white)
    box('Echo exit', (0, 11, 1.8), (3.4, 4, 4.4), white)
    box('Echo pit', (0, 4.5, -3), (7, 20, .3), gray)
    for i, color in enumerate(['green', 'red', 'blue']):
        box('Echo platform ' + str(i), (0, i * 3 + 1.5, -.22), (2.8, 2.8, .44), mats[color])
        for side in [-1, 1]:
            box('Echo guide', (side * 1.65, i * 3 + 1.5, .5), (.13, .18, 6), gray)
            for meter in range(5):
                box('Echo meter', (side * 1.65, i * 3 + 1.5 - .15, meter), (.35, .06, .04), white)
        label(['C', 'B', 'A'][i], (-1.2, i * 3 + .4, .04), .45, (0, 0, 0))
    for key in ['red', 'green', 'none']:
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=.3, radius2=0, depth=.75)
        obj = bpy.context.object
        obj.name = 'Echo crystal ' + key
        obj['echo_crystal'] = key
        obj.data.materials.append(mats[key])
    for slot, accept in [('A', 'green'), ('B', 'green'), ('B', 'none'), ('stand', 'none')]:
        obj = box('Echo socket ' + slot + ' ' + accept, socket_position(slot), (.8, .8, .22), mats[accept])
        obj['echo_slot'] = slot
        obj['echo_accept'] = accept
    box('Echo console', (-2.1, -2, .15), (.65, .65, .3), gray)
    box('Echo exit post left', (-1.25, 10.2, 5.2), (.18, .25, 2.4), gray)
    box('Echo exit post right', (1.25, 10.2, 5.2), (.18, .25, 2.4), gray)
    box('Echo exit lintel', (0, 10.2, 6.4), (2.7, .25, .18), gray)
    label('EXIT +4m', (-1.1, 10.0, 6.7), .35)
    label('THREE ECHO', (-2.3, 12.9, 8.3), .65)
    label('COLOR CHANGE = +1m', (-2.3, 12.9, 7.7), .33)
    bpy.ops.object.camera_add(location=(0, -3, 1.65))
    scene.camera = bpy.context.object
    scene.camera.name = 'Echo player'
    scene.camera.rotation_euler = (math.pi / 2, 0, 0)
    scene.camera.data.lens = 22
    scene.camera.data.clip_end = 150
    bpy.ops.object.light_add(type='AREA', location=(0, 4, 12))
    bpy.context.object.data.energy = 2200
    bpy.context.object.data.shape = 'DISK'
    bpy.context.object.data.size = 12
    scene.echo_green = False
    scene.echo_null = False
    reset()
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.color_type = 'MATERIAL'
            area.spaces.active.overlay.show_overlays = False
            area.spaces.active.show_region_ui = True
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100


class ECHO_OT_reset(bpy.types.Operator):
    bl_idname = 'echo.reset'
    bl_label = 'Reset platforms'

    def execute(self, context):
        reset()
        context.scene.camera.location = (0, -3, 1.65)
        context.scene.camera.rotation_euler = (math.pi / 2, 0, 0)
        return {'FINISHED'}


class ECHO_OT_play(bpy.types.Operator):
    bl_idname = 'echo.play'
    bl_label = 'Play / first person'
    _timer = None

    def clear_highlight(self):
        obj = getattr(self, 'highlighted', None)
        if obj and obj.name in bpy.data.objects:
            obj.data.materials[0] = self.original_material
        self.highlighted = None

    def update_highlight(self, context):
        obj, self.hint, self.hint_ready = interaction_hint(context)
        if obj != getattr(self, 'highlighted', None):
            self.clear_highlight()
            if obj:
                self.original_material = obj.data.materials[0]
                obj.data.materials[0] = bpy.data.materials['Echo interaction highlight']
                self.highlighted = obj

    def draw_hud(self, context):
        if context.area != self.area:
            return
        width, height = self.region.width, self.region.height
        blf.size(0, 22)
        blf.color(0, 1, 1, 1, 1)
        blf.position(0, width / 2 - 6, height / 2 - 8, 0)
        blf.draw(0, '+')
        font = self.font
        blf.size(font, 22)
        blf.color(font, *( (1, .85, .2, 1) if self.hint_ready else (1, 1, 1, 1) ))
        text_width, _ = blf.dimensions(font, self.hint)
        blf.position(font, (width - text_width) / 2, height / 2 - 48, 0)
        blf.draw(font, self.hint)
        blf.size(0, 16)
        blf.position(0, 28, 65, 0)
        blf.draw(0, 'WASD move | Mouse look | E pick/place | Left click shoot | R reset | Esc release')
        blf.position(0, 28, 40, 0)
        blf.draw(0, context.scene.get('message', ''))
        blf.position(0, 28, 90, 0)
        held = context.scene.get('held', '')
        blf.draw(0, 'Carrying: ' + (held.upper() if held else 'empty'))

    def stop(self, context):
        self.stopped = True
        self.clear_highlight()
        context.window_manager.event_timer_remove(self._timer)
        bpy.types.SpaceView3D.draw_handler_remove(self._draw, 'WINDOW')
        context.window.cursor_modal_restore()
        self.space.show_region_ui = self.sidebar
        context.workspace.status_text_set(None)
        bpy.app.driver_namespace.pop('echo_stop', None)
        self.area.tag_redraw()


    def invoke(self, context, event):
        if context.area.type != 'VIEW_3D':
            return {'CANCELLED'}
        if 'echo_stop' in bpy.app.driver_namespace:
            self.report({'INFO'}, 'Already playing. Esc releases the mouse.')
            return {'CANCELLED'}
        self.highlighted = None
        self.hint = ''
        self.hint_ready = False
        self.font = 0
        font_path = '/System/Library/Fonts/Hiragino Sans GB.ttc'
        if os.path.exists(font_path):
            self.font = blf.load(font_path)
        self.keys = set()
        self.yaw = context.scene.camera.rotation_euler.z
        self.pitch = context.scene.camera.rotation_euler.x - math.pi / 2
        self.last = time.monotonic()
        self.waves = []
        self.next_wave = 0
        self.target = [context.scene.get('height_' + str(i), 0) for i in range(3)]
        self._timer = context.window_manager.event_timer_add(.025, window=context.window)
        context.window_manager.modal_handler_add(self)
        context.space_data.region_3d.view_perspective = 'CAMERA'
        self.area = context.area
        self.region = next(r for r in context.area.regions if r.type == 'WINDOW')
        self.space = context.space_data
        self.sidebar = self.space.show_region_ui
        self.space.show_region_ui = False
        self.space.region_3d.view_camera_offset = (0, 0)
        self.center = (self.region.x + self.region.width // 2, self.region.y + self.region.height // 2)
        context.window.cursor_modal_set('NONE')
        context.window.cursor_warp(*self.center)
        self._draw = bpy.types.SpaceView3D.draw_handler_add(self.draw_hud, (context,), 'WINDOW', 'POST_PIXEL')
        self.stopped = False
        bpy.app.driver_namespace['echo_stop'] = self
        return {'RUNNING_MODAL'}

    def modal(self, context, event):
        scene = context.scene
        if getattr(self, 'stopped', False):
            return {'CANCELLED'}
        if event.type == 'ESC' or scene.name != SCENE:
            self.stop(context)
            return {'CANCELLED'}
        if event.type == 'MOUSEMOVE':
            dx = event.mouse_x - self.center[0]
            dy = event.mouse_y - self.center[1]
            self.yaw -= dx * .0025
            self.pitch = max(-1.35, min(1.35, self.pitch + dy * .0025))
            context.window.cursor_warp(*self.center)
            scene.camera.rotation_euler = (math.pi / 2 + self.pitch, 0, self.yaw)
        if event.type == 'WINDOW_DEACTIVATE':
            self.stop(context)
            return {'CANCELLED'}
        if event.value == 'PRESS' and not event.is_repeat:
            self.keys.add(event.type)
            if event.type == 'E':
                self.clear_highlight()
                interact(context)
            if event.type == 'R':
                self.clear_highlight()
                bpy.ops.echo.reset()
                self.waves = []
                self.target = [0, 0, 0]
                self.yaw = self.pitch = 0
            if event.type == 'LEFTMOUSE' and not scene.get('fired'):
                self.clear_highlight()
                obj = aim(context)
                if scene.get('held'):
                    scene['message'] = 'Place the carried crystal before shooting.'
                elif obj and obj.get('echo_crystal') == 'red':
                    scene['fired'] = True
                    self.waves = solve(scene.echo_green, scene.echo_null)
                    self.next_wave = time.monotonic()
                    scene['message'] = 'Red crystal hit. Chain starting.'
                else:
                    scene['message'] = 'Miss. Aim at the fixed RED crystal.'
        elif event.value == 'RELEASE':
            self.keys.discard(event.type)
        if event.type == 'TIMER':
            now = time.monotonic()
            dt = min(now - self.last, .06)
            self.last = now
            if self.waves and now >= self.next_wave:
                self.clear_highlight()
                symbol, affected, colors, self.target = self.waves.pop(0)
                scene['consumed'] += ',' + symbol
                scene['message'] = 'Pulse -> ' + symbol.upper() + ' | platforms ' + ', '.join('CBA'[i] for i in affected)
                for i, color in enumerate(colors):
                    bpy.data.objects['Echo platform ' + str(i)].data.materials[0] = bpy.data.materials['Echo ' + color]
                self.next_wave = now + 1.5
            for i in range(3):
                current = scene.get('height_' + str(i), 0)
                current += min(max(self.target[i] - current, 0), dt * 1.5)
                scene['height_' + str(i)] = current
                bpy.data.objects['Echo platform ' + str(i)].location.z = current - .22
            self.yaw += (('LEFT_ARROW' in self.keys) - ('RIGHT_ARROW' in self.keys)) * dt * 1.6
            self.pitch = max(-1.1, min(1.1, self.pitch + (('UP_ARROW' in self.keys) - ('DOWN_ARROW' in self.keys)) * dt))
            camera = scene.camera
            camera.rotation_euler = (math.pi / 2 + self.pitch, 0, self.yaw)
            forward = Vector((-math.sin(self.yaw), math.cos(self.yaw), 0))
            right = Vector((math.cos(self.yaw), math.sin(self.yaw), 0))
            movement = forward * (('W' in self.keys) - ('S' in self.keys)) + right * (('D' in self.keys) - ('A' in self.keys))
            if movement.length:
                movement.normalize()
            candidate = camera.location + movement * dt * 2.8
            surfaces = [(-2.5, 2.5, -4, 0, 0), (-1.7, 1.7, 9, 13, 4)]
            surfaces += [(-1.4, 1.4, i*3+.1, i*3+2.9, scene.get('height_' + str(i), 0)) for i in range(3)]
            ground = next((h for x0, x1, y0, y1, h in surfaces if x0 <= candidate.x <= x1 and y0-.12 <= candidate.y <= y1+.12), None)
            feet = camera.location.z - 1.65
            if ground is None:
                camera.location = (0, -3, 1.65)
                scene['message'] = 'Returned to entrance. Walk along the center of the platforms.'
            elif ground <= feet + 1.08:
                candidate.z = ground + 1.65
                camera.location = candidate
            set_symbol_positions()
            if camera.location.y > 10 and camera.location.z >= 5.6:
                scene['message'] = 'COMPLETE! You built the three-step staircase. R to replay.'
            context.workspace.status_text_set('WASD move | Mouse look | E pick/place | Left click shoot | R reset | Esc stop')
            self.update_highlight(context)
            self.area.tag_redraw()
        return {'RUNNING_MODAL'}


class ECHO_PT_panel(bpy.types.Panel):
    bl_label = 'Three Echo / 三级回声'
    bl_idname = 'ECHO_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Three Echo'

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        if scene.name != SCENE:
            layout.label(text='Select the Three Echo scene')
            return
        layout.label(text='启动：点击下方 Play')
        layout.operator('echo.play')
        layout.label(text='WASD / Mouse look / E pick-place')
        layout.label(text='Left click shoot / R reset / Esc')
        layout.operator('echo.reset')
        layout.separator()
        layout.label(text='Each color change lifts +1m')
        layout.label(text='Goal: C=1 / B=2 / A=3 / Exit=4')
        for i in range(3):
            layout.label(text='CBA'[i] + ': ' + str(round(scene.get('height_' + str(i), 0), 1)) + 'm')
        # Short lines fit Blender's sidebar.
        message = scene.get('message', '')
        for start in range(0, len(message), 34):
            layout.label(text=message[start:start+34])


CLASSES = [ECHO_OT_reset, ECHO_OT_play, ECHO_PT_panel]
active = bpy.app.driver_namespace.get('echo_stop')
if active:
    active.stop(bpy.context)
    active.stopped = True
prior_arrange = getattr(bpy.types, 'ECHO_OT_arrange', None)
if prior_arrange:
    bpy.utils.unregister_class(prior_arrange)
for cls in CLASSES:
    prior = getattr(bpy.types, cls.__name__, None)
    if prior:
        bpy.utils.unregister_class(prior)
    bpy.utils.register_class(cls)
bpy.types.Scene.echo_green = bpy.props.BoolProperty(default=False)
bpy.types.Scene.echo_null = bpy.props.BoolProperty(default=False)
build()
print('Three Echo ready. N sidebar > Three Echo > Play. Mouse look, E pick/place, left click shoot, WASD, R reset, Esc exit.')
