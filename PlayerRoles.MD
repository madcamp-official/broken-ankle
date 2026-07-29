# 플레이어 A / B 배정

`Multiplayer.MD` 7절(스폰)의 구현. 두 사람이 접속했을 때 누가 A이고 누가 B인지
정하고, 그 결과를 캐릭터에 반영하는 데까지다. 위치·손전등 동기화(1절)는 아직
없다.

| 슬롯 | 인물 | 담당 | 프리팹 |
|---|---|---|---|
| 0 (A) | 네이선 하먼 (Nathan Harmon) | 음향 장비, 기록 복원 | `Assets/Resources/Nathan.prefab` |
| 1 (B) | 그랜트 브리지스 (Grant Bridges) | 전력, 기계 설비 | `Assets/Resources/Grant.prefab` |

설정은 `Ashburn.MD` 4절에 있다. 두 사람은 센틸(SENTIL)의 하청업자로 애슈번에
들어온다.

---

## 0. 역할 분기는 이미 있었다

네트워크가 새로 만든 로직은 없다. 기존 두 진입점에 값을 넣어주는 것이 전부다.

| 진입점 | 하는 일 |
|---|---|
| `PlayerSpawner.PrefabFor(index)` | `index` 로 캐릭터 프리팹을 고른다 |
| `PlayerSpawner.Configure(character, index, viewer, controlled, splitKeyboard)` | `index` 로 스폰 지점과 액션맵을 고른다 |
| `PlayerRig.Apply(viewer, controlled)` | 입력·카메라·손전등·어둠·발소리 종류를 한 번에 가른다 |

그래서 문제는 **`index` 를 누가 정하고 어떻게 나눠주는가** 하나로 줄어든다.

### 캐릭터가 둘

`playerPrefabs` 는 인덱스 순서다. 0번이 `Nathan`, 1번이 `Grant`.

`Grant` 는 복사본이 아니라 **`Nathan` 의 프리팹 배리언트**다. 이동, 손전등,
소음, 무전은 두 사람이 같은 것을 쓰므로 원본을 고치면 양쪽에 따라간다.
스프라이트든 담당 장비든 달라져야 하는 것만 배리언트에서 덮어쓴다.

배리언트가 못 하는 일이 하나 있다. **원본에 있는 컴포넌트를 지우지는 못한다.**
그랜트가 음향 장비를 아예 안 든다면 끄는 것으로 될지, 프리팹을 갈라야 할지는
그때 판단한다.

### 그림

| | 헤드셋 쓴 시트 | 벗은 시트 | 애니메이터 |
|---|---|---|---|
| `Nathan` | `Art/Characters/Player1_off` | `Player1_on` | `Animations/Nathan/Nathan.controller` |
| `Grant` | `Art/Characters/Player2_on` | `Player2_off` | `Animations/Grant/Grant.controller` |

시트 배치는 전부 같다. 160×128 안에 32픽셀 20칸이고, 행이 방향
(아래·위·왼쪽·오른쪽), 각 행의 첫 칸이 정지, 나머지 넷이 걷기다. 그래서 다른
캐릭터로 갈아끼우는 일은 **인덱스 그대로의 교체**이지 클립을 다시 만드는 일이
아니다.

캐릭터마다 컨트롤러를 따로 두는 이유는 클립이 스프라이트를 프레임마다 직접
들고 있기 때문이다. 컨트롤러 하나를 공유하면서 그림만 바꿀 방법은 2D Animation
패키지의 Sprite Library 가 필요하고, 캐릭터가 둘뿐인 지금은 클립 여덟 개를
복사하는 쪽이 싸다.

`Animations/Nathan` 은 예전 `Animations/Player` 를 이름만 바꾼 것이다. 그
컨트롤러를 쓰는 것이 `Nathan.prefab` 하나뿐이라 복사하지 않았고, 그래서 쓰이지
않는 폴더가 남지 않았다.

### 헤드셋 착용

> **`_on` / `_off` 는 헤드셋 착용을 가리키지만, 두 시트의 방향이 서로 반대다.**
>
> | | 헤드셋 |
> |---|---|
> | `Player1_off` | **쓰고 있음** |
> | `Player1_on` | 벗음 |
> | `Player2_off` | 벗음 |
> | `Player2_on` | **쓰고 있음** |
>
> 이름을 믿고 같은 규칙을 적용하면 한쪽이 정확히 반대로 나온다. 네 방향 모두
> 확대해서 눈으로 확인한 결과다. 픽셀 수로는 판별되지 않는다 — `Player1` 은
> 헤드셋을 쓴 쪽이 오히려 400픽셀 적다. 머리카락을 덮기 때문이다.
>
> 언젠가 그림 파일 이름을 한쪽으로 맞추는 편이 낫다. 맞추면 `HeadsetSprites`
> 의 두 목록만 서로 바꾸면 된다.

`HeadsetSprites` 가 `HearingRingToggle.Switched` 를 듣고, 애니메이터가 방금 쓴
스프라이트를 `LateUpdate` 에서 짝으로 바꿔치기한다. 매 프레임 하는 이유는
애니메이터가 매 프레임 다시 쓰기 때문이다.

컴포넌트는 `Nathan` 에 붙어 있고 `Grant` 는 물려받아 두 목록만 자기 시트로
덮어쓴다. 배리언트를 둔 값이 여기서 나온다 — 붙이는 자리와 배선은 한 번만
정하고, 다른 것은 그림뿐이다.

클립을 한 벌 더 만들어 컨트롤러를 갈아끼우는 방법도 있었지만, 캐릭터마다 클립이
여덟 개씩 늘고 **키를 누를 때마다 걷기 사이클이 처음으로 되감긴다.** 두 시트가
같은 순서로 그려져 있는 한 바꿔치기가 싸다.

`HearingRingToggle` 은 시청자가 아닌 캐릭터에서 꺼져 있고, 꺼진 컴포넌트는
`Start` 가 돌지 않는다. `IsOn` 이 `startOn` 과 무관하게 `false` 로 남아 동료가
늘 헤드셋을 벗은 모습이 되기에, 초기화를 `Awake` 로 옮겼다.

**동료 화면에는 아직 반영되지 않는다.** 착용 상태가 네트워크를 건너지 않기
때문이고, `Multiplayer.MD` 1절에 보낼 값으로 올려두었다. 링 자체는 여전히
동기화하지 않는다 — 링은 시점이고 착용은 세계의 사실이다.

프리팹 목록이 스포너에만 있는 것도 의도한 것이다. 네트워크 쪽이 자기 목록을
따로 들면 두 기기가 서로 모르는 캐릭터를 만들 수 있다. `NetworkGame` 은
`spawner.PrefabFor(slot)` 을 물어본다.

---

## 1. 슬롯 번호

A = 0, B = 1.

| 방법 | 판정 |
|---|---|
| `ActorNumber` 를 그대로 | ✗ 재입장할 때마다 커진다. 3, 4번이 나온다 |
| `PhotonNetwork.IsMasterClient ? 0 : 1` 를 매번 계산 | ✗ 호스트가 이관되면 역할이 뒤바뀐다 |
| 입장 순간 위 값을 계산해 **커스텀 프로퍼티에 고정** | ✓ 채택 |

두 클라이언트가 각자 계산해도 같은 답이 나오므로 합의 절차가 필요 없고, 한 번
굳으면 이후 이관에 흔들리지 않는다. 로비에서 A/B 를 직접 고르게 하더라도
프로퍼티를 쓰는 자리는 그대로 두고 계산식만 갈아끼우면 된다.

`NetworkGame.OnJoinedRoom` 에 있다.

---

## 2. 스폰은 각자 자기 것만

`PhotonNetwork.Instantiate` 는 **호출한 클라이언트를 소유자로 만든다.** 호스트가
둘 다 만들면 B 캐릭터도 호스트 소유가 되어 `Multiplayer.MD` 소유권 표가 깨진다.
`TransferOwnership` 으로 넘길 수는 있으나 한 프레임 늦고 실패 경로가 하나 는다.

각 클라이언트가 자기 캐릭터만 만들고, 슬롯 번호를 인스턴스화 데이터로 같이
보낸다.

```csharp
var prefab = spawner.PrefabFor(_slot);
PhotonNetwork.Instantiate(prefab.name, spawner.PositionFor(_slot), Quaternion.identity,
                          0, new object[] { _slot });
```

PUN 은 참조가 아니라 **이름**을 받는다. 그래서 두 프리팹 모두 Resources 폴더
바로 아래에 있어야 하고, 하위 폴더에 넣으면 이름만으로는 찾지 못한다.

---

## 3. 역할 적용은 프리팹이 스스로

받는 쪽 클라이언트에도 상대 캐릭터가 생긴다. 그 사본은 스폰을 호출한 사람이
손댈 수 없는데, **동료로 보여야 하는 것이 바로 그 사본**이다. 그래서 캐릭터가
자기 자신에게 적용한다 — `NetworkPlayerBinder`.

슬롯은 커스텀 프로퍼티가 아니라 **인스턴스화 데이터에서** 읽는다. 프로퍼티는
따로 복제되어 아직 도착하지 않았을 수 있고, 그러면 도착할 때까지 캐릭터가 엉뚱한
스폰 지점에 서 있게 된다. 인스턴스화 데이터는 객체에 붙어서 같이 온다.

### viewer / controlled 조합

| 상황 | viewer | controlled |
|---|---|---|
| 네트워크 · 내 캐릭터 | ✓ | ✓ |
| 네트워크 · 동료 캐릭터 | ✗ | ✗ |
| 오프라인 분할 키보드 · 2번 | ✗ | ✓ |

네트워크에서는 둘이 항상 같다. 따로 둔 이유는 세 번째 줄 하나 때문이다.

### 오프라인 캐릭터는 건드리면 안 된다

프리팹에 `PhotonView` 가 붙은 이상, 오프라인 테스트가 평범한 `Instantiate` 로
만든 캐릭터에도 `NetworkPlayerBinder` 가 올라탄다. 그대로 두면 스포너가 이미
설정을 끝낸 캐릭터를 **한 번 더, 그것도 주인 없는 A 로** 덮어써서 두 캐릭터
모두 입력도 카메라도 없이 서 있게 된다. 실제로 그렇게 됐다.

`photonView.InstantiationId` 는 PUN 이 넣고 Unity 는 넣지 않는 값이라, 둘을
가르는 정직한 기준이 된다. 0 이면 비활성화하고 물러난다.

---

## 4. 함정: 액션맵

`PlayerSpawner` 의 `actionMaps = { "Player", "Player2" }` 를 네트워크에서도
`index` 로 쓰면 **B 플레이어가 자기 키보드에서 분할용 반쪽 키맵을 쓰게 된다.**
`Player2` 는 한 키보드를 나눠 쓰는 로컬 테스트 전용이다.

그래서 `Configure` 에 `splitKeyboard` 를 받는다. 오프라인은 `true` 로 `index` 를
따르고, 네트워크는 `false` 로 언제나 첫 액션맵을 쓴다. 슬롯이 가르는 것은
**스폰 지점과 역할**이지 키 배치가 아니다.

---

## 5. 배선

전부 되어 있다.

| 대상 | 상태 |
|---|---|
| `Assets/Resources/Nathan.prefab` | 옛 `Assets/Prefabs/Player.prefab`. 옮기고 이름을 바꿨다. GUID 가 유지되어 참조는 그대로다 |
| `Assets/Resources/Grant.prefab` | `Nathan` 의 배리언트. 그림과 애니메이터만 다르다 |
| 프리팹의 `PhotonView`, `NetworkPlayerBinder`, `HeadsetSprites` | `Nathan` 에 추가. `Grant` 가 물려받는다 |
| `PlayerSpawner.playerPrefabs` | `[Nathan, Grant]` |
| `Systems.unity` 의 `NetworkGame` | 추가. **기본값은 비활성** |
| `Offline Players` | 2 그대로 |

### "Player" 라는 이름 중 바뀌지 않은 것

인물 이름으로 바꾼 것은 **프리팹과, 스폰된 오브젝트의 이름**뿐이다. 나머지
`"Player"` 는 인물이 아니라 다른 것을 가리키므로 그대로 둔다.

| 남은 `"Player"` | 무엇인가 |
|---|---|
| `PlayerRig` 가 붙이는 태그 | "이 화면의 주인" 표시. 카메라와 청각 링이 이걸로 찾는다 |
| 액션맵 `"Player"`, `"Player2"` | 키 배치. 4절대로 인물이 아니라 키보드를 나누는 이름이다 |
| `PlayerController`, `PlayerRig`, `PlayerSpawner` | 인물이 아니라 역할을 다루는 코드다 |

이름으로 캐릭터를 찾는 코드는 없다. 오브젝트 이름을 바꿔도 안전한 이유다.

### 왜 스위치가 하나인가

`NetworkGame.Awake` 가 `PlayerSpawner.HandOverToNetwork()` 를 불러 오프라인
채우기를 끈다. Awake 는 어떤 Start 보다도 먼저 도므로 순서는 보장된다.

`Offline Players` 를 0 으로 두는 방법도 있었지만, 그러면 손으로 맞춰야 할 값이
둘이 되고 어긋나면 캐릭터가 네 개 생긴다. **체크박스 하나만 켜면 멀티, 끄면
분할 키보드 테스트**다.

비활성으로 둔 이유는 `PhotonAppIds` 가 기기마다 따로 만들어야 하는
gitignore 대상이기 때문이다. 켜진 채로 커밋하면 App ID 를 아직 안 넣은 사람이
받았을 때, 잘 돌던 오프라인 테스트 대신 빈 맵과 경고를 보게 된다.

---

## 6. 확인한 것

플레이 모드에서 오프라인 경로가 그대로인지 봤다.

```
zone=House
prefab[0]=Player  prefab[1]=Player2
claim House=2
Player   viewer=True  ctrl=True  tag=Player    pos=(-18, -5, 0)
Player 2 viewer=False ctrl=True  tag=Untagged  pos=(-15, -5, 0)
```

맵 홀드 수가 캐릭터 수와 같다. `MapTravel` 이 이동하는 사람마다 하나씩
놓으므로, 둘 다 나가야 집이 내려간다.

**둘이 실제로 다른 프리팹에서 나왔는지는 눈으로 확인하지 못했다.** 지금은
배리언트가 원본과 내용이 같아 구별할 것이 없다. 확인한 것은 `PrefabFor` 가
슬롯마다 다른 프리팹을 돌려준다는 것까지다.

**네트워크 경로는 아직 실행해 보지 않았다.** App ID 가 들어간 기기 두 대가
필요하다. 다음 순서다.

```
1. 두 기기에 PhotonAppIds 를 만들고 팀 App ID 를 넣는다   (Multiplayer.MD 9절)
2. 양쪽 Systems 씬에서 NetworkGame 을 켠다
3. 접속해서 A 와 B 가 각자 다른 스폰 지점에 서는지 본다
```

3번에서 **두 캐릭터는 서로 보이지 않는다.** 위치 동기화가 없기 때문이고,
정상이다. `Multiplayer.MD` 1절이 다음 단계다.

---

## 미정

- **맵 슬롯이 클라이언트마다 다르다.** `MapZone` 은 로드 순서대로 슬롯을 잡고
  월드 좌표는 슬롯에서 나온다. 시작 맵은 양쪽 다 첫 번째라 일치하지만, 그
  뒤로는 둘이 다른 순서로 맵을 열면 같은 맵이 서로 다른 좌표에 선다. 위치를
  월드 좌표로 보내는 1절을 붙이기 전에 정해야 한다 — 맵 기준 상대 좌표로
  보내든가, 슬롯을 호스트가 정해서 내리든가
- A 와 B 가 능력까지 다른가. 지금 슬롯이 가르는 것은 스폰 지점뿐이다
- 로비에서 A/B 를 직접 고르게 할지, 접속 순서로 둘지. 방 이름도 지금은 고정
  문자열이라 방 코드가 필요하면 여기에 붙는다
- 재접속했을 때 원래 슬롯을 돌려주는 방법
