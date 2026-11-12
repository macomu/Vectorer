# Vectorer 日本語・英語対応設計

状態: 設計案（アプリの実装は未変更）。対象: .NET 10 / Avalonia 11.3.0。

## 1. 採用方針

**.NET 標準の `.resx` / `ResourceManager` を基盤とし、`INotifyPropertyChanged` に対応した型付きローカライズサービスへバインドする。言語変更は、ウィンドウや ViewModel を作り直さずに即時反映する。**

Avalonia 11 の公式ガイドに沿って `.resx` を使う。即時更新には、現在の公式ガイドでも説明されている通知可能なサービスを採用する。`x:Static` で取得した文字列は自動更新されないため、翻訳文字列の表示には使用しない。追加のローカライズ用 NuGet パッケージは導入しない。[Avalonia 11 公式ガイド](https://v11.docs.avaloniaui.net/docs/guides/implementation-guides/localizing/)、[公式の実行時切り替えの説明](https://docs.avaloniaui.net/docs/app-development/localizing#runtime-language-switching)

日本語と英語の UI 品質を同等に保つ。英語をリソースの最終フォールバックとし、日本語環境では初回から日本語を表示する。英語を基底リソースにすることと、日本語ユーザーの既定表示は別に扱う。

| 項目 | 決定 |
| --- | --- |
| 初期対応言語 | 日本語 `ja`、英語 `en` |
| 設定の選択肢 | システムに従う / 日本語 / English |
| 設定保存値 | `system` / `ja` / `en` |
| 初期値 | `system`。OS の UI 言語が日本語なら日本語、それ以外は英語 |
| 切り替え | 通常は即時反映。ドラッグ・IME 変換中等は安全な区切りまで延期（11.5節） |
| 翻訳の所在 | ホスト UI は `Vectorer.App`、プラグイン独自 UI は各プラグイン |
| 数値・日付の地域設定 | UI 言語と分離し、OS の `CurrentCulture` を維持 |
| SVG 等の機械形式 | 従来どおり `InvariantCulture` |

## 2. 現状と変更箇所

現状では `Views/MainWindow.axaml` などの固定文字列、`ViewModels/DocumentFiles.cs` のステータスメッセージ、`Services/EditorFileDialogs.cs` のダイアログに表示文言が分散している。`MainWindowViewModel.cs` には英語のメッセージもあり、単に日本語文字列を検索するだけでは抽出しきれない。

| 対象 | 必要な変更 |
| --- | --- |
| `Views/*.axaml` | メニュー、ラベル、ツールチップ、タイトル、入力ヒント、アクセシビリティ名をリソース参照へ置換 |
| `ViewModels/*.cs` | 状態、検証エラー、ツール説明、列挙値の表示名、最近使ったファイルメニューを抽出 |
| `Services/EditorFileDialogs.cs` | コードで組み立てる独自ダイアログ、ファイル選択タイトル・形式名を抽出 |
| `Controls/*.cs` / `Tools/*.cs` | ユーザーに見える固定文言やキャンバス上の操作ヒントを点検 |
| `Services/EditorUserSettings.cs` | 言語設定を既存のユーザー設定に追加 |
| `ViewModels/UserSettings.cs` | 既存設定の読み書きに言語設定を統合し、他の設定保存で消失しないよう変更 |
| `App.axaml.cs` | View / ViewModel の生成より前に設定読込・言語確定を完了 |
| `build.cs` | publish 出力と配布アーカイブに日本語サテライトアセンブリが含まれることを検証 |

実装時は表示文字列の一覧を作り、場所、キー、日英訳、引数、翻訳対象外の理由を記録する。`SVG`、`PNG`、`SkSL`、数式記号、ショートカット表記などは、必要な文脈を添えて共通表記を維持する。

## 3. 配置と責務

```text
src/Vectorer.App/
  Localization/
    Strings.resx                 # 英語。全キーの基準
    Strings.ja.resx              # 日本語。基準と同一キー集合
    AppLocalization.cs          # UI カルチャと型付き表示プロパティ、変更通知
    LanguageChangeCoordinator.cs # 切り替え順序、安全な実行時点、設定保存の調整
    LocalizedMessage.cs         # キーと引数。ステータス・検証結果の再翻訳
    LocalizedDisplayNames.cs    # enum -> 表示名。enum 自体は変更しない
  Services/EditorUserSettings.cs # UiLanguage を追加
tests/Vectorer.App.Tests/
  LocalizationTests.cs          # 言語解決、翻訳整合性、設定互換性
```

`Strings` は基底 `.resx` から生成する public な型付きクラス。翻訳ファイルからはクラスを生成しない。`Strings.resx` は英語なので `<NeutralLanguage>en</NeutralLanguage>` を App プロジェクトに指定する。`Strings.en.resx` は不要。

`.resx` は `AvaloniaResource` ではなく `EmbeddedResource` とする。既存の `Assets/**` の対象から外れる `Localization/` に配置する。日本語は `ja/Vectorer.App.resources.dll` に格納され、`ja-JP` などから親言語へフォールバックできる。[.NET のサテライトアセンブリ](https://learn.microsoft.com/en-us/dotnet/core/extensions/create-satellite-assemblies)

型付きクラスは IDE の Custom Tool 実行に依存させず、MSBuild の `GenerateResource` によってクリーンな CLI ビルドでも生成する。生成物を `obj/` に置き、C# の Compile 対象に含める。基底リソースの `StronglyTypedLanguage`、`StronglyTypedNamespace`、`StronglyTypedClassName`、`StronglyTypedFileName`、`PublicClass` を設定し、生成順序と Compile 登録を実際の SDK で検証する。最新の公式ガイドにも MSBuild 生成の案内があるが、導入時は本プロジェクトの Avalonia 11.3.0 / .NET 10 で確認する。[Avalonia のコード生成案内](https://docs.avaloniaui.net/docs/app-development/localizing#other-tools-rider-vs-code-cli)

Core / Svg / Rendering から App のリソースを参照しない。表示名をモデルの enum やシリアライズ値に埋め込まない。新規の共通ローカライズプロジェクトは不要。プラグインの即時更新用に限り、Abstractions へ任意インターフェースを追加する（7節）。

## 4. 言語決定と起動シーケンス

1. 非同期で既存の `settings.json` を読み込む。古い設定に `UiLanguage` がない場合は `system` として扱う。
2. `ja` / `en` の明示指定を優先する。不明・空の値は `system` に正規化する。
3. 起動時の OS UI カルチャを、アプリが変更する前に別途保持する。`system` はこの値とその親から解決し、`ja` があれば日本語、その他は英語とする。英語指定から `system` に戻すときも、アプリが変更した `CurrentUICulture` を OS 値として再利用しない。OS 言語の実行中の変更監視は初期対象外とし、次回起動で再取得する。
4. アプリで共有する `AppLocalization` に解決済みの UI カルチャを設定する。ホストの翻訳は `ResourceManager.GetString(key, UiCulture)` で明示的に取得する。互換用に `CurrentUICulture` と `DefaultThreadCurrentUICulture` も設定するが、これだけに依存しない。`CurrentCulture` / `DefaultThreadCurrentCulture` は変更しない。
5. ViewModel、メニュー、プラグイン、MainWindow を生成する。App の XAML に翻訳を含める場合は、そのロードも言語確定後に行う。
6. 読み込んだ同一設定を ViewModel に渡して通常画面を表示する。

`CurrentUICulture` はリソース検索用であり、タスクにも呼び出し元の UI カルチャが伝播する。ただし既存の実行コンテキストを一括更新する仕組みではないため、初期化を処理開始前に終える。[.NET CurrentUICulture](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.currentuiculture?view=net-10.0)

現在の `MainWindow` 表示後の設定読込は、初回表示の言語がちらつく原因になる。設定読込はアプリ起動の責務へ移し、UI スレッドを `.Result` / `.Wait()` でブロックしない。Avalonia のライフタイムを維持する翻訳不要の起動画面を使い、`Task` を返す初期化処理を開始し、完了後に MainWindow へ差し替える。起動イベント境界では例外を捕捉し、失敗時は既定設定で続行する。ViewModel は設定の適用と以後の保存を担当し、二重読込は行わない。

`EditorUserSettings` は既定値付きの `UiLanguage` を末尾に追加する。設定全体を保持し、`with` 更新等で幅・履歴・言語を併せて保存する。現在の `new(InspectorWidth, RecentFiles.ToArray())` による保存は言語を初期値に戻すため変更が必要。設定保存の失敗時は成功扱いにせず現在の言語で通知する。

画面では「表示 → 言語 / Language」にチェック付きの3項目を置き、選択後に即時更新する。「日本語」「English」は各言語の自称を固定表示する。設定選択値 `RequestedLanguage` と、実際の `UiCulture` を分離する。`system` と `ja` の切り替えで解決言語が同じ場合も、選択状態と保存値は更新する。言語設定は SVG に保存せず、Undo 対象やドキュメントの変更状態にも含めない。

## 5. XAML・C# での利用規約

以下は実装イメージ。リソースキーは用途が分かる英語の識別子とする。

```xml
<Window x:DataType="vm:MainWindowViewModel" ...>
  <MenuItem Header="{Binding L10n.Menu_File_Open, Mode=OneWay}"
            InputGesture="Ctrl+O" Command="{Binding OpenDocumentCommand}" />
</Window>
```

```csharp
Status = new LocalizedMessage(nameof(Strings.Status_FileOpened), [file.Name]);
// StatusMessage の getter で L10n.Format(Status) を呼ぶ。
// Status または UI 言語が変わったときに StatusMessage を通知する。
// en: Opened: {0}
// ja: 開きました: {0}
```

| キー例 | 日本語 | 英語 |
| --- | --- | --- |
| `Menu_File_Open` | 開く… | Open… |
| `Common_Cancel` | キャンセル | Cancel |
| `Status_FileOpened` | 開きました: {0} | Opened: {0} |
| `Error_FileOpenFailed` | ファイルを開けませんでした。 | Could not open the file. |
| `BlendMode_Multiply` | 乗算 | Multiply |

文を連結して翻訳を組み立てず、語順を入れ替えられる完全な文をリソース化する。`.resx` のコメントに引数の意味、表示場所、改行・省略の制約を記載する。同じ日本語でも意味が違う場合は別キーにする。

件数表示はまず「出力ファイル数: {0} / Files exported: {0}」のように単複で変わらない表現を選ぶ。必要なら `One` / `Other` を明示的に分ける。`.resx` 自体は複数形を選択しない。対応言語を増やす段階で、その言語の複数形規則と MessageFormat 系ライブラリの必要性を再評価する。

列挙値は値と通知可能な表示名の組を App 側で作る。例: `LayerBlendMode.Multiply` と `L10n.BlendMode_Multiply`。翻訳文字列から enum を逆引きしない。`ComboBox` は値による選択を保ち、表示名の変更で選択を解除しない。ショートカット定義も翻訳文字列から切り離す。詳細は11.3節。

## 6. 翻訳とデータの境界

| 種類 | 方針 |
| --- | --- |
| メニュー・検証・状態・ホストのエラー概要 | 日英のリソースにする |
| ユーザーが入力した文書名、レイヤー名、テキスト、ファイル名 | そのまま表示・保存する |
| `Untitled` / `Layer 1` 等の自動生成名 | App が新規作成する時にだけ翻訳。以後は文書データとして固定 |
| Core にある既定名 | UI カルチャへ依存させない。App の新規作成経路で適切な初期名を設定する |
| SVG の欠落名に対する表示上の代替名 | App でのみ翻訳し、表示のためにモデルを書き換えない |
| SVG 属性、enum 名、ID、ShaderId、SkSL、パスデータ | 翻訳しない |
| 数値入力・表示 | 通常の数値は OS の地域設定。入力と表示で同じカルチャを使う |
| SVG・破線配列・倍率プリセット等の構文 | `InvariantCulture` を維持し、`.` や区切りの仕様を入力ヒントに示す |
| 例外・外部ライブラリのメッセージ | ホスト側の概要を翻訳し、元メッセージは技術詳細として保持 |

既存の `SvgSerializer.FormatFloat` 等の invariant 処理は維持する。英語 UI を選んでも小数点やリスト区切りを暗黙に英語式へ変更しない。バインディング、NumericUpDown、独自パーサーそれぞれで入力と表示が一致するか確認する。

既存の Undo / Redo メニューは操作名を含まないため、初期対応ではメニューの固定文言だけ翻訳する。`IDocumentCommand.Description` は内部説明として維持し、Core に翻訳リソースを追加しない。将来履歴パネルを公開する場合は、安定した操作 ID と引数を持つ任意のメタデータ契約を Abstractions に追加し、App が表示時に翻訳する。英語の Description の内容照合による翻訳は行わない。

## 7. プラグイン互換性

既存 SDK の必須メンバーは変更しない。`Id`、`MenuPath`、`ShaderId` はそのまま使い、ホストのメニュー見出しだけ翻訳する。`MenuPath` は現在の登録・解決規則に従うルーティング値として扱い、ホストの翻訳済み見出しと比較しない。必要なら App 側で既存パスとメニュー実体の対応を明示する。

即時更新に対応するプラグイン用に、Abstractions へ `IUiCultureAwarePlugin` と `OnUiCultureChanged(CultureInfo culture)` を任意契約として追加する案を採る。プラグインは自身の翻訳に明示的なカルチャを使う。ホストは Initialize より前に初期カルチャを通知し、切り替え時も UI スレッドで通知する。通知中は登録・文書操作を行わず、表示用カルチャだけを更新する契約とする。SamplePlugin を対応例にする。

ホストは登録済み contribution のラッパー ViewModel を保持し、通知後に `Title` / `Tooltip` / `DisplayName` を再取得して表示プロパティを通知する。既存の登録オブジェクト、コマンド、ID は維持する。Shader の表示名の更新で再コンパイルを行わない。非対応 DLL にも取得時の `CurrentUICulture` を一時的に揃える互換スコープを提供できるが、キャッシュ済みの文字列を翻訳することはできない。

**ホスト UI の日英対応は、すべての第三者プラグインの翻訳を保証しない**。固定文言しか返さないプラグインは継続利用可能とする。例外はプラグイン単位で捕捉し、その表示は前の値に保ち、他の UI の更新を続行する。独自ウィンドウはプラグイン側が通知から更新する。再初期化や DLL の再ロードは行わない。識別用の Metadata.Name 等は原則として作者の表記を尊重する。第三者プラグインは引き続き Abstractions のみを参照でき、App / Avalonia の依存を要求しない。

## 8. レイアウト・フォント

英語は日本語より幅を取る場合がある。テキストの固定幅を減らし、Grid の Auto / Star、適切な MinWidth、折り返しを使う。主要操作は省略で読めなくしない。既存 OverflowToolbar も日英で確認する。最小ウィンドウ `1024 × 720` と通常 `1280 × 800`、表示倍率 100% / 150% / 200% を確認する。

`.WithInterFont()` を維持しつつ、日本語のグリフが OS フォールバックで欠けないことを Windows / macOS で確認する。フォントの同梱は必要性を確認して別途判断し、追加時はライセンスと THIRD_PARTY_NOTICES.md を更新する。UI フォントの変更によって SVG テキストのフォントを変更しない。

アクセシビリティ名、ツールチップ、検証エラー、空状態も翻訳対象。ネイティブファイル選択画面の OS 管理ボタンは OS 言語のままになり得るため、ホストから指定するタイトル等を検証する。RTL は初期対象外とし、将来対応時も図形キャンバスの座標やドキュメントを無条件に反転しない。

## 9. 段階的な実装

1. **基盤**: `.resx`、型付きクラス生成、通知可能なサービス、言語解決・切り替え、起動処理、設定保存、言語メニューを導入。ファイルメニューで日英往復と即時更新を先に検証する。
2. **全画面**: メイン画面、インスペクタ、ツール、各ダイアログ、空状態、検証、アクセシビリティの文言を抽出し、日英を同時に埋める。
3. **動的表示**: enum、自動生成名、ステータス、エラー分類、プラグインのホスト表示と SamplePlugin を整備する。
4. **品質と配布**: リソース検査、設定互換性、文化差による回帰テスト、両言語の目視確認、publish 配布確認、利用者向け言語設定の案内を追加する。

各段階で既存のビルド・全テストを維持する。基盤だけを導入した状態で「英語対応済み」とは扱わない。

## 10. 完了条件と検証

- 基底と日本語のキー集合、空値、重複キー、書式引数の集合・妥当性を App.Tests で検査する。日本語のキー欠落は英語へのフォールバックで隠さずテスト失敗とする。
- `system` / `ja` / `en`、`ja-JP` / `en-GB` / 未対応言語、不明な保存値、旧設定 JSON、破損設定での解決を検証する。
- 言語設定が即時反映され、幅や最近使ったファイルを保存しても言語が失われず、次回起動でも維持されることを確認する。
- 明示的な CultureInfo を渡せる解決・表示関数を優先する。グローバルなカルチャを変更するテストは直列化し、元の値を finally で復元する。
- Core / Svg / Rendering の新しいロジックを変更する場合は対応プロジェクトにもテストを追加する。`ja-JP` / `en-US` / 小数点カンマの `de-DE` でも SVG 数値の読み書き・再読込結果が不変であることを検証する。
- 文書やレイヤーの明示名、日本語の SVG テキストが英語 UI でも保持され、言語設定で未保存フラグや Undo スタックが変化しないことを確認する。
- 全画面と主な操作のメッセージを両言語で確認する。固定文字列検索は補助とし、ASCII の英語やアクセシビリティ名の取りこぼしも確認する。
- クリーンな CLI ビルドでリソースクラス生成と XAML 参照が成功することを確認する。`dotnet build Vectorer.slnx` / `dotnet test Vectorer.slnx` を実行する。
- win-x64 / win-arm64 / osx-arm64 の publish 出力と ZIP / tar アーカイブに `ja/Vectorer.App.resources.dll` があり、対応環境で配布物から日英起動できることを確認する。

## 11. 即時反映の詳細設計

### 11.1 サービスと型付き Binding

`AppLocalization` は `ObservableObject` を継承し、アプリ全体で1インスタンスを共有する。App が所有し、各 ViewModel にコンストラクターで注入する。テストでは別インスタンスを生成できるようにし、静的 singleton を必須にしない。ViewModel の `L10n` は public な読み取り専用プロパティで、インスタンスを差し替えない。

以下は契約イメージであり、そのままビルドする実装ではない。

```csharp
public sealed partial class AppLocalization : ObservableObject
{
    public CultureInfo UiCulture { get; private set; }
    public CultureInfo FormattingCulture { get; } // 起動時の OS 地域設定
    public long Revision { get; private set; }

    public string Menu_File_Open => Get(nameof(Strings.Menu_File_Open));
    public string BlendMode_Multiply => Get(nameof(Strings.BlendMode_Multiply));

    public string Get(string key); // ResourceManager.GetString(key, UiCulture)
    public string Format(LocalizedMessage message);
    internal void ApplyCulture(CultureInfo culture); // UI スレッド専用
}
```

表示用プロパティは最初は明示的に宣言し、`nameof(Strings.…)` でリソースの存在をコンパイル時に確認する。UI から参照するプロパティと対応キーの検査をテストへ追加する。将来生成する場合もこの public 契約を維持し、キーの名称変換と衝突を検出する。

`ApplyCulture` はカルチャと Revision を更新した後、型付き表示プロパティへ `PropertyChanged` を発行する。小規模な初期実装では空名による全表示プロパティ通知を1回行う方針とし、個別通知を重ねて発行しない。UI スレッド上で更新と通知を同期的に完了し、更新途中に await を挟まない。翻訳ファイルの I/O や ResourceManager の再生成は切り替えごとに行わない。

既存の `AvaloniaUseCompiledBindingsByDefault=true` を維持する。画面は `x:DataType` のある ViewModel の `L10n.Menu_File_Open` 等へ `Mode=OneWay` でバインドする。文字列キーのインデクサーや独自 MarkupExtension を標準経路にはしない。DataTemplate はアイテム自身の通知可能な Title 等へバインドし、親の DataContext を置き換えない。Popup / ContextMenu では暗黙の親探索に依存せず、表示用 ViewModel または明示的なソースを渡す。[Avalonia 11 の compiled bindings](https://v11.docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings/)

`DynamicResource` で言語辞書を差し替える方式も可能だが、今回は C# からの動的メッセージも同じ `.resx` で管理するため採用しない。単に文字列を返す Converter や MarkupExtension は再評価の契機にならず、サービスの通知を監視する Binding が必要となる。

### 11.2 切り替えの順序と設定保存

`LanguageChangeCoordinator` を切り替えの入口とし、メニュー、将来の設定画面、テストから同じ処理を使う。処理手順は次のとおり。

1. 選択値を検証し、保持した OS UI カルチャから実効言語を解決する。同じ選択値への要求は何もしない。
2. 安全な実行時点かを判定する（11.5節）。延期中に別の要求が来た場合は最後の値だけを保持する。
3. UI スレッドで `RequestedLanguage` と共通サービスの UiCulture / Revision を更新する。実効言語が同じなら翻訳更新を省き、選択状態と保存だけを更新する。
4. ホストの静的表示、派生表示、プラグインの表示を更新する。ドキュメント編集コマンドやツール選択処理は呼び出さない。
5. 最新のユーザー設定全体を非同期で保存する。反映完了を待ってからディスク I/O を開始し、表示を保存完了まで待たせない。

手順3〜4は1つの UI スレッド処理として行い、通常は次の描画で新しい表示を見せる。これはすべての外部 UI が同時に更新されるトランザクションを保証するものではない。プラグイン通知の再入要求はキューに回し、通知中に再帰的に切り替えない。

保存はアプリ共通の設定管理で直列化し、Revision または設定世代で古い保存要求をまとめる。言語だけでなく幅・履歴の更新も同じ管理経路で扱い、古いスナップショットが新しい設定を最後に上書きしないようにする。適用済み・保存済みの世代を区別し、終了時は最新の保存を await する。

保存失敗時も画面を旧言語へ戻さない。「言語は変更されましたが、設定を保存できませんでした」という概要を新しい言語で表示し、未保存のユーザー設定として保持して次の保存時に再試行する。文書の未保存状態とは別管理にする。

### 11.3 動的メッセージ・選択肢・検証エラー

翻訳済み string を永続的な状態として持つと、言語変更時に古い表示が残る。表示の種類ごとに、意味を保持して再計算する。

| 対象 | 保持する状態 | 切り替え時の更新 |
| --- | --- | --- |
| ステータス・完了通知 | `LocalizedMessage(Key, Arguments)` | `StatusMessage` を再通知して現在言語で Format |
| 選択件数・ツールヒント | 件数、DrawingTool 等の実値 | `SelectionSummary` / `ToolHint` 等だけを再通知 |
| ツール一覧 | 同一の ToolItem ViewModel と DrawingTool | Title / Hint だけを通知。ActiveToolItem の参照は維持 |
| ブレンド・線種等の選択肢 | 安定した値・IDと同一のラッパー | 表示名だけを更新し、SelectedValue を維持 |
| 検証エラー | フィールド名、エラーキー、引数 | `ErrorsChanged` で文言を再取得。入力を再確定しない |
| 最近使ったファイル | ファイル情報とコマンド、固定項目のキー | ファイル名は維持、空状態・履歴消去項目だけを翻訳 |
| ファイル操作の失敗 | エラー種別と元の技術詳細 | ホストの概要を翻訳し、元例外の文字列は保持 |

`LocalizedMessage` の引数は表示に必要な数値・ファイル名等のスナップショットとする。可変の SceneNode や IDisposable なネイティブオブジェクト、遅延実行のクロージャは保持しない。enum の表示名等も必要ならキーとして保持する。数量を先に英語の string に変換せず、Format 時に FormattingCulture を使う。ユーザー入力・外部ログ等の固定文字列には明示的な Literal 扱いを設け、翻訳キーとして解釈しない。

現在の `ToolItem` は固定文言を持つ record であるため、通知可能な表示用 ViewModel へ変更する。`StrokeDash` の「実線」のように日本語を状態値として使う箇所も、線種 ID / enum と表示名へ分離する。表示更新で `OnActiveToolItemChanged`、描画ツール交換、スタイル変更、Undo 記録が発火しないことを確認する。

単なるプロパティ通知では更新されないキャッシュも一覧化する。例として、生成済みメニューの Header、Toast の本文、独自コントロール内の TextBlock、キャンバスに描く操作ヒントがある。独自コントロールは Binding を使うか専用の表示更新を行い、キャンバスは操作ヒント等のオーバーレイだけを再描画する。図形の幾何キャッシュやドキュメントの再構築は行わない。

### 11.4 スレッドと非同期処理

`CurrentUICulture` の更新は、既に動いている Task の実行コンテキストまで更新するものではない。async な言語変更メソッド内で設定した値が、以後のすべての UI コールバックに残るとも仮定しない。ホストの文字列取得では常にサービスの明示的な UiCulture を使い、`Strings.Culture` を別の可変な正本にしない。

PNG の一括出力などはバックグラウンドで継続できる。バックグラウンド処理は件数やエラー種別などの結果を返し、UI スレッドで LocalizedMessage を設定する。完了時には切り替え後の言語で表示される。バックグラウンドからサービスの変更通知を発行しない。

進捗と完了の順序は操作 ID で管理し、完了後に古い進捗が表示を上書きしないようにする。言語変更は進捗や操作自体の再実行・キャンセルを意味しない。互換スコープで CurrentUICulture を変更する場合は同期呼び出しの範囲に限定し、finally で元に戻す。

### 11.5 ダイアログ・編集中操作と即時反映の範囲

| 状態 | 動作 |
| --- | --- |
| 通常の待機中 | 直ちに反映 |
| 独自のダイアログ・モーダルが開いている | 同じサービスでタイトル・ボタン・エラーを更新。入力値、フォーカス、キャレットを保持 |
| ネイティブファイル選択画面が開いている | 既存画面は OS 管理のため更新対象外。安全のため言語変更要求全体を閉じるまで延期し、次回表示から反映 |
| ドラッグ・ポインターキャプチャ中 | 操作終了またはキャンセルまで延期。言語変更のために操作をキャンセルしない |
| IME の変換途中 | composition 終了まで延期し、確定・取消を強制しない |
| 通常の文字・数値入力中 | 表示ラベルのみ更新し、入力 Text / バインド先・選択範囲を変更しない |
| バックグラウンド出力中 | 表示は更新し、出力はそのまま継続。既存 IsBusy によるメニュー無効化を迂回して操作させる必要はない |

延期中は `PendingLanguage` を別に保持し、適用済みのチェック表示と区別する。未適用の要求は設定へ保存しない。必要なら「操作終了後に言語を変更します」と現在の言語で案内する。UI スレッド上の非同期通知で安全な区切りを捉え、ポーリングや無期限の待機ループは使わない。IME composition の検知可否は Avalonia 11.3.0 の対象プラットフォームで検証し、検知が難しい経路では該当エディタのフォーカス離脱を安全な区切りとする。

Menu が開いている状態からの要求はクリック処理終了後の Dispatcher 処理へまとめ、メニュー閉鎖と更新の競合を避ける。独自モーダル中にメインのメニューを操作できない場合も、サービス経由で言語が変われば表示を更新できるようにする。

地域設定は変えないため、入力中の `1,5` 等の解釈も変えない。言語変更でデータの再ロード、入力確定、Control の作り直し、Focus() の呼び直しを行わない。レイアウトによる再配置は許容するが、スクロール位置や展開状態、ズーム・パン、現在のツールを維持する。

### 11.6 購読・破棄と性能

静的ラベルの Binding は Avalonia の購読管理に任せる。動的な派生プロパティのためにサービスへ直接購読する ViewModel は IDisposable 等で解除する。ダイアログは閉じた後の finally で購読と、コード生成 Binding が返す IDisposable を破棄する。MainWindow に自動的な ViewModel の Dispose を期待せず、アプリの終了経路で明示する。言語切り替えごとに購読を追加しない。

通知はローカライズ専用サービスの表示プロパティに限定する。MainWindowViewModel 全体の全プロパティ通知や ItemsSource の置換で済ませない。大量の SceneTreeItem を翻訳のために作り直さず、実データの名前はそのまま表示する。言語変更でフォント一覧の再走査、SVG 再解析、Skia リソース再生成を行わない。

初期受入目標は、通常の編集画面で適用可能になってから最初の描画まで100ms程度。機種依存のため実測して調整し、タイミング依存の単体テストにはしない。遅い場合は通知回数、再レイアウト、プラグイン通知を計測する。連続切り替えは最後の要求へまとめ、プラグインの遅延をホストの翻訳処理と区別する。

### 11.7 即時切り替えの受入テスト

10節の検証に加え、以下を実装段階で追加する。

- 同じ Window / ViewModel / ドキュメントのインスタンスのまま `ja → en → ja` で固定文言と既存ステータスが更新される。
- `ja → en → system` が起動時の OS UI 言語へ戻る。同じ実効言語の選択変更では余分な表示通知を出さず、設定は保存する。
- 列挙値・線種・ツールの選択が変わらず、SelectionChanged 等による編集コマンド、キャンセル、Dirty 変更が発生しない。
- 独自ダイアログ内の入力、検証エラー、フォーカス、キャレット、IME 入力を保持する。ネイティブ画面やドラッグ中の要求は安全な区切りで最後の値だけ反映する。
- 旧言語で開始した出力が、新しい言語で進捗・完了を表示する。メッセージの引数と技術詳細は変わらない。
- 設定保存失敗、保存中の連続切り替え、幅・履歴との同時更新でも画面と最新の設定状態が整合する。
- 対応プラグインは再登録せず翻訳が更新され、非対応・例外を出すプラグインがあってもホスト更新は完了する。
- ダイアログの開閉と言語切り替えを反復しても購読数が増えず、閉じたダイアログがサービスに保持されない。
- App.Tests の純粋な状態・通知テストに加え、Avalonia の実際の Binding、DataTemplate、Popup、コード生成ダイアログの更新を UI 検証する。必要に応じて MIT の Avalonia.Headless.XUnit を追加する場合は、中央パッケージ定義のライセンスコメントと THIRD_PARTY_NOTICES.md も更新する。

この設計は公式の通知可能なサービスを基にした Vectorer 固有の設計案である。実装前の短い検証では、compiled binding の更新、ToolItem の選択保持、開いた独自ダイアログ、IME の延期を優先し、ウィンドウ再作成なしで成立することを確認する。
