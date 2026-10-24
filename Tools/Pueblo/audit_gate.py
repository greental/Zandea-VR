"""Check the source gate's real mesh opening across its depth at human height."""
from pathlib import Path
import numpy as np
import trimesh

source = Path.home() / "Downloads/puebloBonito/output/wall_gate.glb"
mesh = trimesh.load(source, force="mesh")
triangles = mesh.triangles
minimum_y = mesh.bounds[0, 1]


def blocked(x: float, y: float) -> bool:
    origin = np.array([x, y, mesh.bounds[0, 2] - 1])
    direction = np.array([0., 0., 1.])
    edge1 = triangles[:, 1] - triangles[:, 0]
    edge2 = triangles[:, 2] - triangles[:, 0]
    h = np.cross(np.broadcast_to(direction, edge2.shape), edge2)
    a = np.einsum("ij,ij->i", edge1, h)
    valid = abs(a) > 1e-8
    f = np.zeros_like(a)
    f[valid] = 1 / a[valid]
    s = origin - triangles[:, 0]
    u = f * np.einsum("ij,ij->i", s, h)
    q = np.cross(s, edge1)
    v = f * q[:, 2]
    t = f * np.einsum("ij,ij->i", edge2, q)
    return bool(np.any(valid & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 0)))


for height in [0.2, 0.4, 0.6]:
    open_x = [round(x, 3) for x in np.arange(-0.95, 0.96, 0.05) if not blocked(x, minimum_y + height)]
    print("height_m", round(height * 4, 2), "open_model_x", open_x)
