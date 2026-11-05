using UnityEngine;
using System.Linq;

namespace Seabreeze
{
    // Replaceable sprite puppet: transforms supply idle, walk and airborne poses.
    public class CharacterVisual : MonoBehaviour
    {
        public int character;
        public bool walking;
        // World distance for one complete left/right cycle, calibrated to the production art.
        [Min(.1f)] public float playerStrideLength = 2.05f;
        [Range(0, .03f)] public float playerBobHeight = .012f;
        Transform rig, leftLeg, rightLeg, leftArm, rightArm, head;
        float phase;
        SpriteRenderer fullSprite;
        Sprite idleSprite, jumpSprite;
        Sprite[] walkFrames;
        float playerScale, walkWeight, bob, breath;
        bool facingLeft, usesEightFrameWalk;
        bool HasPlayerFrames => character == 0 && fullSprite != null && walkFrames != null && walkFrames.Length > 0;
        public string Pose { get; private set; } = "Idle";
        public void Build(int kind)
        {
            character = kind;
            rig = new GameObject("Painted character rig").transform;
            rig.SetParent(transform, false);
            string[] names = {"player", "mio", "aya", "chen"};
            var sprite = Resources.Load<Sprite>("Art/" + names[Mathf.Clamp(kind, 0, 3)]);
            if (sprite == null)
                sprite = Resources.LoadAll<Sprite>("Art/characters").FirstOrDefault(s => s.name == names[Mathf.Clamp(kind, 0, 3)]);
            idleSprite = sprite;
            if (kind == 0)
            {
                var legacyFrames = Resources.LoadAll<Sprite>("Art/player_walk").OrderBy(s => s.name).ToArray();
                if (legacyFrames.Length > 0) sprite = idleSprite = legacyFrames[0];
                jumpSprite = legacyFrames.Length > 0 ? legacyFrames[Mathf.Min(2, legacyFrames.Length - 1)] : idleSprite;
                var authoredWalk = Resources.LoadAll<Sprite>("Art/player_walk_v1_sheet").OrderBy(s => s.name).ToArray();
                // Accept only a completely sliced cycle; keep the original four-frame fallback.
                usesEightFrameWalk = authoredWalk.Length == 8;
                walkFrames = usesEightFrameWalk ? authoredWalk : legacyFrames;
            }
            if (sprite != null)
            {
                fullSprite = Part("Character artwork", sprite, Vector2.zero, new Vector2(2.6f, 2.6f), Color.white, 25, rig);
                fullSprite.transform.localScale = Vector3.one * (2.6f / sprite.bounds.size.y);
                fullSprite.transform.localPosition = new Vector3(0, 1.3f, 0);
                if (HasPlayerFrames)
                {
                    playerScale = 2.6f * sprite.pixelsPerUnit / sprite.rect.height;
                    fullSprite.transform.localScale = Vector3.one * playerScale;
                    fullSprite.transform.localPosition = Vector3.zero;
                }
                return;
            }

            Color ink = new Color(.08f, .12f, .21f), skin = new Color(.91f, .72f, .57f);
            Color coat = kind == 1 ? new Color(.56f, .44f, .64f) : kind == 2 ? new Color(.63f, .39f, .26f) : kind == 3 ? new Color(.38f, .46f, .43f) : new Color(.31f, .43f, .60f);
            leftLeg = Limb("Far trouser", new Vector2(-.12f, 1.03f), new Vector2(-.08f, -.81f), .18f, ink, 21, rig);
            rightLeg = Limb("Near trouser", new Vector2(.13f, 1.03f), new Vector2(.07f, -.81f), .2f, ink * 1.12f, 24, rig);
            Shape("Far shoe", new[]{new Vector2(-.26f, .08f), new Vector2(-.22f, .23f), new Vector2(-.04f, .22f), new Vector2(.08f, .12f), new Vector2(.07f, .07f)}, kind == 0 ? new Color(.7f, .76f, .80f) : ink, 23, rig);
            leftArm = Limb("Far sleeve", new Vector2(-.16f, 1.92f), new Vector2(-.12f, -.63f), .18f, coat * .8f, 22, rig);
            Shape("Jacket", new[]{new Vector2(-.26f, 2.02f), new Vector2(.19f, 2.04f), new Vector2(.32f, 1.76f), new Vector2(.26f, 1.08f), new Vector2(-.27f, 1.08f), new Vector2(-.33f, 1.75f)}, coat, 24, rig);
            Shape("Shirt", new[]{new Vector2(.04f, 2.01f), new Vector2(.19f, 2), new Vector2(.21f, 1.16f), new Vector2(.06f, 1.18f)}, new Color(.83f, .82f, .75f), 25, rig);
            if (kind == 2)
                Shape("Apron", new[]{new Vector2(-.2f, 1.8f), new Vector2(.19f, 1.8f), new Vector2(.29f, .98f), new Vector2(-.31f, .98f)}, new Color(.82f, .7f, .5f), 26, rig);
            if (kind == 1)
                Shape("Skirt", new[]{new Vector2(-.27f, 1.23f), new Vector2(.27f, 1.23f), new Vector2(.37f, .56f), new Vector2(-.37f, .56f)}, new Color(.2f, .22f, .34f), 26, rig);
            rightArm = Limb("Near sleeve", new Vector2(-.13f, 1.94f), new Vector2(.04f, -.69f), .21f, coat * 1.08f, 28, rig);
            Part("Hand", WorldArt.Oval, new Vector2(.04f, -.7f), new Vector2(.13f, .2f), skin, 29, rightArm);
            Part("Neck", WorldArt.Oval, new Vector2(.04f, 2.05f), new Vector2(.18f, .25f), skin, 25, rig);
            head = new GameObject("Head").transform;
            head.SetParent(rig, false);
            head.localPosition = new Vector3(.02f, 2.3f, 0);
            Part("Face", WorldArt.Oval, Vector2.zero, new Vector2(.42f, .49f), skin, 27, head);
            Shape("Profile", new[]{new Vector2(.1f, .13f), new Vector2(.25f, .03f), new Vector2(.29f, -.02f), new Vector2(.21f, -.05f), new Vector2(.2f, -.16f), new Vector2(.05f, -.23f)}, skin, 28, head);
            Color hair = kind == 3 ? new Color(.6f, .63f, .62f) : kind == 1 ? new Color(.18f, .16f, .29f) : new Color(.08f, .1f, .16f);
            Shape("Hair", new[]{new Vector2(-.25f, -.1f), new Vector2(-.29f, .12f), new Vector2(-.2f, .24f), new Vector2(-.26f, .27f), new Vector2(-.06f, .3f), new Vector2(.05f, .35f), new Vector2(.12f, .26f), new Vector2(.23f, .28f), new Vector2(.27f, .17f), new Vector2(.17f, .13f), new Vector2(.12f, .02f), new Vector2(.03f, .15f), new Vector2(-.08f, .06f), new Vector2(-.14f, -.16f)}, hair, 29, head);
            Part("Eye", WorldArt.Oval, new Vector2(.16f, .02f), new Vector2(.038f, .06f), ink, 30, head);
            if (kind == 3)
            {
                Part("Glasses", WorldArt.Oval, new Vector2(.16f, .02f), new Vector2(.11f, .09f), new Color(.3f, .33f, .3f, .55f), 31, head);
            }

            Shape("Near shoe", new[]{new Vector2(.03f, .08f), new Vector2(.04f, .22f), new Vector2(.22f, .22f), new Vector2(.36f, .12f), new Vector2(.35f, .07f)}, kind == 0 ? new Color(.76f, .79f, .84f) : ink, 29, rig);
            AttachShoe("Far shoe", leftLeg);
            AttachShoe("Near shoe", rightLeg);
        }

        void AttachShoe(string name, Transform leg)
        {
            var shoe = rig.Find(name);
            if (shoe == null)
                return;
            Vector3 local = shoe.localPosition - leg.localPosition;
            shoe.SetParent(leg, false);
            shoe.localPosition = local;
        }

        Transform Limb(string n, Vector2 from, Vector2 to, float width, Color color, int order, Transform p)
        {
            var t = new GameObject(n).transform;
            t.SetParent(p, false);
            t.localPosition = from;
            var r = Part(n + " sprite", WorldArt.Oval, to * .5f, new Vector2(width, to.magnitude + .1f), color, order, t);
            r.transform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(to.x, -to.y) * Mathf.Rad2Deg);
            return t;
        }

        void Shape(string n, Vector2[] points, Color color, int order, Transform p)
        {
            var s = WorldArt.Polygon(points);
            var g = new GameObject(n);
            g.transform.SetParent(p, false);
            Vector2 low = points[0], high = points[0];
            foreach (var point in points)
            {
                low = Vector2.Min(low, point);
                high = Vector2.Max(high, point);
            }

            g.transform.localPosition = (low + high) * .5f;
            var r = g.AddComponent<SpriteRenderer>();
            r.sprite = s;
            r.sharedMaterial = Resources.Load<Material>("Art/SpriteLit");
            r.color = color;
            r.sortingOrder = order;
        }

        SpriteRenderer Part(string n, Sprite sprite, Vector2 at, Vector2 size, Color c, int order, Transform parent)
        {
            return WorldArt.Sprite(n, sprite, at, size, c, order, parent);
        }

        public void Animate(float vx, bool grounded, float vy)
        {
            if (HasPlayerFrames)
            {
                AnimateLocomotion(vx, grounded, vy, Mathf.Abs(vx) * Time.fixedDeltaTime, Time.fixedDeltaTime);
                return;
            }
            if (rig == null)
                return;
            walking = Mathf.Abs(vx) > .1f;
            Pose = !grounded ? "Jump" : walking ? "Walk" : "Idle";
            if (walking)
                transform.localScale = new Vector3(Mathf.Sign(vx) * Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);
            phase += Time.fixedDeltaTime * (walking ? 9 : 1.6f);
            float swing = grounded && walking ? Mathf.Sin(phase) * 22 : grounded ? 0 : 12;
            rig.localPosition = new Vector3(0, (walking ? Mathf.Abs(Mathf.Sin(phase)) * .045f : Mathf.Sin(phase) * .012f), 0);
            if (fullSprite != null)
            {
                if (walkFrames != null && walkFrames.Length > 0)
                {
                    // The authored sheet contains neutral, two stride poses, and a raised-knee airborne pose.
                    int cycle = Mathf.FloorToInt(phase / (Mathf.PI * 2) * 4) % 4;
                    int stride = cycle == 1 ? 1 : cycle == 3 ? 3 : 0;
                    var frame = !grounded ? walkFrames[Mathf.Min(2, walkFrames.Length - 1)] : walking ? walkFrames[Mathf.Min(stride, walkFrames.Length - 1)] : idleSprite;
                    fullSprite.sprite = frame;
                    fullSprite.transform.localScale = Vector3.one * (2.6f / frame.bounds.size.y);
                }

                rig.localEulerAngles = new Vector3(0, 0, walking ? Mathf.Sin(phase) * 1.4f : 0);
                return;
            }

            leftLeg.localEulerAngles = new Vector3(0, 0, swing);
            rightLeg.localEulerAngles = new Vector3(0, 0, -swing);
            leftArm.localEulerAngles = new Vector3(0, 0, -swing * .6f);
            rightArm.localEulerAngles = new Vector3(0, 0, swing * .6f);
        }

        public void AnimateLocomotion(float vx, bool grounded, float vy, float distance, float dt)
        {
            if (!HasPlayerFrames)
            {
                Animate(vx, grounded, vy);
                return;
            }
            float speed = Mathf.Abs(vx);
            bool wasWalking = walking;
            walking = grounded && speed > (walking ? .04f : .1f);
            Pose = !grounded ? "Jump" : walking ? "Walk" : "Idle";
            bool changedFacing = false;
            if (speed > .15f && facingLeft != (vx < 0))
            {
                facingLeft = vx < 0;
                changedFacing = true;
            }
            if (usesEightFrameWalk && ((walking && !wasWalking) || changedFacing))
                phase = .25f; // Passing pose joins the neutral idle with a small silhouette change.
            else if (changedFacing)
                phase = 0;
            fullSprite.flipX = facingLeft;
            if (grounded && walking)
                phase = Mathf.Repeat(phase + distance / Mathf.Max(.1f, playerStrideLength), 1);
            else if (speed < .04f)
                phase = usesEightFrameWalk ? .25f : 0;

            float blend = 1 - Mathf.Exp(-dt / .07f);
            walkWeight = Mathf.Lerp(walkWeight, walking ? Mathf.Clamp01(speed / 1.4f) : 0, blend);
            int beat = Mathf.RoundToInt(phase * 4) % 4;
            int stride = beat == 1 ? 1 : beat == 3 ? 3 : 0;
            if (usesEightFrameWalk)
                fullSprite.sprite = !grounded ? jumpSprite : walking ? walkFrames[Mathf.FloorToInt(phase * 8) % 8] : idleSprite;
            else
                fullSprite.sprite = !grounded ? jumpSprite :
                    speed > .8f ? walkFrames[Mathf.Min(stride, walkFrames.Length - 1)] : idleSprite;
            // The authored cycle already contains its vertical weight shift and planted-foot baseline.
            float targetBob = grounded ? (.5f + .5f * Mathf.Cos(phase * Mathf.PI * 4)) * playerBobHeight * walkWeight : 0;
            bob = usesEightFrameWalk ? 0 : Mathf.Lerp(bob, targetBob, 1 - Mathf.Exp(-dt / .035f));
            breath += dt * 1.6f;
            rig.localPosition = new Vector3(0, bob, 0);
            rig.localRotation = Quaternion.identity;
            fullSprite.transform.localScale = usesEightFrameWalk && walking ? Vector3.one * playerScale :
                new Vector3(playerScale, playerScale * (1 + Mathf.Sin(breath) * .0015f * (1 - walkWeight)), playerScale);
        }

        public void ResetLocomotion()
        {
            if (!HasPlayerFrames) return;
            phase = usesEightFrameWalk ? .25f : 0;
            walkWeight = bob = 0;
            walking = false;
            Pose = "Idle";
            rig.localPosition = Vector3.zero;
            rig.localRotation = Quaternion.identity;
            fullSprite.sprite = idleSprite;
            fullSprite.transform.localScale = Vector3.one * playerScale;
        }

        void Update()
        {
            if (GetComponentInParent<PlayerController>() == null)
                Animate(0, true, 0);
        }
    }
}
