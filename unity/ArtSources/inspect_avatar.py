import bpy, sys, json
from mathutils import Vector

source, output = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
report = {'meshes': [], 'bones': []}
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        points = [obj.matrix_world @ v.co for v in obj.data.vertices]
        row = {'name': obj.name, 'vertices': len(points), 'materials': [],
               'bounds': [[min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]],
               'shapes': list(obj.data.shape_keys.key_blocks.keys()) if obj.data.shape_keys else []}
        for index, slot in enumerate(obj.material_slots):
            ids = {v for poly in obj.data.polygons if poly.material_index == index for v in poly.vertices}
            subset = [obj.matrix_world @ obj.data.vertices[v].co for v in ids]
            row['materials'].append({'name': slot.name, 'index': index, 'vertices': len(ids),
                'bounds': [[min(p[i] for p in subset) for i in range(3)], [max(p[i] for p in subset) for i in range(3)]] if subset else []})
        report['meshes'].append(row)
    elif obj.type == 'ARMATURE':
        for bone in obj.data.bones:
            if any(key in bone.name for key in ['Hips', 'Spine', 'Chest', 'Neck', 'Head', 'UpperArm', 'LowerArm', 'Hand', 'UpperLeg', 'LowerLeg', 'Foot']):
                report['bones'].append({'name': bone.name, 'head': list(obj.matrix_world @ bone.head_local), 'tail': list(obj.matrix_world @ bone.tail_local)})
with open(output, 'w') as f: json.dump(report, f, indent=2)
print('AVATAR_INSPECTED', output)
