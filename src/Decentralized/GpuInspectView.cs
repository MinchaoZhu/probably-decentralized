using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Decentralized
{
    /// <summary>
    /// 放大镜检视显卡：和香烟一样把物品放大摆在检视台上，正反面各一张，下面是标签栏。
    /// 标签栏每行一个标签（型号、风扇、金手指、挡板、丝印、贴纸）：左边是卖家的说法，右边的按钮"指出"——
    /// 说法属实就没事，隐瞒了问题价格回到真实情况，指错了掉信誉。归自己的卡，有标签机时按钮改成"改"，随便改说法。
    /// 游戏没有给 mod 开放检视界面，这里不打补丁，只在检视台（InspectionUIManager.panel）下面挂一个自己的界面，
    /// 用一个常驻的 MonoBehaviour 每帧看一眼是否正在检视显卡。
    /// </summary>
    public sealed class GpuInspectView : MonoBehaviour
    {
        private const float RowH = 26f, CardScale = 3f, PanelTop = -112f, DetailH = 26f, GroupGap = 7f;
        private static readonly int RowCount = CardLabels.Rows.Length;
        private TextMeshProUGUI priceText;
        private static GpuInspectView instance;
        private RectTransform root, cardRoot;
        private Button flip;
        private TextMeshProUGUI flipText;
        private GameItem shown;
        private string shownSig;
        private bool hidGlass;
        private Transform buttonTemplate;
        private TMP_FontAsset font;
        private readonly TextMeshProUGUI[] rowName = new TextMeshProUGUI[16], rowText = new TextMeshProUGUI[16];
        private readonly Button[] rowButton = new Button[16];
        private readonly TextMeshProUGUI[] rowButtonText = new TextMeshProUGUI[16];
        private TextMeshProUGUI serialText;

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("pd_gpu_inspect_view");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GpuInspectView>();
        }

        public static void Remove()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        private void Update()
        {
            var m = InspectionUIManager.Instance;
            var item = m != null && m.panel != null && m.panel.activeSelf ? m.currentInspectingItem : null;
            if (item == null || !Items.IsGpu(item))
            {
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
                shown = null;
                if (hidGlass && m != null && m.magnifyingGlass != null) { m.magnifyingGlass.SetActive(true); hidGlass = false; }
                return;
            }
            Log.Guard("显卡检视大图", () =>
            {
                if (root == null) Build(m);
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                // 检视台自带的放大镜图标会压在大图下面，显卡用不着
                if (m.magnifyingGlass != null && m.magnifyingGlass.activeSelf) { m.magnifyingGlass.SetActive(false); hidGlass = true; }
                var keys = new List<string>();
                string sig = Items.LookKeys(item, keys) + "|" + string.Join(",", keys) + "|" + item.unitValue + "|" + Items.Exposed(item) + "|" + (Items.GetClaim(item)?.Encode() ?? "") + "|" + GeneralHelper.IsItemOwned(item) + "|" + HasLabeler();
                if (item != shown || sig != shownSig)
                {
                    shown = item;
                    shownSig = sig;
                    Redraw(item);
                    UpdateRows(item);
                }
            });
        }

        /// <summary>检视台上的叠加层：每个标签的每个取值对应一种细节，一一对应。</summary>
        public static List<string> BigOverlays(GameItem item)
        {
            var o = new List<string>();
            var u = Items.ToUnit(item);
            if (u.Dead) return o;
            var l = u.Appearance;
            string P = "gpu_big_ov_";
            if (!Items.IsBack(item))
            {
                int n = Items.FansOf(u);
                if (l.Fan == 1) o.Add(P + "fan_dust2_" + n);
                if (l.Fan == 2) { o.Add(P + "fan_dust1_" + n); o.Add(P + "fan_chip_" + n); }
                if (l.Fingers == 1) o.Add(P + "fin_dim");
                if (l.Fingers == 2) o.Add(P + "fin_oxide");
                if (l.Bracket == 1) o.Add(P + "brk_scratch");
            }
            else
            {
                string model = (l.Print == 1 && u.FakeOf != null ? u.FakeOf : u.Model).ToLowerInvariant();
                o.Add(P + "print_" + (l.Print == 2 ? "skew_" : l.Print == 1 ? "stamp_" : "normal_") + model);
                if (l.Sticker == 2) o.Add(P + "seal_missing");
            }
            return o;
        }

        private static bool HasLabeler() => EmporiumEntry.Instance != null && EmporiumEntry.Instance.GetAllOwnedItemsByTag("LABELER_TAG").Count > 0;

        private static void MakeRect(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.localPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        private TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, TextAlignmentOptions align, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.localPosition = pos;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        private Button MakeButton(Transform parent, string name, Vector2 pos, Vector2 size, out TextMeshProUGUI label)
        {
            var go = Instantiate(buttonTemplate.gameObject, parent, false);
            go.name = name;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localPosition = pos;
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            label = go.GetComponentInChildren<TextMeshProUGUI>();
            label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 24;
            // 去掉复制来的按钮上驱动文字的本地化组件，文字由这里设置
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string n = mb.GetType().Name;
                if (mb is Button || mb is Image || mb is TextMeshProUGUI) continue;
                if (n.Contains("Local") || n.Contains("Appraisal") || n.Contains("Send")) Destroy(mb);
            }
            return b;
        }

        private void Build(InspectionUIManager m)
        {
            root = new GameObject("_PdGpuInspection", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(m.panel.transform, false);
            root.sizeDelta = new Vector2(1920, 1080);
            root.localPosition = Vector3.zero;
            cardRoot = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            cardRoot.SetParent(root, false);
            cardRoot.localPosition = new Vector3(0, 85, 0);
            buttonTemplate = m.appraisalPanel.transform.Find("buttonSend");
            // 贴纸上的序列号：随卡变化，运行时用文字写在贴纸的空白处
            serialText = MakeText(root, "Serial", Vector2.zero, new Vector2(48 * CardScale, 8 * CardScale), TextAlignmentOptions.Center, 20);
            serialText.color = new Color(0.1f, 0.1f, 0.13f);
            serialText.fontStyle = FontStyles.Bold;
            serialText.overflowMode = TextOverflowModes.Overflow;
            serialText.rectTransform.sizeDelta = new Vector2(48 * CardScale, 14 * CardScale);
            if (buttonTemplate == null) return;
            font = buttonTemplate.GetComponentInChildren<TextMeshProUGUI>()?.font;
            if (font != null) serialText.font = font;
            // 标签区：深色底加浅色边框，和上面的大图分开。三组：四项真伪依据、型号、四项成色，最下面一行是标签价
            float panelH = RowCount * RowH + 10f + DetailH + GroupGap * 2, panelY = PanelTop - panelH / 2;
            MakeRect(root, "LabelFrame", new Vector2(0, panelY), new Vector2(648, panelH + 8), new Color(0.78f, 0.68f, 0.45f, 1f));
            MakeRect(root, "LabelBack", new Vector2(0, panelY), new Vector2(640, panelH), new Color(0.10f, 0.11f, 0.14f, 0.96f));
            MakeRect(root, "LabelDivider1", new Vector2(0, GroupTop(CardLabels.AuthRows) + GroupGap / 2 + 1), new Vector2(620, 2), new Color(0.78f, 0.68f, 0.45f, 0.8f));
            MakeRect(root, "LabelDivider2", new Vector2(0, GroupTop(CardLabels.AuthRows + 1) + GroupGap / 2 + 1), new Vector2(620, 2), new Color(0.78f, 0.68f, 0.45f, 0.5f));
            MakeRect(root, "LabelDivider3", new Vector2(0, PanelTop - 5 - RowH * RowCount - GroupGap * 2 - 1), new Vector2(620, 2), new Color(0.78f, 0.68f, 0.45f, 0.8f));
            priceText = MakeText(root, "LabelPrice", new Vector2(165, PanelTop - 5 - RowH * RowCount - GroupGap * 2 - DetailH / 2 - 2), new Vector2(300, DetailH), TextAlignmentOptions.Right, 21);
            priceText.color = new Color(1f, 0.85f, 0.45f);
            PledgerSkin.Fit(priceText);
            flip = MakeButton(root, "Flip", new Vector2(0, PanelTop - panelH - 24), new Vector2(200, 30), out flipText);
            flip.onClick.AddListener(() => { if (shown != null) Items.Flip(shown); });
            for (int i = 0; i < RowCount; i++)
            {
                int row = CardLabels.Rows[i];
                float y = GroupTop(i) - RowH / 2;
                rowName[i] = MakeText(root, "RowName" + i, new Vector2(-232, y), new Vector2(156, RowH), TextAlignmentOptions.Left, 20);
                rowText[i] = MakeText(root, "RowText" + i, new Vector2(24, y), new Vector2(300, RowH), TextAlignmentOptions.Left, 20);
                rowButton[i] = MakeButton(root, "RowButton" + i, new Vector2(245, y), new Vector2(110, RowH - 3), out rowButtonText[i]);
                rowButton[i].onClick.AddListener(() => Log.Guard("标签栏", () => OnRowButton(row)));
            }
        }

        /// <summary>第 i 行的上沿（三组之间各留一道间隔）。</summary>
        private static float GroupTop(int i) => PanelTop - 5 - RowH * i - (i >= CardLabels.AuthRows ? GroupGap : 0f) - (i >= CardLabels.AuthRows + 1 ? GroupGap : 0f);

        private static string RowKey(int row) => row == CardLabels.RowSpec ? "spec" : row == CardLabels.RowCount ? "count" : row == CardLabels.RowPin ? "pin" : row == CardLabels.RowFan ? "fan" : row == CardLabels.RowFingers ? "fingers"
            : row == CardLabels.RowBracket ? "bracket" : row == CardLabels.RowPrint ? "print" : row == CardLabels.RowSerial ? "serial" : "seal";

        private static string ValueText(int row, int value) => row == CardLabels.RowSpec ? "" : L.T("lbl." + RowKey(row) + "." + (row == CardLabels.RowSeal && value == 1 ? 0 : value));

        private static string RowName(int row) => L.T("lbl.row." + RowKey(row));

        /// <summary>现场有没有卖家 / 鉴定客户在：决定"指出"按钮是否出现。</summary>
        private static bool CanPoint(GameItem item)
        {
            var client = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
            if (client == null || GeneralHelper.IsItemOwned(item)) return false;
            if (client.clientIntent == StoreClient.ClientIntent.APPRAISAL_SERVICE) return true;
            return client.clientIntent == StoreClient.ClientIntent.SELL && !InspectionUIManager.IsConfrontLocked();
        }

        private void UpdateRows(GameItem item)
        {
            if (rowName[0] == null) return;
            var u = Items.ToUnit(item);
            var real = Claim.Truth(u);
            var claim = Items.GetClaim(item);
            int exposed = Items.Exposed(item);
            bool owned = GeneralHelper.IsItemOwned(item);
            bool canPoint = CanPoint(item), canEdit = owned && HasLabeler();
            for (int i = 0; i < RowCount; i++)
            {
                int row = CardLabels.Rows[i];
                rowName[i].text = RowName(row);
                rowName[i].color = i < CardLabels.AuthRows ? new Color(1f, 0.85f, 0.45f) : new Color(0.6f, 0.85f, 1f);   // 真伪行金黄，成色行浅蓝
                rowName[i].fontSize = 20; PledgerSkin.Fit(rowName[i]); PledgerSkin.Fit(rowText[i]);
                string text;
                Color color = new Color(0.96f, 0.98f, 1f);
                string Show(Claim c) => row == CardLabels.RowSpec ? DisplayModel(c.Model) : ValueText(row, c.Get(row));
                if (claim == null) text = Show(real);
                else if ((exposed & row) != 0)
                {
                    text = Show(real);
                    bool lied = row == CardLabels.RowSpec ? claim.Value.Model != real.Model : claim.Value.Get(row) != real.Get(row);
                    if (lied) color = new Color(1f, 0.45f, 0.4f);   // 卖家的说法只在机制里起作用，面板上不写
                    else color = new Color(0.5f, 1f, 0.55f);
                }
                else text = Show(claim.Value);
                rowText[i].text = text;
                rowText[i].color = color;
                bool editable = row == CardLabels.RowSpec || !CardLabels.IsAuth(row);
                bool hasButton = row == CardLabels.RowSpec || !CardLabels.IsAuth(row);
                rowButton[i].gameObject.SetActive(hasButton && (canEdit ? editable : canPoint && (exposed & row) == 0));
                rowButtonText[i].text = L.T(canEdit ? "lbl.edit" : row == CardLabels.RowSpec ? "lbl.point.model" : "lbl.point");
            }
            if (priceText != null) priceText.text = L.T("lbl.price", item.unitValue);
            if (flipText != null) flipText.text = L.T(Items.IsBack(item) ? "inspect.flip.front" : "inspect.flip.back");
            // 贴纸上的序列号：只在背面显示
            if (serialText != null)
            {
                serialText.gameObject.SetActive(Items.IsBack(item) && !u.Dead);
                serialText.text = CardLabels.SerialText(u);
                // 贴纸空白处中心在大图 (142, 91)：卡片坐标以图中心为原点，y 向上
                serialText.rectTransform.localPosition = new Vector3((142 - 96) * CardScale, 85 + (64 - 91) * CardScale, 0);
            }
        }

        private static string DisplayModel(string model) => Items.DisplayModel(model);

        private void OnRowButton(int row)
        {
            if (shown == null) return;
            var item = shown;
            if (GeneralHelper.IsItemOwned(item))
            {
                // 标签机：改说法，每按一次换下一个取值（型号是在八个型号里轮换）
                var u = Items.ToUnit(item);
                var cur = Items.GetClaim(item) ?? Claim.Truth(u);
                if (row == CardLabels.RowSpec)
                {
                    var models = Hardware.Specs.Values.Where(s => !s.Asic && !s.Scrap).Select(s => s.Model).ToList();
                    Items.EditClaim(item, row, 0, models[(models.IndexOf(cur.Model) + 1) % models.Count]);
                }
                else Items.EditClaim(item, row, row == CardLabels.RowSeal ? (cur.Get(row) == 0 ? 2 : 0) : row == CardLabels.RowBracket ? (cur.Get(row) + 1) % 2 : (cur.Get(row) + 1) % 3);
                EmporiumAudioPlayer.Instance?.audioLabeler?.Play();
                return;
            }
            int kind = Items.Point(item, row);
            Log.Info($"指出 {row}，结果 {kind}，报价 {item.unitValue}");
            if (kind == 0)
            {
                InspectionUIManager.Instance.OnConfrontFailed();
                DialogUIManager.Instance.StartDialogue(new Dialogue().SetText("CLIENT", L.T("point.none." + Random.Range(1, 3))));
            }
            else
            {
                InspectionUIManager.Instance.OnConfrontSuccess();
                DialogUIManager.Instance.StartDialogue(new Dialogue().SetText("CLIENT", L.T(kind == 2 ? "point.fake." + Random.Range(1, 3) : "point.worn." + Random.Range(1, 3))));
            }
        }

        private void Redraw(GameItem item)
        {
            foreach (Transform c in cardRoot) Destroy(c.gameObject);
            var keys = new List<string>();
            string baseKey = Items.LookKeys(item, keys);   // 小图键名只用来取底图（品牌与正反面）
            var layers = new List<string> { "gpu_big_" + baseKey.Substring("gpu_".Length) };
            layers.AddRange(BigOverlays(item));
            foreach (var k in layers)
            {
                var sprite = Items.ModSprite(k);
                if (sprite == null) continue;
                var go = new GameObject(k, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(cardRoot, false);
                rt.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);
                rt.localScale = Vector3.one * CardScale;
                var img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
            }
        }
    }
}
