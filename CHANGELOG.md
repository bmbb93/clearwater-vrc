**日本語** | [English](CHANGELOG.en.md)

# 変更履歴

Clearwater VRC（`com.vbamboo.clearwater`）の版ごとの変更です。書き方は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、版の番号は[セマンティック バージョニング](https://semver.org/lang/ja/)に従います。各版の節は、英語の `CHANGELOG.en.md` の節と合わせて、GitHub のリリースノートになります。

## [1.1.0] - Unreleased

### 追加

- [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes)（RED_SIM）に対応しました。Light Volumes を置いたワールドでは、Additive の Light Volume と Point Light Volume の光が、アバターと同じように浜・水の中の底・泡にも当たり、水面にはその照り返しが映ります。夜の浜に置いたランプや焚き火が、砂と浅瀬を照らします。Additive でない Light Volume は、焼いたときの時刻の明るさが入っているので読みません（[README](https://github.com/bmbb93/clearwater-vrc/blob/main/README.md#vrc-light-volumes) の「VRC Light Volumes」）
- Light Volumes のパッケージは必須ではありません。シェーダーが読む部分（`LightVolumes.cginc` 2.1.3、MIT）を同梱しています。Light Volume のないワールドでは、見た目も負荷も 1.0.0 と変わりません

## [1.0.0] - 2026-09-29

最初の公開版です。

- 歩いて入れる透明な浅瀬：水面の反射、水底の光の模様（コースティクス）、水の吸収と散乱、スネルの窓のある水中の見え方
- 線で描いて焼き込む海岸：岸の形・断面・歩ける地面。自分のメッシュや Unity の Terrain も地面にでき、その先は生成した浜が続きます
- 波音に合わせて砕け、泡を連れて浜を駆け上がる波
- 海とは別の高さに置けるプールと、地面を盛る・削る・波が砕ける障害物にするスタンプ
- 時刻で変わる空：大気が日光を散らす様子から計算した色、夜の月と星、インスタンス内の同期、ワールドの中で時刻を変えるパネル
- トーンマッピングは、ポストプロセス（PPSv2）とシェーダー内から選べます

[1.1.0]: https://github.com/bmbb93/clearwater-vrc/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/bmbb93/clearwater-vrc/releases/tag/v1.0.0
