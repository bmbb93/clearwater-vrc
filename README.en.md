[日本語](README.md) | **English**

# Clearwater VRC

A package for building clear shallows and coasts you can walk into in VRChat worlds. It covers reflections on the water surface, the patterns of light that sway on the bottom (caustics), a shoreline where waves wash in and out in time with the surf sound, and the view underwater when you dive in. It is based on the WebGL demo [clearwater by Aureliengmz](https://github.com/Aureliengmz/clearwater) (MIT, © Lumaris, `Third Party Notices.md`).

Try it in VRChat: [Clearwater Beach](https://vrchat.com/home/world/wrld_9795f8ab-5305-4cfb-a757-745fd264252e) (the standard scene Build Scene makes, published as a world)

![A shallow sea seen from the beach](Documentation~/images/beach.jpg)

| Shallow water and caustics | The shoreline (run-up and foam) |
| --- | --- |
| ![The water surface and caustics on the bottom, looking down on the shallows](Documentation~/images/shallows.jpg) | ![Waves washing up the beach with foam](Documentation~/images/shore.jpg) |
| **Underwater** | **The sky seen from underwater (cut out as a circle)** |
| ![Looking toward the beach from underwater](Documentation~/images/underwater.jpg) | ![Looking up at the water surface from underwater](Documentation~/images/snells-window.jpg) |

The sky can change with the time of day, from sunrise through sunset to a moonlit night. Its colors are computed physically, from how the atmosphere scatters sunlight.

![Top row: sunrise, morning, afternoon (16:30); bottom row: sunset, twilight, and the sky and sea on a moonlit night](Documentation~/images/time-of-day.jpg)

The time can also be changed from a panel inside the world. The heavy scattering computation is done once in the editor and baked into a table, and at run time the shaders only look the table up. So compared with the fixed sky, it adds only 0.1–0.15 ms per VR eye ([Time of day and the sky](#time-of-day-and-the-sky-clearwater-sky), [Performance](#performance)).

For PC only (it relies heavily on GPU computation, so it does not run on Quest). It requires VRChat Worlds SDK 3.10 or later and assumes you have built and uploaded a world in Unity before.

The first time, read the three sections "Installation", "Getting started" and "Drawing the coast" in order, and you will have a working coast. Refer to the later sections when you want to use your own terrain or pools, or tune the look. Terms are defined in `CONTEXT.md` (Japanese). How things work inside (wave computation, how the bottom is drawn, draw order, the sky and syncing, etc.) is explained with diagrams in [docs/technical.en.md](docs/technical.en.md). That document also lists the default values of the tunable settings and what each file does. The main mechanisms are also shown as [animated diagrams](https://bmbb93.github.io/clearwater-vrc/how-it-works/) (Japanese).

## Installation

Add this package's repository to ALCOM / VCC, then add "Clearwater VRC" to your world project.

1. In the ALCOM / VCC settings (Packages), add a repository with the following URL (you can also add it with the button on the [distribution page](https://bmbb93.github.io/clearwater-vrc/))

   ```
   https://bmbb93.github.io/clearwater-vrc/index.json
   ```

2. Add "Clearwater VRC" in the project's management screen

You can also extract a release zip into the project's `Packages/` (an embedded package). To use the package while editing it, keep this repository locally and add a reference to its folder under `dependencies` in the project's `Packages/manifest.json` (the folder is read directly, not copied). The path can also be relative to `Packages/`.

```json
"com.vbamboo.clearwater": "file:<path to this repository's folder>"
```

## Getting started

### Starting from a new scene

Run `Tools > Clearwater > Build Scene` to create a scene with the water, beach, sun and spawn point at `Assets/Clearwater/Scenes/Clearwater.unity`, and the scene list in the build settings becomes just this one scene. Running it again rebuilds the assets, but objects you placed in the scene by hand and the world ID are kept. Values you tuned in the materials (in the scene's folder inside `Assets/Clearwater/Generated`), such as wave height, brightness, clouds and the tone mapping method, are kept too; only values Clearwater owns, such as texture references, are rebuilt. To rebuild the scene from scratch, use `Recreate Scene`. Tone mapping is done in post-processing (PPv2) (see the "Tone mapping" section).

### Adding to an existing world

Open your world's scene and run `Tools > Clearwater > Add to Current Scene`. This adds the full water setup and "Coast (editor only)", an object for editing a straight coast. First it asks whether to use Clearwater's sky and sun as well.

- Yes: you get the same sky and sun (Sun (Clearwater)) as Build Scene, with a sky that changes with the time of day (see the "Time of day and the sky" section). Ambient light also follows that sky. Only the Light component of the world's Directional Light is turned off (the object and its other components stay as they are, and nothing is deleted)
- No: the world's own sun is used as is. If its shadows are disabled, Clearwater enables them. If there is no Directional Light, a "Sun" is created, and if the skybox is unset or Unity's default, it is set to Clearwater's sky

Either way, tone mapping is done in post-processing (PPv2) (see the "Tone mapping" section), and the scene is saved after the addition.

To switch later, use `Tools > Clearwater > Use Clearwater Sky and Sun`. The water uses the screen's depth information to show the bottom and objects in the water through it, so keep shadows enabled. With shadows off, no depth information is produced and the water cannot be drawn correctly. The Far Clip of the world's Reference Camera also has to match the size of the sea (`Sea size` in the next section).

With either way of starting, the next step is to draw the shape of the coast and Bake.

### Trying the demo scenes

Six scenes are provided as usage examples (seven with VRC Light Volumes in the project). Run `Tools > Clearwater > Demo Scenes > Build Demo Scenes` to create the scenes and their terrain, materials and textures in `Assets/Clearwater/Demo` (it takes about 30 seconds). Each is a new scene like the one from Build Scene: Beach is left as is (the ground Clearwater makes), and the others add ground to it. The open scene is not changed. Before running, it asks whether to save the open scene. When done, the Beach demo opens. In every demo you can call the sky panel (see the "Time of day and the sky" section) to hand with two pulls of the left trigger in VR, or the Tab key on desktop. The pebble that brings up the panel is placed only in Beach, as an example (near the spawn point). The other demos only have a caller (Sky Panel Caller) with no visuals and no collider. In every demo, the spawn point is on dry ground away from the sea. All demos use post-processing (PPv2) for tone mapping.

| Scene | Menu to open it | Contents | Section with details |
| --- | --- | --- | --- |
| Demo_Beach | `Open Beach (Clearwater's own ground)` | The same beach as Build Scene, on the ground Clearwater makes (a straight coastline, the default profile and undulation). Start with this one | Drawing the coast |
| Demo_Cove | `Open Cove (a mesh as the ground)` | A sandy cove made from a mesh. The ground outside it is made the same sand with "Match the user terrain" | Using your own terrain |
| Demo_Harbor | `Open Harbor (a quay and a pier)` | A vertical quay wall, stepped revetment, a slope, and a pier standing on piles. The piles have Obstacle | Using your own terrain, Placing objects |
| Demo_Pool | `Open Pool (still water only)` | A 25 m pool with no sea. It is 0.5 m deep, and you can get out onto the poolside by jumping. It does not use Pool; the line is set to `Closed` to enclose it, and shore waves are off | Adding pools (first paragraph) |
| Demo_Resort | `Open Resort (the sea and two pools)` | A seaside building with an indoor pool on the ground floor and a pool on the second-floor terrace. You reach the terrace by a slope along the outer wall on the right as you face the sea. Both pools are 0.7 m deep, and you can get out onto the edge by jumping | Adding pools |
| Demo_Terrain | `Open Terrain (a Unity Terrain)` | A cove, headland and sandbar made with Unity's Terrain | Using your own terrain |
| Demo_LightVolumes | `Open Light Volumes (VRC Light Volumes)` | The beach at night (21:00) lit by Point Light Volumes: lamps on the sand, over the shallows and in the water (in the shallows and 1.6 m down), a spot over the beach, and a lamp that goes round across the waterline changing its colour and brightness (`ClearwaterDemoLamp`). Made, and in the menu, only with VRC Light Volumes in the project | VRC Light Volumes |

You can open the demos from the `Open ...` menu items or from the Project window. Bakes are per scene (see "Bakes are per scene" below), so opening or rebuilding the demos does not change your own scenes.

Build Demo Scenes rebuilds the shared assets used by every scene (directly under `Assets/Clearwater/Generated`) the same way Build Scene does. When you no longer need the demos, delete the `Assets/Clearwater/Demo` folder and the folders in `Assets/Clearwater/Generated` whose names start with `Demo_`.

## Drawing the coast (Coast (editor only))

Select "Coast (editor only)" in the scene and the shoreline appears in the Scene view. The line and the profile (the height of the ground going out to sea) are baked into the water surface, bottom, waves, walkable ground and surf sound positions when you press **Bake** in the Inspector. Bake again after changing settings. Until you do, the water and ground keep the previous shape.

The Scene view shows three squares. Yellow is the area baked in fine detail (`Area Size`), green is the walkable area (`Ground Half Size` is half its side; invisible walls stand at its edges), and light blue is the sea (`Sea size`).

The line's shape:

- Drag points to move them. Add a point with the "+" along the line; select a point and press Delete to remove it
- Choose the line type with `Line`: Straight (points joined by straight lines) / Smooth (a smooth curve through the points, default) / Handles (like paths in Illustrator, each point's two handles set direction and curvature)
- With Handles, the selected point and its neighbors show handles (squares). Drag them to change the curvature; drag with Alt held to make a corner. "Smooth point / Corner point / Auto handles" in the Inspector switch each point. Adding a point with "+" does not change the curve's shape
- The arrow points to the sea side (the left of the line's direction). If it is the wrong way, use "Reverse direction"
- An open line continues straight past both ends. Set `Closed` to make it a loop (an island or a lake)
- "Reset line" returns the line to the initial straight line (3 points, 200 m). The profile and other settings stay as they are. To reset all settings to their defaults, use Reset in the component's ⋮ menu

Profile and waves:

- Profile: `Gentle Beach` (a gently sloping beach, adjusted with numbers) or `Curve` (draw the ground height against the distance from the shoreline as a curve)
- Profile presets: choose in `Preset` and press "Apply". Gently sloping beach (numbers) / steep beach / beach with a sandbar / lagoon (wide shallows) / lake shore (for `Shore waves` off) / steep rocky shore / gently sloping beach below hills (the land is visible from far away). All except the gently sloping beach come in as `Curve`, so you can edit the curve right away
- Turning `Shore waves` off removes the waves and surf sound at the shoreline, and the water at the shore becomes calm (for lakes and ponds). What goes away: the incoming waves, breaking waves, foam, wet sand, and the run-up (the water of a broken wave running up the beach and back). The small waves offshore and the ambient sound of the distant sea remain

The size of the sea is set with `Sea size` (default 5000 m square, centered on the water object). When you Bake, the water surface, the ground drawn into the distance and the underwater effects adapt to this size, and the edge of the water surface fades into the distant haze. The Far Clip of the world's Reference Camera must be 0.8 times `Sea size` (4000 m with the default). If it is too short, the Inspector shows a warning and a "Set N m" button, which sets it on the world's Reference Camera.

### Bakes are per scene

Baked data and the materials that read it (water, bottom, underwater, sky, etc.) live in a per-scene folder, `Assets/Clearwater/Generated/<scene name>_<8 alphanumeric characters>`. The characters are the start of the scene's GUID, so the same folder is used even if you rename the scene. When several scenes in one project use Clearwater, their Bakes do not affect each other. Values you tune in the materials are also separate per scene.

- When you duplicate a scene, its materials are copied into the new scene's folder on the first Bake, carrying over your tuned values
- Data that earlier versions created directly under `Assets/Clearwater/Generated` moves into the folder of the first scene you Bake. If another scene used the same data, it is copied and split off when that scene is baked
- Deleting a scene leaves its folder. Delete it by hand if you do not need it
- Things that do not depend on the scene, such as the wave spectrum, caustics, the sky LUT and tone mapping, are shared directly under `Assets/Clearwater/Generated`

### Where the waves come from

Waves come in from offshore from one direction, and each beach gets only as much of them as it is open toward that direction. The back of a bay gets only what comes in through its mouth, and along a wall that the waves run alongside they hardly break. This shows in the wave height, run-up, whitewater, foam and the loudness of the surf sound.

The arrival timing also depends on the direction. Waves reach the back of a bay later, and waves coming in at an angle break one after another along the beach. Waves travel slower in shallower water, and the arrival times are computed with that in mind. The surf sound follows the waves on the beach it is heard from.

- `Swell direction auto` (on by default): waves come in head-on to the direction the coast within about 200 m of this object faces on average
- `Swell from`: the direction when auto is off (angle clockwise from world +Z)
- `Swell spread` (default 25°): widen it to let waves wrap into the back of bays and onto beaches facing sideways

The "swell" arrow in the Scene view shows the current direction.

### Drawing the line beyond the walkable area

The line can extend beyond the walkable area. Placing points far away gives shapes such as headlands, bays and the opposite shore, and the water surface, distant ground and offshore waves follow that shape. Outside the yellow square, things are drawn from coarse data baked for the whole sea (the light blue square). Its spacing is `Sea size` ÷ `Outer Resolution`, which is 5000 m ÷ 1024 ≈ 5 m by default. Beyond the sea, the line continues in the same direction.

- Distant land that is low sinks below the horizon and cannot be seen. To show it, set the profile to `Curve` and raise the land side (the negative side)
- Even with a long line, the part near the walkable area is baked finely at about 1 m spacing. Farther segments are coarser (up to 512 points for the whole line)
- Surf sound sources use only the parts of the line within 100 m of the walkable area

## The bed's look (Bed look)

The look of the ground that runs continuously from under the water up to the beach (the Bed) is chosen with the Coast's `Bed look`. Under the water and on the beach look the same, and changes apply immediately without a Bake.

| `Sand` | `Pebbles` |
| --- | --- |
| ![A beach with Sand](Documentation~/images/bed-sand.jpg) | ![A beach with Pebbles](Documentation~/images/bed-pebbles.jpg) |

- Bundled presets: `Sand` (a beige-to-brown sandy beach, default) / `Pebbles`. Switch with the Coast's `Sand` / `Pebbles` buttons
- Your own look: in Project, right-click > Create > Clearwater > Bed Look. Set `Color` (color, required, tiled), `Height` (height, optional; estimated from the color's brightness if absent) and how many meters square one tile covers (`Tile size`)
- Layered effects: sand in the gaps (`Sand fill`), ripple marks (`Ripple marks`; with `Sand bed` on, over the whole bed), weed color (`Weed tint`), and color adjustments (`Tint`, `Saturation`, `Brightness`, large blotches `Patchiness`, a subdued pebble tone `Muted grade`)
- Gloss: `Smoothness` (same meaning as the Standard shader's Smoothness; default 0 = matte). Dry ground above the water gets sky reflections and sun highlights. At 0 it adds no cost
- Matching your own terrain: see "Match the user terrain" in the next section

## Using your own terrain (Terrain source: User)

The ground in the walkable area (the green square) can be your own mesh or a Unity Terrain. Use this for shapes that the line and profile cannot make, such as a rocky cove or a harbor with a quay wall.

| A mesh cove | A harbor with a quay wall | A Unity Terrain |
| --- | --- | --- |
| ![A cove made from a mesh](Documentation~/images/user-terrain-cove.jpg) | ![A quay wall and stepped revetment](Documentation~/images/harbor-quay.jpg) | ![The shoreline of a beach made with Terrain](Documentation~/images/unity-terrain.jpg) |

1. Put the terrain meshes / Terrain under one parent object. Add the colliders yourself too (Mesh Collider, Terrain Collider, etc.)
2. Set the Coast's `Terrain source` to `User` and assign that parent to `User terrain`
3. Draw the Coast's line so that at the edges of the walkable area it matches the terrain's shoreline (Waterline: where the terrain meets the water level, height 0)
4. Bake

When you Bake:

- The terrain's look and collision stay your own materials and colliders. Clearwater bakes the terrain's height seen from above, and fits the water color, waves, light on the bottom and the Seabed (the object that draws the bottom) to it
- Inside the walkable area, the line where the terrain meets the water surface is found and used as the shoreline (only the part of the drawn line outside it is used). With the line set to `Closed` it is not replaced, so draw the line along the shoreline
- Outside the terrain, the ground stays the terrain Clearwater makes from the line and profile (the generated terrain). The generated terrain joins the height of the terrain's edge smoothly over a seam `Seam width` wide (default 20 m)
- Underwater caustics and the wetness and foam of the shoreline are overlaid on the terrain. They are drawn by two Projectors (the Unity component), "User Terrain Caustics" and "User Terrain Beach", created under the water. The Projectors affect the same layer as the terrain, so they also fall on other objects in that layer
- The generated terrain's collision is turned off (the invisible walls at the edges of the walkable area remain)
- The speed and reach of the run-up are set by the slope of the terrain's shoreline
- Bake again after moving or reshaping the terrain (the Inspector tells you)

To give the outside the same look, press "Match the user terrain" in `Bed look`. A Bed look is made from the texture, color, tile size and Smoothness of the material covering most of the terrain (for a Terrain, the most-painted layer) (matte if `Specular Highlights` and `Reflections` are both off). It is saved in the same folder as the scene as "<scene name> Bed Look" and assigned to the Coast. Beyond the seam and on the distant bottom, the ground looks the same as the terrain, and you can adjust brightness and so on in that Bed look.

For a Unity Terrain, heights are read directly from the Terrain's data (where it overlaps a mesh, the higher one wins). The bottom's average color and "Match the user terrain" take their values from the painted layers. The standard Terrain shader (Standard) uses the texture's alpha as Smoothness, so textures with alpha make the ground shine like a mirror. For a matte look, assign a `Nature/Terrain/Diffuse` material to the Terrain.

Problems found during the Bake appear as numbered warnings in the Coast's Inspector. The Scene view shows red markers with the same numbers, and "Show" frames that spot.

| Warning | What happens | How to fix |
| --- | --- | --- |
| Part of the walkable area is not covered by the terrain | There is no ground to stand on there, and you fall | Cover it with terrain, or make `Ground Half Size` smaller |
| The terrain extends past the baked area (the walkable area and the seam) | The generated terrain shows through where it extends past | Make the terrain smaller, or make `Ground Half Size` or `Seam width` larger |
| The seam is steep (the height difference exceeds 0.2 times the seam width) | Banks or ditches form around the terrain | Widen `Seam width`, or extend the terrain until its edge height is close to the generated terrain |
| At the edges of the walkable area, the Coast's line is 5 m or more away from the terrain's shoreline | The shoreline is misaligned at the edges | Match the line's ends to the terrain's shoreline |

## Adding pools (Clearwater Pool)

You can add any number of pools whose water surface sits at a different height from the sea. For a resort building, you can have an indoor pool on the ground floor and a pool on the second-floor terrace, together with the sea. The water surface, underwater view, caustics and ripples when touched work the same way as the sea; there are no incoming waves or surf sound.

| Indoor pool on the ground floor (reflecting the room) | Pool on the second-floor terrace | Inside the pool |
| --- | --- | --- |
| ![The indoor pool and its opening to the sea](Documentation~/images/pool-indoor.jpg) | ![The terrace pool](Documentation~/images/pool-terrace.jpg) | ![Underwater in the terrace pool](Documentation~/images/pool-underwater.jpg) |

For a world with no sea and only pools with the same water level, you can also skip Pool and use your own terrain from the previous section. Turn `Shore waves` off and set the line to `Closed` around the pool. Water only shows where the terrain is below the water surface, so as long as the water level is the same, any number of pools works. The line can be one loop around all of them (enclosing just one looks almost the same).

Setting up a Pool:

1. Place a "Pool" with `Tools > Clearwater > Add Pool` and move it to the pool's water level. The object's position is the water surface. Do not rotate it
2. Put the mesh of the pool's basin (walls and floor; the surrounding deck can be included) under the child "Basin". Add the colliders yourself too. Do not include ceilings or diving boards, because the highest surface seen from above is baked as the floor
3. Make `Size` (the extent of the water surface, x and z) a little larger than the inside of the basin. Water only shows where the basin is below the water surface, so larger is fine
4. Press **Bake** in the Pool's Inspector (a Coast Bake also bakes all pools)

Settings:

- `Wave Strength`: how much the surface moves (relative to the sea's waves as 1; default 0.35). The caustics weaken by the same amount
- `Indoor`: indoors. Sunlight (caustics, glare, the sunlit floor) goes away, and the room is reflected instead of the sky. The room comes from reflection probes, so nothing is reflected unless you place a probe
  - Turn on Box Projection on the probe
  - In VRChat, Baked probes are cheap and Realtime probes are expensive. [Not verified] How it looks with a Baked probe has not been tried in a real world yet
- `Fog Distance`: how far you can see underwater (m)

When you Bake:

- The pool gets children "Pool Water" (water surface), "Pool Underwater" (underwater) and "Pool Caustics" (caustics on the walls and floor, and on avatars in the pool). Assets are saved in `Pools/` in the scene's folder
- The sea does not enter the pool's volume (inside the square seen from above, the space from the floor to the water surface). Even if the pool's floor is lower than the sea surface or the generated terrain, the sea's water surface, ground and caustics do not appear there, and that part is cut out of the generated terrain's collision
- After moving a pool, Bake the Coast again too. Until you do, the sea's collision and the hole stay at the previous position (the Inspector tells you)
- Which water you are in is decided by the pool's volume. When your head is in the pool, you get the pool's underwater view and the underwater sound. On-screen and photo cameras also get the underwater view of whichever water they are in
- Ripples appear only on the water you are in. If you are above, in, or right beside a pool, that is the pool. [Not verified] Whether ripples appear on a pool when touched with feet or hands, or when tapped, has not been tried in a real world yet
- From a lower floor, a pool on an upper floor is not shown as seen from below its water surface

Problems found during the Bake appear as numbered warnings in the Pool's Inspector, with markers in the Scene view.

| Warning | What happens | How to fix |
| --- | --- | --- |
| No water | The water surface is not drawn | Put the Pool at the water level. Remove ceilings and covers from Basin |
| The basin continues below the water surface past `Size` | The water is cut off there | Make `Size` larger |
| It overlaps another pool at the same height | Only one of them can be underwater at a time | Move the pool or change its height |
| It is rotated | The water surface does not rotate and stays a world-axis-aligned square | Do not rotate the Pool; rotate the contents of Basin |

Not supported:

- Seeing one body of water through another (the sea through a glass-walled pool, for example). The water behind is not shown
- Incoming waves in pools
- Rotated pools (the volume is an axis-aligned square; place an angled pool as the square that encloses it)

## Placing objects (Clearwater Stamp)

Objects placed on the coast are treated as part of the coast when you add `ClearwaterStamp` and Bake.

![A pier with Obstacle on its piles](Documentation~/images/harbor-pier.jpg)

| Mode | Use | Effect |
| --- | --- | --- |
| Obstacle | Add to visible objects standing in the water, such as rocks, piles and driftwood | Waves hit the object and break, with whitewater around it. With `Receive Caustics`, caustics fall on the part under the water (moved to the ClearwaterProps layer) |
| Raise Ground | An invisible brush for shaping (any mesh) | Its top surface becomes the ground (a sandbar, a rock shelf). The bottom, walkable ground and waves all follow |
| Carve Ground | Same as above | The ground is dug down to its top surface (a tide pool, a channel) |

Brushes become invisible after the Bake and show as wireframes in the Scene view (they are not included in the upload). A slope of about 30° is added around a stamp automatically. Bake again after moving a stamp.

## Changing waves, brightness and clouds

Where to find the settings for waves, brightness, clouds and volume. The materials are in the scene's folder inside `Assets/Clearwater/Generated` (see "Bakes are per scene").

- Wave size: `Breaker height` (height of breaking waves) / `Run-up` (how high the run-up reaches, as a multiple of `Breaker height`) in the Water and Seabed materials
- Where offshore the waves appear: `Swell start depth` in the Water material (default 2.6 m). The swell appears where the water is shallower than this and reaches full height 0.8 m shallower. The deeper it is, the farther out the waves are visible (waves are lower farther out). Making it deeper than the deepest part of the coast profile puts swell over the whole sea, at a higher cost (range 0.5–6 m)
- Whitewater at the shoreline: `Whitewater height` (the bulge at the front of the run-up, default 3 cm) / `Foam relief` (shading of the foam's bumps, for close-up viewing; default 0 = none)
- The speed and reach of the run-up are set automatically from the beach's slope at Bake time. It moves like a ball thrown up a slope: the gentler the beach, the slower and farther it goes. The beach's slope is the Coast's `Beach Slope` (default 0.1)
- Brightness: `Sun intensity` / `Exposure` in the water, Seabed, sky and underwater materials (Water, Seabed, Sky, Underwater). Use the same values in all four
- Clouds: `Clouds` in the sky material (Sky)
  - Sets `Cover` (0 = none, default), size, and the speed and direction of drift
  - The same clouds appear in the water surface's reflection, in the round area where the sky is visible when looking up from underwater, and in the reflections on wet sand. When changed in the Inspector, they are copied automatically to the water surface and Seabed materials
  - Clouds drift while the world is running (in the Scene view, turn on Always Refresh)
- Distant land: `Distant land` in the sky material (Sky). Sets how much of the horizon is land (pine-covered headlands), from 0 to 1 (0 = sea all around, 1 = land all around, default 0.5). The land spreads centered on the side opposite the sea, and its ends slope gently down to the sea surface. Which side is the sea is decided by the Coast's Bake from the direction the swell comes from (+Z if there is no coast). Like the clouds, it is copied to the water surface and Seabed materials
- Volume of ripples and surf sound: "Clearwater Controller" in the scene
- The underwater view: in the underwater material (Underwater), `Fog density` (visibility; 1 is the same density as the water seen from above the surface, smaller values let you see farther; default 0.2) / `Fog saturation` (how saturated the water color reached in the distance is; default 0.7) / `Fog brightness` (brightness of that color; default 1). The water color seen from above the surface does not change

## Time of day and the sky (Clearwater Sky)

The sky's color is computed physically from how the atmosphere scatters sunlight. Air molecules scatter blue light strongly, so the daytime sky is blue, and in the evening only red light makes it through the long path of air. The white glare around the sun is scattering by fine particles, and the deep blue-violet after sunset is absorption by ozone. This computation runs once in the editor and is baked into a table (`Assets/Clearwater/Generated/SkyLUT.asset`); at runtime the table is only looked up.

Changing the time changes not only the sky's color but also the direction and color of the sunlight, the ambient light on avatars and the world, and the sky's reflections. At night a full moon shines from opposite the sun, and the stars and the Milky Way turn around the north celestial pole.

### Usage

In scenes made with Build Scene, the sun (Sun) has a "Clearwater Sky" component. For existing scenes, running `Tools > Clearwater > Use Clearwater Sky and Sun` once adds it. Scenes that keep the world's own sun (when you chose "No" in Add to Current Scene) do not get it and keep the fixed sky (an afternoon sky with the sun at 31°).

There are two ways to handle the time.

- Stop the time (`Cycle` off, default): the sky stays at `Time of day`
- Let the time pass (`Cycle` on): when the instance opens, it starts at `Time of day`, and a day passes in `Day minutes` minutes (default 24). The instance owner distributes the start time, so people who join later see the same sky

Changing values in the Inspector shows up immediately in the Scene view and Game view. With the defaults (16:30, latitude 35° N, summer solstice), the sun's position and light match the fixed sky.

| Setting | Description |
| --- | --- |
| `Time of day` | The time. Local solar time, with the sun highest at 12:00 |
| `Cycle` / `Day minutes` | Whether the time passes / real time a day takes (minutes) |
| `Latitude` / `Day of year` | Latitude / day counted from January 1. They set the sun's height and the length of day and twilight |
| `North` | Which way north is in the world (angle clockwise from +Z). The `North as the fixed sky's` button returns it to the default |
| `Moon` | Shows a full moon. It lights the night |
| `Night brightness` | Brightness of a moonlit night (relative to day; default 0.05) |
| `Stars` | Brightness of the stars and the Milky Way |
| `Underwater Glow` | On nights with neither sun nor moon lighting, a faint blue-green brightness left in the water when seen from underwater (default 1; 0 is pitch dark, 3 is quite bright) |
| `Update interval` / `Probe interval` | When the time passes, how often the sky is updated (default 0.1 s) / how often the reflections are redrawn (10 s) |

The night brightness includes the eye's adaptation to the dark. The amount of light drops to about one millionth from day to a moonlit night, but the screen does not go pitch dark: a moonlit night is about 5% as bright as day, with faded, bluish colors. Before sunrise and after sunset, the afterglow on the horizon looks bright against the darkened scenery.

### Changing it inside the world

Running `Tools > Clearwater > Add Sky Control Panel` places a panel and a pebble (Sky Stone) on the beach in front of the spawn point. The panel has, on the left under "Sky", sliders for the time, the amount of cloud, how fast the clouds drift (Cloud drift, 0–60 m/s) and the length of a day (A day in), plus a toggle for whether the time passes (Day goes by); on the right under "Waves", sliders for the height of the shore waves (Shore waves), the strength of the offshore ripples (Ripples) and the ripples' speed (Ripple speed), plus a button that returns everything to the initial state (Reset all). The panel is normally hidden; Interact with the pebble (shown as Sky & Waves) and it appears above the pebble, a little below eye level, facing you. Move the pebble wherever you like. If they already exist, they are rebuilt with the pebble left in the same place.

- Anyone can use it, and the changed sky is the same for everyone in the instance. The person who uses it becomes the owner of Clearwater Sky and distributes the values, so people who join later see the current sky
- While the time passes, the time slider moves along with it. Moving the slider midway restarts the time from there
- The length of a day has 17 steps from 1 minute to 24 hours (the same as real time). Changing it does not make the time jump; it continues from the current time at the new speed
- You can call up the panel anywhere without going to the pebble. In VR, pull the left trigger twice quickly and a smaller panel (0.4 times its world size) appears above your left hand and follows it (operate it with your right hand). On desktop, the Tab key shows it 1.2 m in front of you. Doing the same again hides it. The pebble's `Call Anywhere` turns this off, and you can also change the key (`Desktop Key`), the interval for the double pull (`Double Tap Time`), the size on the hand (`Hand Size`) and the distance on desktop (`Desktop Distance`)
- Whether the panel is shown or hidden is per viewer. It hides when you Interact with the pebble again or move 6 m away from the panel (the pebble's `Close Distance`; at 0 it does not hide when you move away). To show it where it was placed, turn off the pebble's `Show Above`. To always show it, enable the panel and delete the pebble
- Changing the cloud speed does not make the clouds jump; they drift from where they are at the new speed
- The shore wave height (Shore waves) ranges from 0 to twice the built value (30 cm if that is under 30 cm). Breaking waves, run-up, foam, wet sand and the loudness of the surf sound change together. At 0 you get the same calm shoreline as with the Coast's `Shore waves` off (foam and wet sand disappear too)
- The offshore ripples (Ripples) are the strength of the sea surface's motion and of the caustics on the bottom. At 0% the sea is a mirror-like calm. Pool surfaces do not change
- The ripple speed (Ripple speed) is 0–200%. Changing it does not make the pattern jump; it moves from its current shape at the new speed. The surface waves are shared with pools, so pool waves get the same speed
- The panel's initial values (the values at world start and after Reset all) can all be changed in the Inspector of "Sky & Waves Settings (Clearwater)", which is created with the panel: time, Day goes by and day length (actually the values of Clearwater Sky on the Sun), and cloud amount, cloud drift, shore waves, ripples and ripple speed. Clouds and waves are written into the related materials (sky, water surface, bottom, underwater, etc.) as you change them, so the Scene view updates immediately
- Reset all returns everyone to these initial values together (the time it returns to is the Clearwater Sky value when the scene was saved)
- Clearwater Sky syncs the time, and Sky & Waves Settings syncs the clouds and waves (two synced objects, so two network IDs)
- The panel's visuals (child Face) and the pebble's visuals (child Look) are on the UI layer, so they do not show in VRChat's cameras (for photos and streaming); they show only when UI is enabled in the camera settings. The parents that receive input (the panel's VRC Ui Shape, the pebble's collider) are not on the UI layer, because objects on the UI layer can only be touched while the VRChat menu is open
- The panel's parent and the pebble are on the Walkthrough layer, so avatars pass through them without colliding
- The panel's text is TextMesh Pro, and sliders and other shapes are drawn with VRChat's supersampled UI shader (`VRChat/Mobile/Worlds/Supersampled UI`), so edges stay crisp in VR too. If the project lacks TextMesh Pro's Essential Resources, they are imported when the panel is created (`Assets/TextMesh Pro` appears)

To change things from your own Udon, call Clearwater Sky's `SetHour(hour)`, `SetCycle(true/false)`, `SetDayMinutes(minutes)`, `ResetTime()` and Sky & Waves Settings' (`ClearwaterSettings`) `SetClouds(0–1)`, `SetCloudDrift(m/s)`, `SetShoreWaves(m)`, `SetRipples(0–1)`, `SetRippleSpeed(multiplier)`, `ResetAll()`. All of them sync to everyone.

### What changes along with it

- The sun's Directional Light: direction, color and intensity. At night it becomes moonlight
- Ambient light on avatars and the world: Environment Lighting in Lighting is set to Gradient, and its three colors (sky, horizon, ground) change with the time
- Sky reflections: "Sky Reflection (Clearwater)", a reflection probe under the sun that reflects only the sky, is redrawn (every 10 seconds when the time passes)

Reflection probes and lightmaps that the world placed itself keep the brightness they were baked with. If they look too bright in a night scene, remove them or rebake them for night. To bring the sky's light into buildings, use the next section.

### The sky's light in buildings (VRC Light Volumes)

Sky light baked into lightmaps stays at that brightness whatever the time. In a world with [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes), you can bake the sky's light alone into an additive Light Volume, and Clearwater Sky changes its color and strength with the time. The sky's light through the windows is white by day, orange in the evening, and moonlight at night.

1. Place a Light Volume over the building and make it `Additive`. A second one over the rooms only, on top of it, lets you brighten just the inside (`Sky Light Gains` below)
2. Bake it with the sky alone. With Bakery, set the Skylight to white, intensity 1, `Hemispherical` (the upper half only), and switch every other light off. For the ground's bounce, put sand- or sea-colored boards under it while baking
3. Turn that Light Volume's `Bake` off (the sky light it was baked with stays), and bake the lightmaps and the other Light Volumes again with only the light that does not change with the time, such as lamps
4. Put that Light Volume (its LightVolumeInstance) into `Sky Light Volumes` of the Clearwater Sky on the Sun

| Setting | What it does |
| --- | --- |
| `Sky Light Volumes` | The additive Light Volumes baked with the sky alone |
| `Sky Light Gains` | A multiplier per Light Volume (same order; 1 if missing). Indoors the eye gets used to far less light than outside: a Light Volume over the rooms only at 3 to 4 times lights them as they look, not as they measure |
| `Sky Light Horizon` | How much of the light comes from near the horizon (windows, under eaves); default 0.6. The rest comes from high in the sky |
| `Sun Bounce` | Sunlight given back by the ground and floors, as a part of the sunlight on level ground; default 0.15. A room the sun shines into is brightened by it |

- The sunlight itself (the light and shadows through the windows) is drawn in real time by the sun's Directional Light
- Do not use light probes indoors. With baked probes, avatars outside are lit by them too, and the ambient light of the time does not reach them. Light Volumes light the avatars indoors
- The building materials' shaders must add additive Light Volumes over lightmaps (Filamented: turn `VRC Light Volumes` on; Mochie Standard adds them by default)

### Limitations

- The moon is always full and exactly opposite the sun. There are no phases and no lunar eclipses
- The amount of cloud does not change with the time (it stays at `Clouds` in the Sky material)
- The constellations are not the real night sky. Only the number of stars and their brightness distribution match the real one

## Tone mapping (post-processing / in shaders)

Tone mapping rolls colors that are too bright into the range the screen can show. Clearwater has two methods.

- Post-processing (PPv2, default): the whole screen is tone mapped at once with Post Processing Stack v2. The whole scene shares one tone curve, and bloom makes the sun and the glints on the water glow. What the water picks up from the screen (your own terrain, avatars in the water) can be used at the brightness it was drawn. On the other hand, avatars are tone mapped too, and their colors look slightly duller and darker. Post-processing is not available on Quest
- In shaders: the water, Seabed, sky and underwater fog shaders each tone map their own output. The world needs no post-processing, and avatars look unchanged. However, colors the water picks up from the screen are brought back to their original brightness by running the tone curve backward. As a result, near-white colors of objects that are not tone mapped (sunlit Standard ground, for example) become many times brighter. Bright terrain such as pool tiles and quay walls looks blown out to white through the water

Build Scene, Add to Current Scene and the demo scenes use post-processing (in shaders in projects without the Post Processing package). `Tools > Clearwater > Tone Mapping > In Shaders` switches to in shaders, and `In Post-processing (PPv2, default)` switches to post-processing. Switching to post-processing turns off `Tone map in shader` in Clearwater's materials. A global Post-process Volume is added on the "PostProcessing" layer. If that layer does not exist, a free user layer is named "PostProcessing". A Post-process Layer is added to the world's Reference Camera.

The profile is `Assets/Clearwater/Generated/ClearwaterPost.asset`. It contains a LUT baked from the same tone curve as the shaders (`ClearwaterToneLut.asset`, Color Grading in External mode) and a weak bloom (intensity 0.15, threshold 2; only truly bright spots such as the sun and its glints glow faintly). Colors differ from in-shader tone mapping by 1/255 or less on average.

- Adjust brightness with the profile's `Post-exposure`; bloom can be adjusted there too. Switching again does not overwrite the profile
- After changing a material's `Exposure`, choose `In Post-processing` again to rebake the LUT

PPv2's built-in ACES is not used. It rolls off bright areas differently, and looking at the sun from underwater makes the white area spread widely.

## Cameras right at the water surface

When a camera (head, screen or photo camera) is within 60 cm of the water surface, whether each pixel is above or below the water is decided per pixel. When the lens straddles the surface, the image splits into above water at the top and underwater (with fog) at the bottom. During this, the water surface and underwater processing cost a little more.

## Translucent parts of avatars in the water

The water surface is drawn before other transparent objects and does not write depth. So translucent clothes and hair on avatars are not hidden by the water at any render queue. Clothes do not vanish underwater and leave the body visible.

How they look depends on the render queue.

| Avatar material | How the underwater part looks |
| --- | --- |
| 2500 or lower (e.g. 2460, lilToon's default for transparent) | Tinted by the water color and refracted |
| 2501 or higher (e.g. 3000) | Not tinted by the water; looks as if above the water |

Seen from underwater, 3050 and below are hazed by the underwater fog, and 3051 and above are seen without haze. Transparent objects placed in the world (bubble particles, etc.) are treated the same way. They are drawn after the water surface, so even when underwater they appear on top of the water.

## VRC Light Volumes

In a world with [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes) (RED_SIM), the light of additive Light Volumes and of Point Light Volumes also falls on the beach, the bottom and the foam, and the water surface shows its reflection. It is the same light that lights the avatars, so a lamp or a fire on the beach at night lights the avatars and the sand beside it alike.

![The beach at night: a lamp on the sand lights the sand, a lamp over the shallows lights the bottom and the water surface, and a blue lamp in the water and a lamp that changes colour glow](Documentation~/images/light-volumes.jpg)

Try it in VRChat: [Clearwater VRCLV demo](https://vrchat.com/home/world/wrld_364c34d1-7e60-4df8-9039-3248d77f2813) (the demo in "Trying the demo" below, published as a world)

- It works without the Light Volumes package: the part the shaders read (`LightVolumes.cginc`, MIT) is included. In a world without Light Volumes, neither the look nor the cost changes
- Only additive Light Volumes and Point Light Volumes are read. A Light Volume that is not additive holds the brightness of the hour it was baked at, which does not match the time-of-day sky (at night the sand alone would stand out at daylight brightness), so it is not read. Put light that does not change with the hour (lamps, fires, the lights of a building) in additive or Point Light Volumes
- Light reaching the bottom under the water is dimmed by the water as much as the sky's light is. Lights placed under the water are treated the same way
- Your own terrain (Terrain source: User) is lit if its material's shader supports Light Volumes. Of the wetness and foam Clearwater lays over it, the foam is lit

### Trying the demo

In a project with Light Volumes, Build Demo Scenes ("Trying the demo scenes") also makes the demo in the image above, Demo_LightVolumes. Open it with `Tools > Clearwater > Demo Scenes > Open Light Volumes (VRC Light Volumes)`. It is the beach at 21:00, with these lights as Point Light Volumes.

| Light | What to look for |
| --- | --- |
| A lamp on the sand (warm) | The sand, and the foam left by the run-up, lit |
| A lamp over the shallows (warm) | The bottom under the water lit, and a path of its reflection on the water |
| Two lamps in the water (blue; in the shallows and 1.6 m down) | The bottom lit from within the water, seen from above the water and from under it |
| A spot over the beach | The ring of light of a spot pointing down |
| The moving lamp | Goes round a 4 m circle across the waterline every 24 seconds, its colour and brightness always changing. The light on the sand, the foam and the bottom follows it, and where the water is deep it dips under the surface (`ClearwaterDemoLamp`; its speed and range can be changed in the Inspector) |

Each lamp has a small glowing ball to show where it is. The time can be changed with the sky panel (the Tab key, or two pulls of the left trigger), to compare the lights by day and by night.

## Performance

Measured with package 1.0.0 on an RTX 4070 Ti SUPER. In the Unity editor, the same view was drawn repeatedly at 2048×2048 with a 90° field of view (close to one VR eye), timing until the GPU finished (with the Unity window in the foreground). The scene is the one Build Scene makes (16:00, 30% clouds), without post-processing. Both VR eyes take roughly twice these values.

| View | Cost (per eye) |
| --- | --- |
| In the shallows, looking at your feet | about 5.7 ms |
| Above the water, looking down at the open sea (water filling the view) | about 4.8 ms |
| From the beach, looking out to sea | about 2.8 ms |
| Underwater, looking up at the surface | about 2.6 ms |

The conditions below add to those values. Each is the difference measured by switching only that condition on and off, alternately.

| Condition | Added cost (per eye) |
| --- | --- |
| A pool's water covering most of the screen (compared with not drawing that water; pools not on screen cost nothing) | +1.3–1.7 ms |
| `Foam relief` at 2 cm, looking at the shoreline up close | +0.45–0.9 ms (depending on how much foam there is at the time) |
| Tone mapping in post-processing (compared with in the shaders; bloom included) | +0.35–0.5 ms |
| Clouds (Cover above 0) | +0.25 ms |
| The time-of-day sky (Clearwater Sky, compared with the fixed sky) | +0.1 ms looking up at the sky by day, +0.15 ms looking up at the night sky |
| Sun shadows (drawing them, and the beach and bottom reading them; they cannot be turned off, as the water uses the depth information) | +0.1–0.15 ms |
| Your own terrain (for the Projectors) | +0.15 ms or less |
| Stamps | +0.05 ms or less |
| Looking near two VRC Light Volumes Point Light Volumes (nothing is added in a world without Light Volumes) | +0.1–0.4 ms |

## Remaking the wave audio

`Tools~/` (a folder Unity does not import) contains the steps for processing the surf sound. ffmpeg is required.

- `Tools~/make_wave_audio.sh <source recording.flac>`: makes the three loops (`Runtime/Audio/*.ogg`) from the source recording
- `node Tools~/detect_wave_breaks.js`: finds the moments waves break in the shoreline sound and makes the wave timetable (`Runtime/Audio/WavesShore_breaks.json`). Always run it after replacing the surf sound

## License

MIT License (`LICENSE`, © 2026 bmbb93 (vbamboo)). The water rendering is ported from clearwater by Aureliengmz (MIT, © Lumaris, `Third Party Notices.md`). The VRC Light Volumes shader include (`Runtime/Shaders/ThirdParty/LightVolumes.cginc`) is by RED_SIM, under the MIT License.

## Credits

- Water rendering: clearwater by Aureliengmz (MIT, © Lumaris)
- Surf sound: Freesound "Stromboli beach" by nicola_ariutti (CC0)
- Reading the light of VRC Light Volumes: VRC Light Volumes by RED_SIM (MIT)
- Bed textures: both are generated procedurally, with no photos (the pebbles by the original's `tools/make_pebbles.py`, the sand by `Tools~/make_sand.py`)
