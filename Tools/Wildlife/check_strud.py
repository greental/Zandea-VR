import bpy, os, json, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath('Tools/Wildlife/Strud_Authored.blend'))
rig=bpy.data.objects['StrudRig'];mesh=bpy.data.objects['StrudSkin'];scene=bpy.context.scene
report={'vertices':len(mesh.data.vertices),'bones':len(rig.data.bones),'clips':list(bpy.data.actions.keys())}
for name in ['Walk','Trot','Graze','Death']:
 rig.animation_data.action=bpy.data.actions[name]
 frames=int(rig.animation_data.action.frame_range.y)
 points=[]
 for f in range(frames+1):
  scene.frame_set(f)
  points.append([list(rig.pose.bones[n].head) for n in ['FrontLFoot','FrontRFoot','HindLFoot','HindRFoot','Head']])
 report[name]={'first':points[0],'middle':points[frames//2], 'last':points[-1]}
 if name in ('Walk','Trot'):
  minimum=1e9; maximum_jump=0; previous={}
  for f in range(frames+1):
   scene.frame_set(f)
   for end in ('Front','Hind'):
    for side in ('L','R'):
     key=end+side
     hip=rig.pose.bones[key+'Upper'].head
     knee=rig.pose.bones[key+'Lower'].head
     ankle=rig.pose.bones[key+'Ankle'].head
     axis=(ankle-hip).normalized()
     pole=Vector((0,-1,0));pole=(pole-axis*pole.dot(axis)).normalized()
     signed=(knee-hip).dot(pole)
     minimum=min(minimum,signed)
     if key in previous:maximum_jump=max(maximum_jump,(knee-previous[key]).length)
     previous[key]=knee.copy()
  report[name]['minimum_forward_knee_bend']=minimum
  report[name]['maximum_knee_step']=maximum_jump
  assert minimum>0, name+' knee reversed'
  assert maximum_jump<.65, name+' knee jumped between frames'
os.makedirs('Temp/StrudReview',exist_ok=True)
open('Temp/StrudReview/anatomy.json','w').write(json.dumps(report,indent=2))
print(json.dumps(report))
