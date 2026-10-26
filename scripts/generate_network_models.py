"""Original low-poly hardware icons. Standard-library-only, deterministic OBJ export."""
from pathlib import Path
from math import sin, cos, pi, sqrt

ROOT = Path(__file__).resolve().parents[1] / 'Assets/HackThePlanet/Resources/HackThePlanet/NodeModels'

class Model:
    def __init__(self): self.vertices, self.faces = [], []
    def solid(self, vertices, faces):
        offset = len(self.vertices)
        self.vertices.extend(vertices)
        for face in faces:
            for i in range(1, len(face)-1):
                self.faces.append(tuple(offset + j + 1 for j in (face[0], face[i], face[i+1])))
    def box(self, x, y, z, w, h, d):
        self.solid([(x+a*w/2,y+b*h/2,z+c*d/2) for a,b,c in
            [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]],
            [(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)])
    def cylinder(self, x,y,z,r,h,top=None, segments=10, axis='y'):
        top = r if top is None else top
        def point(a,b,c): return (x+a,y+b,z+c) if axis=='y' else (x+a,y+c,z+b)
        v=[point(cos(i*2*pi/segments)*radius,height,sin(i*2*pi/segments)*radius)
           for radius,height in [(r,-h/2),(top,h/2)] for i in range(segments)]
        f=[tuple(reversed(range(segments))),tuple(range(segments,segments*2))]
        f += [(i,(i+1)%segments,(i+1)%segments+segments,i+segments) for i in range(segments)]
        self.solid(v,f)
    def save(self,name):
        mins=[min(v[i] for v in self.vertices) for i in range(3)]
        maxs=[max(v[i] for v in self.vertices) for i in range(3)]
        center=[(a+b)/2 for a,b in zip(mins,maxs)]
        radius=max(sqrt(sum((v[i]-center[i])**2 for i in range(3))) for v in self.vertices)
        lines=[f'o {name}', 's off']
        lines += ['v '+' '.join(f'{(v[i]-center[i])*.5/radius:.6f}' for i in range(3)) for v in self.vertices]
        lines += ['f '+' '.join(map(str,f)) for f in self.faces]
        ROOT.mkdir(parents=True,exist_ok=True)
        (ROOT/(name+'.obj')).write_text('\n'.join(lines)+'\n')
        print(name,len(self.faces),'triangles')

def build():
    m=Model();m.box(0,0,0,1.5,.35,.85)
    for x in [-.55,.55]:m.cylinder(x,.48,-.23,.045,.85,segments=6)
    for x in [-.45,-.15,.15,.45]:m.box(x,.03,-.45,.13,.09,.05)
    m.save('Gateway')
    m=Model();m.box(0,.2,0,1.25,.85,.25);m.box(0,.2,-.15,1.02,.63,.08)
    m.box(0,-.4,0,.16,.4,.15);m.box(0,-.62,0,.75,.1,.5);m.box(0,-.63,-.45,1,.09,.3)
    m.save('Workstation')
    m=Model();m.cylinder(0,-.6,0,.45,.18,segments=8);m.cylinder(0,0,0,.1,1.2,segments=8)
    for y,r in [(-.1,.38),(.25,.28),(.55,.17)]:m.cylinder(0,y,0,r,.12,segments=8)
    m.cylinder(0,.83,0,.12,.3,top=0,segments=8);m.save('Relay')
    m=Model();m.solid([(-.65,.7,-.15),(.65,.7,-.15),(.58,-.1,-.15),(0,-.75,-.15),(-.58,-.1,-.15),
        (-.65,.7,.15),(.65,.7,.15),(.58,-.1,.15),(0,-.75,.15),(-.58,-.1,.15)],
        [(4,3,2,1,0),(5,6,7,8,9),(0,1,6,5),(1,2,7,6),(2,3,8,7),(3,4,9,8),(4,0,5,9)])
    m.box(0,.15,-.22,.17,.72,.15);m.box(0,.24,-.23,.7,.16,.15);m.save('Firewall')
    m=Model();m.cylinder(0,-.57,0,.43,.2,segments=8);m.box(0,-.2,0,.13,.65,.13)
    m.cylinder(0,.3,0,.62,.24,top=.3,segments=12,axis='z');m.cylinder(0,.3,-.3,.07,.55,segments=6,axis='z')
    m.save('Scanner')
    m=Model()
    for y in [-.48,0,.48]:
        m.cylinder(0,y,0,.57,.34,segments=12);m.box(0,y,-.575,.35,.08,.04)
    m.save('Database')
    m=Model();m.box(0,-.59,0,1,.2,1);m.box(0,0,0,.62,1.15,.62)
    for y in [-.32,0,.32]:
        m.box(0,y,-.34,.43,.12,.09);m.box(.34,y,0,.09,.12,.43)
    m.cylinder(0,.75,0,.23,.43,top=0,segments=8);m.save('SecurityHub')

if __name__=='__main__':build()
