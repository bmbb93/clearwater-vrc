[日本語](CHANGELOG.md) | **English**

# Changelog

The changes in each version of Clearwater VRC (`com.vbamboo.clearwater`). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).
Each version's section, with the Japanese one in `CHANGELOG.md`, makes its release notes on GitHub.

## [1.3.0] - Unreleased

### Changed

- The clouds are made anew. They are cumulus drawn as a volume, lit by the sun and the sky: flat bases and rounded, heaped tops, white on the sunlit side, light grey in the shade and underneath, with frayed edges the blue shows through. They are redrawn a little at a time into a cloud dome for each scene (a 2048 × 1024 CustomRenderTexture; 16 strips in turn, the whole sky in about 0.2 seconds), and the sky, the reflections on the water and the sky seen from underwater just read it once. Between redraws the clouds still drift smoothly with the wind. On an RTX 4070 Ti SUPER they cost about 0.4 ms per frame, once whatever the screen size and for both VR eyes. With less cloud, clouds go from the sparse places first, and the ones left are smaller. The dome is made in the scene's folder in `Generated` by Build Scene, by the Coast's Bake and when the scene is opened
- The far clouds no longer repeat the same pattern to the horizon (the cloud map is read two ways, and the cover's field at two scales). The fine grain on the clouds' lit faces at dusk, and the gray, smudge-like shade on their tops, are gone. At dusk the clouds' lit undersides are softer and their shaded tops lighter with the skylight, so the contrast is gentler; after sunset their flat bases no longer glow alone like saucers
- When the time passes, the sky is worked out every frame (`Update interval` defaults to 0 instead of 0.1 s): the sun, the moon, the stars and the Directional Light (the shadows' direction) move smoothly, and the dusk sky's brightness no longer changes in steps. With a day of a few minutes they used to move in jerks. It costs about 0.11 ms of Udon a frame; the rendering cost is unchanged. With an interval, the directions still move every frame. Existing scenes keep the 0.1 s saved on their Sun: set Clearwater Sky's `Update interval` to 0
- The sky's reflection probe (Sky Reflection (Clearwater)) is 512 instead of 128: the clouds and the sun reflected on smooth surfaces such as window glass are no longer blurred. It draws the sky alone, so redrawing it costs hardly more. In existing scenes, run Build Scene again or set the probe's Resolution to 512

### Added

- Cloud shadows: the clouds cast soft shadows on the sea and the beach, drifting with them (at night they cut the moonlight)
- The pebble's `Hand Reach`: the panel called up on the left hand in VR shows 25 cm beyond the hand (away from the eyes); right over the hand it was too close to work. `Hand Size` now defaults to 0.55 instead of 0.4 to match (pebbles already in a scene keep their values)
- The panel called up on the left hand stays where it appeared (following the hand, it moved away when the right hand reached for it). It hides when the eyes are `Hand Close Distance` (1.5 m by default) away from it
- `Buildings` and `Indoor Level` on Clearwater Controller: with the buildings' boxes (the same as the Sky Light Volumes) in it, the sea is shut out indoors (0.3 by default), and the surf no longer sounds from the waterline under a floor. In a house built over the beach, the surf sounded loud from the waterline under the floor
- `As in 1.2` on the sky material: the clouds can go back to those up to 1.2 (flat clouds the sky draws for each pixel; no shadows), to keep the look of worlds made with 1.2. It is off by default (the new clouds). While it is on, the cloud dome is not drawn, so they cost the same as in 1.2

## [1.2.0] - 2026-10-03

### Added

- The sky's light in buildings: put additive Light Volumes (VRC Light Volumes) baked with the sky alone into
  `Sky Light Volumes` of Clearwater Sky, and their color and strength follow the sky of the time. The sky's light
  through the windows is white by day, orange in the evening and moonlight at night. With the lightmaps baked from
  the light that does not change with the time (lamps and the like), rooms no longer stay as bright as by day
  whatever the hour. A multiplier per Light Volume (`Sky Light Gains`, for the eye getting used to the dark
  indoors), the part of the light that comes from the horizon (`Sky Light Horizon`) and the sunlight given back by
  the ground (`Sun Bounce`) can be set too. Reflection probes baked with the sky alone follow it as well
  (`Sky Reflection Probes`), and Light Volumes baked with the lamps alone can be brought up at night
  (`Lamp Light Volumes`, for the eye getting used to the lamps indoors) (see "The sky's light in buildings" in the [README](https://github.com/bmbb93/clearwater-vrc/blob/main/README.en.md)).
- The boxes of the `Sky Light Volumes` are the buildings to the beach and the water: inside one, no additive Light
  Volume is added to them (the sky's light was doubled there, a bright box on the sand), and a Point Light Volume
  inside one does not light them (a room's lamps came through the walls onto the sand outside).
- Support for [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes) 3.0 (checked with 3.0.0-dev.20). A project
  with 3.0 compiles, and Build Demo Scenes makes Demo_LightVolumes the 3.0 way (each light's settings on its Point
  Light Volume Instance, registered with the Light Volume Manager). The beach, the bottom and the foam take Light
  Volumes' light with 3.0 as with 2.x. Projects with 2.x, and without Light Volumes, work as before.
- The distant land follows the coast. With a coast, it stands only where the ground goes on 2 km out, and where the
  shoreline turns away from the walkable beach it comes down to the sea with it (with a shore running off at an
  angle, the headland stood over the open sea). Bake the Coast again for it. `Land setback` on the sky material moves
  the mountains inland from the shoreline, and `Land height` scales them (0: none).
- Clearwater Sky's `darkness`: how deep into the night it is (0 by day and well into the dusk, 1 at full night). A world's U# can read it, say to bring its lights up at night
- `Sound Volume` on Clearwater Controller: one multiplier (0 to 1, default 1) for the three sea sounds (the surf, the
  distant sea, under water). The world's U# can set it, to turn the sea down while a video plays or off with a switch.

### Changed

- The relief on the beach face the swash washes (shallower than 0.3 m) and on the land is a third of what it was. On a gentle beach the run-up's edge turned back short of the mounds, leaving dry islands, and filled the hollows with pools. Bake the Coast again for the collider to match

## [1.1.0] - 2026-09-30

### Added

- Support for [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes) (RED_SIM). In a world that has them,
  the light of additive Light Volumes and Point Light Volumes falls on the beach, the bottom under the water and the
  foam, as it does on the avatars there, and the water surface shows its glint: a lamp or a fire on the beach at
  night lights the sand and the shallows. Light Volumes that are not additive are not read, as they hold the light of
  the hour they were baked at (see "VRC Light Volumes" in the [README](https://github.com/bmbb93/clearwater-vrc/blob/main/README.en.md#vrc-light-volumes)).
- The Light Volumes package is optional: its shader include (`LightVolumes.cginc` 2.1.3, MIT) comes with the
  package. In a world without Light Volumes, the look and the cost are the same as in 1.0.0.
- With VRC Light Volumes in the project, Build Demo Scenes also makes a seventh demo, Demo_LightVolumes: the beach at
  night lit by Point Light Volumes on the sand, over the shallows and in the water, and by a lamp that goes round
  across the waterline changing its colour and brightness (`Open Light Volumes (VRC Light Volumes)`).

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

[1.2.0]: https://github.com/bmbb93/clearwater-vrc/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/bmbb93/clearwater-vrc/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/bmbb93/clearwater-vrc/releases/tag/v1.0.0
