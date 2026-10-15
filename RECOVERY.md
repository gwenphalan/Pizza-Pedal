# Pizza Pedal recovery

Recovered on October 15, 2026 from `unimatrix-03`:

`/mnt/external-4tb/Backup/Users/ussvo/Documents/Pizza Pedal`

The project targets **Unity 6000.1.0f1** and contains the GeoJSON road generator,
minimap, navigation/pathfinding, delivery/objective systems, and bicycle controls.
The original backup is unchanged.

The adjacent `Pizza Pedal - Copy` folder was also checked. It has five custom
scripts versus 26 in this snapshot, no additional custom scripts, and older
versions of the three shared scripts that differ. Its two additional planning
notes remain in the original backup. GitHub and both folders shared the initial
commit `2759a2a`; the later source had not been committed.

## Repository contents

- Restore the newer custom source, project assets, Unity metadata, package
  manifest/lock files, project settings, map data, and implementation notes.
- Keep `Assets/Packages/` excluded as in the original repository; import those
  third-party asset packs from the local recovery or original backup when needed.
- Exclude generated Unity caches, IDE files, logs, and Blender backup copies.
- Store the large test scene and HDR sky texture with Git LFS. Install Git LFS
  and run `git lfs pull` after cloning.
- Update the stale build-scene entry to the recovered
  `Assets/Scenes/Test Environment.unity` and its existing GUID.

## Validation

All 26 custom C# files compiled together in editor mode with Roslyn, the installed
Unity 6000.3.1 reference assemblies, and recovered compiled package dependencies.
Only unassigned-field warnings were emitted; Unity commonly serializes these
fields through the Inspector. This is a compile check, not a full Unity import,
player build, or gameplay test under the target 6000.1.0f1 editor.

The test scene references the road generator, GeoJSON processor, navigation
manager, and minimap controller. Script metadata, package JSON, staged file sizes,
and common secret patterns were checked before publishing.
