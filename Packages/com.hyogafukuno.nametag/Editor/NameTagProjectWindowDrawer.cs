using UnityEditor;
using UnityEngine;

namespace NameTag.Editor
{
    /// <summary>
    /// Project ウィンドウのアイコン右下に名前タグを描画する。
    /// </summary>
    [InitializeOnLoad]
    static class NameTagProjectWindowDrawer
    {
        const float k_ListRowMaxHeight = 20f;
        const float k_CornerRadius = 4f;
        const float k_HorizontalPadding = 3f;

        static readonly Color k_BackgroundColor = Color.white;
        static readonly Color k_BorderColor = new Color(0f, 0f, 0f, 0.35f);
        static readonly Color k_OwnTextColor = new Color(0.1f, 0.1f, 0.1f);
        static readonly Color k_InheritedTextColor = new Color(0.45f, 0.45f, 0.45f);

        static GUIStyle s_Style;
        static readonly GUIContent s_Content = new GUIContent();

        static NameTagProjectWindowDrawer()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            NameTagSettings.Changed += EditorApplication.RepaintProjectWindow;
        }

        static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;

            var result = NameTagResolver.Resolve(guid);
            if (!result.HasTag) return;

            if (s_Style == null)
            {
                s_Style = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    // 斜体などのスタイル指定はエディタフォントに該当データがないと OS フォントの読み込みが走り、
                    // Windows では存在しない macOS 用フォント(Menlo)の読み込み警告が出るため、通常スタイルに固定する
                    fontStyle = FontStyle.Normal,
                };
            }

            var isListMode = rect.height <= k_ListRowMaxHeight;
            if (isListMode)
            {
                // リスト表示ではアイコンが小さく名前と重なるため、行の右端に表示する
                s_Style.fontSize = 9;
                DrawTag(result, rect, rect.height - 2f, alignBottom: false);
            }
            else
            {
                // グリッド表示: アイコン領域(正方形)の右下に表示する
                var iconRect = new Rect(rect.x, rect.y, rect.width, rect.width);
                s_Style.fontSize = Mathf.Clamp(Mathf.RoundToInt(iconRect.width * 0.14f), 8, 12);
                DrawTag(result, iconRect, s_Style.fontSize + 5f, alignBottom: true);
            }
        }

        static void DrawTag(NameTagResolver.Result result, Rect area, float height, bool alignBottom)
        {
            s_Content.text = result.TagName;
            s_Style.normal.textColor = result.Inherited ? k_InheritedTextColor : k_OwnTextColor;

            var width = Mathf.Min(s_Style.CalcSize(s_Content).x + k_HorizontalPadding * 2f, area.width);
            var y = alignBottom ? area.yMax - height : area.y + (area.height - height) * 0.5f;
            var tagRect = new Rect(area.xMax - width - 1f, y, width, height);

            var radius = Mathf.Min(k_CornerRadius, height * 0.5f);
            GUI.DrawTexture(tagRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, k_BackgroundColor, 0f, radius);
            GUI.DrawTexture(tagRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, k_BorderColor, 1f, radius);
            // GUI.Label はマウスのホバー・押下に応じて hover/active の文字色(エディタ標準の薄いグレー)で描くため、
            // クリックすると文字が薄くなる。タグは操作対象ではないので、常に通常状態で描画する
            s_Style.Draw(tagRect, s_Content, false, false, false, false);
        }
    }
}
