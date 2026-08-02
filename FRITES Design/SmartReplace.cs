using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using FRITES.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace FRITES_Design
{
    public static class SmartReplace
    {
        
        public static List<RecordedMate> CaptureMates(
            ModelDoc2 model,
            Component2 targetComponent)
        {
            var recordedMates = new List<RecordedMate>();

            ModelDocExtension ext = model.Extension;

            Feature feature = model.FirstFeature();

            while (feature != null)
            {
                if (feature.GetTypeName2() != "MateGroup")
                {
                    feature = feature.GetNextFeature();
                    continue;
                }

                Feature mateFeature = feature.GetFirstSubFeature();

                while (mateFeature != null)
                {
                    IMate2 mate = mateFeature.GetSpecificFeature2() as IMate2;

                    if (mate != null)
                    {
                        bool rotationLocked = false;

                        if ((swMateType_e)mate.Type == swMateType_e.swMateCONCENTRIC)
                        {
                            var def = (IConcentricMateFeatureData)mateFeature.GetDefinition();
                            rotationLocked = def.LockRotation;
                        }
                        
                        RecordedMate recorded = new RecordedMate
                        {
                            OriginalFeature = mateFeature,

                            Type = (swMateType_e)mate.Type,
                            Alignment = (swMateAlign_e)mate.Alignment,

                            Flipped = mate.Flipped,
                            RotationLocked = rotationLocked,
                            CanBeFlipped = mate.CanBeFlipped,

                            MaximumVariation = mate.MaximumVariation,
                            MinimumVariation = mate.MinimumVariation
                        };

                        bool referencesTarget = false;

                        int count = mate.GetMateEntityCount();

                        for (int i = 0; i < count; i++)
                        {
                            MateEntity2 mateEntity = mate.MateEntity(i);

                            if (mateEntity == null)
                                continue;

                            Entity entity = mateEntity.Reference as Entity;

                            if (entity == null)
                                continue;

                            RecordedMateEntity recordedEntity =
                                new RecordedMateEntity();

                            recordedEntity.Component =
                                mateEntity.ReferenceComponent;

                            recordedEntity.Entity = entity;

                            recordedEntity.IsReplacementEntity =
                                mateEntity.ReferenceComponent == targetComponent;

                            if (recordedEntity.IsReplacementEntity)
                            {
                                referencesTarget = true;

                                object specific = entity.GetSafeEntity();

                                if (specific is Face2 face)
                                {
                                    recordedEntity.GeometryType =
                                        RecordedEntityType.Face;

                                    recordedEntity.FaceSignature = SignatureBuilder.BuildSignature(
                                            mateEntity.ReferenceComponent,
                                            face);
                                }
                                else if (specific is Edge edge)
                                {
                                    recordedEntity.GeometryType =
                                        RecordedEntityType.Edge;

                                    recordedEntity.EdgeSignature = SignatureBuilder.BuildSignature(edge);
                                }
                                else if (specific is Vertex vertex)
                                {
                                    recordedEntity.GeometryType =
                                        RecordedEntityType.Vertex;

                                    recordedEntity.VertexSignature = SignatureBuilder.BuildSignature(vertex);
                                }
                            }
                            else
                            {
                                recordedEntity.PersistReference =
                                    (byte[])ext.GetPersistReference3(entity);
                            }

                            recorded.Entities.Add(recordedEntity);
                        }

                        if (referencesTarget)
                        {
                            // Capture dimension for distance / angle mates
                            DisplayDimension disp =
                                mate.DisplayDimension2[0];

                            if (disp != null)
                            {
                                Dimension dim =
                                    (Dimension)disp.GetDimension();

                                double[] value =
                                    (double[])dim.GetSystemValue3(
                                        (int)swInConfigurationOpts_e.swThisConfiguration,
                                        null);

                                if (value != null && value.Length > 0)
                                    recorded.Dimension = value[0];
                            }

                            recordedMates.Add(recorded);
                        }
                    }

                    mateFeature = mateFeature.GetNextSubFeature();
                }

                feature = feature.GetNextFeature();
            }

            return recordedMates;
        }

        

        public static Entity ResolveEntity(
            ModelDoc2 model,
            Component2 replacementComponent,
            RecordedMateEntity recorded)
        {
            if (recorded.IsReplacementEntity)
            {
                switch (recorded.GeometryType)
                {
                    case RecordedEntityType.Face:
                    {
                        Face2 face = GeometryMatcher.FindBestFace(
                            replacementComponent,
                            recorded.FaceSignature);

                        double[] box = (double[])face.GetBox();

                        return face as Entity;
                    }

                    case RecordedEntityType.Edge:
                    {
                        Edge edge = GeometryMatcher.FindBestEdge(
                            replacementComponent,
                            recorded.EdgeSignature);

                        return edge as Entity;
                    }

                    case RecordedEntityType.Vertex:
                    {
                        Vertex vertex = GeometryMatcher.FindBestVertex(
                            replacementComponent,
                            recorded.VertexSignature);

                        return vertex as Entity;
                    }

                    default:
                        return null;
                }
            }

            int errors;

            return model.Extension.GetObjectByPersistReference3(
                recorded.PersistReference,
                out errors) as Entity;
        }

        public static bool DeleteMate(
            ModelDoc2 model,
            RecordedMate mate)
        {
            model.ClearSelection2(true);

            if (!mate.OriginalFeature.Select2(false, 0))
                return false;

            return model.Extension.DeleteSelection2(
                (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
        }

        public static bool RecreateMate(
            AssemblyDoc assembly,
            ModelDoc2 model,
            Component2 replacementComponent,
            RecordedMate mate)
        {
            Debug.WriteLine("========================================");
            Debug.WriteLine($"Recreating {(swMateType_e)mate.Type}");

            if (mate.Entities.Count != 2)
                return false;

            //----------------------------------------
            // Resolve entities
            //----------------------------------------

            Entity entity1 = ResolveEntity(
                model,
                replacementComponent,
                mate.Entities[0]);

            Entity entity2 = ResolveEntity(
                model,
                replacementComponent,
                mate.Entities[1]);

            if (entity1 == null || entity2 == null)
            {
                Debug.WriteLine("Failed to resolve entities.");
                return false;
            }

            //----------------------------------------
            // Delete original mate
            //----------------------------------------

            if (!DeleteMate(model, mate))
            {
                Debug.WriteLine("Failed to delete mate.");
                return false;
            }

            //----------------------------------------
            // Select entities
            //----------------------------------------

            model.ClearSelection2(true);

            if (!entity1.Select4(false, null) ||
                !entity2.Select4(true, null))
            {
                model.ClearSelection2(true);
                return false;
            }

            //----------------------------------------
            // Determine mate value
            //----------------------------------------

            double mateValue = 0.0;

            switch ((swMateType_e)mate.Type)
            {
                case swMateType_e.swMateDISTANCE:
                case swMateType_e.swMateANGLE:
                    mateValue = mate.Dimension;
                    break;
            }

            //----------------------------------------
            // Supported mate types
            //----------------------------------------

            switch ((swMateType_e)mate.Type)
            {
                case swMateType_e.swMateCOINCIDENT:
                case swMateType_e.swMateCONCENTRIC:
                case swMateType_e.swMatePARALLEL:
                case swMateType_e.swMatePERPENDICULAR:
                case swMateType_e.swMateTANGENT:
                case swMateType_e.swMateDISTANCE:
                case swMateType_e.swMateANGLE:
                case swMateType_e.swMateLOCK:
                case swMateType_e.swMateSYMMETRIC:

                    break;

                default:

                    Debug.WriteLine($"Unsupported mate type: {(swMateType_e)mate.Type}");
                    model.ClearSelection2(true);
                    return false;
            }

            //----------------------------------------
            // Create mate
            //----------------------------------------

            int errors;

            Mate2 newMate = assembly.AddMate5(
                (int) mate.Type,
                (int) mate.Alignment,
                mate.Flipped, // Flip
                mateValue,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                false,
                mate.RotationLocked,
                0,
                out errors);

            model.ClearSelection2(true);

            Debug.WriteLine($"AddMate5 returned {(newMate != null)}");
            Debug.WriteLine($"ErrorStatus = {errors}");

            if (newMate == null)
                return false;

            model.EditRebuild3();

            return true;
        }
    }
}