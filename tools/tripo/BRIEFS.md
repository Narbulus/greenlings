# Writing briefs that rig cleanly

The generated mesh is skinned to the vanilla greyling skeleton automatically. What makes that work
or break is decided by the brief, not by post-processing. Lessons from the first five models
(gen1, bare, mushroom, leafy, sprout):

## Do

- **4-view turnaround, T-pose**: front, side, side, back, same scale, plain background. Arms straight
  out to the sides with clear air between arm and torso all the way to the armpit.
- **Ornaments on the torso, back and head only**: mushrooms, twigs and leaves rooted on the back or
  the top of the head ride the torso bone and need no fixing (bare, mushroom, sprout came out clean).
- **Greyling proportions**: arm height at about 60% of total height, legs slightly bent, hands at the
  end of long arms. Big departures mean refitting the skeleton per model.
- **Generate with `low_poly=1`** (40 credits): clean topology that survives cutting down to ~2-3k
  triangles; the full-detail mode (30 credits) turns leaves into slabs when decimated.

## Avoid

- **Anything draped over the shoulders or along the arms** (leaf capes, pauldrons, hanging moss).
  It connects arm to torso, so it stretches into sails whenever the arm moves. Leafy needed hours of
  per-model surgery for exactly this.
- **Thin features crossing the arm/torso boundary** (a twig growing from the shoulder).
- **Fringe on the upper arm** (spiky leaves standing off the arm): it becomes plates when arms hang.

## Check before accepting a generation

`greenling_variants.remove_stretch` poses the arms through `ARM_TEST_POSES` and reports how many
faces stretch. A generation that needs it to delete more than a handful of faces is a brief problem:
fix the brief and regenerate rather than editing the mesh.
