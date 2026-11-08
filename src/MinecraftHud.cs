using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TotemMod
{
    // Local-only canvases: controller aiming intersects the panel plane directly.
    // Nothing is attached to the player rig, physics, networking or EventSystem.
    internal sealed class MinecraftHud : IDisposable
    {
        private sealed class Slot
        {
            internal RectTransform Rect;
            internal Image Icon;
            internal Text Count;
        }

        private readonly SurvivalAssets assets;
        private readonly Font font;
        private readonly Slot[] hotbar = new Slot[9];
        private readonly Slot[] slots = new Slot[36];
        private readonly Image[] hearts = new Image[10];
        private readonly Image[] hunger = new Image[10];
        private readonly MenuPointerState aim = new MenuPointerState();
        private readonly string[] counts = new string[65];
        private GameObject hud, panel, audioRoot;
        private RectTransform hudRect, panelRect, selection, focusRect, cursor, reticle;
        private Canvas hudCanvas, panelCanvas;
        private Transform[] hudHierarchy, panelHierarchy;
        private Material overlay;
        private AudioSource click;
        private LineRenderer laser;
        private Image offhand, offhandIcon, eatingFill;
        private Image cursorIcon;
        private Text offhandCount, cursorCount, message;
        private int layer = -1, focus, hovered = -1;
        private int inventoryRevision = -1, stateRevision = -1, lastTotems = -1;
        private bool lastEquipped, open, disposed;
        private double refreshAt;
        private float scale = 1;

        internal MinecraftHud(SurvivalAssets assets, Font font)
        {
            if (assets == null) throw new ArgumentNullException("assets");
            if (font == null) throw new ArgumentNullException("font");
            this.assets = assets; this.font = font;
            counts[0] = string.Empty;
            for (int i = 1; i < counts.Length; i++) counts[i] = i.ToString();
        }

        internal bool IsOpen { get { return open && panel != null; } }

        public float Scale
        {
            get { return scale; }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                scale = Mathf.Clamp(value, 0.6f, 1.6f);
                if (hudRect != null) hudRect.localScale = Vector3.one * (0.001f * scale);
            }
        }

        internal void Open(Camera camera)
        {
            if (disposed || camera == null || camera.cullingMask == 0) return;
            EnsureCreated();
            panelRect.position = camera.transform.position + camera.transform.forward * 0.85f;
            panelRect.rotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
            panelCanvas.worldCamera = camera;
            open = true;
            panel.SetActive(true);
            aim.Reset();
            hovered = -1;
            inventoryRevision = -1;
        }

        internal void Close(MinecraftInventory inventory)
        {
            if (!open) return;
            // A full inventory must never discard a stack carried by the cursor.
            if (inventory != null && !inventory.ReturnCursor()) return;
            open = false;
            aim.Reset(); hovered = -1;
            if (panel != null) panel.SetActive(false);
            if (laser != null) laser.enabled = false;
            inventoryRevision = -1;
        }

        internal void Toggle(Camera camera, MinecraftInventory inventory)
        {
            if (open) Close(inventory); else Open(camera);
        }

        internal void MoveFocus(int direction)
        {
            if (!open) return;
            // Screen rows are storage 9..35 followed by hotbar 0..8.
            int display = focus < 9 ? focus + 27 : focus - 9;
            display = ((display + direction) % 36 + 36) % 36;
            focus = display < 27 ? display + 9 : display - 27;
            hovered = -1;
            aim.Reset();
        }

        internal void Activate(MinecraftInventory inventory)
        {
            if (!open || inventory == null) return;
            inventory.Click(focus);
            PlayClick();
            inventoryRevision = -1;
        }

        internal void Tick(Camera camera, MinecraftInventory inventory, SurvivalState vitals, int totems,
            bool offhandEquipped, bool allowed, Ray pointer, bool pointerValid, bool triggerDown,
            bool triggerPressed, double now, float eatProgress)
        {
            if (disposed) return;
            if (!allowed || camera == null || camera.cullingMask == 0 || inventory == null || vitals == null)
            {
                if (hud != null) hud.SetActive(false);
                if (panel != null) panel.SetActive(false);
                aim.Reset();
                return;
            }
            EnsureCreated();
            if (!hud.activeSelf) hud.SetActive(true);
            if (panel.activeSelf != open) panel.SetActive(open);
            Pose(camera);
            if (open) Pointer(pointer, pointerValid, triggerDown, triggerPressed, inventory, now);
            if (inventoryRevision != inventory.Revision)
            {
                for (int i = 0; i < hotbar.Length; i++) UpdateSlot(hotbar[i], inventory.Get(i));
                for (int i = 0; i < slots.Length; i++) UpdateSlot(slots[i], inventory.Get(i));
                selection.anchoredPosition = new Vector2(-224 + inventory.Selected * 56, 0);
                UpdateCursor(inventory.Cursor);
                inventoryRevision = inventory.Revision;
            }
            if (stateRevision != vitals.Revision)
            {
                for (int i = 0; i < 10; i++)
                {
                    int health = vitals.Health - i * 2;
                    hearts[i].sprite = assets.Get(health >= 2 ? "heart-full" : health == 1 ? "heart-half" : "heart-container");
                    int food = vitals.Hunger - (9 - i) * 2;
                    hunger[i].sprite = assets.Get(food >= 2 ? "food-full" : food == 1 ? "food-half" : "food-empty");
                }
                stateRevision = vitals.Revision;
            }
            if (lastTotems != totems || lastEquipped != offhandEquipped)
            {
                offhand.color = offhandEquipped ? (Color)new Color32(255, 231, 142, 255) : Color.white;
                offhandIcon.color = totems > 0 ? Color.white : new Color(1, 1, 1, 0.3f);
                offhandCount.text = Count(Math.Max(0, totems));
                lastTotems = totems; lastEquipped = offhandEquipped;
            }
            if (now >= refreshAt)
            {
                string content = vitals.Dead ? "You died  |  Respawn in " + Math.Max(0, (int)Math.Ceiling(vitals.DeathUntil - now)) + "s" :
                    eatProgress > 0 ? "Eating..." : string.Empty;
                if (message.text != content) message.text = content;
                refreshAt = now + 0.1;
            }
            float progress = Mathf.Clamp01(eatProgress);
            eatingFill.gameObject.SetActive(progress > 0 && !vitals.Dead);
            eatingFill.rectTransform.sizeDelta = new Vector2(240 * progress, 4);
            if (open)
            {
                focusRect.anchoredPosition = slots[focus].Rect.anchoredPosition;
                if (inventory.Cursor.Count > 0)
                {
                    Vector2 position = aim.Valid ? new Vector2(aim.X, aim.Y) : slots[focus].Rect.anchoredPosition;
                    cursor.anchoredPosition = position + new Vector2(28, -26);
                }
            }
        }

        private void Pose(Camera camera)
        {
            Transform eye = camera.transform;
            hudCanvas.worldCamera = camera; panelCanvas.worldCamera = camera;
            // Independent local canvas follows the camera without modifying it.
            hudRect.position = eye.position + eye.rotation * new Vector3(0, -0.30f, 0.85f);
            hudRect.rotation = Quaternion.LookRotation(hudRect.position - eye.position, eye.up);
            if (open) panelRect.rotation = Quaternion.LookRotation(panelRect.position - eye.position, eye.up);
            int visible = 5;
            if ((camera.cullingMask & (1 << visible)) == 0)
                for (int i = 0; i < 32; i++) if ((camera.cullingMask & (1 << i)) != 0) { visible = i; break; }
            if (layer != visible)
            {
                layer = visible;
                for (int i = 0; i < hudHierarchy.Length; i++) hudHierarchy[i].gameObject.layer = layer;
                for (int i = 0; i < panelHierarchy.Length; i++) panelHierarchy[i].gameObject.layer = layer;
            }
        }

        private void Pointer(Ray pointer, bool valid, bool down, bool pressed, MinecraftInventory inventory, double now)
        {
            float distance = 0;
            Vector3 local = Vector3.zero;
            bool hit = valid && new Plane(panelRect.forward, panelRect.position).Raycast(pointer, out distance);
            if (hit)
            {
                hit = distance > 0 && distance < 3;
                if (hit) local = panelRect.InverseTransformPoint(pointer.GetPoint(distance));
                hit = hit && Mathf.Abs(local.x) <= 264 && Mathf.Abs(local.y) <= 153;
            }
            aim.Update(hit, local.x, local.y, now, 0.75f);
            hovered = -1;
            if (aim.Valid)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    Vector2 center = slots[i].Rect.anchoredPosition;
                    if (Mathf.Abs(aim.X - center.x) <= 24 && Mathf.Abs(aim.Y - center.y) <= 24) { hovered = i; break; }
                }
                if (hovered >= 0) focus = hovered;
                reticle.anchoredPosition = new Vector2(aim.X, aim.Y);
                reticle.localScale = Vector3.one * (down ? 0.8f : 1);
                laser.SetPosition(0, pointer.origin);
                laser.SetPosition(1, panelRect.TransformPoint(new Vector3(aim.X, aim.Y, -1)));
                if (pressed && hovered >= 0)
                {
                    aim.Click(now);
                    Activate(inventory);
                }
            }
            reticle.gameObject.SetActive(aim.Valid);
            laser.enabled = aim.Valid;
        }

        private string Count(int count) { return count < counts.Length ? counts[Math.Max(0, count)] : count.ToString(); }

        private void UpdateSlot(Slot slot, CraftStack stack)
        {
            Sprite sprite = assets.ItemSprite(stack.Item);
            slot.Icon.sprite = sprite;
            slot.Icon.enabled = sprite != null && stack.Count > 0;
            slot.Count.text = stack.Count > 1 ? Count(stack.Count) : string.Empty;
        }

        private void UpdateCursor(CraftStack stack)
        {
            cursor.gameObject.SetActive(stack.Count > 0);
            cursorIcon.sprite = assets.ItemSprite(stack.Item);
            cursorCount.text = stack.Count > 1 ? Count(stack.Count) : string.Empty;
        }

        private void PlayClick()
        {
            if (click != null && assets.ClickSound != null) click.PlayOneShot(assets.ClickSound, 0.3f);
        }

        private void EnsureCreated()
        {
            if (hud != null) return;
            hud = CanvasRoot("PrimalCraft Minecraft survival HUD", 680, 150, out hudCanvas, out hudRect);
            Image template = hud.AddComponent<Image>();
            overlay = new Material(template.defaultMaterial);
            overlay.name = "PrimalCraft survival overlay UI";
            overlay.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            template.enabled = false;
            ImageAt("Hotbar", hudRect, "hotbar", 0, 0, 509.6f, 61.6f);
            for (int i = 0; i < 9; i++) hotbar[i] = MakeSlot(hudRect, "Hotbar " + (i + 1), -224 + i * 56, 0, 44.8f);
            selection = ImageAt("Selected slot", hudRect, "hotbar-selection", -224, 0, 67.2f, 64.4f).rectTransform;
            // Icons/counts stay above the selection border in the transparent center.
            selection.SetSiblingIndex(2);
            for (int i = 0; i < 10; i++)
            {
                ImageAt("Heart container " + i, hudRect, "heart-container", -241 + i * 22.4f, 52, 25.2f, 25.2f);
                hearts[i] = ImageAt("Health " + i, hudRect, "heart-full", -241 + i * 22.4f, 52, 25.2f, 25.2f);
                ImageAt("Food container " + i, hudRect, "food-empty", 39.4f + i * 22.4f, 52, 25.2f, 25.2f);
                hunger[i] = ImageAt("Hunger " + i, hudRect, "food-full", 39.4f + i * 22.4f, 52, 25.2f, 25.2f);
            }
            offhand = ImageAt("Read-only totem pool", hudRect, "offhand", -302, 0, 81.2f, 67.2f);
            // The 29px sprite's actual 22px frame is left-aligned within it.
            offhandIcon = ImageAt("Equipped totem", hudRect, "totem", -311.8f, 0, 44.8f, 44.8f);
            offhandCount = Label("Totem count", hudRect, -306, -14, 44.8f, 24, 18, TextAnchor.MiddleRight);
            Label("Offhand shortcut", hudRect, -311.8f, -45, 86, 20, 13, TextAnchor.MiddleCenter).text = "Y: Totem";
            message = Label("Survival status", hudRect, 0, 93, 600, 30, 20, TextAnchor.MiddleCenter);
            eatingFill = Solid("Eating progress", hudRect, -120, 74, 240, 4, new Color32(124, 252, 110, 255));
            eatingFill.rectTransform.pivot = new Vector2(0, 0.5f);

            panel = CanvasRoot("PrimalCraft 36-slot inventory", 528, 306, out panelCanvas, out panelRect);
            // Inventory keeps a comfortable fixed angular size independently of
            // the HUD size preference, so its footer cannot grow into the HUD.
            panelRect.localScale = Vector3.one * 0.001f;
            Solid("Inventory header", panelRect, 0, 129, 528, 48, new Color32(198, 198, 198, 255));
            ImageAt("Original Minecraft top border", panelRect, "inventory-top", 0, 144, 528, 18);
            ImageAt("Original Minecraft storage frame", panelRect, "inventory-bottom", 0, -24, 528, 258);
            Text title = Label("Inventory title", panelRect, -12, 124, 458, 28, 22, TextAnchor.MiddleLeft);
            title.text = "Inventory"; title.color = new Color32(64, 64, 64, 255);
            focusRect = Solid("Focused slot", panelRect, 0, 0, 48, 48, new Color(1, 1, 1, 0.42f)).rectTransform;
            for (int i = 0; i < 36; i++)
            {
                int column = i % 9;
                float y = i < 9 ? -105 : 69 - ((i - 9) / 9) * 54;
                slots[i] = MakeSlot(panelRect, "Inventory slot " + i, -216 + column * 54, y, 48);
                if (i < 9)
                {
                    Text number = Label("Shortcut " + (i + 1), slots[i].Rect, -17, 18, 18, 17, 11, TextAnchor.MiddleCenter);
                    number.text = counts[i + 1]; number.color = new Color32(255, 255, 160, 255);
                }
            }
            Text controls = Label("Inventory controls", panelRect, 0, -179, 700, 30, 17, TextAnchor.MiddleCenter);
            controls.text = "Point + trigger: pick / place   |   Arrows + Enter   |   B / E: close";
            cursor = RectAt("Carried stack", panelRect, 0, 0, 48, 48);
            cursorIcon = ImageAt("Carried item", cursor, null, 0, 0, 48, 48);
            cursorCount = Label("Carried count", cursor, 0, -15, 48, 22, 18, TextAnchor.MiddleRight);
            cursor.gameObject.SetActive(false);
            reticle = RectAt("Inventory pointer", panelRect, 0, 0, 8, 8);
            Solid("Pointer top", reticle, 0, 4, 10, 1.5f, Color.white);
            Solid("Pointer bottom", reticle, 0, -4, 10, 1.5f, Color.white);
            Solid("Pointer left", reticle, -4, 0, 1.5f, 7, Color.white);
            Solid("Pointer right", reticle, 4, 0, 1.5f, 7, Color.white);
            GameObject beam = new GameObject("Inventory controller laser");
            beam.transform.SetParent(panelRect, false);
            laser = beam.AddComponent<LineRenderer>();
            laser.sharedMaterial = overlay; laser.useWorldSpace = true; laser.positionCount = 2;
            laser.startWidth = 0.0008f; laser.endWidth = 0.00025f;
            laser.startColor = new Color(1, 1, 1, 0.35f); laser.endColor = Color.white;
            laser.shadowCastingMode = ShadowCastingMode.Off; laser.receiveShadows = false;
            laser.sortingOrder = 32760; laser.enabled = false;
            audioRoot = new GameObject("PrimalCraft inventory click audio");
            UnityEngine.Object.DontDestroyOnLoad(audioRoot);
            click = audioRoot.AddComponent<AudioSource>();
            click.playOnAwake = false; click.spatialBlend = 0; click.ignoreListenerPause = true;
            hudHierarchy = hud.GetComponentsInChildren<Transform>(true);
            panelHierarchy = panel.GetComponentsInChildren<Transform>(true);
            inventoryRevision = stateRevision = lastTotems = -1;
            layer = -1;
            panel.SetActive(false);
        }

        private GameObject CanvasRoot(string name, float width, float height, out Canvas canvas, out RectTransform rect)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);
            rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * (0.001f * scale);
            canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32755; canvas.overrideSorting = true;
            canvas.targetDisplay = 0; canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 1;
            CanvasGroup group = root.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            return root;
        }

        private static RectTransform RectAt(string name, Transform parent, float x, float y, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private Image ImageAt(string name, Transform parent, string sprite, float x, float y, float width, float height)
        {
            Image image = RectAt(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.material = overlay; image.sprite = assets.Get(sprite); image.raycastTarget = false;
            return image;
        }

        private Image Solid(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            Image image = ImageAt(name, parent, null, x, y, width, height); image.color = color; return image;
        }

        private Text Label(string name, Transform parent, float x, float y, float width, float height, int size, TextAnchor anchor)
        {
            Text text = RectAt(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.alignment = anchor; text.color = Color.white;
            text.material = overlay; text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            Shadow shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.9f); shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private Slot MakeSlot(Transform parent, string name, float x, float y, float size)
        {
            Slot slot = new Slot();
            slot.Rect = RectAt(name, parent, x, y, size, size);
            slot.Icon = ImageAt("Item", slot.Rect, null, 0, 0, size, size);
            slot.Count = Label("Count", slot.Rect, 0, -14, size, 24, 19, TextAnchor.MiddleRight);
            return slot;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; open = false;
            UnityEngine.Object.Destroy(hud); UnityEngine.Object.Destroy(panel); UnityEngine.Object.Destroy(audioRoot);
            UnityEngine.Object.Destroy(overlay);
            hud = null; panel = null; audioRoot = null; overlay = null;
        }
    }
}
