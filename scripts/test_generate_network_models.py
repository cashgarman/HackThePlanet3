"""Check outward winding on each convex solid used by the hardware models."""
import contextlib
import io
import unittest
from unittest.mock import patch

from generate_network_models import Model, build


def subtract(a, b):
    return tuple(x - y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


class ModelWindingTests(unittest.TestCase):
    def assert_outward(self, vertices, faces):
        center = tuple(sum(v[i] for v in vertices)/len(vertices) for i in range(3))
        edges = {}
        for face in faces:
            a, b, c = (vertices[i-1] for i in face)
            normal = cross(subtract(b, a), subtract(c, a))
            self.assertGreater(sum(n*n for n in normal), 1e-20, "Degenerate triangle")
            self.assertGreater(sum(n*d for n, d in zip(normal, subtract(a, center))), 0,
                               "Triangle faces into its solid")
            for start, end in ((a, b), (b, c), (c, a)):
                key = tuple(sorted((start, end)))
                edges.setdefault(key, []).append(1 if start < end else -1)
        for directions in edges.values():
            self.assertEqual(len(directions), 2, "Solid has an open or non-manifold edge")
            self.assertEqual(sum(directions), 0, "Adjacent triangles disagree on winding")

    def test_box(self):
        model = Model()
        model.box(2, -3, 4, 1, 2, 3)
        self.assert_outward(model.vertices, model.faces)

    def test_cylinders_frustums_and_cones_on_both_axes(self):
        for axis in ('y', 'z'):
            for top in (None, .3, 0):
                with self.subTest(axis=axis, top=top):
                    model = Model()
                    model.cylinder(2, -3, 4, .6, 1, top=top, axis=axis)
                    self.assert_outward(model.vertices, model.faces)

    def test_every_solid_in_the_complete_model_set(self):
        # Check locally: a large correct box must not hide an inside-out detail.
        original_solid = Model.solid
        counts = []

        def checked_solid(model, vertices, faces):
            solid = Model()
            original_solid(solid, vertices, faces)
            self.assert_outward(solid.vertices, solid.faces)
            counts.append(len(solid.faces))
            original_solid(model, vertices, faces)

        with patch.object(Model, 'solid', checked_solid), patch.object(Model, 'save'), contextlib.redirect_stdout(io.StringIO()):
            build()
        self.assertGreaterEqual(len(counts), 40)


if __name__ == '__main__':
    unittest.main()
