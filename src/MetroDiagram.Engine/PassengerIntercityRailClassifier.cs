using System;
using System.Collections.Generic;

namespace MetroDiagram.Engine
{
    public sealed class PassengerRailEvidence
    {
        public bool LinePrefabIsTrain { get; set; }

        public bool VehiclePrefabIsTrain { get; set; }

        public bool WaypointHasTrainStop { get; set; }

        public bool VehiclePrefabIsCargo { get; set; }

        public bool StopPrefabIsCargo { get; set; }

        public int TrainStopCount { get; set; }

        public int CargoStopCount { get; set; }

        public int OutsideConnectionReferenceCount { get; set; }
    }

    public sealed class PassengerRailClassification
    {
        public PassengerRailClassification(bool included, string reason, IReadOnlyList<string> evidence)
        {
            Included = included;
            Reason = reason ?? string.Empty;
            Evidence = evidence ?? Array.Empty<string>();
        }

        public bool Included { get; }

        public string Reason { get; }

        public IReadOnlyList<string> Evidence { get; }
    }

    public static class PassengerIntercityRailClassifier
    {
        public static PassengerRailClassification Classify(PassengerRailEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException(nameof(evidence));
            }

            List<string> details = new List<string>();
            Add(details, evidence.LinePrefabIsTrain, "line-prefab:train");
            Add(details, evidence.VehiclePrefabIsTrain, "vehicle-prefab:passenger-train");
            Add(details, evidence.WaypointHasTrainStop, "waypoint:train-stop");
            Add(details, evidence.VehiclePrefabIsCargo, "vehicle-prefab:cargo");
            Add(details, evidence.StopPrefabIsCargo, "stop-prefab:cargo");

            if (evidence.TrainStopCount > 0)
            {
                details.Add("train-stop-count:" + evidence.TrainStopCount);
            }

            if (evidence.CargoStopCount > 0)
            {
                details.Add("cargo-stop-count:" + evidence.CargoStopCount);
            }

            if (evidence.OutsideConnectionReferenceCount > 0)
            {
                details.Add("outside-connection-references:" + evidence.OutsideConnectionReferenceCount);
            }

            // Train and cargo services share TransportType.Train. Explicit cargo
            // components therefore override every positive train-mode signal.
            if (evidence.VehiclePrefabIsCargo || evidence.StopPrefabIsCargo)
            {
                return new PassengerRailClassification(
                    false,
                    "excluded-cargo-or-freight",
                    details);
            }

            if (evidence.LinePrefabIsTrain
                || evidence.VehiclePrefabIsTrain
                || evidence.WaypointHasTrainStop)
            {
                return new PassengerRailClassification(
                    true,
                    "included-passenger-intercity-train",
                    details);
            }

            return new PassengerRailClassification(
                false,
                "excluded-no-passenger-train-evidence",
                details);
        }

        private static void Add(List<string> details, bool condition, string value)
        {
            if (condition)
            {
                details.Add(value);
            }
        }
    }
}
