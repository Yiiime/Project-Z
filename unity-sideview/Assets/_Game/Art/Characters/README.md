# Islander portrait placeholder

`IslanderPortrait.png` is an original static NPC portrait generated with the built-in `image_gen` tool for the dialogue UI. It is a functional placeholder, not a finalized character design, Live2D model, or Live2D SDK asset.

- Output: 1024 × 1536, RGBA PNG; copied byte-for-byte from the selected generated output, without pixel editing.
- Alpha: range 0–254; 1,045,138 fully transparent pixels. Main character pixels are approximately 99% opaque in the tool output. Very low alpha remnants (1–4/255) exist outside the main silhouette; the generated alpha was preserved.
- Head and both hands are visible; character faces slightly right.
- Unity import: Sprite (Single), Full Rect, alpha transparency enabled, mipmaps disabled, uncompressed, maximum size 2048. The dialogue definition references this Sprite; no Live2D SDK is required.

## Initial generation prompt

Use case: illustration-story.
Asset type: a single static transparent NPC portrait placeholder for a Unity 2D narrative game; this is a functional placeholder, not a finalized character design or a Live2D model.
Subject: one original adult female island resident, approximately mid-20s, short-to-medium purple hair, a light lavender casual jacket over a pale inner top, and a modest dark skirt. Calm, friendly, natural expression and relaxed standing pose, turned slightly toward the viewer's right.
Style: polished Japanese 2D narrative-game character illustration, clean controlled linework, gentle cel shading, restrained details; harmonious with a seaside sunset environment.
Lighting and palette: warm sunset highlights and soft blue-violet ambient shadows, lavender clothing, no overpowering glow.
Composition: portrait orientation, one character centered, three-quarter-length figure from full head to knees. Entire crown of hair, both arms and both hands fully inside the canvas with comfortable transparent margins. Hands relaxed and anatomically readable. No props.
Background: truly transparent alpha, isolated character only. No scenery, floor, backdrop, frame, opaque white rectangle, or checkerboard pattern baked into pixels.
Constraints: no text, no logos, no watermark, no extra people, no cropped hands, no cropped head.

## Selected output refinement prompt

Use case: background-extraction. Edit the supplied generated NPC portrait into a clean transparent game UI cutout. Preserve the same adult woman's identity, face, purple hair, lavender jacket, pale top, dark skirt, pose, linework, body proportions and clothing colors. Remove ALL background pixels and all diffuse pink/orange/purple aura around the figure. There must be no soft halo, no gradient cloud, no backdrop shadow, and no glow outside the actual character silhouette. Fully transparent alpha outside the silhouette, with only narrow antialiased edges. Maintain warm lighting painted on the character only. Reframe slightly smaller within the same portrait-oriented canvas to provide at least 5 percent fully transparent clearance above the head, left and right of both arms/hands, and below the knees. Show the full head, both hands, and knees. Single character, no text, logos or props. Output a genuine transparent RGBA PNG, not a checkerboard rendering.

