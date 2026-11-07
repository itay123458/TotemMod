using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace TotemMod
{
    // Uses the local GorillaNotifications WorldSpaceCanvas and JetBrains Mono
    // template assets. The HUD is local to the owner and never networked.
    internal sealed class TotemNotice : IDisposable
    {
        private GameObject root;
        private Text label;
        private CanvasGroup opacity;
        private Canvas canvas;
        private Transform rootTransform;
        private Transform[] hierarchy;
        private Camera attachedCamera;
        private int assignedLayer = -1;
        private AssetBundle bundle;
        private Stream stream;
        private bool ownsBundle;
        private Material textMaterial;
        private string message;
        private double until;
        private double since;
        private bool empty;
        private bool rechargeCountdown = true;
        private float scale = 1f;
        private double duration = 5;
        private Vector3 offset = EyeOffset;
        private int capacity = 5;
        private string timer;
        private bool regenerationNotice=true;
        internal static readonly Vector3 EyeOffset = new Vector3(-0.28f, -0.22f, 0.85f);

        public void Show(string text, double now, bool outOfTotems)
        {
            message = text; since = now; until = now + duration; empty = outOfTotems;
        }

        public void Configure(bool countdown,float scale,float duration,Vector3 offset,int capacity,string timer,bool regenerationNotice=true)
        {
            rechargeCountdown = countdown; this.scale = scale; this.duration = duration;
            this.offset = offset; this.capacity = capacity; this.timer = timer;
            this.regenerationNotice=regenerationNotice;
        }

        public void Tick(Camera camera, double now, int available, int seconds)
        {
            if (empty && available > 0)
            {
                message = regenerationNotice ? "Totem regenerated  |  " + available + "/" + capacity + "\nPress Y to equip" : null;
                empty = false; since = now; until = now + duration;
            }
            bool visibleMessage = message != null && ((empty && rechargeCountdown) || now < until);
            if (camera == null || !visibleMessage && string.IsNullOrEmpty(timer)) { Hide(); return; }
            if (root == null) Create();
            bool cameraChanged = attachedCamera != camera;
            if (cameraChanged)
            {
                rootTransform.SetParent(camera.transform, false);
                canvas.worldCamera = camera;
                attachedCamera = camera;
            }
            if (cameraChanged || rootTransform.localPosition != offset)
            {
                rootTransform.localPosition = offset;
                // Unity UI faces -Z: point +Z away from the eye along the offset.
                rootTransform.localRotation = Quaternion.LookRotation(offset, Vector3.up);
            }
            Vector3 localScale = Vector3.one * (0.001f * scale);
            if (rootTransform.localScale != localScale) rootTransform.localScale = localScale;
            int layer = (int)UnityLayer.FirstPersonOnly;
            if ((camera.cullingMask & (1 << layer)) == 0)
            {
                layer = 0;
                for (int i=0; i<32; i++) if ((camera.cullingMask & (1 << i)) != 0) { layer=i; break; }
            }
            if (assignedLayer != layer)
            {
                foreach (Transform part in hierarchy) part.gameObject.layer = layer;
                assignedLayer = layer;
            }
            string content = visibleMessage ? (empty && available == 0 && rechargeCountdown ? "Out of totems\nNext totem in " + seconds + "s" : message) : "";
            if (!string.IsNullOrEmpty(timer)) content += (content.Length > 0 ? "\n" : "") + "<color=#83E4EE>" + timer + "</color>";
            string nextText = "<color=#F4D26C>[TOTEM]</color> " + content;
            if (label.text != nextText) label.text = nextText;
            float alpha = !string.IsNullOrEmpty(timer) ? 1f : Mathf.Clamp01((float)((now-since)/0.12));
            if (!empty && string.IsNullOrEmpty(timer)) alpha *= Mathf.Clamp01((float)((until-now)/0.25));
            if (opacity.alpha != alpha) opacity.alpha = alpha;
            if (!root.activeSelf) root.SetActive(true);
        }

        public Font LoadFont(string name)
        {
            EnsureBundle();
            return bundle.LoadAsset<Font>(name);
        }

        private void EnsureBundle()
        {
            if (bundle == null)
            {
                foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                    if (loaded.name == "gnotifications") { bundle = loaded; break; }
                if (bundle == null)
                {
                    stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TotemMod.gnotifications");
                    if (stream == null) throw new InvalidOperationException("Notification template is missing.");
                    bundle = AssetBundle.LoadFromStream(stream);
                    ownsBundle = bundle != null;
                }
            }
            if (bundle == null) throw new InvalidOperationException("Cannot load notification template.");
        }

        private void Create()
        {
            EnsureBundle();
            GameObject prefab = bundle.LoadAsset<GameObject>("WorldSpaceCanvas");
            Font font = bundle.LoadAsset<Font>("JetBrainsMono-ExtraBold");
            if (prefab == null || font == null) throw new InvalidOperationException("Notification canvas/font is missing.");
            root = UnityEngine.Object.Instantiate(prefab);
            root.name = "Totem lower-left eye-facing notification";
            root.SetActive(false);
            rootTransform = root.transform;
            hierarchy = root.GetComponentsInChildren<Transform>(true);
            attachedCamera = null;
            assignedLayer = -1;
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(480, 140);
            rect.pivot = new Vector2(0.5f, 0.5f);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;
            opacity = root.GetComponent<CanvasGroup>();
            if (opacity == null) opacity = root.AddComponent<CanvasGroup>();
            opacity.interactable = false; opacity.blocksRaycasts = false;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3;
            GraphicRaycaster raycaster = root.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
            label = root.GetComponentInChildren<Text>(true);
            if (label == null) throw new InvalidOperationException("Notification label is missing.");
            RectTransform holder = (RectTransform)label.transform.parent;
            holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = Vector2.zero;
            holder.sizeDelta = rect.sizeDelta;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.font = font;
            label.fontSize = 23;
            label.resizeTextForBestFit = false;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = Color.white;
            label.supportRichText = true;
            textMaterial = new Material(label.material);
            textMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            label.material = textMaterial;
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
                Image image = graphic as Image;
                if (image != null) image.enabled = false;
            }
            Outline outline = label.GetComponent<Outline>();
            if (outline == null) outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0,0,0,0.9f);
            outline.effectDistance = new Vector2(1f,-1f);
        }

        public void Hide() { if (root != null && root.activeSelf) root.SetActive(false); }
        public void Clear() { Hide(); message = null; empty = false; }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(textMaterial);
            // Retain loaded assets if another mod began sharing this bundle.
            if (ownsBundle && bundle != null) bundle.Unload(false);
            if (stream != null) stream.Dispose();
            root = null; bundle = null; stream = null; message = null;
            canvas = null; rootTransform = null; hierarchy = null; attachedCamera = null; assignedLayer = -1;
        }
    }
}
