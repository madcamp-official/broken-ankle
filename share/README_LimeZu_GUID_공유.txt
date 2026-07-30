LimeZu 필수 에셋 공유 묶음

파일:
  LimeZu_required_build_scenes_20260730.zip

포함 기준:
  Unity Build Scene List의 모든 활성 씬
  Assets/Resources
  Assets/Prefabs

사용 방법:
1. Unity와 Git 작업을 모두 종료합니다.
2. zip을 프로젝트 최상위 폴더에 압축 해제합니다.
3. 압축 안의 Assets 폴더를 기존 Assets 폴더와 병합합니다.
4. Unity를 열고 에셋 임포트가 끝날 때까지 기다립니다.

주의:
  에셋 파일과 .meta 파일을 반드시 함께 덮어써야 GUID가 일치합니다.
  Assets/Art/Tiles/LimeZu 아래의 기존 파일을 먼저 삭제하지 마세요.
  한글과 공백이 포함된 경로를 유지하기 위해 폴더 복사가 아닌 이 zip을 사용하세요.

검증:
  옆의 .sha256.txt 값과 zip의 SHA-256을 비교할 수 있습니다.
  zip 내부의 _share/LimeZu_dependency_manifest.txt에서 포함 파일 목록을 확인할 수 있습니다.
