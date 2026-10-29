import bpy, sys, math
from mathutils import Vector, Matrix

source, output = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
for obj in list(bpy.data.objects):
    if obj.type == 'MESH' and obj.name not in ['Face', 'Body', 'Hair001']:
        bpy.data.objects.remove(obj, do_unlink=True)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for side, angle in [('L', -70), ('R', 70)]:
    bone = rig.pose.bones['J_Bip_' + side + '_UpperArm']
    world = rig.matrix_world @ bone.matrix
    pivot = world.translation.copy()
    bone.matrix = rig.matrix_world.inverted() @ Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(angle), 4, 'Y') @ Matrix.Translation(-pivot) @ world
    bpy.context.view_layer.update()
world = bpy.data.worlds.new('Preview world')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (.75,.82,.81,1)
world.node_tree.nodes['Background'].inputs[1].default_value = .75
bpy.context.scene.world = world
def light(name, position, energy, size):
    d = bpy.data.lights.new(name, 'AREA'); d.energy=energy; d.shape='DISK'; d.size=size
    o = bpy.data.objects.new(name, d); bpy.context.collection.objects.link(o); o.location=position
    o.rotation_euler=(Vector((0,0,.9))-o.location).to_track_quat('-Z','Y').to_euler()
light('Key',(-2,3,4),280,4)
light('Fill',(2,2,2),100,3)
bpy.ops.object.camera_add(location=(0,3.5,.86))
camera=bpy.context.object; camera.rotation_euler=(Vector((0,0,.8))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO'; camera.data.ortho_scale=1.8; bpy.context.scene.camera=camera
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.render.resolution_x=720; scene.render.resolution_y=960; scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'
scene.render.filepath=output; scene.render.image_settings.file_format='PNG'
bpy.ops.render.render(write_still=True)
