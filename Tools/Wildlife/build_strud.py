"""Non-destructive Strud authoring pipeline. Run from repository root in Blender 2.93+.
Anatomical landmarks are in source GLB coordinates (Y up, Z forward).
"""
import bpy, math, os, json
from mathutils import Vector, Matrix
ROOT=os.getcwd()
OUT=os.path.join(ROOT,'Assets/Wildlife/Strud/Art')
os.makedirs(OUT,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT,'Assets/MyAssets/mesh/Wildlife/Strud/Strud.glb'))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
mesh.name='StrudSkin'
S=3.0
def P(p): return Vector((p[0]*S,-p[2]*S,(p[1]+.636684)*S))
# Apply scale and grounded origin into geometry, leaving an identity object transform.
for v in mesh.data.vertices: v.co=Vector((v.co.x*S,v.co.y*S,(v.co.z+.636684)*S))
mesh.matrix_world=Matrix.Identity(4)
bpy.ops.object.armature_add()
rig=bpy.context.object;rig.name='StrudRig'
bpy.ops.object.mode_set(mode='EDIT');rig.data.edit_bones.remove(rig.data.edit_bones[0])
spec={}
def bone(n,h,t,parent=None):
 b=rig.data.edit_bones.new(n);b.head=P(h);b.tail=P(t)
 if parent:b.parent=rig.data.edit_bones[parent]
 spec[n]=(Vector(h),Vector(t),parent)
bone('Root',(0,-.636684,0),(0,-.5,0))
bone('Pelvis',(0,0,-.3),(0,.03,-.12),'Root')
bone('Spine01',(0,.03,-.12),(0,.05,.12),'Pelvis')
bone('Spine02',(0,.05,.12),(0,.06,.33),'Spine01')
bone('Chest',(0,.06,.33),(0,.12,.5),'Spine02')
bone('Neck01',(0,.12,.5),(0,.32,.56),'Chest')
bone('Neck02',(0,.32,.56),(0,.51,.57),'Neck01')
bone('Head',(0,.51,.57),(0,.52,.86),'Neck02')
bone('Tail01',(0,0,-.3),(0,-.04,-.52),'Pelvis')
bone('Tail02',(0,-.04,-.52),(0,-.1,-.72),'Tail01')
bone('Tail03',(0,-.1,-.72),(0,-.15,-.94),'Tail02')
legs=[]
for side,sgn in [('L',1),('R',-1)]:
 for end in ['Front','Hind']:
  x=.18*sgn
  pts=[(x,.02,.45),(x,-.30,.46),(x,-.53,.5),(x,-.61,.56),(x,-.61,.66)] if end=='Front' else [(x,.04,-.26),(x,-.28,-.22),(x,-.49,-.33),(x,-.61,-.28),(x,-.61,-.16)]
  names=[end+side+s for s in ['Upper','Lower','Ankle','Foot']]
  for i,n in enumerate(names):bone(n,pts[i],pts[i+1],('Chest' if end=='Front' else 'Pelvis') if i==0 else names[i-1])
  legs.append((end,side,names,pts))
bpy.ops.object.mode_set(mode='OBJECT')
# Deterministic four-influence skinning, region-gated to prevent torso/contralateral capture.
groups={n:mesh.vertex_groups.new(name=n) for n in spec}
def dist(p,a,b):
 q=b-a;t=max(0,min(1,(p-a).dot(q)/q.length_squared));return (p-a-t*q).length
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
for v in mesh.data.vertices:
 p=Vector((v.co.x/S,v.co.z/S-.636684,-v.co.y/S));x,y,z=p
 side='L' if x>0 else 'R';end='Front' if z>.12 else 'Hind'
 legblend=(1-smooth(-.30,.04,y))*(1-smooth(-.30,-.14,y)*(1-smooth(.07,.14,abs(x))))
 region=smooth(.20,.35,z)*(1-smooth(.65,.75,z)) if end=='Front' else smooth(-.55,-.40,z)*(1-smooth(-.14,0,z))
 legblend*=1-smooth(-.32,-.12,y)*(1-region)
 # Let the fleshy shoulder and haunch follow the upper bone, fading gradually
 # into the trunk instead of leaving a stationary cap above a moving shin.
 hip=spec[end+side+'Upper'][0]
 attach=(1-smooth(.12,.38,(p-hip).length))*smooth(.055,.18,abs(x))*(1-smooth(.12,.30,y))
 legblend=max(legblend,.82*attach)
 # Shoulders and hips retain soft body attachment; feet remain rigid inside their segment.
 candidates=['Pelvis','Spine01','Spine02','Chest','Neck01','Neck02','Head','Tail01','Tail02','Tail03']
 body=[(dist(p,*spec[n][:2]),n) for n in candidates]
 limb=[end+side+s for s in ['Upper','Lower','Ankle','Foot']]
 near=[(dist(p,*spec[n][:2]),n) for n in limb]
 weights={}
 for pairs,mix in [(body,1-legblend),(near,legblend)]:
  ws=[1/(d+.055)**3 for d,n in pairs];total=sum(ws)
  for (_,n),w in zip(pairs,ws):weights[n]=mix*w/total
 # Unity's four-bone skinning budget: choose the strongest continuous weights.
 strongest=sorted(weights.items(),key=lambda nw:nw[1],reverse=True)[:4]
 total=sum(w for _,w in strongest)
 for n,w in strongest:
  w/=total
  if w>1e-6:groups[n].add([v.index],w,'REPLACE')
mod=mesh.modifiers.new('Quadruped skin','ARMATURE');mod.object=rig
mod.show_viewport=False
mesh.parent=rig
for im in bpy.data.images:
 if im.size[0]>0 and im.type=='IMAGE':
  im.filepath_raw=os.path.join(OUT,'Strud_BaseColor.png');im.file_format='PNG';im.save()
rig.animation_data_create()
scene=bpy.context.scene;scene.render.fps=30
rest={n:b.matrix_local.copy() for n,b in rig.data.bones.items()}
def orient(n,head,tail):
 base=rest[n];q=base.to_quaternion();direction=q@Vector((0,1,0))
 rot=direction.rotation_difference((tail-head).normalized())@q
 rig.pose.bones[n].matrix=Matrix.Translation(head)@rot.to_matrix().to_4x4()
def solve(names,pts,offset):
 upper,lower,ankle,foot=names
 hip=rig.pose.bones[upper].head.copy();target=P(pts[2])+offset
 a=(P(pts[1])-P(pts[0])).length;b=(P(pts[2])-P(pts[1])).length
 delta=target-hip;d=max(.001,min(delta.length,a+b-.005));axis=delta.normalized()
 bend=P(pts[1])-P(pts[0]);bend=(bend-axis*bend.dot(axis)).normalized()
 along=(a*a-b*b+d*d)/(2*d);knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
 orient(upper,hip,knee);bpy.context.view_layer.update()
 orient(lower,knee,target);bpy.context.view_layer.update()
 orient(ankle,target,target+P(pts[3])-P(pts[2]));bpy.context.view_layer.update()
 orient(foot,rig.pose.bones[foot].head,rig.pose.bones[foot].head+P(pts[4])-P(pts[3]))
clips=[('Idle',4,0),('Walk',1.6,1.35),('Trot',.95,2.8),('Turn',2,0),('Graze',6,0),('Alert',2,0),('Hit',.6,0),('Death',2.5,0)]
for name,duration,speed in clips:
 action=bpy.data.actions.new(name);rig.animation_data.action=action
 frames=round(duration*30)
 for frame in range(frames+1):
  scene.frame_set(frame)
  t=frame/30;phase=frame/frames;tau=phase*math.tau
  for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4);pb.rotation_mode='QUATERNION'
  pelvis=rig.pose.bones['Pelvis']
  pelvis.matrix=Matrix.Translation(Vector((0,0,-.16+.016*math.sin(tau*2 if speed else tau))))@rest['Pelvis']
  if name=='Graze':
   amount=math.sin(math.pi*min(1,phase/.25)/2) if phase<.25 else (math.sin(math.pi*min(1,(1-phase)/.25)/2) if phase>.75 else 1)
   for bn,angle in [('Chest',.18),('Neck01',.7),('Neck02',.30),('Head',-.16)]:
    rig.pose.bones[bn].rotation_quaternion=__import__('mathutils').Quaternion((1,0,0),angle*amount)
  elif name=='Alert':
   for bn,angle in [('Chest',-.08),('Neck01',-.38),('Neck02',-.46),('Head',.14)]:
    rig.pose.bones[bn].rotation_quaternion=__import__('mathutils').Quaternion((1,0,0),angle*math.sin(math.pi*phase))
   rig.pose.bones['Head'].rotation_quaternion=__import__('mathutils').Quaternion((0,0,1),.14*math.sin(tau))@rig.pose.bones['Head'].rotation_quaternion
  elif name=='Idle':
   reach=max(0,math.sin(tau))
   for bn,angle in [('Neck01',.16),('Neck02',-.24),('Head',.08)]:
    rig.pose.bones[bn].rotation_quaternion=__import__('mathutils').Quaternion((1,0,0),angle*reach)
  elif name=='Hit':pelvis.location.x=.12*math.sin(math.pi*phase)*(1-phase)
  elif name=='Death':
   ease=smooth(0,.85,phase)
   pelvis.matrix=Matrix.Translation(rest['Pelvis'].translation+Vector((.35*ease,0,-.16-.75*ease)))@__import__('mathutils').Quaternion((0,1,0),1.1*ease).to_matrix().to_4x4()@rest['Pelvis'].to_quaternion().to_matrix().to_4x4()
   for bn in ['Neck01','Neck02']:rig.pose.bones[bn].rotation_quaternion=__import__('mathutils').Quaternion((1,0,0),.5*ease)
  for bn in ['Tail01','Tail02','Tail03']:
   rig.pose.bones[bn].rotation_quaternion=__import__('mathutils').Quaternion((0,0,1),.045*math.sin(tau+.5*int(bn[-1])))
  if name not in ['Graze','Alert','Death','Idle']:rig.pose.bones['Neck02'].rotation_quaternion=__import__('mathutils').Quaternion((1,0,0),.025*math.sin(tau))
  bpy.context.view_layer.update()
  for end,side,names,pts in legs:
   offset=Vector((0,0,0))
   if speed or name=='Turn':
    phases={('Hind','L'):0,('Front','L'):.25,('Hind','R'):.5,('Front','R'):.75} if name!='Trot' else {('Hind','L'):0,('Front','R'):0,('Hind','R'):.5,('Front','L'):.5}
    p=(phase+phases[end,side])%1;stance=.68 if name!='Trot' else .55
    stride=speed*duration*stance if speed else .25
    if p<stance:forward=stride*(.5-p/stance);lift=0
    else:
     u=(p-stance)/(1-stance);forward=stride*(-.5+u*u*(3-2*u));lift=(.20 if name!='Trot' else .28)*math.sin(math.pi*u)**2
    offset=Vector((0,-forward,lift))
   if name!='Death':solve(names,pts,offset)
  for pb in rig.pose.bones:
   pb.keyframe_insert('location',frame=frame,group=pb.name);pb.keyframe_insert('rotation_quaternion',frame=frame,group=pb.name)
 for fc in action.fcurves:
  for k in fc.keyframe_points:k.interpolation='LINEAR'
 action.use_fake_user=True
 print('AUTHORED',name,flush=True)
rig.animation_data.action=bpy.data.actions['Idle'];scene.frame_set(0)
mod.show_viewport=True
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'Tools/Wildlife/Strud_Authored.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Strud_Rigged.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='RELATIVE')
# Visual evidence of actual skinned deformation at four walk phases.
scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=1000;scene.render.resolution_y=750;scene.render.resolution_percentage=100
bpy.ops.object.camera_add(location=(8,-10,5));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1.9))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=7.5;scene.camera=cam
rig.animation_data.action=bpy.data.actions['Walk']
for f in [0,12,24,36]:
 scene.frame_set(f);scene.render.filepath=os.path.join(ROOT,'Temp/StrudReview/walk_%02d.png'%f);bpy.ops.render.render(write_still=True)
print('STRUD_BUILD_COMPLETE',flush=True)
