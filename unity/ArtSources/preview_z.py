"""Render the actual editable model with a relaxed pose; no source texture edits."""
import bpy, sys, math
from mathutils import Vector, Matrix
source, output = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=source)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
for side,angle in [('L',-73),('R',73)]:
    bone=rig.pose.bones['J_Bip_'+side+'_UpperArm']; world=rig.matrix_world@bone.matrix; pivot=world.translation.copy()
    bone.matrix=rig.matrix_world.inverted()@Matrix.Translation(pivot)@Matrix.Rotation(math.radians(angle),4,'Y')@Matrix.Translation(-pivot)@world
    bpy.context.view_layer.update()
for mat in bpy.data.materials:
    if 'Hair' in mat.name and mat.use_nodes:
        p=mat.node_tree.nodes.get('Principled BSDF')
        if p:
            for link in list(p.inputs['Base Color'].links):mat.node_tree.links.remove(link)
            p.inputs['Base Color'].default_value=(.027,.025,.038,1)
            p.inputs['Roughness'].default_value=.78
world=bpy.data.worlds.new('Preview world');world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.8,.84,.84,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.7;bpy.context.scene.world=world
for name,loc,energy,size in [('Key',(-2,3,4),220,4),('Fill',(2,2,2),80,3)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);obj.location=loc
    obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(.25,3.5,.95));cam=bpy.context.object
cam.rotation_euler=(Vector((0,0,.8))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=1.8;bpy.context.scene.camera=cam
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=840;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard';scene.render.filepath=output
bpy.ops.render.render(write_still=True)
