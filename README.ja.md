# Enhanced Context Menu

[English](README.md)

![ゲームのコンテキストメニューとオーバーレイパネル](docs/screenshots/hero-1280x720.png)

Enhanced Context Menu は、他のプラグインがコンテキストメニューに追加した項目を、オーバーレイパネルに移します。コンテキストメニューを開くと、オーバーレイパネルが自動で表示されます。

ネイティブUI を直接書き換えて追加された項目はネイティブ側のコンテキストメニューに残るため、注意が必要です。

## インストール

1. `/xlsettings` を実行し、**試験的機能**タブを開く
2. **カスタムプラグインリポジトリ** に次の URL を追加する:

```
https://raw.githubusercontent.com/exatrines/DalamudPlugins/refs/heads/main/pluginmaster.json
```

3. `/xlplugins` を実行し、**Enhanced Context Menu** をインストールする

## 機能

- プラグインによって追加されたコンテキストメニューをオーバーレイパネルに切り出し
- ゲームパッドでの操作に対応

## コマンド

| コマンド | 説明 |
| --- | --- |
| `/enhancedcontextmenu` | プラグイン設定画面の表示切替 |

## 開発者向け

1. ビルド: `dotnet build EnhancedContextMenu.sln -c Release -p:Platform=x64`
2. Dalamud の **dev plugin** に `EnhancedContextMenu/bin/Release/` を指定する
3. プラグインインストーラ（dev）で **Enhanced Context Menu** を有効にする

共有 UI キットの [MirageUI](https://github.com/exatrines/MirageUI) を git サブモジュールとして同梱しています。

## ライセンス

[AGPL-3.0-or-later](LICENSE)
