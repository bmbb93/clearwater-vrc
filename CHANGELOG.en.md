[日本語](CHANGELOG.md) | **English**

# Changelog

The changes in each version of Clearwater VRC (`com.vbamboo.clearwater`). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).
Each version's section, with the Japanese one in `CHANGELOG.md`, makes its release notes on GitHub.

## [1.1.0] - Unreleased

### Added

- Support for [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes) (RED_SIM). In a world that has them,
  the light of additive Light Volumes and Point Light Volumes falls on the beach, the bottom under the water and the
  foam, as it does on the avatars there, and the water surface shows its glint: a lamp or a fire on the beach at
  night lights the sand and the shallows. Light Volumes that are not additive are not read, as they hold the light of
  the hour they were baked at (see "VRC Light Volumes" in the [README](https://github.com/bmbb93/clearwater-vrc/blob/main/README.en.md#vrc-light-volumes)).
- The Light Volumes package is optional: its shader include (`LightVolumes.cginc` 2.1.3, MIT) comes with the
  package. In a world without Light Volumes, the look and the cost are the same as in 1.0.0.

## [1.0.0] - 2026-09-29

The first public release.

- Clear shallow water you can walk into: reflections, caustics on the bottom, absorption and scattering in the water,
  and an underwater view with Snell's window.
- A coast drawn as a line and baked: the shore's shape, its cross-section, and the ground you walk on. Your own
  meshes or a Unity Terrain can be the ground, with the generated beach carrying on past them.
- Waves that break and run up the beach with foam, in time with the surf sound.
- Pools at their own heights, and stamps that raise, carve or stand in the water for the waves to break round.
- A sky that changes with the time of day, computed from how the atmosphere scatters sunlight, with a moon and stars
  at night, synced across the instance, and a panel to change it inside the world.
- Tone mapping in post-processing (PPSv2) or in the shaders.

[1.1.0]: https://github.com/bmbb93/clearwater-vrc/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/bmbb93/clearwater-vrc/releases/tag/v1.0.0
