# Terry CityHall / Hanger Update

이 폴더는 `terry` 브랜치의 CityHall, Hanger, Village Map 변경사항을 수동으로
공유해야 할 때 사용하는 델타 패키지입니다.

## 권장 확인 방법

저장소를 사용하는 팀원은 파일을 직접 복사하는 것보다 Git으로 받는 것이 안전합니다.

```powershell
git fetch origin
git switch terry
git pull --ff-only origin terry
```

Unity 6.3 LTS (`6000.3.20f1`)에서 프로젝트를 열고, Unity의 임포트와 컴파일이
끝난 뒤 `Assets/Scenes/Village Map.unity`를 실행하면 됩니다.

## 수동 복사 방법

Git을 사용할 수 없는 경우 Unity를 닫고 `project-files` 안의 `Assets`와
`ProjectSettings`를 프로젝트 루트에 그대로 덮어씁니다. `.meta` 파일을 반드시
함께 복사해야 씬 참조와 GUID가 유지됩니다.

이 패키지는 `terry` 브랜치의 기존 프로젝트를 기준으로 한 변경 파일 모음이며,
프로젝트 전체 백업은 아닙니다.

## 포함된 동작

- Village Map, CityHall 1F/2F, Hanger 씬과 씬 이동 설정
- CityHall 1F/2F 및 Hanger 레이아웃 루트 비활성화
- Hanger의 Wall, Objects, Objects2 충돌
- CityHall 2F의 Wall, Wall2 충돌
- CityHall 층간 이동과 Village Map 출입 위치
- Hanger 출구의 초기 잠금 및 `SceneTransition.Unlock()` 해제 인터페이스
- 필요한 에디터 설정, 스크립트, Build Settings 및 Unity `.meta` 파일

`MoveTo3F`와 `MoveToBasement`는 이후 연결을 위해 현재 미설정 상태로 유지됩니다.
