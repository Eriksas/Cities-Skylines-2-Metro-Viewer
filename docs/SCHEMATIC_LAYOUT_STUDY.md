# Schematic layout: what we have, why geographic still reads better, and what to try

Experiment log, 2026-07-28. No product code was changed for this study. The
baseline measured is `git archive HEAD` built separately, so the numbers describe
the shipped algorithm rather than any working-tree edits. The experiment code
lives outside the repository; see "Reproducing" at the end.

---

## How this was measured

Five metrics, chosen to match what a player actually perceives rather than what
the current cost function happens to optimize. All are computed from the
*rendered SVG* of the real pipeline, over 6–28 real city exports plus samples.

| metric | meaning | good |
|---|---|---|
| **turns** | direction changes >12° along a line's station polyline | low |
| **cov** | coefficient of variation of edge length — spacing evenness | low |
| **dev°** | mean angle between an edge's drawn bearing and its true bearing | low |
| **flip%** | station pairs whose N/S or E/W relationship is reversed | low |
| **octi%** | edges within 5° of a multiple of 45° | high |
| **crowd%** | station pairs closer than half the median edge length | low |

`dev°` and `flip%` together are the **lie tax**: how much the diagram misleads
about where things are. `turns`, `octi%`, `cov`, `crowd%` are what the lie is
supposed to buy.

---

## Result 1 — the shipped schematic does not schematize

Mean over 28 networks, geographic vs shipped schematic-anneal:

```
                turns    cov   dev°  flip%
geographic       51.1   0.89    0.0   0.00
schematic-anneal 53.4   0.64   23.0   6.41
```

**The schematic has slightly MORE direction changes than the geographic map.**
It pays 23° of bearing distortion and reverses the relative position of 6.4% of
station pairs, and in exchange the lines are not one bit simpler to follow — only
the spacing is more even (cov 0.89 → 0.64).

That is the whole answer to "why does geographic look better". Geographic tells
no lies at all. A schematic is only worth its lie tax if it makes the network
substantially easier to read, and ours currently does not. The owner's instinct
that "地理图描绘其实挺不错的，就差我们去怎么规划" is exactly right, and the
missing thing is literally the 规划 — the planning of line structure.

On the narrower 6-city set used for the rest of the experiments, adding
octilinearity to the picture shows what the algorithm *is* buying:

```
                turns    cov   dev°  flip%  octi%  crowd%
geographic       67.0   0.71    0.0   0.00   39.4    1.55
anneal (shipped) 60.3   0.64   20.4   6.33   91.5    0.68
```

So the shipped algorithm is genuinely strong at two things — **octilinearity
(91.5%)** and **not letting stations collide (0.68%, better than reality)**. It
is weak at the third: turning a tangle into a small number of long, legible runs.

---

## Result 2 — the parameters cannot be tuned out of this

Grid size sweep on four cities (turns):

```
city                  GEO   g24   g32*  g48   g64   g96  g128
奥斯特-0717            11     6    12     5    13    17    20
谢菲尔德-0628           82    75    70   116   129   114    97
肇庆-0607              97    86    78    89   100   116    98
安岭市-0716             34    26    32    27    33    34    36
                                  (* = shipped default)
```

The response is not monotonic and not even smooth: 谢菲尔德 jumps 70 → 116 turns
between grid 32 and 48, while 奥斯特 drops 12 → 5 across the same step. Adjacent
parameter values differ by more than the parameter plausibly should — the signal
is dominated by search noise, which is the same instability found earlier in the
annealer (a single trajectory whose outcome swings ±30% under perturbations too
small to see). **Further tuning of this formulation is tuning on noise.**

---

## Result 3 — the structural cause

The shipped algorithm is **station-first**: it puts every station near its true
position on a grid and hopes the edges come out octilinear and collinear. Its
move set is one station at a time within ±2 grid cells, and its cost function
scores each *vertex* and each *edge* independently. Nothing in it represents "these
six stations form one straight run".

A real metro map is drawn **line-first**: you decide that a line consists of a
handful of straight octilinear runs, then space the stations along them. That
decision is what removes turns, and it is a decision about a *sequence* of
stations, which no single-station move can make.

This also explains the staircase effect. A gently curving real line, snapped
station-by-station to 8 directions, becomes a staircase — and a staircase has
*more* direction changes than the smooth curve it replaced. That is precisely how
a schematic ends up with more turns than the geography it came from.

---

## Result 4 — a line-first prototype, and two wrong turns on the way

Prototype: simplify each line's station polyline (Douglas-Peucker) into a few
runs, snap each run to one of 8 directions, then solve all station positions at
once. ~120 lines of Python, in `lab/spine.py`.

Two formulations failed, and the failures are informative:

- **Soft least squares on `edge = direction × length`** (`layout`). Reduced turns
  to 53.8 and spacing to cov 0.34, but octilinearity collapsed to 45.6%, because
  constraining direction *and* length over-determines the edge and the solver
  compromises on direction — the one thing that must not bend.
- **Hard per-run projection with averaging at shared stations** (`layout_hard`).
  Worse still, 55.8% octilinear: most stations sit on more than one run, and
  averaging their run-targets pulls them straight back off the octilinear lines.
  The octilinear constraint is globally coupled; it cannot be satisfied run by
  run and then reconciled.

The formulation that works (`layout_perp`) constrains **only the component
perpendicular to the run direction** — which is all octilinearity actually
requires — leaves the along-run component free with a light evenness pull, and
solves every run simultaneously so shared stations need no averaging:

```
                     turns    cov   dev°  flip%  octi%  crowd%
geographic            67.0   0.71    0.0   0.00   39.4    1.55
anneal (shipped)      60.3   0.64   20.4   6.33   91.5    0.68
spine-PERP prototype  42.7   0.65   19.9   7.85   92.2    5.16
```

**−29% direction changes, at the same spacing evenness, the same octilinearity,
and slightly less bearing distortion.** The same lie tax buys substantially more
simplification. The rendered 谢菲尔德 map shows it plainly: 9号线 becomes one long
diagonal into one horizontal, 3号线 is dead straight, 1号线 crosses the map in
three runs, where the shipped version breaks all of them into short jogs.

---

## What the prototype is missing

It has **no minimum-separation constraint**, and it shows: crowding 5.16% vs the
shipped 0.68%, and in the rendered image the city centre collapses into a knot
with labels on top of each other. That constraint is real work and the shipped
annealer already does it well.

So the two approaches are **complementary, not competing**:

| | line structure | collision avoidance |
|---|---|---|
| shipped anneal | ✗ | ✓ |
| spine prototype | ✓ | ✗ |

Neither alone is a metro map. The evidence says the next algorithm is the
combination, not a replacement of one by the other.

---

## Recommended direction

1. **Add a run concept to the layout.** Before optimizing, decide for each line a
   small set of straight runs and one octilinear direction per run. This is the
   missing 规划 step and it is where the turn reduction comes from.
2. **Constrain perpendicular offset, not edge vectors.** One equation per edge,
   heavy weight. Direction becomes near-hard; spacing stays soft and tunable.
   This is the single change that made the prototype work.
3. **Keep the annealer for what it is good at.** Feed it the run-structured
   layout as its starting point and let it resolve separation and clearance,
   which it already does better than reality. Restrict its moves so it cannot
   destroy a run — or penalize breaking one.
4. **Then re-tune.** Not before: current parameters were fitted on a noisy
   landscape and mean little.

Expected: turns ≈ 43 (from 60), octilinearity ≈ 92% (unchanged), crowding back to
≈ 0.7%, lie tax unchanged. That is a schematic that finally earns its distortion.

### Separately, and for free

The **multi-start annealer fix was reverted** along with the tram/train work
(`AnnealSeeds` is absent from HEAD). It was independent of that feature and was
validated corpus-wide: mean weighted layout cost 68.89 → 63.64 over 23 networks,
every network improved or tied, none regressed, and it was what straightened the
1号线 detour. Re-applying it costs nothing and helps every metro map, whatever
happens to the rest of this.

---

## Honest limits of this study

- 6–28 networks, all from one player. Corpus bias is real.
- The metrics are proxies. `turns` uses a 12° threshold; `flip%` counts all pairs
  equally though a player only cares about salient ones. They agree with visual
  inspection on the cases I rendered, but they are not a substitute for looking.
- The prototype is Python and unoptimized; it says nothing about whether this
  fits the in-game render budget.
- I got the formulation wrong twice before getting it right, and my first
  reported result was wrong because I had not measured octilinearity — the metric
  the shipped algorithm optimizes. Any further comparison must keep all six
  metrics visible at once.

## Reproducing

```
lab/baseline/      git archive HEAD, built Release
lab/metrics.py     metric definitions + SVG station extraction
lab/spine.py       three prototype formulations
lab/hard.py        the headline comparison table
lab/sweep.py       grid-size sweep
lab/bake.py        write a prototype layout back into an export for real rendering
lab/png/           rendered comparisons
```

---

## Round 2 — decomposition, and a rule confirmed a third time

The owner's read of round 1 was that the annealer is under too much pressure on
its own ("单独退火确实压力比较大"). That is the right frame: one search is being
asked to choose run structure, octilinearity, spacing, separation, crossings and
geographic anchoring at once, with a move set of one station at a time. Round 2
tested splitting the job.

**Attempt A — plan, then separate as a post-pass.** After the perpendicular solve
every run is an exact straight line, so two motions provably preserve
octilinearity: sliding a station along its run, and translating a whole run
along its normal. Resolving clearance inside only those freedoms should be free.
It was not:

```
                     turns    cov   dev°  flip%  octi%  crowd%
PERP (plan only)      42.7   0.65   19.9   7.85   92.2    5.16
PLANNED sep=0.62      84.8   0.44   27.4  10.45   46.3    1.04
```

Crowding fell, and everything else collapsed — octilinearity halved and turns
doubled. **This is the third time a decoupled post-pass has destroyed the
structure the previous stage built** (the first was per-run projection with
averaging at shared stations, the second was this). The recurring cause is
shared stations: an interchange belongs to several runs, so any pass that
adjusts it for one run spends the other run's constraint. Post-processing an
octilinear layout is not a local operation.

**Attempt B — solve the plan and the clearance together.** Same least-squares,
with violating pairs contributing an extra row asking them to be `min_sep`
apart, and the active set recomputed each outer iteration:

```
                     turns    cov   dev°  flip%  octi%  crowd%
geographic            67.0   0.71    0.0   0.00   39.4    1.55
anneal (shipped)      60.3   0.64   20.4   6.33   91.5    0.68
JOINT sep_w=6.0       42.7   0.64   24.0  10.05   92.0    1.97
```

That works. Crowding drops from 5.16% to 1.97% while turns stay at 42.7 and
octilinearity at 92%. Rendered, the Sheffield map keeps its long runs — 7号线 one
horizontal across the top, 3号线 dead straight, 9号线 one diagonal into one
horizontal — and the centre knot from the plan-only version is largely gone.

### Where it actually stands against the shipped algorithm

| | vs shipped |
|---|---|
| direction changes | **−29%** (42.7 vs 60.3) |
| octilinearity | parity (92.0 vs 91.5) |
| spacing evenness | parity (0.64 vs 0.64) |
| bearing distortion | worse (+3.6°) |
| position reversals | worse (+3.7pp) |
| crowding | worse (+1.3pp) |

So it is no longer a free win — it is a real trade: a large simplification for a
moderate increase in positional lying and some residual congestion. Visually the
structure is clearly better and the remaining congestion is local, concentrated
where several lines converge.

### The concrete next problem

Clearance is an **inequality** ("at least min_sep apart") and a least-squares row
is an **equality**. The current formulation therefore also pulls apart pairs that
are already comfortable, and under-corrects the badly overlapping ones — which is
exactly the residual congestion pattern seen in the render. The fix is an
active-set or barrier treatment of the clearance term rather than a soft row, in
the same simultaneous solve. That is the next experiment, and it should recover
most of the remaining crowd% and some of the flip%.

### Rule earned, twice over

**Solve simultaneously; do not post-process an octilinear layout.** Both the
per-run projection and the plan-then-separate attempts failed the same way, for
the same reason. Any future stage that wants to adjust positions must enter the
solve as a constraint, not run after it.

---

## Round 3 — the exact formulation, and what actually blocks it

Question posed: is there a mathematically exact algorithm here, rather than a
tuned search?

### The formulation

Once the **combinatorial** half is fixed — the decomposition of each line into
runs, and one octilinear direction per run — the **continuous** half is a convex
quadratic program:

```
variables   p_s in R^2                       station positions

minimise    w_a * SUM_s |p_s - g_s|^2                        geographic anchor
          + w_l * SUM_e ((p_b - p_a).d_r - L)^2              even spacing

subject to  (A)  (p_b - p_a).n_r = 0        for each edge in run r   [equality]
            (B)  (p_b - p_a).d_r >= L_min   for each edge in run r   [inequality]
            (C)  (p_i - p_j).a_ij >= sep    for candidate pairs      [inequality]
```

(A) *is* octilinearity: the component perpendicular to the run direction
vanishes. (B) keeps station order along a line and a minimum gap.

(C) is the interesting one. Clearance is naturally `|p_i - p_j| >= sep`, which is
non-convex. Linearise it by requiring separation along **one** axis, and choose
that axis from the ORIGINAL geography — the axis on which the pair is already
more separated. That single substitution does two jobs at once:

- it is the clearance constraint, and
- it forbids the pair from swapping sides, which is precisely the "map lies about
  where things are" error (`flip%`).

**Clearance and positional honesty become the same linear inequality.** Because
`a_ij` comes from fixed input rather than from the variables, the feasible set is
a polyhedron and the problem stays convex: unique optimum, deterministic, no
seed, no annealing, and no possibility that more search would help.

### What the experiment showed

Implemented as an active-set penalty solve (`layout_qp`), sweeping the
octilinearity weight from soft towards hard:

```
                       turns    cov   dev°  flip%  octi%  crowd%
anneal (shipped)        60.3   0.64   20.4   6.33   91.5    0.68
QP octi_w=200           36.8   0.69   16.9   8.60   78.1    1.63
QP octi_w=1500          55.7   1.11   34.6  13.67   84.8    5.87
QP octi_w=6000          55.7   1.19   35.9  13.21   91.6    8.68
QP octi_w=25000         55.0   1.16   35.3  13.23   91.3    9.00
```

Octilinearity does converge to ~92% as the penalty approaches a constraint —
confirming the formulation — but everything else collapses. That is the signature
of an **infeasible** constraint set, not of a bad solver: with (A) effectively
hard, (B) and (C) cannot also be satisfied, so the least-squares compromises
everywhere at once.

### Confirming it: the feasibility frontier

Hold octilinearity near-hard and relax only the run decomposition (`tol` larger =
fewer, longer runs demanded):

```
                       turns    cov   dev°  flip%  octi%  crowd%
QP hard-octi tol=0.008  51.0   0.79   17.1   7.38   92.7    3.92
QP hard-octi tol=0.015  42.5   0.82   21.1   7.94   92.8    4.03
QP hard-octi tol=0.030  55.7   1.19   35.9  13.21   91.6    8.68
QP hard-octi tol=0.060  55.7   1.22   41.8  15.80   86.8   10.86
```

Demanding **longer** runs makes the turn count go **up**, not down. That is only
possible if the constraints became unsatisfiable and the solver returned a poor
layout, which the next iteration then re-decomposed into more runs. The frontier
is real and it is sharp: around `tol=0.015` everything is simultaneously good;
past it the problem stops having a solution.

Best measured configuration against the shipped algorithm:

| | shipped | QP tol=0.015 |
|---|---|---|
| direction changes | 60.3 | **42.5  (−29%)** |
| octilinearity | 91.5% | **92.8%** |
| bearing distortion | 20.4° | 21.1° (parity) |
| position reversals | 6.33% | 7.94% |
| crowding | **0.68%** | 4.03% |

### The real answer

**Solvability was never the problem; feasibility is.** The continuous half is
exactly solvable. What is not guaranteed is that the combinatorial half hands it
a solvable instance — and Douglas-Peucker plus nearest-direction snapping chooses
runs from geometry alone, knowing nothing about whether the resulting directions
leave room for the required station clearances.

Every failure in this study traces back to that: the crowding the plan-only
version could not fix, the collapse when clearance was added as equalities, and
now the collapse when octilinearity is made hard. Three different symptoms, one
cause.

So a mathematically complete algorithm is:

1. **Choose directions with feasibility in view**, not by geometric simplification
   alone. Per line, a dynamic program (Viterbi over the 8 directions as states,
   one stage per edge) minimising bend count plus a term for the clearance demand
   the choice creates. O(stations x 8 x 8) per line — trivial at 20-90 stations.
2. **Solve the convex QP exactly** for the chosen directions.
3. **If infeasible, spend one more turn** where the constraint violation is
   largest, and re-solve. Terminates because each step strictly relaxes the
   problem.

This is the tractable decomposition of what the published mixed-integer
formulations solve jointly. It keeps the exactness where exactness is available
(step 2) and confines search to a small, well-structured space (step 1) instead
of a 30,000-move random walk over positions.

### Honest status

Not yet better than what ships. The prototype wins decisively on the metric that
matters most for a schematic — direction changes, −29% — at parity octilinearity
and bearing fidelity, but it is still 6x worse at keeping stations apart, and
station separation is not optional in a map people read. Steps 1 and 3 above are
the missing work, and the feasibility frontier measured here is what they have to
respect.

---

## Round 4 — external review, and a correction to Round 3

Three independent reviews of the formulation (literature grounding, adversarial
check, combinatorial alternatives) were run. They agreed, and they found two
defects in the *implementation* — not merely the specification — that invalidate
part of Round 3's conclusion.

### Grounding

The formulation turns out to be well-precedented, and now has proper names:

- (A) and (B) are the Nollenburg & Wolff MIP's inner constraints with the sector
  binaries deleted. Fixing those binaries and solving the rest exactly is a
  recognised decomposition (Tamassia's topology-shape-metrics; Onda/Moriguchi/
  Imai's two-phase Tokyo MIP; **Li & Dong's stroke method, which is
  Douglas-Peucker plus projection onto octilinear directions — step 1 verbatim**).
- (C) is two known devices fused: **separation constraints** (the VPSC problem,
  Dwyer/Marriott/Stuckey) for the inequality, and **orthogonal ordering
  constraints** (Misue/Eades/Lai/Sugiyama, mental-map preservation) for taking
  the axis from the input. The metro-map literature calls the criterion
  "preserving relative position between pairs of stations".
- The convexity and uniqueness claim is correct: with `w_a > 0` the Hessian is
  positive definite.

### Two implementation defects, both infeasibility sources

1. **(C) was applied to pairs inside the same run.** For such a pair (A) already
   forces the perpendicular component to zero, so demanding a positive
   separation along that same axis is `0 >= sep` — unsatisfiable for any
   positive sep. Reproduced by review on `samples/sample-metro-loop.json`: the
   run Central -> Garden -> Old Town snaps to due west, and (C) picks +y for the
   pair (Station, Garden).
2. **Runs were decomposed per line, so a shared corridor received a different
   direction from each line through it.** Two independent perpendicular
   equalities on one edge force its endpoints to coincide, which then
   contradicts the minimum-length constraint. Reproduced on
   `samples/regression/sample-regression-shared-platform.json`, where one edge is
   carried by three lines.

Both fixed in `layout_qp2`: clearance skips same-run pairs, and each physical
edge gets exactly one direction.

### Corrected results

```
                            turns    cov   dev°  flip%  octi%  crowd%
geographic (truth)           67.0   0.71    0.0   0.00   39.4    1.55
anneal (shipped)             60.3   0.64   20.4   6.33   91.5    0.68
QP v1  (both defects)        46.3   0.74   13.9   7.74   79.7    1.74
QP v2  octi_w=1500           42.3   0.70   14.3   6.23   95.2    2.04
QP v2  octi_w=6000           42.2   0.70   14.4   5.80   95.9    2.53
```

| vs shipped | QP v2 |
|---|---|
| direction changes | **42.3 vs 60.3 — 30% fewer** |
| bearing distortion | **14.3° vs 20.4° — 30% less** |
| position reversals | **6.23% vs 6.33% — slightly better** |
| octilinearity | **95.2% vs 91.5%** |
| spacing evenness | 0.70 vs 0.64 — slightly worse |
| crowding | 2.04% vs 0.68% — 3x worse |

**Round 3's "infeasibility frontier" was substantially self-inflicted.** With the
two defects fixed, raising the octilinearity weight no longer collapses the other
metrics: at `octi_w=6000` the earlier run gave turns 55.7 / dev 35.9 / crowd
8.68, and now gives 42.2 / 14.4 / 2.53. The frontier is real in principle — the
reviews confirm infeasibility is the documented failure mode of this whole family
— but it is much further away than measured, and Round 3 overstated it.

Rendered, Sheffield now reads as a properly drawn diagram: 7号线 horizontal across
the top into one long diagonal, 2号线 a single clean diagonal, 9号线 diagonal into
horizontal, 3号线 dead straight. Only a horizontal congestion band through the
centre remains, which is the crowd% showing.

### Defects found but NOT yet fixed

The reviews raised more than was addressed. In rough order of importance:

- **Cycle closure.** Around any closed cycle, (A)+(B) force `SUM T_r d_r = 0`
  with all `T_r > 0`. Independent per-run snapping has no awareness of this.
  Exact infeasibility certificate given: infeasible iff all run directions in the
  cycle lie in a closed half-plane. Every loop line is exposed.
- **Loop lines are undefined in step 1** — Douglas-Peucker's top-level chord for
  a closed chain is the zero vector, so "nearest snap of the chord" has no value.
- **Topology preservation was dropped.** Station separation is not edge
  non-crossing; two long runs can cross with all four endpoints comfortably
  apart. Crossing avoidance is irreducibly non-convex and the shipped cost
  function does score it. This is a genuine capability regression, not an
  oversight of measurement.
- **(B) vs (C) contradict on backtracking edges** within a run.
- **Axis choice is a coin flip on grid-aligned game exports** — measured 1 exact
  tie and 2 near-ties out of 21 pairs on the shipped 7-station fixture, each
  becoming an inviolable hard constraint.
- **Per-edge bearing distortion is not bounded.** Nearest-snap bounds the error
  of a run's *chord* at 22.5°, but (A) forces every edge inside the run to the
  run direction exactly; an edge at 70° inside a run snapped to 0° absorbs 70°.
  The measured improvement to 14.3° is empirical, not guaranteed.

### Recommended step 1, from the review

Replace Douglas-Peucker plus nearest-snap with an **exact segmented dynamic
program**: state `(breakpoint, run direction)`, cost = perpendicular variance of
the run's geographic points about the chosen 45° axis — which is exactly the
lateral displacement constraint (A) will force each station to absorb, so the DP
minimises a direct predictor of what the continuous stage will pay. O(1) per
candidate run via prefix sums, O(64·m²) per line, 1–2 ms at 20–90 stations.

Add a run-count dimension `F[i][d][r]` to get the entire fidelity-vs-run-count
Pareto frontier in one pass (~10 ms at m=90), then re-rank the handful of
candidates with real QP solves scored on all six metrics — because the DP only
minimises a surrogate.

Also worth taking from the review: build runs on the **deduplicated network
graph** (the stroke literature does this precisely to avoid defect 2), generate
(C) lazily by scan line rather than materialising every pair (VPSC gives
O(n log n)), and constrain each run's direction to within one sector of its
chord as a hard bearing cap — the literature's device for geographic honesty,
O(edges) rather than O(n²).

---

## Round 5 — measuring the visual objection, and four failed repairs

Owner verdict on the Round 4 render: still not good enough visually. Correct, and
the earlier metric set could not see why.

### The metric that was missing

`crowd%` averaged over all n^2 pairs, which diluted the problem. The quantity the
eye reacts to is **nearest-neighbour** distance, in units of the median edge
length: below about 0.35 a station label cannot be placed without colliding.

```
                        coinc%  nnMed  tight%  fill%
Sheffield geographic      0.24   0.61     0.0   27.1
Sheffield anneal          0.24   0.71     0.0   33.3
Sheffield QP v2           1.02   0.44    44.1   24.3
Zhaoqing anneal           0.79   0.67     0.0   27.8
Zhaoqing QP v2            2.33   0.60    37.3   25.0
```

`tight%` = share of stations whose nearest neighbour is closer than 0.35 x median
edge. **The shipped annealer scores 0.0% on every city.** That is its real
achievement and it is worth more than it looks: it is the difference between a
map that can carry labels and one that cannot. The QP left 37-44% of stations
unlabelable, which is exactly what looked wrong.

Also added: `coinc%`, the share of edge pairs from *different* lines drawn
collinear and overlapping — two lines on top of each other read as one. The QP
was 3-9x worse there too.

### Four repairs, four failures

1. **Heavier penalty rows.** Clearance at weight 400 against octilinearity at
   1500 simply loses. Raising it above octilinearity trades the problem for the
   Round 3 collapse.
2. **Cutting-plane generation** (rescan the *current* layout each iteration for
   violated pairs instead of using a candidate set frozen from the input
   geography — the device the metro MIP literature uses). Real improvement:
   `tight%` 44.1 -> 23.7, `coinc%` 1.02 -> 0.38. Still nowhere near 0.
3. **Run translation.** A run translated along its own normal stays exactly
   octilinear, so this should buy clearance for free. Implemented as pairwise
   accumulated shifts averaged at shared stations, it overshot badly and made
   things much worse: `tight%` 23.7 -> 69.5. The idea is sound; that
   implementation is not. The right version treats every run in one direction
   class as a rigid body with a single degree of freedom and solves an exact 1-D
   separation (VPSC) along their shared normal, instead of accumulating pairwise
   pushes and averaging.
4. **Hand the QP layout to the shipped annealer.** This is the hybrid proposed in
   Round 2, tested for the first time by baking the QP positions into an export
   and re-rendering with `--layout schematic-anneal`.

```
layout                      turns  octi%   dev°  flip%  tight%  coinc%  fill%
geographic                   63.3   47.5    0.0   0.00     6.3    0.17   22.7
anneal (shipped)             53.3   93.4   16.8   3.98     0.0    0.34   24.5
QP alone                     43.3   90.2    8.6   5.02    17.1    0.46   23.1
QP -> anneal                 64.7   92.3   16.0   4.37     0.0    0.31   27.5
```

The annealer fixes separation perfectly and **destroys the structure doing it**:
turns 43.3 -> 64.7, worse than starting from raw geography. It has no term that
values a long straight run, so it will break one to shave a bend angle or a
clearance violation elsewhere. Round 2 suggested restricting its moves so it
cannot break a run; this measurement shows that is not an optimisation, it is a
precondition.

### The structural finding

**Turn count and station clearance are in direct competition, through degrees of
freedom.** Fewer turns means longer rigid runs; a station on a long run has
almost no freedom left, so collisions cannot be resolved locally. The shipped
annealer sits at one extreme — maximum freedom, perfect clearance, no
simplification. The QP sits at the other — best structure, best bearing fidelity,
unlabelable.

Neither endpoint is the product. What is missing is one primitive:

> **a separation operator that preserves runs.**

Everything else in this study is in hand. Run decomposition works, the QP solves
exactly, bearing fidelity and octilinearity both beat the shipped algorithm, and
the annealer already knows how to guarantee clearance. The single unbuilt piece
is the operator that creates room without breaking straight lines, and the most
promising form is repair 3 done properly: runs as rigid bodies, exact 1-D VPSC
per direction class, with shared stations as coupling constraints rather than as
an averaging step.

### Do not repeat

- Clearance as a least-squares penalty against a heavier octilinear penalty.
- A candidate pair set frozen from the input geometry.
- Pairwise accumulated run shifts reconciled by averaging.
- Feeding a good structure to the current annealer and expecting it to survive.

---

## Round 6 — the three-way tension, and a layout that finally looks right

### Repair 3, done properly

Round 5 identified run translation as the right idea implemented wrongly. The
correct version has two parts:

- **`solve_1d_separation`** — exact solution of `min SUM (x_i - x0_i)^2` subject
  to `x_(i+1) - x_i >= gap`, by block merging (the pool-adjacent-violators
  construction). This is the 1-D case of VPSC. Unit-checked: `[0,1,2]` with gap
  10 gives `[-9,1,11]` (spread about the mean), an already-feasible input is
  returned unchanged, and `[0,3,100]` merges only the pair that needed it.
- **`plan_run_offsets`** — group runs by direction class, and within a class
  solve that 1-D problem on their perpendicular offsets. Runs sharing a
  direction are parallel, so keeping them apart *is* the 1-D problem.

The crucial difference from the failed attempt: it returns **target offsets**
which are fed back into the QP as constraints, instead of translating runs and
averaging at shared stations. The averaging had no reconciliation mechanism and
simply undid itself; the QP reconciles a station belonging to two runs by the
same least squares that places everything else.

### The result is a clean three-way trade-off

```
variant                     turns  octi%   dev°  flip%  tight%  coinc%
anneal (shipped)             53.3   93.4   16.8   3.98     0.0    0.34
octi=1500 off=900            56.0   69.5   10.6   5.66     7.1    0.19
octi=4000 off=900            45.3   92.7   11.2   5.53    15.3    0.49
octi=4000 off=400            41.7   97.2   11.5   5.40    16.6    0.83
octi=8000 off=400            41.7   97.1   11.4   5.19    20.2    0.94
octi=8000 off=150            45.7   96.0   12.9   5.22    24.1    1.11
```

Monotone and without exception: **the higher the octilinearity, the worse the
clearance.** Octilinearity destroys degrees of freedom — every station on a run
shares one offset coordinate — and clearance is bought with exactly the freedom
it removes.

So there are three quantities in tension, not two, and a layout can have any
**two**:

| | octilinearity | clearance | simplification |
|---|---|---|---|
| anneal (shipped) | yes | yes | **no** (turns 53.3 vs geographic 63.3) |
| QP, high octi | yes | **no** | yes |
| QP, low octi | **no** | yes | yes |
| geographic | no | yes | no — but perfectly honest |

This reframes the whole study. The shipped algorithm is not badly built; it made
a coherent choice — octilinearity plus clearance — and paid for it with the
simplification a schematic exists to provide. Nobody chose that deliberately.

### Near-octilinear is perceptually worse than either extreme

The `octi_w=1500` variant scores best on almost every number — lowest bearing
distortion, lowest coincidence, good clearance — and **looks the worst of the
three rendered**. At 69.5% octilinear the lines are visibly *almost* straight:
9号线, 1号线 and 7号线支线 all drift by a few degrees over their length. A line
3 degrees off horizontal reads as a mistake; a line 30 degrees off reads as
intentional. Near-miss alignment is worse than either commitment.

This is a real trap for metric-driven tuning: the best-scoring variant here is
the one to reject.

### Best configuration so far

`octi_w=4000, off_w=900`:

| vs shipped | value |
|---|---|
| direction changes | **45.3 vs 53.3 — 15% fewer** |
| octilinearity | 92.7% vs 93.4% — parity |
| bearing distortion | **11.2° vs 16.8° — 33% less** |
| position reversals | 5.53% vs 3.98% — worse |
| nearest-neighbour tightness | 15.3% vs 0.0% — worse by metric |

Rendered on Sheffield it is the first output in this study that reads as a
properly drawn metro map: every label placeable, 1号线/3号线/9号线/7号线支线 as
long clean horizontals, 4号线/5号线/2号线 as clean verticals, and a legible grid
through the centre.

Note the disagreement between `tight%` (15.3, bad) and the render (clean). The
0.35-of-median threshold is too strict, and the renderer's own crowded-label
avoidance absorbs some of it. **The metric is a screen, not a verdict** — which
is the same lesson as Round 5, in the opposite direction.

### Where this leaves the algorithm

Genuinely ahead of what ships on simplification and honesty, at parity on
octilinearity, behind on clearance. The remaining gap is smaller than any
previous round and no longer looks structural: it is the residual of one
approximation, the reconciliation of stations shared between runs of different
direction classes, which the exact elimination formulation (parameterise each
station by along-run coordinate plus run offset, and treat sharing as linear
coupling) would remove entirely.

Still unaddressed from the Round 4 review, and still the honest blockers for
shipping: cycle closure, loop lines, and crossing minimisation.
