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
os.makedirs('Temp/StrudReview',exist_ok=True)
open('Temp/StrudReview/anatomy.json','w').write(json.dumps(report,indent=2))
print(json.dumps(report))
