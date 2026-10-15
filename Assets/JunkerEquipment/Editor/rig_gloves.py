import bpy, math, os, json, numpy as np
from mathutils import Vector, Matrix
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../../..'))
OUT=ROOT+'/Assets/JunkerEquipment/Hands'
os.makedirs(OUT,exist_ok=True)
# Anatomical landmarks in the supplied left glove's Blender world space.
# Each path runs MCP -> PIP -> DIP -> fingertip, with thumb CMC/MCP/IP.
paths={
 'Index':[(.27,.06,.19),(.32,.045,.49),(.36,.03,.72),(.37,.02,.86)],
 'Middle':[(.015,.065,.20),(.035,.05,.53),(.04,.025,.77),(.025,.02,.955)],
 'Ring':[(-.24,.06,.17),(-.28,.04,.48),(-.31,.025,.67),(-.31,.02,.84)],
 'Little':[(-.47,.045,.09),(-.53,.025,.32),(-.58,.02,.48),(-.62,.02,.61)],
 'Thumb':[(.30,.08,-.46),(.47,.12,-.20),(.62,.12,.035),(.70,.10,.18)]}
def closest(p,a,b):
    t=max(0,min(1,(p-a).dot(b-a)/(b-a).length_squared))
    return (p-(a+(b-a)*t)).length,t
def smooth(a,b,v):
    t=max(0,min(1,(v-a)/(b-a)));return t*t*(3-2*t)
def surface_digit(x,z):
    # Mirrored anatomical regions of the actual glove surface. The thumb tip
    # rises above the index knuckle, so it MUST be classified before fingers.
    if x>.34 and z<.34:return 'Thumb'
    if z>.08:
        if x<-.415:return 'Little'
        if x<-.205:return 'Ring'
        if x<.10:return 'Middle'
        return 'Index'
    return None
reports={}
for side in ['Left','Right']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=ROOT+'/Assets/MyAssets/mesh/Armor/Gloves/Junker'+side+'Hand.fbx')
    mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    # Bake the import/mirror transform. Mesh is now measured in metres and centered at the wrist.
    matrix=mesh.matrix_world.copy()
    for v in mesh.data.vertices:v.co=matrix@v.co
    mesh.parent=None
    mesh.matrix_parent_inverse=Matrix.Identity(4)
    mesh.delta_location=(0,0,0);mesh.delta_rotation_euler=(0,0,0);mesh.delta_scale=(1,1,1)
    mesh.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    print('RAW',side,[(min(v.co[i] for v in mesh.data.vertices),max(v.co[i] for v in mesh.data.vertices)) for i in range(3)])
    sign=1 if side=='Left' else -1
    # Some FBX negative-scale exports are converted back to the same handedness
    # by the importer. Determine the thumb side from the actual baked vertices.
    thumb_tip=max((v.co for v in mesh.data.vertices if -.2<v.co.z<.3), key=lambda p:abs(p.x))
    imported_sign=1 if thumb_tip.x>0 else -1
    if imported_sign!=sign:
        for v in mesh.data.vertices:v.co.x=-v.co.x
        import bmesh
        bm=bmesh.new();bm.from_mesh(mesh.data);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(mesh.data);bm.free()
    # The right source is a mirrored copy: inspect original bounds preserved by this bake.
    wrist=Vector((0,.07,-.62)); scale=.125
    for v in mesh.data.vertices:v.co=(v.co-wrist)*scale
    import bmesh
    bm=bmesh.new();bm.from_mesh(mesh.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000005)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh.data);bm.free()
    if mesh.data.has_custom_normals:
        bpy.context.view_layer.objects.active=mesh
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    mesh.name='Junker'+side+'Glove'
    armdata=bpy.data.armatures.new(side+'GloveSkeleton');arm=bpy.data.objects.new(side+'GloveRig',armdata);bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active=arm;arm.select_set(True);mesh.select_set(False);bpy.ops.object.mode_set(mode='EDIT')
    root=armdata.edit_bones.new('Wrist');root.head=(0,0,0);root.tail=(0,0,.08)
    digitpaths={}
    for finger,pts in paths.items():
        ps=[(Vector((p[0]*sign,p[1],p[2]))-wrist)*scale for p in pts];digitpaths[finger]=ps
        parent=root
        for i in range(3):
            bone=armdata.edit_bones.new(finger+str(i+1));bone.head=ps[i];bone.tail=ps[i+1];bone.parent=parent;bone.use_connect=i>0;parent=bone
    bpy.ops.object.mode_set(mode='OBJECT')
    groups={bone.name:mesh.vertex_groups.new(name=bone.name) for bone in armdata.bones}
    owners=[];protected=[]
    for v in mesh.data.vertices:
        p=v.co
        rawx=p.x*sign/scale;rawz=p.z/scale-.62
        owner=surface_digit(rawx,rawz)
        nearest=[]
        for finger,ps in digitpaths.items():
            if owner and finger!=owner:continue
            for i in range(3):
                d,t=closest(p,ps[i],ps[i+1]);nearest.append((d,finger,i,t))
        _,finger,i,t=min(nearest)
        ps=digitpaths[finger]
        if finger=='Thumb':
            # The inner thumb is narrower in X than its bone centerline. Blend
            # by progress along the thumb as well, instead of pinning it to Wrist.
            blend=max(smooth(.22,.43,rawx),smooth(.45,.95,i+t)*smooth(.26,.36,rawx))
        else:
            blend=smooth(.035,.25,rawz)
        owners.append(finger)
        protected.append(smooth(.30,.38,rawx)*smooth(-.34,-.12,rawz) if owner=='Thumb' else
                         smooth(.12,.25,rawz) if owner else 0)
        # Blend at anatomical joints, never between adjacent fingers.
        u=i+t
        lo=max(0,min(2,math.floor(u-.5)));hi=min(2,lo+1)
        w=smooth(lo+.65,lo+1.35,u) if hi!=lo else 0
        weights={'Wrist':1-blend,finger+str(lo+1):blend*(1-w)}
        if w>0: weights[finger+str(hi+1)]=blend*w
        for name,value in weights.items():
            if value>0:groups[name].add([v.index],value,'REPLACE')
    modifier=mesh.modifiers.new('GloveSkin','ARMATURE');modifier.object=arm;mesh.parent=arm
    # Smooth skin weights along the actual surface, including the finger webs.
    # Heat weights fail on this scanned mesh, so avoid discontinuous nearest-digit seams.
    names=list(groups);weights=np.zeros((len(mesh.data.vertices),len(names)),dtype=np.float32)
    for v in mesh.data.vertices:
        for group in v.groups: weights[v.index,group.group]=group.weight
    anatomical_weights=weights.copy()
    edges=np.array([e.vertices[:] for e in mesh.data.edges],dtype=np.int32)
    counts=np.zeros(len(weights),dtype=np.float32)
    np.add.at(counts,edges[:,0],1);np.add.at(counts,edges[:,1],1)
    for iteration in range(10):
        accum=np.zeros_like(weights)
        np.add.at(accum,edges[:,0],weights[edges[:,1]])
        np.add.at(accum,edges[:,1],weights[edges[:,0]])
        weights=.55*weights+.45*accum/np.maximum(counts,1)[:,None]
    # UV seams must share skin weights or they split open when fingers bend.
    points=np.array([v.co[:] for v in mesh.data.vertices])
    _,seams=np.unique(np.round(points,6),axis=0,return_inverse=True)
    averages=np.zeros((seams.max()+1,len(names)),dtype=np.float32)
    np.add.at(averages,seams,weights)
    averages/=np.bincount(seams)[:,None]
    weights=averages[seams]
    # Keep diffusion at the palm/web, but restore each distal surface to its
    # own chain. This includes the inner faces and applies equally to both hands.
    protection=np.array(protected,dtype=np.float32)[:,None]
    weights=weights*(1-protection)+anatomical_weights*protection
    # Re-average seams after correction as well: duplicate UV vertices must
    # never disagree when the thumb/finger is curled.
    averages[:]=0;np.add.at(averages,seams,weights)
    averages/=np.bincount(seams)[:,None];weights=averages[seams]
    report={}
    for finger in paths:
        selected=np.array([(owner==finger and protect>.9999) for owner,protect in zip(owners,protected)])
        chain=[names.index(finger+str(i)) for i in range(1,4)]
        if selected.any():
            minimum=float(weights[selected][:,chain].sum(1).min())
            assert minimum>.999, (side,finger,'distal vertices pinned to another chain',minimum)
            report[finger]={'protected_vertices':int(selected.sum()),'minimum_own_chain_weight':minimum}
    assert np.max(np.abs(weights.sum(1)-1))<.0001
    reports[side]=report
    for group in groups.values():group.remove(list(range(len(weights))))
    for i,row in enumerate(weights):
        indices=np.argsort(row)[-4:];total=sum(row[j] for j in indices)
        for j in indices:
            if row[j]>.00001:groups[names[j]].add([i],float(row[j]/total),'REPLACE')
    mesh.matrix_parent_inverse=Matrix.Identity(4);mesh.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=arm
    bpy.ops.export_scene.fbx(filepath=OUT+'/Junker'+side+'GloveRig.fbx',use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y')
    # Preview a representative controller grip, curling towards the palm (+Y).
    for finger in paths:
        for i in range(3):
            bone=arm.pose.bones[finger+str(i+1)];bone.rotation_mode='AXIS_ANGLE'
            direction=bone.bone.tail_local-bone.bone.head_local
            axis=direction.cross(Vector((0,1,0))).normalized()
            local=bone.bone.matrix_local.to_3x3().inverted()@axis
            angle=math.radians(([42,65,50] if finger!='Thumb' else [12,28,24])[i])
            bone.rotation_axis_angle=(angle,*local)
    scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_cavity=True
    scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    data=bpy.data.cameras.new('Preview');cam=bpy.data.objects.new('Preview',data);scene.collection.objects.link(cam);target=Vector((0,0,.065));cam.location=target+Vector((.28,-.5,.15));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=.32;scene.camera=cam
    scene.render.filepath=ROOT+'/Temp/glove-'+side+'-grip.png';bpy.ops.render.render(write_still=True)
    print('FINAL',side,[(min((mesh.matrix_world@v.co)[i] for v in mesh.data.vertices),max((mesh.matrix_world@v.co)[i] for v in mesh.data.vertices)) for i in range(3)],mesh.matrix_world)
    print('RIGGED',side,len(mesh.data.vertices),'vertices',len(armdata.bones),'bones')
os.makedirs(ROOT+'/Library/GloveFix',exist_ok=True)
with open(ROOT+'/Library/GloveFix/weights-validation.json','w') as f:json.dump(reports,f,indent=2)
