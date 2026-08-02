using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    public class FaceSignature
    {
        public swSurfaceTypes_e SurfaceType;

        public double[] Normal = new double[3];

        public double PlaneOffset;

        public double[] Axis = new double[3];

        // NEW
        public double Extent1;
        public double Extent2;
    }
}