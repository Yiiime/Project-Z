# Skeletal Player Candidate

Independent candidate. Original Player, movement parameters and eight-frame resources are unchanged.

Use **Seabreeze > Candidates > Play Skeletal Comparison** in Unity. The project normally forces Street as its Play start scene; the candidate menu temporarily selects this scene and restores Street on Stop.

Controls: A/D or arrow keys for manual motion, release for Idle, Space for automatic/manual mode. Auto preview alternates 2.5s walking and 0.7s resting. No Jump or gameplay session is attached.

The visual Prefab has 17 shared bones, 6 real SpriteSkin meshes, and editable Idle/Walk Transform clips. No Sprite switching or image fades are used by the candidate. Actual displacement drives Walk CycleRate. The FacingRoot mirrors the entire visual rig; the physical root remains unchanged.

Stop refinement: RigFootContact runs after Animator and before SpriteSkin, holding one foot while the other takes a small settling step. Restart releases this constraint smoothly. Elbow/knee weights now blend across a wider region; wrist/ankle regions remain tighter. Sleeve caps share Chest influence to reduce shoulder separation.

Known candidate limitations: flat-ground contact targets (not a terrain/Jump IK system), fast full-speed cadence, limited shoe roll, visible source shoulder lines and reused near/far limb artwork. Do not treat this as approved replacement art.

Full production notes, original PSB/layers and actual Unity preview footage:
`ArtSource/Candidates/SkeletalPlayer/2026-11-04/README.md`.

The three Build menus regenerate candidate authoring data and overwrite candidate manual edits. They are not needed to play the already-authored assets.
