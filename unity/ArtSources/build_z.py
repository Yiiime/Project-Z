"""Create Z's editable, rigged art-slice model from the licensed VRoid A base.
Run with Blender --background --python build_z.py -- <base.glb> <output-dir>.
Original raster textures are extracted unchanged. Clothing uses the project's fabric textures.
"""
import bpy, bmesh, json, math, os, struct, sys
from mathutils import Vector, Matrix

SOURCE, OUT = sys.argv[sys.argv.index('--') + 1:]
os.makedirs(OUT, exist_ok=True)
TEX = os.path.join(OUT, 'Textures'); os.makedirs(TEX, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
raw = open(SOURCE, 'rb').read(); json_size = struct.unpack_from('<I', raw, 12)[0]
gltf = json.loads(raw[20:20+json_size]); binary_start = 28 + json_size
image_paths = {}
for index, image in enumerate(gltf['images']):
    if image.get('name') == 'Thumbnail': continue
    view = gltf['bufferViews'][image['bufferView']]
    filename = f"{index:02d}_{image.get('name', 'Texture')}.png"
    target = os.path.join(TEX, filename)
    start = binary_start + view.get('byteOffset', 0)
    open(target, 'wb').write(raw[start:start+view['byteLength']])
    image_paths.setdefault(image.get('name'), target)
bpy.ops.import_scene.gltf(filepath=SOURCE)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); rig.name = 'Z_Rig'
face, body, hair = (bpy.data.objects[n] for n in ('Face','Body','Hair001'))
for obj in list(bpy.data.objects):
    if obj.type == 'MESH' and obj not in (face, body, hair): bpy.data.objects.remove(obj, do_unlink=True)
for image in bpy.data.images:
    if image.name in image_paths:
        if image.packed_file: image.unpack(method='REMOVE')
        image.filepath = image_paths[image.name]
        image.reload()

materials = {}
def material(name, color):
    mat = bpy.data.materials.new(name); mat.diffuse_color=(*color,1); mat.use_nodes=True
    p=mat.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=.72
    materials[name]=mat
    return mat
purple=material('Z_Cardigan_Knit',(.46,.34,.56))
rib=material('Z_Cardigan_Rib',(.39,.285,.48))
navy=material('Z_Skirt_Floral',(.075,.095,.16))
white=material('Z_Cotton',(.9,.87,.80))
button=material('Z_Buttons',(.205,.14,.255))
silver=material('Z_Silver',(.68,.70,.78))
pearl=material('Z_Moon_Pearl',(.67,.55,.83))

def body_part(name, keep_material):
    obj=body.copy(); obj.data=body.data.copy(); bpy.context.collection.objects.link(obj); obj.name=name
    bm=bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index!=keep_material], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    for f in bm.faces: f.material_index=0
    bm.to_mesh(obj.data); bm.free()
    mat=body.material_slots[keep_material].material
    obj.data.materials.clear(); obj.data.materials.append(mat)
    return obj
skin=body_part('Z_Skin',0)
shoes=body_part('Z_Loafers',3)
bm=bmesh.new();bm.from_mesh(skin.data)
hidden=[]
for f in bm.faces:
    p=skin.matrix_world@f.calc_center_median()
    if .75<p.z<1.28 and abs(p.x)<.535:hidden.append(f)
bmesh.ops.delete(bm,geom=hidden,context='FACES')
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(skin.data);bm.free()
body.hide_render=True
body.hide_viewport=True
face.name='Z_Face'; hair.name='Z_LongHair'

# Preserve the complete facial rig, giving the supported expressions stable names.
names={0:'Neutral',1:'Firm',2:'Smile',3:'Joy',4:'Thoughtful',5:'Surprised',13:'Blink',14:'BlinkRight',15:'BlinkLeft',37:'MouthA',38:'MouthI',39:'MouthU',40:'MouthE',41:'MouthO'}
for index, name in names.items(): face.data.shape_keys.key_blocks[index+1].name=name

# Refine the connected original locks before sculpting the longer wavy silhouette.
bpy.context.view_layer.objects.active=hair
sub=hair.modifiers.new('Smooth hair curves','SUBSURF');sub.levels=1
bpy.ops.object.modifier_move_up(modifier=sub.name)
bpy.ops.object.modifier_apply(modifier=sub.name)
inverse=hair.matrix_world.inverted()
hair_material={}
for polygon in hair.data.polygons:
    for index in polygon.vertices:hair_material[index]=polygon.material_index
for vertex in hair.data.vertices:
    p=hair.matrix_world@vertex.co
    if hair_material.get(vertex.index)!=0 and p.z<1.43 and (abs(p.x)>.055 or p.y<.025):
        t=max(0,min(1,(1.43-p.z)/.132))
        layer=hair_material.get(vertex.index,2)
        p.z=1.43-(1.43-p.z)*(2.62 if layer==2 else 2.45)
        wave=math.sin(t*8.5+p.y*14)
        p.x+=math.copysign(.026*t,p.x)+math.copysign(1,p.x)*.024*wave*t*t
        p.y+=(.069 if p.y>.025 else -.022)*t
        p.y+=.013*math.sin(t*8+p.x*12)*t*t
        vertex.co=inverse@p
base_face=[face.matrix_world@v.co for v in face.data.shape_keys.key_blocks[0].data]
for shape in face.data.shape_keys.key_blocks:
    for index,vertex in enumerate(shape.data):
        p=face.matrix_world@vertex.co
        base=base_face[index]
        jaw=max(0,min(1,(1.390-base.z)/.065))
        eye=math.exp(-((abs(base.x)-.038)/.034)**4-((base.z-1.411)/.031)**4)
        p.x*=1+.022*jaw
        p.z=1.411+(p.z-1.411)*(1-.09*eye)+.003*jaw
        vertex.co=face.matrix_world.inverted()@p

generated=[]
def mesh_object(name, verts, faces, uv, mat):
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts, [], faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    layer=mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        poly.use_smooth=True
        for loop in poly.loop_indices: layer.data[loop].uv=uv[mesh.loops[loop].vertex_index]
    generated.append(obj)
    return obj

def bind(obj, bone_name=None, weight_source=None):
    if bone_name:
        obj.vertex_groups.new(name=bone_name).add(list(range(len(obj.data.vertices))),1,'REPLACE')
    else:
        donor=weight_source or body
        for group in donor.vertex_groups: obj.vertex_groups.new(name=group.name)
        transfer=obj.modifiers.new('Transfer original skin weights','DATA_TRANSFER')
        transfer.object=donor; transfer.use_vert_data=True; transfer.data_types_verts={'VGROUP_WEIGHTS'}
        transfer.vert_mapping='POLYINTERP_NEAREST'
        bpy.context.view_layer.objects.active=obj; obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=transfer.name); obj.select_set(False)
    modifier=obj.modifiers.new('Z skeleton','ARMATURE'); modifier.object=rig
    world=obj.matrix_world.copy(); obj.parent=rig; obj.matrix_world=world
    return obj

def garment(name, rings, mat, segments=96, opening=None, pleats=0):
    verts=[]; uv=[]; faces=[]
    for row,(z,rx,ry,cy) in enumerate(rings):
        gap=opening(z) if opening else 0
        for col in range(segments+1):
            t=col/segments; angle=gap+(2*math.pi-2*gap)*t
            r=1+pleats*math.sin(angle*20)*(1-row/max(1,len(rings)-1))
            ripple=.0018*math.sin(angle*11+z*29)
            verts.append((math.sin(angle)*(rx*r+ripple),cy+math.cos(angle)*(ry*r+ripple),z))
            uv.append((t,row/(len(rings)-1)))
    for row in range(len(rings)-1):
        for col in range(segments):
            a=row*(segments+1)+col; b=a+segments+1
            faces.append((a,b,b+1,a+1))
    obj=mesh_object(name,verts,faces,uv,mat)
    solid=obj.modifiers.new('Cloth thickness','SOLIDIFY'); solid.thickness=.003
    bpy.context.view_layer.objects.active=obj; obj.select_set(True); bpy.ops.object.modifier_apply(modifier=solid.name); obj.select_set(False)
    return obj

# Retain the donor's coherent shoulder loops and deformation weights, remove the
# hood, cords and pocket pieces, then tailor the body into a cropped cardigan.
cardigan=body_part('Z_Cardigan',1);cardigan.hide_render=False;cardigan.hide_viewport=False
bm=bmesh.new();bm.from_mesh(cardigan.data)
remaining=set(bm.verts);discard=[]
while remaining:
    seed=remaining.pop();component={seed};queue=[seed]
    while queue:
        for edge in queue.pop().link_edges:
            for vertex in edge.verts:
                if vertex in remaining:remaining.remove(vertex);component.add(vertex);queue.append(vertex)
    if len(component)<300:discard.extend(component)
bmesh.ops.delete(bm,geom=discard,context='VERTS')
for v in bm.verts:
    p=cardigan.matrix_world@v.co
    if abs(p.x)<.20 and p.z<1.24:
        p.z=.866+(p.z-.7895)*.83
        p.x*=.95
        if p.y>.025 and p.z<1.12:
            factor=max(0,min(1,(p.y-.025)/.065))
            target=math.sqrt(max(0,1-(p.x/.185)**2))*.124
            p.y=p.y*(1-factor)+target*factor
    v.co=cardigan.matrix_world.inverted()@p
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0003)
bm.to_mesh(cardigan.data);bm.free()
cardigan.data.materials.clear();cardigan.data.materials.append(purple);cardigan.data.materials.append(rib)
for poly in cardigan.data.polygons:
    p=cardigan.matrix_world@poly.center
    poly.material_index=0
    poly.use_smooth=True
bpy.context.view_layer.objects.active=cardigan
sub=cardigan.modifiers.new('Tailored smooth cloth','SUBSURF');sub.levels=2
bpy.ops.object.modifier_move_up(modifier=sub.name)
bpy.ops.object.modifier_apply(modifier=sub.name)
generated.append(cardigan)
bind(garment('Z_White_Undershirt',[(.90,.116,.062,.003),(1.19,.123,.064,0),(1.26,.066,.039,-.006),(1.282,.045,.038,-.008)],white))
bind(garment('Z_Crew_Collar',[(1.278,.047,.040,-.008),(1.285,.045,.038,-.008)],white))

# A fitted facing follows the actual neck opening instead of floating above it.
bm=bmesh.new();bm.from_mesh(cardigan.data)
edges=[e for e in bm.edges if e.is_boundary and all((cardigan.matrix_world@v.co).z>1.20 and abs((cardigan.matrix_world@v.co).x)<.115 for v in e.verts)]
verts=[];quads=[];uv=[]
for edge in edges:
    p,q=[cardigan.matrix_world@v.co for v in edge.verts]
    n=len(verts)
    for co,outer in [(p,False),(q,False),(q,True),(p,True)]:
        v=co+Vector((co.x*.09 if outer else 0,co.y*.075 if outer else .001,-.012 if outer else .001))
        verts.append(tuple(v));uv.append((co.x*8,1 if outer else 0))
    quads.append((n,n+1,n+2,n+3))
bm.free()
if quads:bind(mesh_object('Z_Neck_Facing',verts,quads,uv,rib),weight_source=cardigan)

skirt=[]
for row in range(29):
    t=row/28; skirt.append((.245+.655*t,.274-(.274-.137)*t**.8,.204-(.204-.106)*t**.8,-.004))
skirt_obj=garment('Z_Long_Skirt',skirt,navy,segments=128,pleats=.038)
bind(skirt_obj,'J_Bip_C_Hips')

# Extra skirt bones stay outside the Humanoid map and carry restrained secondary motion.
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
skirt_directions=[('Front',0),('Left',math.pi/2),('Back',math.pi),('Right',math.pi*1.5)]
for name,angle in skirt_directions:
    bone=rig.data.edit_bones.new('Z_Skirt_'+name)
    bone.head=rig.matrix_world.inverted()@Vector((math.sin(angle)*.08,math.cos(angle)*.06,.865))
    bone.tail=rig.matrix_world.inverted()@Vector((math.sin(angle)*.16,math.cos(angle)*.12,.40))
    bone.parent=rig.data.edit_bones['J_Bip_C_Hips']
bpy.ops.object.mode_set(mode='OBJECT')
groups=[skirt_obj.vertex_groups.new(name='Z_Skirt_'+name) for name,angle in skirt_directions]
for vertex in skirt_obj.data.vertices:
    p=vertex.co;theta=math.atan2(p.x,p.y)
    weight=max(0,min(.78,(.86-p.z)/.61*.78))
    raw_weights=[max(0,math.cos(theta-angle))**4 for name,angle in skirt_directions]
    total=sum(raw_weights)
    skirt_obj.vertex_groups['J_Bip_C_Hips'].add([vertex.index],1-weight,'REPLACE')
    for group,value in zip(groups,raw_weights):
        if value>0:group.add([vertex.index],weight*value/total,'REPLACE')

def uv_sphere(name, loc, scale, mat, bone):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,location=loc)
    obj=bpy.context.object;obj.name=name;obj.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    obj.data.materials.append(mat)
    for p in obj.data.polygons:p.use_smooth=True
    generated.append(obj);bind(obj,bone,weight_source=cardigan if bone is None else None);return obj
for z in (.89,.968,1.046,1.124,1.202):
    uv_sphere('Z_Cardigan_Button',(0,.130,z),(.006,.003,.006),button,None)

bare_leg=material('Z_BareLeg',(.91,.69,.59));skin.data.materials.append(bare_leg)
for poly in skin.data.polygons:
    if (skin.matrix_world@poly.center).z<.6:poly.material_index=1

for side in (-1,1):
    sock=garment('Z_White_Sock_'+str(side),[(.066,.026,.038,-.024),(.095,.029,.030,-.022),(.18,.031,.030,-.014),(.218,.032,.031,-.012)],white,segments=40)
    for vertex in sock.data.vertices:vertex.co.x+=side*.041
    bind(sock,'J_Bip_'+('R' if side>0 else 'L')+'_LowerLeg')

def curve_tube(name, points, radius, mat, bone=None):
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=16
    spline=data.splines.new('POLY');spline.points.add(len(points)-1)
    for p,co in zip(spline.points,points):p.co=(*co,1)
    data.bevel_depth=radius;data.bevel_resolution=3
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);obj.data.materials.append(mat)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.convert(target='MESH');obj.select_set(False)
    generated.append(obj)
    if bone:bind(obj,bone)
    return obj

# A raised button band gives the front opening a readable construction at medium distance.
bind(curve_tube('Z_Button_Band',[(0,.126,z) for z in (.875,.94,1.0,1.06,1.12,1.18,1.23)],.005,rib),weight_source=cardigan)

def crescent(name, center, radius, material_main, bone, tilt=.40):
    inner=radius*.80; distance=radius*.44
    x=(radius*radius-inner*inner+distance*distance)/(2*distance)
    y=math.sqrt(radius*radius-x*x); outer_a=math.atan2(y,x); inner_a=math.atan2(y,x-distance)
    points=[]
    for i in range(49):
        a=outer_a+(2*math.pi-2*outer_a)*i/48;points.append((radius*math.cos(a),radius*math.sin(a)))
    for i in range(49):
        a=2*math.pi-inner_a-(2*math.pi-2*inner_a)*i/48;points.append((distance+inner*math.cos(a),inner*math.sin(a)))
    verts=[]
    for depth in (-.0015,.0015):
        for u,v in points:
            rx=u*math.cos(tilt)-v*math.sin(tilt);rz=u*math.sin(tilt)+v*math.cos(tilt)
            verts.append((center[0]-rx,center[1]+depth,center[2]+rz))
    n=len(points);faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    obj=mesh_object(name,verts,faces,[(0,0)]*len(verts),material_main);bind(obj,bone)
    outline=[tuple(verts[n+i]) for i in range(n)]+[tuple(verts[n])]
    curve_tube(name+'_Silver_Edge',outline,radius*.035,silver,bone)

crescent('Z_Moon_Hairpin',(-.084,.088,1.494),.029,pearl,'J_Bip_C_Head')
chain=[]
for i in range(81):
    a=i/80*math.pi*2
    chain.append((.047*math.sin(a),-.004+.105*math.cos(a),1.268-.040*(.5+.5*math.cos(a))))
curve_tube('Z_Necklace_Chain',chain,.0008,silver,'J_Bip_C_UpperChest')
crescent('Z_Necklace_Moon',(0,.112,1.220),.007,silver,'J_Bip_C_UpperChest',.35)

# Only the character and its rig are exported. The .blend remains fully editable.
def textile_preview(mat, filename, tiling, tint):
    path=os.path.abspath(os.path.join(os.path.dirname(__file__),'../Assets/ProjectZ/Art/Characters/Z/Textures',filename))
    if not os.path.isfile(path):return
    nodes=mat.node_tree.nodes;links=mat.node_tree.links
    coordinates=nodes.new('ShaderNodeTexCoord')
    scale=nodes.new('ShaderNodeVectorMath');scale.operation='MULTIPLY';scale.inputs[1].default_value=(*tiling,1)
    texture=nodes.new('ShaderNodeTexImage');texture.image=bpy.data.images.load(path,check_existing=True)
    multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
    multiply.inputs[2].default_value=(*tint,1)
    links.new(coordinates.outputs['UV'],scale.inputs[0]);links.new(scale.outputs[0],texture.inputs['Vector'])
    links.new(texture.outputs['Color'],multiply.inputs[1])
    links.new(multiply.outputs[0],nodes['Principled BSDF'].inputs['Base Color'])
textile_preview(purple,'Z_Knit_v2.jpg',(3.5,3),(.46,.34,.56))
textile_preview(navy,'Z_Floral_v2.jpg',(7,4),(1,1,1))
keep=[rig,face,hair,skin,shoes]+generated
bpy.data.objects.remove(body,do_unlink=True)
for obj in bpy.context.scene.objects: obj.select_set(obj in keep)
bpy.context.view_layer.objects.active=rig
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'Z_ArtSlice.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Z_ArtSlice.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
    add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')

material_info=[]
for mat in bpy.data.materials:
    tex=None
    if mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image and 'nml' not in node.image.name.lower():
                tex=os.path.basename(node.image.filepath);break
    material_info.append({'name':mat.name,'color':list(mat.diffuse_color),'texture':tex})
json.dump({'materials':material_info,'humanBones':gltf['extensions']['VRM']['humanoid']['humanBones'],
    'nodes':[n.get('name','') for n in gltf['nodes']]},open(os.path.join(OUT,'Z_metadata.json'),'w'),indent=2)
print('Z_MODEL_EXPORTED',OUT)
