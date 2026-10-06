using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace NameTag.Editor
{
    /// <summary>
    /// アセット(GUID)ごとの名前タグの割り当て。
    /// 1 件につき 1 ファイル(ProjectSettings/NameTags/&lt;GUID&gt;.txt)で保存するため、
    /// 別々のアセットへの割り当て変更が git 上でコンフリクトしない。
    /// </summary>
    public static class NameTagAssignments
    {
        public const string DirectoryPath = "ProjectSettings/NameTags";
        const string k_Extension = ".txt";
        const string k_NameKey = "name:";
        const string k_PathKey = "path:";

        static readonly Regex s_GuidPattern = new Regex("^[0-9a-f]{32}$");
        static readonly UTF8Encoding s_Utf8NoBom = new UTF8Encoding(false);

        // ファイル 1 件分の内容
        readonly struct Record
        {
            public readonly string TagName;
            public readonly string Path;

            public Record(string tagName, string path)
            {
                TagName = tagName;
                Path = path;
            }
        }

        static Dictionary<string, Record> s_Map;
        static long s_Signature;

        public readonly struct Entry
        {
            public readonly string Guid;
            public readonly string TagName;
            /// <summary>割り当てファイルに記録されているパス(アセットが存在しない場合の手がかり)。</summary>
            public readonly string RecordedPath;

            public Entry(string guid, string tagName, string recordedPath)
            {
                Guid = guid;
                TagName = tagName;
                RecordedPath = recordedPath;
            }
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return s_Map.Count;
            }
        }

        public static IEnumerable<Entry> All
        {
            get
            {
                EnsureLoaded();
                return s_Map.Select(kv => new Entry(kv.Key, kv.Value.TagName, kv.Value.Path)).ToList();
            }
        }

        /// <summary>アセット自身に設定されている名前タグ(未登録名も含む)。なければ null。</summary>
        public static string GetOwnTag(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            EnsureLoaded();
            return s_Map.TryGetValue(guid, out var record) ? record.TagName : null;
        }

        /// <summary>名前タグを設定して即座に保存する。null または空文字で解除。</summary>
        public static void SetTag(IEnumerable<string> guids, string tagName)
        {
            EnsureLoaded();

            var changedGuids = new List<string>();
            foreach (var guid in guids)
            {
                if (IsValidGuid(guid) && Write(guid, tagName)) changedGuids.Add(guid);
            }

            if (changedGuids.Count == 0) return;
            s_Signature = ComputeSignature();
            NameTagSettings.NotifyChanged();

            // タグを付けた・変えたスクリプト(フォルダなら直下のスクリプト)に担当者コメントを残す。外したときは残したままにする
            if (!string.IsNullOrEmpty(tagName)) NameTagScriptHeader.Apply(changedGuids, tagName);
        }

        public static void SetTag(string guid, string tagName) => SetTag(new[] { guid }, tagName);

        /// <summary>
        /// 現在のプロジェクトに存在しないアセットへの割り当てを返す。
        /// 判定はチェックアウト中のブランチの状態によるため、削除する前に利用者の確認を取ること。
        /// </summary>
        public static List<Entry> FindMissing()
        {
            return All.Where(e => !NameTagSettings.AssetExists(e.Guid)).ToList();
        }

        /// <summary>git pull などでファイルが外部から変更されていたら読み直す。</summary>
        public static void ReloadIfChangedOnDisk()
        {
            if (s_Map == null) return;
            if (ComputeSignature() == s_Signature) return;

            s_Map = null;
            NameTagSettings.NotifyChanged();
        }

        /// <summary>
        /// 割り当てファイルの path を現在のアセットパスに合わせる。アセットの移動後に呼ばれる。
        /// 食い違っているファイルだけを書き直すため、ブランチ切り替えなどで不要な差分は生まれない。
        /// </summary>
        internal static void SyncPaths()
        {
            EnsureLoaded();

            var changed = false;
            foreach (var kv in s_Map.ToList())
            {
                // 削除されたアセットは「存在しないアセットの割り当てを削除」で扱うため対象外
                if (!NameTagSettings.AssetExists(kv.Key)) continue;
                if (AssetDatabase.GUIDToAssetPath(kv.Key) == kv.Value.Path) continue;

                changed |= Write(kv.Key, kv.Value.TagName, force: true);
            }

            // path は表示に影響しないため Changed は発行しない
            if (changed) s_Signature = ComputeSignature();
        }

        /// <summary>次回アクセス時にファイルから読み直す。</summary>
        internal static void Invalidate() => s_Map = null;

        static bool Write(string guid, string tagName, bool force = false)
        {
            var filePath = GetFilePath(guid);
            if (string.IsNullOrEmpty(tagName))
            {
                if (!s_Map.Remove(guid)) return false;
                if (File.Exists(filePath)) File.Delete(filePath);
                return true;
            }

            if (!force && s_Map.TryGetValue(guid, out var current) && current.TagName == tagName) return false;

            // path は差分レビュー時に対象を分かりやすくするための参考情報(名前タグの解決には使わない)
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var content = $"{k_NameKey} {tagName}\n{k_PathKey} {assetPath}\n";
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(filePath, content, s_Utf8NoBom);
            s_Map[guid] = new Record(tagName, assetPath);
            return true;
        }

        static void EnsureLoaded()
        {
            if (s_Map != null) return;

            s_Map = new Dictionary<string, Record>();
            if (Directory.Exists(DirectoryPath))
            {
                foreach (var filePath in Directory.GetFiles(DirectoryPath, "*" + k_Extension))
                {
                    var guid = Path.GetFileNameWithoutExtension(filePath);
                    if (!IsValidGuid(guid)) continue;

                    var record = ReadRecord(filePath);
                    if (!string.IsNullOrEmpty(record.TagName)) s_Map[guid] = record;
                }
            }
            s_Signature = ComputeSignature();

            // 旧形式(NameTagSettings.asset 内のリスト)からの移行
            var legacy = NameTagSettings.instance.TakeLegacyAssignments();
            foreach (var (guid, tagName) in legacy)
            {
                // 新形式のファイルが既にあればそちらを優先する
                if (IsValidGuid(guid) && !s_Map.ContainsKey(guid)) Write(guid, tagName);
            }
            if (legacy.Count > 0) s_Signature = ComputeSignature();
        }

        static Record ReadRecord(string filePath)
        {
            string tagName = null;
            string path = null;
            try
            {
                foreach (var line in File.ReadAllLines(filePath, s_Utf8NoBom))
                {
                    if (line.StartsWith(k_NameKey, StringComparison.Ordinal)) tagName = line.Substring(k_NameKey.Length).Trim();
                    else if (line.StartsWith(k_PathKey, StringComparison.Ordinal)) path = line.Substring(k_PathKey.Length).Trim();
                }
            }
            catch (IOException)
            {
                // 書き込み途中などで読めない場合は次回の再読み込みに任せる
            }
            return new Record(tagName, path);
        }

        /// <summary>ファイル構成と更新日時から変更検知用の値を作る。</summary>
        static long ComputeSignature()
        {
            if (!Directory.Exists(DirectoryPath)) return 0;

            unchecked
            {
                long signature = 17;
                foreach (var file in new DirectoryInfo(DirectoryPath).EnumerateFiles("*" + k_Extension))
                {
                    signature += file.Name.GetHashCode() * 31L ^ file.LastWriteTimeUtc.Ticks;
                    signature += file.Length;
                }
                return signature;
            }
        }

        static string GetFilePath(string guid) => $"{DirectoryPath}/{guid}{k_Extension}";

        static bool IsValidGuid(string guid) => !string.IsNullOrEmpty(guid) && s_GuidPattern.IsMatch(guid);
    }

    /// <summary>
    /// アセットの移動を検知して、割り当てファイルの path を更新する。
    /// 移動があるたびに全割り当てを確認するため、フォルダの移動で中のアセットのパスが変わった場合も更新される。
    /// </summary>
    class NameTagAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (movedAssets.Length > 0) NameTagAssignments.SyncPaths();
        }
    }
}
