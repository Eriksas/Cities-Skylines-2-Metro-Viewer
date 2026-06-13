# Schematic Map Rebuild Plan

## Direction

The Viewer no longer exposes `schematic-lite` as a normal user choice. It remains available in the CLI and tests for historical comparison, but the product direction is a rebuilt topology-first schematic mode.

Target principle:

```text
A schematic map may distort geography, but it must preserve topology and corridor meaning.
```

The current alpha recommendation remains `geographic + UsePathPoints + service family merge`. `schematic-v2` is the experimental base for the next schematic-map work.

## Phase S0 - Retire Viewer schematic-lite Entry

Status: done.

- Remove `schematic-lite` from the Viewer layout dropdown.
- Migrate saved Viewer settings from `schematic-lite` to `schematic-v2`.
- Keep CLI `--layout schematic-lite` for regression comparison and old scripts.

## Phase S1 - Schematic Contract And Diagnostics

Goal: define what a generated schematic must never break.

Checks:

- Stop order is preserved per display family.
- Every adjacent stop pair has a visible route connection.
- Interchange nodes remain shared.
- Shared corridors are represented as corridors, not accidental overlays.
- Express / skip-stop services are represented as service attributes unless a reliable canonical route exists.
- Output is stable across size presets.

Outputs:

- Schematic contract report.
- Debug SVG with station ids, family ids, corridor ids, and problem markers.
- Regression fixtures for known city cases.

## Phase S2 - Canonical Network Model

Status: initial implementation complete.

Goal: build a render-only graph that is separate from raw export data.

Inputs:

- `line.stops` for service order.
- `line.pathPoints` for physical corridor hints.
- Display family merge metadata.

Model:

- Station nodes.
- Adjacency edges.
- Corridor groups.
- Service families.
- Service variants.
- Interchange groups.
- Terminal tails.

Raw JSON must stay unchanged.

Implemented model:

- `CanonicalSchematicNetwork`
- `CanonicalStationNode`
- `CanonicalServiceFamily`
- `CanonicalServiceVariant`
- `CanonicalAdjacencyEdge`
- `CanonicalCorridorHint`
- `CanonicalInterchangeGroup`

Builder:

```text
CanonicalSchematicNetworkBuilder.Build(document)
```

Current S2 behavior:

- Resolves display/service families using the existing renderer family rules.
- Selects a canonical service route for each family.
- Records express/rapid service variants as metadata.
- Builds one adjacency edge per family/variant stop pair without mutating raw stops.
- Records exact shared adjacent-stop edges as high-confidence corridor hints.
- Records pathPoints-based physical corridor hints when two family primary paths share enough length.
- Collapses service variants into station family membership while preserving variant metadata.

S2 does not yet replace the schematic-v2 layout solver. It provides the stable graph layer that S3 should consume.

## Phase S3 - Layout Skeleton Solver

Status: initial implementation complete.

Goal: place the network skeleton before labels and styling.

Rules:

- Start from topology, not from snapped geographic coordinates.
- Assign each family a primary direction.
- Quantize segments to horizontal, vertical, and 45-degree directions.
- Preserve corridor runs before branch splits.
- Keep minimum station spacing in schematic units.
- Avoid sharp artificial zigzags on terminal tails.

This phase should replace patch-based station spacing and overlap fixes with a single coherent placement pass.

Current S3 behavior:

- `schematic-v2` now builds a `CanonicalSchematicNetwork` before layout.
- The schematic-v2 station adjacency graph uses canonical network adjacency edges when available.
- The schematic-v2 family paths use canonical service-family routes when available.
- Existing schematic-v2 spacing, sharp-angle relaxation, terminal-tail straightening, and route-guide passes still operate on the rendered station graph; this is an incremental skeleton integration, not a full solver rewrite.
- `geographic`, `schematic-lite`, exporter output, raw `line.stops`, raw `line.pathPoints`, and the JSON schema are unchanged.

## Phase S4 - Corridor Rendering Rules

Status: initial implementation complete.

Goal: make all lines visible without distorting topology.

Rules:

- Exact shared station-edge corridors render as shared corridor units.
- Physical shared corridors from `pathPoints` can guide schematic corridors only when confidence is high.
- Parallel tracks should be continuous along a corridor, not per-fragment offsets.
- Express / rapid services use a center marker and legend explanation when they are service variants.
- Station markers stay centered on final schematic nodes.

Current S4 behavior:

- Schematic-v2 shared platform overlays are generated from final render route chains, not from arbitrary schematic-lite segment patches.
- Exact shared station-edge corridors and materialized geometry route-guide corridors carry `data-schematic-v2-canonical-corridor="true"` debug attributes.
- Geometry route guides remain high-confidence and conservative; they are still experimental and do not make schematic-v2 the default output.
- Express service families continue to use the existing white center stripe marker on the canonical visible route and on supported shared-corridor overlays.

## Phase S5 - Official-map Styling

Goal: move closer to real transit-system-map language.

Work:

- Route number badges with collision avoidance.
- Stronger station and interchange symbols.
- Better line legend and service-variant legend.
- Title band / key panel polish.
- Label hierarchy for terminals, interchanges, ordinary stations, and generic stations.

## Phase S6 - Validation Loop

Goal: ensure one city-specific fix does not break other cities.

Each validation bundle should include:

- Geographic baseline.
- Current schematic-v2 output.
- Future schematic-map output.
- Diagnostics reports.
- Human review notes.

Do not promote a rebuilt schematic mode as the default until multiple real city bundles pass topology and readability review.
