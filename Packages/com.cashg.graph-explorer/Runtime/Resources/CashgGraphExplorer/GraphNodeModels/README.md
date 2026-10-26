# Graph node models

Original low-poly OBJ meshes, with geometry on both sides and no texture or material dependencies.

| Shape ID  | Model                                        | Triangles |
|-----------|----------------------------------------------|----------:|
| Briefcase | Briefcase with open handle and clasp         |       108 |
| Building  | Office building with windows                 |       216 |
| Badge     | ID badge with portrait and text bars         |       228 |
| Factory   | Factory with sawtooth roof and chimneys      |       132 |
| Gear      | Eight-tooth gear with center hole            |       256 |
| Person    | Faceted head and shoulders                   |       180 |
| MapPin    | Map pin with center hole                     |       144 |
| Globe     | Wire globe with meridians and latitude rings |       576 |
| Document  | Document with folded corner and text bars    |       128 |
| Gem       | Faceted gem                                  |        32 |

All meshes use Y up, a centered bounding-box pivot, and a maximum vertex distance of 0.5 from the pivot. The graph
scales them by `2 * Radius(node)`, so geometry fits the existing node radius. The selection halo has its own mesh.
Picking uses a slightly larger sphere, making thin shapes and holes easy to select.

`GraphNodeModels.Create` loads one imported mesh per shape and clones it once for the renderer to own. The renderer
supplies white vertex colors and the existing instance tint. Each model has one submesh, so the existing batches remain
one instanced draw per shape per 1,023 nodes. Models are not regenerated per frame and need no per-node GameObjects or
colliders. The importer keeps CPU mesh access enabled for assigning vertex colors in player builds.

Regenerate from the repository root:

```powershell
.\.venv\Scripts\python.exe scripts/generate_graph_node_models.py
```

The generator uses only Python's standard library. Its small geometry helpers expose extrusion, surfaces of revolution,
and tubes directly. Edit these source dimensions to revise the set; the final normalization preserves consistent graph
scale. Keep schema shape IDs aligned with the OBJ filenames and retain their `.meta` files. The OBJ object names remain
unchanged to preserve imported subasset IDs.

In Unity, run **Job Harvester > Validate and preview node models**. It checks every imported mesh, renders front /
three-quarter / rear views with the actual graph shader, and checks submission and picking through `GraphRenderer`.
Results are written under the project directory's `node-model-validation` folder. These checks do not measure
large-graph frame time.
