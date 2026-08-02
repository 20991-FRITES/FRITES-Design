using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    public class RecordedMateEntity
    {
        public Component2 Component;

        // Original entity
        public Entity Entity;

        // Which kind of entity?
        public swSelectType_e SelectType;

        // Is this entity on the component being replaced?
        public bool IsReplacementEntity;

        // Persistent reference for entities we are NOT replacing
        public byte[] PersistReference;

        // Geometry signatures for replacement entities
        public FaceSignature FaceSignature;

        public EdgeSignature EdgeSignature;

        public VertexSignature VertexSignature;
        
        public RecordedEntityType GeometryType;
    }

    public enum RecordedEntityType
    {
        Face,
        Edge,
        Vertex
    }
}