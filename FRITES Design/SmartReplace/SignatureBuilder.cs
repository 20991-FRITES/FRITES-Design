using System;
using System.Collections.Generic;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    class SignatureBuilder
    {
        public static void Normalize(double[] v)
        {
            double len =
                Math.Sqrt(v[0] * v[0] +
                          v[1] * v[1] +
                          v[2] * v[2]);

            if (len < 1e-9)
                return;

            v[0] /= len;
            v[1] /= len;
            v[2] /= len;
        }

        public static double NormalizePlaneOffset(
            double point,
            double min,
            double max)
        {
            double size = max - min;

            if (size < 1e-9)
                return 0;

            return (point - min) / size;
        }
        
        public static FaceSignature BuildSignature(
            Component2 component,
            Face2 face)
        {
            if (face == null)
                return null;

            FaceSignature sig = new FaceSignature();

            //----------------------------------------
            // Component bounding box
            //----------------------------------------

            object[] bodies = (object[])component.GetBodies3(
                (int)swBodyType_e.swSolidBody,
                out _);

            if (bodies == null || bodies.Length == 0)
                return sig;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;

            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (Body2 body in bodies)
            {
                double[] box = (double[])body.GetBodyBox();

                minX = Math.Min(minX, box[0]);
                minY = Math.Min(minY, box[1]);
                minZ = Math.Min(minZ, box[2]);

                maxX = Math.Max(maxX, box[3]);
                maxY = Math.Max(maxY, box[4]);
                maxZ = Math.Max(maxZ, box[5]);
            }

            //----------------------------------------
            // Surface
            //----------------------------------------

            Surface surface = face.GetSurface();

            if (surface == null)
                return sig;

            sig.SurfaceType = (swSurfaceTypes_e)surface.Identity();

            switch (sig.SurfaceType)
            {
                case swSurfaceTypes_e.PLANE_TYPE:
                {
                    double[] plane = (double[])surface.PlaneParams;

// Normal
                    sig.Normal[0] = plane[0];
                    sig.Normal[1] = plane[1];
                    sig.Normal[2] = plane[2];

                    Normalize(sig.Normal);

// Point on plane
                    double px = plane[3];
                    double py = plane[4];
                    double pz = plane[5];

                    Normalize(sig.Normal);

                    double ax = Math.Abs(sig.Normal[0]);
                    double ay = Math.Abs(sig.Normal[1]);
                    double az = Math.Abs(sig.Normal[2]);

                    double[] box = (double[])face.GetBox();

                    double dx = box[3] - box[0];
                    double dy = box[4] - box[1];
                    double dz = box[5] - box[2];

                    // Ignore the thickness direction
                    List<double> lengths = new List<double>()
                    {
                        dx,
                        dy,
                        dz
                    };

                    lengths.Sort();

                    sig.Extent1 = lengths[1];
                    sig.Extent2 = lengths[2];

                    if (ax >= ay && ax >= az)
                    {
                        sig.PlaneOffset = NormalizePlaneOffset(px, minX, maxX);
                    }
                    else if (ay >= az)
                    {
                        sig.PlaneOffset = NormalizePlaneOffset(py, minY, maxY);
                    }
                    else
                    {
                        sig.PlaneOffset = NormalizePlaneOffset(pz, minZ, maxZ);
                    }

                    Debug.WriteLine(
                        $"Normal = ({sig.Normal[0]:F3}, {sig.Normal[1]:F3}, {sig.Normal[2]:F3})");

                    Debug.WriteLine(
                        $"Plane point = ({px:F3}, {py:F3}, {pz:F3})");

                    Debug.WriteLine(
                        $"PlaneOffset = {sig.PlaneOffset:F3}");

                    break;
                }

                case swSurfaceTypes_e.CYLINDER_TYPE:
                {
                    double[] cyl = (double[])surface.CylinderParams;

                    if (cyl != null && cyl.Length >= 7)
                    {
                        sig.Axis[0] = cyl[3];
                        sig.Axis[1] = cyl[4];
                        sig.Axis[2] = cyl[5];

                        Normalize(sig.Axis);
                    }

                    break;
                }

                case swSurfaceTypes_e.CONE_TYPE:
                {
                    double[] cone = (double[])surface.ConeParams;

                    if (cone != null && cone.Length >= 6)
                    {
                        sig.Axis[0] = cone[3];
                        sig.Axis[1] = cone[4];
                        sig.Axis[2] = cone[5];

                        Normalize(sig.Axis);
                    }

                    break;
                }
            }

            return sig;
        }

        public static  VertexSignature BuildSignature(Vertex vertex)
        {
            VertexSignature sig = new VertexSignature();

            double[] pt = (double[])vertex.GetPoint();

            sig.Point[0] = pt[0];
            sig.Point[1] = pt[1];
            sig.Point[2] = pt[2];

            return sig;
        }

        public static EdgeSignature BuildSignature(Edge edge)
        {
            EdgeSignature sig = new EdgeSignature();

            Curve curve = edge.GetCurve();
            double[] curveParams = (double[])edge.GetCurveParams2();

            double startParam = curveParams[6];
            double endParam = curveParams[7];

            sig.Length = curve.GetLength3(startParam, endParam);


            sig.CurveType = (swCurveTypes_e)curve.Identity();

            Vertex start = edge.GetStartVertex();

            if (start != null)
            {
                double[] p = (double[])start.GetPoint();

                Array.Copy(p, sig.Start, 3);
            }

            Vertex end = edge.GetEndVertex();

            if (end != null)
            {
                double[] p = (double[])end.GetPoint();

                Array.Copy(p, sig.End, 3);
            }

            sig.MidPoint[0] = (sig.Start[0] + sig.End[0]) * 0.5;
            sig.MidPoint[1] = (sig.Start[1] + sig.End[1]) * 0.5;
            sig.MidPoint[2] = (sig.Start[2] + sig.End[2]) * 0.5;

            switch (sig.CurveType)
            {
                case swCurveTypes_e.LINE_TYPE:
                {
                    sig.Direction[0] =
                        sig.End[0] - sig.Start[0];

                    sig.Direction[1] =
                        sig.End[1] - sig.Start[1];

                    sig.Direction[2] =
                        sig.End[2] - sig.Start[2];

                    Normalize(sig.Direction);

                    break;
                }

                case swCurveTypes_e.CIRCLE_TYPE:
                {
                    double[] circle =
                        (double[])curve.CircleParams;

                    sig.Center[0] = circle[0];
                    sig.Center[1] = circle[1];
                    sig.Center[2] = circle[2];

                    sig.Radius = circle[6];

                    break;
                }
            }

            return sig;
        }
    }
}