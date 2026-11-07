using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace TotemMod
{
    internal sealed class MenuSkin
    {
        public Sprite Background = null, Button = null, ButtonHighlighted = null, ButtonDisabled = null;
        public Sprite Slider = null, SliderHighlighted = null, Handle = null, HandleHighlighted = null;
        public AudioClip Click = null;
    }

    internal sealed class MenuEntry
    {
        public readonly Func<string> Text;
        public readonly Action<int> Activate;
        public readonly bool Stepper;
        public readonly Func<bool> Enabled;
        public readonly Func<float> Value;
        public readonly Action<float> SetValue;
        public readonly float Minimum, Maximum, Step;
        public readonly bool IsSlider;

        public MenuEntry(Func<string> text, Action<int> activate, bool stepper = false, Func<bool> enabled = null)
        {
            Text = text; Activate = activate; Stepper = stepper; Enabled = enabled;
        }

        public MenuEntry(Func<string> text, Func<float> value, Action<float> setValue,
            float minimum, float maximum, float step, Func<bool> enabled = null)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (setValue == null) throw new ArgumentNullException("setValue");
            if (float.IsNaN(minimum) || float.IsInfinity(minimum) || float.IsNaN(maximum) ||
                float.IsInfinity(maximum) || maximum <= minimum || step <= 0 || float.IsNaN(step) || float.IsInfinity(step))
                throw new ArgumentException("Sliders require a finite increasing range and positive step.");
            Text = text; Value = value; SetValue = setValue;
            Minimum = minimum; Maximum = maximum; Step = step; Enabled = enabled; IsSlider = true;
        }
    }

    internal sealed class MenuPage
    {
        public readonly string Title;
        public readonly MenuEntry[] Entries;
        public MenuPage(string title, MenuEntry[] entries)
        {
            Title = title; Entries = entries ?? new MenuEntry[0];
        }
    }

    internal sealed class MenuCategory
    {
        public readonly string Title;
        public readonly MenuPage[] Pages;

        public MenuCategory(string title, MenuPage[] pages)
        {
            Title = title; Pages = pages ?? new MenuPage[0];
        }
    }

    // A stationary world-space canvas. Input is supplied by the owner; this view
    // never creates an EventSystem, raycaster, collider, or camera attachment.
    internal sealed class TotemMenu : IDisposable
    {
        private sealed class Target
        {
            public RectTransform Rect;
            public Image Face;
            public Text Label;
            public MenuEntry Entry;
            public int Direction;
            public int Category = -1;
            public int PageDirection;
            public bool Done;
            public Image Handle;
        }

        private sealed class Row
        {
            public MenuEntry Entry;
            public Text Label;
        }

        private static readonly Color Brown = new Color32(43, 31, 22, 255);
        private static readonly Color Panel = new Color32(31, 25, 20, 255);
        private static readonly Color Button = new Color32(105, 105, 105, 255);
        private static readonly Color DisabledButton = new Color32(61, 61, 61, 255);
        private static readonly Color FocusButton = new Color32(122, 122, 133, 255);
        private static readonly Color Yellow = new Color32(255, 255, 160, 255);
        private static readonly Color DisabledText = new Color32(146, 146, 146, 255);
        private readonly Func<Font> fontProvider;
        private readonly Func<string> statusProvider;
        private readonly MenuCategory[] categories;
        private readonly MenuNavigation navigation;
        private readonly MenuSkin skin;
        private readonly List<Target> targets = new List<Target>(32);
        private readonly List<Row> rows = new List<Row>(8);
        private GameObject root;
        private RectTransform rootRect;
        private RectTransform rowRoot;
        private RectTransform reticle;
        private Text status;
        private Text pageTitle;
        private Font font;
        private Canvas canvas;
        private Target focused;
        private Target hovered;
        private Target done;
        private Target dragging;
        private readonly Image[] reticleEdges = new Image[4];
        private readonly MenuPointerState pointerState = new MenuPointerState();
        private LineRenderer laser;
        private Material overlayMaterial;
        private AudioSource clickSource;
        private GameObject audioRoot;
        private float scale = 1;
        private float menuDistance = 1.1f;
        private float clickVolume = 0.3f;
        private float pointerWidth = 1;
        private float pointerSmoothing = 1;
        private Color pointerColor = new Color32(80, 235, 255, 255);
        private int layer;
        private double nextRefresh;
        private bool open;
        private bool disposed;

        public TotemMenu(Func<Font> fontProvider, Func<string> statusProvider, MenuPage[] pages, MenuSkin skin = null)
            : this(fontProvider, statusProvider, LegacyCategories(pages), skin)
        {
        }

        public TotemMenu(Func<Font> fontProvider, Func<string> statusProvider, MenuCategory[] categories, MenuSkin skin = null)
        {
            if (fontProvider == null) throw new ArgumentNullException("fontProvider");
            this.fontProvider = fontProvider;
            this.statusProvider = statusProvider;
            this.categories = categories ?? new MenuCategory[0];
            this.skin = skin ?? new MenuSkin();
            int[] pageCounts = new int[this.categories.Length];
            for (int i = 0; i < this.categories.Length; i++)
            {
                MenuCategory category = this.categories[i];
                if (category == null) throw new ArgumentException("Menu categories cannot be null.", "categories");
                pageCounts[i] = category.Pages.Length;
                for (int j = 0; j < category.Pages.Length; j++)
                {
                    if (category.Pages[j] == null) throw new ArgumentException("Menu pages cannot be null.", "categories");
                    if (category.Pages[j].Entries.Length > 8) throw new ArgumentException("A menu page supports at most eight rows.", "categories");
                }
            }
            navigation = new MenuNavigation(pageCounts);
        }

        private static MenuCategory[] LegacyCategories(MenuPage[] pages)
        {
            if (pages == null) return new MenuCategory[0];
            MenuCategory[] result = new MenuCategory[pages.Length];
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null) throw new ArgumentException("Menu pages cannot be null.", "pages");
                result[i] = new MenuCategory(pages[i].Title, new[] { pages[i] });
            }
            return result;
        }

        public bool IsOpen { get { return open && root != null; } }

        public float Scale
        {
            get { return scale; }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                scale = Mathf.Clamp(value, 0.6f, 1.6f);
                if (IsOpen) rootRect.localScale = Vector3.one * (0.001f * scale);
            }
        }

        public Color PointerColor { get { return pointerColor; } set { pointerColor = value; } }

        public float MenuDistance
        {
            get { return menuDistance; }
            set { if (!float.IsNaN(value) && !float.IsInfinity(value)) menuDistance = Mathf.Clamp(value, 0.65f, 1.8f); }
        }

        public float ClickVolume
        {
            get { return clickVolume; }
            set { if (!float.IsNaN(value) && !float.IsInfinity(value)) clickVolume = Mathf.Clamp01(value); }
        }

        public float PointerWidth
        {
            get { return pointerWidth; }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                pointerWidth = Mathf.Clamp(value, 0.5f, 2);
                if (laser == null) return;
                laser.startWidth = 0.0010f * pointerWidth;
                laser.endWidth = 0.00025f * pointerWidth;
            }
        }

        public float PointerSmoothing
        {
            get { return pointerSmoothing; }
            set { if (!float.IsNaN(value) && !float.IsInfinity(value)) pointerSmoothing = Mathf.Clamp01(value); }
        }

        public void Recenter(Camera camera) { Open(camera); }

        public void Toggle(Camera camera)
        {
            if (IsOpen) Close(); else Open(camera);
        }

        public void Open(Camera camera)
        {
            if (disposed || camera == null || camera.cullingMask == 0) return;
            if (root == null) Create();
            canvas.worldCamera = camera;
            int visibleLayer = 5;
            if ((camera.cullingMask & (1 << visibleLayer)) == 0)
                for (int i = 0; i < 32; i++)
                    if ((camera.cullingMask & (1 << i)) != 0) { visibleLayer = i; break; }
            if (layer != visibleLayer)
            {
                layer = visibleLayer;
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            }
            Transform eye = camera.transform;
            rootRect.position = eye.position + eye.forward * menuDistance;
            rootRect.rotation = Quaternion.LookRotation(eye.forward, eye.up);
            rootRect.localScale = Vector3.one * (0.001f * scale);
            open = true;
            root.SetActive(true);
            reticle.gameObject.SetActive(false);
            hovered = null;
            dragging = null;
            pointerState.Reset();
            laser.enabled = false;
            Refresh();
            nextRefresh = 0;
        }

        public void Close()
        {
            open = false;
            hovered = null;
            dragging = null;
            pointerState.Reset();
            if (laser != null) laser.enabled = false;
            if (root != null) root.SetActive(false);
        }

        public void Tick(Ray pointer, bool pointerValid, bool clicked, double now, bool held = false)
        {
            if (!IsOpen) return;
            if (now >= nextRefresh)
            {
                Refresh();
                nextRefresh = now + 0.1;
            }
            Target hit = null;
            bool onPanel = false;
            bool onPlane = false;
            bool hadCapture = dragging != null;
            pointerValid = pointerValid && Finite(pointer.origin) && Finite(pointer.direction) && pointer.direction.sqrMagnitude > 0.000001f;
            Vector3 world = Vector3.zero;
            Vector2 aim = Vector2.zero;
            float distance;
            Plane plane = new Plane(rootRect.forward, rootRect.position);
            if (pointerValid && plane.Raycast(pointer, out distance) && distance >= 0 && distance <= 8)
            {
                onPlane = true;
                world = pointer.GetPoint(distance);
                Vector3 local = rootRect.InverseTransformPoint(world);
                aim = new Vector2(local.x, local.y);
                onPanel = rootRect.rect.Contains(aim);
            }
            // Do this before hit testing. The exact same point drives hover,
            // clicks, captured sliders, the hollow cursor and the beam endpoint.
            pointerState.Update(onPlane && (onPanel || hadCapture), aim.x, aim.y, now, pointerSmoothing);
            if (pointerState.Valid)
            {
                aim = new Vector2(pointerState.X, pointerState.Y);
                world = rootRect.TransformPoint(new Vector3(aim.x, aim.y, 0));
                onPanel = rootRect.rect.Contains(aim);
                if (onPanel && !hadCapture)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        Target candidate = targets[i];
                        Vector3 point = candidate.Rect.InverseTransformPoint(world);
                        if (candidate.Rect.rect.Contains(new Vector2(point.x, point.y)) && IsEnabled(candidate))
                        { hit = candidate; break; }
                    }
                }
            }
            if (dragging != null)
            {
                if (!held || !IsEnabled(dragging) || !targets.Contains(dragging))
                { Target old = dragging; dragging = null; Paint(old); }
                else if (pointerState.Valid) UpdateSlider(dragging, world);
            }
            // Capture remains attached to its slider until release, even when
            // tracking briefly drops or the hand passes across another control.
            if (dragging != null && pointerState.Valid) hit = dragging;
            if (hovered != hit)
            {
                Target old = hovered;
                hovered = hit;
                Paint(old);
                Paint(hovered);
            }
            if (clicked && pointerState.Valid && hit != null && !hadCapture)
            {
                pointerState.Click(now);
                Target old = focused;
                focused = hit;
                Paint(old);
                Paint(focused);
                if (hit.Entry != null && hit.Entry.IsSlider)
                {
                    dragging = held ? hit : null;
                    PlayClick();
                    UpdateSlider(hit, world);
                }
                else Activate(hit, hit.Direction);
            }
            if (!IsOpen) return;
            // Actions may synchronously replace the page and its targets.
            if (hit != null && !targets.Contains(hit)) hit = null;
            bool showReticle = pointerState.Valid && (onPanel || dragging != null);
            if (reticle.gameObject.activeSelf != showReticle) reticle.gameObject.SetActive(showReticle);
            if (showReticle)
            {
                reticle.anchoredPosition = aim;
                Color cursorColor = dragging != null ? Color.white : hit != null ? Yellow : pointerColor;
                for (int i = 0; i < reticleEdges.Length; i++) reticleEdges[i].color = cursorColor;
                float pulse = pointerState.Pulse(now);
                reticle.localScale = Vector3.one * ((hit != null || dragging != null ? 1.1f : 1) + pulse * 0.25f);
            }
            laser.enabled = pointerValid;
            if (pointerValid)
            {
                laser.startColor = new Color(pointerColor.r, pointerColor.g, pointerColor.b, 0.28f);
                laser.endColor = dragging != null ? Color.white : hit != null ? Yellow : pointerColor;
                laser.SetPosition(0, pointer.origin + pointer.direction.normalized * 0.025f);
                laser.SetPosition(1, showReticle ? world : pointer.GetPoint(1.4f));
            }
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public void MoveFocus(int direction)
        {
            if (!IsOpen || direction == 0 || targets.Count == 0) return;
            int current = targets.IndexOf(focused);
            int step = direction < 0 ? -1 : 1;
            if (current < 0) current = step > 0 ? -1 : 0;
            for (int i = 0; i < targets.Count; i++)
            {
                current = (current + step + targets.Count) % targets.Count;
                if (!IsEnabled(targets[current])) continue;
                Target old = focused;
                focused = targets[current];
                Paint(old);
                Paint(focused);
                break;
            }
        }

        public void ActivateFocused(int direction)
        {
            if (!IsOpen) return;
            if (focused == null || !IsEnabled(focused)) { MoveFocus(1); return; }
            Activate(focused, direction == 0 ? focused.Direction : (direction < 0 ? -1 : 1));
        }

        public void NextPage(int direction)
        {
            if (!IsOpen || navigation.PageCount < 2 || direction == 0) return;
            navigation.MovePage(direction);
            NavigationChanged(false);
        }

        public void NextCategory(int direction)
        {
            if (!IsOpen || categories.Length < 2 || direction == 0) return;
            navigation.MoveCategory(direction);
            NavigationChanged(true);
        }

        private bool IsEnabled(Target target)
        {
            return target != null && (target.PageDirection == 0 || navigation.PageCount > 1) && (target.Entry == null ||
                ((target.Entry.Activate != null || target.Entry.IsSlider) && (target.Entry.Enabled == null || target.Entry.Enabled())));
        }

        private void Activate(Target target, int direction)
        {
            if (!IsOpen || !IsEnabled(target)) { Refresh(); return; }
            if (target.Entry != null && target.Entry.IsSlider)
            {
                if (SetSlider(target, CurrentValue(target.Entry) + (direction < 0 ? -1 : 1) * target.Entry.Step)) PlayClick();
                return;
            }
            PlayClick();
            if (target.Done) { Close(); return; }
            if (target.Category >= 0) { SetCategory(target.Category); return; }
            if (target.PageDirection != 0) { NextPage(target.PageDirection); return; }
            target.Entry.Activate(target.Entry.Stepper ? (direction < 0 ? -1 : 1) : 1);
            if (IsOpen) Refresh();
        }

        private void SetCategory(int next)
        {
            if (next == navigation.Category) { Refresh(); return; }
            navigation.SelectCategory(next);
            NavigationChanged(true);
        }

        private void NavigationChanged(bool categoryChanged)
        {
            Target previousFocus = focused;
            hovered = null;
            dragging = null;
            pointerState.Reset();
            BuildRows();
            focused = !categoryChanged && targets.Contains(previousFocus) ? previousFocus : null;
            if (focused == null)
                for (int i = 0; i < targets.Count; i++)
                    if (targets[i].Category == navigation.Category && navigation.Category >= 0) { focused = targets[i]; break; }
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                SetText(row.Label, row.Entry.Text == null ? string.Empty : row.Entry.Text());
                if (!row.Entry.IsSlider && (row.Entry.Activate == null || row.Entry.Stepper))
                {
                    bool enabled = row.Entry.Enabled == null || row.Entry.Enabled();
                    Color color = enabled ? Color.white : DisabledText;
                    if (row.Label.color != color) row.Label.color = color;
                }
            }
            SetText(status, statusProvider == null ? string.Empty : statusProvider());
            SetText(pageTitle, navigation.Page < 0 ? "No pages" :
                categories[navigation.Category].Pages[navigation.Page].Title + "  " + (navigation.Page + 1) + "/" + navigation.PageCount);
            if (!IsEnabled(focused)) focused = null;
            if (!IsEnabled(hovered)) hovered = null;
            for (int i = 0; i < targets.Count; i++) Paint(targets[i]);
        }

        private static float CurrentValue(MenuEntry entry)
        {
            float value = entry.Value();
            if (float.IsNaN(value) || float.IsInfinity(value)) value = entry.Minimum;
            return Mathf.Clamp(value, entry.Minimum, entry.Maximum);
        }

        private bool SetSlider(Target target, float value)
        {
            if (!IsEnabled(target)) return false;
            MenuEntry entry = target.Entry;
            value = Mathf.Clamp(value, entry.Minimum, entry.Maximum);
            if (value > entry.Minimum && value < entry.Maximum)
                value = Mathf.Clamp(entry.Minimum + Mathf.Round((value - entry.Minimum) / entry.Step) * entry.Step, entry.Minimum, entry.Maximum);
            if (Mathf.Abs(CurrentValue(entry) - value) < entry.Step * 0.0001f) return false;
            entry.SetValue(value);
            if (IsOpen) Refresh();
            return true;
        }

        private void UpdateSlider(Target target, Vector3 world)
        {
            if (!IsEnabled(target)) { dragging = null; return; }
            float width = target.Rect.rect.width;
            float handle = target.Handle.rectTransform.rect.width;
            float localX = target.Rect.InverseTransformPoint(world).x;
            float fraction = Mathf.Clamp01((localX + (width - handle) * 0.5f) / (width - handle));
            MenuEntry entry = target.Entry;
            SetSlider(target, Mathf.Lerp(entry.Minimum, entry.Maximum, fraction));
        }

        private void PlayClick()
        {
            if (clickSource != null && skin.Click != null) clickSource.PlayOneShot(skin.Click, clickVolume);
        }

        private void Paint(Target target)
        {
            if (target == null) return;
            bool enabled = IsEnabled(target);
            bool selected = enabled && (target == focused || target == hovered || target == dragging);
            bool slider = target.Entry != null && target.Entry.IsSlider;
            Sprite sprite = slider ? (selected && skin.SliderHighlighted != null ? skin.SliderHighlighted : skin.Slider) :
                (!enabled && skin.ButtonDisabled != null ? skin.ButtonDisabled :
                ((selected || target.Category >= 0 && target.Category == navigation.Category) && skin.ButtonHighlighted != null ? skin.ButtonHighlighted : skin.Button));
            if (target.Face.sprite != sprite) target.Face.sprite = sprite;
            Color face = !enabled ? DisabledButton : selected ? FocusButton : Button;
            if (target.Category == navigation.Category && target.Category >= 0 && !selected) face = new Color32(96, 88, 64, 255);
            if (sprite != null) face = enabled || !slider && skin.ButtonDisabled != null ? Color.white : DisabledText;
            if (target.Face.color != face) target.Face.color = face;
            Color text = !enabled ? DisabledText : selected ? Yellow : Color.white;
            if (target.Label.color != text) target.Label.color = text;
            if (target.Handle != null)
            {
                Sprite handleSprite = selected && skin.HandleHighlighted != null ? skin.HandleHighlighted : skin.Handle;
                if (target.Handle.sprite != handleSprite) target.Handle.sprite = handleSprite;
                target.Handle.color = enabled ? (handleSprite == null ? (selected ? Yellow : Color.white) : Color.white) : DisabledText;
                float fraction = Mathf.InverseLerp(target.Entry.Minimum, target.Entry.Maximum, CurrentValue(target.Entry));
                float travel = target.Rect.rect.width - target.Handle.rectTransform.rect.width;
                Vector2 position = new Vector2((fraction - 0.5f) * travel, 0);
                if (target.Handle.rectTransform.anchoredPosition != position) target.Handle.rectTransform.anchoredPosition = position;
            }
        }

        private void Create()
        {
            font = fontProvider();
            if (font == null) throw new InvalidOperationException("The settings menu font is unavailable.");
            if (audioRoot != null) UnityEngine.Object.Destroy(audioRoot);
            if (overlayMaterial != null) UnityEngine.Object.Destroy(overlayMaterial);
            targets.Clear(); rows.Clear();
            focused = null; hovered = null; dragging = null;
            rowRoot = null; reticle = null;
            root = new GameObject("Totem Settings", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(880, 700);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32750;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1;
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Image background = root.AddComponent<Image>();
            overlayMaterial = new Material(background.defaultMaterial);
            overlayMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            background.material = overlayMaterial;
            background.sprite = skin.Background;
            background.type = skin.Background == null ? Image.Type.Simple : Image.Type.Tiled;
            background.color = skin.Background == null ? Brown : (Color)new Color32(100, 100, 100, 255);
            if (skin.Background != null) background.pixelsPerUnitMultiplier = 0.25f;
            background.raycastTarget = false;
            // Keep the short local click alive when Done deactivates the canvas.
            audioRoot = new GameObject("Totem Settings Sound");
            clickSource = audioRoot.AddComponent<AudioSource>();
            clickSource.playOnAwake = false;
            clickSource.spatialBlend = 0;
            clickSource.ignoreListenerPause = true;
            GameObject beam = new GameObject("Controller pointer");
            beam.transform.SetParent(rootRect, false);
            laser = beam.AddComponent<LineRenderer>();
            laser.sharedMaterial = overlayMaterial;
            laser.useWorldSpace = true;
            laser.positionCount = 2;
            laser.startWidth = 0.0010f * pointerWidth;
            laser.endWidth = 0.00025f * pointerWidth;
            laser.shadowCastingMode = ShadowCastingMode.Off;
            laser.receiveShadows = false;
            laser.sortingOrder = 32760;
            laser.enabled = false;
            MakeImage("Title backing", rootRect, 0, 299, 880, 102, Panel);
            MakeImage("Footer backing", rootRect, 0, -296, 880, 108, Panel);
            MakeText("Title", rootRect, "Totem Settings", 0, 307, 800, 48, 34);
            int count = categories.Length;
            float tabWidth = count == 0 ? 0 : (808f - (count - 1) * 8) / count;
            for (int i = 0; i < count; i++)
            {
                Target tab = MakeButton("Category " + i, rootRect, categories[i].Title, -404 + tabWidth * 0.5f + i * (tabWidth + 8), 245, tabWidth, 44, count > 6 ? 17 : 20);
                tab.Category = i;
                targets.Add(tab);
            }
            MakeImage("Page backing", rootRect, 0, 195, 808, 37, Panel);
            Target previousPage = MakeButton("Previous page", rootRect, "<", -375, 195, 58, 37, 24);
            previousPage.PageDirection = -1; targets.Add(previousPage);
            pageTitle = MakeText("Page title", rootRect, string.Empty, 0, 195, 658, 37, 21);
            Target nextPage = MakeButton("Next page", rootRect, ">", 375, 195, 58, 37, 24);
            nextPage.PageDirection = 1; targets.Add(nextPage);
            MakeImage("Status backing", rootRect, 0, -251, 808, 24, Panel);
            status = MakeText("Status", rootRect, string.Empty, 0, -251, 792, 24, 16);
            done = MakeButton("Done", rootRect, "Done", 0, -289, 350, 46, 24);
            done.Done = true;
            MakeText("Controls", rootRect, "Point + trigger / drag | arrows + Enter | Tab: page | Shift+Tab: category", 0, -331, 844, 28, 16);
            BuildRows();
            reticle = MakeRect("Pointer", rootRect, 0, 0, 12, 12);
            // A Minecraft-style hollow square keeps the aimed-at text visible.
            MakeImage("Pointer top shadow", reticle, 0, 5, 14, 3, Color.black);
            MakeImage("Pointer bottom shadow", reticle, 0, -5, 14, 3, Color.black);
            MakeImage("Pointer left shadow", reticle, -5, 0, 3, 8, Color.black);
            MakeImage("Pointer right shadow", reticle, 5, 0, 3, 8, Color.black);
            reticleEdges[0] = MakeImage("Pointer top", reticle, 0, 5, 12, 1.4f, pointerColor);
            reticleEdges[1] = MakeImage("Pointer bottom", reticle, 0, -5, 12, 1.4f, pointerColor);
            reticleEdges[2] = MakeImage("Pointer left", reticle, -5, 0, 1.4f, 8, pointerColor);
            reticleEdges[3] = MakeImage("Pointer right", reticle, 5, 0, 1.4f, 8, pointerColor);
            reticle.gameObject.SetActive(false);
            Refresh();
        }

        private void BuildRows()
        {
            for (int i = targets.Count - 1; i >= 0; i--)
                if (targets[i].Entry != null || targets[i].Done) targets.RemoveAt(i);
            rows.Clear();
            if (rowRoot != null)
            {
                rowRoot.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(rowRoot.gameObject);
            }
            rowRoot = MakeRect("Page rows", rootRect, 0, 0, 880, 700);
            if (navigation.Page >= 0)
            {
                MenuEntry[] entries = categories[navigation.Category].Pages[navigation.Page].Entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    MenuEntry entry = entries[i];
                    if (entry == null) continue;
                    float y = 145 - i * 51;
                    Text label;
                    if (entry.IsSlider)
                    {
                        Target slider = MakeSlider("Slider " + i, rowRoot, entry, y);
                        targets.Add(slider);
                        label = slider.Label;
                    }
                    else if (entry.Activate == null)
                    {
                        label = MakeText("Information " + i, rowRoot, string.Empty, 0, y, 800, 45, 21);
                    }
                    else if (entry.Stepper)
                    {
                        MakeImage("Setting backing " + i, rowRoot, 0, y, 808, 45, Panel);
                        Target minus = MakeButton("Decrease " + i, rowRoot, "-", -375, y, 58, 45, 26);
                        minus.Entry = entry; minus.Direction = -1; targets.Add(minus);
                        label = MakeText("Setting " + i, rowRoot, string.Empty, 0, y, 658, 45, 22);
                        Target plus = MakeButton("Increase " + i, rowRoot, "+", 375, y, 58, 45, 26);
                        plus.Entry = entry; plus.Direction = 1; targets.Add(plus);
                    }
                    else
                    {
                        Target button = MakeButton("Action " + i, rowRoot, string.Empty, 0, y, 808, 45, 22);
                        button.Entry = entry; button.Direction = 1; targets.Add(button);
                        label = button.Label;
                    }
                    rows.Add(new Row { Entry = entry, Label = label });
                }
            }
            targets.Add(done);
            if (reticle != null) reticle.SetAsLastSibling();
        }

        private Target MakeButton(string name, RectTransform parent, string text, float x, float y, float width, float height, int size)
        {
            if (skin.Button != null)
            {
                Image faceSprite = MakeImage(name, parent, x, y, width, height, Color.white);
                faceSprite.sprite = skin.Button;
                faceSprite.type = Image.Type.Sliced;
                return new Target { Rect = faceSprite.rectTransform, Face = faceSprite,
                    Label = MakeText("Label", faceSprite.rectTransform, text, 0, 0, width - 14, height - 6, size) };
            }
            Image border = MakeImage(name, parent, x, y, width, height, Color.black);
            RectTransform rect = border.rectTransform;
            MakeImage("Top", rect, 0, height * 0.5f - 2, width - 4, 2, new Color32(202, 202, 202, 255));
            MakeImage("Left", rect, -width * 0.5f + 2, 0, 2, height - 4, new Color32(202, 202, 202, 255));
            MakeImage("Bottom", rect, 0, -height * 0.5f + 2, width - 4, 2, new Color32(48, 48, 48, 255));
            MakeImage("Right", rect, width * 0.5f - 2, 0, 2, height - 4, new Color32(48, 48, 48, 255));
            Image face = MakeImage("Face", rect, 0, 0, width - 6, height - 6, Button);
            Text label = MakeText("Label", rect, text, 0, 0, width - 14, height - 6, size);
            return new Target { Rect = rect, Face = face, Label = label };
        }

        private Target MakeSlider(string name, RectTransform parent, MenuEntry entry, float y)
        {
            Image track = MakeImage(name, parent, 0, y, 808, 45, skin.Slider == null ? DisabledButton : Color.white);
            track.sprite = skin.Slider;
            track.type = Image.Type.Sliced;
            Image handle = MakeImage("Handle", track.rectTransform, 0, 0, 28, 45, Color.white);
            handle.sprite = skin.Handle;
            handle.type = Image.Type.Sliced;
            Text label = MakeText("Label", track.rectTransform, string.Empty, 0, 0, 766, 39, 22);
            return new Target { Rect = track.rectTransform, Face = track, Label = label, Entry = entry, Handle = handle, Direction = 1 };
        }

        private RectTransform MakeRect(string name, RectTransform parent, float x, float y, float width, float height)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.layer = layer;
            RectTransform rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private Image MakeImage(string name, RectTransform parent, float x, float y, float width, float height, Color color)
        {
            RectTransform rect = MakeRect(name, parent, x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.material = overlayMaterial;
            return image;
        }

        private Text MakeText(string name, RectTransform parent, string value, float x, float y, float width, float height, int size)
        {
            RectTransform rect = MakeRect(name, parent, x, y, width, height);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.material = overlayMaterial;
            text.text = value ?? string.Empty;
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color32(0, 0, 0, 230);
            shadow.effectDistance = new Vector2(2, -2);
            shadow.useGraphicAlpha = true;
            return text;
        }

        private static void SetText(Text label, string value)
        {
            value = value ?? string.Empty;
            if (label.text != value) label.text = value;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Close();
            if (root != null) UnityEngine.Object.Destroy(root);
            if (audioRoot != null) UnityEngine.Object.Destroy(audioRoot);
            if (overlayMaterial != null) UnityEngine.Object.Destroy(overlayMaterial);
            root = null; rootRect = null; rowRoot = null; reticle = null;
            status = null; pageTitle = null; font = null; canvas = null; focused = null; hovered = null; done = null;
            targets.Clear(); rows.Clear();
            overlayMaterial = null; audioRoot = null; clickSource = null; laser = null; dragging = null;
            for (int i = 0; i < reticleEdges.Length; i++) reticleEdges[i] = null;
        }
    }
}
