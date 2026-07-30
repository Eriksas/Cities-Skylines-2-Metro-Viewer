# Rail-map domain notes, from railmapgen/rmp

Reference notes for CS2 Metro Diagram, taken from
[railmapgen/rmp](https://github.com/railmapgen/rmp) ("Rail Map Painter") — a mature
TypeScript tool for drawing rail maps in the house styles of ~25 real systems
(Shanghai, Guangzhou, Beijing, MTR, London, Tokyo, Singapore, Berlin, …).

**Why it is worth reading.** It is not an algorithm to copy — rmp is a *human editor*
and we *generate* layouts, which is a different problem. What it has that we do not is a
vocabulary: twenty-odd real systems' conventions, factored into named types. Where we
have argued about how to draw a tram, rmp already carries three different real answers
and says which city each belongs to.

**What NOT to take from it.** Its React components, its per-city pixel constants, and
anything that only makes sense when a human is placing elements by hand. Those are
called out below where relevant.

---

## 1. The finding that matters most: an edge may bend between stations

This one contradicts an assumption baked into our schematic layout, and it invalidates
part of `docs/SCHEMATIC_LAYOUT_STUDY.md`.

**What rmp does.** A line connects *nodes*, and a node is either a station or a
**misc node** — including `MiscNodeType.Virtual`, which renders as a small cross with
`className="removeMe"` and is **stripped on export**
(`src/components/svgs/nodes/virtual.tsx`). It exists so a line can change direction at a
point that is not a station.

Further, even a single edge between two adjacent stations is not a straight segment.
Both non-trivial path generators build **a three-point path with exactly one
intermediate corner** — `makeSharpTurnPath(p1, corner, p2)`:

- `LinePathType.Perpendicular` (`paths/perpendicular.tsx`): the L. From p1 go
  horizontal to (x2, y1), then vertical to p2 — or the mirror image.
- `LinePathType.Diagonal` (`paths/diagonal.tsx`): the classic metro-map shape. Go
  horizontal or vertical from p1, then leave at 45° to reach p2. The corner is computed
  from the endpoints.

Both take `startFrom: 'from' | 'to'` — which of the two orientations to use — plus
`offsetFrom`/`offsetTo` (parallel-corridor offsets) and `roundCornerFactor` (corner
radius).

**What we do.** In `schematic-anneal` a route is a polyline through station positions.
A direction change can only happen **at a station**. Nothing else is expressible.

**Why that matters.** Our layout study forced octilinearity as a constraint on the
station positions themselves — for every edge, the component perpendicular to the run
direction had to vanish. That removes a degree of freedom per edge, and Round 6 measured
the consequence: octilinearity, station clearance and simplification became a three-way
trade where you could have any two.

If an edge may carry one corner, **that constraint disappears**. Any two points can be
joined by two octilinear segments: (0,0)→(7,3) is `(0,0)→(7,0)→(7,3)`, or
`(0,0)→(4,0)→(7,3)`. So octilinearity becomes a property of the *drawing*, not of the
*positions*, and it stops competing with clearance at all.

The objective then changes shape:

> Old: place stations so that the straight lines between them are octilinear.
> New: place stations for clarity and honesty; route each edge octilinearly with as few
> corners as possible — an edge whose endpoints happen to align already needs **zero**.

That is a strictly better formulation and it is what the study was missing. Note the
metric consequence too: our `turns` counts direction changes at stations, and in this
model a corner can also sit mid-edge, so the metric would need to count both.

**Status: tested. Half confirmed, half wrong.**

The dogleg was implemented and measured on six real networks
(`scratchpad/lab/dogleg.py`, `dogtest.py`). Metrics are taken on the polyline the
reader actually sees, so a mid-edge corner counts as a direction change:

```
layout                              turns  octi%   dev°  flip%  tight%  coinc%
anneal (shipped, bends at stations)  55.5   93.0   17.9   5.80     8.9    0.29
free placement + dogleg on every edge 223.2 100.0   23.0   5.67    16.3    0.25
shipped positions + dogleg REPAIR    64.2  100.0   17.9   5.80     8.9    0.29
```

- **Confirmed:** octilinearity becomes 100% by construction and stops constraining
  station positions at all.
- **Wrong:** it is not free. Using the dogleg as the default gives every edge a
  corner — direction changes went 55.5 -> 223.2, a 4x regression, and station
  quality got worse too because placement then has no reason to align anything.

The error in the hypothesis was assuming rmp's dogleg is a routing default. It is
an **escape hatch**: a human places stations so most edges are already aligned and
need no corner, and the dogleg absorbs the few that are not. Used that way —
keep the existing layout, dogleg only the edges that are not already octilinear —
the result is **100% octilinearity for +16% direction changes, with station
positions untouched, so every other metric is identical**. It is a pure rendering
change; the layout solver is not involved.

**But look before shipping it.** Rendered on Sheffield
(`scratchpad/lab/dogleg-compare.png`), the repair is not unconditionally better.
Where the misalignment is small it converts a sloppy 3-degree slant into a crisp
step, which is a clear win. Where the misalignment is large the corner lands far
from the direct line and the edge visibly bulges — one cyan run gains a detour
that does not exist in reality, and a blue run gains a narrow hairpin. A reader
reads those as real geography.

So the rule to test next is a **bounded** repair: apply the dogleg only when the
edge's angular error is below a threshold, so the corner stays near the straight
path, and leave larger errors to the layout. That threshold is unmeasured.

---

## 2. Structural separation: path is not style

`LinePathType` (Simple, Diagonal, Perpendicular, RotatePerpendicular, RayGuided) is
**orthogonal** to line style (`single-color`, `bjsubway-tram`, `shinkansen`, …). One says
how the line gets from A to B; the other says what it looks like. Every line carries both.

We conflate the two: our corridor code decides geometry and appearance together. The
separation is worth adopting on its own merits — it is what let rmp add ~40 line styles
without touching routing.

---

## 3. The type vocabulary, as a checklist of what a rail map can say

Listed because each entry is a distinction some real system found necessary. Ours are
marked.

### Stations (`src/components/svgs/stations/`)

Every city ships a `-basic` and an `-int` pair — the plain stop and the interchange are
*different symbols*, not the same symbol at two sizes. Specials worth knowing:

| type | meaning | us |
|---|---|---|
| `-basic` / `-int` per city | plain stop vs interchange, distinct geometry | we scale one circle |
| `shmetro-osysi` | **out-of-system interchange** — you must exit the fare gates and re-enter | we have `EnableVirtualTransferHints`, same idea |
| `guangdong-intercity-railway` | intercity rail station, its own symbol | IG2 territory |
| `shanghai-suburban-railway` | suburban/regional rail station | — |
| `jr-east-important` | major station gets a larger/heavier symbol | missing: we rank only by line count |
| `london-river-services-interchange` | interchange with a non-rail mode | — |
| `secondary-name` | bilingual label handling, sized and placed as a unit | we render CJK + Latin |

### Line styles (`src/components/svgs/lines/styles/`)

Grouped by what they encode:

- **Mode**: `bjsubway-tram`, `mtr-light-rail`, `lrt-single-color` (tram/LRT — three
  different real answers); `shinkansen`, `china-railway`,
  `guangdong-intercity-railway`, `shanghai-suburban-railway`, `london-rail`
  (mainline/intercity — four); `london-ifs-cloud-cable-car`, `mrt-sentosa-express`.
- **Operational state**: `bjsubway-dotted` and `mrt-under-construction` (planned / under
  construction); `mtr-race-day` (event-only service).
- **Fare geography**: `mtr-paid-area` / `mtr-unpaid-area`,
  `chengdurt-outside-fare-gates` — the map tells you where the gateline is.
- **Connections that are not track**: `london-tube-10-min-walk` (walking transfer),
  `london-tube-internal-int`, `gzmtr-virtual-int`, `shmetro-virtual-int`.
- **Topology**: `gzmtr-loop`, `chongqingrt-loop` — **loop lines get their own style.**
  Directly relevant: our layout maths is undefined on a closed chain (the
  Douglas-Peucker chord of a loop is the zero vector).
- **Service structure**: `dual-color` (one line drawn in two colours, for branches or
  through-services), `london-sandwich`, `jr-east-single-color-pattern`.
- **Context**: `river` — water is drawn because it is the strongest landmark a reader
  has for orienting an otherwise abstract diagram.

### Misc nodes (`src/components/svgs/nodes/`)

- **Line badges**, per city, in numeric and text variants (~15 cities). The badge is a
  *node you place*, not an automatic annotation — because where the badge goes is a
  design decision. We place ours automatically.
- `facilities` (airport and similar), `london-arrow` (direction/continuation arrow —
  the same device we adopted for outside connections), `mrt-dest-num`,
  `i18n-text`, `text`, `image`, `fill`, `master` (user-defined composite).

---

## 4. What to do with this

Ordered by value, and none of it is urgent:

1. **Test the mid-edge corner hypothesis** (§1). It is the only item here that could
   change the layout algorithm, and it is cheap to falsify.
2. **Split path from style** (§2) if the renderer is ever refactored — it is what makes
   a style library affordable.
3. **Give interchanges their own symbol** rather than a bigger circle. Universal across
   every city rmp models; we are the odd one out.
4. **Loop lines** — when we return to them, note that real systems treat a loop as its
   own line style, not as a chain that happens to close.
5. When tram/intercity comes back, start from a named real convention rather than
   inventing one. We invented a grey hatched band; `china-railway` and
   `guangdong-intercity-railway` are two real answers to that exact question, and
   `shinkansen` a third.

**Do not** import their per-city constants wholesale. Our maps come from a game, not a
real city; a player's network has no house style, and adopting Shanghai's exact station
geometry would be cargo-culting. Take the distinctions, not the pixels.

---

## 5. Viewer map elements and reusable line grammar

The Viewer now applies the same separation to manual cartographic content:

- facility symbols and information boxes are sidecar objects, independent of
  the transport topology;
- routes remain generated from the export document;
- manual objects can be selected and positioned without changing service data;
- station nodes can be box-selected for group cartographic adjustments.

Three RMP concepts are especially useful for future line polish:

1. A path family describes geometry (`simple`, diagonal, perpendicular).
2. A style is an ordered set of layers, each with its own width, color, opacity,
   cap, and dash/gap pattern.
3. Rounded line-line corners are a path operation, not a station-position
   mutation.

This is a design vocabulary, not a source dependency. RMP is GPL-3.0; CS2 Metro
Diagram does not copy its implementation or bundled assets.
