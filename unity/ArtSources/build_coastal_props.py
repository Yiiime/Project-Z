"""Editable coastal props. Meters, beveled edges, separate material slots."""
import bpy, math, os, sys
from mathutils import Vector
OUT=sys.argv[sys.argv.index('--')+1];os.makedirs(OUT,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
def mat(name,color):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);return m
wood=mat('Coast_WarmWood',(.45,.29,.19));edge=mat('Coast_PaleWood',(.69,.51,.32))
metal=mat('Coast_Iron',(.17,.27,.27));plaster=mat('Coast_Plaster',(.88,.82,.66))
roof=mat('Coast_Slate',(.27,.43,.50));glass=mat('Coast_Glass',(.40,.65,.68))
paper=mat('Coast_Paper',(.88,.85,.68));ink=mat('Coast_MapInk',(.32,.52,.47))
def box(name,loc,scale,material,bevel=.025,rotation=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if rotation:o.rotation_euler=rotation
    o.data.materials.append(material)
    if bevel:
        b=o.modifiers.new('Soft edges','BEVEL');b.width=bevel;b.segments=3
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=b.name)
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o
def rod(name,a,b,r,material):
    delta=Vector(b)-Vector(a);bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=delta.length,location=(Vector(a)+Vector(b))/2)
    o=bpy.context.object;o.name=name;o.rotation_euler=delta.to_track_quat('Z','Y').to_euler();o.data.materials.append(material)
    return o
def export(name):
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]
    bpy.ops.object.join();o=bpy.context.object;o.name=name
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,name+'.blend'))
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,name+'.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')
    bpy.data.objects.remove(o,do_unlink=True)

for y in (-.23,-.077,.077,.23):box('Seat slat',(0,y,.50),(2.25,.135,.07),wood)
for z in (.77,.97):box('Back slat',(0,-.31,z),(2.25,.065,.17),wood,rotation=(math.radians(-8),0,0))
for x in (-.83,.83):
    box('Leg',(x,0,.23),(.075,.63,.46),metal)
    box('Back upright',(x,-.31,.69),(.065,.065,.66),metal)
    box('Armrest',(x,0,.75),(.12,.75,.07),edge)
    box('Arm support',(x,.20,.60),(.045,.045,.30),metal)
    for z in (.77,.97):rod('Bolt',(x,-.268,z),(x,-.25,z),.018,metal)
export('CoveBench')

for x in (-.62,.62):
    for y in (-.40,.40):box('Table leg',(x,y,.45),(.11,.11,.90),wood)
box('Table frame',(0,0,.77),(1.65,1.10,.14),wood)
for y in (-.44,-.22,0,.22,.44):box('Table plank',(0,y,.91),(1.8,.20,.10),edge)
box('Map paper',(0,0,.97),(1.35,.81,.012),paper,.006)
points=[(-.36,-.23),(-.50,-.04),(-.41,.20),(-.17,.28),(.25,.21),(.44,.02),(.31,-.21),(.06,-.27),(-.36,-.23)]
for a,b in zip(points,points[1:]):rod('Island ink',(*a,.980),(*b,.980),.005,ink)
for x,y in ((-.15,.05),(.2,-.11)):
    for a in (0,math.pi/2):
        rod('Map cross',(x-.023*math.cos(a),y-.023*math.sin(a),.981),(x+.023*math.cos(a),y+.023*math.sin(a),.981),.004,ink)
rod('Rolled edge',(-.69,-.44,.989),(-.69,.44,.989),.022,paper)
export('MapTable')

box('Stone foundation',(0,0,.14),(4.5,3.85,.28),edge,.06)
box('Stucco walls',(0,0,1.55),(4,3.45,2.85),plaster,.045)
verts=[(-2,-1.725,2.97),(2,-1.725,2.97),(0,-1.725,4),(-2,1.725,2.97),(2,1.725,2.97),(0,1.725,4)]
mesh=bpy.data.meshes.new('Gables');mesh.from_pydata(verts,[],[(0,1,2),(5,4,3),(0,2,5,3),(2,1,4,5)]);mesh.materials.append(plaster)
obj=bpy.data.objects.new('Gables',mesh);bpy.context.collection.objects.link(obj)
for side in (-1,1):
    angle=side*math.atan2(1.1,2.2)
    box('Roof',(side*1.1,0,3.47),(2.54,4.15,.13),roof,.035,rotation=(0,angle,0))
    for row in range(5):
        x=side*(.24+.47*row);z=4.02-abs(x)*.5
        box('Slate seam',(x,0,z),( .055,4.14,.025),edge,.008,rotation=(0,angle,0))
box('Ridge',(0,0,4.06),(.16,4.24,.15),roof,.06)
for x in (-1.93,1.93):box('Corner beam',(x,-1.76,1.55),(.13,.10,2.82),edge)
box('Door recess',(.7,-1.755,1.15),(1.02,.06,2.06),metal)
for x in (.31,.47,.63,.79,.95,1.11):box('Door boards',(x,-1.80,1.13),(.148,.06,1.96),wood,.012)
rod('Handle',(.98,-1.85,1.1),(.98,-1.90,1.1),.038,metal)
for y,z,w in ((-1.97,.20,1.3),(-2.24,.10,1.6)):box('Doorstep',(.7,y,z),(w,.58,.19),edge,.045)
for x in (-1.08,):
    box('Window trim',(x,-1.79,1.86),(1.12,.12,1.15),edge)
    box('Window glass',(x,-1.86,1.86),(.97,.025,.98),glass,.005)
    box('Window vertical',(x,-1.89,1.86),(.065,.04,1.0),plaster,.008)
    box('Window horizontal',(x,-1.89,1.86),(1.0,.04,.065),plaster,.008)
    box('Sill',(x,-1.91,1.24),(1.3,.32,.09),plaster)
    for side in (-1,1):
        for z in (1.50,1.69,1.88,2.07,2.26):box('Shutter',(x+side*.77,-1.80,z),(.35,.08,.16),roof,.015)
box('Chimney',(1.15,.80,3.65),(.47,.53,1.15),plaster)
box('Chimney cap',(1.15,.80,4.26),(.60,.65,.13),edge)
export('IslandCottage')
