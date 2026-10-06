# NameTag

複数人でプロジェクトを運用するときに「このフォルダ・ファイルは誰が触るか」を明文化するための Unity エディタ拡張です。
Project ウィンドウのフォルダ・ファイルアイコンの右下に名前タグを表示します。

## インストール

Package Manager の **+ > Install package from git URL...** に次の URL を入力します。

```
https://github.com/HyogaFukuno/NameTag.git?path=Packages/com.hyogafukuno.nametag
```

または `Packages/manifest.json` の `dependencies` に追加します。

```json
"com.hyogafukuno.nametag": "https://github.com/HyogaFukuno/NameTag.git?path=Packages/com.hyogafukuno.nametag"
```

特定のバージョンに固定する場合は、URL の末尾にタグを指定します（例: `#v1.1.2`）。

## 使い方

1. **Project Settings > NameTag Settings** で名前タグに使う名前を登録します。
2. Project ウィンドウでフォルダまたはファイルを選択し、インスペクタ下部の **Name Tag > Assignee** から名前を選びます。
3. アイコンの右下に名前タグが表示されます。

## 表示ルール

- フォルダは、自身に設定された名前タグを表示します。
- ファイルは、自身に名前タグがなければ最も近い親フォルダの名前タグを表示します（グレーの斜体で表示）。
- ファイル自身に名前タグが設定されている場合は、そちらを優先して表示します。
- Project ウィンドウを 1 行のリスト表示にしている場合は、行の右端に表示します。

## データの保存先

| 内容 | 保存先 |
| --- | --- |
| 登録した名前の一覧 | `ProjectSettings/NameTagSettings.asset` |
| アセットごとの割り当て | `ProjectSettings/NameTags/<GUID>.txt`（1 件につき 1 ファイル） |

割り当てを 1 件ずつ別ファイルに保存しているため、別々のアセットへのタグ付けは git でコンフリクトしません。
コンフリクトするのは、同じアセットの名前タグを複数人が同時に変更した場合だけです。
名前タグの変更だけを専用のブランチで先に共有しても、他の作業ブランチと衝突しません。

割り当てファイルの中身は次のようなテキストです。`path` はレビュー時に対象を分かりやすくするための参考情報です（名前タグの判定には GUID を使います）。Unity 上でアセットやフォルダを移動すると、`path` も自動で更新されます。

```
name: 佐藤
path: Assets/_Project/Title
```

割り当てはアセットの GUID で管理しているため、ファイルを移動・リネームしても名前タグは外れません。
削除したアセットの割り当ては、NameTag Settings の「存在しないアセットの割り当てを削除」で整理できます。
git pull などで設定ファイルや割り当てファイルが更新された場合は、エディタが自動で読み直します。

### 1.0.x からの移行

1.0.x では割り当ても `NameTagSettings.asset` に保存していました。
1.1.0 以降を導入して最初に読み込んだとき、既存の割り当てを `ProjectSettings/NameTags/` へ自動で移行します。
移行で変更された `NameTagSettings.asset` と、新しく作られた `ProjectSettings/NameTags/` をコミットしてください。

## 動作確認環境

- Unity 6000.3

## ライセンス

[MIT License](LICENSE.md)
