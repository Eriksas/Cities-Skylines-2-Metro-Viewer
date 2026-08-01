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

## 4. Corner rounding, and why ours cannot copy theirs

**Status: implemented** (`MetroSvgRenderer.RoundedCorners.cs`, branch
`feature/corner-rounding`). This is the second rmp idea we tested and the first we
shipped.

### What we had

A route was a `<polyline>`. Its only softening was `stroke-linejoin: round`, whose
radius is fixed at half the stroke width — 7 on our 14px routes. There was not one
curve command anywhere in the output. rmp's corners are 4–7x more rounded than
that, which is most of why their lines read as *drawn* and ours read as *plotted*.

### What rmp actually does — the exact construction

Not an arc, and not the obvious quadratic. Each corner is a single **cubic** Bézier
that replaces the vertex. For vertex `P` between `A` and `N`, with factor `r`:

```
ds = min(r, |A-P|)      de = min(r, |N-P|)
S  = P + (A-P)/|A-P| * ds        E = P + (N-P)/|N-P| * de
C1 = midpoint(S, P)              C2 = midpoint(P, E)
emit:  ... L S ; C C1 C2 E ; L N ...
```

The vertex never appears in the output. The code is Yona Appletree's Apache-2.0
path-rounding snippet vendored as `src/util/pathRounding.js`, applied to the `d`
string, always with `useFractionalRadius = false`.

Two things about it that are easy to get wrong:

- **`roundCornerFactor` is a trim length, not a radius.** The fillet radius it
  produces is `R = d * tan(interior/2)`, so the same factor gives a very different
  radius at different angles. Their 10 on a 45-degree corner is `R = 24.1`
  (4.8x their 5px stroke); their 18.33 on a 90-degree corner is `R = 18.33`
  (3.7x). Porting the number as a radius gets both cases wrong.
- **Clamping is per-leg and independent** (`min(1, r/legLength)` on each side), so
  short legs give asymmetric curves, and a segment shorter than the factor keeps a
  hard kink.

### Why we could not use it as-is

rmp bends mostly at `MiscNodeType.Virtual` nodes — invisible points carrying no
marker (§1). It can cut a corner arbitrarily deep because there is nothing at the
corner to leave behind.

**Almost every corner we draw is a station.** Rendered at rmp's proportions the
result was unusable: the curve cuts inside the corner, the vertex ends up outside
the stroke, and the station marker visibly floats off the line. This was measured,
not guessed — it is the failure the first render of this feature produced.

### What we do instead: bound the deviation, not the radius

The binding constraint is *how far the curve falls from the vertex*, because that
distance is what detaches the marker. For a quadratic bridge with trim `d` and turn
angle `t`, the curve's furthest point from the vertex is `d * sin(t/2) / 2`, so we
solve for the trim instead of picking it:

```
trim = min( 2 * maxDeviation / sin(turn/2),   // marker stays on the stroke
            2.0 * strokeWidth,                // rmp-ish upper bound
            0.45 * shorter leg )              // adjacent corners cannot overrun
maxDeviation = 0.22 * strokeWidth
```

This has a property worth naming: **the trim is largest exactly at shallow turns**.
A 45-degree direction change — the 135-degree corner — gets the longest, most
sweeping curve, and a hairpin gets almost none. That is the behaviour a metro map
wants, and here it falls out of the safety constraint rather than being tuned in.

We also kept the **quadratic** rather than porting rmp's cubic, and the reason is
this same constraint. For an identical trim the two deviate from the vertex by
`0.5 * d * sin(t/2)` (quadratic) against `0.625 * d * sin(t/2)` (rmp's cubic). Under
a fixed deviation budget the quadratic therefore permits a **25% longer trim** —
a longer visible curve for the same marker safety. The shapes are otherwise within
~3% of a true circular fillet, which is invisible under a 14px stroke.

### Structural consequence: display geometry is not edit geometry

Routes are emitted as `<path>` with the rounded `d`, and carry the original point
list in `data-points`. The Viewer's bend handles, segment dragging and hit-testing
read `data-points` through a small SVGPointList shim; the tests read it too. This
is the same split rmp makes between its path generators and its node model, and it
is what let the change land without touching the layout solver or the editor's
logic.

`EnableRouteCornerRounding` (CLI: `--no-route-corner-rounding`) turns it off and
restores the exact previous geometry, which is also how the before/after renders
in this note were produced.

### Not done yet

The in-game renderer (`PortableMetroSvgRenderer`) still emits polylines. It is the
same ~60 lines of geometry but a separate corpus and a separate risk surface, so it
is deliberately a follow-up.

---

## 5. Why rmp looks smooth, and what we did about it

**Status: implemented** (`MetroSvgRenderer.OctilinearSnap.cs`,
`MetroSvgRenderer.CorridorLanes.cs`, branch `feature/octilinear-snap`, on top of
`feature/corner-rounding`).

> **Which mode.** This section and sections 6 and 7 describe passes that run only
> in the `schematic-beta` layout. `schematic-anneal` is byte-for-byte what it was
> before them -- verified against the branch point on seven networks -- because
> these passes move station positions, and a layout someone has adjusted by hand
> or already printed should not change under them without being asked. Corner
> rounding (section 4) is the exception: it is a drawing change that does not move
> anything, so it applies everywhere.

### It was never about the corners

rmp's lines read as smooth because **they only ever take eight directions**. Its
path generators cannot emit anything else: `diagonal.tsx` solves

```js
x = x2b + Math.abs(y2b - y1b) * (x2b - x1b > 0 ? -1 : 1)
y = y1b
```

so the first leg is exactly horizontal and the second has |dx| = |dy|, for any
pair of endpoints. Off-grid geometry is only reachable by hand-placing a
`Virtual` node.

Measured against that, over 22 real networks, **65% of our drawn segments were
within a degree of a 45-degree multiple**; on 肇庆 it was 43%, with the remainder
scattered across some thirty distinct angles. That scatter is what reads as
noise, and corner rounding cannot touch it -- rounding smooths the joint, not the
direction. Secondary factor: rmp users place stations on a grid, so segment
lengths are regular; ours had a length CoV of 0.44-0.99.

### The formulation

Once an edge has a heading, "this edge lies exactly on that heading" is a
**linear** condition on the two station positions -- the component of (pj - pi)
along the heading's normal vanishes. So:

```
minimise    sum |p - p0|^2          stations move as little as possible
subject to  (pj - pi) . n_e = 0     every assigned edge is exact
```

Convex, one global optimum, and automatically consistent for a station shared by
several lines. Solved as weighted least squares through the normal equations with
a Cholesky factorisation; the unit-weight "stay put" rows keep it positive
definite.

The division of labour is the useful part: **the annealer decides which heading
each run gets, the solve makes it exact.** Earlier attempts that tried to do both
at once (`docs/SCHEMATIC_LAYOUT_STUDY.md`, rounds 3-6) ran into a three-way trade
between octilinearity, clearance and simplification. That trade largely
disappears when the assignment is already made.

### Runs, not edges

Headings are assigned per *run* -- consecutive edges that already agree within 22
degrees. Snapping edge by edge was tried first and is wrong: doglegging an edge
that is 5 degrees off horizontal does not remove the 5 degrees, it folds them into
a corner, and a 200px edge gains a bulge of 200*sin(5) = 17px, wider than the
stroke. Corpus effect of the per-edge version: direction changes +72% on 肇庆
(32 -> 55) for the same 100% octilinearity the run version reaches with *fewer*
corners (32 -> 25).

### Crowding, and the weight ratio

Snapping squeezes stations together, so crowded pairs are pushed apart and the
system re-solved, up to six rounds. The interesting part is why that does not
fight the heading constraints: those rows are *relative*, so translating a whole
run sideways satisfies them exactly and costs only the anchor rows. With a large
enough weight ratio the cheapest answer to a separation request is therefore to
slide a run, not to bend an edge.

The ratio is the design, not a knob:

| constraint : anchor | 奥斯特-0728 crowding | octilinearity |
|---|---|---|
| 40 | 20.8% | broke (100% -> 84.9% on 肇庆) |
| 800 | 13.2% | held |
| 12000 | 7.5% | held |

An earlier attempt restricted separation to each station's own run direction, on
the theory that it had to stay orthogonal to the constraints. That made crowding
*worse* (肇庆 3.9% -> 11.8%): it also blocked the whole-run translation, which is
the degree of freedom that actually does the work.

### Corridors, and rmp's offset model

Snapping turns "nearly parallel" into "exactly collinear", so lines that used to
merely crowd each other end up one on top of the other -- 26% of stroke length on
肇庆. The existing parallel-corridor pass cannot catch it: that one keys on an
*edge*, a pair of stations, and these lines share a grid line while calling at
different stops.

It is also what makes the fix exact -- sharing a corridor becomes a test on
heading and intercept rather than a tolerance judgement. The mechanism is rmp's:
an offset per edge *end*, applied along that end's own normal
(`offsetFrom`/`offsetTo`), which is what lets a line ramp in and out. A lane
change on a straight run is drawn as a 45-degree jog, so the ramp stays on the
grid.

Four things that had to be right, each of which was wrong first:

1. **Parallel tolerance.** Every heading is a multiple of 45 degrees, so two
   genuinely different ones give |det| >= 0.707. A 1e-9 threshold treated
   collinear edges as intersecting and threw a vertex 42000px off canvas.
2. **Lane sign.** The lane is chosen in the corridor's canonical direction but
   applied along each edge's own normal, so two lines running the corridor in
   opposite senses need the sign flipped or they land on the same side.
3. **Heading quantisation.** Keying on the raw direction put an edge 0.08 degrees
   off horizontal in a bucket of its own, where it never got a lane.
4. **Lane order.** Ordering by line id let two lines swap sides -- 3号线 and 4号线
   on 肇庆 went from 13.6px apart to 5.3px, closer than before they were laned.
   Order by where the lines already sit across the corridor.

Same-colour families share a lane: a line and its own branch run the same track,
and giving them separate lanes draws one service as two (7号线 / 7号线支线 on
谢菲尔德).

Lane pitch is 1.35x the stroke width, and that is bounded above by the
interchange symbol: a station served by every line in the corridor keeps one
centred marker, which has to reach the outermost lanes. At radius 9.8 on a
14-wide stroke, 1.35 is the widest pitch that still touches. Going wider needs
the elongated interchange symbol -- which is exactly what `shmetro-int` is, a
rounded rect with `ry = height/2`. Guangzhou's `gzmtr-int` takes the other route:
a circle with per-line arcs and station codes, which needs a numbering system we
do not have.

### Ordering

The snap runs **after the layout and before the canvas fit**. It changes the
content bounding box, and fitting first leaves stations outside it -- measured on
谢菲尔德, content grew 131px past where the legend starts, taking 1号线's terminus
with it.

---

## 6. Density relief — drawing the centre at a larger scale

**Status: implemented** (`MetroSvgRenderer.DensityRelief.cs`).

A real network is not evenly dense. Downtown stations sit a couple of hundred
metres apart, suburban ones kilometres apart, and drawing that faithfully makes
the middle of the diagram unreadable while the rim is empty. Measured over 23
networks: local station spacing at the sparse end is **4.4x** the spacing at the
dense end, up to 6.9x.

The answer is the one the London tube map has used since 1933 — draw the centre
at a larger effective scale than the rim. Here that is a radial remap
`r' = R * (r/R)^0.65` about the station centroid weighted by local crowding, so
the dense part of the network locates the centre rather than the geometric middle
of a lopsided map.

Two properties matter beyond simplicity. It is monotone in radius, so it cannot
turn the map inside out. And it is a *global* reshaping rather than a per-edge
correction, so it does not re-open the trade the annealer already priced between
edge length, crossings and clearance — which is exactly what sank the four
per-edge attempts in round 7 of `SCHEMATIC_LAYOUT_STUDY.md`.

It runs **before** the octilinear snap. The relief deliberately moves stations
off their headings and the snap is what puts them back; the other order fights
constraints that are already established. Measured: on-heading share is unchanged
at 98.4%.

### Only where it is needed

A network whose spacing is already even gains nothing and pays the distortion
anyway. Ungated, relief took 安岭市 from 6% crowded to 47% — and 安岭市 had the
*least* spread in the corpus to begin with, 2.25x. So the pass measures the
spread first and returns the layout untouched below 3.0x.

That single gate is what turns the corpus result from a regression into a win:
crowding went 9.64% -> 10.49% ungated, and 9.64% -> 8.72% gated.

### Corpus, 23 networks, relief off -> on

| | off | on |
|---|---|---|
| sparse/dense spacing ratio | 4.38x | **3.18x** |
| crowded stations | 9.64% | **8.72%** |
| edge-length spread (CoV) | 0.83 | **0.69** |
| exactly on a heading | 98.38% | 98.45% |
| overlapping stroke | 2.83% | 3.00% |

Note the edge-length column. Round 7 failed to move it with a direct length term
at any weight; reshaping density moves it 17% as a side effect. Uniform spacing
is a *consequence* of even density, not a thing to optimise for on its own.

### Costs, stated plainly

- Relative position flips rise from 0.6-0.8% to 1.1-3.0%, and mean bearing shift
  is 3.8-6.7 degrees. That is inherent: expanding the centre distorts the rim,
  and the London map does the same.
- Six of 23 networks come out more crowded, not fewer. The mean improves; not
  every map does.
- Stroke overlap edges up 2.83% -> 3.00%.

---

## 7. Keeping plain stops out of the bends

**Status: implemented** (`MetroSvgRenderer.StationStraightRuns.cs`).

A line that changes direction *at* a station puts a small dot in the crook of the
bend. rmp never does this -- it bends at invisible `Virtual` nodes -- and it is
one of the things that separates a drawn map from a plotted one. After the
octilinear snap, 56-68% of our drawn corners still sat on a station.

### Only the plain stops

Not every one is worth moving. A corner at an **interchange** is ordinary in real
maps: the symbol is large enough to own the bend and it reads as deliberate. It is
the plain stop, a small dot wedged in the angle, that reads as an accident.
Splitting the corpus that way turned a 15-21 per map problem into a 3-10 per map
one.

### The construction, and a hard limit

Moving a corner costs a corner. The layout is exactly octilinear, so the two runs
meeting at a station intersect *at* that station; leaving it means leaving one of
the two run lines, and one inserted point cannot rejoin the other without going
off-grid. Two can: carry the arrival heading past the station, then close the
offset with a 45-degree connector that lands back on the departure run.

**A 45-degree turn has no clean version of this.** There is no heading between two
adjacent ones, so the corner cannot be cut -- the line must overshoot and come
back, leaving a bump of roughly 0.7x the stub length. Ninety-degree turns do have
a clean cut, because the diagonal bisects them. Most of our station corners are
45-degree turns, so most get the overshoot.

That is why the stub is short. At 1.6x the stroke the bump was obvious on
谢菲尔德's 东野火车站; at 0.9x it reads as a slight shoulder. This was reviewed on
renders at full map scale before shipping, not only zoomed in.

### Corpus, 23 networks

| | off | on |
|---|---|---|
| corners on a plain stop | 111 | **22** (-80%) |
| total corners | 511 | 600 (+17%) |
| networks made worse | — | **0 / 23** |
| exactly on a heading | 98.38% | 98.47% |
| overlapping stroke | 2.96% | 2.95% |

Station positions, crowding and density spread are untouched: this changes drawn
geometry only. The one metric that moves the wrong way is drawn-segment length
spread, 0.74 -> 0.84, and that is the metric counting the short connectors the
pass inserts rather than a change in how the map is laid out.

---

## 8. What to do with this

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

## 9. Viewer map elements and reusable line grammar

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
