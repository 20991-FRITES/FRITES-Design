using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    public class RecordedMate
    {
        // Mate properties
        public swMateType_e Type;
        public swMateAlign_e Alignment;

        public bool Flipped;
        public bool CanBeFlipped;
        public bool RotationLocked;

        // Distance / Angle mates
        public double Dimension;

        public double MinimumVariation;
        public double MaximumVariation;

        // Keep temporarily so we can delete it
        public Feature OriginalFeature;

        // The entities that define this mate
        public List<RecordedMateEntity> Entities =
            new List<RecordedMateEntity>();
    }
}