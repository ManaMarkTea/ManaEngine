"""Convert the public-domain ShaderBall OBJ ZIP to a single-material, unit-height GLB.
Usage: python Tools/Models/convert_shader_ball.py Assets/Models/ShaderBall-master.zip
Requires numpy. Preserves split UVs and authored normals, triangulating source polygons.
"""
import json
import struct
import sys
import zipfile
from pathlib import Path
import numpy as np

source = Path(sys.argv[1])
output = Path(__file__).resolve().parents[2] / 'Mana/Assets/Models/ShaderBall'
output.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(source) as archive:
    text = archive.read('ShaderBall-master/shaderBallNoCrease/shaderBall.obj').decode()
    (output / 'LICENSE').write_bytes(archive.read('ShaderBall-master/LICENSE'))
    (output / 'SOURCE.md').write_text('ShaderBall by Mat Makin, released by derkreature.\nSource: https://github.com/derkreature/ShaderBall\nLicense: Unlicense (public domain), included alongside this file.\nConverted from shaderBallNoCrease/shaderBall.obj with Tools/Models/convert_shader_ball.py.\nAll six source groups share one editable material. UVs and normals are retained; height is one unit, base at y=0.\n')
positions, normals, uvs, vertices, indices = [], [], [], [], []
lookup = {}
for line in text.splitlines():
    fields = line.split()
    if not fields:
        continue
    if fields[0] == 'v':
        positions.append(list(map(float, fields[1:4])))
    elif fields[0] == 'vn':
        normals.append(list(map(float, fields[1:4])))
    elif fields[0] == 'vt':
        uvs.append(list(map(float, fields[1:3])))
    elif fields[0] == 'f':
        face = []
        for item in fields[1:]:
            key = tuple(int(x) for x in item.split('/'))
            if key not in lookup:
                lookup[key] = len(vertices)
                vertices.append(key)
            face.append(lookup[key])
        for i in range(1, len(face)-1):
            indices.extend([face[0], face[i], face[i+1]])
p = np.asarray([positions[x[0]-1] for x in vertices], dtype='<f4')
low, high = p.min(0), p.max(0)
p = (p - np.array([(low[0]+high[0])/2, low[1], (low[2]+high[2])/2], dtype='<f4')) / (high[1]-low[1])
n = np.asarray([normals[x[2]-1] for x in vertices], dtype='<f4')
n /= np.linalg.norm(n, axis=1, keepdims=True)
uv = np.asarray([uvs[x[1]-1] for x in vertices], dtype='<f4')
uv[:,1] = 1-uv[:,1]
arrays = [p,n,uv,np.asarray(indices,dtype='<u4')]
blob = bytearray(); views=[]; accessors=[]
for i,a in enumerate(arrays):
    blob.extend(b'\0' * ((-len(blob)) % 4))
    views.append({'buffer':0,'byteOffset':len(blob),'byteLength':a.nbytes,'target':34963 if i==3 else 34962})
    blob.extend(a.tobytes())
    acc={'bufferView':i,'componentType':5125 if i==3 else 5126,'count':len(a),'type':['VEC3','VEC3','VEC2','SCALAR'][i]}
    if i==0:
        acc.update(min=p.min(0).tolist(),max=p.max(0).tolist())
    accessors.append(acc)
doc={'asset':{'version':'2.0','generator':'Mana ShaderBall converter'},'scene':0,'scenes':[{'nodes':[0]}],
     'nodes':[{'name':'ShaderBall','mesh':0}], 'meshes':[{'primitives':[{'attributes':{'POSITION':0,'NORMAL':1,'TEXCOORD_0':2},'indices':3,'material':0}]}],
     'materials':[{'name':'Surface','pbrMetallicRoughness':{'baseColorFactor':[1,1,1,1],'metallicFactor':0,'roughnessFactor':0.5}}],
     'buffers':[{'byteLength':len(blob)}],'bufferViews':views,'accessors':accessors}
j=json.dumps(doc,separators=(',',':')).encode();j+=b' '*((-len(j))%4);blob.extend(b'\0'*((-len(blob))%4))
glb=struct.pack('<III',0x46546c67,2,12+8+len(j)+8+len(blob))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(blob),0x004e4942)+blob
(output/'ShaderBall.glb').write_bytes(glb)
print(f'{len(vertices)} vertices, {len(indices)//3} triangles, {len(glb)/1048576:.2f} MiB: {output}')
