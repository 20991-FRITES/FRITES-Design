using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    public static class GeometryMatcher
    {

        private static double CompareVertices(
            VertexSignature a,
            VertexSignature b)
        {
            double dx = a.Point[0] - b.Point[0];
            double dy = a.Point[1] - b.Point[1];
            double dz = a.Point[2] - b.Point[2];

            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // Larger score = better
            return -distance;
        }

        public static Vertex FindBestVertex(
            Component2 component,
            VertexSignature original)
        {
            Vertex best = null;
            double bestScore = double.MinValue;

            foreach (Body2 body in (object[])component.GetBodies3(
                         (int)swBodyType_e.swSolidBody,
                         out _))
            {
                object[] vertices = body.GetVertices() as object[];

                if (vertices == null)
                    continue;

                foreach (Vertex vertex in vertices)
                {
                    VertexSignature sig = SignatureBuilder.BuildSignature(vertex);

                    double score =
                        CompareVertices(original, sig);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = vertex;
                    }
                }
            }

            return best;
        }

        private static double CompareEdges(
            EdgeSignature a,
            EdgeSignature b)
        {
            double score = 0;

            if (a.CurveType != b.CurveType)
                return double.MinValue;

            score -= Math.Abs(a.Length - b.Length) * 1000.0;

            double dx =
                a.MidPoint[0] - b.MidPoint[0];

            double dy =
                a.MidPoint[1] - b.MidPoint[1];

            double dz =
                a.MidPoint[2] - b.MidPoint[2];

            score -= Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (a.CurveType == swCurveTypes_e.LINE_TYPE)
            {
                score +=
                    100.0 *
                    Math.Abs(Dot(a.Direction, b.Direction));
            }

            if (a.CurveType == swCurveTypes_e.CIRCLE_TYPE)
            {
                score -=
                    Math.Abs(a.Radius - b.Radius) * 1000.0;
            }

            return score;
        }

        public static Edge FindBestEdge(
            Component2 component,
            EdgeSignature original)
        {
            Edge best = null;

            double bestScore =
                double.MinValue;

            foreach (Body2 body in (object[])component.GetBodies3(
                         (int)swBodyType_e.swSolidBody,
                         out _))
            {
                object[] edges =
                    body.GetEdges() as object[];

                if (edges == null)
                    continue;

                foreach (Edge edge in edges)
                {
                    EdgeSignature sig = SignatureBuilder.BuildSignature(edge);

                    double score =
                        CompareEdges(original, sig);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = edge;
                    }
                }
            }

            return best;
        }
        
        public static IEnumerable<Face2> GetFaces(Component2 component)
        {
            object[] bodies =
                (object[])component.GetBodies3(
                    (int)swBodyType_e.swSolidBody,
                    out _);

            if (bodies == null)
                yield break;

            foreach (Body2 body in bodies)
            {
                object[] faces =
                    (object[])body.GetFaces();

                if (faces == null)
                    continue;

                foreach (Face2 face in faces)
                    yield return face;
            }
        }

        public static Face2 FindBestFace(
            Component2 component,
            FaceSignature original)
        {
            List<(Face2 Face, FaceSignature Sig)> candidates = GetFaces(component)
                .Select(f => (f, SignatureBuilder.BuildSignature(component, f)))
                .ToList();

            Debug.WriteLine($"Initial: {candidates.Count}");

            //------------------------------------
            // Surface type
            //------------------------------------

            var filtered = candidates
                .Where(x => x.Sig.SurfaceType == original.SurfaceType)
                .ToList();

            Debug.WriteLine($"Surface: {filtered.Count}");

            if (filtered.Any())
                candidates = filtered;

            //------------------------------------
            // Plane orientation
            //------------------------------------

            if (original.SurfaceType == swSurfaceTypes_e.PLANE_TYPE)
            {
                filtered = candidates
                    .Where(x => Dot(x.Sig.Normal, original.Normal) > 0.99)
                    .ToList();

                Debug.WriteLine($"Normal: {filtered.Count}");

                if (filtered.Any())
                    candidates = filtered;

                //------------------------------------
                // Plane position
                //------------------------------------

                var ranked = candidates
                    .OrderBy(x =>
                        Math.Abs(x.Sig.PlaneOffset - original.PlaneOffset))
                    .ThenBy(x =>
                        Math.Abs(x.Sig.Extent1 - original.Extent1) +
                        Math.Abs(x.Sig.Extent2 - original.Extent2));


                foreach (var c in ranked.Take(20))
                {
                    Debug.WriteLine(
                        $"Offset={Math.Abs(c.Sig.PlaneOffset - original.PlaneOffset):F4}  " +
                        $"Ext={c.Sig.Extent1:F4} x {c.Sig.Extent2:F4}");
                }

                Debug.WriteLine($"Final candidates: {candidates.Count}");

                return ranked.First().Face;
            }

            //------------------------------------
            // Fallback
            //------------------------------------

            return candidates.FirstOrDefault().Face;
        }
        
        public static double Dot(
            double[] a,
            double[] b)
        {
            return
                a[0] * b[0] +
                a[1] * b[1] +
                a[2] * b[2];
        }
    }
}