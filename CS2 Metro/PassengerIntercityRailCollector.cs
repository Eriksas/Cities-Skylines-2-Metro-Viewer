using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using MetroDiagram.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using RouteTransportStop = Game.Routes.TransportStop;

namespace CS2_Metro
{
    internal static class PassengerIntercityRailCollector
    {
        public static PassengerIntercityCollectionResult Collect(
            EntityManager entityManager,
            NativeArray<Entity> transportLineEntities,
            StringBuilder diagnostics)
        {
            PassengerIntercityCollectionResult result = new PassengerIntercityCollectionResult();
            diagnostics.AppendLine("Passenger Intercity Rail Collector");
            diagnostics.AppendLine("- State: enabled through hidden development setting");
            diagnostics.AppendLine($"- TransportLine entities inspected: {transportLineEntities.Length}");

            for (int i = 0; i < transportLineEntities.Length; i++)
            {
                Entity lineEntity = transportLineEntities[i];
                PassengerIntercityLineCandidate candidate = InspectLine(entityManager, lineEntity);
                result.InspectedLineCount++;

                if (candidate.Classification.Included)
                {
                    result.Candidates.Add(candidate);
                    AppendCandidateDiagnostics(diagnostics, candidate, "included");
                }
                else if (candidate.Evidence.VehiclePrefabIsCargo || candidate.Evidence.StopPrefabIsCargo)
                {
                    result.CargoLineCount++;
                    AppendCandidateDiagnostics(diagnostics, candidate, "excluded");
                }
                else if (candidate.ReadFailures.Count > 0)
                {
                    result.LineReadFailureCount++;
                    AppendCandidateDiagnostics(diagnostics, candidate, "excluded");
                }
            }

            diagnostics.AppendLine("- Collector summary:");
            diagnostics.AppendLine($"  - passenger candidates: {result.Candidates.Count}");
            diagnostics.AppendLine($"  - cargo/freight lines excluded: {result.CargoLineCount}");
            diagnostics.AppendLine($"  - lines with recoverable read failures: {result.LineReadFailureCount}");
            diagnostics.AppendLine();
            return result;
        }

        public static bool IsOutsideConnectionWaypoint(
            EntityManager entityManager,
            Entity waypointEntity,
            Entity connectedStop)
        {
            HashSet<Entity> references = CollectStationReferences(entityManager, waypointEntity, connectedStop);
            return references.Any(entity => IsOutsideConnection(entityManager, entity));
        }

        private static PassengerIntercityLineCandidate InspectLine(EntityManager entityManager, Entity lineEntity)
        {
            PassengerRailEvidence evidence = new PassengerRailEvidence();
            List<string> failures = new List<string>();
            HashSet<Entity> outsideConnections = new HashSet<Entity>();

            try
            {
                if (IsValidEntity(entityManager, lineEntity)
                    && entityManager.HasComponent<PrefabRef>(lineEntity))
                {
                    Entity linePrefab = entityManager.GetComponentData<PrefabRef>(lineEntity).m_Prefab;
                    evidence.LinePrefabIsTrain = IsValidEntity(entityManager, linePrefab)
                        && entityManager.HasComponent<TransportLineData>(linePrefab)
                        && entityManager.GetComponentData<TransportLineData>(linePrefab).m_TransportType == TransportType.Train;
                }
            }
            catch (Exception ex)
            {
                failures.Add("line-prefab:" + FormatException(ex));
            }

            try
            {
                if (IsValidEntity(entityManager, lineEntity)
                    && entityManager.HasBuffer<VehicleModel>(lineEntity))
                {
                    DynamicBuffer<VehicleModel> models = entityManager.GetBuffer<VehicleModel>(lineEntity, true);
                    for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
                    {
                        Entity vehiclePrefab = models[modelIndex].m_PrimaryPrefab;
                        if (!IsValidEntity(entityManager, vehiclePrefab))
                        {
                            continue;
                        }

                        if (entityManager.HasComponent<CargoTransportVehicleData>(vehiclePrefab))
                        {
                            evidence.VehiclePrefabIsCargo = true;
                        }

                        if (entityManager.HasComponent<PublicTransportVehicleData>(vehiclePrefab)
                            && entityManager.GetComponentData<PublicTransportVehicleData>(vehiclePrefab).m_TransportType == TransportType.Train)
                        {
                            evidence.VehiclePrefabIsTrain = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add("vehicle-model:" + FormatException(ex));
            }

            try
            {
                if (IsValidEntity(entityManager, lineEntity)
                    && entityManager.HasBuffer<RouteWaypoint>(lineEntity))
                {
                    DynamicBuffer<RouteWaypoint> waypoints = entityManager.GetBuffer<RouteWaypoint>(lineEntity, true);
                    for (int waypointIndex = 0; waypointIndex < waypoints.Length; waypointIndex++)
                    {
                        Entity waypoint = waypoints[waypointIndex].m_Waypoint;
                        Entity connected = ReadConnectedEntity(entityManager, waypoint);
                        if (!IsValidEntity(entityManager, connected))
                        {
                            continue;
                        }

                        if (entityManager.HasComponent<TrainStop>(connected))
                        {
                            evidence.TrainStopCount++;
                        }

                        if (entityManager.HasComponent<PrefabRef>(connected))
                        {
                            Entity stopPrefab = entityManager.GetComponentData<PrefabRef>(connected).m_Prefab;
                            if (IsValidEntity(entityManager, stopPrefab)
                                && entityManager.HasComponent<CargoTransportStationData>(stopPrefab))
                            {
                                evidence.CargoStopCount++;
                                evidence.StopPrefabIsCargo = true;
                            }
                        }

                        foreach (Entity reference in CollectStationReferences(entityManager, waypoint, connected))
                        {
                            if (IsOutsideConnection(entityManager, reference))
                            {
                                outsideConnections.Add(reference);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add("route-waypoints:" + FormatException(ex));
            }

            evidence.WaypointHasTrainStop = evidence.TrainStopCount > 0;
            evidence.OutsideConnectionReferenceCount = outsideConnections.Count;
            PassengerRailClassification classification = PassengerIntercityRailClassifier.Classify(evidence);

            return new PassengerIntercityLineCandidate(
                lineEntity,
                evidence,
                classification,
                failures);
        }

        private static HashSet<Entity> CollectStationReferences(
            EntityManager entityManager,
            Entity waypointEntity,
            Entity connectedStop)
        {
            HashSet<Entity> references = new HashSet<Entity>();
            AddIfValid(entityManager, references, waypointEntity);
            AddIfValid(entityManager, references, connectedStop);
            AddIfValid(entityManager, references, ReadOwner(entityManager, waypointEntity));
            AddIfValid(entityManager, references, ReadOwner(entityManager, connectedStop));
            AddIfValid(entityManager, references, ReadParent(entityManager, waypointEntity));
            AddIfValid(entityManager, references, ReadParent(entityManager, connectedStop));

            try
            {
                if (IsValidEntity(entityManager, connectedStop)
                    && entityManager.HasComponent<RouteTransportStop>(connectedStop))
                {
                    AddIfValid(
                        entityManager,
                        references,
                        entityManager.GetComponentData<RouteTransportStop>(connectedStop).m_AccessRestriction);
                }
            }
            catch
            {
            }

            return references;
        }

        private static void AppendCandidateDiagnostics(
            StringBuilder diagnostics,
            PassengerIntercityLineCandidate candidate,
            string disposition)
        {
            diagnostics.AppendLine($"- Line {FormatEntity(candidate.LineEntity)}: {disposition}");
            diagnostics.AppendLine($"  - reason: {candidate.Classification.Reason}");
            diagnostics.AppendLine($"  - evidence: {(candidate.Classification.Evidence.Count == 0 ? "none" : string.Join(", ", candidate.Classification.Evidence))}");
            if (candidate.ReadFailures.Count > 0)
            {
                diagnostics.AppendLine($"  - recoverable read failures: {string.Join(" | ", candidate.ReadFailures)}");
            }
        }

        private static Entity ReadConnectedEntity(EntityManager entityManager, Entity waypointEntity)
        {
            try
            {
                return IsValidEntity(entityManager, waypointEntity)
                    && entityManager.HasComponent<Connected>(waypointEntity)
                    ? entityManager.GetComponentData<Connected>(waypointEntity).m_Connected
                    : Entity.Null;
            }
            catch
            {
                return Entity.Null;
            }
        }

        private static Entity ReadOwner(EntityManager entityManager, Entity entity)
        {
            try
            {
                return IsValidEntity(entityManager, entity)
                    && entityManager.HasComponent<Owner>(entity)
                    ? entityManager.GetComponentData<Owner>(entity).m_Owner
                    : Entity.Null;
            }
            catch
            {
                return Entity.Null;
            }
        }

        private static Entity ReadParent(EntityManager entityManager, Entity entity)
        {
            try
            {
                return IsValidEntity(entityManager, entity)
                    && entityManager.HasComponent<Attached>(entity)
                    ? entityManager.GetComponentData<Attached>(entity).m_Parent
                    : Entity.Null;
            }
            catch
            {
                return Entity.Null;
            }
        }

        private static bool IsOutsideConnection(EntityManager entityManager, Entity entity)
        {
            try
            {
                return IsValidEntity(entityManager, entity)
                    && (entityManager.HasComponent<Game.Objects.OutsideConnection>(entity)
                        || entityManager.HasComponent<Game.Net.OutsideConnection>(entity));
            }
            catch
            {
                return false;
            }
        }

        private static void AddIfValid(
            EntityManager entityManager,
            HashSet<Entity> entities,
            Entity entity)
        {
            if (IsValidEntity(entityManager, entity))
            {
                entities.Add(entity);
            }
        }

        private static bool IsValidEntity(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null && entity.Index >= 0 && entityManager.Exists(entity);
        }

        private static string FormatEntity(Entity entity)
        {
            return entity.Index + ":" + entity.Version;
        }

        private static string FormatException(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    internal sealed class PassengerIntercityCollectionResult
    {
        public List<PassengerIntercityLineCandidate> Candidates { get; } = new List<PassengerIntercityLineCandidate>();

        public int InspectedLineCount { get; set; }

        public int CargoLineCount { get; set; }

        public int LineReadFailureCount { get; set; }
    }

    internal sealed class PassengerIntercityLineCandidate
    {
        public PassengerIntercityLineCandidate(
            Entity lineEntity,
            PassengerRailEvidence evidence,
            PassengerRailClassification classification,
            IReadOnlyList<string> readFailures)
        {
            LineEntity = lineEntity;
            Evidence = evidence;
            Classification = classification;
            ReadFailures = readFailures;
        }

        public Entity LineEntity { get; }

        public PassengerRailEvidence Evidence { get; }

        public PassengerRailClassification Classification { get; }

        public IReadOnlyList<string> ReadFailures { get; }
    }
}
