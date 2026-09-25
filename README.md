# Clearwater Coast

VRChat ワールドに、歩いて入れる透明な浅瀬と海岸を作るパッケージです。水面の反射・水底の光の網（コースティクス）・波音に合わせて寄せては返す波打ち際・潜ったときの水中表現を含みます。元になった WebGL デモは [clearwater by Aureliengmz](https://github.com/Aureliengmz/clearwater)（MIT、`LICENSE-clearwater.txt`）です。

PC 向けです（GPU 計算を多用するため Quest では動きません）。VRChat Worlds SDK 3.10 以降が必要です。

## 入れ方

- このフォルダ（`com.vbamboo.clearwater`）をワールドのプロジェクトの `Packages/` に置く（埋め込みパッケージ）
- または ALCOM / VCC の「User Packages」にこのフォルダを追加し、プロジェクトに追加する

## 使い方

### 新しいシーンから始める

`Tools > Clearwater > Build Scene` を実行すると、`Assets/Clearwater/Scenes/Clearwater.unity` に水・砂浜・太陽・スポーン地点入りのシーンができます。2 回目以降は素材だけを作り直し、シーンに手で置いた物やワールド ID はそのまま残ります。シーンを初めから作り直すときは `Recreate Scene` を使います。

### 既存のワールドに足す

ワールドのシーンを開いて `Tools > Clearwater > Add to Current Scene` を実行します。水一式と、まっすぐな海岸（編集用）が追加されます。ワールドの太陽（Directional Light）はそのまま使い、影が無効なら有効にします。水は奥行き情報を使うため、影は有効のままにしてください。Reference Camera の Far Clip は数 km にしてください。

## 海岸を描く（Coast (editor only)）

シーンの「Coast (editor only)」を選ぶと、Scene ビューに波打ち際の線が出ます。

- 点をドラッグして動かす。線の途中の「+」で点を追加、点を選んで Delete で削除
- 矢印が海の側です（線の進む向きの左側）。逆なら「Reverse direction」
- 開いた線は両端の先へまっすぐ続きます。「Closed」にすると輪（島・湖）になります
- 断面：`Gentle Beach`（遠浅の浜、数値で調整）か `Curve`（波打ち際からの距離に対する地面の高さを曲線で描く）
- 変更したら **Bake**。水・海底・波・歩ける地面・波音の位置が形に合わせて更新されます

黄色の四角が焼き込む範囲（外側は線がそのまま続くとみなします）、緑の四角が歩ける地面の範囲（端に見えない壁）です。

## 物を置く（Clearwater Stamp）

海岸の上に置いた物は、`ClearwaterStamp` を付けてから Bake すると、海岸の一部として扱われます。

| モード | 使い方 | 効果 |
| --- | --- | --- |
| Obstacle | 水中に立つ岩・杭・流木など、実際に見える物に付ける | 波がその物に当たって砕け、周りに白波ができる。「Receive Caustics」で水中部分に光の網が落ちる（ClearwaterProps レイヤーに移動） |
| Raise Ground | 形を作るための見えないブラシ（任意のメッシュ） | その上面が地面になる（砂州・岩棚）。海底・歩ける地面・波がすべて追従 |
| Carve Ground | 同上 | その上面まで地面が掘られる（潮だまり・水路） |

ブラシは Bake 後に非表示になり、ワイヤーフレームで表示されます（アップロードには含まれません）。スタンプの周りには 30° ほどの斜面が自動で付きます。スタンプを動かしたら Bake し直してください。

## 調整

- 波の大きさ：水面と海底のマテリアルの `Breaker height` / `Run-up`
- 明るさ：`Sun intensity` / `Exposure`（水・海底・空・水中で同じ値に）
- 波紋・波音の音量：シーンの「Clearwater Controller」

## 負荷の目安

RTX 4070 Ti SUPER、VR（片目 2048²）で、水が画面いっぱいのとき片目 約 3ms。水中のカメラでは水面の描き方が変わり +0.7ms ほど。スタンプがあると +0.1ms 程度。

## クレジット

- 水の表現：clearwater by Aureliengmz（MIT）
- 波音：Freesound「Stromboli beach」nicola_ariutti（CC0）
