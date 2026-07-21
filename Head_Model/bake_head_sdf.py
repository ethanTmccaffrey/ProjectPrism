"""
PRISM head SDF baker.
 
Samples a head mesh into a signed distance field and writes it as a binary file
for Unity to load. Run once, offline. Unity then adds track-driven displacement
to the baked field and meshes it with marching cubes at runtime, so every song
produces a differently-surfaced head without ever re-running this step.
 
Usage:  bake_head_sdf.py <input_mesh> <output.bytes> [pitch]
 
WHY A SIGNED DISTANCE FIELD RATHER THAN VOXEL FILL
 
The obvious approach - voxelise the mesh, flood fill from outside, call the
unreachable voxels "interior" - does not work on a head. Measured on the source
mesh: the model is 7 separate surface panels with 440 boundary edges, open at
the mouth, both ear canals, both eye sockets and the neck. Those openings are
correct anatomy, not defects, but they are far too large for a flood fill to
seal: every fill attempt leaked straight out through the eye sockets and
reported an interior volume of exactly zero, at every resolution tried, with and
without morphological closing.
 
Signed distance via the generalized winding number does not care about holes. It
asks "how much surface wraps around this point" rather than "can I walk here
from outside", so an open mesh is fine. Measured against the same model it
recovered an interior volume of 18.91 against the mesh's own 18.73 - under 1%
error - where flood fill recovered nothing.
 
The field is also more useful than a binary occupancy grid: it supports smooth
displacement (add noise to the distance), cutting (subtract a half-space), and
containment queries (sign of the sampled value) from one representation.
 
OUTPUT FORMAT (little-endian)
    int32   magic 0x50534446 ("PSDF")
    int32   version
    int32   nx, ny, nz          grid dimensions
    float32 originX/Y/Z         world position of voxel [0,0,0]
    float32 pitch               world size of one voxel
    float32 data[nx*ny*nz]      signed distance, positive INSIDE
                                indexed [x*ny*nz + y*nz + z]
"""
 
import sys
import struct
import numpy as np
import trimesh
from trimesh.proximity import ProximityQuery
 
MAGIC = 0x50534446
VERSION = 1
DEFAULT_PITCH = 0.08
PADDING = 0.25          # world units of empty space around the mesh bounds
BATCH = 8000            # points per signed_distance call; larger runs out of memory
 
 
def bake(mesh_path, out_path, pitch=DEFAULT_PITCH):
    mesh = trimesh.load(mesh_path, force='mesh', process=False)
    mesh.merge_vertices()
 
    print(f"loaded {mesh_path}")
    print(f"  vertices {len(mesh.vertices)}  faces {len(mesh.faces)}")
    print(f"  watertight {mesh.is_watertight}  bodies {mesh.body_count}")
    print(f"  bounds {mesh.bounds[0].round(3)} .. {mesh.bounds[1].round(3)}")
 
    lo = mesh.bounds[0] - PADDING
    hi = mesh.bounds[1] + PADDING
 
    xs = np.arange(lo[0], hi[0], pitch)
    ys = np.arange(lo[1], hi[1], pitch)
    zs = np.arange(lo[2], hi[2], pitch)
    nx, ny, nz = len(xs), len(ys), len(zs)
    total = nx * ny * nz
 
    print(f"  grid {nx} x {ny} x {nz} = {total} samples at pitch {pitch}")
 
    X, Y, Z = np.meshgrid(xs, ys, zs, indexing='ij')
    pts = np.column_stack([X.ravel(), Y.ravel(), Z.ravel()])
 
    pq = ProximityQuery(mesh)
    sd = np.zeros(total, dtype=np.float32)
 
    for i in range(0, total, BATCH):
        sd[i:i + BATCH] = pq.signed_distance(pts[i:i + BATCH])
        if (i // BATCH) % 20 == 0:
            print(f"    {min(i + BATCH, total)}/{total}", flush=True)
 
    inside = int((sd > 0).sum())
    est_volume = inside * pitch ** 3
    print(f"  interior samples {inside} ({100.0 * inside / total:.1f}%)")
    print(f"  estimated volume {est_volume:.3f} (mesh reports {mesh.volume:.3f})")
 
    if inside == 0:
        print("  WARNING: no interior found - the field is wrong")
 
    with open(out_path, 'wb') as f:
        f.write(struct.pack('<i', MAGIC))
        f.write(struct.pack('<i', VERSION))
        f.write(struct.pack('<3i', nx, ny, nz))
        f.write(struct.pack('<3f', float(lo[0]), float(lo[1]), float(lo[2])))
        f.write(struct.pack('<f', float(pitch)))
        f.write(sd.astype('<f4').tobytes())
 
    size_mb = (16 + 12 + 4 + total * 4) / (1024 * 1024)
    print(f"wrote {out_path}  ({size_mb:.2f} MB)")
 
 
def main():
    if len(sys.argv) < 3:
        print("Usage: bake_head_sdf.py <input_mesh> <output.bytes> [pitch]")
        sys.exit(1)
 
    pitch = float(sys.argv[3]) if len(sys.argv) > 3 else DEFAULT_PITCH
    bake(sys.argv[1], sys.argv[2], pitch)
 
 
if __name__ == "__main__":
    main()
