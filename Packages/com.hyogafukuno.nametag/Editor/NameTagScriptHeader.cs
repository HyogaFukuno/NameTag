using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace NameTag.Editor
{
    /// <summary>
    /// 名前タグを付けたスクリプトの 1 行目に「// Last assigned by ○○」を書き込む。
    /// フォルダにタグを付けた場合は、直下のスクリプトのうち自身にタグがないものにも書き込む(孫以降は対象外)。
    /// タグを外しても担当履歴が分かるよう、コメントは削除しない。
    /// </summary>
    static class NameTagScriptHeader
    {
        const string k_Prefix = "// Last assigned by ";

        static readonly byte[] s_Utf8Bom = { 0xEF, 0xBB, 0xBF };
        static readonly UTF8Encoding s_StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>タグを付けた・変えたアセットに応じて、スクリプトに担当者コメントを書き込む。</summary>
        public static void Apply(IEnumerable<string> guids, string tagName)
        {
            if (!NameTagSettings.IsValidName(tagName)) return;

            var paths = new List<string>();
            foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath))
            {
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path)) paths.AddRange(GetDirectChildScriptsWithoutOwnTag(path));
                else paths.Add(path);
            }

            WriteHeaders(paths, tagName);
        }

        /// <summary>
        /// 新しく作られたスクリプトに、すぐ上の親フォルダの名前タグで担当者コメントを書き込む。
        /// </summary>
        public static void ApplyToNewScript(string scriptPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(scriptPath);
            if (string.IsNullOrEmpty(guid) || !string.IsNullOrEmpty(NameTagAssignments.GetOwnTag(guid))) return;

            var index = scriptPath.LastIndexOf('/');
            if (index <= 0) return;

            var folderTag = NameTagAssignments.GetOwnTag(AssetDatabase.AssetPathToGUID(scriptPath.Substring(0, index)));
            if (!NameTagSettings.instance.IsRegistered(folderTag)) return;

            WriteHeaders(new[] { scriptPath }, folderTag);
        }

        static void WriteHeaders(IEnumerable<string> paths, string tagName)
        {
            var targets = paths.Distinct().Where(IsEditableScript).ToList();
            if (targets.Count == 0) return;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in targets)
                {
                    if (WriteHeader(path, tagName)) AssetDatabase.ImportAsset(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        /// <summary>フォルダ直下のスクリプトのうち、自身に名前タグが設定されていないもの。</summary>
        static IEnumerable<string> GetDirectChildScriptsWithoutOwnTag(string folderPath)
        {
            var physicalFolder = FileUtil.GetPhysicalPath(folderPath);
            if (!Directory.Exists(physicalFolder)) yield break;

            foreach (var file in Directory.GetFiles(physicalFolder, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var assetPath = $"{folderPath}/{Path.GetFileName(file)}";
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;

                // スクリプト自身のタグを優先する(未登録名でも本人が設定したものとして扱う)
                if (!string.IsNullOrEmpty(NameTagAssignments.GetOwnTag(guid))) continue;

                yield return assetPath;
            }
        }

        static bool IsEditableScript(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
            if (path.StartsWith("Assets/", StringComparison.Ordinal)) return true;

            // パッケージ内は、編集可能な embedded / local パッケージだけを対象にする
            var package = PackageInfo.FindForAssetPath(path);
            return package != null && (package.source == PackageSource.Embedded || package.source == PackageSource.Local);
        }

        /// <summary>1 行目の担当者コメントを追加または置き換える。書き換えた場合は true。</summary>
        static bool WriteHeader(string assetPath, string tagName)
        {
            var physicalPath = FileUtil.GetPhysicalPath(assetPath);
            if (!File.Exists(physicalPath)) return false;

            var bytes = File.ReadAllBytes(physicalPath);
            var hasBom = bytes.Length >= 3 && bytes[0] == s_Utf8Bom[0] && bytes[1] == s_Utf8Bom[1] && bytes[2] == s_Utf8Bom[2];
            var offset = hasBom ? 3 : 0;

            string text;
            try
            {
                text = s_StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                // Shift_JIS などで保存されたファイルを UTF-8 として書き戻すと文字化けするため触らない
                Debug.LogWarning($"[NameTag] UTF-8 以外の文字コードのため、担当者コメントを書き込みませんでした: {assetPath}");
                return false;
            }

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var header = k_Prefix + tagName;

            // 既存の担当者コメントがあれば置き換える(行を増やさない)
            var body = text;
            var firstLineEnd = text.IndexOf('\n');
            var firstLine = (firstLineEnd >= 0 ? text.Substring(0, firstLineEnd) : text).TrimEnd('\r');
            if (firstLine.StartsWith(k_Prefix, StringComparison.Ordinal))
            {
                if (firstLine == header) return false;
                body = firstLineEnd >= 0 ? text.Substring(firstLineEnd + 1) : string.Empty;
            }

            var result = Encoding.UTF8.GetBytes(header + newline + body);
            using (var stream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write))
            {
                if (hasBom) stream.Write(s_Utf8Bom, 0, s_Utf8Bom.Length);
                stream.Write(result, 0, result.Length);
            }
            return true;
        }
    }

    /// <summary>
    /// 新しく作られたスクリプトを検知する。
    /// OnWillCreateAsset は .meta を新しく作るときに呼ばれるため、git で .meta ごと取り込んだスクリプトは対象にならない。
    /// </summary>
    class NameTagScriptCreationWatcher : AssetModificationProcessor
    {
        const string k_MetaExtension = ".meta";

        static void OnWillCreateAsset(string assetName)
        {
            var path = assetName.EndsWith(k_MetaExtension, StringComparison.OrdinalIgnoreCase)
                ? assetName.Substring(0, assetName.Length - k_MetaExtension.Length)
                : assetName;
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;

            // この時点ではまだインポート前で GUID が引けないため、インポート後に処理する
            ScheduleApply(path, k_MaxRetries);
        }

        const int k_MaxRetries = 10;

        static void ScheduleApply(string path, int retries)
        {
            EditorApplication.delayCall += () =>
            {
                // インポートが終わっていなければ少し待ってやり直す
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) || EditorApplication.isUpdating)
                {
                    if (retries > 0) ScheduleApply(path, retries - 1);
                    return;
                }
                NameTagScriptHeader.ApplyToNewScript(path);
            };
        }
    }
}
