# プラグイン & シェーダー拡張システム仕様書 (`PLUGIN_SYSTEM.md`)

**Languages:** 日本語 | [English](PLUGIN_SYSTEM.en.md)

Vectorerは外部DLLからメニュー、ツールバーの機能と、オブジェクト単位のSkiaSharpシェーダーを追加できます。

---

## 1. プラグイン開発の基本方針

1. プラグイン開発者はクラスライブラリプロジェクト（`net10.0`）を作成し、`Vectorer.Abstractions` を参照します。
2. `Vectorer.Abstractions` は `SkiaSharp` への参照を含んでいるため、プラグイン側から直接 `SKPath`、`SKCanvas`、`SKShader`、`SKRuntimeEffect` などを扱うことができます。
3. プラグインは `IVectorerPlugin` インターフェースを実装したパブリッククラスを公開します。

---

## 2. 主要インターフェース (`Vectorer.Abstractions.Plugins`)

### 2.1 `IVectorerPlugin`
プラグイン DLL のエントリポイントです。

```csharp
public interface IVectorerPlugin
{
    PluginMetadata Metadata { get; }
    void Initialize(IPluginContext context);
}
```

### 2.2 `IPluginContext`
プラグイン初期化時にホスト（Vectorer 本体）から渡される登録用コンテキストです。

- `RegisterMenuContribution(IMenuContribution item)`: メニューバー（「フィルタ・拡張機能」メニューや任意のカテゴリ）にコマンド項目を追加します。
- `RegisterToolbarContribution(IToolbarContribution item)`: ツールバー（または左ツールボックス下部）に、現在の描画ツールを切り替えることなく即座に `Execute(IEditorWorkspace)` を実行するワンショットアクションボタンを追加します。
- `RegisterShaderProvider(IShaderEffectProvider provider)`: オブジェクトに適用可能な SkiaSharp カスタムシェーダー (`SKRuntimeEffect`) を登録します。

### 2.3 `IEditorWorkspace`
メニューやツールバーのアクションが実行された際、プラグインに渡される操作対象インターフェースです。プラグインはこのインターフェースを通じて現在の SVG ドキュメントを安全に読み書きできます。

- `SvgDocument CurrentDocument { get; }`: 現在開いている SVG ドキュメント
- `IReadOnlyList<SceneNode> SelectedNodes { get; }`: 現在選択されているノード一覧
- `void ExecuteCommand(IDocumentCommand command)`: Undo/Redo 履歴に対応したドキュメント変更コマンドの実行
- `void RequestRedraw()`: キャンバスの再描画要求

---

## 3. オブジェクト単位の SkiaSharp シェーダー (`IShaderEffectProvider`)

Vectorer では、任意の図形 (`ShapeNode`) やレイヤー (`LayerNode`) に対して `ShaderEffectDescriptor` をアタッチできます。

```csharp
public interface IShaderEffectProvider
{
    string ShaderId { get; }
    string DisplayName { get; }
    string SkslSource { get; }
    IReadOnlyDictionary<string, float> DefaultUniforms { get; }
    SKShader? CreateShader(SKRect bounds, IReadOnlyDictionary<string, float> uniforms);
}
```

- レンダリングエンジンは対応する `IShaderEffectProvider` が返すシェーダーを適用します。v0.1.0では既存SVG内のインラインSkSLを保持し、通常の塗りで描画します。

---

## 4. DLL の動的ロード (`Vectorer.Plugins`)

- `PluginLoader` は指定されたディレクトリ（例: `./plugins/`）内の `*.dll` をスキャンします。
- 各プラグインは分離された `AssemblyLoadContext` (`PluginLoadContext`) で読み込まれ、ホスト側の `Vectorer.Abstractions` および `SkiaSharp` アセンブリはデフォルトコンテキストと共有されるため、型の不一致が起きません。
- 公式の実装サンプルとして `plugins/Vectorer.SamplePlugin` が用意されています。新しいプラグインを作成する際は参考にしてください。
