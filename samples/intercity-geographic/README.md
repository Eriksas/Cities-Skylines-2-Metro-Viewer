# Intercity Geographic Fixtures

These fixtures exercise the renderer-only geometry contract before passenger
rail is reintroduced into the exporter.

- `sample-intercity-outside-tail.json` checks that a long outside-connection
  tail is clipped and cannot expand map bounds.
- `sample-intercity-through-route.json` checks a line that enters and exits the
  city while preserving the in-city route order.
- `real-fixture-manifest.json` inventories the currently available real-city
  exports without copying multi-megabyte player data into the repository.

The compact fixtures are intentionally independent of CS2 ECS component names.
Cargo/passenger classification fixtures will be added with IG2.1, where the
isolated passenger-rail collector is implemented.
