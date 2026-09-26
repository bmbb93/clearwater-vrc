# Clearwater

VRChat ワールド向けの、浅い海と波打ち際の表現。海岸の形を描いて焼き込み、その上に水面・水底・打ち寄せる波・水中の表現を重ねる。

## 地面

**Bed（底）**:
水の下から浜までひと続きの地面の表面。見た目は一つの Bed look で決まる。
_Avoid_: 海底、floor（水の下だけを指すときは Seabed）

**Seabed（水底）**:
Bed のうち水の下の部分。
_Avoid_: 海底、底面

**Beach（浜）**:
Bed のうち水より上で、波や濡れが届く部分。
_Avoid_: 砂浜（砂とは限らない）、陸

**Bed look（底の見た目）**:
Generated terrain の Bed の色と凹凸を決める設定一式（アセット）。色と高さのテクスチャ、その大きさ、Bed effects からなる。小石（Pebbles）と砂（Sand）もプリセットの Bed look。水の下も浜も同じ Bed look を使う。
_Avoid_: 底質、マテリアル、テクスチャ（テクスチャは Bed look の材料の一つ）

**Bed effects（重ねる効果）**:
Bed look の上に重ねる表現。隙間の砂（Sand fill）、波紋（Ripple marks）、藻の色（Weed tint）、色味（Grade）。それぞれオン／オフと強さを持つ。
_Avoid_: オーバーレイ、フィルター

**Seam（境目）**:
Walkable area の端で User terrain と Generated terrain が接する帯。Generated terrain の高さをメッシュの縁に合わせてなじませる。
_Avoid_: 継ぎ目、ブレンド領域

**Caustics（光の模様）**:
水面で屈折した日光が Bed に落とす、ゆらめく明るい模様。User terrain には上から投影して重ねる。
_Avoid_: 集光模様

**Walkable area（歩ける範囲）**:
Coast オブジェクトのまわりの、歩ける地面と見えない壁を用意する四角い範囲。
_Avoid_: 地面の範囲、ground area

**Terrain source（地形の出どころ）**:
Bed の形をどこから取るか。Generated terrain か User terrain のどちらか。
_Avoid_: 地形モード

**Generated terrain（生成地形）**:
Coast の線と Cross-section から作る地形。
_Avoid_: 計算地形、デフォルト地形

**User terrain（ユーザー地形）**:
Walkable area のまわりで、ユーザーが用意したメッシュを Bed の形と見た目として使う地形。見た目はメッシュ自身のマテリアルのまま水越しに見え、Waterline はメッシュが水の高さと交わる線から求まる。その外側は Generated terrain につながる。
_Avoid_: カスタム地形、自作メッシュ

## 海岸と波

**Coast（海岸）**:
Waterline を描いた線と、そこから沖への Cross-section の組。焼き込んで水・地面・波の元になる。

**Waterline（波打ち際）**:
静かなときの水面と Bed が交わる線。
_Avoid_: 海岸線（Coast と紛らわしい）、汀線

**Cross-section（断面）**:
Waterline からの距離に対する Bed の高さ。

**Swell（うねり）**:
沖から一方向に寄せる波。向きと広がりを持つ。

**Shore exposure（波の当たりやすさ）**:
浜の各所に届く Swell の強さ。開けた浜を 1 とする波の高さの倍率。

**Shore delay（波の遅れ）**:
Swell が浜の各所に届くまでの時間差。

**Swash（打ち上げ）**:
砕けた波が Beach を駆け上がり、引いていく水の動き。
_Avoid_: 遡上、run-up（run-up は Swash が届く高さ）
