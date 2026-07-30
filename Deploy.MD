# 배포

Windows 빌드를 만들어 팀 밖의 사람에게 건네는 절차. 규모는 수십 명까지를 전제한다.

## 1. 지금 어디까지 되어 있나

- [x] 씬 목록 확인 — `Systems` 가 0번, 맵 14개 전부 등록됨
- [x] `gameVersion` 을 `3` 으로 올림 (코드 기본값과 `Systems.unity` 양쪽)
- [x] `Assets/Editor/BuildWindows.cs` — 메뉴와 배치모드 양쪽에서 도는 빌드
- [x] `Build/` 와 `*.zip` 을 gitignore 에 추가
- [x] 빌드 실행 — `ea9c748` 기준, 414.8 MB, 214초, 오류 0
- [x] 압축 — `Ashburn-v3-win64.zip`, 57.6 MB, 239개 항목 전부 정상 해제 확인
- [ ] GitHub Releases 에 올리기 — 이 기계에 `gh` 가 없어 아직 못 올렸다

## 2. 빌드하기

### 에디터에서

`Ashburn > Build Windows Player`. 끝나면 결과를 띄우고 폴더를 연다.

### 명령줄에서

에디터가 열려 있으면 안 된다. 프로젝트 잠금 파일 하나를 두고 다투다 실패한다.

```
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" ^
  -quit -batchmode -nographics ^
  -projectPath "C:\Users\lucky\Unity_Projects\broken-ankle" ^
  -logFile - ^
  -executeMethod Ashburn.EditorTools.BuildWindows.Run
```

성공하면 종료 코드 0, 실패하면 1이다. 결과물은 `Build/Windows/Ashburn.exe`.

### 스크립팅 백엔드

**Mono** 로 되어 있다. IL2CPP 모듈이 이 기계에 깔려 있지 않다.

IL2CPP 가 낫다 — 시작이 빠르고 코드가 그대로 노출되지 않는다. 쓰려면 Unity Hub
에서 `Windows Build Support (IL2CPP)` 모듈을 설치하고 `BuildWindows.cs` 의
`Mono2x` 를 `IL2CPP` 로 바꾸면 된다. 빌드 시간은 몇 배로 늘어난다.

Mono 로 나간 빌드는 어셈블리가 평범한 .NET DLL 이라 뜯어보면 App ID 가 나온다.
IL2CPP 라도 문자열은 남으니 근본적인 차이는 아니다. 아래 App ID 항을 보라.

## 3. 압축과 배포

`Build/Windows` 폴더를 **통째로** 압축한다. `Ashburn.exe` 만 꺼내면 실행조차 되지
않는다. `Ashburn_Data` 와 `UnityPlayer.dll` 이 옆에 있어야 한다.

`*_BurstDebugInformation_DoNotShip` 은 뺀다. 이름 그대로 배포용이 아니다.

### `Compress-Archive` 를 쓰지 말 것

Windows PowerShell 5.1 의 `Compress-Archive` 도, .NET Framework 의
`ZipFile.CreateFromDirectory` 도 zip 안의 경로 구분자를 **역슬래시로 쓴다.** zip 규격은
슬래시이고, 이것을 지키지 않은 압축 파일은 Windows 기본 압축 해제에서만 폴더로 풀린다.
7-Zip, macOS, Linux 에서는 `Ashburn_Data\level0` 이 폴더가 아니라 **그 이름의 파일 하나**가
되어, 받은 사람이 `Ashburn.exe` 옆에 `Ashburn_Data` 폴더가 없는 상태를 보게 된다.

항목 이름을 직접 넘겨야 한다.

```powershell
$src = "Build\Windows"; $zip = "Ashburn-v3-win64.zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sep = [System.IO.Path]::DirectorySeparatorChar
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::CreateNew)
$ar = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
$prefix = (Resolve-Path $src).Path.TrimEnd($sep) + $sep
foreach ($f in Get-ChildItem $src -Recurse -File) {
  $rel = $f.FullName.Substring($prefix.Length).Replace($sep, [char]0x2F)
  [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($ar, $f.FullName, $rel, 'Optimal') | Out-Null
}
$ar.Dispose(); $fs.Dispose()
```

확인은 역슬래시가 하나도 없는지 세는 것으로 충분하다.

```powershell
$a = [System.IO.Compression.ZipFile]::OpenRead("Ashburn-v3-win64.zip")
@($a.Entries | Where-Object { $_.FullName.Contains([char]0x5C) }).Count   # 0 이어야 한다
```

GitHub Releases 로 올린다. 저장소가 공개라 그대로 쓸 수 있고, 무엇보다 어느
버전을 누가 받았는지가 남는다.

```
gh release create v3 Ashburn-v3-win64.zip -t "Ashburn v3" -n "무엇이 바뀌었는지"
```

## 4. 받은 사람의 최신화

**압축을 새 폴더에 풀고 예전 폴더를 지운다.** 덮어쓰지 않는다 — `_Data` 안에 남은
옛 파일이 섞이면 진단하기 어려운 고장이 난다.

강제하는 장치는 이미 있다. `gameVersion` 이 다르면 Photon 이 매치메이킹을 가르므로
옛 빌드를 쓰는 사람은 새 빌드를 쓰는 사람과 만날 수 없다.

**이때 아무것도 실패하지 않는다.** 두 사람이 같은 방 번호를 넣으면 각자 자기 버전의
세계에서 그 이름의 방을 얻고, 둘 다 성공한 채로 영원히 혼자 기다린다. 그래서 첫
화면 구석에 버전을 항상 적어 두고, 혼자 25초 넘게 기다리면 이유를 알려 준다.

**그러므로 빌드마다 `gameVersion` 을 올려야 한다.** 두 군데다:

- `Assets/Scripts/Net/NetworkGame.cs` 의 기본값
- `Assets/Scenes/Systems.unity` 의 `gameVersion`

씬에 직렬화된 값이 코드 기본값을 이긴다. 코드만 고치면 아무 일도 일어나지 않는다.

## 5. App ID

`Assets/Resources/PhotonAppIds.asset` 은 git 에 올라가지 않지만 **빌드에는 들어간다.**
빌드를 뜯으면 꺼낼 수 있다.

App ID 는 비밀번호가 아니고 대시보드 접근 권한을 주지 않는다. 다만 접속에 쓰는
열쇠이므로, 주운 사람이 무료 요금제의 동시접속 20명을 대신 쓸 수 있다. 수십 명
범위의 팀 배포에서는 감수할 만하다. 공개 링크로 아무나 받게 할 생각이면 Photon
커스텀 인증이 필요하고, 그것은 하루에 끝나는 일이 아니다.

## 6. 빌드 전에 확인할 것: 타일 GUID 충돌

`Assets/Art/Tiles/LimeZu/` 는 gitignore 대상이라 저장소에 없다. 병원 서가가 쓰는 타일
여섯 개만 `Assets/RequiredTiles/HospitalShelves/` 에 복사해 커밋해 두었는데, `.meta` 까지
같이 복사돼 **원본과 GUID 가 같다.**

둘 다 있는 기계(= LimeZu 를 받아 둔 팀원의 기계)에서는 Unity 가 충돌을 감지하고 복사본에
새 GUID 를 발급한다. 임포트할 때마다 다시 발급하므로 `git status` 에 계속 뜬다.

**그 변경을 커밋하면 안 된다.** 커밋되는 순간, LimeZu 를 갖고 있지 않은 사람 쪽에서
`Hospital.unity` 가 가리키던 GUID 가 사라져 서가 타일이 통째로 빠진다.

```
git checkout -- Assets/RequiredTiles/
```

빌드 자체는 어느 쪽이든 같은 그림이 들어가므로 영향이 없다. 근본적으로는 복사본을 지우고
필요한 타일 여섯 개만 gitignore 예외로 빼는 편이 낫다.

## 7. 다른 플랫폼

패키지를 더 받을 일은 없다. Unity Hub 에서 모듈을 체크하면 된다. PUN2 와 Voice 2 는
iOS/Android/macOS 를 모두 지원한다.

| 대상 | 드는 일 |
|---|---|
| macOS | 모듈 설치 후 빌드. **맥이 있어야 한다.** 공증하지 않으면 받는 사람이 우클릭 > 열기로 경고를 넘겨야 한다 |
| Linux | 모듈 설치 후 빌드. 거의 그대로 된다 |
| iOS / Android | 입력을 새로 설계해야 한다. 조작이 전부 키보드고 화면 UI 는 마우스 전제다. iOS 는 맥과 Xcode 도 필요하고, `Info.plist` 에 마이크 권한 설명이 없으면 무전기를 켜는 순간 앱이 죽는다 |

키보드 게임이라 macOS 와 Linux 는 그대로 동작한다. 모바일은 포팅이 아니라 별개의
작업이다.
