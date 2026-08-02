using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    public class EdgeSignature
    {
        /// <summary>
        /// Line, circle, ellipse, spline, etc.
        /// </summary>
        public swCurveTypes_e CurveType;

        /// <summary>
        /// Total edge length.
        /// </summary>
        public double Length;

        /// <summary>
        /// Start vertex position.
        /// </summary>
        public double[] Start = new double[3];

        /// <summary>
        /// End vertex position.
        /// </summary>
        public double[] End = new double[3];

        /// <summary>
        /// Midpoint of the edge.
        /// </summary>
        public double[] MidPoint = new double[3];

        /// <summary>
        /// Unit direction vector for linear edges.
        /// </summary>
        public double[] Direction = new double[3];

        /// <summary>
        /// Circle/arc center.
        /// </summary>
        public double[] Center = new double[3];

        /// <summary>
        /// Radius for circles/arcs.
        /// </summary>
        public double Radius;
    }
}