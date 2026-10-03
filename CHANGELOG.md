**日本語** | [English](CHANGELOG.en.md)

# 変更履歴

Clearwater VRC（`com.vbamboo.clearwater`）の版ごとの変更です。書き方は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、版の番号は[セマンティック バージョニング](https://semver.org/lang/ja/)に従います。各版の節は、英語の `CHANGELOG.en.md` の節と合わせて、GitHub のリリースノートになります。

## [1.3.0] - Unreleased

### 変更

- 雲を作り直しました。積雲を立体として、日の光と空の光で照らして描きます。底は平らで頂はもこもこと丸く、日の当たる側は白く、陰と底は淡い灰色で、縁はほつれて青が透けます。雲はシーンごとの雲のドーム（2048 × 1024 の CustomRenderTexture）に少しずつ描き直し（16 本の帯を順に。約 0.2 秒で空を一周）、空・水面の映り込み・水中から見上げた空は、それを 1 回読むだけです。描き直しの合間も、雲は風に合わせてなめらかに流れます。負荷は RTX 4070 Ti SUPER で 1 フレーム約 0.4 ms で、画面の大きさや VR の両目によらず 1 回です。雲の量を減らすと、まばらな所の雲から消え、残る雲も小さくなります。ドームは Build Scene、Coast の Bake、シーンを開いたときに、`Generated` のシーンのフォルダーに作られます
- 遠くの雲が地平線まで同じ模様で繰り返して見えなくなりました（雲の地図を 2 通りに読み分け、雲量の場も 2 つの縮尺で読みます）。夕暮れに雲の照らされた面に出ていた細かいざらつきと、頂の灰色の汚れのような陰もなくしました。夕暮れの雲は、照らされた下側を抑え、陰の頂を空の光で明るくして、明暗の差を和らげました。日が沈んだ後に雲の平らな底だけが光り、円盤のように見えていたのもなくしました

### 追加

- 雲の影：雲が海と浜に柔らかい影を落とし、雲と一緒に流れます（夜は月の光を遮ります）
- 小石の `Hand Reach`：VR で左手に呼び出したパネルを、手の 25 cm 先（目から離れる向き）に出します。手のすぐ上では近すぎて操作しにくかったためです。そのぶん `Hand Size` の初期値を 0.4 から 0.55 にしました（シーンに置いた小石の値は変わりません）
- 左手に呼び出したパネルは、出した位置にとどまります（手に追従していると、右手で触ろうとしてもパネルが逃げました）。目から `Hand Close Distance`（初期値 1.5 m）離れると隠れます
- Clearwater Controller の `Buildings` と `Indoor Level`：建物の箱（Sky Light Volumes と同じ）を入れると、中では波音を遮って小さくし（初期値 0.3 倍）、床下を通る波打ち際からは鳴らしません。浜の上に建つ家の中で、床下の波打ち際から波音が大きく鳴っていました
- 空のマテリアルの `As in 1.2`：雲を 1.2 までの雲（空が画素ごとに描く平らな雲。影なし）に戻せます。1.2 で作ったワールドの見た目を保つためで、初期値はオフ（新しい雲）です。オンのときは雲のドームを描かないので、負荷も 1.2 と同じです

## [1.2.0] - 2026-10-03

### 追加

- 建物の中の空の光：空だけで焼いた Additive の Light Volume（VRC Light Volumes）を Clearwater Sky の `Sky Light Volumes` に入れると、その色と強さが時刻の空に合わせて変わります。窓から入る空の光が、昼は白く、夕方は橙に、夜は月明かりになります。ライトマップは照明など時刻によらない光だけで焼けば、時刻を変えても室内が昼のまま明るく残りません。Light Volume ごとの倍率（`Sky Light Gains`。室内の目の慣れの分）、地平線から来る光の割合（`Sky Light Horizon`）、日差しの照り返し（`Sun Bounce`）も決められます。空だけで焼いたリフレクションプローブの強さも合わせられ（`Sky Reflection Probes`）、照明だけで焼いた Light Volume を夜に強めることもできます（`Lamp Light Volumes`。夜の室内で目が灯りに慣れる分）（[README](https://github.com/bmbb93/clearwater-vrc/blob/main/README.md) の「建物の中の空の光」）
- `Sky Light Volumes` の箱は、浜と水にとっての建物になります。箱の中の浜と水には Additive の Light Volume を足さず（空の光が二重になり、箱の形に明るくなっていました）、箱の中の Point Light Volume は浜と水を照らしません（室内の照明が壁を抜けて、外の砂に漏れていました）
- [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes) 3.0 に対応しました（3.0.0-dev.20 で確認）。3.0 を入れたプロジェクトでもコンパイルが通り、Build Demo Scenes が Demo_LightVolumes を 3.0 の作り方（点光源の設定を Point Light Volume Instance に持たせ、Light Volume Manager に登録する）で作ります。浜・水底・泡は、3.0 でも 2.x と同じように Light Volumes の光を受けます。2.x のプロジェクトと、Light Volumes のないプロジェクトの動作は変わりません
- 遠景の陸が海岸に沿うようになりました。海岸があれば、陸は 2 km 先まで地面が続く方角にだけ立ち、海岸線が歩ける浜から離れていく所では、海岸と一緒に海面まで下がります（海岸が斜めに延びていると、山が海の上に浮いて見えていました）。Coast を Bake し直すと効きます。空のマテリアルの `Land setback` で山を海岸線から内陸へ下げ、`Land height` で高さを変えられます（0 で山なし）
- Clearwater Sky の `darkness`：夜の深さ（昼と夕暮れの前半は 0、真夜中は 1）。ワールドの U# から読んで、夜に明るくする照明などに使えます
- Clearwater Controller の `Sound Volume`：波音 3 つ（波打ち際・遠くの海・水中）にまとめて掛ける倍率（0〜1、初期値 1）。ワールドの U# から変えて、動画の再生中に波音を下げたり、スイッチで消したりできます

### 変更

- 打ち上げの洗う浜（水深 0.3 m より浅い所）と陸の起伏を 3 分の 1 にしました。緩い浜では、打ち上げの先端が起伏の盛り上がりの手前で引き返して乾いた島が残り、くぼみに水たまりができていました。Coast を Bake し直すと、当たり判定も合います

## [1.1.0] - 2026-09-30

### 追加

- [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes)（RED_SIM）に対応しました。Light Volumes を置いたワールドでは、Additive の Light Volume と Point Light Volume の光が、アバターと同じように浜・水の中の底・泡にも当たり、水面にはその照り返しが映ります。夜の浜に置いたランプや焚き火が、砂と浅瀬を照らします。Additive でない Light Volume は、焼いたときの時刻の明るさが入っているので読みません（[README](https://github.com/bmbb93/clearwater-vrc/blob/main/README.md#vrc-light-volumes) の「VRC Light Volumes」）
- Light Volumes のパッケージは必須ではありません。シェーダーが読む部分（`LightVolumes.cginc` 2.1.3、MIT）を同梱しています。Light Volume のないワールドでは、見た目も負荷も 1.0.0 と変わりません
- VRC Light Volumes がプロジェクトに入っていれば、Build Demo Scenes が 7 つ目のデモ「Demo_LightVolumes」も作ります。夜の浜を、砂の上・浅瀬の上・水の中に置いた Point Light Volume と、水際を回りながら色と明るさを変えるランプで照らしたものです（`Open Light Volumes (VRC Light Volumes)`）

## [1.0.0] - 2026-09-29

最初の公開版です。

- 歩いて入れる透明な浅瀬：水面の反射、水底の光の模様（コースティクス）、水の吸収と散乱、スネルの窓のある水中の見え方
- 線で描いて焼き込む海岸：岸の形・断面・歩ける地面。自分のメッシュや Unity の Terrain も地面にでき、その先は生成した浜が続きます
- 波音に合わせて砕け、泡を連れて浜を駆け上がる波
- 海とは別の高さに置けるプールと、地面を盛る・削る・波が砕ける障害物にするスタンプ
- 時刻で変わる空：大気が日光を散らす様子から計算した色、夜の月と星、インスタンス内の同期、ワールドの中で時刻を変えるパネル
- トーンマッピングは、ポストプロセス（PPSv2）とシェーダー内から選べます

[1.2.0]: https://github.com/bmbb93/clearwater-vrc/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/bmbb93/clearwater-vrc/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/bmbb93/clearwater-vrc/releases/tag/v1.0.0
