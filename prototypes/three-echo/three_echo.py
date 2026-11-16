"""Run in Blender's Text Editor. N sidebar > Three Echo > Play."""
import bpy
import math
import time
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


def set_symbol_positions():
    scene = bpy.data.scenes[SCENE]
    for key, index, x in [('red', 2, -.65),
                          ('green', 2 if scene.echo_green else 1, .65),
                          ('none', 1 if scene.echo_null else -1, -.65)]:
        obj = bpy.data.objects['Echo crystal ' + key]
        if index < 0:
            obj.location = (-2.1, -2, .65)
        else:
            obj.location = (x, index * 3 + 1.5, scene.get('height_' + str(index), 0) + .65)
        obj.hide_viewport = key in scene.get('consumed', '').split(',')


def reset():
    scene = bpy.data.scenes[SCENE]
    scene['fired'] = False
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
        obj.data.materials.append(mats[key])
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


class ECHO_OT_arrange(bpy.types.Operator):
    bl_idname = 'echo.arrange'
    bl_label = 'Arrange crystal'
    which: bpy.props.StringProperty()

    def execute(self, context):
        scene = context.scene
        if scene.get('fired'):
            self.report({'INFO'}, 'Reset before changing the arrangement')
            return {'CANCELLED'}
        if self.which == 'green':
            scene.echo_green = not scene.echo_green
        else:
            scene.echo_null = not scene.echo_null
        set_symbol_positions()
        return {'FINISHED'}


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

    def invoke(self, context, event):
        if context.area.type != 'VIEW_3D':
            return {'CANCELLED'}
        self.keys = set()
        self.yaw = 0
        self.pitch = 0
        self.last = time.monotonic()
        self.waves = []
        self.next_wave = 0
        self.target = [context.scene.get('height_' + str(i), 0) for i in range(3)]
        self._timer = context.window_manager.event_timer_add(.025, window=context.window)
        context.window_manager.modal_handler_add(self)
        context.space_data.region_3d.view_perspective = 'CAMERA'
        self.area = context.area
        return {'RUNNING_MODAL'}

    def modal(self, context, event):
        scene = context.scene
        if event.type == 'ESC' or scene.name != SCENE:
            context.window_manager.event_timer_remove(self._timer)
            context.workspace.status_text_set(None)
            return {'CANCELLED'}
        if event.value == 'PRESS':
            self.keys.add(event.type)
            if event.type in {'G', 'N'}:
                bpy.ops.echo.arrange(which='green' if event.type == 'G' else 'none')
            if event.type == 'R':
                bpy.ops.echo.reset()
                self.waves = []
                self.target = [0, 0, 0]
                self.yaw = self.pitch = 0
            if event.type == 'SPACE' and not scene.get('fired'):
                scene['fired'] = True
                self.waves = solve(scene.echo_green, scene.echo_null)
                self.next_wave = time.monotonic()
        elif event.value == 'RELEASE':
            self.keys.discard(event.type)
        if event.type == 'TIMER':
            now = time.monotonic()
            dt = min(now - self.last, .06)
            self.last = now
            if self.waves and now >= self.next_wave:
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
            set_symbol_positions()
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
            if camera.location.y > 10 and camera.location.z >= 5.6:
                scene['message'] = 'COMPLETE! You built the three-step staircase. R to replay.'
            context.workspace.status_text_set('WASD move | Arrows look | G move GREEN | N move NULL | Space fire | R reset | Esc stop')
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
        layout.operator('echo.play')
        layout.label(text='WASD move / Arrows look')
        layout.label(text='Space fire / R reset / Esc stop')
        layout.separator()
        op = layout.operator('echo.arrange', text='G: Green -> ' + ('A (blue)' if scene.echo_green else 'B (red)'))
        op.which = 'green'
        op = layout.operator('echo.arrange', text='N: Null -> ' + ('B (red)' if scene.echo_null else 'Entrance'))
        op.which = 'none'
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


CLASSES = [ECHO_OT_arrange, ECHO_OT_reset, ECHO_OT_play, ECHO_PT_panel]
for cls in CLASSES:
    prior = getattr(bpy.types, cls.__name__, None)
    if prior:
        bpy.utils.unregister_class(prior)
    bpy.utils.register_class(cls)
bpy.types.Scene.echo_green = bpy.props.BoolProperty(default=False)
bpy.types.Scene.echo_null = bpy.props.BoolProperty(default=False)
build()
print('Three Echo ready. N sidebar > Three Echo > Play. G / N arrange, Space fire, WASD, arrows, R reset, Esc exit.')
