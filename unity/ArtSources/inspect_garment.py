import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
source, output = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
body=bpy.data.objects['Body']
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.material_index!=1],context='FACES')
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(body.data);bm.free()
adj={v.index:set() for v in body.data.vertices}
for edge in body.data.edges:
    a,b=edge.vertices;adj[a].add(b);adj[b].add(a)
remaining=set(adj)
while remaining:
    seed=remaining.pop();component={seed};queue=[seed]
    while queue:
        for n in adj[queue.pop()]&remaining:
            remaining.remove(n);component.add(n);queue.append(n)
    pts=[body.matrix_world@body.data.vertices[i].co for i in component]
    groups={body.vertex_groups[g.group].name for i in component for g in body.data.vertices[i].groups if g.weight>.05}
    print('CLOTH_COMPONENT',len(component),[round(min(p[j] for p in pts),3) for j in range(3)],[round(max(p[j] for p in pts),3) for j in range(3)],sorted(groups))
for obj in list(bpy.data.objects):
    if obj.type=='MESH' and obj!=body:bpy.data.objects.remove(obj,do_unlink=True)
m=bpy.data.materials.new('test');m.use_nodes=True
m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.45,.32,.52,1)
body.data.materials.clear();body.data.materials.append(m)
for f in body.data.polygons:f.material_index=0
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
for side,angle in [('L',-73),('R',73)]:
    b=rig.pose.bones['J_Bip_'+side+'_UpperArm'];w=rig.matrix_world@b.matrix;p=w.translation.copy()
    b.matrix=rig.matrix_world.inverted()@Matrix.Translation(p)@Matrix.Rotation(math.radians(angle),4,'Y')@Matrix.Translation(-p)@w
    bpy.context.view_layer.update()
scene=bpy.context.scene;world=bpy.data.worlds.new('World');world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.7,.74,.77,1);scene.world=world
bpy.ops.object.light_add(type='AREA',location=(-2,3,4));l=bpy.context.object;l.data.energy=220;l.data.size=4
l.rotation_euler=(Vector((0,0,1.1))-l.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(.25,3,1.1));cam=bpy.context.object
cam.rotation_euler=(Vector((0,0,1.1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=.85
scene.camera=cam;scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.resolution_x=850;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard';scene.render.filepath=output;bpy.ops.render.render(write_still=True)
