using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

// NOTE THIS IS the basic version, adjust it to your needs

namespace SvgToStl {
    public struct Pt3 {
        public double X, Y, Z;
        public Pt3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public class Tri {
        public Pt3 A, B, C;
        public Tri(Pt3 a, Pt3 b, Pt3 c) { A = a; B = b; C = c; }
    }

    public static class MeshBuilder {
        // Extrude the geometry into a 3D mesh
        public static List<Tri> Extrude(Geometry g, double thickness = 1) {
            var r = new List<Tri>();
            // Flatten curves to lines
            var flat = g.GetFlattenedPathGeometry();
            foreach (var fig in flat.Figures) {
                // Collect points in the figure
                var pts = fig.Segments.OfType<LineSegment>()
                    .Select(s => s.Point).Prepend(fig.StartPoint).ToList();

                // Top face
                r.AddRange(Fan(pts, thickness));
                // Bottom face
                r.AddRange(Fan(pts, 0, flip: true));
                // Sides
                r.AddRange(Sides(pts, thickness));
            }
            return r;
        }

        // Triangulate top/bottom
        static IEnumerable<Tri> Fan(List<Point> ps, double z, bool flip = false) {
            var outList = new List<Tri>();
            for (int i = 1; i < ps.Count - 1; i++) {
                var a = To3D(ps[0], z);
                var b = To3D(ps[i], z);
                var c = To3D(ps[i + 1], z);
                outList.Add(flip ? new Tri(a, c, b) : new Tri(a, b, c));
            }
            return outList;
        }

        // Build side quads as 2 triangles each
        static IEnumerable<Tri> Sides(List<Point> ps, double thick) {
            var outList = new List<Tri>();
            for (int i = 0; i < ps.Count; i++) {
                int n = (i + 1) % ps.Count;
                var b1 = To3D(ps[i], 0);
                var b2 = To3D(ps[n], 0);
                var t1 = To3D(ps[i], thick);
                var t2 = To3D(ps[n], thick);
                outList.Add(new Tri(b1, b2, t1));
                outList.Add(new Tri(t1, b2, t2));
            }
            return outList;
        }

        static Pt3 To3D(Point p, double z) => new Pt3(p.X, p.Y, z);
    }

    public static class StlWriter {
        public static void WriteAsciiStl(List<Tri> mesh, string filePath, string name = "MyObject") {
            using (var w = new System.IO.StreamWriter(filePath)) {
                w.WriteLine($"solid {name}");
                foreach (var tri in mesh) {
                    var n = CalcNormal(tri);
                    w.WriteLine($"  facet normal {n.X} {n.Y} {n.Z}");
                    w.WriteLine("    outer loop");
                    w.WriteLine($"      vertex {tri.A.X} {tri.A.Y} {tri.A.Z}");
                    w.WriteLine($"      vertex {tri.B.X} {tri.B.Y} {tri.B.Z}");
                    w.WriteLine($"      vertex {tri.C.X} {tri.C.Y} {tri.C.Z}");
                    w.WriteLine("    endloop");
                    w.WriteLine("  endfacet");
                }
                w.WriteLine($"endsolid {name}");
            }
        }

        static Pt3 CalcNormal(Tri t) {
            var ux = t.B.X - t.A.X; var uy = t.B.Y - t.A.Y; var uz = t.B.Z - t.A.Z;
            var vx = t.C.X - t.A.X; var vy = t.C.Y - t.A.Y; var vz = t.C.Z - t.A.Z;
            var nx = (uy * vz) - (uz * vy);
            var ny = (uz * vx) - (ux * vz);
            var nz = (ux * vy) - (uy * vx);
            var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 1e-8) { nx /= len; ny /= len; nz /= len; }
            return new Pt3(nx, ny, nz);
        }
    }

    class Program {
        static void Main() {
            // Load the SVG
            var svgFile = new FileSvgReader(new WpfDrawingSettings());
            var drawing = svgFile.Read(@"C:\alarm1 2-2 no label.svg"); // your file

            // Collect geometries from the drawing
            var geoms = new List<Geometry>();
            CollectGeometries(drawing, geoms);

            if (geoms.Count == 0) {
                Console.WriteLine("No geometry found!");
                return;
            }

            // We'll extrude *all* geometries, or just the first. 
            // For multiple, you might combine them in one big mesh list.
            var meshAll = new List<Tri>();

            foreach (var g in geoms) {
                // Convert stroke to fill outline (1.0 wide):
                double strokeWidth = 5.0;
                var pen = new Pen(Brushes.Black, strokeWidth);
                // Outline the geometry
                var widened = g.GetWidenedPathGeometry(pen);

                // Scale if needed
                var scale = new ScaleTransform(100, 100);
                widened.Transform = scale;

                // Extrude the final geometry
                var triList = MeshBuilder.Extrude(widened, 2.0);
                meshAll.AddRange(triList);
            }

            // Write to STL
            StlWriter.WriteAsciiStl(meshAll, @"C:\pcb.stl", "MyPCB");
            Console.WriteLine("Done. Check pcb.stl!");
        }

        static void CollectGeometries(Drawing d, List<Geometry> list) {
            if (d is DrawingGroup dg) {
                foreach (var child in dg.Children)
                    CollectGeometries(child, list);
            } else if (d is GeometryDrawing gd && gd.Geometry != null) {
                // Add it to the list
                list.Add(gd.Geometry);
            }
        }
    }
}
