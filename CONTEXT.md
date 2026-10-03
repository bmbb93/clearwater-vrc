# Clearwater

VRChat ワールド向けの、浅い海と波打ち際の表現。海岸の形を描いてベイクし、その上に水面・水底・打ち寄せる波・水中の表現を重ねる。

## 水

**Water body（水）**:
ひと続きの水面を持つ水のまとまり。Sea か Pool のどちらか。見ている人がいる Water body だけが波紋を持ち、水中の霧はカメラごとにそのカメラがいる Water body のものがかかる。
_Avoid_: 水面（水面は Water body の表面）、水域

**Sea（海）**:
ワールドにひとつの、Coast を持つ Water body。水平線まで広がり、打ち寄せる波と波音がある。
_Avoid_: メインの水、海面

**Pool（プール）**:
Sea とは別の高さに置ける、ユーザーが作った水槽（Basin）の中の Water body。打ち寄せる波はなく、揺れは Sea の波を弱めたもの。屋内（Indoor）にすると太陽の光がなく、空の代わりに部屋が映る。Sea は Pool の範囲には入り込まない。
_Avoid_: 水槽（水槽は Basin）、サブの水

**Basin（水槽）**:
Pool の壁と底になるユーザーのメッシュ。上から見て一番高い面が底としてベイクされる。
_Avoid_: プール本体、容器

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
Generated terrain の Bed の色と凹凸とつやを決める設定一式（アセット）。色と高さのテクスチャ、その大きさ、Bed effects、つや（Smoothness）からなる。User terrain から作って、その外側を同じ見た目にもできる。小石（Pebbles）と砂（Sand）もプリセットの Bed look。水の下も浜も同じ Bed look を使う。
_Avoid_: 底質、マテリアル、テクスチャ（テクスチャは Bed look の材料の一つ）

**Bed effects（重ねる効果）**:
Bed look の上に重ねる表現。隙間の砂（Sand fill）、波紋（Ripple marks）、藻の色（Weed tint）、色味（Grade）。それぞれオン／オフと強さを持つ。
_Avoid_: オーバーレイ、フィルター

**Seam（境目）**:
User terrain の縁のすぐ外側で、User terrain と Generated terrain が接する帯。Generated terrain の高さを User terrain の縁に合わせてなじませる。
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
Walkable area のまわりで、ユーザーが用意したメッシュや Unity の Terrain を Bed の形と見た目として使う地形。見た目は自身のマテリアルのまま水越しに見え、Waterline はそれが水の高さと交わる線から求まる。その外側は Seam を挟んで Generated terrain につながる。
_Avoid_: カスタム地形、自作メッシュ

## 海岸と波

**Coast（海岸）**:
Waterline を描いた線と、そこから沖への Cross-section の組。ベイクして水・地面・波の元になる。

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

## 空

**Sky of the day（時刻の空）**:
時刻・緯度・季節から太陽と月の位置が決まり、大気の散乱で色が付く空。時刻を止めておくことも、Cycle で進めることもできる。ワールドの太陽をそのまま使うシーンにはなく、そこでは Fixed sky になる。
_Avoid_: 昼夜サイクル（Cycle はその進め方の一つ）、動的な空

**Fixed sky（時刻のない空）**:
太陽が 31° の高さにある午後の空。Sky of the day がないシーンの空で、Sky of the day はその高さでこの空と同じ光になるように合わせてある。
_Avoid_: 既定の空、古い空

**Cycle（時間の進行）**:
Sky of the day の時刻が進むこと。インスタンスが開いたときに設定の時刻から始まり、インスタンスの全員に同じ時刻が見える。
_Avoid_: ループ、アニメーション

**Adaptation（目の順応）**:
景色が暗くなるほど、見える明るさを持ち上げること。月夜が昼の数 % の明るさに、色が褪せて青みがかって見える。
_Avoid_: 自動露出、アイアダプテーション

**Cloud dome（雲のドーム）**:
シーンごとに雲を描いておく 1 枚の画像（CustomRenderTexture）。全天の雲を方向ごとに持ち、毎フレーム一部ずつ描き直す。空・水面・水中から見上げた空は、雲をこれから読む。
_Avoid_: パノラマ、雲のマップ（雲の配置の地図と紛れる）

**Panel settings（パネルの設定）**:
空と波のパネルで変えられる値（時刻・Day goes by・1 日の長さ・雲・波）の初期値と、実行中に変えた値の同期。時刻の 3 つは Sky of the day が持ち、雲と波は Sky & Waves Settings が持つ。Reset all はこの初期値に戻す。
_Avoid_: 既定値（パッケージの既定値と紛れる）、メニュー設定
